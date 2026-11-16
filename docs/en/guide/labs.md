# Lab blueprints

A blueprint describes a whole lab: its switch, its NAT network, its domain and its VMs. You compose it on the **Labs** page with a form, without writing any JSON: HV-LAB generates the file, then deploys the lab in one go.

Open the **Labs** page from the left menu, or with the **Labs** button of the **Virtual Machines** page. Blueprints live in `<VMs folder>\Blueprints`; **Open the folder** shows it in Explorer.

## Compose a lab

1. Click **New lab**. The form starts from an example: a domain controller, a member server and a Linux server, on the newest base images of the host.
2. **Lab** card: lab name (it becomes the Hyper-V group name), password, language and time zone.
3. **Network** card: the switch, picked from the list or typed (an unknown name is created as an internal switch), and the NAT created with it, with its gateway and prefix.
4. **Active Directory domain** card: the domain, the account allowed to join and the organizational unit. The VM ticked **Controller** creates the domain; only the VMs ticked **Domain** join it.
5. **Virtual machines** card, one row per VM:
    - **Name** and **Base image**, picked from the images of the host; `*` matches anything and the newest version is used;
    - **RAM (MB)**, **vCPU**, **Static IP** and **DNS**;
    - **Controller** and **Domain** for a Windows VM, **User** for a Linux VM;
    - the arrows change the creation order, the bin removes the VM;
    - **Add a VM** adds a row.
6. Click **Save**. An unsaved lab shows **Unsaved changes**, and HV-LAB asks before discarding them.

The **Generated JSON** panel shows live the file the form produces, password masked. **Open the JSON** opens it in Notepad, if you prefer the text.

A draft can be saved without a password; only deploying needs it.

## Check and deploy

**Check the plan** compares the lab with the host:

- **will be created**: the item does not exist yet;
- **already exists**: it is left unchanged;
- **impossible**: for example when no base image matches.

**Deploy** saves the lab, shows the plan, then asks for confirmation. HV-LAB creates only what is missing: the switch and its NAT, the [domain controller](#the-domain-controller) if there is one, then the other VMs in row order. It then puts the VMs in a Hyper-V group named after the lab.

When an item is **impossible**, nothing is deployed. Deploying the same lab again only creates what is missing, for example a VM you deleted.

!!! note "The VMs are not started"
    Only the domain controller is left running after the deployment. Select the VMs of the lab (the **Group** filter shows them), then use **Start in sequence**: they start one by one, in name order. See [Several VMs at once](virtual-machines.md#several-vms-at-once).

## Export VMs as a blueprint

**Export as blueprint** is in two places on the **Virtual Machines** page:

- in the bar shown when several VMs are selected: it exports the selection;
- in the **⋯** menu of a row, or its right-click menu: it exports that VM, or the selected VMs when the row is part of a multi-selection.

HV-LAB writes `<VMs folder>\Blueprints\<name>.json`, named after the group of the VMs, or else after the common start of their names; an existing file is never overwritten (`<name>-2.json`…). The **Labs** page then opens on that lab: its switch, its NAT and, for each VM, its base image, memory, processors and static IP. Fill in the password before you deploy it.

## File format

Comments (`//`) and trailing commas are allowed.

```json
// A domain controller, a member server and a Linux web server on a NAT
{
  "name": "LAB-AD",
  "switch": "LAB-NAT",
  "nat": { "gateway": "192.168.100.1", "prefixLength": 24 },
  "password": "Lab#2026",
  "domain": { "name": "lab.local", "user": "Administrator" },
  "vms": [
    { "name": "DC01",  "base": "WindowsServer2025*", "memoryMB": 4096, "ip": "192.168.100.10", "domainController": true },
    { "name": "SRV01", "base": "WindowsServer2025*", "ip": "192.168.100.11", "join": true },
    { "name": "WEB01", "base": "LinuxAlma*", "ip": "192.168.100.20", "user": "web" }
  ]
}
```

### The lab

| Field | Content | Default |
|---|---|---|
| `name` | Name of the lab, and of its Hyper-V group | Required |
| `switch` | Switch of every VM: used if it exists, else created as an internal switch | Required |
| `nat` | `gateway` and `prefixLength`: NAT network created with the switch. Its gateway is also the gateway of the static IPs. | None |
| `password` | Administrator password of the Windows VMs, user password of the Linux VMs, and password of the domain join account | Required |
| `language` | Language and keyboard of the VMs | `fr-FR` |
| `timeZone` | Time zone of the VMs | `Romance Standard Time` |
| `domain` | `name`, `user` (the account allowed to join, `Administrator` by default) and `ou` (optional): the domain joined by the VMs that have `join` | None |
| `vms` | The VMs, described below | Required |

The NAT is only created together with the switch. When the switch already exists, HV-LAB uses it as it is.

### Each VM

| Field | Content | Default |
|---|---|---|
| `name` | Name of the VM | Required |
| `base` | Pattern of the base's OS identifier, with `*`: the newest matching `BASE_*` image is used. For example `"WindowsServer2025*"` or `"LinuxAlma*"`. | Required |
| `memoryMB` | Memory, in MB | `2048` |
| `cpu` | Processors | `2` |
| `ip` | Static IPv4 address. It needs `nat`. | DHCP |
| `dns` | DNS servers, comma-separated | The lab's domain controller if there is one, else the NAT gateway |
| `domainController` | Windows Server: created first and promoted to domain controller of `domain` (one per lab) | `false` |
| `join` | Windows: join the domain of `domain` during setup | `false` |
| `user` | Linux: the sudo user | `labadmin` |

The OS identifier is the part of the file name that follows `BASE_`, for example `WindowsServer2025Standard(DesktopExperience)` or `LinuxAlmaLinux`. A Windows VM is created as with [Create a VM](create-vm.md), a Linux VM as a [Linux clone](linux.md).

## The domain controller

Tick **Controller** on a Windows Server VM with a static IP (`"domainController": true` in the file). When deploying, HV-LAB:

1. creates this VM before the others, whatever its row;
2. starts it and waits for Windows setup to finish;
3. installs AD DS and creates a new forest with its DNS server, through [PowerShell Direct](virtual-machines.md#automation-windows-guests);
4. restarts it and waits until the domain answers;
5. then creates the other VMs: the ones ticked **Domain** join the domain on their first boot.

Allow 10 to 15 minutes for a controller and its members. The other VMs of the lab use the controller as DNS server, unless their **DNS** field is filled in.

| Item | Value |
|---|---|
| NetBIOS name | The first part of the domain in uppercase: `lab.local` gives `LAB` |
| Domain administrator account | `LAB\Administrator`, with the lab password |
| Restore mode (DSRM) password | The lab password |

With a controller, the lab password must follow the Windows rule: 8 characters or more, from 3 of these 4 kinds: uppercase, lowercase, digits, symbols.

When the controller already exists, a new deployment that creates members checks its role first: it starts it if needed, promotes it if it is not one yet, then waits for the domain. A member already created without joining is left unchanged: delete it first, or join it by hand.
