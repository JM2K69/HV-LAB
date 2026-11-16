# Blueprints de lab

Un blueprint décrit un lab entier : son commutateur, son réseau NAT, son domaine et ses VM. Vous le composez dans la page **Labs** avec un formulaire, sans écrire de JSON : HV-LAB génère le fichier, puis déploie le lab d'un coup.

Ouvrez la page **Labs** dans le menu de gauche, ou avec le bouton **Labs** de la page **Machines Virtuelles**. Les blueprints sont rangés dans `<dossier des VM>\Blueprints` ; **Ouvrir le dossier** l'affiche dans l'Explorateur.

## Composer un lab

1. Cliquez sur **Nouveau lab**. Le formulaire part d'un exemple : un contrôleur de domaine, un serveur membre et un serveur Linux, sur les images de base les plus récentes de l'hôte.
2. Carte **Lab** : nom du lab (il devient le nom du groupe Hyper-V), mot de passe, langue et fuseau horaire.
3. Carte **Réseau** : le commutateur, à choisir dans la liste ou à saisir (un nom inconnu est créé en commutateur interne), et le NAT créé avec lui, avec sa passerelle et son préfixe.
4. Carte **Domaine Active Directory** : le domaine, le compte autorisé à joindre et l'unité d'organisation. La VM cochée **Contrôleur** crée le domaine ; seules les VM cochées **Domaine** le rejoignent.
5. Carte **Machines virtuelles**, une ligne par VM :
    - **Nom** et **Image de base**, choisie parmi les images de l'hôte ; `*` remplace n'importe quoi et la version la plus récente est prise ;
    - **RAM (Mo)**, **vCPU**, **IP fixe** et **DNS** ;
    - **Contrôleur** et **Domaine** pour une VM Windows, **Utilisateur** pour une VM Linux ;
    - **Étiquettes**, séparées par des virgules, écrites dans les notes de la VM ;
    - pour une VM Linux, les **commandes au premier démarrage**, une par ligne ;
    - les flèches changent l'ordre de création, la corbeille retire la VM ;
    - **Ajouter une VM** ajoute une ligne.
6. Cliquez sur **Enregistrer**. Un lab non enregistré est signalé par **Modifications non enregistrées**, et HV-LAB demande avant de les abandonner.

Le panneau **JSON généré** montre en direct le fichier que le formulaire produit, mot de passe masqué. **Ouvrir le JSON** l'ouvre dans le Bloc-notes, si vous préférez le texte.

Un brouillon peut être enregistré sans mot de passe ; seul le déploiement l'exige.

## Vérifier et déployer

**Vérifier le plan** compare le lab à l'hôte :

- **sera créé** : la ligne n'existe pas encore ;
- **existe déjà** : elle reste inchangée ;
- **impossible** : par exemple quand aucune image de base ne correspond.

