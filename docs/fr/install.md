# Installation

## Prérequis

- **Windows 10/11** Pro, Entreprise ou Éducation, ou **Windows Server**.
- La fonctionnalité **Hyper-V**, que l'installeur peut activer pour vous.
- Un compte **administrateur**, car l'application demande l'élévation au démarrage.

Rien d'autre à installer : la version publiée est autonome, avec .NET 10 et le Windows App SDK inclus.

## Installer

1. Téléchargez `HV-LAB-X.Y.Z-win-x64.zip` depuis la [dernière release](https://github.com/JM2K69/HV-LAB/releases/latest) et extrayez-le.
2. Ouvrez PowerShell **en administrateur** dans le dossier extrait et lancez :

    ```powershell
    Unblock-File .\Install.ps1
    .\Install.ps1
    ```

L'application est copiée dans `C:\Program Files\HV-LAB`, avec un raccourci dans le menu Démarrer.

!!! tip "Vérifier le téléchargement"
    Chaque release publie aussi un fichier `.sha256`. Comparez-le avec l'empreinte du zip :

    ```powershell
    (Get-FileHash .\HV-LAB-X.Y.Z-win-x64.zip -Algorithm SHA256).Hash
    ```

### Options de l'installeur

| Option | Effet |
|---|---|
| `-DesktopShortcut` | Crée aussi un raccourci sur le Bureau public. |
| `-EnableHyperV` | Active la fonctionnalité Hyper-V si elle est absente. Un redémarrage est nécessaire. |
| `-Destination <dossier>` | Installe ailleurs que dans `C:\Program Files\HV-LAB`. |
| `-Source <dossier>` | Dossier qui contient `HVLab.exe`. Par défaut, le dossier du script, ou `HVLab\publish` depuis les sources. |
| `-Uninstall` | Supprime le dossier d'installation et les raccourcis. |

## Premier démarrage

Au démarrage, l'application :

1. demande l'élévation via l'UAC ;
2. vérifie le module Hyper-V et le service `vmms`.

Si Hyper-V n'est pas disponible, un bandeau rouge en donne la raison. Si le service est seulement arrêté, le bandeau propose un bouton **Démarrer le service**.

Choisissez ensuite vos dossiers dans les [Paramètres](guide/settings.md) :

- le dossier des images de base, `C:\HV-LAB\BaseImages` par défaut ;
- le dossier des VM, `C:\HV-LAB\VMs` par défaut.

## Mettre à jour

Téléchargez la nouvelle release et lancez son `Install.ps1` de la même façon :

- il ferme l'application si elle est ouverte ;
- il remplace les fichiers installés.

Vos paramètres sont conservés, car ils sont dans `%LocalAppData%\HV-LAB`.

## Désinstaller

```powershell
& "C:\Program Files\HV-LAB\Install.ps1" -Uninstall
```

Les paramètres et journaux de `%LocalAppData%\HV-LAB` sont conservés, ainsi que vos VM et vos images. Le désinstalleur ne supprime jamais d'objet Hyper-V ni de fichier VHDX.
