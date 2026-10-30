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

    [Fact]
    public void AutoLogon_is_emitted_only_when_enabled()
        => Assert.Single(Generate(c => c.AutoLogon = true).Descendants(U + "AutoLogon"));

    [Fact]
    public void Special_characters_in_password_are_escaped()
    {
        var doc = Generate(c => c.AdminPassword = "a<b&\"c>");
        var value = doc.Descendants(U + "AdministratorPassword").Descendants(U + "Value").Single().Value;
        Assert.Equal("a<b&\"c>", value);
    }

    [Theory]
    [InlineData(OsFamily.Server)]
    [InlineData(OsFamily.Client)]
    public void Only_the_built_in_administrator_is_used(OsFamily family)
    {
        var doc = Generate(c => { c.OsFamily = family; c.AutoLogon = true; });
        Assert.Empty(doc.Descendants(U + "LocalAccounts"));
        Assert.Equal("Administrator", doc.Descendants(U + "AutoLogon").Single().Element(U + "Username")!.Value);
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
