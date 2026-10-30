# Create a VM

The **Create VM** page creates one or more VMs on a differencing disk of a [base image](base-images.md). An `unattend.xml` answer file is injected, so Windows configures itself on first boot without any question.

## The form

### Identity

- **VM name**: the name shown in Hyper-V.
- **Bulk creation**: creates several numbered VMs at once. Set a **prefix** and a **number of VMs**:
    - prefix `SRV` with 3 VMs gives `SRV-01`, `SRV-02` and `SRV-03`;
    - the **preview** shows the names before creation.

### Hardware

| Field | Choices |
|---|---|
| Generation | 1 or 2, the same as the base image |
| Memory (MB) | 512 to 16384 |
| Processors | 1 to 16 |

### Network and storage

- **Virtual switch**: the switch the VM's network adapter connects to. See [Switches & NAT](networking.md).
- **Base VHDX image**: the parent of the differencing disk.
- **Destination folder**: where the VM and its disk are created. The default is the VMs folder from [Settings](settings.md).

## Answer file (unattend.xml)

Leave **Use an answer file** checked to get an unattended setup.

| Field | Notes |
|---|---|
| Computer name | 15 characters at most. In bulk mode each VM gets its own name. |
| Administrator password | **Required**. Use the eye button to show or hide it. |
| Language / Locale | `fr-FR`, `en-US`, `en-GB`, `de-DE`, `es-ES` or `it-IT`. Sets the UI language, system locale, user locale and keyboard. |
| Time zone | For example `Romance Standard Time` (Paris) or `Pacific Standard Time`. |
| KMS key (GVLK) | Microsoft's public KMS client keys. The key that matches the base image is suggested and copied into **Product key**. |
| Product key (optional) | Any key, set during the *specialize* pass. Leave it empty to skip it. |

### Options

- **Automatic logon**:
    - logs on as Administrator for the first three logons;
    - off by default, because Windows then stores the password in plain text in the VM's registry.
- **Build the CBS feature cache on first boot** (Windows Server only). At the first logon, a console window:
    1. runs `Get-WindowsFeature` and `Get-WindowsOptionalFeature`;
    2. cleans up the component store with DISM;
    3. restarts the VM after 15 seconds.

    The later *Add Roles and Features* operations are then faster.
- **Disable automatic Windows Update**: turns off automatic updates in the VM.

### Preview

The **unattend.xml preview** shows the file exactly as it will be injected, with the password masked. Click **Update preview** after a change.

## Accounts

HV-LAB **does not create any extra account**. The answer file only sets the password of the built-in **Administrator** account:

- on **Windows Server**, the setup no longer asks for the Administrator password;
- on **Windows 10/11**, the account-creation screens are hidden, and Windows Setup enables the built-in Administrator.

Log on as `Administrator` with the password you entered.

## What happens on creation

1. The differencing disk is created on the base image.
2. Its Windows volume is mounted on a private folder, without a drive letter, and `unattend.xml` is copied into `Windows\Panther`.
3. The VM is created and attached to the switch.
4. On first boot, Windows Setup runs the *specialize* and *oobeSystem* passes, then shows the logon screen.

!!! note "The answer file is deleted"
    `unattend.xml` contains the password in plain text. A first-logon command deletes `%WINDIR%\Panther\unattend.xml` from the VM.

!!! tip "Without an answer file"
    Uncheck **Use an answer file** to get the normal Windows OOBE, where you answer the setup questions in the VM console.
