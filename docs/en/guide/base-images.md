# Base images

A base image is a generalized Windows installation in a `BASE_*.vhdx` file. VMs are then created on **differencing disks** of this file: each VM stores only its own changes, so creating a VM takes seconds.

!!! tip "Linux, a customized Windows VM, or a VHDX file"
    A base can also be copied from an existing VM (see [Linux bases](linux.md)), or [imported from a VHDX file](#import-a-vhdx-file).

The **Base VHDX Images** page lists the images found in the base images folder (see [Settings](settings.md)). The list shows the OS, version, edition, size and date of each image, and **Used by N VM(s)**: the number of VMs built on it. Hover over it to see their names. Below the list is the form that builds a new image.

## Build an image

### 1 — Source WIM / ESD file

1. Mount a Windows ISO by double-clicking it in Explorer.
2. Click **Select…** and choose the image file of the mounted ISO:
    - `sources\install.wim`;
    - or `sources\install.esd`.
3. Click **Analyze**.

The **WIM images** list then shows every index of the file, for example *Windows Server 2022 Standard (Desktop Experience)*.

### 2 — Operating system

After the analysis, HV-LAB fills in the form for you:

- the **OS family**, from the names of the analyzed images: `Windows11`, `Windows10`, `WindowsServer2025`, `WindowsServer2022`, `WindowsServer2019` or `WindowsServer2016`;
- the **edition**, which you can change:
    - Server: `Standard`, `Datacenter`, `StandardCore` or `DatacenterCore`;
    - client: `Pro`, `Enterprise`, `Education`, `ProEducation` or `ProWorkstation`;
- **Desktop Experience** for server editions that are not Core;
- the **WIM index** that matches the chosen edition, with a ✓ when it is found;
- the **version**, for example `10.0.20348.587`. If it cannot be detected, enter it by hand.

### 3 — Disk & destination

| Field | Default | Notes |
|---|---|---|
| Generation | 2 | Generation 2 builds a GPT/UEFI disk, Generation 1 an MBR/BIOS disk. VMs created from the image must use the same generation. |
| Size (GB) | 50 | Maximum size of the dynamic VHDX. The file only grows as data is written. |
| Destination folder | base images folder | |

The **file name preview** shows the name of the file to be built, for example:

```text
BASE_WindowsServer2022Standard(DesktopExperience)_10.0.20348.587_50.vhdx
```

Click **Create base image**. The progress is shown live, and the build can be cancelled.

## What happens during the build

1. A dynamic VHDX is created in a **temporary file** and partitioned for the chosen generation.
2. The Windows image is applied with DISM, which takes a few minutes.
3. The boot files are written.
4. The temporary file is renamed to its final `BASE_*.vhdx` name.

The build is silent:

- the volumes are mounted on private folders under `%ProgramData%\HV-LAB`, never on a drive letter;
- no Explorer window opens.

An existing image is never left half-built. If a file with the same name already exists, HV-LAB asks before replacing it.

!!! info "Image in sysprep state"
    The base image is built **without an answer file** and stays generalized. The `unattend.xml` is injected into each VM's differencing disk when the VM is created. See [Create a VM](create-vm.md).

!!! danger "Never modify a base image used by VMs"
    Differencing disks depend on their parent's exact content. If you start a VM directly on a base image, or replace it, every VM built on it breaks. HV-LAB warns you before replacing an image.

At startup, HV-LAB makes the base images folder writable by administrators only. Everyone keeps read access, but a standard user can no longer replace a base.

## If the build fails

The error banner gives the reason and the path of the build log, which has the full details. Common causes:

- the ISO was dismounted during the build;
- the destination disk is full;
- the destination file is locked, for example by a running VM.

## Import a VHDX file

**Import a VHDX file** makes a base out of a disk that is already prepared: a sysprepped Windows, or a Linux with cloud-init (see [Linux bases](linux.md)).

1. **Browse**: choose the `.vhdx` or `.vhd` file. HV-LAB reads it mounted read-only and shows:
    - its generation: 2 for a GPT disk (UEFI), 1 for an MBR disk (BIOS);
    - its size;
    - for Windows, its version and whether it is sysprepped.
2. Check the **family**, the OS **name** (for example `WindowsServer2022Standard`) and the **version**, already filled by the analysis. The preview shows the `BASE_…` file name.
3. **Import as a base** copies the disk into the base images folder, with its sidecar (generation, Secure Boot).

The original file is never changed. A differencing disk is merged during the copy, and a `.vhd` is converted to `.vhdx`. A disk used by a running VM, or mounted, is refused.

!!! warning "Windows must be sysprepped"
    Run `sysprep /generalize /oobe /shutdown` in the original VM before copying its disk, and do not start it again. Otherwise the clones keep the same SID and skip the answer file: name, password, IP and domain are not applied. HV-LAB warns you and asks for a confirmation in that case.

## Delete an image

Use the delete button on the image's row and confirm. An image still used by a VM cannot be deleted: HV-LAB refuses and lists those VMs. Delete them first, otherwise they would no longer start.
