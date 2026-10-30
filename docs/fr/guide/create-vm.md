# Créer une VM

La page **Créer une VM** crée une ou plusieurs VM en disque différentiel sur une [image de base](base-images.md). Un fichier de réponse `unattend.xml` est injecté, et Windows se configure seul au premier démarrage, sans poser de question.

## Le formulaire

### Identité

- **Nom de la VM** : le nom affiché dans Hyper-V.
- **Création en lot (Bulk)** : crée plusieurs VM numérotées d'un coup. Indiquez un **préfixe** et un **nombre de VM** :
    - le préfixe `SRV` avec 3 VM donne `SRV-01`, `SRV-02` et `SRV-03` ;
    - l'**aperçu** affiche les noms avant la création.

### Matériel

| Champ | Choix |
|---|---|
| Génération | 1 ou 2, la même que l'image de base |
| Mémoire (Mo) | 512 à 16384 |
| Processeurs | 1 à 16 |

### Réseau et stockage

- **Commutateur virtuel** : le commutateur auquel se connecte la carte réseau de la VM. Voir [Commutateurs & NAT](networking.md).
- **Image de base VHDX** : le parent du disque différentiel.
- **Dossier de destination** : où sont créés la VM et son disque. Par défaut, le dossier des VM défini dans les [Paramètres](settings.md).

## Fichier de réponse (unattend.xml)

Laissez **Utiliser un fichier de réponse** coché pour une installation automatisée.

| Champ | Remarques |
|---|---|
| Nom de l'ordinateur | 15 caractères maximum. En mode lot, chaque VM reçoit son propre nom. |
| Mot de passe Administrateur | **Obligatoire**. Le bouton en forme d'œil l'affiche ou le masque. |
| Langue / Locale | `fr-FR`, `en-US`, `en-GB`, `de-DE`, `es-ES` ou `it-IT`. Définit la langue de l'interface, les paramètres régionaux système et utilisateur, et le clavier. |
| Fuseau horaire | Par exemple `Romance Standard Time` (Paris) ou `Pacific Standard Time`. |
| Clé KMS (GVLK) | Les clés client KMS publiques de Microsoft. La clé qui correspond à l'image de base est proposée et recopiée dans **Clé produit**. |
| Clé produit (optionnel) | N'importe quelle clé, appliquée pendant la passe *specialize*. Laissez vide pour ne pas en mettre. |

### Options

- **Ouverture de session automatique** :
    - ouvre la session Administrateur lors des trois premières connexions ;
    - désactivée par défaut, car Windows stocke alors le mot de passe en clair dans le registre de la VM.
- **Initialiser le cache CBS des fonctionnalités** (Windows Server uniquement). À la première ouverture de session, une fenêtre console :
    1. lance `Get-WindowsFeature` et `Get-WindowsOptionalFeature` ;
    2. nettoie le magasin de composants avec DISM ;
    3. redémarre la VM après 15 secondes.

    Les opérations *Ajouter des rôles et fonctionnalités* suivantes sont ensuite plus rapides.
- **Désactiver les mises à jour automatiques Windows Update** : coupe les mises à jour automatiques dans la VM.

### Aperçu

L'**aperçu unattend.xml** affiche le fichier exactement tel qu'il sera injecté, avec le mot de passe masqué. Cliquez sur **Mettre à jour l'aperçu** après une modification.

## Comptes

HV-LAB **ne crée aucun compte supplémentaire**. Le fichier de réponse définit seulement le mot de passe du compte **Administrateur** intégré :

- sous **Windows Server**, l'installation ne demande plus le mot de passe Administrateur ;
- sous **Windows 10/11**, les écrans de création de compte sont masqués, et le programme d'installation active l'Administrateur intégré.

Connectez-vous en `Administrator` avec le mot de passe saisi.

## Déroulement de la création

1. Le disque différentiel est créé sur l'image de base.
2. Son volume Windows est monté sur un dossier privé, sans lettre de lecteur, et `unattend.xml` est copié dans `Windows\Panther`.
3. La VM est créée et reliée au commutateur.
4. Au premier démarrage, le programme d'installation de Windows exécute les passes *specialize* et *oobeSystem*, puis affiche l'écran de connexion.

!!! note "Le fichier de réponse est supprimé"
    `unattend.xml` contient le mot de passe en clair. Une commande de première ouverture de session supprime `%WINDIR%\Panther\unattend.xml` de la VM.

!!! tip "Sans fichier de réponse"
    Décochez **Utiliser un fichier de réponse** pour obtenir l'OOBE Windows classique, où vous répondez aux questions d'installation dans la console de la VM.
