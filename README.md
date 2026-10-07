# Manifold Garden VR（MGVR）

Manifold Garden 的实验性 PC VR Mod，使用 MelonLoader 和 Unity OpenXR。

**0.8.29：主要 VR 功能已实现，形成可运行的开发版本。** 支持双眼渲染、头部跟踪、手柄移动与转向、重力切换、射线物体交互、射线菜单和 VR 设置。仍有已知问题，尚未完成所有关卡与设备的验证。

当前已验证环境：Windows x64、Steam 版 Manifold Garden（Unity 2020.3.12f1）、Quest 3、Virtual Desktop / VDXR。其他头显、运行时和平台尚未系统验证。本项目不是游戏官方发布的 VR 版本，需要自行拥有并安装原游戏。

## 下载与安装

在 [Releases](https://github.com/Tualin14/ManifoldGardenVR/releases) 下载 **`MGVR-0.8.29-Windows-x64.zip`**。这是编译好的运行包，包含 MelonLoader 0.7.3 x64、MGVR 和所需 XR 运行文件；不需要另外安装 MelonLoader。GitHub 自动生成的 `Source code` 和 `MGVR-0.8.29-source.zip` 是源码，不能直接用于安装。

1. 正常退出游戏。
2. 在 Steam 中右键 Manifold Garden → 管理 → 浏览本地文件，找到包含 `ManifoldGarden.exe` 的游戏根目录。
3. 首次安装前备份存档；如果已经安装加载器或其他 Mod，也先备份将被覆盖的同名文件。本包按 MelonLoader 0.7.3 打包，已有其他版本加载器的环境尚未验证。
4. 解压运行包，将**解压出来的全部文件和文件夹**复制到游戏根目录，合并文件夹。更新本 Mod 时覆盖同名文件，不需要清空 `UserData` 或删除存档。不要把外层下载文件夹整体放进去。
5. 连接头显并进入 PCVR。当前已验证方式是 Virtual Desktop / VDXR；它需要自行安装，不包含在本包中。
6. 在 Steam 的游戏属性 → 通用 → 启动选项中填入 `--vr`，然后启动游戏。

正确安装后，关键文件的位置如下（省略加载器内的其他文件）：

```text
Manifold Garden/
├─ ManifoldGarden.exe                  原游戏文件，本包不包含
├─ version.dll                        MelonLoader 入口
├─ MelonLoader/
├─ Mods/
│  └─ MGVR.dll
├─ UserLibs/
│  └─ Valve.OpenVR.dll
├─ MGVR.Native.dll
├─ openvr_api.dll
├─ ManifoldGarden_Data/
│  ├─ Plugins/x86_64/
│  │  ├─ UnityOpenXR.dll
│  │  └─ openxr_loader.dll
│  └─ UnitySubsystems/UnityOpenXR/
│     └─ UnitySubsystemsManifest.json
└─ MGVR-Info/                         安装说明、许可证和文件清单
```

第一次启动时，MelonLoader 需要联网获取依赖并生成当前游戏的适配程序集，启动时间可能较长；等待它完成，不要因生成期间尚未出现游戏窗口而重复启动。运行包不包含从游戏生成的程序集。

MelonLoader 0.7.3 的官方要求是 Windows x64 [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)；加载器支持自动处理运行时。如果启动提示缺少 .NET，按提示安装 **Desktop Runtime x64** 后再试，无需安装开发用 SDK。详见 [MelonLoader 0.7.3 官方说明](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/README.md#requirements)。

## VR 启动与桌面模式

VR 启动参数：

```text
--vr
```

进入主菜单后，戴好头显并面向舒适的正前方短暂停稳，等待 VR 自动启动和校准。进入关卡后继续使用同一 XR 会话。需要调整高度或朝向时，恢复平时的坐姿／站姿，让摇杆回中、松开其他按钮，长按 **Y 约 1 秒**归位。

要使用桌面模式，清空 `--vr` 并重新启动游戏。无参数时 MGVR 不安装游戏钩子、不添加 VR 设置页、不接管键鼠、不读取 VR 配置、不启动 XR。MelonLoader 本身仍会加载；如需停用所有 Mod，可使用加载器的 `--no-mods` 参数。旧配置中的 `Enabled` 字段不决定启动模式。

直接从命令行启动时也可以使用：

```powershell
.\ManifoldGarden.exe --vr
```

## Quest 手柄操作

以下是默认布局。扳机是食指位置的上方按钮，握把是手柄侧面的握持按钮。游戏设置中的 **VR Settings** 提供分段／平滑转向、分段角度和平滑转向速度；下方图片用于查看操作说明，不提供按键重绑。

| 按钮或动作 | 游戏中的功能 |
| --- | --- |
| 头显转动 | 观察视线 |
| 左摇杆 | 前后左右移动 |
| 右摇杆左右 | 分段或平滑转向 |
| 左握把 | 跑步 |
| 右扳机 | 用右手射线瞄准后拾取／放下／交互 |
| 右握把 | 改变重力方向 |
| A | 顺时针旋转所持物体 |
| B | 逆时针旋转所持物体 |
| Y 长按约 1 秒 | 归位；先让摇杆回中并松开其他操作 |
| X | 打开测试选关 |
| 左手菜单键 | 菜单（暂停／继续） |
| 左扳机 | 默认无动作 |
| 左右摇杆按下 | 无动作 |
| 右手 Meta 系统键 | 由头显系统处理 |

菜单中使用**右手射线与右扳机**操作：

- 指向按钮，按下并松开右扳机进行点击；移开后松开会取消点击。
- 指向滑块，按住右扳机并移动手柄进行拖动。
- 点击页面的 Back／返回按钮返回；设置顶部标签也通过射线点击。
- 长按 Y 可重新归位并调整菜单位置。
- 菜单中摇杆、A/B、握把和 X 不承担导航操作。

新增操作图在简体中文下显示中文，其他语言显示英文。新增 VR 设置文字使用英文，原游戏界面沿用游戏自身语言。

## 测试选关与存档

X 选关用于开发测试，不等同正常游戏进度功能。先从正常存档进入关卡，再打开列表，用射线选择关卡和 Previous／Next／Back。

**实际选择测试关卡后，本次游戏进程会阻止保存，直到退出并重新启动。** 选关前会备份当前存档；备份失败则不执行测试跳关。只打开列表再取消，不会进入测试状态。返回主菜单或重新载入存档不能退出测试状态，恢复正常保存必须重启游戏。

没有开发工作区标记时，运行记录和测试存档备份位于游戏目录的 `_vr_analysis/`，备份在其中的 `TestSaveBackups/`。本发行包不包含本机工作区标记或个人配置。

目前存档相关问题尚未完成调查，建议使用备份的测试存档验证。请避开 `World_072_FinalLevel` 等结局场景；进入最终关后再测试跳关，可能出现物体无法放下或无法交互。

## 已知限制与反馈

- 这是可运行的开发版本，尚未覆盖全部关卡、设备和长时间运行。
- 从远处看门内另一侧场景，可能出现局部遮挡，暂未修复。
- 最终关相关测试跳关交互异常和存档问题暂缓处理。
- 性能尚未正式测量；后台／焦点兼容和其他 XR 运行时尚未系统验证。
- 当前没有手柄模型、触觉反馈或直接用手抓取的六自由度交互。

请通过 [Issues](https://github.com/Tualin14/ManifoldGardenVR/issues) 提供插件版本、游戏版本、头显、显卡、运行时、场景名、复现步骤和实际现象。日志优先提供 `MelonLoader/Latest.log`，必要时附 `_vr_analysis/runtime-probe.log`。发布日志前自行检查其中的本机路径与个人信息。

## 停用与卸载

仅切回桌面模式：清空 Steam 启动选项里的 `--vr`，退出并重新启动游戏。

仅卸载 MGVR：游戏退出后移走 `Mods/MGVR.dll`。如果其他 Mod 依赖 MelonLoader，可以保留加载器。`UserData/MGVR.json` 是运行时生成的 VR 配置，是否保留由你决定。

完全卸载本包：根据 `MGVR-Info/SHA256SUMS.txt` 的文件清单移除本包文件；安装前已存在的同名文件应从备份恢复。安装说明目录中还有校验清单本身，需要一并移走。已有其他 Mod 时，不要直接删除整个 `MelonLoader`、`Mods` 或 `UserData` 目录。原游戏文件和存档不属于本包。

## 从源码构建

运行包使用者无需构建。开发者需要 .NET 6 SDK、MSBuild 和 C++ x64 工具链；原生工程默认使用 `v145`。还需要自己的游戏安装，以及 MelonLoader 首次运行生成的引用程序集。

1. 将 `Build.Local.props.example` 复制为 `Build.Local.props`，把 `GameRoot` 改为本机游戏根目录；也可在没有本机配置文件时设置 `MGVR_GAME_DIR`。
2. 在仓库根目录执行：

```powershell
.\Scripts\Build.ps1 -Configuration Release
```

构建输出位于 `Artifacts/Build/`，构建不会自动部署或启动游戏。源码不包含原游戏程序集、存档或反编译产物。

## 许可证与第三方依赖

项目许可证为 [AGPL v3](https://github.com/Tualin14/ManifoldGardenVR/blob/main/LICENSE)。第三方组件沿用各自许可证，不因打包而改为 AGPL。运行包保留了 MelonLoader 的官方许可证和第三方声明，并在 `MGVR-Info/Licenses/` 提供 MGVR、OpenVR、Unity OpenXR 和 OpenXR loader 的相关说明。源码和构建基准信息见运行包中的 `MGVR-Info/BUILD.json`。
