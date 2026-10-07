$ErrorActionPreference = 'Stop'
$MGVRRoot = Split-Path -Parent $PSScriptRoot
function Get-MGVRGameRoot {
    $local = Join-Path $MGVRRoot 'Build.Local.props'
    if (Test-Path -LiteralPath $local) {
        [xml]$settings = Get-Content -LiteralPath $local -Raw
        $game = [string]$settings.Project.PropertyGroup.GameRoot
    } else { $game = $env:MGVR_GAME_DIR }
    if ([string]::IsNullOrWhiteSpace($game)) { throw 'Set GameRoot in Build.Local.props or MGVR_GAME_DIR.' }
    $game = [IO.Path]::GetFullPath($game)
    if (-not (Test-Path -LiteralPath (Join-Path $game 'ManifoldGarden.exe'))) { throw 'Configured GameRoot does not contain ManifoldGarden.exe.' }
    return $game
}
function Get-MGVRMSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if(-not(Test-Path -LiteralPath $vswhere)){throw 'Visual Studio Installer/vswhere.exe not found.'}
    $install = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if(-not $install){throw 'Visual Studio C++ build tools are required.'}
    return Join-Path $install 'MSBuild/Current/Bin/MSBuild.exe'
}
