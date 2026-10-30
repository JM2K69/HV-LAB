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

The **Virtual Machines** page lists every VM on the host with:

- its state;
- its processors, memory and generation;
- its VLAN.

The filters show **All**, **Running** or **Stopped** VMs.

| Action | Effect |
|---|---|
| **Start** | Starts the VM. |
| **Stop** | Shuts down the VM and waits until it is off. |
| **Console** | Opens the VM in *Virtual Machine Connection* (`vmconnect`). |
| **Delete** | Removes the VM from Hyper-V after a confirmation. |

!!! note "Delete keeps the disks"
    Deleting a VM removes it from Hyper-V, but its VHDX files stay in the VMs folder. Delete them by hand if you no longer need them.

States refresh automatically every 5 seconds. Each VM runs one action at a time, so a double click does nothing.

### Keyboard shortcuts

| Key | Action |
|---|---|
| ++f5++ | Refresh the list |
| ++del++ | Delete the selected VM, after a confirmation |
