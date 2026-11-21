# Settings

| Setting | Default | Role |
|---|---|---|
| Base VHDX images | `C:\HV-LAB\BaseImages` | Folder containing the `BASE_*.vhdx` files |
| Virtual machines | `C:\HV-LAB\VMs` | Folder where the VMs and their differencing disks are created |
| Application theme | System | **System** follows the Windows theme; **Light** or **Dark** forces one. |
| Interface language | Français | French or English. The change is immediate. |
| Enhanced session mode (Hyper-V host) | Host setting | Lets `vmconnect` use enhanced session: clipboard, sound, drives, any resolution. Read when the page opens, applied to the host with `Set-VMHost -EnableEnhancedSessionMode`. It is not saved in `settings.json`. |
| Custom product keys | None | Your keys for bases the KMS catalog does not know, such as Windows Insider builds. See [Custom product keys](#custom-product-keys). |
| Protect the password in the notes (DPAPI) | Off | Keeps the password of new VMs encrypted in their notes. See [Security](#security). |

Settings are saved in `%ProgramData%\HV-LAB\settings`, one file per Windows account. Application logs are in `%ProgramData%\HV-LAB\logs`. Only administrators may write in that folder: a program started without elevation cannot change the folders HV-LAB works in, while HV-LAB runs as administrator.

Versions 2.0 and earlier kept them in `%LocalAppData%\HV-LAB`: on its first start, HV-LAB takes those settings over.

!!! tip "Fast storage"
    Put both folders on an SSD. The base image is read by every VM that depends on it.

## Custom product keys

The KMS key catalog of HV-LAB only knows released versions of Windows, so a Windows Insider base, such as Windows Server Insider Preview, gets no key. Microsoft publishes the keys of these builds on their download page: add them in **Custom product keys**, in the **Product keys** section.

| Field | Role |
|---|---|
| Name | The name shown in the lists, for example `Server Insider Datacenter`. Without a name, the pattern is shown. |
| Identifier pattern | Compared with the identifier of the base, the `<identifier>` part of `BASE_<identifier>_<version>_<GB>.vhdx`. |
| Product key | In the `XXXXX-XXXXX-XXXXX-XXXXX-XXXXX` format. |

The pattern is compared like this:

- a word, such as `Insider`, matches an identifier that contains it, whatever the case;
- a pattern with `*` or `?`, such as `WindowsServerInsider*`, must cover the whole identifier;
- when several patterns match, the most specific one wins: `*Insider*Azure*` comes before `Insider` for a `WindowsServerInsiderDatacenterAzureEdition` base.

**Add key** saves it at once. A key added with a pattern already in the list replaces the old one. The bin button deletes a key, after a confirmation.

A custom key comes before the KMS catalog:

- when you [create a VM](create-vm.md#answer-file-unattendxml), it is suggested for the chosen base and listed first in **KMS key (GVLK)**, in the **Custom keys** group;
- when a [lab](labs.md) is deployed, each Windows VM gets the key that matches its base.

## Security

**Protect the password in the notes (DPAPI)**, in the **Security** section, is off by default. When it is on, each new VM keeps its password encrypted with DPAPI, for your Windows account on this host. Its [notes](create-vm.md#vm-notes) then hold this line instead of the password:

```text
Password (DPAPI): AQAAANCMnd8BFdERjHoAwE…
```

- HV-LAB can still copy the password and type it into the VM.
- Hyper-V Manager and `Get-VM` no longer show it in clear.
- Another Windows account, or another host, cannot decrypt it.

The VMs created before keep their notes as they are.
