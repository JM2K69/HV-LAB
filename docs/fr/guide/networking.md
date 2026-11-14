# Commutateurs & NAT

La page **Commutateurs & NAT** a trois onglets : **Topologie**, **Commutateurs** et **NAT**. Tout est lu via WMI, donc la page s'ouvre immédiatement.

## Topologie

L'onglet **Topologie** dessine le réseau du labo. Chaque commutateur apparaît ainsi :

```text
[ sortie ] ──── [ commutateur ] ──┬── ● SRV-01   192.168.100.10   VLAN 20
                                  ├── ● SRV-02   192.168.100.11
                                  └── ● CLI-01
```

| Sortie | Signification |
|---|---|
| Carte physique | Commutateur externe : les VM sont sur votre réseau local. L'adresse de l'hôte s'affiche quand l'hôte partage la carte. |
| Réseau NAT | Commutateur interne avec un [réseau NAT](#reseaux-nat) : son préfixe et la passerelle sur l'hôte |
| Partage de connexion | Commutateur interne partagé par Windows : [NAT avec DHCP](#nat-avec-dhcp-partage-de-connexion) |
| Hôte uniquement | Commutateur interne sans NAT : l'hôte et les VM se voient, sans Internet |
| Isolé | Commutateur privé : les VM se voient entre elles uniquement |

Chaque carte de VM affiche :

- une pastille pour l'état de la VM : verte quand elle tourne, grise quand elle est arrêtée, orange entre les deux ;
- le nom de la carte, quand la VM en a plusieurs ;
- son adresse IP et son VLAN.

Les cartes reliées à aucun commutateur sont regroupées sous **Cartes non connectées**.

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

Pour supprimer un commutateur, utilisez le bouton de suppression sur sa ligne et confirmez.

## Changer le type d'un commutateur

Le bouton ✎ d'un commutateur change son type sur place, sans le supprimer : les VM restent connectées.

- **Interne ou privé → externe** : choisissez la carte physique. La connexion de l'hôte est coupée quelques secondes.
- **Externe → interne ou privé** : les VM perdent le réseau physique.
- Un commutateur qui porte un réseau NAT ne le garde que s'il reste **interne** : la boîte de dialogue vous prévient sinon.

Si Hyper-V refuse le changement, HV-LAB remet le type précédent et restaure les adresses IPv4 fixes de la carte de l'hôte, celle qui porte la passerelle NAT.

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
    Le NAT de Windows ne distribue pas d'adresses. Au choix :

    - donnez à chaque VM une adresse fixe dans le sous-réseau, ce que fait [Créer une VM](create-vm.md) pour les clones Linux ;
    - installez un serveur DHCP dans une VM du labo ;
    - ou utilisez plutôt le [NAT avec DHCP](#nat-avec-dhcp-partage-de-connexion).

    Par exemple, pour `192.168.100.0/24` :

    | Paramètre | Valeur |
    |---|---|
    | Adresse IP | `192.168.100.10` |
    | Masque | `255.255.255.0` |
    | Passerelle par défaut | `192.168.100.1`, la passerelle du NAT |
    | DNS | un contrôleur de domaine du labo, ou un résolveur public |

Pour supprimer un réseau NAT, utilisez le bouton de suppression sur sa ligne et confirmez.

## NAT avec DHCP (partage de connexion)

La section **NAT avec DHCP (partage de connexion Windows)** de l'onglet NAT utilise le partage de connexion Internet de Windows (ICS) :

1. Choisissez un **commutateur interne** et la **carte partagée**, celle qui a l'accès à Internet (Ethernet, Wi-Fi).
2. Cliquez sur **Activer le partage** et confirmez.

Windows :

- donne à la carte de l'hôte sur ce commutateur l'adresse `192.168.137.1/24` ;
- distribue aux VM des adresses DHCP en `192.168.137.0/24`, avec l'hôte comme passerelle et DNS.

!!! warning "Limites"
    - Windows n'autorise **qu'un seul** partage de connexion à la fois : l'activer désactive tout autre partage.
    - Le sous-réseau est toujours `192.168.137.0/24`.
    - Un commutateur qui porte déjà un réseau NAT ne peut pas être partagé : supprimez d'abord le réseau NAT.

**Désactiver le partage** l'arrête. La section affiche le partage actif quand vous l'ouvrez.
