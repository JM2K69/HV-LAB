# Switches & NAT

The **Switches & NAT** page has two tabs: one for virtual switches and one for NAT networks.

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

To delete a switch, use **Delete** on its row and confirm. The list shows each switch's type, adapters and VLAN.

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
    Windows NAT does not hand out addresses. Configure each VM with a static address in the subnet. For example, on `192.168.100.0/24`:

    | Setting | Value |
    |---|---|
    | IP address | `192.168.100.10` |
    | Subnet mask | `255.255.255.0` |
    | Default gateway | `192.168.100.1`, the NAT gateway |
    | DNS | a lab domain controller, or a public resolver |

    Alternatively, install a DHCP server in a lab VM.

To delete a NAT network, use **Delete** on its row and confirm.
