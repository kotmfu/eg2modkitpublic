using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// One replaceable/extractable asset: a named chunk in a game file. Identity = (File, Tag, Name, Occurrence).
/// RSCF chunks are embedded files (sounds are MS-ADPCM WAV, textures are DDS); HCAN/FAAN/HSKN/HSKL are
/// the engine's own animation/model formats, extracted and replaced as raw chunk bodies.
/// </summary>
public sealed class AssetEntry
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string File { get; init; } = "";
    [DisplayName("Size (KB)")] public long SizeKb => (Size + 1023) / 1024;
    [Browsable(false)] public string Tag { get; init; } = "";
    [Browsable(false)] public int Occurrence { get; init; }
    /// <summary>File offset of the chunk header in an uncompressed container; -1 inside compressed packages.</summary>
    [Browsable(false)] public long Offset { get; init; } = -1;
    [Browsable(false)] public long Size { get; init; }
    /// <summary>File offset of the embedded data (DDS/WAV) in an uncompressed container; -1 inside packages. .ts tables point here.</summary>
    [Browsable(false)] public long DataOffset { get; init; } = -1;
    [Browsable(false)] public bool Streamed => File.EndsWith(".pc_textures", StringComparison.OrdinalIgnoreCase);
    [Browsable(false)] public string Key => AssetIndex.Key(File, Tag, Name, Occurrence);
    public override string ToString() => $"{Kind} {Name}";
}

/// <summary>A mod's replacement for one asset; Source is relative to the mod's "&lt;id&gt;.assets" folder.</summary>
public sealed class AssetReplacement
{
    public string File { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public int Occurrence { get; set; }
    public string Source { get; set; } = "";
    public string? Note { get; set; }
    /// <summary>A new lair's stem (streamed textures only): the texture changes on that lair's island copy alone (its .ts
    /// table), not on the island it was copied from.</summary>
    public string? Island { get; set; }

    public bool Targets(AssetEntry e) => Same(File, e.File) && Tag == e.Tag && Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase) && Occurrence == e.Occurrence;
    internal string Slot => AssetIndex.Key(File, Tag, Name, Occurrence);
    internal static bool Same(string a, string b) => a.Replace('/', '\\').Equals(b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
}

public static class AssetIndex
{
    static readonly HashSet<string> Tags = new() { "RSCF", "HCAN", "FAAN", "HSKN", "HSKL" };

    /// <summary>Identity string shared by <see cref="AssetEntry"/> and <see cref="AssetReplacement"/>.</summary>
    public static string Key(string file, string tag, string name, int occurrence) =>
        $"{file.Replace('/', (char)92)}|{tag}|{name}|{occurrence}".ToLowerInvariant();


    public static string KindOf(string tag, ReadOnlySpan<byte> dataStart, uint rscfType = 0) => tag switch
    {
        "RSCF" when rscfType == 8 => "Mesh",
        "RSCF" when dataStart.StartsWith("RIFF"u8) => "Sound",
        "RSCF" when dataStart.StartsWith("DDS "u8) => "Texture",
        "RSCF" => "File",
        "HCAN" => "Animation",
        "FAAN" => "Animation (FAAN)",
        "HSKN" => "Model",
        "HSKL" => "Skeleton",
        _ => tag,
    };

    /// <summary>RSCF: its path. Others: the first NUL-terminated name in the first 256 bytes, else "".</summary>
    public static string NameOf(string tag, ReadOnlySpan<byte> body)
    {
        if (tag == "RSCF")
        {
            int end = body.Length > 20 ? body[20..].IndexOf((byte)0) : -1;
            return end > 0 ? Encoding.Latin1.GetString(body.Slice(20, end)).TrimStart('\\') : "";
        }
        var head = body[..Math.Min(body.Length, 256)];
        for (int i = 0; i < head.Length; i++)
        {
            int j = i;
            while (j < head.Length && head[j] is >= (byte)'0' and <= (byte)'z' or (byte)' ' or (byte)'#' or (byte)'-' or (byte)'.') j++;
            if (j - i >= 4 && j < head.Length && head[j] == 0 && char.IsLetter((char)head[i])) return Encoding.Latin1.GetString(head[i..j]);
            i = j;
        }
        return "";
    }

