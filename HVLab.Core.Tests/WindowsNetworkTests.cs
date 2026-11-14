using System.Xml.Linq;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

public class WindowsNetworkTests
{
    private static readonly XNamespace U = "urn:schemas-microsoft-com:unattend";

    private static AnswerFileConfig Config() => new()
    {
        ComputerName = "SRV-01", AdminPassword = "Lab#2026",
        StaticIp = new StaticIpConfig("192.168.100.10", 24, "192.168.100.1", ["192.168.100.2", "1.1.1.1"]),
        MacAddress = "00-15-5D-0A-0B-0C",
    };

    private static XElement Component(XDocument doc, string name)
        => doc.Descendants(U + "component").Single(c => (string?)c.Attribute("name") == name);

    [Fact]
    public void A_static_ip_goes_to_the_adapter_with_the_static_mac()
    {
        var doc = XDocument.Parse(AnswerFileGenerator.Generate(Config()));   // well-formed XML
        var tcpip = Component(doc, "Microsoft-Windows-TCPIP");
        Assert.Equal("00-15-5D-0A-0B-0C", tcpip.Descendants(U + "Identifier").First().Value);
        Assert.Equal("false", tcpip.Descendants(U + "DhcpEnabled").Single().Value);
        Assert.Equal("192.168.100.10/24", tcpip.Descendants(U + "IpAddress").Single().Value);
        Assert.Equal("192.168.100.1", tcpip.Descendants(U + "NextHopAddress").Single().Value);
        var dns = Component(doc, "Microsoft-Windows-DNS-Client");
        Assert.Equal(["192.168.100.2", "1.1.1.1"], dns.Descendants(U + "IpAddress").Select(e => e.Value));
        Assert.Equal("specialize", (string?)tcpip.Parent!.Attribute("pass"));
    }

    [Fact]
    public void Without_a_static_ip_there_is_no_network_block()
    {
        var config = Config();
        config.StaticIp = null;
        var doc = XDocument.Parse(AnswerFileGenerator.Generate(config));
        Assert.DoesNotContain(doc.Descendants(U + "component"), c => (string?)c.Attribute("name") == "Microsoft-Windows-TCPIP");
    }

    [Theory]
    [InlineData("Administrator", "lab.local", "Administrator")]
    [InlineData(@"LAB\joiner", "LAB", "joiner")]
    [InlineData("joiner@corp.lab", "corp.lab", "joiner")]
    public void The_domain_join_takes_the_account_domain(string user, string expectedDomain, string expectedUser)
    {
        var config = Config();
        config.JoinDomain = "lab.local"; config.DomainUser = user; config.DomainPassword = "J<o>in&\"";
        config.DomainOu = "OU=Servers,DC=lab,DC=local";
        var doc = XDocument.Parse(AnswerFileGenerator.Generate(config));
        var join = Component(doc, "Microsoft-Windows-UnattendedJoin");
        Assert.Equal("lab.local", join.Descendants(U + "JoinDomain").Single().Value);
        Assert.Equal(expectedDomain, join.Descendants(U + "Domain").Single().Value);
        Assert.Equal(expectedUser, join.Descendants(U + "Username").Single().Value);
        Assert.Equal("J<o>in&\"", join.Descendants(U + "Password").Single().Value);   // escaped, read back intact
        Assert.Equal("OU=Servers,DC=lab,DC=local", join.Descendants(U + "MachineObjectOU").Single().Value);
    }

    [Theory]
    [InlineData("lab.local", true)]
    [InlineData("LAB", true)]
    [InlineData("corp-01.lab.example", true)]
    [InlineData("", false)]
    [InlineData("lab..local", false)]
    [InlineData("-lab.local", false)]
    [InlineData("lab local", false)]
    public void Domain_names_are_checked(string name, bool valid)
        => Assert.Equal(valid, InputValidator.CheckDomainName(name) is null);

    [Fact]
    public void Static_macs_are_random_in_the_hyperv_range()
    {
        var a = InputValidator.NewHyperVMac();
        Assert.Matches("^00-15-5D-[0-9A-F]{2}-[0-9A-F]{2}-[0-9A-F]{2}$", a);
        Assert.NotEqual(a, InputValidator.NewHyperVMac());
    }

    [Fact]
    public async Task A_windows_series_with_static_ips_gets_one_mac_and_ip_per_vm()
    {
        var dir = Directory.CreateTempSubdirectory("hvlab-winip-").FullName;
        var basePath = Path.Combine(dir, "BASE_WindowsServer2022Standard_10.0.20348.587_50.vhdx");
        File.WriteAllBytes(basePath, []);
        var hv = new FakeHyperVService();
        var vm = new CreateVmViewModel(hv, new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            SelectedBaseVhdx = basePath, SelectedSwitch = "LAN", AdminPassword = "Lab#2026",
            BulkMode = true, BulkPrefix = "SRV", BulkCount = 2,
            UseStaticIp = true, IpAddress = "192.168.100.10", PrefixLength = 24, Gateway = "192.168.100.1", DnsServers = "192.168.100.2",
        };
        await vm.CreateVmAsync();

        Assert.Contains("192.168.100.10/24", hv.CreatedAnswers["SRV-01"]);
        Assert.Contains("192.168.100.11/24", hv.CreatedAnswers["SRV-02"]);
        Assert.NotEqual(hv.CreatedMacs["SRV-01"], hv.CreatedMacs["SRV-02"]);
        Assert.Contains(hv.CreatedMacs["SRV-01"]!, hv.CreatedAnswers["SRV-01"]);          // the MAC of its own adapter
        Assert.Equal("192.168.100.11", VmNotes.Read(hv.CreatedNotes["SRV-02"]).IpAddress);   // IP in the notes
    }

    [Fact]
    public async Task A_static_mac_is_set_before_first_boot()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).CreateVMWithDifferencingDiskAsync("SRV-01", "D:/Bases/BASE_x.vhdx", "LAN", 2048, 2, 2, "D:/VMs",
            "<unattend/>", staticMac: "00-15-5D-0A-0B-0C");
        var script = runner.Scripts.Single();
        Assert.Contains("$staticMac    = '00155D0A0B0C'", script);
        Assert.Contains("Set-VMNetworkAdapter -StaticMacAddress $staticMac", script);
    }
}
