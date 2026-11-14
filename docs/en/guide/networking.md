# Switches & NAT

The **Switches & NAT** page has three tabs: **Topology**, **Switches** and **NAT**. Everything is read through WMI, so the page opens at once.

## Topology

The **Topology** tab draws the lab network. Each switch is shown as:

```text
[ uplink ] ──── [ switch ] ──┬── ● SRV-01   192.168.100.10   VLAN 20
                             ├── ● SRV-02   192.168.100.11
                             └── ● CLI-01
```

| Uplink | Meaning |
|---|---|
| Physical adapter | External switch: the VMs are on your LAN. The host address is shown when the host shares the adapter. |
| NAT network | Internal switch with a [NAT network](#nat-networks): its prefix and the gateway on the host |
| Connection sharing | Internal switch shared by Windows: [NAT with DHCP](#nat-with-dhcp-connection-sharing) |
| Host only | Internal switch without NAT: the host and the VMs see each other, no Internet |
| Isolated | Private switch: the VMs see each other only |

Each VM adapter shows:

- a dot for the VM state: green when running, grey when off, orange in between;
- the adapter name, when the VM has several adapters;
- its IP address and VLAN.

Adapters connected to no switch are grouped under **Adapters not connected**.

## Switch types

| Type | Who can talk to the VMs | Typical use |
|---|---|---|
| **External** | The host, the VMs and your physical network, through a network adapter | VMs that must be visible on the LAN |
| **Internal** | The host and the VMs | Isolated lab, with Internet access through [NAT](#nat-networks) |
| **Private** | The VMs only | Fully isolated segment |

## Create a switch

1. Enter a **switch name**, for example `LAB-Internal`.
2. Choose the **type**.
3. For an External switch, choose the **physical network adapter** it binds to.
4. Optional: check **Enable VLAN (Access mode)** and enter a **VLAN ID** between 1 and 4094.
5. Click **Create switch**.

!!! warning "External switch"
    Binding an External switch to the adapter you use for network access briefly cuts the host's connection while Hyper-V reconfigures it.

To delete a switch, use the delete button on its row and confirm.

## Change the type of a switch

The ✎ button of a switch changes its type in place, without deleting it: the VMs stay connected.

- **Internal or Private → External**: choose the physical adapter. The host's connection drops for a few seconds.
- **External → Internal or Private**: the VMs lose the physical network.
- A switch that carries a NAT network keeps it only if it stays **Internal**: the dialog warns you otherwise.

If Hyper-V refuses the change, HV-LAB puts the previous type back, and the static IPv4 addresses of the host adapter (where a NAT gateway lives) are restored.

## NAT networks

A NAT network gives the VMs of an **internal** switch access to the Internet through the host. Create the internal switch first.

1. Enter a **NAT name**, for example `NAT-192.168.100`.
2. Choose the **internal switch**.
3. Enter the **gateway IP**, for example `192.168.100.1`, and the **prefix** (`/24`). The prefix must be between 8 and 30.
4. Click **Create NAT**.

HV-LAB then:

- assigns the gateway IP to the host's network adapter on that switch;
- creates the NAT network for the subnet with `New-NetNat`.

!!! info "No DHCP"
    Windows NAT does not hand out addresses. Either:

    - give each VM a static address in the subnet, which [Create a VM](create-vm.md) does for Linux clones;
    - install a DHCP server in a lab VM;
    - or use [NAT with DHCP](#nat-with-dhcp-connection-sharing) instead.

    For example, on `192.168.100.0/24`:

    | Setting | Value |
    |---|---|
    | IP address | `192.168.100.10` |
    | Subnet mask | `255.255.255.0` |
    | Default gateway | `192.168.100.1`, the NAT gateway |
    | DNS | a lab domain controller, or a public resolver |

To delete a NAT network, use the delete button on its row and confirm.

## NAT with DHCP (connection sharing)

The **NAT with DHCP (Windows connection sharing)** section of the NAT tab uses Windows Internet connection sharing (ICS):

1. Choose an **internal switch** and the **shared adapter**, the one that has Internet access (Ethernet, Wi-Fi).
2. Click **Turn sharing on** and confirm.

Windows then:

- gives the switch's host adapter the address `192.168.137.1/24`;
- hands out DHCP addresses in `192.168.137.0/24` to the VMs, with the host as gateway and DNS.

!!! warning "Limits"
    - Windows allows **one** connection sharing at a time: turning it on turns off any other sharing.
    - The subnet is always `192.168.137.0/24`.
    - A switch that already has a NAT network cannot be shared: delete the NAT network first.

**Turn sharing off** stops it. The section shows the active sharing when you open it.
