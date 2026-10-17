using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

public class PowerShellEscapeTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("it's", "it''s")]
    [InlineData("‘a’", "‘‘a’’")]      // U+2018 / U+2019 also close a PowerShell single-quoted string
    [InlineData("‚b‛", "‚‚b‛‛")]      // U+201A / U+201B
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Esc_doubles_every_single_quote_variant(string? input, string expected)
        => Assert.Equal(expected, PowerShellRunner.Esc(input));
}

public class InputValidatorTests
{
    [Theory]
    [InlineData("SRV-01")]
    [InlineData("Win 11 Lab")]
    public void Valid_vm_names_pass(string name) => Assert.Null(InputValidator.CheckVmName(name));

    [Theory]
    [InlineData("", "Val_VmNameEmpty")]
    [InlineData("   ", "Val_VmNameEmpty")]
    [InlineData(@"..\evil", "Val_VmNameChars")]
    [InlineData("a/b", "Val_VmNameChars")]
    [InlineData("a:b", "Val_VmNameChars")]
    [InlineData(" lead", "Val_VmNameEdges")]
    [InlineData("dot.", "Val_VmNameEdges")]
    public void Invalid_vm_names_are_rejected(string name, string expectedKey)
        => Assert.Equal(expectedKey, InputValidator.CheckVmName(name));

    [Fact]
    public void Vm_name_longer_than_100_is_rejected()
        => Assert.Equal("Val_VmNameTooLong", InputValidator.CheckVmName(new string('a', 101)));

    [Theory]
    [InlineData("LAB-VM", true)]
    [InlineData("SRV01", true)]
    [InlineData("12345", false)]          // digits only
    [InlineData("-LAB", false)]
    [InlineData("TOO-LONG-NAME-123", false)] // 17 chars
    [InlineData("LAB_VM", false)]
    public void Computer_names_follow_netbios_rules(string name, bool valid)
        => Assert.Equal(valid, InputValidator.CheckComputerName(name) is null);

    [Theory]
    [InlineData("", true)]
    [InlineData("W269N-WFGWX-YVC9B-4J6C9-T83GX", true)]
    [InlineData("W269N-WFGWX-YVC9B-4J6C9", false)]
    [InlineData("W269N WFGWX YVC9B 4J6C9 T83GX", false)]
    public void Product_key_format(string key, bool valid)
        => Assert.Equal(valid, InputValidator.CheckProductKey(key) is null);

    [Theory]
    [InlineData("Win 11_Lab", "Win-11-Lab")]
    [InlineData("SERVER-WITH-A-LONG-NAME", "SERVER-WITH-A-L")]
    [InlineData("2025", "VM-2025")]
    public void ToComputerName_produces_a_valid_name(string vmName, string expected)
    {
        var name = InputValidator.ToComputerName(vmName);
        Assert.Equal(expected, name);
        Assert.Null(InputValidator.CheckComputerName(name));
    }
}

public class NatAndBulkTests
{
    [Theory]
    [InlineData("192.168.100.1", 24, "192.168.100.1")]
    [InlineData(" 10.0.0.1 ", 16, "10.0.0.1")]
    [InlineData("10.0.0.1'; Remove-Item C:\\ -Recurse; '", 24, null)]
    [InlineData("::1", 24, null)]
    [InlineData("10.0.0.1", 31, null)]
    [InlineData("10.0.0.1", 7, null)]
    public void NormalizeGateway_accepts_only_ipv4_and_prefix_8_to_30(string ip, int prefix, string? expected)
        => Assert.Equal(expected, NatService.NormalizeGateway(ip, prefix));

    [Fact]
    public void Bulk_names_are_numbered_with_two_digits()
        => Assert.Equal(["SRV-01", "SRV-02", "SRV-03"], VmCreationViewModelBase.BulkNames("SRV", 3));

    [Fact]
    public void Bulk_preview_is_truncated_after_ten_names()
    {
        var preview = VmCreationViewModelBase.FormatBulkPreview("LAB", 12);
        Assert.StartsWith("LAB-01  LAB-02", preview);
        Assert.Contains("LAB-10", preview);
        Assert.DoesNotContain("LAB-11", preview);
        Assert.EndsWith("(+2)", preview);
    }
}
