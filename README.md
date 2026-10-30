# HV-LAB Manager

> 🇫🇷 [Version française](README.fr.md)

A **WinUI 3 / .NET 10** desktop application to build and run Microsoft Hyper-V lab environments on Windows: base images, differencing-disk VMs with unattended setup, virtual switches and NAT.

📖 **Documentation**: <https://jm2k69.github.io/HV-LAB/en/>, with a step-by-step user guide.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20%7C%20Server-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![WinUI](https://img.shields.io/badge/UI-WinUI%203%20(App%20SDK%201.8)-blueviolet)
[![Release](https://img.shields.io/github/v/release/JM2K69/HV-LAB)](https://github.com/JM2K69/HV-LAB/releases/latest)

---

## Features

- **Dashboard**: running and stopped VMs, switches and base images at a glance, with a check of the Hyper-V environment (module, `vmms` service) at startup.
- **Virtual machines**:
  - start, stop, open the console (`vmconnect`) and delete VMs;
  - states are refreshed automatically every 5 s;
  - each VM runs one action at a time, so a double click does nothing.
- **Quick VM**: size presets, blank disk or differencing disk, Gen 1/2, Secure Boot, PXE, bulk creation (`PREFIX-01…nn`).
- **Create VM**: differencing disk from a base image with an injected `unattend.xml`:
  - computer name, administrator password (required) and optional auto-logon;
  - language, time zone and a KMS key suggested for the OS;
  - optional CBS cache warm-up and Windows Update disabling;
  - live preview of the answer file, with the password masked;
  - the answer file is removed from the VM at first logon.
- **Base images**: build `BASE_*.vhdx` from a WIM/ESD file, with automatic edition matching:
  - the image is built in a temporary file, so an existing image is never left half-built;
  - overwriting an existing image asks for confirmation;
  - progress is shown live and the build can be cancelled.
- **Switches & NAT**: External, Internal and Private switches with VLAN, and NAT networks with an IPv4 gateway.
- **Fluent UI**:
  - Mica backdrop, light/dark theme and an adaptive navigation pane;
  - the window position and size are remembered;
  - empty states and error banners;
  - accessible names on buttons, F5 to refresh, Del to delete the selection.
- **French / English** interface, switchable live.

All long operations can be cancelled, all inputs are validated before reaching PowerShell, and scripts are never written to `%TEMP%`.

---

## Install

**Requirements**: Windows 10/11 Pro, Enterprise or Education, or Windows Server, with Hyper-V. The application runs as administrator. Nothing else is needed: the build is self-contained (.NET 10 and Windows App SDK included).

1. Download `HV-LAB-X.Y.Z-win-x64.zip` from the [latest release](https://github.com/JM2K69/HV-LAB/releases/latest) and extract it.
2. In an **elevated** PowerShell, from the extracted folder:
   ```powershell
   Unblock-File .\Install.ps1
   .\Install.ps1                 # options: -DesktopShortcut  -EnableHyperV
   ```
   The app is copied to `C:\Program Files\HV-LAB` and gets a Start menu shortcut.
3. Uninstall:
   ```powershell
   & "C:\Program Files\HV-LAB\Install.ps1" -Uninstall
   ```
   Settings and logs in `%LocalAppData%\HV-LAB` are kept.

---

## Build from source

Requires the **.NET 10 SDK**. Visual Studio is not required.

```powershell
git clone https://github.com/JM2K69/HV-LAB.git
cd HV-LAB

dotnet test HVLab.Core.Tests                                   # unit tests
dotnet build HVLab\HVLab.csproj -c Debug -p:Platform=x64       # debug build
dotnet publish HVLab\HVLab.csproj -p:PublishProfile=HVLab-Release   # -> HVLab\publish
.\Install.ps1                                                  # elevated: installs HVLab\publish
```

> In `HVLab\`, a bare `dotnet build` picks up `HVLab.slnx`. Pass `HVLab.csproj` explicitly together with `-p:Platform=x64`.

### Release

The version has a single source: `VersionPrefix` in `Directory.Build.props`. The assembly, file and manifest versions and the version shown in the app all derive from it.

1. Update `VersionPrefix`, then commit and push.
2. Push a matching tag:
   ```powershell
   git tag -a v1.27.0 -m "HV-LAB Manager 1.27.0"
   git push origin v1.27.0
   ```
   A tag like `v1.27.0-beta.1` publishes a pre-release.

The [Release workflow](.github/workflows/release.yml) then:
- runs the tests;
- publishes the self-contained folder;
- creates the GitHub release with `HV-LAB-X.Y.Z-win-x64.zip` and its SHA256.

The workflow refuses a tag that does not match `VersionPrefix`. Running it manually (Actions > Release > Run workflow) only produces the zip as an artifact.

---

## Project structure

```
HV-LAB/
├── HVLab/                      WinUI 3 application (views, shell, UI services)
│   ├── Views/                  Dashboard, VMs, Quick VM, Create VM, base images, switches & NAT, settings
│   ├── Services/               DialogService, ThemeService, LocalizationHelper (UI side)
│   ├── Helpers/                WindowPlacement (DPI, min size, restore), AppInfo, A11y, folder picker
│   ├── Converters/
│   ├── App.xaml(.cs)           DI container, startup, global exception logging
│   └── MainWindow.xaml(.cs)    Title bar, navigation, Hyper-V environment banner
├── HVLab.Core/                 UI-independent code (unit-tested)
│   ├── Models/                 VirtualMachine, VirtualSwitch, BaseVhdx, NatNetwork, LabCatalog…
│   ├── Services/               PowerShellRunner, HyperVService, VhdxService, NatService,
│   │                           AnswerFileGenerator, InputValidator, LocalizationService (FR/EN)…
│   └── ViewModels/             One per page + shared bases (PageViewModelBase, VmCreationViewModelBase)
├── HVLab.Core.Tests/           xUnit tests
├── Install.ps1                 Install / uninstall (Program Files + shortcuts)
├── Directory.Build.props       Application version
└── .github/workflows/          Release workflow
```

**Architecture**:
- **MVVM** with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- **Dependency injection** with Microsoft.Extensions.DependencyInjection: `IHyperVService`, `IVhdxService`, `INatService`, `IPowerShellRunner`, `IDialogService` and `IThemeService`.
- **Hyper-V access** goes through Windows PowerShell 5.1:
  - scripts run in a pool of up to 3 warm `powershell.exe` workers (Hyper-V module imported once), streamed over stdin, with cancellation and timeouts;
  - errors are translated into the UI language;
  - every value is escaped by a single helper (`PowerShellRunner.Esc`).

---

## Troubleshooting

| Symptom | What to check |
|---|---|
| Red banner "Hyper-V unavailable" | Enable Hyper-V (`Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All`, or `.\Install.ps1 -EnableHyperV`), then restart. If the `vmms` service is stopped, use the banner's **Start service** button. |
| Error banner on a page | The message is the error returned by PowerShell or Hyper-V (e.g. switch name already used, file locked by a running VM). |
| The app does not start | Look in `%LocalAppData%\HV-LAB\logs` and in the Windows *Application* event log. Reinstall with `Install.ps1`. |
| "Access denied" | The app must run as administrator (it requests elevation at startup). |

---

## Author & support

- **Author**: [JM2K69](https://github.com/JM2K69)
- **Issues**: [GitHub Issues](https://github.com/JM2K69/HV-LAB/issues)

© 2026 JM2K69
