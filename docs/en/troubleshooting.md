# Troubleshooting

## Application

Red banner "Hyper-V unavailable"
:   Enable Hyper-V, then restart the computer. Use either command:

    ```powershell
    Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All
    ```

    ```powershell
    .\Install.ps1 -EnableHyperV
    ```

    If only the `vmms` service is stopped, use the banner's **Start service** button.

"Access denied"
:   The application must run as administrator. It requests elevation at startup, so accept the UAC prompt.

The application does not start
:   Check the logs in `%LocalAppData%\HV-LAB\logs` and the Windows *Application* event log, then reinstall with `Install.ps1`.

Error banner on a page
:   The message is the error returned by PowerShell or Hyper-V, for example a switch name that is already used, or a file locked by a running VM.

## Base images

The analysis finds no image
:   Choose the `install.wim` or `install.esd` file in the `sources` folder of the mounted ISO, not the ISO itself.

"No index found" for the edition
:   The WIM does not contain that edition. Choose another edition or another OS family, or pick the index by hand.

The build fails
:   The banner gives the path of the build log. Check that:

    - the ISO is still mounted;
    - the destination disk has enough free space;
    - no VM uses the file being replaced.

## VMs

Windows Setup asks questions on first boot
:   The VM was created without an answer file (Quick VM, or **Use an answer file** unchecked). Answer them in the console, or recreate the VM with [Create a VM](guide/create-vm.md).

The VM does not start after its base image changed
:   A differencing disk only works with the exact parent it was created on. Recreate the VM on the current base image.

No Internet in the VM
:   Check that:

    - the VM is on the internal switch that has the [NAT network](guide/networking.md#nat-networks);
    - the VM has a static IP in the NAT subnet, with the NAT gateway as its default gateway and a DNS server.

## Report a problem

Open an issue on [GitHub](https://github.com/JM2K69/HV-LAB/issues). Include:

- the application version, shown in **Settings**;
- the Windows version of the host;
- the relevant log.
