# Settings

| Setting | Default | Role |
|---|---|---|
| Base VHDX images | `C:\HV-LAB\BaseImages` | Folder containing the `BASE_*.vhdx` files |
| Virtual machines | `C:\HV-LAB\VMs` | Folder where the VMs and their differencing disks are created |
| Application theme | System | **System** follows the Windows theme; **Light** or **Dark** forces one. |
| Interface language | Français | French or English. The change is immediate. |

Settings are saved in `%LocalAppData%\HV-LAB\settings.json`. Application logs are in `%LocalAppData%\HV-LAB\logs`.

!!! tip "Fast storage"
    Put both folders on an SSD. The base image is read by every VM that depends on it.
