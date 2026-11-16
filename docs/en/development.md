# Development

## Build from source

You need the **.NET 10 SDK**. Visual Studio is not required.

```powershell
git clone https://github.com/JM2K69/HV-LAB.git
cd HV-LAB

dotnet test HVLab.Core.Tests                                        # unit tests
dotnet build HVLab\HVLab.csproj -c Debug -p:Platform=x64            # debug build
dotnet publish HVLab\HVLab.csproj -p:PublishProfile=HVLab-Release   # -> HVLab\publish
.\Install.ps1                                                       # elevated: installs HVLab\publish
```

!!! note
    In `HVLab\`, a bare `dotnet build` picks up `HVLab.slnx`. Pass `HVLab.csproj` explicitly together with `-p:Platform=x64`.

### Tests

| Level | How | What it checks |
|---|---|---|
| Unit | `dotnet test HVLab.Core.Tests` | View models, generators, validation, and every generated PowerShell script parsed by Windows PowerShell 5.1 with hostile values (quotes, `$()`, backticks). |
| Live, read-only | `HVLAB_LIVE=1`, elevated | WMI results compared with the Hyper-V cmdlets on the host. Nothing is changed. |
| Live, changes the host | `HVLAB_LIVE=2`, elevated | The read-only tests, plus round trips on real objects: power, settings, checkpoints and delete by GUID, a refused namesake, a NAT outside a /24, a switch type change. It creates and deletes only its own `HVLT-*` VMs, switches and NAT, even when a test fails. |

```powershell
# In an elevated PowerShell
$env:HVLAB_LIVE = '2'; dotnet test HVLab.Core.Tests --filter "FullyQualifiedName~HyperVLive"
```

### Continuous integration

- **CI** (every push to `master` and every pull request): x64 build with warnings as errors, unit tests, and every `*.ps1` parsed by Windows PowerShell 5.1.
- **Docs**: builds and publishes this site.
- **Release** (tags): see below.

The actions are pinned by commit SHA and kept current by Dependabot (`.github/dependabot.yml`), together with the NuGet packages.

## Project structure

```text
HV-LAB/
├── HVLab/                  WinUI 3 application (views, shell, UI services)
├── HVLab.Core/             UI-independent code (unit-tested)
│   ├── Models/             VirtualMachine, VirtualSwitch, BaseVhdx, NatNetwork, LabCatalog…
│   ├── Services/           PowerShellRunner, HyperVService, VhdxService, NatService,
│   │                       AnswerFileGenerator, InputValidator, LocalizationService (FR/EN)…
│   └── ViewModels/         One per page, plus shared bases
├── HVLab.Core.Tests/       xUnit tests
├── docs/                   This documentation (Zensical, English and French)
├── Install.ps1             Install / uninstall
└── Directory.Build.props   Application version
```

## Architecture

- **MVVM** with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`). The view models live in `HVLab.Core`, which has no WinUI reference and is unit-tested.
- **Dependency injection** with Microsoft.Extensions.DependencyInjection: `IHyperVService`, `IVhdxService`, `INatService`, `IPowerShellRunner`, `IDialogService` and `IThemeService`.
- **Frequent Hyper-V operations** go through WMI from C# (`System.Management`, `HyperVWmi`): VM list with live CPU, memory, IP and notes, power actions, screen thumbnail, keyboard, checkpoint list, switches and NAT networks. A query takes a few tens of milliseconds, where `Get-VM` takes over a second.
- **Heavy, rare operations** (base images, VM creation, disks, settings, checkpoint and group changes) go through Windows PowerShell 5.1:
    - scripts run in a pool of up to 3 warm `powershell.exe` workers, with the Hyper-V module imported once;
    - scripts are streamed over stdin and are never written to `%TEMP%`;
    - operations support cancellation and timeouts;
    - every value is escaped by a single helper, `PowerShellRunner.Esc`;
    - errors are translated into the UI language.
- **Localization**: every UI string exists in French and English in `LocalizationService`. A unit test fails when a key is missing in one language.

## Release

The version has a single source: `VersionPrefix` in `Directory.Build.props`.

1. Update `VersionPrefix`, then commit and push.
2. Push a matching tag:

    ```powershell
    git tag -a v2.0.0 -m "HV-LAB Manager 2.0.0"
    git push origin v2.0.0
    ```

    A tag like `v2.0.0-beta.1` publishes a pre-release.

The **Release** workflow then:

- runs the tests;
- publishes the self-contained folder;
- creates the GitHub release with `HV-LAB-X.Y.Z-win-x64.zip` and its SHA256.

It refuses a tag that does not match `VersionPrefix`. The build job is read-only; only the release job may write to the repository.

The release zip is not signed: on another machine the UAC prompt shows *Unknown publisher*. On your own machine, `Install.ps1 -SelfSign` signs the installed exe (see [Installation](install.md)).

## Documentation

This site is built with [Zensical](https://zensical.org). Zensical builds one language per project, so there are two sites side by side:

| Language | Configuration | Pages | Published at |
|---|---|---|---|
| English | `docs/zensical.en.toml` | `docs/en/` | `/HV-LAB/en/` |
| French | `docs/zensical.fr.toml` | `docs/fr/` | `/HV-LAB/fr/` |

`docs/index.html` sends visitors of `/HV-LAB/` to the language of their browser. Both sites use the same file names, so the language selector can switch between translations of the same page. When you add or change a page, update both languages.

Zensical is not installed locally: only the **Docs** workflow (`.github/workflows/docs.yml`) builds the site. It builds both sites in strict mode:

- on every pull request that touches `docs/`;
- on every push to `master` that touches `docs/`, after which it also publishes the sites to GitHub Pages.
