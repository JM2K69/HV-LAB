# HV-LAB Manager

> 🇬🇧 [English version](README.md)

Application de bureau **WinUI 3 / .NET 10** pour monter et faire tourner des laboratoires Microsoft Hyper-V sous Windows : images de base, VM en disque différentiel avec installation automatisée, commutateurs virtuels et NAT.

![Plateforme](https://img.shields.io/badge/plateforme-Windows%2010%2F11%20%7C%20Server-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![WinUI](https://img.shields.io/badge/UI-WinUI%203%20(App%20SDK%201.8)-blueviolet)
[![Release](https://img.shields.io/github/v/release/JM2K69/HV-LAB)](https://github.com/JM2K69/HV-LAB/releases/latest)

---

## Fonctionnalités

- **Tableau de bord** : VM démarrées et arrêtées, commutateurs et images de base en un coup d'œil. L'environnement Hyper-V (module, service `vmms`) est vérifié au démarrage.
- **Machines virtuelles** :
  - démarrage, arrêt, console (`vmconnect`) et suppression ;
  - les états se rafraîchissent automatiquement toutes les 5 s ;
  - une seule action à la fois par VM, donc un double clic n'a aucun effet.
- **Quick VM** : tailles prédéfinies, disque vierge ou différentiel, Gen 1/2, Secure Boot, PXE, création en série (`PREFIXE-01…nn`).
- **Créer une VM** : disque différentiel issu d'une image de base, avec `unattend.xml` injecté :
  - nom d'ordinateur, mot de passe administrateur (obligatoire) et ouverture de session automatique optionnelle ;
  - langue, fuseau horaire et clé KMS suggérée selon l'OS ;
  - préchargement du cache CBS et désactivation de Windows Update en option ;
  - aperçu en direct du fichier de réponse, avec le mot de passe masqué ;
  - le fichier de réponse est supprimé de la VM à la première ouverture de session.
- **Images de base** : construction de `BASE_*.vhdx` à partir d'un WIM ou ESD, avec choix automatique de l'édition :
  - la construction se fait dans un fichier temporaire, donc une image existante n'est jamais laissée à moitié construite ;
  - remplacer une image existante demande une confirmation ;
  - la progression s'affiche en direct et la construction peut être annulée.
- **Commutateurs & NAT** : commutateurs externes, internes et privés avec VLAN, et réseaux NAT avec passerelle IPv4.
- **Interface Fluent** :
  - fond Mica, thème clair/sombre et volet de navigation adaptatif ;
  - la taille et la position de la fenêtre sont mémorisées ;
  - états vides et bandeaux d'erreur ;
  - noms accessibles sur les boutons, F5 pour actualiser, Suppr pour supprimer la sélection.
- **Interface français / anglais**, changement de langue instantané.

Toutes les opérations longues sont annulables. Les saisies sont validées avant d'atteindre PowerShell, et aucun script n'est écrit dans `%TEMP%`.

---

## Installation

**Prérequis** : Windows 10/11 Pro, Entreprise ou Éducation, ou Windows Server, avec Hyper-V. L'application s'exécute en administrateur. Rien d'autre à installer : la version publiée est autonome (.NET 10 et Windows App SDK inclus).

1. Téléchargez `HV-LAB-X.Y.Z-win-x64.zip` depuis la [dernière release](https://github.com/JM2K69/HV-LAB/releases/latest) et extrayez-le.
2. Dans un PowerShell **administrateur**, depuis le dossier extrait :
   ```powershell
   Unblock-File .\Install.ps1
   .\Install.ps1                 # options : -DesktopShortcut  -EnableHyperV
   ```
   L'application est copiée dans `C:\Program Files\HV-LAB`, avec un raccourci dans le menu Démarrer.
3. Pour désinstaller :
   ```powershell
   & "C:\Program Files\HV-LAB\Install.ps1" -Uninstall
   ```
   Les paramètres et journaux de `%LocalAppData%\HV-LAB` sont conservés.

---

## Compiler les sources

Nécessite le **SDK .NET 10**. Visual Studio n'est pas nécessaire.

```powershell
git clone https://github.com/JM2K69/HV-LAB.git
cd HV-LAB

dotnet test HVLab.Core.Tests                                   # tests unitaires
dotnet build HVLab\HVLab.csproj -c Debug -p:Platform=x64       # build de débogage
dotnet publish HVLab\HVLab.csproj -p:PublishProfile=HVLab-Release   # -> HVLab\publish
.\Install.ps1                                                  # en admin : installe HVLab\publish
```

> Dans `HVLab\`, un `dotnet build` sans argument prend la solution `HVLab.slnx`. Avec `-p:Platform=x64`, indiquez explicitement `HVLab.csproj`.

### Publier une release

La version a une source unique : `VersionPrefix` dans `Directory.Build.props`. Les versions d'assembly, de fichier et du manifeste, ainsi que celle affichée dans l'app, en découlent.

1. Modifiez `VersionPrefix`, commitez et poussez.
2. Poussez un tag correspondant :
   ```powershell
   git tag -a v1.27.0 -m "HV-LAB Manager 1.27.0"
   git push origin v1.27.0
   ```
   Un tag du type `v1.27.0-beta.1` publie une pré-version.

Le [workflow Release](.github/workflows/release.yml) se charge ensuite de :
- lancer les tests ;
- publier la version autonome ;
- créer la release GitHub avec `HV-LAB-X.Y.Z-win-x64.zip` et son SHA256.

Il refuse un tag qui ne correspond pas à `VersionPrefix`. Lancé à la main (Actions > Release > Run workflow), il produit seulement le zip en artefact.

---

## Structure du projet

```
HV-LAB/
├── HVLab/                      Application WinUI 3 (vues, fenêtre, services UI)
│   ├── Views/                  Tableau de bord, VM, Quick VM, Créer une VM, images de base, commutateurs & NAT, paramètres
│   ├── Services/               DialogService, ThemeService, LocalizationHelper (côté UI)
│   ├── Helpers/                WindowPlacement (DPI, taille minimale, restauration), AppInfo, A11y, sélecteur de dossier
│   ├── Converters/
│   ├── App.xaml(.cs)           Conteneur DI, démarrage, journalisation des exceptions
│   └── MainWindow.xaml(.cs)    Barre de titre, navigation, bandeau d'état Hyper-V
├── HVLab.Core/                 Code indépendant de l'UI (testé unitairement)
│   ├── Models/                 VirtualMachine, VirtualSwitch, BaseVhdx, NatNetwork, LabCatalog…
│   ├── Services/               PowerShellRunner, HyperVService, VhdxService, NatService,
│   │                           AnswerFileGenerator, InputValidator, LocalizationService (FR/EN)…
│   └── ViewModels/             Un par page + bases communes (PageViewModelBase, VmCreationViewModelBase)
├── HVLab.Core.Tests/           Tests xUnit
├── Install.ps1                 Installation / désinstallation (Program Files + raccourcis)
├── Directory.Build.props       Version de l'application
└── .github/workflows/          Workflow de release
```

**Architecture** :
- **MVVM** avec CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- **Injection de dépendances** avec Microsoft.Extensions.DependencyInjection : `IHyperVService`, `IVhdxService`, `INatService`, `IPowerShellRunner`, `IDialogService` et `IThemeService`.
- **Accès à Hyper-V** via Windows PowerShell 5.1 :
  - les scripts tournent dans un pool de 3 `powershell.exe` maximum gardés ouverts (module Hyper-V importé une seule fois), via l'entrée standard, avec annulation et délais ;
  - les erreurs sont traduites dans la langue de l'interface ;
  - toutes les valeurs sont échappées par un seul helper (`PowerShellRunner.Esc`).

---

## Résolution de problèmes

| Symptôme | À vérifier |
|---|---|
| Bandeau rouge « Hyper-V indisponible » | Activez Hyper-V (`Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All`, ou `.\Install.ps1 -EnableHyperV`), puis redémarrez. Si le service `vmms` est arrêté, utilisez le bouton **Démarrer le service** du bandeau. |
| Bandeau d'erreur sur une page | Le message est l'erreur renvoyée par PowerShell ou Hyper-V (ex. : nom de commutateur déjà utilisé, fichier verrouillé par une VM démarrée). |
| L'application ne démarre pas | Consultez `%LocalAppData%\HV-LAB\logs` et le journal Windows *Application*. Réinstallez avec `Install.ps1`. |
| « Accès refusé » | L'application doit tourner en administrateur (elle demande l'élévation au démarrage). |

---

## Auteur & support

- **Auteur** : [JM2K69](https://github.com/JM2K69)
- **Issues** : [GitHub Issues](https://github.com/JM2K69/HV-LAB/issues)

© 2026 JM2K69
