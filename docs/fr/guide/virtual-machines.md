# Tableau de bord & VM

## Tableau de bord

Le tableau de bord donne une vue d'ensemble du labo :

- **Vue d'ensemble** : nombre de VM démarrées et arrêtées, de commutateurs et d'images de base.
- **Capacité de l'hôte** :
    - la mémoire utilisée par les VM démarrées, face à la mémoire libre et totale de l'hôte ;
    - les vCPU des VM démarrées, face aux processeurs logiques de l'hôte ;
    - l'espace libre des disques qui portent le dossier des VM et le dossier des images de base.
- **Démarrage rapide** : la [démarche recommandée](../index.md#demarche-recommandee), du commutateur interne jusqu'aux VM.
- **Configuration système** :
    - le dossier des images de base ;
    - l'état de l'élévation ;
    - la disponibilité d'Hyper-V.

Quand Hyper-V n'est pas disponible, un bandeau en donne la raison. Si le service `vmms` est seulement arrêté, le bandeau propose de le démarrer.

## Machines virtuelles

La page **Machines Virtuelles** liste toutes les VM de l'hôte. Elle lit Hyper-V directement via WMI, en quelques dizaines de millisecondes, et se rafraîchit toutes les 3 secondes.

L'en-tête tient sur deux lignes :

- le titre, le bouton **Labs**, qui ouvre l'éditeur de labs (voir [Blueprints de lab](labs.md)), et **Actualiser** (++f5++) ;
- la zone **Rechercher (Ctrl+F) : nom, IP, OS, groupe**, le filtre d'état et le filtre **Groupe**.

++ctrl+f++ place le curseur dans la zone de recherche. La recherche porte sur le nom, l'adresse IP, le système invité et le groupe de chaque VM. Le filtre d'état affiche les VM **Toutes**, **En cours**, **Arrêtées** ou **Enregistrées ou en pause**. Le rafraîchissement automatique garde la recherche, les filtres et la sélection.

| Colonne | Contenu |
|---|---|
| Nom | La VM, avec son groupe Hyper-V en dessous |
| État | En cours, Arrêtée, Enregistrée, En pause… |
| RAM | Mémoire utilisée (mémoire de démarrage si la VM est arrêtée) |
| CPU | Processeurs, et la charge en % quand la VM tourne |
| Adresse IP | Remontée par l'invité, sinon trouvée dans la table de voisinage de l'hôte, sinon l'IP fixe des notes |
| Commutateur, VLAN | Réseau de la première carte |

Les états, le signal de vie et les types de commutateur s'affichent dans la langue de l'interface.

### Actions

Chaque ligne a trois boutons :

- **Démarrer** ;
- **Arrêter**, un arrêt propre par l'invité ;
- **Connexion vmconnect**, qui ouvre *Connexion à une machine virtuelle* (`vmconnect`). Une seule fenêtre par VM : un second clic ramène au premier plan la fenêtre déjà ouverte. À ne pas confondre avec la [console intégrée](#console-integree), un onglet de la fenêtre de la VM.

Le menu **⋯** regroupe les autres actions. Un clic droit sur la ligne, ou la touche menu, ouvre le même menu.

| Action | Effet |
|---|---|
| Ouvrir la fenêtre de la VM | Voir [Fenêtre de la VM](#fenetre-de-la-vm) |
| Éteindre (forcé) | Coupe l'alimentation, pour une VM bloquée ou encore en installation |
| Redémarrer | Redémarrage propre par l'invité |
| Réinitialiser | Réinitialisation matérielle |
| Enregistrer l'état / Suspendre / Reprendre | États enregistré et en pause d'Hyper-V |
| Console intégrée | Ouvre la fenêtre de la VM sur son onglet **Console** |
| Exporter en blueprint | Écrit cette VM, ou les VM sélectionnées quand la ligne fait partie d'une sélection multiple, dans un [blueprint](labs.md#exporter-des-vm-en-blueprint) |
| Paramètres… | Ouvre la fenêtre de la VM sur son onglet **Paramètres** |
| Supprimer | Voir [Supprimer une VM](#supprimer-une-vm) |

Au plus 4 actions d'alimentation tournent en même temps. Un arrêt qui n'est pas terminé au bout de 2 minutes est signalé comme une erreur : l'invité l'a refusé ou est encore occupé, utilisez alors **Éteindre (forcé)**.

Quand une action échoue, le bandeau rouge en donne la raison. Il reste affiché jusqu'à ce que vous le fermiez ou lanciez une autre action. **Copier les détails** copie la sortie de PowerShell, utile pour [signaler un problème](../troubleshooting.md#signaler-un-probleme).

!!! note "VM de même nom"
    HV-LAB désigne chaque VM par son GUID Hyper-V : suppression, paramètres, points de contrôle, groupes et `vmconnect -G`. Deux VM de même nom, créées hors d'HV-LAB, ne sont jamais confondues.

### Fenêtre de la VM

Double-cliquez sur une VM, appuyez sur ++enter++ ou choisissez **Ouvrir la fenêtre de la VM** dans son menu pour ouvrir la VM dans **sa propre fenêtre**, une fenêtre par VM. La fenêtre reste à jour, quelle que soit la page affichée dans la fenêtre principale. Elle a une taille minimale, et n'interroge rien tant qu'elle est réduite. Son onglet **Aperçu** contient :

- l'**écran en direct** de la VM démarrée, rafraîchi toutes les 2 secondes ;
- le badge **Session avancée** quand les services d'intégration de la VM l'acceptent : `vmconnect` offre alors presse-papiers, son et résolution libre ;
- les actions d'alimentation en haut : Démarrer, Arrêter, **Connexion vmconnect**, et dans **⋯** Éteindre, Redémarrer, Réinitialiser, Enregistrer l'état, Suspendre et Reprendre.
- **Système invité**, adresse IP, durée de fonctionnement et signal de vie.
- Boutons **SSH**, **Bureau à distance** et **Copier l'adresse IP**, actifs dès que la VM a une adresse IPv4. SSH et Bureau à distance démarrent avec l'utilisateur connecté, pas en administrateur :
    - SSH lance `ssh <compte>@<IP>` dans Windows Terminal, ou à défaut dans une console, avec le compte des notes ;
    - Bureau à distance ouvre `mstsc` sur l'adresse IP.
- **Identifiants** écrits par HV-LAB à la création :
    - **Copier le mot de passe** : la copie est exclue de l'historique du presse-papiers et du presse-papiers cloud, et effacée au bout de 30 secondes ;
    - **Taper le mot de passe** : le saisit dans la VM, puis appuie sur Entrée. Choisissez le **clavier de la VM** (`fr-FR`, `en-US`…) pour qu'un invité AZERTY reçoive les bons caractères ;
    - **Ctrl+Alt+Suppr**.
- la carte **Automatisation (invités Windows)**, décrite ci-dessous ;
- les onglets **Console**, **Points de contrôle** et **Paramètres**, décrits ci-dessous.

++f5++ actualise la fenêtre et recharge l'arbre des points de contrôle.

### Automatisation (invités Windows)

La carte **Automatisation (invités Windows)** s'affiche quand les [notes](create-vm.md#notes-de-la-vm) de la VM contiennent des identifiants. Ses boutons agissent sur une VM démarrée, avec le compte des notes :

- **Exécuter une commande…** lance un script PowerShell dans l'invité et affiche sa sortie. Un script peut tourner jusqu'à 10 minutes ;
- **Copier un fichier vers la VM…** copie un fichier de l'hôte dans `C:\HV-LAB` de l'invité.

Les deux passent par Hyper-V, pas par le réseau :

- la commande utilise **PowerShell Direct**, par le VMBus : la VM n'a besoin ni de réseau ni de règle de pare-feu ;
- la copie utilise `Copy-VMFile` et le service d'intégration *Interface de services d'invité*, qu'HV-LAB active automatiquement.

### Console intégrée

L'onglet **Console** affiche l'écran de la VM à sa résolution réelle et lui envoie clavier et souris par WMI. Elle fonctionne dès le BIOS et l'installation de l'OS, sans réseau dans la VM et sans identifiants.

- **Zoom** : *Ajuster* (l'écran entier dans la fenêtre) ou 50 à 200 %. À 100 %, un pixel de la VM est un pixel de l'écran, quelle que soit la mise à l'échelle de Windows. ++ctrl++ + molette change de niveau.
- **Clavier et souris** : cliquez dans l'écran ; une bordure de couleur indique que les touches et la souris vont à la VM. ++ctrl+alt+left++ (comme dans `vmconnect`) ou un clic hors de l'écran les libère, et quitte aussi le plein écran : les touches encore enfoncées sont relâchées dans la VM.
- Les touches sont envoyées comme **codes physiques** : la VM applique sa propre disposition (un invité AZERTY reçoit de l'AZERTY). Les touches tapées pendant qu'un appel à la VM est encore en cours partent ensemble, dans l'appel suivant.
- ++ctrl+alt+del++ et la touche Windows restent à l'hôte : utilisez le bouton **Ctrl+Alt+Suppr**.
- **Taper le presse-papiers** saisit le texte copié sur l'hôte, avec le clavier de la VM choisi à côté.
- **Plein écran** n'affiche plus que la console ; le même bouton en sort.

L'écran se rafraîchit environ 4 à 5 fois par seconde, la limite de WMI. Pour une frappe fluide, le presse-papiers partagé, le son ou une autre résolution, utilisez **Connexion vmconnect** en session avancée, à activer dans **Paramètres > Hôte Hyper-V**, ou SSH.

### Points de contrôle

L'onglet **Points de contrôle** de la fenêtre de la VM affiche les points de contrôle en arbre. **Maintenant** indique le point à partir duquel la VM tourne.

| Bouton | Effet |
|---|---|
| Créer | Nouveau point de contrôle, nommé d'après la VM et la date par défaut |
| Appliquer | Revient au point de contrôle sélectionné. Une VM démarrée est éteinte, puis redémarrée. |
| Renommer | Renomme le point de contrôle |
| 🗑 | Supprime le point de contrôle : ses modifications sont fusionnées, la VM ne perd rien. Une option supprime aussi les points de contrôle qui en dépendent. |

++del++ supprime le point de contrôle sélectionné, comme 🗑.

### Paramètres après création

L'onglet **Paramètres** de la fenêtre de la VM, ouvert aussi par **Paramètres…** dans le menu **⋯**, modifie les paramètres sur place : **Appliquer** les enregistre, **Annuler les modifications** revient aux valeurs actuelles.

| Paramètre | Quand |
|---|---|
| Processeurs, virtualisation imbriquée | VM arrêtée |
| Mémoire de démarrage, mémoire dynamique (minimum, maximum) | VM arrêtée |
| Image ISO du lecteur DVD (parcourir ou éjecter) | À tout moment |
| Commutateur de la première carte réseau | À tout moment |
| VLAN de la première carte : sans étiquette, **Access** (un VLAN) ou **Trunk** (VLAN natif et liste autorisée, ex. `10,20,100-110`) | À tout moment |
| Protections du port : **protection DHCP** (bloque les offres DHCP de la VM), **protection routeur** (bloque ses annonces de routeur), **usurpation d'adresse MAC** | À tout moment |

La **virtualisation imbriquée** permet de faire tourner Hyper-V, WSL ou des conteneurs dans la VM. HV-LAB passe alors la mémoire en statique et active l'usurpation d'adresse MAC sur la carte, pour que les VM internes accèdent au réseau.

### Supprimer une VM

**Supprimer** affiche les fichiers de la VM :

- son disque ;
- les disques de ses points de contrôle ;
- le disque cloud-init d'un clone Linux.

L'option **Supprimer aussi ses disques et son dossier** est cochée par défaut. L'image de base et les disques utilisés par d'autres VM ne sont jamais supprimés : la boîte de dialogue les liste comme conservés.

### Plusieurs VM à la fois

Sélectionnez plusieurs VM avec ++ctrl++ ou ++shift++. Une barre agit alors sur toutes :

| Bouton | Effet |
|---|---|
| Démarrer, Arrêter, Éteindre | L'action d'alimentation sur chaque VM |
| Démarrer en séquence | Démarre une à une les VM arrêtées de la sélection, par ordre de nom (`01-DC`, `02-SRV`…). Chaque VM attend jusqu'à 3 minutes le signal de vie de la précédente. |
| Exporter en blueprint | Écrit les VM sélectionnées dans un [blueprint](labs.md#exporter-des-vm-en-blueprint) |
| Point de contrôle du lab… | Demande un nom, puis crée sur chaque VM sélectionnée un point de contrôle de ce même nom. Les VM démarrées sont suspendues le temps de la capture, puis reprennent. |
| Regrouper…, Retirer du groupe | Groupes de VM Hyper-V |
| Supprimer | Supprime les VM arrêtées, après confirmation |

Les **groupes** sont des groupes de VM Hyper-V :

- la liste **Groupe** en haut de la page filtre les VM par groupe ;
- une série créée en lot devient un groupe nommé d'après son préfixe (option de [Créer une VM](create-vm.md#identite) et de Quick VM) ;
- un [lab déployé](labs.md) devient un groupe nommé d'après le lab.

### Raccourcis clavier

| Touche | Action |
|---|---|
| ++ctrl+f++ | Rechercher dans la liste |
| ++f5++ | Actualiser la liste, ou la fenêtre de la VM et ses points de contrôle |
| ++enter++ | Ouvrir la fenêtre de la VM sélectionnée |
| ++del++ | Supprimer la ou les VM sélectionnées, après confirmation ; dans l'onglet **Points de contrôle**, le point de contrôle sélectionné |
