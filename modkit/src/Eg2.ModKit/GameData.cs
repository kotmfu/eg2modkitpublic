using System.ComponentModel;
using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

public sealed record TextInfo(string Table, string Key, string Text);

public sealed class FurnitureRow
{
    public string Package { get; init; } = "";
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public uint? Cost { get; init; }
    [Browsable(false)] public uint NameHash { get; init; }
}

/// <summary>
/// Everything the builder needs from the base game: the text index (hash -> table/key/text),
/// the merged furniture catalog, and every id the base game already uses (for collision checks).
/// </summary>
public sealed class GameData
{
    public required GameInstall Install { get; init; }
    public required string Language { get; init; }
    public Dictionary<uint, TextInfo> Text { get; } = new();
    public SortedSet<string> TextTables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FurnitureRow> Furniture { get; } = new();
    public HashSet<uint> TakenIds { get; } = new();
    /// <summary>Every data object and furniture record in every base package.</summary>
    public List<GameObject> Objects { get; } = new();
    public Dictionary<uint, List<GameObject>> ObjectsById { get; } = new();
    /// <summary>Named assets inside the content packages (filled by Load). See <see cref="AllAssets"/> for the rest.</summary>
    public List<AssetEntry> PackageAssets { get; } = new();
    List<AssetEntry>? _allAssets;
    /// <summary>Material records (MARE) of every content package by material hash; see <see cref="AllMaterials"/>.</summary>
    public Dictionary<uint, byte[]> PackageMaterials { get; } = new();
    bool _commonMaterials;

    /// <summary>Package materials plus misc\common.asr's shared ones (loaded once, ~255 MB archive; only the records are kept).</summary>
    public Dictionary<uint, byte[]> AllMaterials(Action<string> log)
    {
        if (_commonMaterials) return PackageMaterials;
        var common = Install.Full(Path.Combine("misc", "common.asr"));
        if (File.Exists(common))
        {
            log(@"reading shared materials (misc\common.asr)");
            if (AsuraArchive.Load(common).First("MARE") is { } m)
                foreach (var r in Materials.Parse(m.Body).Records) PackageMaterials.TryAdd(Bytes.U32(r, 0), r);
        }
        _commonMaterials = true;
        return PackageMaterials;
    }

    /// <summary>Package assets plus every sound bank and texture blob (header scan, cached after the first call).</summary>
    public List<AssetEntry> AllAssets(Action<string> log, CancellationToken ct = default)
    {
        if (_allAssets is not null) return _allAssets;
        var all = new List<AssetEntry>(PackageAssets);
        foreach (var rel in AssetIndex.RawStores(Install))
        {
            ct.ThrowIfCancellationRequested();
            var found = AssetIndex.ScanRaw(Install.Root, rel);
            log($"{rel}: {found.Count} assets");
            all.AddRange(found);
        }
        return _allAssets = all;
    }

