namespace HVLab.Services;

public enum OsFamily { Server, Client }

public class AnswerFileConfig
{
    public string ComputerName { get; set; } = "LAB-VM";
    /// <summary>No default: the user must type one (validated before creation).</summary>
    public string AdminPassword { get; set; } = "";
    public string ProductKey { get; set; } = "";
    public string UILanguage { get; set; } = "fr-FR";
    public string InputLocale { get; set; } = "040c:0000040c";
    public string SystemLocale { get; set; } = "fr-FR";
    public string UserLocale { get; set; } = "fr-FR";
    public string TimeZone { get; set; } = "Romance Standard Time";
    public int ImageIndex { get; set; } = 1;
    /// <summary>Off by default: auto-logon stores the password in plain text in the registry.</summary>
    public bool AutoLogon { get; set; } = false;
    public string RegisteredOwner { get; set; } = "HV-LAB";
    public string RegisteredOrganization { get; set; } = "HV-LAB";
    /// <summary>Windows Server only — injects a FirstLogonCommand that builds the CBS feature cache, runs DISM cleanup, then reboots.</summary>
    public bool BuildCbsCache { get; set; } = false;
    /// <summary>Injects Microsoft-Windows-WindowsUpdate-AU in specialize pass to disable automatic updates (Server and Client).</summary>
    public bool DisableWindowsUpdate { get; set; } = false;
    /// <summary>Controls which OOBE elements are emitted. Server omits client-only nodes.</summary>
    public OsFamily OsFamily { get; set; } = OsFamily.Server;

    /// <summary>Static IPv4 (null = DHCP), set on the adapter whose MAC is <see cref="MacAddress"/>.</summary>
    public StaticIpConfig? StaticIp { get; set; }
    /// <summary>Static MAC HV-LAB gives the VM's adapter (00-15-5D-...): the interface identifier of the TCPIP settings.</summary>
    public string MacAddress { get; set; } = "";
    /// <summary>Active Directory domain joined during setup; empty = workgroup.</summary>
    public string JoinDomain { get; set; } = "";
    /// <summary>Account allowed to join: DOMAIN\user, user@domain, or a user of <see cref="JoinDomain"/>.</summary>
    public string DomainUser { get; set; } = "";
    public string DomainPassword { get; set; } = "";
    /// <summary>Optional OU (distinguished name) of the computer account.</summary>
    public string DomainOu { get; set; } = "";
}

public static class AnswerFileGenerator
{
    /// <summary>Built-in account name of the English images HV-LAB builds its bases from (the only account set).</summary>
    public const string AdministratorAccount = "Administrator";

