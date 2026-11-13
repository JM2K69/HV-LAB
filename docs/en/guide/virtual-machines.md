# Dashboard & VMs

## Dashboard

The dashboard gives an overview of the lab:

- **Overview**: number of running and stopped VMs, switches and base images.
- **Quick start**: the [recommended workflow](../index.md#recommended-workflow), from the internal switch to the VMs.
- **System configuration**:
    - the base images folder;
    - the elevation state;
    - whether Hyper-V is available.

When Hyper-V is unavailable, a banner explains why and, if the `vmms` service is only stopped, offers to start it.

## Virtual machines

The **Virtual Machines** page lists every VM on the host. It reads Hyper-V directly through WMI, which takes a few tens of milliseconds, and refreshes every 3 seconds.

| Column | Content |
|---|---|
| Name | The VM, with its Hyper-V group underneath |
| State | Running, Off, Saved, Paused… |
| RAM | Memory in use (startup memory when the VM is off) |
| CPU | Processors, and the load in % while the VM runs |
| IP address | Reported by the guest, else found in the host's neighbour cache, else the static IP from the notes |
| Switch, VLAN | Network of the first adapter |

### Actions

Each row has three buttons:

- **Start**;
- **Shut down**, a clean shutdown through the guest;
- **Console**, which opens *Virtual Machine Connection* (`vmconnect`).

The **⋯** menu holds the other actions:

| Action | Effect |
|---|---|
| Turn off (forced) | Cuts the power, for a VM that is stuck or still in setup |
| Restart | Clean restart through the guest |
| Reset | Hard reset |
| Save state / Pause / Resume | Hyper-V saved and paused states |
| Settings… | See [Settings after creation](#settings-after-creation) |
| Delete | See [Delete a VM](#delete-a-vm) |

### Detail pane

Select one VM to open its detail pane on the right:

- **Live screen** of the running VM, refreshed every 2 seconds.
- **Guest OS**, IP address, uptime and heartbeat.
- **Console**, **SSH** and **Remote Desktop** buttons:
    - SSH opens Windows Terminal with `ssh <account>@<IP>`, using the account from the notes;
    - Remote Desktop opens `mstsc` on the IP address.
- **Credentials** written by HV-LAB at creation:
    - **Copy password**;
    - **Type the password**: types it into the VM, then presses Enter. Choose the **VM keyboard** (`fr-FR`, `en-US`…) so that an AZERTY guest receives the right characters;
    - **Ctrl+Alt+Del**.
- **Checkpoints** (see below).

### Checkpoints

The detail pane shows the checkpoints as a tree. **Now** marks the point the VM runs from.

| Button | Effect |
|---|---|
| Create | New checkpoint, named after the VM and the date by default |
| Apply | Goes back to the selected checkpoint. A running VM is turned off, then restarted. |
| Rename | Renames the checkpoint |
| 🗑 | Deletes the checkpoint: its changes are merged, the VM loses nothing. An option also deletes the checkpoints that depend on it. |

### Settings after creation

**Settings…**, in the **⋯** menu or the detail pane, changes:

| Setting | When |
|---|---|
| Processors, nested virtualization | VM off |
| Startup memory, dynamic memory (minimum, maximum) | VM off |
| ISO image of the DVD drive (browse or eject) | Anytime |
| Switch of the network adapter, VLAN | Anytime |

**Nested virtualization** lets you run Hyper-V, WSL or containers inside the VM. HV-LAB then sets static memory and turns on MAC spoofing on the adapter, so the inner VMs reach the network.

### Delete a VM

**Delete** shows the files of the VM:

- its disk;
- its checkpoint disks;
- the cloud-init disk of a Linux clone.

The option **Also delete its disks and folder** is checked by default. The base image and the disks used by other VMs are never deleted: the dialog lists them as kept.

### Several VMs at once

Select several VMs with ++ctrl++ or ++shift++. A bar then offers **Start**, **Shut down**, **Turn off**, **Group…**, **Remove from group** and **Delete** for all of them.

**Groups** are Hyper-V VM groups:

- the **Group** list at the top of the page filters the VMs by group;
- a series created in bulk becomes a group named after its prefix (an option of [Create a VM](create-vm.md#identity) and Quick VM).

### Keyboard shortcuts

| Key | Action |
|---|---|
| ++f5++ | Refresh the list |
| ++del++ | Delete the selected VM or VMs, after a confirmation |
