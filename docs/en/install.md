# Installation

## Requirements

- **Windows 10/11** Pro, Enterprise or Education, or **Windows Server**.
- The **Hyper-V** feature, which the installer can enable for you.
- An **administrator** account, because the application requests elevation at startup.

There is nothing else to install. The release is self-contained and includes .NET 10 and the Windows App SDK.

## Install

1. Download `HV-LAB-X.Y.Z-win-x64.zip` from the [latest release](https://github.com/JM2K69/HV-LAB/releases/latest) and extract it.
2. Open PowerShell **as administrator** in the extracted folder and run:

    ```powershell
    Unblock-File .\Install.ps1
    .\Install.ps1
    ```

The application is copied to `C:\Program Files\HV-LAB`, and a shortcut is added to the Start menu.

!!! tip "Checking the download"
    Each release also publishes a `.sha256` file. Compare it with the hash of the zip:

    ```powershell
    (Get-FileHash .\HV-LAB-X.Y.Z-win-x64.zip -Algorithm SHA256).Hash
    ```

### Installer options

| Option | Effect |
|---|---|
| `-DesktopShortcut` | Also creates a shortcut on the public desktop. |
| `-EnableHyperV` | Enables the Hyper-V feature if it is missing. A restart is required. |
| `-Destination <folder>` | Installs somewhere else than `C:\Program Files\HV-LAB`. |
| `-Source <folder>` | Folder that contains `HVLab.exe`. The default is the script's own folder, or `HVLab\publish` when run from the sources. |
| `-Uninstall` | Removes the installation folder and the shortcuts. |

## First start

On startup, the application:

1. asks for elevation through UAC;
2. checks the Hyper-V module and the `vmms` service.

If Hyper-V is unavailable, a red banner explains why. When the service is only stopped, the banner offers a **Start service** button.

Then choose your folders in [Settings](guide/settings.md):

- the base images folder, `C:\HV-LAB\BaseImages` by default;
- the VMs folder, `C:\HV-LAB\VMs` by default.

## Update

Download the new release and run its `Install.ps1` in the same way:

- it closes the application if it is running;
- it replaces the installed files.

Your settings are kept, because they live in `%LocalAppData%\HV-LAB`.

## Uninstall

```powershell
& "C:\Program Files\HV-LAB\Install.ps1" -Uninstall
```

The settings and logs in `%LocalAppData%\HV-LAB` are kept, and so are your VMs and images. Hyper-V objects and VHDX files are never deleted by the uninstaller.