    public FurnitureRow? FindFurniture(string name) =>
        Furniture.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static GameData Load(GameInstall install, string lang, Action<string> log, CancellationToken ct = default)
    {
        var g = new GameData { Install = install, Language = lang };

        log($"reading text ({lang})");
        foreach (var (table, path) in install.TextFiles(lang))
        {
            ct.ThrowIfCancellationRequested();
            g.TextTables.Add(table);
            foreach (var c in AsuraArchive.Load(path).Find("HTXT"))
                foreach (var e in TextTable.Parse(c.Body).Entries)
                    g.Text.TryAdd(e.Hash, new TextInfo(table, e.Key ?? "", e.Text));
        }
        log($"text: {g.Text.Count} entries in {g.TextTables.Count} tables ({lang})");

        var packages = install.Packages().ToList();
        int n = 0;
        foreach (var p in packages)
        {
            ct.ThrowIfCancellationRequested();
            log($"reading package {++n}/{packages.Count}: {p.Name}");
            if (File.Exists(p.ManifestPath))
            {
                var rp = AsuraArchive.Load(p.ManifestPath).First("rpkg");
                if (rp is not null)
                {
                    var pkg = Package.Parse(rp.Tag, rp.Body);
                    g.TakenIds.Add(pkg.PackageId);
                    g.TakenIds.Add(pkg.AuxId);
                    foreach (var ids in pkg.IdArrays().Values) g.TakenIds.UnionWith(ids);
                }
            }

            // ponytail: loads a whole content archive at a time (~1 GB peak for furniture); stream chunks if memory matters
            var arc = AsuraArchive.Load(p.ContentPath);
            foreach (var c in arc.Chunks)
            {
                if (c.Tag == "COMA" && c.Body.Length >= 16) g.TakenIds.Add(Bytes.U32(c.Body, 12));
                else if (c.IsDataObjectTag && c.Tag != "fntr" && c.Body.Length >= ObjectHeader.Size)
                {
                    g.TakenIds.Add(Bytes.U32(c.Body, 8));
                    g.TakenIds.Add(Bytes.U32(c.Body, 20));
                    g.Objects.Add(new GameObject { Tag = c.Tag, Package = p.Name, ObjectId = Bytes.U32(c.Body, 8), Body = c.Body });
                }
            }
            g.PackageAssets.AddRange(AssetIndex.FromChunks(install.Rel(p.ContentPath), arc.Chunks));
            if (arc.First("MARE") is { } mare)
                foreach (var r in Materials.Parse(mare.Body).Records) g.PackageMaterials.TryAdd(Bytes.U32(r, 0), r);
            var fntr = arc.First("fntr");
            if (fntr is null || fntr.Body.Length <= 13) continue;
            foreach (var r in FurnitureTable.Parse(fntr.Body).Records)
            {
                uint hash = r.HasTextRef(0) ? r.TextHash(0) : 0;
                g.Furniture.Add(new FurnitureRow
                {
                    Package = p.Name, Name = r.Name, NameHash = hash,
                    DisplayName = g.Text.TryGetValue(hash, out var t) ? t.Text : "",
                    Cost = r.HasCost ? r.Cost : null,
                });
                g.Objects.Add(new GameObject { Tag = "fntr", Package = p.Name, RecordName = r.Name, Body = r.Payload });
            }
        }
        // the interface's own images (genius select icons, loading screens, character icons, logos...) live in gui\*.asr,
        // outside every package; the builder patches them as whole files like sound banks
        // and each lair island level (envs\lair_*.pc) carries its own copies of ~200 island textures (grass, leaves, sand),
        // which is what the game draws there: replacing only the streamed copies leaves those islands unchanged
        var archives = new[] { "gui.asr", "main.asr", "splash.asr" }.Select(n => Path.Combine(install.Root, "gui", n))
            .Concat(Directory.Exists(Path.Combine(install.Root, "envs")) ? Directory.EnumerateFiles(Path.Combine(install.Root, "envs"), "lair_*.pc") : []);
        foreach (var path in archives)
        {
            if (!File.Exists(path)) continue;
            ct.ThrowIfCancellationRequested();
            log($"reading {install.Rel(path)}");
            g.PackageAssets.AddRange(AssetIndex.FromChunks(install.Rel(path), AsuraArchive.Load(path).Chunks));
        }
        g.TakenIds.Remove(0);
        foreach (var o in g.Objects)
        {
            o.Name = ObjectInspector.DisplayName(o, g);
            if (o.IsRecord) continue;
            if (!g.ObjectsById.TryGetValue(o.ObjectId, out var list)) g.ObjectsById[o.ObjectId] = list = new();
            list.Add(o);
        }
        NameTags(g);
        log($"furniture: {g.Furniture.Count} items; {g.Objects.Count} objects; {g.TakenIds.Count} ids in use");
        return g;
    }

    /// <summary>
    /// rtag objects are only a header, and their source names aren't shipped (no string in the packages or exe
    /// hashes to their ids), so each is named after what uses it, e.g. "Furniture: Muscle Bed, Science Bed +2".
    /// </summary>
    static void NameTags(GameData g)
    {
        var users = g.Objects.Where(o => o.Tag == "rtag").ToDictionary(o => o.ObjectId, _ => new List<GameObject>());
        foreach (var o in g.Objects.Where(o => o.Tag != "rtag"))
            for (int i = o.IsRecord ? 0 : ObjectHeader.Size; i + 4 <= o.Body.Length; i++)
                if (users.TryGetValue(Bytes.U32(o.Body, i), out var l) && (l.Count == 0 || l[^1] != o)) l.Add(o);
        static string Short(GameObject o) => (o.IsRecord && o.Name.IndexOf("  (") is > 0 and var i ? o.Name[(i + 3)..^1] : o.Name).Trim('"');
        foreach (var t in g.Objects.Where(o => o.Tag == "rtag"))
        {
            var byType = users[t.ObjectId].GroupBy(o => o.Tag).OrderByDescending(x => x.Count()).ToList();
            if (byType.Count == 0) { t.Name = $"{Bytes.Hex(t.ObjectId)} (unused)"; continue; }
            string Label(string tag) => ObjectInspector.Types.TryGetValue(tag, out var ti) ? ti.List : tag;
            var names = byType[0].Select(Short).Distinct().ToList();
            t.Name = $"{Label(byType[0].Key)}: {string.Join(", ", names.Take(3))}{(names.Count > 3 ? $" +{names.Count - 3}" : "")}"
                     + (byType.Count > 1 ? $"; also {string.Join(", ", byType.Skip(1).Select(x => Label(x.Key).ToLowerInvariant()))}" : "");
        }
    }
}
