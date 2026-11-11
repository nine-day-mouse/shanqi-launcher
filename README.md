# ShanQi Launcher · 闪启 Launcher

A keyboard-first launcher for Windows, built as a rebranded and de-clouded derivative of
[Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher). Press a hotkey, type, hit Enter —
launch apps, find files, do math, run shell commands, search the web.

**中文文档：[README-zh.md](README-zh.md)**

---

## Why this fork

Upstream Flow Launcher is excellent but assumes a fast, unrestricted connection to GitHub. This fork
keeps the whole launcher core intact and only changes what gets in the way on an offline / restricted
network, plus a full rebrand:

| # | Change | Where |
|---|--------|-------|
| 1 | Plugin store manifest download on startup is disabled (it blocked startup on slow networks) | `Flow.Launcher/App.xaml.cs` |
| 2 | Version update check and plugin auto-update are disabled | `Flow.Launcher/App.xaml.cs` |
| 3 | Single-instance lock uses its own channel name, so it can coexist with official Flow Launcher | `Flow.Launcher/Helper/SingleInstance.cs` |
| 4 | Auto-start registry value uses its own name, never touching the official one | `Flow.Launcher/Helper/AutoStartup.cs` |
| 5 | User data moved to `%APPDATA%\ShanQi` instead of `%APPDATA%\FlowLauncher` | `Infrastructure/UserSettings/DataLocation.cs` |
| 6 | Rebrand: window title, application icon, installer / shortcut names, product name | `MainWindow.xaml`, `Resources/app.ico`, `installer/` |
| 7 | Build is tolerant of `taskkill` returning non-zero when no instance is running | `Flow.Launcher/Flow.Launcher.csproj` |
| 8 | `Shift+Enter` is passed through to plugins (open in Explorer, open Task Manager, …) | `MainWindow.xaml` |

Everything else — query engine, plugin architecture, themes, hotkeys — is upstream behaviour.

## Features

- Global hotkey (default `Alt+Space`), instant fuzzy search over apps, files and folders
- Inline calculator, shell / cmd execution, URL and web search
- Bundled plugins:
  `Program`, `Explorer`, `FileManager`, `Calculator`, `Shell`, `Sys`, `Url`, `WebSearch`,
  `BrowserBookmark`, `WindowsSettings`, `KillProcess`, `ProcessKiller`, `PluginsManager`,
  `PluginIndicator`, `Help`
- Themeable WPF UI (Win11 light / dark, system-follow), fully keyboard driven

## Requirements

| | |
|---|---|
| OS | Windows 10 version 2004 (build 19041) or later, Windows 11 |
| Runtime | None for the installer — it ships the self-contained build (.NET 9 desktop runtime included) |
| Build | .NET SDK 9.0, Windows PowerShell 5.1 |

## Install

Grab a build from the **Releases** page:

| File | Use case |
|------|----------|
| `ShanQiLauncher-Setup-<version>.exe` | Recommended. Double-click, no admin rights, installs and starts the app |
| `ShanQiLauncher-Setup-<version>.msi` | Silent / scripted deployment, same payload |

What the installer does:

- Installs to `%LOCALAPPDATA%\ShanQiLauncher` (per-user, no elevation)
- Creates a Desktop shortcut `闪启 Launcher.lnk` and a Start Menu folder `闪启 Launcher`
- Launches the app when finished
- Uninstall removes the program folder and both shortcuts; user settings in `%APPDATA%\ShanQi` are kept

Silent install / uninstall:

```powershell
msiexec /i ShanQiLauncher-Setup-1.0.0.msi /qn /norestart
msiexec /x ShanQiLauncher-Setup-1.0.0.msi /qn /norestart
```

## Build from source

```powershell
# framework-dependent build -> .\release\
.\build.ps1 -Configuration Release

# additionally publish the self-contained build -> .\release-standalone\
.\build.ps1 -Configuration Release -Standalone

# full pipeline: build + self-contained publish + MSI + single-file EXE installer
.\build.ps1 -Configuration Release -Standalone -Installer -Version 1.0.0
```

Packaging requires WiX Toolset 5 as a global dotnet tool:

```powershell
dotnet tool install --global wix --version 5.0.2
```

Artifacts land in the repository root:

- `ShanQiLauncher-Setup-1.0.0.msi` — WiX package of `release-standalone\`
- `ShanQiLauncher-Setup-1.0.0.exe` — WinForms bootstrapper produced with the .NET Framework
  `csc.exe` bundled in Windows; the MSI is embedded as a managed resource, extracted to `%TEMP%`,
  installed via `msiexec /qn`, then deleted. No extra runtime is needed on the target machine.
- `ShanQiLauncher-Setup-1.0.0.wixpdb` — debug symbols, not needed for distribution

## Repository layout

```
.
├── app/                     # launcher source (Flow Launcher solution, rebranded)
│   ├── Flow.Launcher/            # WPF host application
│   ├── Flow.Launcher.Core/       # query / plugin orchestration
│   ├── Flow.Launcher.Infrastructure/
│   ├── Flow.Launcher.Plugin/     # plugin interfaces
│   ├── Plugins/                  # bundled plugins
│   └── LICENSE                   # MIT, upstream copyright
├── installer/
│   ├── installer.wxs        # WiX v4/v5 package definition (per-user, shortcuts, upgrade code)
│   └── Bootstrapper.cs      # single-file EXE installer that embeds the MSI
├── build.ps1                # build / publish / package pipeline
└── README.md / README-zh.md
```

## Data & uninstall

- Settings, plugins and cache: `%APPDATA%\ShanQi`
- Program files: `%LOCALAPPDATA%\ShanQiLauncher`
- Uninstall: Settings → Apps → Installed apps → **闪启 Launcher**, or `msiexec /x` as above

## License

Released under the MIT License, inherited from [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher)
(copyright Flow-Launcher, Wox) — see [`app/LICENSE`](app/LICENSE) and [`app/attribution.md`](app/attribution.md).
All upstream credits remain intact; this repository only contains rebranding and the local-network
adjustments listed above.
