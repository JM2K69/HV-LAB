# Base images

A base image is a generalized Windows installation in a `BASE_*.vhdx` file. VMs are then created on **differencing disks** of this file: each VM stores only its own changes, so creating a VM takes seconds.

The **Base VHDX Images** page lists the images found in the base images folder (see [Settings](settings.md)). The list shows the OS, version, edition, size and date of each image. Below the list is the form that builds a new image.

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

## If the build fails

The error banner gives the reason and the path of the build log, which has the full details. Common causes:

- the ISO was dismounted during the build;
- the destination disk is full;
- the destination file is locked, for example by a running VM.

## Delete an image

Use the delete button on the image's row and confirm. Delete the VMs that depend on the image first.
