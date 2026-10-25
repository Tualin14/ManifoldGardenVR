# 第三方组件与许可证

MGVR 的 AGPL-3.0 不改变下列组件的原有许可证。运行包需保留依赖原包中的版权与第三方声明。

| 组件 | 来源与说明 |
| --- | --- |
| MelonLoader 0.7.3 x64 | 官方发行包；保留 `MelonLoader/Documentation/` 中的许可证和依赖声明。本机下载缓存不进入 Git |
| OpenVR SDK | `OpenVR/` 保存绑定、头文件、链接库和 Windows x64 运行库；固定源提交见 `SOURCE-COMMIT.txt`，许可证见 `LICENSE` |
| Unity OpenXR 1.3.1 | 来自仓库 Unity XR 验证工程的构建；包许可证和第三方声明见 `UnityOpenXR/` |
| Khronos OpenXR loader | 随 Unity OpenXR 1.3.1 分发，保留上游 Apache-2.0 文本和包内声明 |
| Noto Sans CJK SC 字形子集 | 固定源提交和哈希在 `Mod/Assets/MenuFont.source.json`，SIL OFL 版权与许可证在 `MenuFont.OFL.txt`，并内嵌 Mod |

Unity OpenXR 的两个 DLL 和子系统清单不从原游戏提取，不加入源码；本机依赖目录记录其来源和 SHA256。完整 Noto 源字体仅用于制作，运行包只包含已烘焙的静态子集。
