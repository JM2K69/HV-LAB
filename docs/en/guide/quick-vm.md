# Quick VM

**Quick VM** creates VMs from a size profile in one click, without an answer file. Use it for:

- VMs that start from an ISO or the network (PXE);
- VMs that run the normal Windows setup.

## Size profiles

| Profile | vCPU | Memory | Typical use |
|---|---|---|---|
| **XXS** | 1 | 512 MB | Router, proxy, lightweight firewall |
| **XS** | 1 | 1 GB | DNS / DHCP server, agent |
| **S** | 2 | 2 GB | Member server, web, lightweight database |
| **M** | 2 | 4 GB | Domain controller, Exchange, SQL |
| **L** | 4 | 8 GB | Nested Hyper-V, SCCM, heavy services |
| **XL** | 8 | 16 GB | High-load lab, nested virtualization |

## Options

Disk type
:   - **Blank**: a new empty VHDX, ready for an OS installation. Its size defaults to 40 GB.
    - **Differencing**: a differencing disk on a [base image](base-images.md). The VM starts on the generalized image and runs the normal Windows OOBE.

Generation
:   1 or 2. Use generation 2 for current Windows versions.

Secure Boot
:   Generation 2 only. Some Linux distributions need it disabled.

Network boot (PXE)
:   Places the network adapter first in the boot order, for a WDS, MDT or SCCM deployment.

Bulk creation
:   Like [Create a VM](create-vm.md#identity), a prefix and a count create `PREFIX-01…nn`.

!!! tip "Unattended setup"
    To get a VM that configures itself (computer name, password, language), use [Create a VM](create-vm.md) instead.
