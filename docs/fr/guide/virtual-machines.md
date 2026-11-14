# Tableau de bord & VM

## Tableau de bord

Le tableau de bord donne une vue d'ensemble du labo :

- **Vue d'ensemble** : nombre de VM démarrées et arrêtées, de commutateurs et d'images de base.
- **Démarrage rapide** : la [démarche recommandée](../index.md#demarche-recommandee), du commutateur interne jusqu'aux VM.
- **Configuration système** :
    - le dossier des images de base ;
    - l'état de l'élévation ;
    - la disponibilité d'Hyper-V.

Quand Hyper-V n'est pas disponible, un bandeau en donne la raison. Si le service `vmms` est seulement arrêté, le bandeau propose de le démarrer.

## Machines virtuelles

La page **Machines Virtuelles** liste toutes les VM de l'hôte. Elle lit Hyper-V directement via WMI, en quelques dizaines de millisecondes, et se rafraîchit toutes les 3 secondes.

| Colonne | Contenu |
|---|---|
| Nom | La VM, avec son groupe Hyper-V en dessous |
| État | Running, Off, Saved, Paused… |
| RAM | Mémoire utilisée (mémoire de démarrage si la VM est arrêtée) |
| CPU | Processeurs, et la charge en % quand la VM tourne |
| Adresse IP | Remontée par l'invité, sinon trouvée dans la table de voisinage de l'hôte, sinon l'IP fixe des notes |
| Commutateur, VLAN | Réseau de la première carte |

### Actions

Chaque ligne a trois boutons :

- **Démarrer** ;
- **Arrêter**, un arrêt propre par l'invité ;
- **Console**, qui ouvre *Connexion à une machine virtuelle* (`vmconnect`). Une seule fenêtre par VM : un second clic ramène au premier plan la fenêtre déjà ouverte.

Le menu **⋯** regroupe les autres actions :

| Action | Effet |
|---|---|
| Éteindre (forcé) | Coupe l'alimentation, pour une VM bloquée ou encore en installation |
| Redémarrer | Redémarrage propre par l'invité |
| Réinitialiser | Réinitialisation matérielle |
| Enregistrer l'état / Suspendre / Reprendre | États enregistré et en pause d'Hyper-V |
| Console intégrée | Ouvre la fenêtre de la VM sur son onglet **Console** |
| Paramètres… | Ouvre la fenêtre de la VM sur son onglet **Paramètres** |
| Supprimer | Voir [Supprimer une VM](#supprimer-une-vm) |

### Fenêtre de la VM

Double-cliquez sur une VM, appuyez sur ++enter++ ou utilisez son bouton **Détails** pour ouvrir la VM dans **sa propre fenêtre**, une fenêtre par VM. La fenêtre reste à jour, quelle que soit la page affichée dans la fenêtre principale. Son onglet **Aperçu** contient :

- l'**écran en direct** de la VM démarrée, rafraîchi toutes les 2 secondes ;
- le badge **Session avancée** quand les services d'intégration de la VM l'acceptent : `vmconnect` offre alors presse-papiers, son et résolution libre ;
- les actions d'alimentation en haut : Démarrer, Arrêter, Console, et dans **⋯** Éteindre, Redémarrer, Réinitialiser, Enregistrer l'état, Suspendre et Reprendre.
- **Système invité**, adresse IP, durée de fonctionnement et signal de vie.
- Boutons **Console**, **SSH** et **Bureau à distance** :
    - SSH ouvre Windows Terminal avec `ssh <compte>@<IP>`, avec le compte des notes ;
    - Bureau à distance ouvre `mstsc` sur l'adresse IP.
- **Identifiants** écrits par HV-LAB à la création :
    - **Copier le mot de passe** ;
    - **Taper le mot de passe** : le saisit dans la VM, puis appuie sur Entrée. Choisissez le **clavier de la VM** (`fr-FR`, `en-US`…) pour qu'un invité AZERTY reçoive les bons caractères ;
    - **Ctrl+Alt+Suppr**.
- les onglets **Console**, **Points de contrôle** et **Paramètres**, décrits ci-dessous.

### Console intégrée

L'onglet **Console** affiche l'écran de la VM à sa résolution réelle, environ 10 images par seconde, et lui envoie clavier et souris par WMI. Elle fonctionne dès le BIOS et l'installation de l'OS, sans réseau dans la VM et sans identifiants.

- **Zoom** : *Ajuster* (l'écran entier dans la fenêtre) ou 50 à 200 %. À 100 %, un pixel de la VM est un pixel de l'écran, quelle que soit la mise à l'échelle de Windows. ++ctrl++ + molette change de niveau.
- **Clavier et souris** : cliquez dans l'écran ; une bordure de couleur indique que les touches et la souris vont à la VM. Cliquez hors de l'écran pour les libérer : les touches encore enfoncées sont relâchées dans la VM.
- Les touches sont envoyées comme **codes physiques** : la VM applique sa propre disposition (un invité AZERTY reçoit de l'AZERTY).
- ++ctrl+alt+del++ et la touche Windows restent à l'hôte : utilisez le bouton **Ctrl+Alt+Suppr**.
- **Taper le presse-papiers** saisit le texte copié sur l'hôte, avec le clavier de la VM choisi à côté.
- **Plein écran** n'affiche plus que la console ; le même bouton en sort.

Pour le presse-papiers partagé, le son ou une autre résolution, utilisez **Console** (`vmconnect`) en session avancée, à activer dans **Paramètres > Hôte Hyper-V**.

### Points de contrôle

L'onglet **Points de contrôle** de la fenêtre de la VM affiche les points de contrôle en arbre. **Maintenant** indique le point à partir duquel la VM tourne.

| Bouton | Effet |
|---|---|
| Créer | Nouveau point de contrôle, nommé d'après la VM et la date par défaut |
| Appliquer | Revient au point de contrôle sélectionné. Une VM démarrée est éteinte, puis redémarrée. |
| Renommer | Renomme le point de contrôle |
| 🗑 | Supprime le point de contrôle : ses modifications sont fusionnées, la VM ne perd rien. Une option supprime aussi les points de contrôle qui en dépendent. |

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

Sélectionnez plusieurs VM avec ++ctrl++ ou ++shift++. Une barre propose alors **Démarrer**, **Arrêter**, **Éteindre**, **Regrouper…**, **Retirer du groupe** et **Supprimer** pour toutes.

Les **groupes** sont des groupes de VM Hyper-V :

- la liste **Groupe** en haut de la page filtre les VM par groupe ;
- une série créée en lot devient un groupe nommé d'après son préfixe (option de [Créer une VM](create-vm.md#identite) et de Quick VM).

### Raccourcis clavier

| Touche | Action |
|---|---|
| ++f5++ | Actualiser la liste |
| ++del++ | Supprimer la ou les VM sélectionnées, après confirmation |
