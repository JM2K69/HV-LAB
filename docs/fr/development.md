# Développement

## Compiler les sources

Il faut le **SDK .NET 10**. Visual Studio n'est pas nécessaire.

```powershell
git clone https://github.com/JM2K69/HV-LAB.git
cd HV-LAB

dotnet test HVLab.Core.Tests                                        # tests unitaires
dotnet build HVLab\HVLab.csproj -c Debug -p:Platform=x64            # build de débogage
dotnet publish HVLab\HVLab.csproj -p:PublishProfile=HVLab-Release   # -> HVLab\publish
.\Install.ps1                                                       # en admin : installe HVLab\publish
```

!!! note
    Dans `HVLab\`, un `dotnet build` sans argument prend la solution `HVLab.slnx`. Avec `-p:Platform=x64`, indiquez explicitement `HVLab.csproj`.

## Structure du projet

```text
HV-LAB/
├── HVLab/                  Application WinUI 3 (vues, fenêtre, services UI)
├── HVLab.Core/             Code indépendant de l'UI (testé unitairement)
│   ├── Models/             VirtualMachine, VirtualSwitch, BaseVhdx, NatNetwork, LabCatalog…
│   ├── Services/           PowerShellRunner, HyperVService, VhdxService, NatService,
│   │                       AnswerFileGenerator, InputValidator, LocalizationService (FR/EN)…
│   └── ViewModels/         Un par page, plus des bases communes
├── HVLab.Core.Tests/       Tests xUnit
├── docs/                   Cette documentation (Zensical, anglais et français)
├── Install.ps1             Installation / désinstallation
└── Directory.Build.props   Version de l'application
```

## Architecture

- **MVVM** avec CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`). Les view models sont dans `HVLab.Core`, qui ne référence pas WinUI et est testé unitairement.
- **Injection de dépendances** avec Microsoft.Extensions.DependencyInjection : `IHyperVService`, `IVhdxService`, `INatService`, `IPowerShellRunner`, `IDialogService` et `IThemeService`.
- **Accès à Hyper-V** via Windows PowerShell 5.1 :
    - les scripts tournent dans un pool de 3 `powershell.exe` maximum gardés ouverts, avec le module Hyper-V importé une seule fois ;
    - les scripts passent par l'entrée standard et ne sont jamais écrits dans `%TEMP%` ;
    - les opérations gèrent l'annulation et les délais ;
    - toutes les valeurs sont échappées par un seul helper, `PowerShellRunner.Esc` ;
    - les erreurs sont traduites dans la langue de l'interface.
- **Localisation** : chaque texte de l'interface existe en français et en anglais dans `LocalizationService`. Un test unitaire échoue si une clé manque dans une langue.

## Publier une release

La version a une source unique : `VersionPrefix` dans `Directory.Build.props`.

1. Modifiez `VersionPrefix`, commitez et poussez.
2. Poussez un tag correspondant :

    ```powershell
    git tag -a v1.27.0 -m "HV-LAB Manager 1.27.0"
    git push origin v1.27.0
    ```

    Un tag du type `v1.27.0-beta.1` publie une pré-version.

Le workflow **Release** se charge ensuite de :

- lancer les tests ;
- publier la version autonome ;
- créer la release GitHub avec `HV-LAB-X.Y.Z-win-x64.zip` et son SHA256.

Il refuse un tag qui ne correspond pas à `VersionPrefix`.

## Documentation

Ce site est construit avec [Zensical](https://zensical.org). Zensical ne construit qu'une langue par projet, donc il y a deux sites côte à côte :

| Langue | Configuration | Pages | Publié sur |
|---|---|---|---|
| Anglais | `docs/zensical.en.toml` | `docs/en/` | `/HV-LAB/en/` |
| Français | `docs/zensical.fr.toml` | `docs/fr/` | `/HV-LAB/fr/` |

`docs/index.html` envoie les visiteurs de `/HV-LAB/` vers la langue de leur navigateur. Les deux sites utilisent les mêmes noms de fichiers, donc le sélecteur de langue peut passer d'une traduction à l'autre de la même page. Quand vous ajoutez ou modifiez une page, mettez à jour les deux langues.

Zensical n'est pas installé en local : seul le workflow **Docs** (`.github/workflows/docs.yml`) construit le site. Il construit les deux sites en mode strict :

- à chaque pull request qui touche `docs/` ;
- à chaque push sur `master` qui touche `docs/`, après quoi il publie aussi les sites sur GitHub Pages.
