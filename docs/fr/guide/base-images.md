# Images de base

Une image de base est une installation Windows généralisée dans un fichier `BASE_*.vhdx`. Les VM sont ensuite créées en **disque différentiel** sur ce fichier. Chaque VM ne stocke que ses propres modifications, donc créer une VM prend quelques secondes.

La page **Images de base VHDX** liste les images du dossier des images de base (voir [Paramètres](settings.md)). Pour chaque image, la liste affiche l'OS, la version, l'édition, la taille et la date. Sous la liste se trouve le formulaire de construction d'une nouvelle image.

## Construire une image

### 1 — Fichier WIM / ESD source

1. Montez un ISO Windows en double-cliquant dessus dans l'Explorateur.
2. Cliquez sur **Sélectionner…** et choisissez le fichier image de l'ISO monté :
    - `sources\install.wim` ;
    - ou `sources\install.esd`.
3. Cliquez sur **Analyser**.

La liste **Images WIM** affiche alors chaque index du fichier, par exemple *Windows Server 2022 Standard (Desktop Experience)*.

### 2 — Système d'exploitation

Après l'analyse, HV-LAB remplit le formulaire pour vous :

- la **famille OS**, d'après le nom des images analysées : `Windows11`, `Windows10`, `WindowsServer2025`, `WindowsServer2022`, `WindowsServer2019` ou `WindowsServer2016` ;
- l'**édition**, que vous pouvez changer :
    - serveur : `Standard`, `Datacenter`, `StandardCore` ou `DatacenterCore` ;
    - client : `Pro`, `Enterprise`, `Education`, `ProEducation` ou `ProWorkstation` ;
- **Desktop Experience** pour les éditions serveur qui ne sont pas Core ;
- l'**index WIM** qui correspond à l'édition choisie, avec un ✓ quand il est trouvé ;
- la **version**, par exemple `10.0.20348.587`. Si elle n'a pas pu être détectée, saisissez-la à la main.

### 3 — Disque & destination

| Champ | Par défaut | Remarques |
|---|---|---|
| Génération | 2 | La génération 2 construit un disque GPT/UEFI, la génération 1 un disque MBR/BIOS. Les VM créées depuis l'image doivent utiliser la même génération. |
| Taille (Go) | 50 | Taille maximale du VHDX dynamique. Le fichier ne grossit qu'au fur et à mesure de l'écriture. |
| Dossier de destination | dossier des images de base | |

L'**aperçu du nom de fichier** montre le nom du fichier qui sera construit, par exemple :

```text
BASE_WindowsServer2022Standard(DesktopExperience)_10.0.20348.587_50.vhdx
```

Cliquez sur **Créer l'image de base**. La progression s'affiche en direct, et la construction peut être annulée.

## Déroulement de la construction

1. Un VHDX dynamique est créé dans un **fichier temporaire** et partitionné selon la génération choisie.
2. L'image Windows est appliquée avec DISM, ce qui prend quelques minutes.
3. Les fichiers de démarrage sont écrits.
4. Le fichier temporaire est renommé avec son nom final `BASE_*.vhdx`.

La construction est silencieuse :

- les volumes sont montés sur des dossiers privés sous `%ProgramData%\HV-LAB`, jamais sur une lettre de lecteur ;
- aucune fenêtre de l'Explorateur ne s'ouvre.

Une image existante n'est jamais laissée à moitié construite. Si un fichier du même nom existe déjà, HV-LAB demande une confirmation avant de le remplacer.

!!! info "Image en état sysprep"
    L'image de base est construite **sans fichier de réponse** et reste généralisée. Le `unattend.xml` est injecté dans le disque différentiel de chaque VM au moment de sa création. Voir [Créer une VM](create-vm.md).

!!! danger "Ne modifiez jamais une image de base utilisée par des VM"
    Un disque différentiel dépend du contenu exact de son parent. Si vous démarrez une VM directement sur une image de base, ou si vous la remplacez, toutes les VM construites dessus deviennent inutilisables. HV-LAB vous prévient avant de remplacer une image.

## En cas d'échec

Le bandeau d'erreur donne la raison et le chemin du journal de construction, qui contient le détail. Causes fréquentes :

- l'ISO a été démonté pendant la construction ;
- le disque de destination est plein ;
- le fichier de destination est verrouillé, par exemple par une VM démarrée.

## Supprimer une image

Utilisez le bouton de suppression sur la ligne de l'image et confirmez. Supprimez d'abord les VM qui en dépendent.
