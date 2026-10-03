using System.Runtime.InteropServices;
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

    public const string SteamAppId = "700600";

    /// <summary>Running under Wine/Proton (Linux, Steam Deck): Wine's ntdll exports wine_get_version.</summary>
    public static bool IsWine { get; } = NativeLibrary.TryLoad("ntdll.dll", out var h) && NativeLibrary.TryGetExport(h, "wine_get_version", out _);

    /// <summary>Steam launch option Proton needs to load the runtime proxy (Wine otherwise uses its own xinput1_4).</summary>
    public const string WineLaunchOption = "WINEDLLOVERRIDES=\"xinput1_4=n,b\" %command%";

    /// <summary>Under Wine, when the build installs the runtime proxy and it has never run (no log yet): how to make Proton load it.</summary>
    public static string? WineNote(string root, IEnumerable<string> files) =>
        IsWine && files.Contains(ModBuilder.RuntimeDllTarget, StringComparer.OrdinalIgnoreCase) && !File.Exists(Path.Combine(root, ModBuilder.RuntimeLog))
            ? "On Linux / Steam Deck, Proton only loads ModKit's runtime tweaks (minion cap, salaries, intro skip, ...) if the game's Steam launch options say:\n\n"
              + WineLaunchOption + "\n\nSteam → Evil Genius 2 → Properties → Launch options. It has been copied to the clipboard. (This message stops once the game has loaded the tweaks.)"
            : null;

    /// <summary>Starts the game through Steam (app 700600), like the Steam shortcut; running launcher\eg2.exe directly loops it twice.
    /// Under Wine, winebrowser hands the steam:// link to the Linux desktop (and so to Linux Steam).</summary>
    public static string? Launch(string root)
    {
        const string url = "steam://rungameid/" + SteamAppId;
        try
        {
            System.Diagnostics.Process.Start(IsWine ? new System.Diagnostics.ProcessStartInfo("winebrowser", url) : new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }
        catch (System.ComponentModel.Win32Exception e) { return $"couldn't start Steam: {e.Message}"; }
    }

    /// <summary>The game's saves. Under Proton the game has its own Wine prefix next to the install
    /// (steamapps\compatdata\700600\pfx), usually not the one ModKit runs in.</summary>
    public string SavesDir
    {
        get
        {
            const string saves = @"Evil Genius 2\PC_ProfileSaves";
            if (IsWine && Path.GetDirectoryName(Path.GetDirectoryName(Root)) is { } steamapps
                && Path.Combine(steamapps, "compatdata", SteamAppId, @"pfx\drive_c\users\steamuser\AppData\Local", saves) is var proton && Directory.Exists(proton))
                return proton;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), saves);
        }
    }

    public static string? Validate(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return "folder does not exist";
        if (!File.Exists(Path.Combine(root, "bin", "evilgenius_dx12.exe"))) return @"bin\evilgenius_dx12.exe not found";
        if (!Directory.Exists(Path.Combine(root, "misc", "packages"))) return @"misc\packages not found";
        return null;
    }

    /// <summary>Default Steam library, then every library listed in Steam's libraryfolders.vdf. Under Wine also Linux Steam
    /// (native, ~/.local/share, Flatpak), whose Unix paths Wine shows on drive Z:.</summary>
    public static string? FindDefault()
    {
        const string sub = @"steamapps\common\Evil Genius 2";
        var steams = new List<string> { @"C:\Program Files (x86)\Steam" };
        if (IsWine && Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home && home.StartsWith('/'))
            steams.AddRange(new[] { "/.steam/steam", "/.local/share/Steam", "/.var/app/com.valvesoftware.Steam/.local/share/Steam" }.Select(s => Z(home + s)));
        var libs = new List<string>(steams);
        foreach (var vdf in steams.Select(s => Path.Combine(s, "steamapps", "libraryfolders.vdf")).Where(File.Exists))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value is var p && p.StartsWith('/') ? Z(p) : p.Replace(@"\\", @"\"));
        return libs.Select(l => Path.Combine(l, sub)).FirstOrDefault(p => Validate(p) is null);
    }

    /// <summary>A Unix path as Wine sees it (drive Z: is the Unix root).</summary>
    static string Z(string unix) => "Z:" + unix.Replace('/', '\\');

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
