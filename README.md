# HV-LAB Manager

> 🇫🇷 [Version française](README.fr.md)

A **WinUI 3 / .NET 10** desktop application to build and run Microsoft Hyper-V lab environments on Windows: base images, differencing-disk VMs with unattended setup, virtual switches and NAT.

📖 **Documentation**: <https://jm2k69.github.io/HV-LAB/en/>, with a step-by-step user guide.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20%7C%20Server-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![WinUI](https://img.shields.io/badge/UI-WinUI%203%20(App%20SDK%201.8)-blueviolet)
[![Release](https://img.shields.io/github/v/release/JM2K69/HV-LAB)](https://github.com/JM2K69/HV-LAB/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

---

## Features

- **Dashboard**: running and stopped VMs, switches and base images at a glance, host capacity (memory and vCPUs of the running VMs, free disk space), with a check of the Hyper-V environment (module, `vmms` service) at startup.
- **Virtual machines**, read through WMI (a few tens of milliseconds, refreshed every 3 s):
  - search (Ctrl+F: name, IP, OS, group, tag), state, group and tag filters;
  - free tags per VM (`web`, `prod`…), kept in the Hyper-V notes, edited on one VM or a selection;
  - live CPU load, memory, uptime, heartbeat and IP address;
  - start, clean shutdown, turn off, save, pause/resume, restart, reset, `vmconnect` (one window per VM), from the row or its right-click menu;
  - a window per VM (double-click) with the live screen, the credentials (copy, or type them into the VM with the right keyboard layout), SSH and Remote Desktop, an enhanced-session badge, and tabs for checkpoints and settings; working in the VM goes through the native Hyper-V console (`vmconnect`);
  - guest automation for Windows VMs: run a PowerShell command (PowerShell Direct, no network needed) or copy a file into the VM;
  - checkpoint tree (create, apply, rename, delete), settings after creation (CPU, nested virtualization, memory, ISO, switch, VLAN);
  - multi-select, Hyper-V VM groups, delete with the VM's own disks (never the base image);
  - lab operations on a selection: start in sequence (by name, each VM waiting for the previous one's heartbeat), one checkpoint for the whole lab;
  - VMs addressed by their Hyper-V GUID, so VMs with the same name are never confused; each VM runs one action at a time.
- **Labs page**: compose a whole lab in a form (switch, NAT, domain, VMs with their base image, size, static IP and domain join); HV-LAB generates the JSON blueprint, shows the plan, and deploys only what is missing, then groups the VMs. A VM ticked as domain controller is created first and promoted (AD DS and DNS) before its members join the domain. Existing VMs export to a blueprint.
- **Quick VM**: size presets, blank disk or differencing disk, Gen 1/2, Secure Boot, PXE, bulk creation (`PREFIX-01…nn`).
- **Create VM**: differencing disk from a base image with an injected `unattend.xml`:
  - computer name, administrator password (required) and optional auto-logon;
  - language, time zone and a KMS key suggested for the OS, or your own key (Insider builds) added in the settings;
  - static IP (on a static MAC) and Active Directory domain join during setup;
  - optional CBS cache warm-up and Windows Update disabling;
  - live preview of the answer file, with the password masked;
  - the answer file is removed from the VM at first logon;
  - the creation date, the account, its password and the IP are written into the VM's Hyper-V notes;
  - lab defaults (no automatic checkpoints, shut down with the host, Guest Service Interface on), a name already used is refused, and a failed or cancelled creation is cleaned up.
- **Base images**: build `BASE_*.vhdx` from a WIM/ESD file, with automatic edition matching:
  - the image is built in a temporary file, so an existing image is never left half-built;
  - overwriting an existing image asks for confirmation;
  - progress is shown live and the build can be cancelled;
  - each base shows the VMs built on it, and a base still in use cannot be deleted.
- **Import a VHDX file** as a base: a sysprepped Windows or a Linux disk, read first (generation, Windows version, sysprep state), then copied; the original is never changed.
- **Linux bases**: a prepared Linux VM (cloud-init) becomes a base, and each clone gets its own host name, user, password and static IP on first boot, then runs the commands you give it (`runcmd`).
- **Switches & NAT**:
  - a **topology** view: each switch with its uplink (adapter, NAT, connection sharing, host, isolated) and its VMs with state, IP and VLAN;
  - External, Internal and Private switches with VLAN, and a type change in place with rollback;
  - NAT networks with an IPv4 gateway, and NAT with DHCP through Windows connection sharing;
  - per-VM VLAN access or trunk, DHCP guard, router guard and MAC spoofing;
  - host network cards on a switch, one per VLAN (`Add-VMNetworkAdapter -ManagementOS`), with their VLAN, IPv4 and DHCP; the switch list shows every host VLAN.
- **Fluent UI**:
  - Mica backdrop, light/dark theme and an adaptive navigation pane;
  - the window position and size are remembered;
  - empty states and error banners;
  - accessible names on buttons, Ctrl+F to search, F5 to refresh, Del to delete the selection.
- **French / English** interface, switchable live.

All long operations can be cancelled, all inputs are validated before reaching PowerShell, and scripts are never written to `%TEMP%`.

Security in a lab:
- the password in the VM notes can be kept encrypted with DPAPI (Settings > Security, off by default);
- a copied password is kept out of the clipboard history and cleared after 30 s;
- the cloud-init seed of a Linux clone holds a SHA-512 hash of the password, not the password;
- SSH and Remote Desktop start as the signed-in user, not as administrator;
- the base images folder is writable by administrators only;
- so are the settings and logs, in `%ProgramData%\HV-LAB`: a program started without elevation cannot change what the elevated app reads.

---

## Install

**Requirements**: Windows 10/11 Pro, Enterprise or Education, or Windows Server, with Hyper-V. The application runs as administrator. Nothing else is needed: the build is self-contained (.NET 10 and Windows App SDK included).

1. Download `HV-LAB-X.Y.Z-win-x64.zip` from the [latest release](https://github.com/JM2K69/HV-LAB/releases/latest) and extract it.
2. In an **elevated** PowerShell, from the extracted folder:
   ```powershell
   Unblock-File .\Install.ps1
   .\Install.ps1                 # options: -DesktopShortcut  -EnableHyperV
   ```
   The app is copied to `C:\Program Files\HV-LAB`, gets a Start menu shortcut and appears in Settings > Apps. An update is prepared in `HV-LAB.new`, then swapped in, so a failed copy leaves the installed version intact.
3. Uninstall from Settings > Apps, or:
   ```powershell
   & "C:\Program Files\HV-LAB\Install.ps1" -Uninstall
   ```
   Settings and logs in `%ProgramData%\HV-LAB` are kept.

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
   git tag -a v2.3.0 -m "HV-LAB Manager 2.3.0"
   git push origin v2.3.0
   ```
   A tag like `v2.3.0-beta.1` publishes a pre-release.

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
- **Dependency injection** with Microsoft.Extensions.DependencyInjection: `IHyperVService` (made of `IHostService`, `IVmService`, `IGuestService` and `INetworkService`, one instance), `IVhdxService`, `INatService`, `IPowerShellRunner`, `IDialogService` and `IThemeService`.
- **Hyper-V access**:
  - WMI (`System.Management`) for the fast and frequent work: VM list, power actions, checkpoints list, switches, NAT, screen preview, typing the password;
  - Windows PowerShell 5.1 for the heavy, rare operations (creation, disks, settings, base images);
  - scripts run in a pool of up to 3 warm `powershell.exe` workers (Hyper-V module imported once), streamed over stdin, with cancellation and timeouts;
  - errors are translated into the UI language;
  - every value is escaped by a single helper (`PowerShellRunner.Esc`).

---

## Troubleshooting

| Symptom | What to check |
|---|---|
| Red banner "Hyper-V unavailable" | Enable Hyper-V (`Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All`, or `.\Install.ps1 -EnableHyperV`), then restart. If the `vmms` service is stopped, use the banner's **Start service** button. |
| Error banner on a page | The message is the error returned by PowerShell or Hyper-V (e.g. switch name already used, file locked by a running VM). |
| The app does not start | Look in `%ProgramData%\HV-LAB\logs` and in the Windows *Application* event log. Reinstall with `Install.ps1`. |
| "Access denied" | The app must run as administrator (it requests elevation at startup). |

---

## Author & support

- **Author**: [JM2K69](https://github.com/JM2K69)
- **Issues**: [GitHub Issues](https://github.com/JM2K69/HV-LAB/issues)

## License

HV-LAB Manager is released under the [MIT License](LICENSE). © 2026 JM2K69