    /// <summary>Asset entries for chunks already in memory (a loaded package).</summary>
    public static IEnumerable<AssetEntry> FromChunks(string file, IEnumerable<Chunk> chunks)
    {
        var seen = new Dictionary<string, int>();
        foreach (var c in chunks)
        {
            if (!Tags.Contains(c.Tag)) continue;
            string name = NameOf(c.Tag, c.Body);
            int occ = Next(seen, c.Tag, name);
            var data = c.Tag == "RSCF" && c.Body.Length >= 20 ? c.Body.AsSpan(c.Body.Length - (int)Math.Min(Bytes.U32(c.Body, 16), (uint)c.Body.Length)) : c.Body;
            yield return new AssetEntry
            {
                File = file, Tag = c.Tag, Name = name.Length > 0 ? name : $"{c.Tag} #{occ}", Occurrence = occ, Size = data.Length,
                Kind = KindOf(c.Tag, data[..Math.Min(4, data.Length)], c.Body.Length >= 4 ? Bytes.U32(c.Body, 0) : 0),
            };
        }
    }

    static int Next(Dictionary<string, int> seen, string tag, string name)
    {
        string k = tag + "|" + name.ToLowerInvariant();
        int n = seen.GetValueOrDefault(k);
        seen[k] = n + 1;
        return n;
    }

    /// <summary>Header-only scan of an uncompressed container ("Asura   "): seeks past chunk data, so multi-GB blobs are fine.</summary>
    public static List<AssetEntry> ScanRaw(string root, string rel)
    {
        if (StreamSounds.IsStore(rel)) return StreamSounds.Assets(root, rel).ToList();
        var list = new List<AssetEntry>();
        using var fs = System.IO.File.OpenRead(Path.Combine(root, rel));
        var head = new byte[8];
        if (fs.Read(head, 0, 8) != 8 || Encoding.ASCII.GetString(head) != "Asura   ") return list;
        var seen = new Dictionary<string, int>();
        var hb = new byte[8];
        while (fs.Position + 8 <= fs.Length)
        {
            long at = fs.Position;
            fs.ReadExactly(hb);
            string tag = Encoding.Latin1.GetString(hb, 0, 4);
            uint size = Bytes.U32(hb, 4);
            if (size < 8 || tag == "\0\0\0\0" || at + size > fs.Length) break;
            if (Tags.Contains(tag))
            {
                var prefix = new byte[Math.Min(size - 8, 300)];
                fs.ReadExactly(prefix);
                long dataLen = size - 8, dataAt = at + 8;
                if (tag == "RSCF" && prefix.Length >= 20) { dataLen = Math.Min(Bytes.U32(prefix, 16), size - 8); dataAt = at + size - dataLen; }
                var magic = new byte[Math.Min(4, dataLen)];
                fs.Position = dataAt;
                fs.ReadExactly(magic);
                string name = NameOf(tag, prefix);
                int occ = Next(seen, tag, name);
                list.Add(new AssetEntry
                {
                    File = rel, Tag = tag, Name = name.Length > 0 ? name : $"{tag} #{occ}", Occurrence = occ,
                    Offset = at, DataOffset = dataAt, Size = dataLen, Kind = KindOf(tag, magic, prefix.Length >= 4 ? Bytes.U32(prefix, 0) : 0),
                });
            }
            fs.Position = at + size;
        }
        return list;
    }

