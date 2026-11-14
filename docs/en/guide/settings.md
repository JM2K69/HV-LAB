# Settings

| Setting | Default | Role |
|---|---|---|
| Base VHDX images | `C:\HV-LAB\BaseImages` | Folder containing the `BASE_*.vhdx` files |
| Virtual machines | `C:\HV-LAB\VMs` | Folder where the VMs and their differencing disks are created |
| Application theme | System | **System** follows the Windows theme; **Light** or **Dark** forces one. |
| Interface language | Français | French or English. The change is immediate. |
| Enhanced session mode (Hyper-V host) | Host setting | Lets `vmconnect` use enhanced session: clipboard, sound, drives, any resolution. Read when the page opens, applied to the host with `Set-VMHost -EnableEnhancedSessionMode`. It is not saved in `settings.json`. |

Settings are saved in `%LocalAppData%\HV-LAB\settings.json`. Application logs are in `%LocalAppData%\HV-LAB\logs`.

!!! tip "Fast storage"
    Put both folders on an SSD. The base image is read by every VM that depends on it.
