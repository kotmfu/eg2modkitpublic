using System.Text.RegularExpressions;

namespace Eg2.ModKit;

/// <summary>
/// A validated game folder plus path helpers. Everything here points at BASE
/// files; an installed .asrpatch is never read as input.
/// </summary>
public sealed class GameInstall
{
    public const string DevSlotDir = @"misc\packages\development";
    public const string PatchSuffix = ".asrpatch";

    public GameInstall(string root)
    {
        var err = Validate(root);
        if (err is not null) throw new ArgumentException(err);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    /// <summary>Starts the game through Steam (app 700600), like the Steam shortcut; running launcher\eg2.exe directly loops it twice.</summary>
    public static string? Launch(string root)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://rungameid/700600") { UseShellExecute = true }); return null; }
        catch (System.ComponentModel.Win32Exception e) { return $"couldn't start Steam: {e.Message}"; }
    }

    public static string? Validate(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return "folder does not exist";
        if (!File.Exists(Path.Combine(root, "bin", "evilgenius_dx12.exe"))) return @"bin\evilgenius_dx12.exe not found";
        if (!Directory.Exists(Path.Combine(root, "misc", "packages"))) return @"misc\packages not found";
        return null;
    }

    /// <summary>Default Steam library, then every library listed in Steam's libraryfolders.vdf.</summary>
    public static string? FindDefault()
    {
        const string sub = @"steamapps\common\Evil Genius 2";
        var libs = new List<string> { @"C:\Program Files (x86)\Steam" };
        var vdf = @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf";
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        return libs.Select(l => Path.Combine(l, sub)).FirstOrDefault(p => Validate(p) is null);
    }

    public sealed record PackageFiles(string Group, string Name, string ManifestPath, string ContentPath);

    /// <summary>Every base content package under misc\packages\{required,managed}.</summary>
    public IEnumerable<PackageFiles> Packages()
    {
        const string suffix = "_content.asr";
        foreach (var group in new[] { "required", "managed" })
        {
            var dir = Path.Combine(Root, "misc", "packages", group);
            if (!Directory.Exists(dir)) continue;
            // "*.asr"-style patterns also match longer extensions on Windows; filter exactly.
            foreach (var f in Directory.EnumerateFiles(dir)
                         .Where(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(f)[..^suffix.Length];
                yield return new PackageFiles(group, name, Path.Combine(dir, name + ".asr"), f);
            }
        }
    }

    public PackageFiles? Package(string name) =>
        Packages().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>(table, path) for every text\pc\&lt;t&gt;\&lt;t&gt;.asr_&lt;lang&gt;.</summary>
    public IEnumerable<(string Table, string Path)> TextFiles(string lang)
    {
        var dir = Path.Combine(Root, "text", "pc");
        if (!Directory.Exists(dir)) yield break;
        foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var t = Path.GetFileName(d);
            if (t.Equals(ModBuilder.OwnTextTable, StringComparison.OrdinalIgnoreCase)) continue;   // ours, not the game's
            var f = TextFile(t, lang);
            if (File.Exists(f)) yield return (t, f);
        }
    }

    public string TextFile(string table, string lang) => Path.Combine(Root, "text", "pc", table, $"{table}.asr_{lang}");

    public string Rel(string fullPath) => Path.GetRelativePath(Root, fullPath);

    public string Full(string relPath) => Path.Combine(Root, relPath);
}
