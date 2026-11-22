using System.Xml.Linq;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.Core.Tests;

public class AnswerFileTests
{
    private static readonly XNamespace U = "urn:schemas-microsoft-com:unattend";

    private static XDocument Generate(Action<AnswerFileConfig>? configure = null)
    {
        var config = new AnswerFileConfig { ComputerName = "LAB-01", AdminPassword = "Secret1!" };
        configure?.Invoke(config);
        return XDocument.Parse(AnswerFileGenerator.Generate(config));
    }

    [Fact]
    public void Defaults_have_no_password_and_no_autologon()
    {
        var config = new AnswerFileConfig();
        Assert.Equal("", config.AdminPassword);
        Assert.False(config.AutoLogon);
        Assert.Empty(Generate().Descendants(U + "AutoLogon"));
    }

    [Theory]
    [InlineData(OsFamily.Server, false, null)]
    [InlineData(OsFamily.Server, true, "Administrator")]
    [InlineData(OsFamily.Client, false, null)]
    [InlineData(OsFamily.Client, true, "Admins")]
    public void AutoLogon_is_emitted_only_when_enabled_for_the_account_of_the_family(OsFamily family, bool enabled, string? user)
        => Assert.Equal(user, Generate(c => { c.OsFamily = family; c.AutoLogon = enabled; })
                                  .Descendants(U + "AutoLogon").SingleOrDefault()?.Element(U + "Username")?.Value);

    [Fact]
    public void Special_characters_in_password_are_escaped()
    {
        var doc = Generate(c => c.AdminPassword = "a<b&\"c>");
        var value = doc.Descendants(U + "AdministratorPassword").Descendants(U + "Value").Single().Value;
        Assert.Equal("a<b&\"c>", value);
    }

    [Fact]
    public void Windows_server_only_uses_its_built_in_administrator()
    {
        Assert.Empty(Generate(c => c.OsFamily = OsFamily.Server).Descendants(U + "LocalAccounts"));
        Assert.Equal("Administrator", AnswerFileGenerator.AccountFor(OsFamily.Server));
    }

    [Fact]
    public void A_windows_client_gets_an_admins_account_with_the_password()
    {
        // Windows 11 OOBE asks "Who's going to use this device?" without an account of its own
        var account = Generate(c => { c.OsFamily = OsFamily.Client; c.AdminPassword = "P<w&d"; })
                          .Descendants(U + "LocalAccount").Single();
        Assert.Equal("Admins", account.Element(U + "Name")!.Value);
        Assert.Equal("Administrators", account.Element(U + "Group")!.Value);
        Assert.Equal("P<w&d", account.Element(U + "Password")!.Element(U + "Value")!.Value);
        Assert.Equal("Admins", AnswerFileGenerator.AccountFor(OsFamily.Client));
    }

    [Theory]
    [InlineData(OsFamily.Server, 0)]
    [InlineData(OsFamily.Client, 1)]
    public void Client_oobe_skips_the_account_creation_screens(OsFamily family, int expected)
    {
        var oobe = Generate(c => c.OsFamily = family).Descendants(U + "OOBE").Single();
        Assert.Equal(expected, oobe.Elements(U + "HideLocalAccountScreen").Count());
        Assert.Equal(expected, oobe.Elements(U + "HideOnlineAccountScreens").Count());
    }

    [Theory]
    [InlineData(OsFamily.Server)]
    [InlineData(OsFamily.Client)]
    public void Built_in_administrator_password_is_set_so_oobe_does_not_ask_for_it(OsFamily family)
    {
        var accounts = Generate(c => { c.OsFamily = family; c.AdminPassword = "P<w&d"; })
            .Descendants(U + "UserAccounts").Single();
        var admin = accounts.Element(U + "AdministratorPassword");
        Assert.NotNull(admin);
        Assert.Equal("P<w&d", admin.Element(U + "Value")!.Value);
        Assert.Equal("true", admin.Element(U + "PlainText")!.Value);
        // Schema order: AdministratorPassword comes before LocalAccounts
        Assert.Equal("AdministratorPassword", accounts.Elements().First().Name.LocalName);
    }

    [Fact]
    public void First_logon_deletes_the_cached_answer_file()
    {
        var commands = Generate().Descendants(U + "SynchronousCommand").ToList();
        Assert.Contains(commands, c => c.Element(U + "CommandLine")!.Value.Contains(@"Panther\unattend.xml"));
        Assert.Equal("1", commands[0].Element(U + "Order")!.Value);
    }

    [Fact]
    public void Cbs_cache_command_comes_after_the_cleanup()
    {
        var orders = Generate(c => c.BuildCbsCache = true)
            .Descendants(U + "SynchronousCommand").Select(c => c.Element(U + "Order")!.Value).ToList();
        Assert.Equal(["1", "2"], orders);
    }

