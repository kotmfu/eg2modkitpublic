using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// MARE (materials) chunk: u32 0x33, u32 0, u32 count (records), u32 3, u32 n, then n blocks of 1028 bytes, then
/// records [u32 hash][u32 length][length bytes]. Models name their material by hash (per face, and in each
/// geometry RSCF); a record's words at +12.. hold texture hashes (<see cref="FurnitureArt.TextureHash"/>).
/// </summary>
public sealed class Materials
{
    public const int SpecialSize = 1028;
    public byte[] Head { get; private init; } = Array.Empty<byte>();   // 20 bytes + special blocks, kept verbatim
    public List<byte[]> Records { get; } = new();                      // whole record: hash, length, payload

    public static Materials Parse(byte[] b)
    {
        if (b.Length < 20 || Bytes.U32(b, 0) != 0x33) throw new AsuraFormatException("MARE: unknown version");
        int p = 20 + (int)Bytes.U32(b, 16) * SpecialSize;
        var m = new Materials { Head = b[..p] };
        while (p < b.Length)
        {
            int len = (int)Bytes.U32(b, p + 4);
            if (len < 0 || p + 8 + len > b.Length) throw new AsuraFormatException($"MARE: record at {p} overruns");
            m.Records.Add(b[p..(p + 8 + len)]);
            p += 8 + len;
        }
        if (m.Records.Count != Bytes.U32(b, 8)) throw new AsuraFormatException("MARE: record count mismatch");
        return m;
    }

    /// <summary>A materials chunk with just these records (no special blocks), like objectives_loot's.</summary>
    public static byte[] Build(IReadOnlyCollection<byte[]> records) =>
        Bytes.Concat(new[] { Bytes.Le(0x33u, 0u, (uint)records.Count, 3u, 0u) }.Concat(records).ToArray());

    public byte[]? Find(uint hash) => Records.FirstOrDefault(r => Bytes.U32(r, 0) == hash);
}

/// <summary>
/// A furniture item's art: COMA → model (by name hash) → material (per-face hash, MARE) → textures (hash of
/// the texture's path, see <see cref="TextureHash"/>). A model is a run of chunks in its package:
/// HSKN (name), HSKL per LOD ("L1#name"), HMPT, HSBB, RSCF geometry per LOD ("name", "l1#name"), HSKE (name hash).
/// <see cref="Clone"/> copies all of it under same-length new names so the copy can get its own textures.
/// </summary>
public sealed class FurnitureArt
{
    public sealed record Model(string Name, uint Hash, List<Chunk> Chunks, List<uint> Materials);
    /// <summary>A texture a material uses. Chunk = its RSCF in the donor's package, or null when only the streamed (high-res) copy exists.</summary>
    public sealed record Texture(string Name, uint Hash, Chunk? Chunk);

    public List<Model> Models { get; } = new();
    public Dictionary<uint, byte[]> MaterialRecords { get; } = new();
    public Dictionary<uint, Texture> Textures { get; } = new();

    /// <summary>Texture hash: h*31 over the lower-cased path from after "graphics", '/' separators, no extension
    /// ("\graphics\objects\x\bunk_colour.tga" → "/objects/x/bunk_colour"). Matches the .ts tables and MARE.</summary>
    public static uint TextureHash(string path)
    {
        var s = path.Replace('\\', '/');
        int g = s.IndexOf("/graphics/", StringComparison.OrdinalIgnoreCase);
        if (g >= 0) s = s[(g + "/graphics".Length)..];
        else if (s.StartsWith("graphics/", StringComparison.OrdinalIgnoreCase)) s = s["graphics".Length..];
        int dot = s.LastIndexOf('.');
        if (dot > s.LastIndexOf('/')) s = s[..dot];
        return TextTable.KeyHash(s);
    }

    static readonly HashSet<string> ModelTags = new() { "HSKN", "HSKL", "HMPT", "HSBB", "RSCF", "HSKE" };

