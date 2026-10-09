# SpaceKeeper for Windows

A Windows 11 tray app to **name, pin, reorder, add and remove virtual desktops**. It's the Windows companion to SpaceKeeper for Mac, built with current Microsoft technology: **WinUI 3**, the **Windows App SDK** and **.NET 10**, using the MVVM pattern.

## What it does

| Feature | How it works on Windows |
|---|---|
| **See every desktop at once** | The panel shows your desktops as a grid of tiles, up to 8 in a row, so 16 desktops fit in two rows with no scrolling. With very large tiles it switches to 4 (or 2) in a row. |
| **Colour-code desktops** | Right-click a tile › **Colour** to fill it with one of ten colours chosen for low vision and colour blindness: every colour keeps its text at 7:1 contrast or better (WCAG AAA, checked by an automatic test), and each has its own symbol (circle, square, triangle…) and name, so colour is never the only clue. With a Windows contrast theme on, tiles use the theme's colours instead. |
| **Name desktops** | Double-click a tile (or select it and press F2) and type a name. Windows 11 stores it, so the same name appears in Task View (Win+Tab) too. |
| **Switch** | Click a tile, or select it and press Enter. Windows plays its usual slide animation. |
| **Add / remove** | **Add desktop** (or Ctrl+N) adds a desktop. It's never greyed out, because Windows has no maximum number of desktops (macOS stops at 16). The **×** on a tile, then **Remove**, closes that desktop, and Windows moves its windows to a neighbouring desktop. |
| **Pin the order** | Pin important desktops. If one is dragged out of order in Task View, the panel shows a warning saying where it belongs, with **Accept new order** and **Open Task View**. |
| **Your own grid order** | Drag tiles, or use Alt+Shift+arrow keys or right-click › Move earlier/later, to keep related desktops together. Task View's order isn't changed. |
| **Name label** | A small label in a corner of the screen always shows which desktop you're on. You choose the corner, opacity and size. |
| **Switch banner** | The desktop's name appears briefly when you change desktop. |
| **Open from anywhere** | **Double-tap Ctrl** (one hand) opens or closes the panel from any app; **Ctrl+Alt+S** works too. Esc closes it. |
| **Bigger tiles** | Settings › Desktop tile size, the small/large **A** buttons, or Ctrl+Plus / Ctrl+Minus / Ctrl+0. |

SpaceKeeper lives in the notification area ("tray") at the right of the taskbar. Left-click the icon for the panel, or right-click it for Quit. The panel opens centred just above the taskbar. Settings stay folded away until you click **Settings**.

### How it differs from the Mac version

- **Real names:** Windows 11 lets apps rename desktops, so names are real, not just labels.
- **No fixed-order switch:** Windows never shuffles desktops by itself, so there's nothing to switch off.
- **One grid for all monitors:** Windows desktops span every monitor, so the grid isn't split by display.
- **Double-tap Ctrl instead of fn-S:** on almost every PC keyboard the fn key is handled inside the keyboard and Windows never sees it, and Windows is reserving Win+Alt+S for itself, so the one-handed shortcut is a quick double tap of Ctrl. Holding Ctrl or using it in shortcuts like Ctrl+C never counts. It doesn't work while an app running "as administrator" is in front; use Ctrl+Alt+S there.
- **No 16-desktop limit:** Add desktop is never greyed out.
- **"Pin" means something different:** in Windows, pinning usually means showing a window on every desktop. In SpaceKeeper, a pin keeps a desktop's **place in the order**.

## Licence

SpaceKeeper for Windows is open source under the [MIT licence](LICENSE). It isn't code-signed; the [PowerShell installer](#install-on-a-windows-pc-recommended) checks each download against its published checksum instead.

## Getting the app (no Windows development tools needed)

GitHub builds the app for you:

1. On your Mac, run `bash publish_to_github.sh` in this folder. It creates the GitHub repository and uploads the code.
2. GitHub builds SpaceKeeper automatically, which takes about 5–10 minutes. Open the repository's **Actions** tab, select the latest run and download **SpaceKeeper-win-x64** (or **win-arm64** for Arm PCs such as Snapdragon laptops) from **Artifacts**.
3. For a permanent download link, publish a version: `bash publish_to_github.sh SpaceKeeper-Windows v1.0.0`. The zips then appear under **Releases**.

### Install on a Windows PC (recommended)

Press **Win+X** › **Terminal** (or open PowerShell) and paste:

```
powershell -c "irm https://raw.githubusercontent.com/shuistyle/SpaceKeeper-Windows/main/install.ps1 | iex"
```

