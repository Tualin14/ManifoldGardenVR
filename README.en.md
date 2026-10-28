# Manifold Garden VR (MGVR)

[简体中文](README.md) | **English**

An unofficial PC VR mod for Manifold Garden, with head tracking, Quest controller support, gravity switching, ray
interaction, VR menus, and haptic feedback. **You must own the original game.**

**Current version:
1.0.5** · [Download](https://github.com/Tualin14/ManifoldGardenVR/releases) · [Demo video (Bilibili)](https://www.bilibili.com/video/BV1Vtx76MEev/) · [Report an issue](https://github.com/Tualin14/ManifoldGardenVR/issues)

Tested setup: Windows x64, the Steam edition, Quest 3, and Virtual Desktop with VDXR. Other headsets and runtimes have
not been systematically tested.

> [!WARNING]
> **VR comfort:** Gravity changes, continuous movement, smooth turning, and **the spatial changes and visual effects in
> some in-game scenes and sequences** may cause motion sickness, nausea, eye strain, or other discomfort.
> Start with short sessions and try snap turning first.
> **If you feel unwell, stop playing immediately, remove your headset, and rest. Do not push through discomfort.**

Follow your headset manufacturer's safety guidance, such as the [Meta Quest health and safety warnings](https://www.meta.com/legal/quest/health-and-safety-warnings/).

## Installation and launch

1. Download **`MGVR-1.0.5-Windows-x64.zip`**. GitHub's `Source code` archives contain source files and cannot be
   installed directly.
2. Exit the game and back up your saves. In Steam, right-click the game → **Manage → Browse local files** to open the
   folder containing `ManifoldGarden.exe`.
3. Copy **all contents** of the ZIP into that folder, merging folders and replacing existing files. Keep `UserData` and
   your saves when updating. If you use another loader or other mods, back up any files that would be overwritten first.
4. Connect your headset and enter PCVR. In Steam → **Properties → Launch Options**, enter **`--vr`**, then launch the
   game.

MelonLoader and the XR files are included. **The first launch needs an internet connection to download and generate
compatibility files. Please wait for it to finish.** If prompted for .NET, install
the [.NET 6 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/6.0).

VR starts automatically at the main menu. Put on your headset, face forward, and hold still briefly. To return to
desktop mode, **remove `--vr` and restart the game**.

## Quest controls

| Control                   | Action                                                            |
|---------------------------|-------------------------------------------------------------------|
| Left stick                | Move                                                              |
| Right stick left / right  | Turn                                                              |
| Left grip                 | Run                                                               |
| Right trigger             | Pick up, drop, or interact using the right-hand ray               |
| Right grip                | Change gravity                                                    |
| A / B                     | Rotate the held item clockwise / counterclockwise                 |
| Hold Y for about 1 second | Recenter; center both sticks and release the other controls first |
| X                         | Open the menu / resume the game                                   |

The trigger is under your index finger; the grip is on the side of the controller. The left trigger and stick clicks
have no action by default. The controller's menu button is handled by the headset or streaming software.

**Menus:** aim with the right-hand ray, then press and release the right trigger to click. Hold it to drag sliders.
Click **Back** to return to the previous page. Hold Y to reposition the menu.

**Continue a sequence:** when prompted to hold after placing a God Cube, hold the right trigger to continue. A progress
bar and remaining seconds show your progress. Releasing the trigger stops it from advancing.

**Test Levels:** open it from the pause menu. On the level selection page, click **Back** or press X to return to the
pause menu, then press X again to resume.

## VR settings

Open **Settings → VR Settings** to adjust snap / smooth turning, snap angle, smooth turn speed, vibration, and vibration
strength. Changes apply and save immediately. A controller diagram appears below the settings.

The mod's menus and controller diagrams support Simplified Chinese; all other game languages use English. Vibration also
requires the original game's master vibration setting to be enabled.

## Known issues and feedback

Skipping levels may cause unexpected game behavior or progress issues, especially around endings and other special
sequences. Back up your saves and use a separate test save.

When opening an [issue](https://github.com/Tualin14/ManifoldGardenVR/issues), include your mod version, headset and
runtime, current level, and steps to reproduce the problem.

Licensed under [AGPL-3.0](LICENSE). Third-party components retain their own licenses; full notices are included in the
runtime package.
