using System.Security.Cryptography;
using System.Text.Json;

namespace Eg2.ModKit;

public sealed record InstalledFile(string Path, long Size, string Sha256);

public sealed class InstallRecord
{
    public DateTime InstalledAt { get; set; }
    public List<string> Mods { get; set; } = new();
    /// <summary>Version and content fingerprint of each applied mod (empty in records written before versions were kept).</summary>
    public List<InstalledMod> Installed { get; set; } = new();
    public List<InstalledFile> Files { get; set; } = new();
}

public sealed record InstalledMod(string Id, string Name, string Version, string Fingerprint);

/// <summary>
/// Writes build output into the game folder and records what it wrote in
/// eg2modkit.installed.json; uninstall removes exactly those files (if unchanged).
/// Only .asrpatch files and dev-slot files are ever written, so base files are never touched.
/// </summary>
public static class Installer
{
    public const string RecordName = "eg2modkit.installed.json";

    /// <summary>Hash of a mod's file and its assets folder, so an edit without a version bump still shows as "changed".</summary>
    public static string Fingerprint(ModDefinition m)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(m.FilePath is { } f && File.Exists(f) ? File.ReadAllBytes(f) : JsonSerializer.SerializeToUtf8Bytes(m, ModDefinition.Json));
        var dir = ModPackage.AssetDir(m);
        if (m.FilePath is not null && Directory.Exists(dir))
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                sha.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(dir, file).ToLowerInvariant()));
                sha.AppendData(File.ReadAllBytes(file));
            }
        return Convert.ToHexString(sha.GetHashAndReset())[..16];
    }

    /// <summary>One line on where a mod stands against what's in the game (both GUIs show it).</summary>
    public static string Status(InstallRecord? rec, ModDefinition m, bool ticked)
    {
        var e = rec?.Installed.FirstOrDefault(x => x.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
        bool inGame = e is not null || rec?.Mods.Contains(m.Id, StringComparer.OrdinalIgnoreCase) == true;
        if (!inGame) return ticked ? "ticked, not applied yet" : "";
        if (!ticked) return "in the game but unticked: Apply to take it out";
        if (e is null) return "in the game (applied before versions were kept): Apply once to track it";
        if (e.Version != m.Version) return $"v{e.Version} in the game, you have v{m.Version}: Apply to update";
        if (e.Fingerprint != Fingerprint(m)) return "changed since it was applied: Apply to update";
        return "up to date";
    }

    public static InstallRecord? Current(string root)
    {
        var f = Path.Combine(root, RecordName);
        return File.Exists(f) ? JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(f), ModDefinition.Json) : null;
    }

    /// <summary>Target files that exist but were not written by us (e.g. leftover probes or other tools).</summary>
    public static List<string> Foreign(string root, IEnumerable<string> relPaths)
    {
        var ours = Current(root)?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
        return relPaths.Where(p => File.Exists(Path.Combine(root, p))
                                   && !(ours.TryGetValue(p, out var f) && Sha(Path.Combine(root, p)) == f.Sha256))
                       .ToList();
    }

    public static void Install(string root, IReadOnlyDictionary<string, byte[]> files, IReadOnlyList<ModDefinition> mods,
                               bool overwriteForeign, Action<string> log)
    {
        foreach (var rel in files.Keys)
            if (!IsSafeTarget(rel)) throw new InvalidOperationException($"refusing to write a base-game path: {rel}");
        var foreign = Foreign(root, files.Keys);
        if (foreign.FirstOrDefault(IsNewLairFile) is { } game)
            throw new InvalidOperationException($"refusing to replace the game's own lair file: {game} (pick another name for the new lair)");
        if (foreign.Count > 0 && !overwriteForeign)
            throw new InvalidOperationException("files not written by ModKit are in the way:\n" + string.Join("\n", foreign));

        Uninstall(root, log);
        var record = new InstallRecord
        {
            InstalledAt = DateTime.Now, Mods = mods.Select(m => m.Id).ToList(),
            Installed = mods.Select(m => new InstalledMod(m.Id, m.Name, m.Version, Fingerprint(m))).ToList(),
        };
        int fn = 0;
        foreach (var (rel, data) in files)
        {
            var full = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, data);
            record.Files.Add(new InstalledFile(rel, data.Length, Convert.ToHexString(SHA256.HashData(data))));
            log($"wrote {++fn}/{files.Count}: {rel} ({data.Length:N0} bytes)");
            // save after every file so a crash mid-install can still be uninstalled
            File.WriteAllText(Path.Combine(root, RecordName), JsonSerializer.Serialize(record, ModDefinition.Json));
        }
        log($"installed {record.Files.Count} files. Saves made with mods active may depend on them.");
    }

    public static void Uninstall(string root, Action<string> log)
    {
        var record = Current(root);
        if (record is null) return;
        foreach (var f in record.Files)
        {
            var full = Path.Combine(root, f.Path);
            if (!File.Exists(full)) continue;
            if (Sha(full) != f.Sha256) { log($"kept {f.Path}: changed since install"); continue; }
            File.Delete(full);
            log($"removed {f.Path}");
        }
        var runtimeLog = Path.Combine(root, ModBuilder.RuntimeLog);
        if (record.Files.Any(f => f.Path.Equals(ModBuilder.RuntimeDllTarget, StringComparison.OrdinalIgnoreCase)) && File.Exists(runtimeLog))
        {
            File.Delete(runtimeLog);
            log($"removed {ModBuilder.RuntimeLog}");
        }
        var dev = Path.Combine(root, GameInstall.DevSlotDir);
        if (Directory.Exists(dev) && !Directory.EnumerateFileSystemEntries(dev).Any())
        {
            Directory.Delete(dev);
            log($@"removed {GameInstall.DevSlotDir} (empty)");
        }
        File.Delete(Path.Combine(root, RecordName));
        log("uninstalled.");
    }

    static bool IsSafeTarget(string rel) =>
        !Path.IsPathRooted(rel) && !rel.Contains("..")
        && (rel.Equals(ModBuilder.RuntimeDllTarget, StringComparison.OrdinalIgnoreCase)
            || rel.Equals(ModBuilder.RuntimeConfigTarget, StringComparison.OrdinalIgnoreCase)
            || rel.Equals(ModBuilder.TextureBlobTarget, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(ModBuilder.RuntimeBlocksDir + @"\", StringComparison.OrdinalIgnoreCase)
            || rel.EndsWith(GameInstall.PatchSuffix, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(GameInstall.DevSlotDir + @"\", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith($@"text\pc\{ModBuilder.OwnTextTable}\", StringComparison.OrdinalIgnoreCase)
            || IsNewLairFile(rel));

    /// <summary>A new lair's own files (ModDefinition.NewLairs): envs\lair_*.* and envs\basedefinitions\lair_*.base. Allowed
    /// only where the game has no such file (checked in <see cref="Install"/>, overwrite or not).</summary>
    static bool IsNewLairFile(string rel) =>
        Path.GetFileName(rel).StartsWith("lair_", StringComparison.OrdinalIgnoreCase)
        && (Path.GetDirectoryName(rel)!.Equals("envs", StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(rel)!.Equals(@"envs\basedefinitions", StringComparison.OrdinalIgnoreCase) && rel.EndsWith(".base", StringComparison.OrdinalIgnoreCase));

    /// <summary>A runtime DLL's version for display: its build time (PE header), e.g. "2026-09-26 12:34".</summary>
    public static string RuntimeVersion(string path)
    {
        try
        {
            var b = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(b, 0x3C);
            return DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToUInt32(b, pe + 8)).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { return "?"; }
    }

    /// <summary>SHA-256 as hex, or "" when the file can't be read (e.g. an antivirus has blocked it).</summary>
    public static string Sha(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(s));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>Why a file ModKit ships can't be read, or null when it can. Windows Defender sometimes flags the runtime
    /// xinput1_4.dll (a proxy DLL that hooks the game, like many game mods) and blocks or quarantines it.</summary>
    public static string? Unreadable(string path)
    {
        if (!File.Exists(path)) return $"{path} is missing (an antivirus may have quarantined it)";
        try { using var s = File.OpenRead(path); return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return $"{path} can't be read: {e.Message} Windows Defender flags ModKit's runtime DLL by mistake at times; restore it from Protection history and allow it, or exclude the ModKit and game bin folders."; }
    }
}