It downloads the latest release for your PC (x64 or Arm), **checks it against the published SHA-256 checksum** (and stops if it doesn't match), installs it to `%LOCALAPPDATA%\Programs\SpaceKeeper` (a private folder only your Windows account can change), adds it to the Start menu and starts it. No administrator rights needed, and **no "Windows protected your PC" warning**: that warning only appears for files a web browser has marked as downloaded from the internet, and the checksum check takes its place. Run the same command again to update. Your settings are kept.

To remove SpaceKeeper:

```
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/shuistyle/SpaceKeeper-Windows/main/install.ps1))) -Uninstall
```

(add `-RemoveSettings` to delete your saved settings too). You can read exactly what the script does in [install.ps1](install.ps1).

Then turn on **Launch at sign-in** in the panel's settings so SpaceKeeper starts automatically.

### Or download the zip by hand

1. **Check the download.** Each release includes `SHA256SUMS.txt`. In PowerShell, in the folder with the zip, run `Get-FileHash .\SpaceKeeper-win-x64.zip` (or `-arm64`) and check the result matches that file's line in `SHA256SUMS.txt`. With the GitHub CLI you can also confirm the zip was built by this repository's GitHub workflow: `gh attestation verify SpaceKeeper-win-x64.zip --repo shuistyle/SpaceKeeper-Windows`.
2. **Unblock it** (right-click the zip › Properties › tick **Unblock** › OK) to avoid the SmartScreen warning, then **extract** it and run **SpaceKeeper.exe**. (Without Unblock, Windows says "Windows protected your PC" because the app isn't code-signed; click **More info** › **Run anyway**.)
3. **Click Install** in the panel. SpaceKeeper copies itself to `%LOCALAPPDATA%\Programs\SpaceKeeper` and restarts from there; you can then delete the extracted folder.

> **Why install?** A Windows app loads the files next to it. Running from OneDrive (which syncs files in from the cloud and other devices), Downloads or a temporary folder would let a changed file there run inside SpaceKeeper, which can see your keyboard. So Launch at sign-in is only allowed from a safe folder.

> **Smart App Control:** on PCs where Windows' Smart App Control is switched on, unsigned apps can be blocked however they're installed. Most PCs have it off.

## Building on Windows yourself

You need Windows 11 and either Visual Studio 2022 (17.13 or later, with the *WinUI application development* workload) or just the .NET 10 SDK.

```powershell
# Run the tests
dotnet test tests/SpaceKeeper.Core.Tests

# Build and run (use -p:Platform=ARM64 on Arm PCs)
dotnet run --project src/SpaceKeeper.App -p:Platform=x64
```

Or open **SpaceKeeper.slnx** in Visual Studio, choose **x64**, and press F5.

## Accessibility

- **Screen readers (Narrator, NVDA, JAWS):**
  - Every control says what it does and to which desktop, for example "Pin Mail" or "Remove Code".
  - Each tile reads as a summary, for example "Mail, Desktop 2, pinned", with a hint on how to switch and rename.
  - Headings let users jump between sections.
  - Desktop switches, warnings and status messages are announced.
- **Keyboard only:**
  - Tab through everything.
  - Double-tap Ctrl or Ctrl+Alt+S opens the panel, Esc closes it (or cancels renaming first), Ctrl+N adds a desktop.
  - Arrow keys move between tiles; Enter switches, F2 renames, Delete removes (with confirmation), Alt+Shift+arrows reorder.
  - Shift+F10 or the Menu key opens the right-click menu.
- **Vision:**
  - Follows light, dark and **contrast themes** automatically.
  - **Desktop tile size** (Standard, Large, Extra large, Largest — up to double size) makes tiles, names and buttons bigger. It works on top of Windows' **Settings › Accessibility › Text size**, and tiles grow with both so text is never cut off.
  - The label and banner have their own Large and Extra large sizes.
  - Nothing relies on colour alone: the pin icon changes shape, and the current desktop has a thick border, a ringed number and the word "current".
- **Motion and transparency:**
  - The overlays never animate.
  - The banner stays longer when **Animation effects** are off.
  - Backgrounds turn solid when **Transparency effects** are off.
- **Targets:** icon buttons are at least 32 × 32 pixels.

## How the code is organised

```
SpaceKeeper.slnx                 open in Visual Studio
src/SpaceKeeper.Core/            plain logic, no Windows code, fully tested
  Models.cs                      data types (desktops, alerts, settings)
  PinEvaluator.cs                "are pinned desktops still in order?"
  ListOrder.cs                   your own grid order
  StateStore.cs                  saves to %LOCALAPPDATA%\SpaceKeeper\state.json
src/SpaceKeeper.App/             the Windows app (WinUI 3)
  App.xaml.cs                    START HERE: creates and connects everything
  ViewModels/MainViewModel.cs    the "brain": data and actions for the panel
  ViewModels/DesktopTileViewModel.cs one tile in the grid
  Views/PanelWindow.xaml(.cs)    the panel
  Views/OverlayWindow.xaml(.cs)  corner label and switch banner
  Services/VirtualDesktopService.cs  talks to Windows' virtual desktops
  Services/DoubleTapCtrlService.cs  double-tap Ctrl (one-handed shortcut)
  Services/HotKeyService.cs      Ctrl+Alt+S
  Services/SystemServices.cs     sign-in launch, notifications, accessibility settings
  Services/InstallService.cs     "Install": copies SpaceKeeper to a safe private folder
  Services/CrashReport.cs        shows and logs start-up problems
  Services/Win32.cs              direct calls into Windows functions
tests/SpaceKeeper.Core.Tests/    automatic tests (run on every GitHub build)
.github/workflows/build.yml      the automatic GitHub build
```

Every file starts with a plain-English explanation of what it does and how it connects to the rest.

## Good to know

- Windows has no official programming interface for listing, renaming or switching desktops. SpaceKeeper uses the open-source **[Slions.VirtualDesktop](https://github.com/Slion/VirtualDesktop)** library, which handles the undocumented parts and is updated for new Windows 11 releases. If a Windows update ever breaks SpaceKeeper, the Diagnostics section will say so; rebuilding with the newest library version usually fixes it.
- SpaceKeeper has no network code. Your pins, grid order and settings stay in one file on your PC.
