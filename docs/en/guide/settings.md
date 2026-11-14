# Settings

| Setting | Default | Role |
|---|---|---|
| Base VHDX images | `C:\HV-LAB\BaseImages` | Folder containing the `BASE_*.vhdx` files |
| Virtual machines | `C:\HV-LAB\VMs` | Folder where the VMs and their differencing disks are created |
| Application theme | System | **System** follows the Windows theme; **Light** or **Dark** forces one. |
| Interface language | Français | French or English. The change is immediate. |
| Enhanced session mode (Hyper-V host) | Host setting | Lets `vmconnect` use enhanced session: clipboard, sound, drives, any resolution. Read when the page opens, applied to the host with `Set-VMHost -EnableEnhancedSessionMode`. It is not saved in `settings.json`. |
| Protect the password in the notes (DPAPI) | Off | Keeps the password of new VMs encrypted in their notes. See [Security](#security). |

Settings are saved in `%LocalAppData%\HV-LAB\settings.json`. Application logs are in `%LocalAppData%\HV-LAB\logs`.

!!! tip "Fast storage"
    Put both folders on an SSD. The base image is read by every VM that depends on it.

## Security

**Protect the password in the notes (DPAPI)**, in the **Security** section, is off by default. When it is on, each new VM keeps its password encrypted with DPAPI, for your Windows account on this host. Its [notes](create-vm.md#vm-notes) then hold this line instead of the password:

```text
Password (DPAPI): AQAAANCMnd8BFdERjHoAwE…
```

- HV-LAB can still copy the password and type it into the VM.
- Hyper-V Manager and `Get-VM` no longer show it in clear.
- Another Windows account, or another host, cannot decrypt it.

The VMs created before keep their notes as they are.
