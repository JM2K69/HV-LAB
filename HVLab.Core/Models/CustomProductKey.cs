using System.IO.Enumeration;
using System.Text.Json.Serialization;

namespace HVLab.Models;

/// <summary>
/// A product key the user adds for bases the GVLK catalog does not know (Windows Insider builds, MSDN keys…).
/// <see cref="Pattern"/> is matched against the OS identifier of a base: a word ("Insider") matches anywhere,
/// a pattern with <c>*</c> or <c>?</c> ("WindowsServerInsider*") matches the whole identifier.
/// </summary>
public sealed class CustomProductKey
{
    public string Name    { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string Key     { get; set; } = "";

    /// <summary>Name shown in the lists: the name, or the pattern when no name was given.</summary>
    [JsonIgnore] public string DisplayName => Name.Trim().Length > 0 ? Name.Trim() : Pattern.Trim();

    private bool HasWildcard => Pattern.IndexOfAny(['*', '?']) >= 0;

    /// <summary>Number of literal characters: the most specific of several matching patterns wins.</summary>
    [JsonIgnore] public int Specificity => Pattern.Trim().Count(c => c is not '*' and not '?');

    public bool Matches(string? osIdentifier)
    {
        var pattern = Pattern.Trim();
        if (pattern.Length == 0 || string.IsNullOrWhiteSpace(osIdentifier)) return false;
        return HasWildcard
            ? FileSystemName.MatchesSimpleExpression(pattern, osIdentifier, ignoreCase: true)
            : osIdentifier.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public KmsKeyEntry ToEntry()
        => new(HVLab.Services.LocalizationService.Instance["KMS_CustomGroup"], DisplayName, Key.Trim().ToUpperInvariant(), IsCustom: true);
}