    [Fact]
    public void Product_key_is_set_in_specialize_only_when_provided()
    {
        Assert.Empty(Generate().Descendants(U + "ProductKey"));
        Assert.Equal("W269N-WFGWX-YVC9B-4J6C9-T83GX",
            Generate(c => c.ProductKey = "W269N-WFGWX-YVC9B-4J6C9-T83GX").Descendants(U + "ProductKey").Single().Value);
    }
}

public class CatalogTests
{
    [Theory]
    [InlineData("WindowsServer2025Standard(DesktopExperience)", "Windows Server 2025", "Standard")]
    [InlineData("WindowsServer2022Datacenter", "Windows Server 2022", "Datacenter")]
    [InlineData("Windows11Pro", "Windows 11", "Pro")]
    public void Kms_key_is_suggested_from_the_os_identifier(string osId, string group, string edition)
    {
        var entry = KmsKeyCatalog.SuggestForOs(osId);
        Assert.NotNull(entry);
        Assert.Equal(group, entry.Group);
        Assert.Equal(edition, entry.OsName);
    }

    [Fact]
    public void Home_editions_are_flagged_as_generic_keys()
        => Assert.All(KmsKeyCatalog.All.Where(k => k.OsName.StartsWith("Home")), k => Assert.True(k.IsGeneric));

    private static readonly List<CustomProductKey> InsiderKeys =
    [
        new() { Name = "Server Insider", Pattern = "Insider",         Key = "AAAAA-BBBBB-CCCCC-DDDDD-EEEEE" },
        new() { Name = "Insider Azure",  Pattern = "*Insider*Azure*", Key = "fffff-ggggg-hhhhh-jjjjj-kkkkk" },
        new() { Name = "Empty",          Pattern = "Datacenter",      Key = "" },
    ];

    [Fact]
    public void A_key_of_the_settings_is_suggested_for_an_insider_base_the_catalog_does_not_know()
    {
        Assert.Null(KmsKeyCatalog.SuggestForOs("WindowsServerInsiderDatacenter", []));

        var key = KmsKeyCatalog.SuggestForOs("WindowsServerInsiderDatacenter", InsiderKeys)!;
        Assert.Equal("AAAAA-BBBBB-CCCCC-DDDDD-EEEEE", key.Key);
        Assert.True(key.IsCustom);
        Assert.Equal("Server Insider", key.OsName);
    }

    [Fact]
    public void The_most_specific_pattern_wins_and_a_key_is_stored_in_capitals()
        => Assert.Equal("FFFFF-GGGGG-HHHHH-JJJJJ-KKKKK",
                        KmsKeyCatalog.SuggestForOs("WindowsServerInsiderDatacenterAzureEdition", InsiderKeys)!.Key);

    [Fact]
    public void A_pattern_with_a_star_covers_the_whole_identifier_and_a_word_matches_anywhere()
    {
        Assert.True(new CustomProductKey { Pattern = "WindowsServerInsider*", Key = "x" }.Matches("WindowsServerInsiderDatacenter"));
        Assert.False(new CustomProductKey { Pattern = "Insider*", Key = "x" }.Matches("WindowsServerInsiderDatacenter"));
        Assert.True(new CustomProductKey { Pattern = "insider", Key = "x" }.Matches("WindowsServerInsiderDatacenter"));
    }

    [Fact]
    public void Without_a_matching_key_of_the_settings_the_catalog_still_answers()
    {
        var key = KmsKeyCatalog.SuggestForOs("WindowsServer2025Datacenter", InsiderKeys)!;
        Assert.False(key.IsCustom);
        Assert.Equal("D764K-2NDRG-47T6Q-P8T8W-YP6DF", key.Key);   // "Datacenter" has no key: ignored
    }

    [Fact]
    public void The_list_of_the_vm_form_starts_with_the_keys_of_the_settings()
    {
        var list = KmsKeyCatalog.WithCustom(InsiderKeys);
        Assert.Equal(KmsKeyCatalog.All.Count + 2, list.Count);
        Assert.Equal(["Server Insider", "Insider Azure"], list.Take(2).Select(k => k.OsName));
    }

    [Fact]
    public void Base_image_file_name_round_trips()
    {
        var name = BaseVhdx.BuildFileName("WindowsServer2025Standard", "10.0.26100.4652", 60);
        var parsed = BaseVhdx.FromFile(Path.Combine(Path.GetTempPath(), name + ".vhdx"));
        Assert.Equal("WindowsServer2025Standard", parsed.OsIdentifier);
        Assert.Equal("10.0.26100.4652", parsed.OsVersion);
        Assert.Equal(60, parsed.SizeGB);
    }
}

public class VmSizeProfileTests
{
    private static double Luminance(string hex)
    {
        double Channel(int i)
        {
            var c = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    [Fact]
    public void White_text_on_every_size_badge_meets_wcag_aa()
        => Assert.All(VmSizeProfile.All, p =>
            Assert.True(1.05 / (Luminance(p.AccentColor) + 0.05) >= 4.5,
                $"{p.Label} {p.AccentColor}: contrast with white below 4.5:1"));
}
