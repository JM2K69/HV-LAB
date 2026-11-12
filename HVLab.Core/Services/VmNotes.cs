using static HVLab.Services.LocalizationService;

namespace HVLab.Services;

/// <summary>
/// Hyper-V notes written on every VM HV-LAB creates: creation date and, for an unattended VM, the
/// account and its password in clear text (plus its static IP, if any). HV-LAB builds labs, where finding
/// the credentials in Hyper-V Manager matters more than hiding them.
/// </summary>
public static class VmNotes
{
    /// <param name="account">Account set by the answer file or cloud-init, or null when the VM has none.</param>
    public static string Build(DateTime created, string? account = null, string? password = null, string? ipAddress = null)
    {
        var lines = new List<string> { T("Notes_Created", created) };
        if (string.IsNullOrEmpty(account)) lines.Add(T("Notes_NoAnswerFile"));
        else { lines.Add(T("Notes_Account", account)); lines.Add(T("Notes_Password", password ?? "")); }
        if (!string.IsNullOrEmpty(ipAddress)) lines.Add(T("Notes_Ip", ipAddress));
        return string.Join(Environment.NewLine, lines);
    }
}
