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

The application is copied to `C:\Program Files\HV-LAB`, and a shortcut is added to the Start menu. HV-LAB also appears in **Settings > Apps**, with its version and publisher.

!!! tip "Checking the download"
    Each release also publishes a `.sha256` file. Compare it with the hash of the zip:

    ```powershell
    (Get-FileHash .\HV-LAB-X.Y.Z-win-x64.zip -Algorithm SHA256).Hash
    ```

    In the releases after 2.0.0, the zip also has a signed provenance attestation: it proves that the workflow of this repository built it from the version's tag. With the [GitHub CLI](https://cli.github.com/):

    ```powershell
    gh attestation verify .\HV-LAB-X.Y.Z-win-x64.zip --repo JM2K69/HV-LAB
    ```

### Installer options

| Option | Effect |
|---|---|
| `-DesktopShortcut` | Also creates a shortcut on the public desktop. |
| `-EnableHyperV` | Enables the Hyper-V feature if it is missing. A restart is required. |
| `-Destination <folder>` | Installs somewhere else than `C:\Program Files\HV-LAB`. |
| `-Source <folder>` | Folder that contains `HVLab.exe`. The default is the script's own folder, or `HVLab\publish` when run from the sources. |
| `-SelfSign` | Signs `HVLab.exe` with a self-signed `CN=JM2K69` certificate trusted by **this machine only**, so the UAC prompt shows *Verified publisher: JM2K69* instead of *Unknown publisher*. The certificate is created once (non-exportable private key, code signing only, valid 10 years) and reused: later installations sign the exe without the option. |
| `-Uninstall` | Removes the installation folder, the shortcuts, the **Settings > Apps** entry and the certificate created by `-SelfSign`. |

!!! note "Why a self-signed certificate"
    Windows shows a publisher only for a signed exe whose certificate the machine trusts. A public certificate authority certifies a legal name, never a pseudonym, and charges for it. `-SelfSign` adds the certificate to the *Trusted Root* and *Trusted Publishers* stores of the computer: on another machine, the release zip still shows *Unknown publisher*.

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

1. it prepares the new version in a side folder, `HV-LAB.new`, while the installed one stays in place;
2. it asks the running application to close, and stops it if it is still open after 10 seconds;
3. it swaps the two folders.

If the copy fails, the installed version is left intact. Your settings are kept, because they live in `%ProgramData%\HV-LAB`.

## Uninstall

Uninstall HV-LAB from **Settings > Apps**, like any application, or run:

```powershell
& "C:\Program Files\HV-LAB\Install.ps1" -Uninstall
```

The settings and logs in `%ProgramData%\HV-LAB` are kept, and so are your VMs and images. Hyper-V objects and VHDX files are never deleted by the uninstaller.
