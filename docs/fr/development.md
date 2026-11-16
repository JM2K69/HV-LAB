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

### Tests

| Niveau | Comment | Ce qui est vérifié |
|---|---|---|
| Unitaires | `dotnet test HVLab.Core.Tests` | View models, générateurs, validation, et chaque script PowerShell généré, passé au parseur de Windows PowerShell 5.1 avec des valeurs hostiles (apostrophes, `$()`, accents graves). |
| Live, lecture seule | `HVLAB_LIVE=1`, en admin | Résultats WMI comparés aux cmdlets Hyper-V sur l'hôte. Rien n'est modifié. |
| Live, modifie l'hôte | `HVLAB_LIVE=2`, en admin | Les tests en lecture seule, plus des allers-retours sur de vrais objets : alimentation, paramètres, points de contrôle et suppression par GUID, homonyme refusé, NAT hors /24, changement de type de commutateur. Il ne crée et ne supprime que ses propres VM, commutateurs et NAT `HVLT-*`, même si un test échoue. |
| Live, démarre des invités | `HVLAB_LIVE=3`, en admin, une image `BASE_Windows*` dans le dossier des images de base | Les deux niveaux précédents, plus un lab déployé depuis vos bases : commutateur interne, NAT, une VM Windows et, s'il existe une base Linux, une VM Linux, en IP fixe. Il vérifie le groupe, les réglages de lab et la MAC statique, PowerShell Direct et la copie d'un fichier dans l'invité Windows (nom, IP, passerelle, DNS), le port SSH de l'invité Linux sur son IP fixe, un point de contrôle du lab et l'export en blueprint. Un second lab promeut un contrôleur de domaine, et son membre doit rejoindre le domaine. Une création annulée pendant l'injection de l'unattend ne doit rien laisser. Environ 20 minutes. |

```powershell
# Dans un PowerShell administrateur (3 = tous les niveaux ; HVLAB_LIVE_BASES = un autre dossier de bases)
$env:HVLAB_LIVE = '3'; dotnet test HVLab.Core.Tests --filter "FullyQualifiedName~HyperVLive"
```

### Intégration continue

- **CI** (chaque push sur `master` et chaque pull request) : build x64 avec les avertissements en erreurs, tests unitaires, et chaque `*.ps1` passé au parseur de Windows PowerShell 5.1.
- **Docs** : construit et publie ce site.
- **Release** (tags) : voir ci-dessous.

Les actions sont épinglées par SHA de commit et tenues à jour par Dependabot (`.github/dependabot.yml`), avec les paquets NuGet.

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
- **Injection de dépendances** avec Microsoft.Extensions.DependencyInjection : `IHyperVService`, `IVhdxService`, `INatService`, `IPowerShellRunner`, `IDialogService` et `IThemeService`. `IHyperVService` regroupe quatre parties, servies par la même instance : `IHostService` (hôte), `IVmService` (VM), `IGuestService` (écran, clavier, PowerShell Direct) et `INetworkService` (commutateurs, cartes, partage de connexion). Un view model qui n'a besoin que d'une partie ne prend que celle-là. Un test unitaire vérifie que chaque view model se résout.
- **Opérations Hyper-V fréquentes** via WMI depuis C# (`System.Management`, `HyperVWmi`) : liste des VM avec CPU, mémoire, IP et notes en direct, actions d'alimentation, miniature de l'écran, clavier, liste des points de contrôle, commutateurs et réseaux NAT. Une requête prend quelques dizaines de millisecondes, contre plus d'une seconde pour `Get-VM`.
- **Opérations lourdes et rares** (images de base, création des VM, disques, paramètres, points de contrôle et groupes) via Windows PowerShell 5.1 :
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
    git tag -a v2.1.0 -m "HV-LAB Manager 2.1.0"
    git push origin v2.1.0
    ```

    Un tag du type `v2.1.0-beta.1` publie une pré-version.

Le workflow **Release** se charge ensuite de :

- lancer les tests ;
- publier la version autonome ;
- signer une attestation de provenance du zip (`actions/attest-build-provenance`) ;
- créer la release GitHub avec `HV-LAB-X.Y.Z-win-x64.zip` et son SHA256.

Il refuse un tag qui ne correspond pas à `VersionPrefix`. Le job de build ne peut écrire que l'attestation ; seul le job de release peut écrire dans le dépôt.

Le zip de la release n'est pas signé : sur une autre machine, l'invite UAC affiche *Éditeur inconnu*. Sur votre machine, `Install.ps1 -SelfSign` signe l'exe installé (voir [Installation](install.md)).

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
