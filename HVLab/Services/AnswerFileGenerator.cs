namespace HVLab.Services;

public enum OsFamily { Server, Client }

public class AnswerFileConfig
{
    public string ComputerName { get; set; } = "LAB-VM";
    public string AdminPassword { get; set; } = "P@ssw0rd!";
    public string ProductKey { get; set; } = "";
    public string UILanguage { get; set; } = "fr-FR";
    public string InputLocale { get; set; } = "040c:0000040c";
    public string SystemLocale { get; set; } = "fr-FR";
    public string UserLocale { get; set; } = "fr-FR";
    public string TimeZone { get; set; } = "Romance Standard Time";
    public int ImageIndex { get; set; } = 1;
    public bool AutoLogon { get; set; } = true;
    public string RegisteredOwner { get; set; } = "HV-LAB";
    public string RegisteredOrganization { get; set; } = "HV-LAB";
    /// <summary>Windows Server only — injects a FirstLogonCommand that builds the CBS feature cache, runs DISM cleanup, then reboots.</summary>
    public bool BuildCbsCache { get; set; } = false;
    /// <summary>Injects Microsoft-Windows-WindowsUpdate-AU in specialize pass to disable automatic updates (Server and Client).</summary>
    public bool DisableWindowsUpdate { get; set; } = false;
    /// <summary>Controls which OOBE elements are emitted. Server omits client-only nodes.</summary>
    public OsFamily OsFamily { get; set; } = OsFamily.Server;
}

