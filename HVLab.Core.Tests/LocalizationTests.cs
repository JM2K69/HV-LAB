using System.Reflection;
using System.Text.RegularExpressions;
using HVLab.Services;

namespace HVLab.Core.Tests;

public partial class LocalizationTests
{
    private static Dictionary<string, string> Strings(string field)
        => (Dictionary<string, string>)typeof(LocalizationService)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void French_and_english_have_the_same_keys()
    {
        var fr = Strings("Strings_fr").Keys.ToHashSet();
        var en = Strings("Strings_en").Keys.ToHashSet();
        Assert.Empty(fr.Except(en));
        Assert.Empty(en.Except(fr));
    }

    [Fact]
    public void Format_placeholders_match_between_languages()
    {
        var fr = Strings("Strings_fr");
        var en = Strings("Strings_en");
        var mismatches = fr.Keys.Where(k => en.ContainsKey(k) && Placeholders(fr[k]) != Placeholders(en[k])).ToList();
        Assert.Empty(mismatches);
    }

    private static string Placeholders(string s)
        => string.Join(",", PlaceholderRegex().Matches(s).Select(m => m.Groups[1].Value).Distinct().Order());

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex PlaceholderRegex();

    [Fact]
    public void Progress_tokens_are_translated()
    {
        Assert.Equal(LocalizationService.T("Prg_Partition", "3"), LocalizationService.TranslateProgress("##Prg_Partition|3"));
        Assert.Equal("plain line", LocalizationService.TranslateProgress("plain line"));
    }

    /// <summary>Every key used in C# or XAML (T("…"), ["…"], {Binding [..]}, ##tokens) must exist.</summary>
    [Fact]
    public void Every_key_used_in_the_sources_exists()
    {
        var root = RepoRoot();
        var known = Strings("Strings_fr").Keys.ToHashSet();
        var files = Directory.EnumerateFiles(Path.Combine(root, "HVLab"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "HVLab.Core"), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.EndsWith("LocalizationService.cs"));

        var missing = new SortedSet<string>();
        foreach (var file in files)
            foreach (Match m in KeyUsageRegex().Matches(File.ReadAllText(file)))
            {
                var key = m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
                if (!known.Contains(key)) missing.Add($"{key} ({Path.GetFileName(file)})");
            }
        Assert.Empty(missing);
    }

    [GeneratedRegex(@"\bT\(""([A-Za-z]+_[A-Za-z0-9_]+)""|(?:Instance|Loc|loc)\[""([A-Za-z]+_[A-Za-z0-9_]+)""\]|\{Binding \[([A-Za-z]+_[A-Za-z0-9_]+)\]|A11y\.Action\('([A-Za-z]+_[A-Za-z0-9_]+)'|""##([A-Za-z]+_[A-Za-z0-9_]+)")]
    private static partial Regex KeyUsageRegex();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "HVLab.Core")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found");
    }
}
