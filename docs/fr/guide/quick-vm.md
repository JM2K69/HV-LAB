# Quick VM

**Quick VM** crée des VM d'un clic à partir d'un profil de taille, sans fichier de réponse. Utilisez-la pour :

- des VM qui démarrent sur un ISO ou sur le réseau (PXE) ;
- des VM qui passent par l'installation Windows classique.

## Profils de taille

| Profil | vCPU | Mémoire | Usage courant |
|---|---|---|---|
| **XXS** | 1 | 512 Mo | Routeur, proxy, pare-feu léger |
| **XS** | 1 | 1 Go | Serveur DNS / DHCP, agent |
| **S** | 2 | 2 Go | Serveur membre, web, base de données légère |
| **M** | 2 | 4 Go | Contrôleur de domaine, Exchange, SQL |
| **L** | 4 | 8 Go | Hyper-V imbriqué, SCCM, services lourds |
| **XL** | 8 | 16 Go | Labo haute charge, virtualisation imbriquée |

## Options

Type de disque
:   - **Vierge** : un nouveau VHDX vide, prêt pour l'installation d'un OS. Sa taille est de 40 Go par défaut.
    - **Différentielle** : un disque différentiel sur une [image de base](base-images.md). La VM démarre sur l'image généralisée et passe par l'OOBE Windows classique.

Génération
:   1 ou 2. Utilisez la génération 2 pour les versions actuelles de Windows.

Secure Boot
:   Génération 2 uniquement. Certaines distributions Linux demandent de le désactiver.

Démarrage réseau (PXE)
:   Place la carte réseau en premier dans l'ordre de démarrage, pour un déploiement WDS, MDT ou SCCM.

Création en lot
:   Comme pour [Créer une VM](create-vm.md#identite), un préfixe et un nombre créent `PREFIXE-01…nn`.

!!! tip "Installation automatisée"
    Pour une VM qui se configure seule (nom d'ordinateur, mot de passe, langue), utilisez plutôt [Créer une VM](create-vm.md).
