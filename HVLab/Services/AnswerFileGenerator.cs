namespace HVLab.Services;

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
}

public static class AnswerFileGenerator
{
    public static string Generate(AnswerFileConfig c)
    {
        var productKey = !string.IsNullOrWhiteSpace(c.ProductKey)
            ? $"<ProductKey><Key>{X(c.ProductKey)}</Key></ProductKey>"
            : string.Empty;

        var autoLogon = c.AutoLogon ? $"""
                    <AutoLogon>
                        <Password><Value>{X(c.AdminPassword)}</Value><PlainText>true</PlainText></Password>
                        <Enabled>true</Enabled>
                        <LogonCount>3</LogonCount>
                        <Username>Administrator</Username>
                    </AutoLogon>
            """ : string.Empty;

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend">

                <settings pass="windowsPE">
                    <component name="Microsoft-Windows-International-Core-WinPE"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <SetupUILanguage><UILanguage>{c.UILanguage}</UILanguage></SetupUILanguage>
                        <InputLocale>{c.InputLocale}</InputLocale>
                        <SystemLocale>{c.SystemLocale}</SystemLocale>
                        <UILanguage>{c.UILanguage}</UILanguage>
                        <UserLocale>{c.UserLocale}</UserLocale>
                    </component>
                    <component name="Microsoft-Windows-Setup"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <UserData>
                            <AcceptEula>true</AcceptEula>
                            <FullName>{X(c.RegisteredOwner)}</FullName>
                            <Organization>{X(c.RegisteredOrganization)}</Organization>
                            {productKey}
                        </UserData>
                    </component>
                </settings>

                <settings pass="specialize">
                    <component name="Microsoft-Windows-Shell-Setup"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <ComputerName>{X(c.ComputerName)}</ComputerName>
                        <TimeZone>{c.TimeZone}</TimeZone>
                        <RegisteredOwner>{X(c.RegisteredOwner)}</RegisteredOwner>
                        <RegisteredOrganization>{X(c.RegisteredOrganization)}</RegisteredOrganization>
                    </component>
                </settings>

                <settings pass="oobeSystem">
                    <component name="Microsoft-Windows-Shell-Setup"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <OOBE>
                            <HideEULAPage>true</HideEULAPage>
                            <HideLocalAccountSetupPage>true</HideLocalAccountSetupPage>
                            <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                            <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                            <NetworkLocation>Work</NetworkLocation>
                            <SkipUserOOBE>true</SkipUserOOBE>
                            <SkipMachineOOBE>true</SkipMachineOOBE>
                            <ProtectYourPC>3</ProtectYourPC>
                        </OOBE>
                        <UserAccounts>
                            <AdministratorPassword>
                                <Value>{X(c.AdminPassword)}</Value>
                                <PlainText>true</PlainText>
                            </AdministratorPassword>
                        </UserAccounts>
                        {autoLogon}
                    </component>
                    <component name="Microsoft-Windows-International-Core"
                               processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35"
                               language="neutral" versionScope="nonSxS">
                        <InputLocale>{c.InputLocale}</InputLocale>
                        <SystemLocale>{c.SystemLocale}</SystemLocale>
                        <UILanguage>{c.UILanguage}</UILanguage>
                        <UserLocale>{c.UserLocale}</UserLocale>
                    </component>
                </settings>

            </unattend>
            """;
    }

    private static string X(string v) => v
        .Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("\"", "&quot;");

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