    /// <summary>Uncompressed asset stores: sound banks, streamed sound stores and texture blobs. Not ModKit's own
    /// texture blob: it only exists while mods are installed, so a replacement aimed at it breaks once they're removed.</summary>
    public static IEnumerable<string> RawStores(GameInstall install) =>
        Directory.EnumerateFiles(install.Root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".pc.sounds", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pc_textures", StringComparison.OrdinalIgnoreCase) || StreamSounds.IsStore(f))
            .Select(install.Rel)
            .Where(f => !f.Equals(ModBuilder.TextureBlobTarget, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    /// <summary>WAV format tag (1 = PCM, 2 = MS-ADPCM), or null if the data isn't a RIFF/WAVE file.</summary>
    public static int? WavFormat(byte[] d)
    {
        if (d.Length < 12 || !d.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !d.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return null;
        for (int p = 12; p + 10 <= d.Length; p += 8 + (int)Bytes.U32(d, p + 4) + ((int)Bytes.U32(d, p + 4) & 1))
            if (d.AsSpan(p, 4).SequenceEqual("fmt "u8)) return d[p + 8] | d[p + 9] << 8;
        return null;
    }

    /// <summary>What Extract/Open write: an OBJ for meshes, the payload itself otherwise.</summary>
    public static byte[] ExportBytes(AssetEntry e, byte[] payload) =>
        e.Kind == "Mesh" ? Encoding.UTF8.GetBytes(MeshGeometry.Parse(payload).ToObj(e.Name)) : payload;

    /// <summary>Whole chunk body of an asset in an uncompressed container.</summary>
    public static byte[] ReadBody(GameInstall install, AssetEntry e)
    {
        if (e.Offset < 0) throw new InvalidOperationException($"{e.Name} is inside a compressed package");
        using var fs = System.IO.File.OpenRead(install.Full(e.File));
        if (e.Tag == "ASTS") return ReadAt(fs, e);
        var hb = new byte[8];
        fs.Position = e.Offset;
        fs.ReadExactly(hb);
        var body = new byte[Bytes.U32(hb, 4) - 8];
        fs.ReadExactly(body);
        return body;
    }

    /// <summary>Bytes a player edits: the embedded file (WAV/DDS) for RSCF, the raw chunk body otherwise.</summary>
    public static byte[] Payload(string tag, byte[] body) => tag == "RSCF" ? EmbeddedFile.Parse(body).Data : body;

    /// <summary>New chunk body with the replacement applied (RSCF keeps its header, path and filler).</summary>
    public static byte[] Replace(string tag, byte[] body, byte[] data)
    {
        if (tag != "RSCF") return data;
        var e = EmbeddedFile.Parse(body);
        e.Data = data;
        return e.ToBytes();
    }

    /// <summary>Relative output path for an extracted asset ("sounds\gui\x.wav", "furniture_content\Name.hcan").</summary>
    public static string OutputPath(AssetEntry e)
    {
        static string Clean(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) && c != '\\' ? '_' : c)).Trim('\\');
        if (e.Tag is "RSCF" or "ASTS")
        {
            var p = Clean(e.Name.Replace('/', '\\'));
            return e.Kind == "Texture" && !p.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(p, ".dds")
                 : e.Kind == "Mesh" ? p.Replace('#', '~') + ".obj" : p;
        }
        var stem = Path.GetFileName(e.File);
        int dot = stem.IndexOf('.');
        if (dot > 0) stem = stem[..dot];
        return Path.Combine(stem, Clean(e.Name.Replace('\\', '_').Replace('/', '_')) + (e.Occurrence > 0 ? $"_{e.Occurrence}" : "") + "." + e.Tag.ToLowerInvariant());
    }

    /// <summary>
    /// Where a mod keeps its replacement for <paramref name="e"/> (relative to its assets folder). Streamed textures
    /// get their blob's name in front: the low-res package copy has the same name and would otherwise share the file.
    /// </summary>
    public static string ReplacementPath(AssetEntry e) =>
        e.Streamed ? Path.Combine(Path.GetFileNameWithoutExtension(e.File), OutputPath(e)) : OutputPath(e);

    static byte[] ReadAt(FileStream fs, AssetEntry e)
    {
        fs.Position = e.DataOffset;
        var d = new byte[e.Size];
        fs.ReadExactly(d);
        return d;
    }

    /// <summary>Read the assets' payloads; compressed containers are loaded once per file.</summary>
    public static IEnumerable<(AssetEntry Entry, byte[] Data)> Read(GameInstall install, IEnumerable<AssetEntry> entries, CancellationToken ct = default)
    {
        foreach (var group in entries.GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            string full = install.Full(group.Key);
            if (group.All(e => e.Offset >= 0))
            {
                using var fs = System.IO.File.OpenRead(full);
                foreach (var e in group)
                {
                    ct.ThrowIfCancellationRequested();
                    if (e.Tag == "ASTS") { yield return (e, ReadAt(fs, e)); continue; }
                    var hb = new byte[8];
                    fs.Position = e.Offset;
                    fs.ReadExactly(hb);
                    var body = new byte[Bytes.U32(hb, 4) - 8];
                    fs.ReadExactly(body);
                    yield return (e, Payload(e.Tag, body));
                }
            }
            else
            {
                var arc = AsuraArchive.Load(full);
                var want = group.ToList();
                foreach (var (chunk, entry) in Match(arc.Chunks, want))
                    yield return (entry, Payload(entry.Tag, chunk.Body));
            }
        }
    }

    /// <summary>Chunks in <paramref name="chunks"/> that the given targets name, by (Tag, Name, Occurrence).</summary>
    public static IEnumerable<(Chunk Chunk, T Target)> Match<T>(IEnumerable<Chunk> chunks, IList<T> targets) where T : class
    {
        (string Tag, string Name, int Occ) Key(T t) => t switch
        {
            AssetEntry e => (e.Tag, e.Name, e.Occurrence),
            AssetReplacement a => (a.Tag, a.Name, a.Occurrence),
            _ => throw new ArgumentException(nameof(T)),
        };
        var wanted = targets.ToLookup(t => { var k = Key(t); return $"{k.Tag}|{k.Name.ToLowerInvariant()}|{k.Occ}"; });
        var seen = new Dictionary<string, int>();
        foreach (var c in chunks)
        {
            if (!Tags.Contains(c.Tag)) continue;
            string name = NameOf(c.Tag, c.Body);
            int occ = Next(seen, c.Tag, name);
            if (name.Length == 0) name = $"{c.Tag} #{occ}";
            foreach (var t in wanted[$"{c.Tag}|{name.ToLowerInvariant()}|{occ}"]) yield return (c, t);
        }
    }
}

/// <summary>
/// Mod files for sharing: "&lt;id&gt;.json" plus an optional "&lt;id&gt;.assets" folder next to it.
/// A .eg2mod is a zip of exactly those. Used by ModKit's export and the player installer.
/// </summary>
public static class ModPackage
{
    public const string Extension = ".eg2mod";
    /// <summary>File-dialog pattern for everything the installers take: .eg2mod, the same zip named .zip, or a bare .json.</summary>
    public const string Patterns = "*.eg2mod;*.zip;*.json";

    /// <summary>A zipped mod: .eg2mod, or the same zip saved as .zip.</summary>
    public static bool IsPackage(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static string AssetDir(ModDefinition m) =>
        Path.Combine(Path.GetDirectoryName(m.FilePath ?? throw new InvalidOperationException("mod has no file path"))!, m.Id + ".assets");

    public static void Export(ModDefinition m, string zipPath)
    {
        if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);
        using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
        var json = zip.CreateEntry(m.Id + ".json");
        using (var w = new StreamWriter(json.Open())) w.Write(System.Text.Json.JsonSerializer.Serialize(m, ModDefinition.Json));
        var dir = AssetDir(m);
        foreach (var a in m.Assets)
            if (!System.IO.File.Exists(Path.Combine(dir, a.Source))) throw new FileNotFoundException($"asset file missing: {Path.Combine(dir, a.Source)}");
        // the whole folder: new textures, own-art textures, backgrounds and island meshes live there too, not only m.Assets
        if (Directory.Exists(dir))
            foreach (var src in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(src, $"{m.Id}.assets/{Path.GetRelativePath(dir, src).Replace('\\', '/')}", System.IO.Compression.CompressionLevel.Optimal);
    }

    /// <summary>Read the mod definition inside a .json or .eg2mod without installing it.</summary>
    public static ModDefinition Peek(string path)
    {
        if (!IsPackage(path)) return ModDefinition.Load(path);
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var jsons = zip.Entries.Where(e => !e.FullName.Contains('/') && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
        if (jsons.Count != 1) throw new InvalidDataException("a .eg2mod must contain exactly one <id>.json at its top level");
        using var r = new StreamReader(jsons[0].Open());
        return System.Text.Json.JsonSerializer.Deserialize<ModDefinition>(r.ReadToEnd(), ModDefinition.Json) ?? throw new InvalidDataException("empty mod file");
    }

    /// <summary>Copy a .json (plus its .assets folder if next to it) or unpack a .eg2mod into <paramref name="modsDir"/>.</summary>
    public static ModDefinition Install(string path, string modsDir)
    {
        var m = Peek(path);
        if (string.IsNullOrWhiteSpace(m.Id) || m.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || m.Id.Contains(".."))
            throw new InvalidDataException($"bad mod id \"{m.Id}\"");
        Directory.CreateDirectory(modsDir);
        var target = Path.Combine(modsDir, m.Id + ".json");
        var assets = Path.Combine(modsDir, m.Id + ".assets");
        if (Directory.Exists(assets)) Directory.Delete(assets, recursive: true);

        if (IsPackage(path))
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            string root = Path.GetFullPath(modsDir) + Path.DirectorySeparatorChar;
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                bool ok = e.FullName.Equals(m.Id + ".json", StringComparison.OrdinalIgnoreCase)
                          || e.FullName.StartsWith(m.Id + ".assets/", StringComparison.OrdinalIgnoreCase);
                var dest = Path.GetFullPath(Path.Combine(modsDir, e.FullName));
                if (!ok || !dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // zip-slip / stray files
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest, overwrite: true);
            }
        }
        else
        {
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) System.IO.File.Copy(path, target, overwrite: true);
            var srcAssets = Path.Combine(Path.GetDirectoryName(path)!, m.Id + ".assets");
            if (Directory.Exists(srcAssets))
                foreach (var f in Directory.EnumerateFiles(srcAssets, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(assets, Path.GetRelativePath(srcAssets, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    System.IO.File.Copy(f, dest, overwrite: true);
                }
        }
        return ModDefinition.Load(target);
    }

    public static void Delete(ModDefinition m)
    {
        if (m.FilePath is null) return;
        var dir = AssetDir(m);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        if (System.IO.File.Exists(m.FilePath)) System.IO.File.Delete(m.FilePath);
    }
}

/// <summary>
/// TXST (the ".ts" next to each content package): which streamed textures the package uses and where they are.
/// u32 version, u32 0, u32 blob count, u32 entry count; blob paths (NUL, padded to 4); entries of 7 u32:
/// [hash][data offset in blob][data size][flag][blob index][hash2][flags]. Offsets point at the DDS data inside
/// the blob's RSCF chunk (checked on all 4,989 entries), which is why blobs can't be resized in place.
/// </summary>
public sealed class TextureTable
{
    public byte[] Head { get; private init; } = new byte[16];
    public List<string> Blobs { get; } = new();
    public List<uint[]> Entries { get; } = new();
    public const int Offset = 1, Size = 2, Blob = 4;

    public static TextureTable Parse(byte[] b)
    {
        var t = new TextureTable { Head = b[..16] };
        int p = 16;
        for (int i = 0; i < Bytes.U32(b, 8); i++)
        {
            int e = Array.IndexOf(b, (byte)0, p);
            t.Blobs.Add(Encoding.Latin1.GetString(b, p, e - p));
            p = (e + 1 + 3) & ~3;
        }
        for (int i = 0; i < Bytes.U32(b, 12); i++, p += 28)
            t.Entries.Add(Enumerable.Range(0, 7).Select(k => Bytes.U32(b, p + k * 4)).ToArray());
        if (p != b.Length) throw new AsuraFormatException($"TXST: {b.Length - p} unexpected trailing bytes");
        return t;
    }

    public byte[] ToBytes()
    {
        var ms = new MemoryStream();
        var head = (byte[])Head.Clone();
        Bytes.PutU32(head, 8, (uint)Blobs.Count);
        Bytes.PutU32(head, 12, (uint)Entries.Count);
        ms.Write(head);
        foreach (var n in Blobs)
        {
            ms.Write(Encoding.Latin1.GetBytes(n));
            do ms.WriteByte(0); while (ms.Length % 4 != 0);
        }
        foreach (var e in Entries) foreach (var w in e) ms.Write(BitConverter.GetBytes(w));
        return ms.ToArray();
    }
}
