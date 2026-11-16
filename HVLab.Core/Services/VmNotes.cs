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
    /// <param name="protect">
    /// DPAPI-protect the password (readable back by this Windows account on this host only); null = the Settings choice.
    /// </param>
    /// <param name="tags">Tags of the VM (a lab blueprint gives them), written as their own line.</param>
    public static string Build(DateTime created, string? account = null, string? password = null, string? ipAddress = null,
                               bool? protect = null, IEnumerable<string>? tags = null)
    {
        var lines = new List<string> { T("Notes_Created", created) };
        if (string.IsNullOrEmpty(account)) lines.Add(T("Notes_NoAnswerFile"));
        else
        {
            lines.Add(T("Notes_Account", account));
            lines.Add((protect ?? AppSettings.Current.ProtectNotesPassword) && !string.IsNullOrEmpty(password)
                ? T("Notes_PasswordProtected", Dpapi.Protect(password))
                : T("Notes_Password", password ?? ""));
        }
        if (!string.IsNullOrEmpty(ipAddress)) lines.Add(T("Notes_Ip", ipAddress));
        var notes = string.Join(Environment.NewLine, lines);
        return tags is null ? notes : WithTags(notes, tags);
    }

    // ─── Tags ───────────────────────────────────────────────────────────────
    // A line of the notes ("Tags: web, prod"): Hyper-V has no tags of its own, and the notes travel with the VM
    // (export, Hyper-V Manager).

    private static readonly string[] TagsLabels = ["Étiquettes :", "Tags:"];

    /// <summary>The tags of the notes, in either UI language; none when there is no tags line.</summary>
    public static IReadOnlyList<string> ReadTags(string? notes)
    {
        foreach (var raw in (notes ?? "").Split('\n'))
            if (After(raw.TrimEnd('\r'), TagsLabels) is { Length: > 0 } value) return ParseTags(value);
        return [];
    }

    /// <summary>"web, prod; db" → web, prod, db: trimmed, without empty or duplicate tags (any case), 40 characters each.</summary>
    public static IReadOnlyList<string> ParseTags(string? text)
        => (text ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(t => new string(t.Where(c => !char.IsControl(c)).ToArray()))
                       .Select(t => t.Length > 40 ? t[..40].TrimEnd() : t)
                       .Where(t => t.Length > 0)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList();

    /// <summary>The notes with their tags line replaced by <paramref name="tags"/> (gone when there is none); every other line is kept.</summary>
    public static string WithTags(string? notes, IEnumerable<string> tags)
    {
        var lines = (notes ?? "").Split('\n').Select(l => l.TrimEnd('\r'))
                                 .Where(l => !TagsLabels.Any(label => l.StartsWith(label, StringComparison.Ordinal)))
                                 .ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var clean = ParseTags(string.Join(",", tags));
        if (clean.Count > 0) lines.Add(T("Notes_Tags", string.Join(", ", clean)));
        return string.Join(Environment.NewLine, lines);
    }

    // Line labels in both UI languages: the notes keep the language of the VM's creation
    private static readonly string[] AccountLabels  = ["Compte :", "Account:"];
    private static readonly string[] PasswordLabels = ["Mot de passe :", "Password:"];
    private static readonly string[] ProtectedLabels = ["Mot de passe (DPAPI) :", "Password (DPAPI):"];
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
            if (password.Length == 0 && After(line, ProtectedLabels) is { Length: > 0 } sealedPassword)
                password = Dpapi.TryUnprotect(sealedPassword) ?? "";   // another account or host: no password
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
