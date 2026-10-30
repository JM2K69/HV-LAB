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

La page **Machines Virtuelles** liste toutes les VM de l'hôte avec :

- leur état ;
- leurs processeurs, leur mémoire et leur génération ;
- leur VLAN.

Les filtres affichent **Toutes** les VM, celles **En cours** ou celles **Arrêtées**.

| Action | Effet |
|---|---|
| **Démarrer** | Démarre la VM. |
| **Arrêter** | Arrête la VM et attend qu'elle soit éteinte. |
| **Console** | Ouvre la VM dans *Connexion à une machine virtuelle* (`vmconnect`). |
| **Supprimer** | Retire la VM d'Hyper-V après confirmation. |

!!! note "La suppression garde les disques"
    Supprimer une VM la retire d'Hyper-V, mais ses fichiers VHDX restent dans le dossier des VM. Supprimez-les à la main si vous n'en avez plus besoin.

Les états se rafraîchissent automatiquement toutes les 5 secondes. Chaque VM n'exécute qu'une action à la fois, donc un double clic n'a aucun effet.

### Raccourcis clavier

| Touche | Action |
|---|---|
| ++f5++ | Actualiser la liste |
| ++del++ | Supprimer la VM sélectionnée, après confirmation |
