# Créer une VM

La page **Créer une VM** crée une ou plusieurs VM en disque différentiel sur une [image de base](base-images.md). Un fichier de réponse `unattend.xml` est injecté, et Windows se configure seul au premier démarrage, sans poser de question.

## Le formulaire

### Identité

- **Nom de la VM** : le nom affiché dans Hyper-V. Un nom déjà utilisé par une VM de l'hôte est refusé.
- **Création en lot (Bulk)** : crée plusieurs VM numérotées d'un coup. Indiquez un **préfixe** et un **nombre de VM** :
    - le préfixe `SRV` avec 3 VM donne `SRV-01`, `SRV-02` et `SRV-03` ;
    - l'**aperçu** affiche les noms avant la création ;
    - la série est placée dans un groupe de VM Hyper-V nommé d'après le préfixe (option activée par défaut), qui permet de la filtrer et de la piloter depuis la [liste des VM](virtual-machines.md#plusieurs-vm-a-la-fois).

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
| Clé KMS (GVLK) | Les clés client KMS publiques de Microsoft, précédées de vos [clés personnalisées](settings.md#cles-produit-personnalisees). La clé qui correspond à l'image de base est proposée et recopiée dans **Clé produit**. |
| Clé produit (optionnel) | N'importe quelle clé, appliquée pendant la passe *specialize*. Laissez vide pour ne pas en mettre. |

### Adresse IP fixe

**Adresse IP fixe (sinon DHCP)** donne à la VM une adresse fixe, sur une base Windows comme sur une base [Linux](linux.md). Activée, elle demande :

- l'**Adresse IP (1re VM)** et le **Préfixe (/)** ;
- la **Passerelle** ;
- les **Serveurs DNS**, séparés par des virgules.

Sur une base Windows, la carte réseau de la VM reçoit une adresse MAC fixe (`00-15-5D-…`), et `unattend.xml` règle l'IP, le préfixe, la passerelle et les DNS sur cette carte. En mode lot, chaque VM reçoit l'adresse suivante : `SRV-01` reçoit `192.168.100.10`, `SRV-02` reçoit `192.168.100.11`, et ainsi de suite. L'adresse est écrite dans les [notes](#notes-de-la-vm).

!!! info "Réseaux NAT"
    Les [réseaux NAT](networking.md#reseaux-nat) d'HV-LAB n'ont pas de DHCP. Utilisez une IP fixe, avec la passerelle du NAT comme passerelle.

### Joindre un domaine Active Directory

Sur une base Windows, **Joindre un domaine Active Directory** fait rejoindre un domaine à la VM pendant l'installation, dans la passe *specialize*.

| Champ | Remarques |
|---|---|
| Domaine | Par exemple `lab.local`. |
| Unité d'organisation (facultatif) | Par exemple `OU=Servers,DC=lab,DC=local`. Laissez vide pour le conteneur par défaut. |
| Compte autorisé à joindre | `DOMAINE\utilisateur`, `utilisateur@domaine`, ou un utilisateur du domaine comme `Administrator`. |
| Mot de passe du compte | Le mot de passe de ce compte. |

La VM doit pouvoir contacter un contrôleur de domaine pendant l'installation. Avec une IP fixe, mettez l'adresse du contrôleur de domaine en serveur DNS.

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

## Notes de la VM

HV-LAB écrit la date de création et les identifiants dans les **Notes** Hyper-V de la VM, pour les retrouver plus tard. En mode lot, chaque VM reçoit ses propres notes.

```text
Créée par HV-LAB le 02/11/2026 14:35
Compte : Administrator
Mot de passe : Lab#2026
IP : 192.168.100.10
```

La ligne `IP` n'apparaît qu'avec une [IP fixe](#adresse-ip-fixe). Pour lire les notes :

- dans le Gestionnaire Hyper-V : l'onglet **Résumé** de la VM, ou **Paramètres > Nom** ;
- en PowerShell :

    ```powershell
    (Get-VM SRV-01).Notes
    ```

Sans fichier de réponse, les notes contiennent seulement la date de création.

!!! warning "Usage en labo"
    Par défaut, le mot de passe est stocké en clair dans les notes, et tout administrateur de l'hôte peut le lire. Ne réutilisez jamais un mot de passe de production dans un labo. Pour le garder chiffré, activez **Protéger le mot de passe dans les notes (DPAPI)** dans les [Paramètres](settings.md#securite).

## Déroulement de la création

Cliquez sur **Créer la VM**. En cas de problème, un bandeau rouge à côté du bouton en donne la raison. Quand la VM est prête, un bandeau vert propose **Ouvrir la VM**, qui ouvre sa [fenêtre](virtual-machines.md#fenetre-de-la-vm).

1. Le disque différentiel est créé sur l'image de base.
2. Son volume Windows est monté sur un dossier privé, sans lettre de lecteur, et `unattend.xml` est copié dans `Windows\Panther`.
3. La VM est créée avec les réglages de labo ci-dessous et reliée au commutateur, puis ses [notes](#notes-de-la-vm) sont écrites. Avec une IP fixe, sa carte reçoit l'adresse MAC fixe que nomme `unattend.xml`.
4. Au premier démarrage, le programme d'installation de Windows exécute la passe *specialize* (nom d'ordinateur, IP fixe, jonction au domaine) et la passe *oobeSystem*, puis affiche l'écran de connexion.

Chaque VM créée par HV-LAB, ici ou avec [Quick VM](quick-vm.md), reçoit des réglages de labo :

- aucun point de contrôle automatique ;
- l'action d'arrêt automatique **Arrêter** : la VM s'arrête avec l'hôte au lieu d'enregistrer sa mémoire sur le disque ;
- le service d'intégration *Interface de services d'invité* activé, pour [Copier un fichier vers la VM…](virtual-machines.md#automatisation-invites-windows).

Si la création échoue, dépasse son délai ou est annulée, HV-LAB supprime seulement ce qu'elle a produit : les disques sont démontés, puis la VM et son dossier sont supprimés.

!!! note "Le fichier de réponse est supprimé"
    `unattend.xml` contient le mot de passe en clair. Une commande de première ouverture de session supprime `%WINDIR%\Panther\unattend.xml` de la VM.

!!! tip "Sans fichier de réponse"
    Décochez **Utiliser un fichier de réponse** pour obtenir l'OOBE Windows classique, où vous répondez aux questions d'installation dans la console de la VM.
