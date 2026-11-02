using static HVLab.Services.LocalizationService;

namespace HVLab.Services;

/// <summary>
/// Hyper-V notes written on every VM HV-LAB creates: creation date and, for an unattended VM, the
/// account and its password in clear text. HV-LAB builds labs, where finding the credentials in
/// Hyper-V Manager matters more than hiding them.
/// </summary>
public static class VmNotes
{
    /// <param name="account">Account set by the answer file, or null when the VM has none.</param>
    public static string Build(DateTime created, string? account = null, string? password = null)
    {
        string[] lines = string.IsNullOrEmpty(account)
            ? [T("Notes_Created", created), T("Notes_NoAnswerFile")]
            : [T("Notes_Created", created), T("Notes_Account", account), T("Notes_Password", password ?? "")];
        return string.Join(Environment.NewLine, lines);
    }
}
