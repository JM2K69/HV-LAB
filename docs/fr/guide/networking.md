# Commutateurs & NAT

La page **Commutateurs & NAT** a deux onglets : un pour les commutateurs virtuels, un pour les réseaux NAT.

## Types de commutateurs

| Type | Qui peut joindre les VM | Usage courant |
|---|---|---|
| **Externe** | L'hôte, les VM et votre réseau physique, via une carte réseau | VM visibles sur le réseau local |
| **Interne** | L'hôte et les VM | Labo isolé, avec accès à Internet via le [NAT](#reseaux-nat) |
| **Privé** | Les VM seulement | Segment totalement isolé |

## Créer un commutateur

1. Saisissez le **nom du commutateur**, par exemple `LAB-Interne`.
2. Choisissez le **type**.
3. Pour un commutateur externe, choisissez la **carte réseau physique** à laquelle il se lie.
4. En option : cochez **Activer un VLAN (mode Access)** et saisissez un **VLAN ID** entre 1 et 4094.
5. Cliquez sur **Créer le commutateur**.

!!! warning "Commutateur externe"
    Lier un commutateur externe à la carte qui donne accès au réseau coupe brièvement la connexion de l'hôte pendant qu'Hyper-V la reconfigure.

Pour supprimer un commutateur, utilisez **Supprimer** sur sa ligne et confirmez. La liste affiche le type, les adaptateurs et le VLAN de chaque commutateur.

## Réseaux NAT

Un réseau NAT donne accès à Internet, à travers l'hôte, aux VM d'un commutateur **interne**. Créez d'abord le commutateur interne.

1. Saisissez le **nom du NAT**, par exemple `NAT-192.168.100`.
2. Choisissez le **commutateur interne**.
3. Saisissez l'**IP passerelle**, par exemple `192.168.100.1`, et le **préfixe** (`/24`). Le préfixe doit être compris entre 8 et 30.
4. Cliquez sur **Créer le NAT**.

HV-LAB :

- attribue l'IP passerelle à la carte réseau de l'hôte sur ce commutateur ;
- crée le réseau NAT du sous-réseau avec `New-NetNat`.

!!! info "Pas de DHCP"
    Le NAT de Windows ne distribue pas d'adresses. Configurez chaque VM avec une adresse fixe dans le sous-réseau. Par exemple, pour `192.168.100.0/24` :

    | Paramètre | Valeur |
    |---|---|
    | Adresse IP | `192.168.100.10` |
    | Masque | `255.255.255.0` |
    | Passerelle par défaut | `192.168.100.1`, la passerelle du NAT |
    | DNS | un contrôleur de domaine du labo, ou un résolveur public |

    Vous pouvez aussi installer un serveur DHCP dans une VM du labo.

Pour supprimer un réseau NAT, utilisez **Supprimer** sur sa ligne et confirmez.
