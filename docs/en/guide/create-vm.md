# Create a VM

The **Create VM** page creates one or more VMs on a differencing disk of a [base image](base-images.md). An `unattend.xml` answer file is injected, so Windows configures itself on first boot without any question.

## The form

### Identity

- **VM name**: the name shown in Hyper-V. A name already used by a VM of the host is refused.
- **Bulk creation**: creates several numbered VMs at once. Set a **prefix** and a **number of VMs**:
    - prefix `SRV` with 3 VMs gives `SRV-01`, `SRV-02` and `SRV-03`;
    - the **preview** shows the names before creation;
    - the series is put in a Hyper-V VM group named after the prefix (option on by default), which filters and drives it from the [VM list](virtual-machines.md#several-vms-at-once).

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
| KMS key (GVLK) | Microsoft's public KMS client keys, after your [custom keys](settings.md#custom-product-keys). The key that matches the base image is suggested and copied into **Product key**. |
| Product key (optional) | Any key, set during the *specialize* pass. Leave it empty to skip it. |

### Static IP address

**Static IP address (DHCP otherwise)** gives the VM a fixed address, on a Windows base as on a [Linux](linux.md) one. Turned on, it asks for:

- the **IP address (1st VM)** and the **Prefix (/)**;
- the **Gateway**;
- the **DNS servers**, comma-separated.

On a Windows base, the VM's network adapter gets a static MAC address (`00-15-5D-…`), and `unattend.xml` sets the IP, prefix, gateway and DNS on that adapter. In bulk mode, each VM gets the next address: `SRV-01` gets `192.168.100.10`, `SRV-02` gets `192.168.100.11`, and so on. The address is written in the [notes](#vm-notes).

!!! info "NAT networks"
    The [NAT networks](networking.md#nat-networks) of HV-LAB have no DHCP. Use a static IP, with the NAT gateway as the gateway.

### Join an Active Directory domain

On a Windows base, **Join an Active Directory domain** joins the VM to a domain during setup, in the *specialize* pass.

| Field | Notes |
|---|---|
| Domain | For example `lab.local`. |
| Organizational unit (optional) | For example `OU=Servers,DC=lab,DC=local`. Leave it empty for the default container. |
| Account allowed to join | `DOMAIN\user`, `user@domain`, or a user of the domain such as `Administrator`. |
| Account password | The password of that account. |

The VM must reach a domain controller during setup. With a static IP, put the address of the domain controller as the DNS server.

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

## VM notes

HV-LAB writes the creation date and the credentials into the VM's Hyper-V **Notes**, so you can find them later. In a bulk series, every VM gets its own notes.

```text
Created by HV-LAB on 2026-11-02 14:35
Account: Administrator
Password: Lab#2026
IP: 192.168.100.10
```

The `IP` line only appears with a [static IP](#static-ip-address). To read the notes:

- in Hyper-V Manager: the **Summary** tab of the VM, or **Settings > Name**;
- in PowerShell:

    ```powershell
    (Get-VM SRV-01).Notes
    ```

Without an answer file, the notes only hold the creation date.

!!! warning "Lab use"
    By default, the password is stored in clear text in the notes, and any administrator of the host can read it. Never reuse a production password in a lab. To keep it encrypted, turn on **Protect the password in the notes (DPAPI)** in [Settings](settings.md#security).

## What happens on creation

Click **Create VM**. If something goes wrong, a red banner next to the button gives the reason. When the VM is ready, a green banner offers **Open the VM**, which opens its [window](virtual-machines.md#vm-window).

1. The differencing disk is created on the base image.
2. Its Windows volume is mounted on a private folder, without a drive letter, and `unattend.xml` is copied into `Windows\Panther`.
3. The VM is created with the lab settings below, attached to the switch, and its [notes](#vm-notes) are written. With a static IP, its adapter gets the static MAC address that `unattend.xml` names.
4. On first boot, Windows Setup runs the *specialize* pass (computer name, static IP, domain join) and the *oobeSystem* pass, then shows the logon screen.

Every VM that HV-LAB creates, here or with [Quick VM](quick-vm.md), gets lab settings:

- no automatic checkpoints;
- the automatic stop action **Shut down**: the VM shuts down with the host instead of saving its memory to disk;
- the *Guest Service Interface* integration service turned on, for [Copy a file to the VM…](virtual-machines.md#automation-windows-guests).

If the creation fails, times out or is cancelled, HV-LAB removes only what it made: the disks are dismounted, then the VM and its folder are deleted.

!!! note "The answer file is deleted"
    `unattend.xml` contains the password in plain text. A first-logon command deletes `%WINDIR%\Panther\unattend.xml` from the VM.

!!! tip "Without an answer file"
    Uncheck **Use an answer file** to get the normal Windows OOBE, where you answer the setup questions in the VM console.
