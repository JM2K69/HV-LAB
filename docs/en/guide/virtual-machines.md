# Dashboard & VMs

## Dashboard

The dashboard gives an overview of the lab:

- **Overview**: number of running and stopped VMs, switches and base images.
- **Host capacity**:
    - the memory used by the running VMs, against the free and total memory of the host;
    - the vCPUs of the running VMs, against the logical processors of the host;
    - the free space of the drives that hold the VMs folder and the base images folder.
- **Quick start**: the [recommended workflow](../index.md#recommended-workflow), from the internal switch to the VMs.
- **System configuration**:
    - the base images folder;
    - the elevation state;
    - whether Hyper-V is available.

When Hyper-V is unavailable, a banner explains why and, if the `vmms` service is only stopped, offers to start it.

## Virtual machines

The **Virtual Machines** page lists every VM on the host. It reads Hyper-V directly through WMI, which takes a few tens of milliseconds, and refreshes every 3 seconds.

The header has two rows:

- the title, the **Labs** button, which opens the lab editor (see [Lab blueprints](labs.md)), and **Refresh** (++f5++);
- the search box **Search (Ctrl+F): name, IP, OS, group, tag**, the state filter and the **Group** and **Tag** filters.

++ctrl+f++ puts the cursor in the search box. The search matches the name, the IP address, the guest OS, the group and the tags of each VM. The state filter shows **All**, **Running**, **Stopped** or **Saved or paused** VMs. The automatic refresh keeps the search, the filters and the selection.

| Column | Content |
|---|---|
| Name | The VM, with its Hyper-V group and its tags underneath |
| State | Running, Off, Saved, Paused… |
| RAM | Memory in use (startup memory when the VM is off) |
| CPU | Processors, and the load in % while the VM runs |
| IP address | Reported by the guest, else found in the host's neighbour cache, else the static IP from the notes |
| Switch, VLAN | Network of the first adapter |

States, heartbeat and switch types are shown in the interface language.

### Actions

Each row has three buttons:

- **Start**;
- **Shut down**, a clean shutdown through the guest;
- **Connect (vmconnect)**, which opens *Virtual Machine Connection* (`vmconnect`). One window per VM: a second click brings the open window to the front. That is where you work in the VM; the VM window only shows a preview of it.

The **⋯** menu holds the other actions. A right-click on the row, or the menu key, opens the same menu.

| Action | Effect |
|---|---|
| Open the VM window | See [VM window](#vm-window) |
| Turn off (forced) | Cuts the power, for a VM that is stuck or still in setup |
| Restart | Clean restart through the guest |
| Reset | Hard reset |
| Save state / Pause / Resume | Hyper-V saved and paused states |
| Export as blueprint | Writes this VM, or the selected VMs when the row is part of a multi-selection, to a [blueprint](labs.md#export-vms-as-a-blueprint) |
| Settings… | Opens the VM window on its **Settings** tab |
| Delete | See [Delete a VM](#delete-a-vm) |

At most 4 power actions run at the same time. A shutdown that has not finished after 2 minutes is reported as an error: the guest refused it or is still busy, so use **Turn off (forced)**.

When an action fails, the red banner gives the reason. It stays until you close it or start another action. **Copy details** copies PowerShell's own output, useful to [report a problem](../troubleshooting.md#report-a-problem).

!!! note "VMs with the same name"
    HV-LAB addresses each VM by its Hyper-V GUID: delete, settings, checkpoints, groups and `vmconnect -G`. Two VMs with the same name, created outside HV-LAB, are never confused.

### VM window

Double-click a VM, press ++enter++ or choose **Open the VM window** in its menu to open the VM in **its own window**, one window per VM. The window stays live, whatever page the main window shows. It has a minimum size, and polls nothing while it is minimized. Its **Overview** tab holds:

- the **live screen** of the running VM, refreshed every 2 seconds;
- the **Enhanced session** badge when the VM integration services accept it: `vmconnect` then offers clipboard, sound and any resolution;
- the power actions at the top: Start, Shut down, **Connect (vmconnect)**, and in **⋯** Turn off, Restart, Reset, Save state, Pause and Resume.
- **Guest OS**, IP address, uptime and heartbeat.
- **SSH**, **Remote Desktop** and **Copy IP address** buttons, enabled once the VM has an IPv4 address. SSH and Remote Desktop start as the signed-in user, not as administrator:
    - SSH runs `ssh <account>@<IP>` in Windows Terminal, or in a console without it, using the account from the notes;
    - Remote Desktop opens `mstsc` on the IP address.
- **Credentials** written by HV-LAB at creation:
    - **Copy password**: the copy is kept out of the clipboard history and the cloud clipboard, and cleared after 30 seconds;
    - **Type the password**: types it into the VM, then presses Enter. Choose the **VM keyboard** (`fr-FR`, `en-US`…) so that an AZERTY guest receives the right characters;
    - **Ctrl+Alt+Del**.
- the **Automation (Windows guests)** card, described below;
- the **Checkpoints** and **Settings** tabs, described below.

++f5++ refreshes the window and reloads the checkpoint tree.

### Automation (Windows guests)

The **Automation (Windows guests)** card shows when the [notes](create-vm.md#vm-notes) of the VM hold credentials. Its buttons work on a running VM, with the account of the notes:

- **Run a command…** runs a PowerShell script in the guest and shows its output. A script can run for up to 10 minutes;
- **Copy a file to the VM…** copies a file of the host into `C:\HV-LAB` in the guest.

Both go through Hyper-V, not through the network:

- the command uses **PowerShell Direct**, over the VMBus: the VM needs no network and no firewall rule;
- the copy uses `Copy-VMFile` and the *Guest Service Interface* integration service, which HV-LAB turns on automatically.

### Working in the VM

The preview sends no keyboard or mouse input. To work in the VM, use **Connect (vmconnect)**, the native Hyper-V console: it works from the firmware and the OS setup on, with no network. In enhanced session, turned on in **Settings > Hyper-V host**, it offers a shared clipboard, sound and any resolution. SSH and Remote Desktop stay available once the VM has an IP address.

### Checkpoints

The **Checkpoints** tab of the VM window shows the checkpoints as a tree. **Now** marks the point the VM runs from.

| Button | Effect |
|---|---|
| Create | New checkpoint, named after the VM and the date by default |
| Apply | Goes back to the selected checkpoint. A running VM is turned off, then restarted. |
| Rename | Renames the checkpoint |
| 🗑 | Deletes the checkpoint: its changes are merged, the VM loses nothing. An option also deletes the checkpoints that depend on it. |

++del++ deletes the selected checkpoint, like 🗑.

### Settings after creation

The **Settings** tab of the VM window, also opened by **Settings…** in the **⋯** menu, changes the settings in place: **Apply** saves them, **Undo changes** goes back to the current ones.

| Setting | When |
|---|---|
| Processors, nested virtualization | VM off |
| Startup memory, dynamic memory (minimum, maximum) | VM off |
| ISO image of the DVD drive (browse or eject) | Anytime |
| Switch of the first network adapter | Anytime |
| VLAN of the first adapter: untagged, **Access** (one VLAN) or **Trunk** (native VLAN and allowed list, e.g. `10,20,100-110`) | Anytime |
| Port guards: **DHCP guard** (drops DHCP offers from the VM), **router guard** (drops its router advertisements), **MAC address spoofing** | Anytime |

**Nested virtualization** lets you run Hyper-V, WSL or containers inside the VM. HV-LAB then sets static memory and turns on MAC spoofing on the adapter, so the inner VMs reach the network.

### Delete a VM

**Delete** shows the files of the VM:

- its disk;
- its checkpoint disks;
- the cloud-init disk of a Linux clone.

The option **Also delete its disks and folder** is checked by default. The base image and the disks used by other VMs are never deleted: the dialog lists them as kept.

### Several VMs at once

Select several VMs with ++ctrl++ or ++shift++. A bar then acts on all of them:

| Button | Effect |
|---|---|
| Start, Shut down, Turn off | The power action on each VM |
| Start in sequence | Starts the selected stopped VMs one by one, in name order (`01-DC`, `02-SRV`…). Each VM waits up to 3 minutes for the heartbeat of the previous one. |
| Export as blueprint | Writes the selected VMs to a [blueprint](labs.md#export-vms-as-a-blueprint) |
| Lab checkpoint… | Asks for a name, then gives every selected VM a checkpoint with that same name. Running VMs are paused while it is taken, then resume. |
| Group…, Remove from group | Hyper-V VM groups |
| Tags… | Tags of the selected VMs (see below) |
| Delete | Deletes the stopped VMs, after a confirmation |

**Groups** are Hyper-V VM groups:

- the **Group** list at the top of the page filters the VMs by group;
- a series created in bulk becomes a group named after its prefix (an option of [Create a VM](create-vm.md#identity) and Quick VM);
- a [deployed lab](labs.md) becomes a group named after the lab.

**Tags** are free labels (`web`, `prod`, `customer-x`…), and a VM can have several:

- **Tags…**, in the **⋯** menu of a row or in the selection bar, edits them, comma-separated; an empty box removes them all;
- on several VMs, the box offers the tags they all have: a tag added goes to every VM, a tag removed leaves every VM, and the tags of a single VM stay;
- they show under the name (`#web · #prod`), the **Tag** list filters the page, and the search finds them;
- they are written into the Hyper-V notes of the VM (a `Tags:` line): they travel with the VM and show in Hyper-V Manager;
- a [blueprint](labs.md) can give them to each VM.

### Keyboard shortcuts

| Key | Action |
|---|---|
| ++ctrl+f++ | Search the list |
| ++f5++ | Refresh the list, or the VM window and its checkpoints |
| ++enter++ | Open the window of the selected VM |
| ++del++ | Delete the selected VM or VMs, after a confirmation; in the **Checkpoints** tab, the selected checkpoint |
