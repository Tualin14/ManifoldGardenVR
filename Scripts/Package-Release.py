"""Create only the installable runtime ZIP; GitHub supplies source archives from the tag."""
import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = 'https://github.com/Tualin14/ManifoldGardenVR'
LOADER_SHA256 = '5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416'


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)


def native_x64(data, name):
    if data[:2] != b'MZ':
        raise ValueError('Not a PE file: ' + name)
    offset = struct.unpack_from('<I', data, 0x3c)[0]
    if data[offset:offset + 4] != b'PE\0\0' or struct.unpack_from('<H', data, offset + 4)[0] != 0x8664:
        raise ValueError('Not a Windows x64 native binary: ' + name)


def main():
    version = ET.parse(ROOT / 'Mod/MGVR.csproj').findtext('./PropertyGroup/Version')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', default='v' + version)
    parser.add_argument('--loader-archive', type=Path, default=ROOT / 'Artifacts/Downloads/MelonLoader-x64-0.7.3.zip')
    parser.add_argument('--xr-source', type=Path, default=ROOT / 'Artifacts/Dependencies/OpenXR/1.3.1')
    parser.add_argument('--output', type=Path, default=ROOT / 'Artifacts/Releases' / version)
    args = parser.parse_args()
    if args.tag != 'v' + version:
        raise ValueError('The tag must match the project version.')
    commit = git('rev-parse', '--verify', args.tag + '^{commit}').decode().strip()
    if commit != git('rev-parse', 'HEAD').decode().strip():
        raise ValueError('Check out the release tag before packaging.')
    source_paths = ['Mod', 'Native', 'ThirdParty', 'Scripts/Build.ps1', 'Scripts/Common.ps1',
                    'Scripts/Verify-Release.ps1', 'Scripts/Package-Release.py', 'Scripts/build-menu-font.py',
                    'Scripts/requirements-menu-font.txt', 'Directory.Build.props', 'Build.Local.props.example',
                    'ManifoldGardenVR.sln', 'README.md', 'CHANGELOG.md', 'LICENSE']
    if git('diff', '--name-only', args.tag, '--', *source_paths).strip():
        raise ValueError('Release sources differ from the tag.')
    tracked = set(git('ls-tree', '-r', '--name-only', commit).decode().splitlines())
    project = ET.parse(ROOT / 'Mod/MGVR.csproj')
    required = [p.relative_to(ROOT).as_posix() for p in (ROOT / 'Mod').glob('*.cs')]
    required += ['Mod/' + item.attrib['Include'] for item in project.findall('./ItemGroup/EmbeddedResource')]
    required += ['Mod/MGVR.csproj', 'Native/Bridge.cpp', 'Native/MGVR.Native.vcxproj',
                 'Scripts/Package-Release.py', 'Scripts/Verify-Release.ps1', 'README.md', 'CHANGELOG.md']
    missing = sorted(set(required) - tracked)
    if missing:
        raise ValueError('Required release sources were not committed: ' + ', '.join(missing))
    args.output.mkdir(parents=True, exist_ok=True)
    binary_zip = args.output / f'MGVR-{version}-Windows-x64.zip'
    if binary_zip.exists():
        raise FileExistsError('Preserve or remove the existing runtime ZIP before packaging again: ' + str(binary_zip))
    shell = shutil.which('pwsh')
    if not shell:
        raise RuntimeError('PowerShell 7 is required for assembly verification.')
    assembly_report = args.output / 'assembly-verification.json'
    subprocess.run([shell, '-NoProfile', '-File', str(ROOT / 'Scripts/Verify-Release.ps1'),
                    '-AssemblyPath', str(ROOT / 'Artifacts/Build/MGVR/Release/net6.0/MGVR.dll'),
                    '-Report', str(assembly_report)], cwd=ROOT, check=True)
    verified = json.loads(assembly_report.read_text(encoding='utf-8-sig'))
    files, origins = {}, {}

    def add(name, data, origin):
        path = PurePosixPath(name)
        if path.is_absolute() or '..' in path.parts or '\\' in name or name in files:
            raise ValueError('Invalid or duplicate ZIP entry: ' + name)
        files[name] = data
        origins[name] = origin

    def source_file(name, path):
        if path not in tracked:
            raise ValueError('Uncommitted package document: ' + path)
        add(name, git('show', commit + ':' + path), path + ' at ' + commit)

    loader_bytes = args.loader_archive.read_bytes()
    if digest(loader_bytes) != LOADER_SHA256:
        raise ValueError('The MelonLoader 0.7.3 archive does not match the pinned checksum.')
    with zipfile.ZipFile(args.loader_archive) as loader:
        if loader.testzip() is not None:
            raise ValueError('Corrupt MelonLoader archive.')
        for item in loader.infolist():
            if item.is_dir():
                continue
            name = item.filename
            if name != 'version.dll' and not name.startswith('MelonLoader/'):
                raise ValueError('Unexpected loader archive entry: ' + name)
            add(name, loader.read(item), 'Official MelonLoader 0.7.3 x64 archive')

    runtime = {
        'Mods/MGVR.dll': ROOT / 'Artifacts/Build/MGVR/Release/net6.0/MGVR.dll',
        'UserLibs/Valve.OpenVR.dll': ROOT / 'Artifacts/Build/Valve.OpenVR/Release/net6.0/Valve.OpenVR.dll',
        'MGVR.Native.dll': ROOT / 'Artifacts/Build/Native/Release/MGVR.Native.dll',
        'openvr_api.dll': ROOT / 'ThirdParty/OpenVR/openvr_api.dll',
    }
    xr = json.loads(git('show', commit + ':ThirdParty/UnityOpenXR/SOURCE.json'))
    for name, expected in xr['files'].items():
        path = args.xr_source / name
        if digest(path.read_bytes()) != expected:
            raise ValueError('XR dependency differs from the pinned runtime: ' + name)
        runtime['ManifoldGarden_Data/' + name] = path
    for name, path in runtime.items():
        add(name, path.read_bytes(), str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else path.name)
    if digest(files['Mods/MGVR.dll']) != verified['SHA256'] or verified['Version'] != version:
        raise ValueError('The verified plugin changed before packaging.')
    for name in ('version.dll', 'MGVR.Native.dll', 'openvr_api.dll',
                 'ManifoldGarden_Data/Plugins/x86_64/UnityOpenXR.dll',
                 'ManifoldGarden_Data/Plugins/x86_64/openxr_loader.dll'):
        native_x64(files[name], name)
    manifest = json.loads(files['ManifoldGarden_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json'])
    if manifest['version'] != '1.3.1' or manifest['libraryName'] != 'UnityOpenXR':
        raise ValueError('Unexpected XR subsystem manifest.')

    documents = {
        'README.md': 'README.md', 'CHANGELOG.md': 'CHANGELOG.md', 'LICENSE': 'LICENSE',
        'Licenses/OpenVR-LICENSE.txt': 'ThirdParty/OpenVR/LICENSE',
        'Licenses/Unity-OpenXR-LICENSE.md': 'ThirdParty/UnityOpenXR/LICENSE.md',
        'Licenses/Unity-OpenXR-Third-Party-Notices.md': 'ThirdParty/UnityOpenXR/Third Party Notices.md',
        'Licenses/OpenXR-loader-Apache-2.0.txt': 'ThirdParty/UnityOpenXR/OpenXR-loader-Apache-2.0.txt',
        'Licenses/NotoSansCJK-OFL.txt': 'Mod/Assets/MenuFont.OFL.txt',
        'Font-source.json': 'Mod/Assets/MenuFont.source.json',
    }
    for name, path in documents.items():
        source_file('MGVR-Info/' + name, path)
    changelog = files['MGVR-Info/CHANGELOG.md'].decode('utf-8-sig').replace('\r\n', '\n')
    current_section = changelog.split(f'## {version}\n', 1)
    if len(current_section) != 2:
        raise ValueError('CHANGELOG is missing the release version: ' + version)
    release_changes = current_section[1].split('\n## ', 1)[0].strip()
    if not release_changes:
        raise ValueError('CHANGELOG has no release notes for: ' + version)
    notices = f'''# Third-party components in MGVR {version}

MGVR: {REPOSITORY}/tree/{commit}
The project uses AGPL-3.0; see LICENSE. Dependencies retain their own licenses.

- MelonLoader 0.7.3 x64: https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3
  Taken from the official archive. Its licenses and notices remain in
  MelonLoader/Documentation/.
- OpenVR SDK: https://github.com/ValveSoftware/openvr
  Source commit 0924064316de3effbcd1acf1e309182a2deb1c05.
  See Licenses/OpenVR-LICENSE.txt.
- Unity OpenXR 1.3.1: Windows native runtime from the Unity 2020.3.49f1
  XR validation build. Package license and third-party notices are in Licenses/.
- Khronos OpenXR loader: distributed with Unity OpenXR, under Apache-2.0.
  See Licenses/OpenXR-loader-Apache-2.0.txt and the Unity package notices.
- MGVR Menu SC glyph subset: derived from Noto Sans CJK SC, under SIL OFL 1.1.
  See Licenses/NotoSansCJK-OFL.txt and Font-source.json for the pinned source.
  The full source font is not included in this package.
'''
    add('MGVR-Info/THIRD-PARTY-NOTICES.md', notices.encode(), 'Release packaging')
    build = {
        'version': version, 'release_type': 'Release', 'tag': args.tag,
        'source_repository': REPOSITORY, 'source_commit': commit,
        'source_tree': git('rev-parse', commit + '^{tree}').decode().strip(),
        'configuration': 'Release / Windows x64',
        'packaged_at': datetime.now(timezone(timedelta(hours=8))).isoformat(),
        'melonloader_version': '0.7.3', 'melonloader_archive_sha256': LOADER_SHA256,
        'unity_openxr_version': '1.3.1', 'assembly_verification': verified,
        'readme_sha256': digest(files['MGVR-Info/README.md']),
        'runtime_files': {name: {'sha256': digest(files[name]), 'bytes': len(files[name]), 'source': origins[name]} for name in runtime},
    }
    add('MGVR-Info/BUILD.json', (json.dumps(build, ensure_ascii=False, indent=2) + '\n').encode(), 'Release packaging')
    for name in files:
        parts = {part.lower() for part in PurePosixPath(name).parts}
        if parts & {'userdata', 'saves', 'il2cppassemblies', '_vr_analysis', '.git', '.codex', 'unitydependencies'}:
            raise ValueError('Forbidden generated or personal content: ' + name)
        if PurePosixPath(name).name.lower() in {'gameassembly.dll', 'unityplayer.dll', 'manifoldgarden.exe', 'mgvr.workspace.txt'}:
            raise ValueError('Forbidden original game or local file: ' + name)
        if name != 'version.dll' and name not in runtime and not name.startswith(('MelonLoader/', 'MGVR-Info/')):
            raise ValueError('Entry is outside the package allowlist: ' + name)
    sums = ''.join(f'{digest(files[name])}  {name}\n' for name in sorted(files))
    add('MGVR-Info/SHA256SUMS.txt', sums.encode(), 'Checksums; excludes this file itself')
    temporary = binary_zip.with_suffix('.zip.tmp')
    with zipfile.ZipFile(temporary, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as result:
        for name in sorted(files):
            result.writestr(name, files[name])
    with zipfile.ZipFile(temporary) as result:
        if result.testzip() is not None or set(result.namelist()) != set(files):
            raise ValueError('Runtime ZIP readback failed.')
        for name, data in files.items():
            if result.read(name) != data:
                raise ValueError('Runtime ZIP entry mismatch: ' + name)
    temporary.rename(binary_zip)
    checksum = digest(binary_zip.read_bytes())
    (args.output / 'SHA256SUMS.txt').write_text(f'{checksum}  {binary_zip.name}\n', encoding='utf-8')
    release_notes = f'''# MGVR {version}

{release_changes}

## 安装

下载 `MGVR-{version}-Windows-x64.zip`，退出游戏，将包内全部文件复制到游戏根目录；在 Steam 启动选项填入 `--vr`。首次启动请等待加载器生成适配文件。更新时保留 `UserData` 和存档。

已验证环境：Windows x64、Steam 版、Quest 3、Virtual Desktop / VDXR。

## 已知问题

- 测试选关可能影响游戏状态或进度，尤其结局等特殊流程；建议备份并使用测试存档。

GitHub 自动提供的 Source code 为源码，不能直接用于安装。
'''
    (args.output / 'RELEASE-NOTES.md').write_text(release_notes, encoding='utf-8')
    report = {
        'version': version, 'tag': args.tag, 'source_commit': commit,
        'runtime_zip': {'name': binary_zip.name, 'bytes': binary_zip.stat().st_size, 'sha256': checksum, 'files': len(files)},
        'archive_readback_verified': True, 'allowed_files_verified': True,
        'native_x64_verified': True, 'embedded_resources_verified': True,
        'source_zip_created': False, 'installed_to_game': False, 'uploaded': False,
    }
    (args.output / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