**Déployer** enregistre le lab, affiche le plan, puis demande confirmation. HV-LAB crée seulement ce qui manque : le commutateur et son NAT, le [contrôleur de domaine](#le-controleur-de-domaine) s'il y en a un, puis les autres VM dans l'ordre des lignes. Il place ensuite les VM dans un groupe Hyper-V nommé d'après le lab.

Quand une ligne est **impossible**, rien n'est déployé. Déployer à nouveau le même lab crée seulement ce qui manque, par exemple une VM que vous avez supprimée.

!!! note "Les VM ne sont pas démarrées"
    Seul le contrôleur de domaine reste démarré après le déploiement. Sélectionnez les VM du lab (le filtre **Groupe** les affiche), puis utilisez **Démarrer en séquence** : elles démarrent une à une, par ordre de nom. Voir [Plusieurs VM à la fois](virtual-machines.md#plusieurs-vm-a-la-fois).

## Exporter des VM en blueprint

**Exporter en blueprint** se trouve à deux endroits de la page **Machines Virtuelles** :

- dans la barre qui s'affiche quand plusieurs VM sont sélectionnées : il exporte la sélection ;
- dans le menu **⋯** d'une ligne, ou son menu du clic droit : il exporte cette VM, ou les VM sélectionnées quand la ligne fait partie d'une sélection multiple.

HV-LAB écrit `<dossier des VM>\Blueprints\<nom>.json`, nommé d'après le groupe des VM, sinon d'après le début commun de leurs noms ; un fichier existant n'est jamais écrasé (`<nom>-2.json`…). La page **Labs** s'ouvre ensuite sur ce lab : son commutateur, son NAT et, pour chaque VM, son image de base, sa mémoire, ses processeurs, son IP fixe et ses étiquettes. Renseignez le mot de passe avant de le déployer.

## Format du fichier

Les commentaires (`//`) et les virgules finales sont acceptés.

```json
// A domain controller, a member server and a Linux web server on a NAT
{
  "name": "LAB-AD",
  "switch": "LAB-NAT",
  "nat": { "gateway": "192.168.100.1", "prefixLength": 24 },
  "password": "Lab#2026",
  "domain": { "name": "lab.local", "user": "Administrator" },
  "vms": [
    { "name": "DC01",  "base": "WindowsServer2025*", "memoryMB": 4096, "ip": "192.168.100.10", "domainController": true },
    { "name": "SRV01", "base": "WindowsServer2025*", "ip": "192.168.100.11", "join": true },
    { "name": "WEB01", "base": "LinuxAlma*", "ip": "192.168.100.20", "user": "web" }
  ]
}
```

### Le lab

| Champ | Contenu | Par défaut |
|---|---|---|
| `name` | Nom du lab, et de son groupe Hyper-V | Obligatoire |
| `switch` | Commutateur de toutes les VM : utilisé s'il existe, sinon créé en commutateur interne | Obligatoire |
| `nat` | `gateway` et `prefixLength` : réseau NAT créé avec le commutateur. Sa passerelle est aussi celle des IP fixes. | Aucun |
| `password` | Mot de passe Administrateur des VM Windows, mot de passe de l'utilisateur des VM Linux, et mot de passe du compte qui joint le domaine | Obligatoire |
| `language` | Langue et clavier des VM | `fr-FR` |
| `timeZone` | Fuseau horaire des VM | `Romance Standard Time` |
| `domain` | `name`, `user` (le compte autorisé à joindre, `Administrator` par défaut) et `ou` (facultatif) : le domaine que rejoignent les VM qui ont `join` | Aucun |
| `vms` | Les VM, décrites ci-dessous | Obligatoire |

Le NAT n'est créé qu'avec le commutateur. Quand le commutateur existe déjà, HV-LAB l'utilise tel quel.

### Chaque VM

| Champ | Contenu | Par défaut |
|---|---|---|
| `name` | Nom de la VM | Obligatoire |
| `base` | Motif de l'identifiant d'OS de la base, avec `*` : l'image `BASE_*` la plus récente qui correspond est utilisée. Par exemple `"WindowsServer2025*"` ou `"LinuxAlma*"`. | Obligatoire |
| `memoryMB` | Mémoire, en Mo | `2048` |
| `cpu` | Processeurs | `2` |
| `ip` | Adresse IPv4 fixe. Elle nécessite `nat`. | DHCP |
| `dns` | Serveurs DNS, séparés par des virgules | Le contrôleur de domaine du lab s'il y en a un, sinon la passerelle du NAT |
| `domainController` | Windows Server : créé en premier et promu contrôleur du domaine de `domain` (un seul par lab) | `false` |
| `join` | Windows : rejoindre le domaine de `domain` pendant l'installation | `false` |
| `user` | Linux : l'utilisateur sudo | `labadmin` |
| `tags` | [Étiquettes](virtual-machines.md#plusieurs-vm-a-la-fois) de la VM, par exemple `["web", "prod"]` | Aucune |
| `commands` | Linux : [commandes au premier démarrage](linux.md), par exemple `["dnf -y install nginx"]` | Aucune |

L'identifiant d'OS est la partie du nom de fichier qui suit `BASE_`, par exemple `WindowsServer2025Standard(DesktopExperience)` ou `LinuxAlmaLinux`. Une VM Windows est créée comme avec [Créer une VM](create-vm.md), une VM Linux comme un [clone Linux](linux.md).

## Le contrôleur de domaine

Cochez **Contrôleur** sur une VM Windows Server en IP fixe (`"domainController": true` dans le fichier). Au déploiement, HV-LAB :

1. crée cette VM avant les autres, quelle que soit sa ligne ;
2. la démarre et attend la fin de l'installation de Windows ;
3. installe AD DS et crée une nouvelle forêt avec son serveur DNS, par [PowerShell Direct](virtual-machines.md#automatisation-invites-windows) ;
4. la redémarre et attend que le domaine réponde ;
5. crée ensuite les autres VM : celles cochées **Domaine** rejoignent le domaine à leur premier démarrage.

Comptez 10 à 15 minutes pour un contrôleur et ses membres. Les autres VM du lab utilisent le contrôleur comme serveur DNS, sauf si leur champ **DNS** est rempli.

| Élément | Valeur |
|---|---|
| Nom NetBIOS | La première partie du domaine en majuscules : `lab.local` donne `LAB` |
| Compte administrateur du domaine | `LAB\Administrator`, avec le mot de passe du lab |
| Mot de passe du mode restauration (DSRM) | Le mot de passe du lab |

Avec un contrôleur, le mot de passe du lab doit respecter la règle de Windows : 8 caractères ou plus, avec 3 de ces 4 types : majuscules, minuscules, chiffres, symboles.

Quand le contrôleur existe déjà, un nouveau déploiement qui crée des membres vérifie d'abord son rôle : il le démarre au besoin, le promeut s'il ne l'est pas encore, puis attend le domaine. Un membre déjà créé sans avoir joint le domaine reste inchangé : supprimez-le d'abord, ou joignez-le à la main.