    /// <summary>The art a COMA uses. Models come from its package's archive; materials from any package or
    /// misc\common.asr (shared ones); textures from the package, else the streamed blobs.</summary>
    public static FurnitureArt Of(GameData game, AsuraArchive arc, byte[] coma, Action<string> log, CancellationToken ct = default)
    {
        var art = new FurnitureArt();
        var chunks = arc.Chunks;
        var materials = game.AllMaterials(log);
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Tag != "HSKN" || chunks[i].Body.Length < 20) continue;
            int nul = Array.IndexOf(chunks[i].Body, (byte)0, 16);
            string name = Bytes.Latin1.GetString(chunks[i].Body, 16, nul - 16);
            uint hash = TextTable.KeyHash(name);
            if (Bytes.Count(coma, Bytes.Le(hash)) == 0) continue;
            // the run ends at the HSKE holding the name hash
            int end = i;
            while (end < chunks.Count && end - i < 64 && ModelTags.Contains(chunks[end].Tag)
                   && !(chunks[end].Tag == "HSKE" && chunks[end].Body.Length >= 12 && Bytes.U32(chunks[end].Body, 8) == hash)) end++;
            if (end >= chunks.Count || chunks[end].Tag != "HSKE") throw new AsuraFormatException($"model {name}: no HSKE closing its chunk run");
            var run = chunks.GetRange(i, end - i + 1);
            // material hashes sit per face in the HSKN, or in the geometry RSCFs for models whose HSKN is only a hierarchy
            var mats = new HashSet<uint>();
            foreach (var c in run)
                for (int p = 0; p + 4 <= c.Body.Length; p++)
                    if (materials.ContainsKey(Bytes.U32(c.Body, p))) mats.Add(Bytes.U32(c.Body, p));
            art.Models.Add(new Model(name, hash, run, mats.ToList()));
            foreach (var m in mats) art.MaterialRecords[m] = materials[m];
        }
        var inPackage = new Dictionary<uint, Chunk>();
        foreach (var c in chunks.Where(c => c.Tag == "RSCF"))
        {
            var e = EmbeddedFile.Parse(c.Body);
            if (e.IsDds) inPackage.TryAdd(TextureHash(e.Path), c);
        }
        Dictionary<uint, string>? streamed = null;
        foreach (var rec in art.MaterialRecords.Values)
            for (int p = 8; p + 4 <= rec.Length; p += 4)
            {
                uint h = Bytes.U32(rec, p);
                if (art.Textures.ContainsKey(h)) continue;
                if (inPackage.TryGetValue(h, out var tc)) { art.Textures[h] = new Texture(EmbeddedFile.Parse(tc.Body).Path, h, tc); continue; }
                streamed ??= game.AllAssets(log, ct).Where(x => x.Streamed && x.Kind == "Texture")
                    .GroupBy(x => TextureHash(x.Name)).ToDictionary(g => g.Key, g => g.First().Name);
                if (streamed.TryGetValue(h, out var n)) art.Textures[h] = new Texture(n, h, null);
            }
        return art;
    }

    /// <summary>The art of a furniture item by record name (loads its package; a few seconds).</summary>
    public static FurnitureArt ForItem(GameData game, string item, Action<string> log, CancellationToken ct = default)
    {
        var row = game.FindFurniture(item) ?? throw new KeyNotFoundException($"no furniture item {item}");
        var pkg = game.Install.Package(row.Package) ?? throw new KeyNotFoundException($"package {row.Package} not found");
        var arc = AsuraArchive.Load(pkg.ContentPath);
        var rec = FurnitureTable.Parse(arc.First("fntr")!.Body).Records.First(r => r.Name.Equals(item, StringComparison.OrdinalIgnoreCase));
        var fnas = arc.Find("fnas").Where(c => c.Body.Length >= 12).Select(c => Bytes.U32(c.Body, 8)).First(rec.ContainsU32);
        var coma = arc.Find("COMA").First(c => c.Body.Length >= 16 && Bytes.U32(c.Body, 12) == fnas);
        var art = Of(game, arc, coma.Body, log, ct);
        art.Icon = FindIcon(game, arc, rec.IconKey);
        return art;
    }

    /// <summary>Best original DDS of a texture: the high-res streamed copy when the game has one, else the package copy.</summary>
    public static byte[] Original(GameData game, Texture t, Action<string> log, CancellationToken ct = default) =>
        HighRes(game, t.Name, log, ct) ?? EmbeddedFile.Parse(t.Chunk!.Body).Data;

    public static byte[]? HighRes(GameData game, string texture, Action<string> log, CancellationToken ct = default)
    {
        uint h = TextureHash(texture);   // names differ by a leading backslash between packages and blobs
        var hi = game.AllAssets(log, ct).FirstOrDefault(a => a.Streamed && a.Kind == "Texture" && TextureHash(a.Name) == h);
        return hi is null ? null : AssetIndex.Payload("RSCF", AssetIndex.ReadBody(game.Install, hi));
    }

    /// <summary>Same-length name whose last characters are digits from <paramref name="seed"/> (lengths decide
    /// padding inside these chunks, so a same-length rename keeps every offset). For paths, only the file stem changes.</summary>
    public static string Rename(string name, uint seed)
    {
        int stemEnd = name.LastIndexOf('.') > Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/')) ? name.LastIndexOf('.') : name.Length;
        int stemStart = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/')) + 1;
        int k = Math.Min(4, stemEnd - stemStart - 1);
        if (k < 1) throw new ArgumentException($"name too short to rename: {name}");
        string digits = (seed % (uint)Math.Pow(10, k)).ToString().PadLeft(k, '0');
        return name[..(stemEnd - k)] + digits + name[stemEnd..];
    }

    /// <summary>Build-menu icon of an item: found by the item's (record) name.</summary>
    public static string IconPath(string item) => $@"data\graphics\gui\icons\furniture\{item.ToLowerInvariant()}.tga";

    /// <summary>What a furniture record stores to pick its icon (see <see cref="FurnitureRecord.IconKey"/>).</summary>
    public static uint IconKey(string iconName) => TextTable.KeyHash("data/graphics/gui/icons/furniture/" + iconName);

    /// <summary>Icon key of any GUI texture path ("data\graphics\gui\icons\...\x.tga" → h31 of "data/graphics/gui/icons/.../x").</summary>
    public static uint GuiKey(string path)
    {
        var s = path.Replace('\\', '/').TrimStart('/');
        int dot = s.LastIndexOf('.');
        return TextTable.KeyHash(dot > s.LastIndexOf('/') ? s[..dot] : s);
    }

    /// <summary>The icon a record's key points at, with its RSCF (from <paramref name="arc"/> or its own package). Null if none.</summary>
    public static Texture? FindIcon(GameData game, AsuraArchive? arc, uint key)
    {
        var entry = game.PackageAssets.FirstOrDefault(a => a.Tag == "RSCF" && a.Name.Contains(@"\gui\", StringComparison.OrdinalIgnoreCase) && GuiKey(a.Name) == key);
        if (entry is null) return null;
        bool Same(Chunk c) => c.Tag == "RSCF" && EmbeddedFile.Parse(c.Body).Path.Equals(entry.Name, StringComparison.OrdinalIgnoreCase);
        var chunk = arc?.Chunks.FirstOrDefault(Same) ?? AsuraArchive.Load(game.Install.Full(entry.File)).Chunks.FirstOrDefault(Same);
        return chunk is null ? null : new Texture(entry.Name, TextureHash(entry.Name), chunk);
    }

    /// <summary>The donor's icon, when its package has one (set by <see cref="ForItem"/>).</summary>
    public Texture? Icon { get; private set; }

    /// <summary>
    /// A texture RSCF at <paramref name="path"/>: the template's header (or the game's usual one when there is no
    /// template) with zero filler padding the path to 4 bytes, as in the game's own; data = <paramref name="data"/> or the template's.
    /// </summary>
    public static Chunk TextureChunk(Chunk? template, string path, byte[]? data)
    {
        // game's own headers: all 1,757 GUI textures use flags 0x2004420; material textures mostly 0,2,2,0
        bool gui = path.Contains(@"\gui\", StringComparison.OrdinalIgnoreCase);
        var e = template is not null ? EmbeddedFile.Parse(template.Body)
            : gui ? new EmbeddedFile { F0 = 0, F1 = 0, Version = 2, Flags = 0x2004420 } : new EmbeddedFile { F0 = 0, F1 = 2, Version = 2, Flags = 0 };
        if (e.Path.Length != path.Length) e.Filler = new byte[(4 - (20 + path.Length + 1) % 4) % 4];   // same length keeps the game's own filler
        e.Path = path;
        if (data is not null) e.Data = data;
        if (e.Data.Length == 0) throw new InvalidOperationException($"texture {path}: no data");
        return new Chunk("RSCF", e.ToBytes());
    }

    public sealed record CloneResult(byte[] Coma, List<Chunk> Textures, List<byte[]> MaterialRecords, List<Chunk> ModelChunks,
                                     List<uint> ModelHashes, List<string> Notes);

    /// <summary>
    /// Copies the models, materials and textures under new names/hashes and points the COMA at them.
    /// <paramref name="newId"/> gives stable ids per key; <paramref name="textureData"/> may supply a texture's
    /// DDS (by original texture path), otherwise the original is copied.
    /// </summary>
    public CloneResult Clone(byte[] coma, Func<string, uint> newId, Func<string, byte[]?> textureData, ISet<uint> takenHashes)
    {
        var notes = new List<string>();
        coma = (byte[])coma.Clone();
        string Fresh(string name, string key, Func<string, uint> hash)
        {
            for (uint s = newId(key); ; s++)
            {
                var n = Rename(name, s);
                if (takenHashes.Add(hash(n))) return n;
            }
        }
        // textures
        var texMap = new Dictionary<uint, uint>();
        var texChunks = new List<Chunk>();
        foreach (var t in Textures.Values)
        {
            string name = Fresh(t.Name, "tex/" + t.Name, TextureHash);
            uint h = TextureHash(name);
            texMap[t.Hash] = h;
            var data = textureData(t.Name);
            if (t.Chunk is null && data is null) { notes.Add($"texture {t.Name}: no copy found; stays shared"); continue; }
            texChunks.Add(TextureChunk(t.Chunk, name, data));
            notes.Add($"texture {t.Name} → {name}");
        }
        // materials
        var matMap = new Dictionary<uint, uint>();
        var records = new List<byte[]>();
        foreach (var (m, rec) in MaterialRecords)
        {
            uint n = newId($"mat/{m:x8}");
            matMap[m] = n;
            var copy = (byte[])rec.Clone();
            Bytes.PutU32(copy, 0, n);
            for (int p = 8; p + 4 <= copy.Length; p += 4)
                if (texMap.TryGetValue(Bytes.U32(copy, p), out var nt)) Bytes.PutU32(copy, p, nt);
            records.Add(copy);
        }
        // models
        var modelChunks = new List<Chunk>();
        var modelHashes = new List<uint>();
        foreach (var model in Models)
        {
            string name = Fresh(model.Name, "model/" + model.Name, TextTable.KeyHash);
            uint h = TextTable.KeyHash(name);
            int tail = model.Name.Length - Enumerable.Range(0, model.Name.Length).First(i => model.Name[i] != name[i]);
            foreach (var c in model.Chunks)
            {
                var body = RenameAll(c.Body, model.Name, name[^tail..]);
                body = Bytes.Replace(body, Bytes.Le(model.Hash), Bytes.Le(h));
                foreach (var (o, n) in matMap) body = Bytes.Replace(body, Bytes.Le(o), Bytes.Le(n));
                modelChunks.Add(new Chunk(c.Tag, body));
            }
            int inComa = Bytes.Count(coma, Bytes.Le(model.Hash));
            coma = Bytes.Replace(coma, Bytes.Le(model.Hash), Bytes.Le(h));
            modelHashes.Add(h);
            notes.Add($"model {model.Name} → {name} ({model.Chunks.Count} chunks, {inComa} refs in COMA, materials {string.Join(", ", model.Materials.Select(Bytes.Hex))})");
        }
        return new CloneResult(coma, texChunks, records, modelChunks, modelHashes, notes);
    }

    /// <summary>Every case-insensitive occurrence of <paramref name="name"/> gets its last characters replaced
    /// (the occurrence keeps its own casing elsewhere: "Bunk_Tier_1", "bunk_tier_1", "L1#Bunk_Tier_1").</summary>
    static byte[] RenameAll(byte[] body, string name, string newTail)
    {
        var copy = (byte[])body.Clone();
        var s = Bytes.Latin1.GetString(body);
        for (int k = s.IndexOf(name, StringComparison.OrdinalIgnoreCase); k >= 0; k = s.IndexOf(name, k + 1, StringComparison.OrdinalIgnoreCase))
            Bytes.Latin1.GetBytes(newTail).CopyTo(copy, k + name.Length - newTail.Length);
        return copy;
    }
}
