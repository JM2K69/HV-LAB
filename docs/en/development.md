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
- **Hyper-V access** goes through Windows PowerShell 5.1:
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
    git tag -a v1.27.0 -m "HV-LAB Manager 1.27.0"
    git push origin v1.27.0
    ```

    A tag like `v1.27.0-beta.1` publishes a pre-release.

The **Release** workflow then:

- runs the tests;
- publishes the self-contained folder;
- creates the GitHub release with `HV-LAB-X.Y.Z-win-x64.zip` and its SHA256.

It refuses a tag that does not match `VersionPrefix`.

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