    public static string Generate(AnswerFileConfig c)
    {
        // ProductKey in specialize pass (windowsPE pass is ignored on pre-deployed VHDx)
        var productKeySpecialize = !string.IsNullOrWhiteSpace(c.ProductKey)
            ? $"<ProductKey>{X(c.ProductKey)}</ProductKey>"
            : string.Empty;

        var firstLogonCommands = BuildFirstLogonCommands(c.BuildCbsCache);

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

        var networkBlock = NetworkBlock(c);
        var joinBlock    = JoinBlock(c);

        var autoLogonUser = AdministratorAccount;

        var autoLogon = c.AutoLogon ? $"""
                    <AutoLogon>
                        <Password><Value>{X(c.AdminPassword)}</Value><PlainText>true</PlainText></Password>
                        <Enabled>true</Enabled>
                        <LogonCount>3</LogonCount>
                        <Username>{autoLogonUser}</Username>
                        <Domain>.</Domain>
                    </AutoLogon>
            """ : string.Empty;

        // OOBE block: keep only cross-version safe nodes to reduce client/server OOBE parsing issues.
        // Windows 10/11 (client only): no account-creation screens, since no extra account is created; with
        // HideLocalAccountScreen and an AdministratorPassword, Setup enables the built-in Administrator.
        var clientOobe = c.OsFamily == OsFamily.Client
            ? """

                            <HideLocalAccountScreen>true</HideLocalAccountScreen>
                            <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
              """
            : string.Empty;
        var oobeBlock = $"""
                        <OOBE>
                            <HideEULAPage>true</HideEULAPage>{clientOobe}
                            <ProtectYourPC>3</ProtectYourPC>
                        </OOBE>
              """;

        // Only the built-in Administrator (no extra local account). AdministratorPassword locates it by its SID,
        // so its localized name does not matter; Windows Server OOBE otherwise stops to ask for its password.
        var userAccounts = $"""
                        <UserAccounts>
                            <AdministratorPassword><Value>{X(c.AdminPassword)}</Value><PlainText>true</PlainText></AdministratorPassword>
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
                    {windowsUpdateBlock}{networkBlock}{joinBlock}
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

    private const string Component =
        """processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" """;

    /// <summary>
    /// Static IPv4, default route and DNS servers (specialize pass) on the adapter identified by its MAC: HV-LAB
    /// sets that MAC static on the VM, so the right interface gets the address whatever Windows names it.
    /// </summary>
    internal static string NetworkBlock(AnswerFileConfig c)
    {
        if (c.StaticIp is not { } ip || c.MacAddress.Length == 0) return "";
        var mac = X(c.MacAddress);
        var dns = string.Join("", ip.DnsServers.Select((server, i) =>
            $"""<IpAddress wcm:action="add" wcm:keyValue="{i + 1}">{X(server)}</IpAddress>"""));
        var dnsComponent = ip.DnsServers.Count == 0 ? "" : $"""

                    <component name="Microsoft-Windows-DNS-Client" {Component}>
                        <Interfaces>
                            <Interface wcm:action="add">
                                <Identifier>{mac}</Identifier>
                                <DNSServerSearchOrder>{dns}</DNSServerSearchOrder>
                            </Interface>
                        </Interfaces>
                    </component>
            """;
        return $"""

                    <component name="Microsoft-Windows-TCPIP" {Component}>
                        <Interfaces>
                            <Interface wcm:action="add">
                                <Identifier>{mac}</Identifier>
                                <Ipv4Settings><DhcpEnabled>false</DhcpEnabled></Ipv4Settings>
                                <UnicastIpAddresses>
                                    <IpAddress wcm:action="add" wcm:keyValue="1">{X(ip.Address)}/{ip.PrefixLength}</IpAddress>
                                </UnicastIpAddresses>
                                <Routes>
                                    <Route wcm:action="add">
                                        <Identifier>1</Identifier>
                                        <Prefix>0.0.0.0/0</Prefix>
                                        <NextHopAddress>{X(ip.Gateway)}</NextHopAddress>
                                    </Route>
                                </Routes>
                            </Interface>
                        </Interfaces>
                    </component>{dnsComponent}
            """;
    }

    /// <summary>
    /// Domain join during setup (specialize pass). The DNS servers above must reach a domain controller.
    /// "LAB\admin" or "admin@lab.local" give their own domain; a bare user name belongs to <see cref="AnswerFileConfig.JoinDomain"/>.
    /// </summary>
    internal static string JoinBlock(AnswerFileConfig c)
    {
        if (c.JoinDomain.Length == 0) return "";
        var (domain, user) = c.DomainUser.Split('\\') is [var d, var u] ? (d, u)
                           : c.DomainUser.Split('@') is [var u2, var d2] ? (d2, u2)
                           : (c.JoinDomain, c.DomainUser);
        var ou = c.DomainOu.Length > 0 ? $"<MachineObjectOU>{X(c.DomainOu)}</MachineObjectOU>" : "";
        return $"""

                    <component name="Microsoft-Windows-UnattendedJoin" {Component}>
                        <Identification>
                            <Credentials>
                                <Domain>{X(domain)}</Domain>
                                <Username>{X(user)}</Username>
                                <Password>{X(c.DomainPassword)}</Password>
                            </Credentials>
                            <JoinDomain>{X(c.JoinDomain)}</JoinDomain>
                            {ou}
                        </Identification>
                    </component>
            """;
    }

    // Deletes the answer file (plain-text password) left in the guest's Panther folder.
    private const string DeleteUnattendCommand =
        @"cmd.exe /c del /f /q %WINDIR%\Panther\unattend.xml";

    private static string BuildFirstLogonCommands(bool buildCbsCache)
    {
        var cbs = buildCbsCache ? BuildCbsCacheCommand(order: 2) : string.Empty;
        return $"""
                    <FirstLogonCommands>
                        <SynchronousCommand wcm:action="add">
                            <Order>1</Order>
                            <CommandLine>{DeleteUnattendCommand}</CommandLine>
                            <Description>HV-LAB remove unattend.xml</Description>
                        </SynchronousCommand>{cbs}
                    </FirstLogonCommands>
            """;
    }

    private static string BuildCbsCacheCommand(int order)
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

                        <SynchronousCommand wcm:action="add">
                            <Order>{order}</Order>
                            <CommandLine>powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}</CommandLine>
                            <Description>HV-LAB CBS Cache Init</Description>
                        </SynchronousCommand>
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