public static class AnswerFileGenerator
{
    public static string Generate(AnswerFileConfig c)
    {
        // ProductKey in specialize pass (windowsPE pass is ignored on pre-deployed VHDx)
        var productKeySpecialize = !string.IsNullOrWhiteSpace(c.ProductKey)
            ? $"<ProductKey>{X(c.ProductKey)}</ProductKey>"
            : string.Empty;

        var firstLogonCommands = c.BuildCbsCache ? BuildCbsCacheFirstLogonCommands() : string.Empty;

        var windowsUpdateBlock = c.DisableWindowsUpdate
            ? """

                    <component name="Microsoft-Windows-WindowsUpdate-AU"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <NoAutoUpdate>true</NoAutoUpdate>
                        <AUOptions>1</AUOptions>
                    </component>
              """
            : string.Empty;

        var autoLogon = c.AutoLogon ? $"""
                    <AutoLogon>
                        <Password><Value>{X(c.AdminPassword)}</Value><PlainText>true</PlainText></Password>
                        <Enabled>true</Enabled>
                        <LogonCount>3</LogonCount>
                        <Username>Administrator</Username>
                    </AutoLogon>
            """ : string.Empty;

        // OOBE block: client OS has extra nodes not present on Windows Server
        var oobeBlock = c.OsFamily == OsFamily.Client
            ? """
                        <OOBE>
                            <HideEULAPage>true</HideEULAPage>
                            <HideLocalAccountSetupPage>true</HideLocalAccountSetupPage>
                            <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                            <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                            <SkipUserOOBE>true</SkipUserOOBE>
                            <SkipMachineOOBE>true</SkipMachineOOBE>
                            <ProtectYourPC>3</ProtectYourPC>
                        </OOBE>
              """
            : """
                        <OOBE>
                            <HideEULAPage>true</HideEULAPage>
                            <ProtectYourPC>3</ProtectYourPC>
                        </OOBE>
              """;

        // AutoLogon on client uses a local user account; on Server Administrator is built-in
        var userAccounts = c.OsFamily == OsFamily.Client
            ? $"""
                        <UserAccounts>
                            <LocalAccounts>
                                <LocalAccount wcm:action="add">
                                    <Password><Value>{X(c.AdminPassword)}</Value><PlainText>true</PlainText></Password>
                                    <DisplayName>Administrator</DisplayName>
                                    <Group>Administrators</Group>
                                    <Name>Administrator</Name>
                                </LocalAccount>
                            </LocalAccounts>
                        </UserAccounts>
              """
            : $"""
                        <UserAccounts>
                            <AdministratorPassword>
                                <Value>{X(c.AdminPassword)}</Value>
                                <PlainText>true</PlainText>
                            </AdministratorPassword>
                        </UserAccounts>
              """;

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend"
                      xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">

                <settings pass="specialize">
                    <component name="Microsoft-Windows-Shell-Setup"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <ComputerName>{X(c.ComputerName)}</ComputerName>
                        <TimeZone>{c.TimeZone}</TimeZone>
                        <RegisteredOwner>{X(c.RegisteredOwner)}</RegisteredOwner>
                        <RegisteredOrganization>{X(c.RegisteredOrganization)}</RegisteredOrganization>
                        {productKeySpecialize}
                    </component>
                    <component name="Microsoft-Windows-International-Core"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <InputLocale>{c.InputLocale}</InputLocale>
                        <SystemLocale>{c.SystemLocale}</SystemLocale>
                        <UILanguage>{c.UILanguage}</UILanguage>
                        <UserLocale>{c.UserLocale}</UserLocale>
                    </component>
                    {windowsUpdateBlock}
                </settings>

                <settings pass="oobeSystem">
                    <component name="Microsoft-Windows-Shell-Setup"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        {oobeBlock}
                        {userAccounts}
                        {autoLogon}
                        {firstLogonCommands}
                    </component>
                </settings>

            </unattend>
            """;
    }

    private static string X(string v) => v
        .Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string BuildCbsCacheFirstLogonCommands()
    {
        // PowerShell script displayed in a visible console window — no user interaction required.
        // Encoded as UTF-16LE Base64 so special characters survive unattend XML embedding.
        const string psScript = """
            $host.UI.RawUI.WindowTitle = 'HV-LAB - Initialisation du cache CBS'
            Write-Host ''
            Write-Host '======================================================' -ForegroundColor Cyan
            Write-Host '   HV-LAB : Initialisation du cache CBS Windows Server ' -ForegroundColor Cyan
            Write-Host '======================================================' -ForegroundColor Cyan
            Write-Host ''
            Write-Host 'Cette operation est entierement automatique.' -ForegroundColor Yellow
            Write-Host 'Aucune intervention de votre part n est requise.' -ForegroundColor Yellow
            Write-Host ''
            Write-Host '[1/4] Cache des fonctionnalites Windows (Get-WindowsFeature)...' -ForegroundColor White
            Get-WindowsFeature | Out-Null
            Write-Host '[2/4] Fonctionnalites optionnelles (Get-WindowsOptionalFeature)...' -ForegroundColor White
            Get-WindowsOptionalFeature -Online | Out-Null
            Write-Host '[3/4] Nettoyage des composants DISM (StartComponentCleanup)...' -ForegroundColor White
            & dism.exe /Online /Cleanup-Image /StartComponentCleanup
            Write-Host ''
            Write-Host '[4/4] Initialisation terminee. Redemarrage dans 15 secondes...' -ForegroundColor Green
            Start-Sleep -Seconds 15
            Restart-Computer -Force
            """;

        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
        return $"""
                    <FirstLogonCommands>
                        <SynchronousCommand wcm:action="add">
                            <Order>1</Order>
                            <CommandLine>powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}</CommandLine>
                            <Description>HV-LAB CBS Cache Init</Description>
                        </SynchronousCommand>
                    </FirstLogonCommands>
            """;
    }

    public static string GetInputLocale(string lang) => lang switch
    {
        "fr-FR" => "040c:0000040c",
        "en-US" => "0409:00000409",
        "en-GB" => "0809:00000809",
        "de-DE" => "0407:00000407",
        "es-ES" => "0c0a:0000040a",
        "it-IT" => "0410:00000410",
        _       => "0409:00000409",
    };
}
