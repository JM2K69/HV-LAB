using HVLab.Models;
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

    // Line labels in both UI languages: the notes keep the language of the VM's creation
    private static readonly string[] AccountLabels  = ["Compte :", "Account:"];
    private static readonly string[] PasswordLabels = ["Mot de passe :", "Password:"];
    private static readonly string[] IpLabels       = ["IP :", "IP:"];

    /// <summary>Reads back the credentials written by <see cref="Build"/>; other notes give empty values.</summary>
    public static VmCredentials Read(string? notes)
    {
        string account = "", password = "", ip = "";
        foreach (var raw in (notes ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            account  = account.Length  > 0 ? account  : After(line, AccountLabels);
            password = password.Length > 0 ? password : After(line, PasswordLabels);
            ip       = ip.Length       > 0 ? ip       : After(line, IpLabels);
        }
        return new VmCredentials(account, password, ip);
    }

    // The password is kept as is (it may start or end with spaces); the other values are trimmed
    private static string After(string line, string[] labels)
    {
        foreach (var label in labels)
            if (line.StartsWith(label, StringComparison.Ordinal))
            {
                var value = line[label.Length..];
                return value.StartsWith(' ') ? value[1..] : value;
            }
        return "";
    }
}
