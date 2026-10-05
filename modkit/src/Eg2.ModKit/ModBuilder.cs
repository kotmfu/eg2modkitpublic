using System.Text;
using System.Text.RegularExpressions;
using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// Deterministic ids: FNV-1a of "eg2modkit:" + key, re-rolled with "#n" on collision,
/// 0 or 0xFFFFFFFF, so rebuilds and mod reordering keep save compatibility.
/// </summary>
public sealed class IdAllocator
{
    readonly HashSet<uint> _taken;

    public IdAllocator(IEnumerable<uint> taken) => _taken = new HashSet<uint>(taken);

    public uint Get(string key)
    {
        for (int n = 0; ; n++)
        {
            uint h = Fnv1a("eg2modkit:" + key + (n == 0 ? "" : "#" + n));
            if (h != 0 && h != 0xFFFFFFFF && _taken.Add(h)) return h;
        }
    }

    public static uint Fnv1a(string s)
    {
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(s)) h = unchecked((h ^ b) * 16777619);
        return h;
    }
}

public sealed class BuildResult
{
    /// <summary>Output files keyed by path relative to the game root.</summary>
    public SortedDictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; } = new();
    public List<string> Report { get; } = new();
    public bool Ok => Errors.Count == 0;
    /// <summary>New objects copied from patch-only packages (frontend): added to that package's own content patch.</summary>
    internal Dictionary<string, List<Chunk>> PatchObjects { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>New textures for patch-only packages (frontend), added to that package's content patch.</summary>
    internal Dictionary<string, List<Chunk>> PatchTextures { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>New lair stem -> its own world-map region id (its saves carry it; the runtime DLL looks for it).</summary>
    internal Dictionary<string, uint> LairRegions { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Game file, byte offset, replacement file (under bin\eg2modkit_blocks): swapped in by the runtime DLL.</summary>
    internal List<(string File, long Offset, string Source)> FileBlocks { get; } = new();
    /// <summary>New lair stem -> its own streamed textures: (blob file lowercase, data offset of the game's copy) -> place
    /// in ModKit's blob.</summary>
    internal Dictionary<string, Dictionary<(string, long), (uint Offset, uint Size)>> IslandTextures { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Merges every enabled mod into: ONE dev-slot package (new furniture), one full
/// .asrpatch per touched text table, one full content .asrpatch per package with
/// cost edits. Follows HANDOFF §6/§8 exactly; every output is re-parsed and checked.
/// </summary>
public static class ModBuilder
{
    public const string Slot = "e3data";
    static readonly Regex ItemIdRx = new("^[A-Za-z0-9_]+$");
    static readonly Regex ModIdRx = new("^[A-Za-z0-9_-]+$");

    public static string TextKey(string modId, string itemId, string suffix) =>
        Regex.Replace($"MOD_{modId}_{itemId}_{suffix}".ToUpperInvariant(), "[^A-Z0-9_]", "_");

    public static BuildResult Build(GameData game, IReadOnlyList<ModDefinition> mods, Action<string> log, CancellationToken ct = default)
    {
        var r = new BuildResult();
        var ids = new IdAllocator(game.TakenIds);
        var refs = NewObjectIds(mods, ids);
        var text = Validate(game, mods, refs, r);
        if (!r.Ok) return r;
        try
        {
            var items = mods.SelectMany(m => m.NewFurniture.Select(f => (Mod: m, Item: f))).ToList();
            var objects = mods.SelectMany(m => m.NewObjects.Select(o => (Mod: m, Obj: o))).ToList();
            if (items.Count > 0 || objects.Count > 0 || mods.Any(m => m.NewAssets.Count > 0 || m.EngineeringTrees.Any(t => t.Background is { Length: > 0 }))) BuildDevSlot(game, items, objects, refs, ids, r, log, ct, mods);
            if (r.Ok) BuildOverrides(game, mods, refs, ids, r, log, ct);
            if (r.Ok) BuildPackagePatches(game, mods, refs, r, log, ct);
            if (r.Ok) BuildTextureStreams(game, mods.SelectMany(m => m.Assets.Where(IsStreamed).Select(a => (Mod: m, A: a))).ToList(), r, log, ct);
            if (r.Ok) BuildStreamSounds(game, mods.SelectMany(m => m.Assets.Where(a => a.Tag == "ASTS").Select(a => (Mod: m, A: a))).ToList(), r, log, ct);
            if (r.Ok) BuildNewLairs(game, mods, refs, r, log);
            if (r.Ok) BuildMaps(game, mods, r, log, ct);
            if (r.Ok) BuildScenery(game, mods, r, log, ct);
            if (r.Ok) BuildLairSettings(game, mods, r, log);
            if (r.Ok) BuildTextPatches(game, text, r, log, ct);
            if (r.Ok) foreach (var v in mods.SelectMany(m => m.SkipVideos).Select(v => v.Replace('/', Path.DirectorySeparatorChar)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    r.Files[v + GameInstall.PatchSuffix] = Array.Empty<byte>();
                    r.Report.Add($"skip video {v}");
                }
            if (r.Ok) BuildRuntime(mods, r);
            // a dev slot with nothing in it (every new object went into a patch-only package): leave it out
            string slotContent = $@"{GameInstall.DevSlotDir}\{Slot}_Content.asr";
            if (r.Ok && r.Files.TryGetValue(slotContent, out var slot) && AsuraArchive.FromBytes(slot).Chunks.All(c => c.Tag is "FNFO" or "RSFL"))
                foreach (var k in r.Files.Keys.Where(k => k.StartsWith($@"{GameInstall.DevSlotDir}\{Slot}", StringComparison.OrdinalIgnoreCase)).ToList()) r.Files.Remove(k);
        }
        catch (Exception e) when (e is AsuraFormatException or InvalidOperationException or KeyNotFoundException)
        {
            r.Errors.Add(e.Message);
        }
        if (!r.Ok) r.Files.Clear();
        else if (r.Files.Count == 0) r.Report.Add("nothing to build: no enabled mod has any content");
        return r;
    }

    /// <summary>
    /// Ids for every new object, allocated before anything else so "@Key" references resolve. Keys are
    /// "modid/Key" and, when no other mod uses the same Key, plain "Key".
    /// </summary>
    static Dictionary<string, uint> NewObjectIds(IReadOnlyList<ModDefinition> mods, IdAllocator ids)
    {
        var refs = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods)
            foreach (var o in m.NewObjects)
                refs[$"{m.Id}/{o.Key}"] = ids.Get($"{m.Id}/obj/{o.Key}");
        foreach (var g in mods.SelectMany(m => m.NewObjects.Select(o => (m, o))).GroupBy(x => x.o.Key, StringComparer.OrdinalIgnoreCase))
            if (g.Count() == 1) refs[g.Key] = refs[$"{g.First().m.Id}/{g.Key}"];
        // new furniture's fnas ids, so research can require / unlock them by name
        foreach (var m in mods)
            foreach (var f in m.NewFurniture)
                refs[FurnitureRef(f.Id)] = ids.Get($"{m.Id}/{f.Id}/fnas");
        return refs;
    }

    static GameObject? SourceOf(GameData game, NewObject o) =>
        game.Objects.FirstOrDefault(x => !x.IsRecord && x.Package.Equals(o.Package, StringComparison.OrdinalIgnoreCase) && x.Tag == o.Tag
                                         && x.Key.Equals(o.Source, StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks everything that can be checked without touching archives. Returns table -> key -> text.</summary>
    static Dictionary<string, Dictionary<string, string>> Validate(GameData game, IReadOnlyList<ModDefinition> mods, IReadOnlyDictionary<string, uint> refs, BuildResult r)
    {
        var text = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var textOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var costs = new Dictionary<string, (uint Cost, string Mod)>(StringComparer.OrdinalIgnoreCase);
        var newNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var modIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new Dictionary<string, (string Hex, string Mod)>();
        var runtime = new Dictionary<string, (RuntimePatch Patch, string Mod)>(StringComparer.OrdinalIgnoreCase);
        var assets = new Dictionary<string, string>();

        void SetText(string mod, string table, string key, string value)
        {
            if (!text.TryGetValue(table, out var t)) text[table] = t = new(StringComparer.Ordinal);
            string slot = $"{table}/{key}";
            if (t.TryGetValue(key, out var prev) && prev != value)
                r.Errors.Add($"conflict: {slot} set by {textOwner[slot]} and {mod} to different text");
            else { t[key] = value; textOwner.TryAdd(slot, mod); }
        }

        foreach (var m in mods)
        {
            if (!ModIdRx.IsMatch(m.Id)) { r.Errors.Add($"mod '{m.Id}': id must be letters, digits, _ or -"); continue; }
            if (!modIds.Add(m.Id)) r.Errors.Add($"two enabled mods share the id '{m.Id}'");

            foreach (var f in m.NewFurniture)
            {
                string where = $"{m.Id}: new furniture '{f.Id}'";
                if (!ItemIdRx.IsMatch(f.Id)) { r.Errors.Add($"{where}: id must be letters, digits or _"); continue; }
                if (game.FindFurniture(f.Id) is not null) r.Errors.Add($"{where}: the game already has an item with that name");
                if (newNames.TryGetValue(f.Id, out var other)) r.Errors.Add($"{where}: also added by {other}");
                newNames[f.Id] = m.Id;
                var donor = game.FindFurniture(f.Donor);
                if (donor is null) r.Errors.Add($"{where}: donor '{f.Donor}' not found");
                else if (donor.NameHash == 0 || donor.Cost is null) r.Errors.Add($"{where}: donor '{f.Donor}' has no name/cost fields");
                if (string.IsNullOrWhiteSpace(f.DisplayName)) r.Errors.Add($"{where}: display name is empty");
                if (f.Textures.Any(t => !IsIcon(t)) && !f.OwnArt)
                    r.Errors.Add($"{where}: texture replacements need \"own art\" switched on (only the icon can change without it)");
                foreach (var t in f.Textures)
                {
                    var path = Path.Combine(ModPackage.AssetDir(m), t.Source);
                    if (m.FilePath is null || !File.Exists(path)) r.Errors.Add($"{where}: texture file {t.Source} is missing from the mod's assets folder");
                    else if (!File.ReadAllBytes(path).AsSpan().StartsWith("DDS "u8)) r.Errors.Add($"{where}: {t.Source} is not a DDS file");
                }

                foreach (var (suffix, value) in new[] { ("NAME", f.DisplayName), ("DESC", f.Description) })
                {
                    if (string.IsNullOrEmpty(value)) continue;
                    string key = TextKey(m.Id, f.Id, suffix);
                    if (game.Text.TryGetValue(TextTable.KeyHash(key), out var hit))
                        r.Errors.Add($"{where}: text key {key} collides with {hit.Table}/{hit.Key}");
                    SetText(m.Id, OwnTextTable, key, value);
                }
            }

            var objKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in m.NewObjects)
            {
                string where = $"{m.Id}: new object '{o.Key}'";
                if (!ItemIdRx.IsMatch(o.Key)) { r.Errors.Add($"{where}: name must be letters, digits or _"); continue; }
                if (!objKeys.Add(o.Key)) r.Errors.Add($"{where}: name used twice in this mod");
                var src = SourceOf(game, o);
                if (src is null) { r.Errors.Add($"{where}: source {o.Package}/{o.Tag}/{o.Source} not found"); continue; }
                if (o.Into is not null && ViaOverride(o.Into)) r.Errors.Add($"{where}: 'into' can only be {string.Join(", ", PatchOnly)}");
                foreach (var t in o.Texts)
                {
                    if (t.Offset < ObjectHeader.Size || t.Offset + 4 > src.Body.Length || !game.Text.TryGetValue(Bytes.U32(src.Body, t.Offset), out var orig))
                    { r.Errors.Add($"{where}: offset {t.Offset} is not a text reference"); continue; }
                    SetText(m.Id, OwnTableRef(game, src.Body, t.Offset) ? OwnTextTable : orig.Table, TextKey(m.Id, o.Key, $"T{t.Offset}"), t.Text);
                }
                foreach (var f in o.Edits)
                {
                    try { if (f.Offset < ObjectHeader.Size || f.Offset + f.Encode(refs).Length > src.Body.Length) r.Errors.Add($"{where}: offset {f.Offset} outside the object"); }
                    catch (Exception e) when (e is FormatException or OverflowException) { r.Errors.Add($"{where}: bad value '{f.Value}' for {f.Type} ({e.Message})"); }
                }
            }

            foreach (var e in m.FurnitureEdits)
            {
                var row = game.FindFurniture(e.Name);
                if (row is null) { r.Errors.Add($"{m.Id}: cost edit: '{e.Name}' not found in the game"); continue; }
                if (row.Cost is null) { r.Errors.Add($"{m.Id}: cost edit: '{e.Name}' has no cost field"); continue; }
                if (costs.TryGetValue(e.Name, out var prev) && prev.Cost != e.Cost)
                    r.Errors.Add($"conflict: {e.Name} cost set to {prev.Cost} by {prev.Mod} and {e.Cost} by {m.Id}");
                else costs[e.Name] = (e.Cost, m.Id);
            }

            foreach (var f in m.FieldEdits)
            {
                string where = $"{m.Id}: field {f.Package}/{f.Tag}/{f.Object}+{f.Offset}";
                if (game.Install.Package(f.Package) is null) { r.Errors.Add($"{where}: package not found"); continue; }
                byte[] enc;
                try { enc = f.Encode(refs); }
                catch (Exception e) when (e is FormatException or OverflowException) { r.Errors.Add($"{where}: bad value '{f.Value}' for {f.Type}"); continue; }
                if (f.Expect.Length != enc.Length * 2) { r.Errors.Add($"{where}: expect must be {enc.Length} bytes of hex"); continue; }
                string slot = $"{f.Package}/{f.Tag}/{f.Object}/{f.Offset}".ToLowerInvariant();
                string hex = Convert.ToHexString(enc);
                if (fields.TryGetValue(slot, out var prev) && prev.Hex != hex)
                    r.Errors.Add($"conflict: {where} set differently by {prev.Mod} and {m.Id}");
                else fields[slot] = (hex, m.Id);
            }

            foreach (var rp in m.Runtime)
            {
                if (rp.Error() is { } err) { r.Errors.Add($"{m.Id}: runtime '{rp.Name}': {err}"); continue; }
                if (runtime.TryGetValue(rp.Name, out var prev) && (prev.Patch.Where != rp.Where || prev.Patch.Value.Trim() != rp.Value.Trim()))
                    r.Errors.Add($"conflict: runtime '{rp.Name}' set differently by {prev.Mod} and {m.Id}");
                else runtime[rp.Name] = (rp, m.Id);
            }

            foreach (var a in m.Assets)
            {
                string where = $"{m.Id}: asset {a.Name}";
                if (!File.Exists(game.Install.Full(a.File))) { r.Errors.Add($"{where}: game file {a.File} not found"); continue; }
                if (m.FilePath is null || !File.Exists(SourcePath(m, a))) { r.Errors.Add($"{where}: replacement file {a.Source} is missing from the mod's assets folder"); continue; }
                if (a.Island is not null && !IsStreamed(a) && !a.File.EndsWith(".pc", StringComparison.OrdinalIgnoreCase) && !a.File.EndsWith(".pc.pc.sounds", StringComparison.OrdinalIgnoreCase)) { r.Errors.Add($"{where}: only an island's streamed textures, .pc or .pc.pc.sounds can be one island's own"); continue; }
                string slot = a.Island is null ? a.Slot : $"{a.Island}|{a.Slot}";   // an island's own copy doesn't clash with the shared one
                if (assets.TryGetValue(slot, out var prev)) r.Errors.Add($"conflict: {a.File} {a.Name} replaced by both {prev} and {m.Id}");
                else assets[slot] = m.Id;
            }

            foreach (var t in m.TextEdits)
            {
                if (!game.TextTables.Contains(t.Table)) { r.Errors.Add($"{m.Id}: text table '{t.Table}' not found"); continue; }
                if (string.IsNullOrWhiteSpace(t.Key) || t.Key.Any(c => c > 127)) { r.Errors.Add($"{m.Id}: bad text key '{t.Key}'"); continue; }
                SetText(m.Id, t.Table, t.Key, t.Text);
            }
        }

        // new keys must not collide with each other either
        foreach (var dup in text.SelectMany(kv => kv.Value.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                     .GroupBy(TextTable.KeyHash).Where(g => g.Count() > 1))
            r.Errors.Add($"text keys {string.Join(", ", dup)} share hash {Bytes.Hex(dup.Key)}");
        return text;
    }

    static void BuildDevSlot(GameData game, List<(ModDefinition Mod, NewFurniture Item)> items, List<(ModDefinition Mod, NewObject Obj)> objects,
                             IReadOnlyDictionary<string, uint> refs, IdAllocator ids, BuildResult r, Action<string> log, CancellationToken ct,
                             IReadOnlyList<ModDefinition> mods)
    {
        var install = game.Install;
        var records = new List<(string Id, uint Cost, uint Text)>();
        var comas = new List<Chunk>();
        var fnasChunks = new List<Chunk>();
        var fnasIds = new List<uint>();
        var artTextures = new List<Chunk>();
        var artMaterials = new List<byte[]>();
        var artModels = new List<Chunk>();
        var modelHashes = new List<uint>();
        var table = new FurnitureTable();
        bool haveTemplate = false;

        foreach (var group in items.GroupBy(x => game.FindFurniture(x.Item.Donor)!.Package))
        {
            ct.ThrowIfCancellationRequested();
            var pkg = install.Package(group.Key) ?? throw new KeyNotFoundException($"package {group.Key} vanished");
            if (!pkg.Name.Equals("furniture", StringComparison.OrdinalIgnoreCase))
                r.Report.Add($"warning: donors from '{pkg.Name}' are untested (only 'furniture' is proven in-game)");
            log($"loading donors from {pkg.Name}");
            AsuraArchive? arc = AsuraArchive.Load(pkg.ContentPath);
            var donors = FurnitureTable.Parse(arc.First("fntr")!.Body);
            if (!haveTemplate) { table.Version = donors.Version; table.Pad = donors.Pad; table.Flag = donors.Flag; haveTemplate = true; }
            var fnasById = arc.Find("fnas").Where(c => c.Body.Length >= 12)
                .GroupBy(c => Bytes.U32(c.Body, 8)).ToDictionary(g => g.Key, g => g.First());
            var allComa = arc.Find("COMA").Where(c => c.Body.Length >= 16).ToList();
            HashSet<uint>? taken = null;   // model and texture hashes in this package, for fresh own-art names

            foreach (var (mod, item) in group)
            {
                string where = $"{mod.Id}: {item.Id}";
                var donor = donors.Records.First(x => x.Name.Equals(item.Donor, StringComparison.OrdinalIgnoreCase));
                var rec = donor.Clone();
                var oldFnas = fnasById.Keys.Where(rec.ContainsU32).ToList();
                if (oldFnas.Count != 1) { r.Errors.Add($"{where}: expected one fnas id in donor {donor.Name}, found {oldFnas.Count}"); continue; }
                uint old = oldFnas[0], artOld = old;   // artOld: whose look (fnas + COMA) the item gets
                if (!string.IsNullOrEmpty(item.ArtFrom))
                {
                    var look = donors.Records.FirstOrDefault(x => x.Name.Equals(item.ArtFrom, StringComparison.OrdinalIgnoreCase));
                    var lookFnas = look is null ? new List<uint>() : fnasById.Keys.Where(look.ContainsU32).ToList();
                    if (lookFnas.Count != 1) { r.Errors.Add($"{where}: look-alike '{item.ArtFrom}' not found in {pkg.Name} (it must be in the donor's package)"); continue; }
                    artOld = lookFnas[0];
                }
                var coma = allComa.Where(c => Bytes.U32(c.Body, 12) == artOld).ToList();
                if (coma.Count != 1) { r.Errors.Add($"{where}: expected one COMA for {(artOld == old ? donor.Name : item.ArtFrom)}, found {coma.Count}"); continue; }

                uint fnas = refs[FurnitureRef(item.Id)];
                uint nameHash = TextTable.KeyHash(TextKey(mod.Id, item.Id, "NAME"));
                rec.Name = item.Id;                     // re-pads the name field
                rec.Cost = item.Cost;
                // a text ref is [table hash][key hash]: new items' texts live in ModKit's own table (confirmed in-game)
                uint textTable = FurnitureRecord.ModTextTable;
                rec.SetTextHash(nameHash, 0, textTable);
                if (!string.IsNullOrEmpty(item.Description))
                {
                    if (rec.HasTextRef(1)) rec.SetTextHash(TextTable.KeyHash(TextKey(mod.Id, item.Id, "DESC")), 1, textTable);
                    else r.Report.Add($"warning: {where}: donor has no description slot; description ignored");
                }
                if (rec.ReplaceU32(old, fnas) != 1) { r.Errors.Add($"{where}: fnas id not unique in record"); continue; }
                // the record picks its icon by the hash of the icon's path: give the item its own icon (a copy of the
                // donor's, or the mod's) under its own name and point the key at it; no donor icon = share the donor's key
                var donorIcon = FurnitureArt.FindIcon(game, arc, rec.IconKey);
                var myIcon = item.Textures.FirstOrDefault(IsIcon);
                if (donorIcon is not null || myIcon is not null)
                {
                    artTextures.Add(FurnitureArt.TextureChunk(donorIcon?.Chunk, FurnitureArt.IconPath(item.Id),
                        myIcon is null ? null : File.ReadAllBytes(Path.Combine(ModPackage.AssetDir(mod), myIcon.Source))));
                    rec.SetIconKey(FurnitureArt.IconKey(item.Id.ToLowerInvariant()));
                    r.Report.Add($"{item.Id}: icon {FurnitureArt.IconPath(item.Id)} ({(myIcon is null ? "copy of " + Path.GetFileName(donorIcon!.Name) : "from " + myIcon.Source)})");
                }
                else r.Report.Add($"{item.Id}: shares {donor.Name}'s icon key (no icon found for it)");

                table.Add(rec);
                var newComa = Remap(coma[0], artOld, fnas, where, r);
                if (item.OwnArt)
                {
                    taken ??= arc.Chunks.Where(c => c.Tag == "HSKN" && c.Body.Length > 20)
                        .Select(c => TextTable.KeyHash(Bytes.Latin1.GetString(c.Body, 16, Array.IndexOf(c.Body, (byte)0, 16) - 16)))
                        .Concat(arc.Chunks.Where(c => c.Tag == "RSCF").Select(c => FurnitureArt.TextureHash(EmbeddedFile.Parse(c.Body).Path)))
                        .ToHashSet();
                    var art = FurnitureArt.Of(game, arc, newComa.Body, log, ct);
                    if (art.Models.Count == 0) { r.Errors.Add($"{where}: no model found for donor {donor.Name}; can't give it its own art"); continue; }
                    var clone = art.Clone(newComa.Body, key => ids.Get($"{mod.Id}/{item.Id}/art/{key}"),
                        tex => TextureData(game, mod, item, tex, log, ct), taken);
                    foreach (var t in item.Textures.Where(t => !art.Textures.ContainsKey(FurnitureArt.TextureHash(t.Texture)) && !IsIcon(t)))
                        r.Errors.Add($"{where}: {t.Texture} is not one of {donor.Name}'s textures");
                    newComa = new Chunk("COMA", clone.Coma);
                    artTextures.AddRange(clone.Textures);
                    artMaterials.AddRange(clone.MaterialRecords);
                    artModels.AddRange(clone.ModelChunks);
                    modelHashes.AddRange(clone.ModelHashes);
                    r.Report.AddRange(clone.Notes.Select(n => $"{item.Id}: {n}"));
                }
                comas.Add(newComa);
                fnasChunks.Add(Remap(fnasById[artOld], artOld, fnas, where, r));
                fnasIds.Add(fnas);
                records.Add((item.Id, item.Cost, nameHash));
                r.Report.Add($"new item {item.Id} (from {donor.Name}, cost {item.Cost}, fnas {Bytes.Hex(fnas)})");
            }
        }
        if (!r.Ok) return;
        uint pid = ids.Get("package");

        // new objects of any type: the source's bytes, new id / package / aux, own texts and values
        var newChunks = new List<Chunk>();
        var newIds = new List<uint>();
        foreach (var (mod, o) in objects)
        {
            var src = SourceOf(game, o)!;
            var body = (byte[])src.Body.Clone();
            uint id = refs[$"{mod.Id}/{o.Key}"];
            string pkg = o.Into ?? src.Package;
            bool inPlace = !ViaOverride(pkg);   // frontend loads before the dev slot: the copy goes into it
            Bytes.PutU32(body, 8, id);
            if (!inPlace) Bytes.PutU32(body, 16, pid);
            else if (!pkg.Equals(src.Package, StringComparison.OrdinalIgnoreCase))   // moved into another package: that package's id
                Bytes.PutU32(body, 16, Bytes.U32(game.Objects.First(x => !x.IsRecord && x.Package.Equals(pkg, StringComparison.OrdinalIgnoreCase)).Body, 16));
            Bytes.PutU32(body, 20, ids.Get($"{mod.Id}/obj/{o.Key}/aux"));
            foreach (var t in o.Texts)
            {
                if (OwnTableRef(game, src.Body, t.Offset)) Bytes.PutU32(body, t.Offset - 4, FurnitureRecord.ModTextTable);
                Bytes.PutU32(body, t.Offset, TextTable.KeyHash(TextKey(mod.Id, o.Key, $"T{t.Offset}")));
            }
            foreach (var f in o.Edits) f.Encode(refs).CopyTo(body, f.Offset);
            var swaps = mods.Where(m => m == mod).SelectMany(m => m.ScriptSwaps.Where(x => x.Object.Equals("@" + o.Key, StringComparison.OrdinalIgnoreCase)).Select(x => (m, x)));
            body = ApplySwaps(game, body, swaps, o.Edits.Select(f => f.Offset).Concat(o.Texts.Select(t => t.Offset)), refs, r);
            body = ApplyRequirements(game, o.Tag, body, mod.RequirementEdits.Where(x => x.Object.Equals("@" + o.Key, StringComparison.OrdinalIgnoreCase)).Select(x => (mod, x)), refs, r);
            body = ApplyTasks(game, body, mod.TaskEdits.Where(x => x.Object.Equals("@" + o.Key, StringComparison.OrdinalIgnoreCase)).Select(x => (mod, x)), r);
            body = ApplySchemes(game, o.Tag, body, mod.SchemeEdits.Where(x => x.Object.Equals("@" + o.Key, StringComparison.OrdinalIgnoreCase)).Select(x => (mod, x)), r);
            body = ApplyGraphEdits(body, mod.GraphEdits.Where(x => x.Object.Equals("@" + o.Key, StringComparison.OrdinalIgnoreCase)).Select(x => (mod, x)), r);
            if (inPlace)
            {
                if (!r.PatchObjects.TryGetValue(pkg, out var list)) r.PatchObjects[pkg] = list = new();
                list.Add(new Chunk(o.Tag, body));
                if (!pkg.Equals(src.Package, StringComparison.OrdinalIgnoreCase))
                {   // moved into the frontend for the island select: the game proper looks it up after the frontend, so a
                    // same-id copy also goes in ModKit's own package (loaded with the game, like the source's package)
                    var copy = (byte[])body.Clone();
                    Bytes.PutU32(copy, 16, pid);
                    newChunks.Add(new Chunk(o.Tag, copy));
                    newIds.Add(id);
                }
                r.Report.Add($"new {o.Tag} {o.Key} = {Bytes.Hex(id)} (copy of {src.Name}, in the {pkg} package; {o.Texts.Count} texts, {o.Edits.Count} values)");
                continue;
            }
            newChunks.Add(new Chunk(o.Tag, body));
            newIds.Add(id);
            r.Report.Add($"new {o.Tag} {o.Key} = {Bytes.Hex(id)} (copy of {src.Name}; {o.Texts.Count} texts, {o.Edits.Count} values)");
        }

        var content = new AsuraArchive { Compressed = true };
        content.Chunks.Add(new Chunk("FNFO", new FileInfoChunk { Version = 1, Flags = 0, PayloadSize = 0, Alignment = 8 }.ToBytes()));
        content.Chunks.Add(new Chunk("RSFL", new ResourceList { Version = 1, Pad = 0 }.ToBytes()));
        content.Chunks.AddRange(comas.OrderBy(c => Bytes.U32(c.Body, 12)));   // COMA must stay sorted by key
        foreach (var m in mods)
            foreach (var a in m.NewAssets)
            {
                var path = Path.Combine(ModPackage.AssetDir(m), a.Source);
                if (m.FilePath is null || !File.Exists(path)) { r.Errors.Add($"{m.Id}: new asset {a.Name}: file {a.Source} is missing"); continue; }
                var data = File.ReadAllBytes(path);
                if (!data.AsSpan().StartsWith("DDS "u8)) { r.Errors.Add($"{m.Id}: new asset {a.Name}: {a.Source} is not a DDS file"); continue; }
                if (a.Package is { } pkg)
                {
                    if (ViaOverride(pkg)) { r.Errors.Add($"{m.Id}: new asset {a.Name}: package can only be {string.Join(", ", PatchOnly)}"); continue; }
                    if (!r.PatchTextures.TryGetValue(pkg, out var list)) r.PatchTextures[pkg] = list = new();
                    list.Add(FurnitureArt.TextureChunk(null, a.Name, data));
                    r.Report.Add($"new texture {a.Name} ({m.Id}, in the {pkg} package)");
                    continue;
                }
                artTextures.Add(FurnitureArt.TextureChunk(null, a.Name, data));
                r.Report.Add($"new texture {a.Name} ({m.Id})");
            }
        foreach (var m in mods)
            foreach (var t in m.EngineeringTrees.Where(t => t.Background is { Length: > 0 }))
            {
                var path = Path.Combine(ModPackage.AssetDir(m), t.Background!);
                if (m.FilePath is null || !File.Exists(path)) continue;   // reported by BuildOverrides
                var data = File.ReadAllBytes(path);
                if (!data.AsSpan().StartsWith("DDS "u8)) { r.Errors.Add($"{m.Id}: engineering tree {t.Tree}: background {t.Background} is not a DDS file"); continue; }
                artTextures.Add(FurnitureArt.TextureChunk(null, EngineeringBackgroundPath(t.Tree), data));
            }
        if (!r.Ok) return;
        content.Chunks.AddRange(artTextures);                                  // base packages: textures, MARE, then models
        if (artMaterials.Count > 0) content.Chunks.Add(new Chunk("MARE", Materials.Build(artMaterials)));
        content.Chunks.AddRange(artModels);
        if (records.Count > 0) content.Chunks.Add(new Chunk("fntr", table.ToBytes()));
        content.Chunks.AddRange(fnasChunks);
        content.Chunks.AddRange(newChunks);

        // manifest: furniture.asr as the template, nothing but our fnas ids
        var tpl = install.Package("furniture") ?? throw new KeyNotFoundException("furniture package not found");
        var man = AsuraArchive.Load(tpl.ManifestPath);
        var rp = man.First("rpkg")!;
        var mp = Package.Parse(rp.Tag, rp.Body);
        foreach (var path in mp.IdArrays().Keys.ToList()) mp.SetArray(path, Array.Empty<uint>());
        mp.SetArray(Package.FnasPath, fnasIds);
        mp.SetArray(Package.ObjectsPath, newIds);
        if (modelHashes.Count > 0) mp.SetArray(Package.ModelsPath, modelHashes);
        mp.Entitlement = 0;
        mp.Deferred = true;                 // load at game start, after the base packages it references
        mp.Obj.Header.ObjectId = pid;
        mp.Obj.Header.PackageId = pid;
        mp.Obj.Header.AuxId = ids.Get("aux");
        rp.Body = mp.ToBytes();
        var rsfl = man.First("RSFL")!;
        var rl = ResourceList.Parse(rsfl.Body);
        rl.Entries[0].Size = (uint)rp.Size;
        rsfl.Body = rl.ToBytes();

        string dir = GameInstall.DevSlotDir;
        log("compressing dev-slot package");
        r.Files[$@"{dir}\{Slot}.asr"] = man.ToBytes();
        r.Files[$@"{dir}\{Slot}_Content.asr"] = content.ToBytes();
        r.Files[$@"{dir}\{Slot}_Content.ts"] = new AsuraArchive { Compressed = false }.ToBytes();

        // self-check: re-parse what we wrote
        var m2 = Package.Parse("rpkg", AsuraArchive.FromBytes(r.Files[$@"{dir}\{Slot}.asr"]).First("rpkg")!.Body);
        var arrays = m2.IdArrays();
        List<uint> Arr(string path) => arrays.TryGetValue(path, out var a) ? a : new();   // empty arrays aren't listed
        Check(r, Arr(Package.ObjectsPath).SequenceEqual(newIds) && Arr(Package.FnasPath).SequenceEqual(fnasIds)
                 && m2.Entitlement == 0 && m2.Deferred, "manifest");
        var c2 = AsuraArchive.FromBytes(r.Files[$@"{dir}\{Slot}_Content.asr"]);
        if (records.Count > 0)
            Check(r, FurnitureTable.Parse(c2.First("fntr")!.Body).Records.Select(x => (x.Name, x.Cost, x.TextHash(0))).SequenceEqual(records), "furniture records");
        Check(r, newIds.All(id => c2.Chunks.Count(c => c.IsDataObjectTag && c.Tag != "fntr" && c.Body.Length >= 24 && Bytes.U32(c.Body, 8) == id) == 1), "new objects");
        Check(r, c2.Find("fnas").Select(c => Bytes.U32(c.Body, 8)).SequenceEqual(fnasIds), "fnas chunks");
        Check(r, Arr(Package.ModelsPath).SequenceEqual(modelHashes), "model list");
        if (artMaterials.Count > 0) Check(r, Materials.Parse(c2.First("MARE")!.Body).Records.Count == artMaterials.Count, "materials");
        var keys = c2.Find("COMA").Select(c => Bytes.U32(c.Body, 12)).ToList();
        Check(r, keys.SequenceEqual(keys.Order()) && keys.Order().SequenceEqual(fnasIds.Order()), "COMA order");
    }

    /// <summary>
    /// Packages whose edits still go through a base .asrpatch. Everything else is overridden from the dev slot by
    /// same-id copies (confirmed in-game for a furniture price and a research value, 2026-09-24).
    /// ponytail: frontend loads at boot, before the dev slot; move it over once an override there is tested.
    /// </summary>
    static readonly HashSet<string> PatchOnly = new(StringComparer.OrdinalIgnoreCase) { "frontend" };
    static bool ViaOverride(string package) => !PatchOnly.Contains(package);

    /// <summary>
    /// Price and value edits as same-id copies in the dev slot: one copy per edited object or furniture record with
    /// every mod's edits applied (base bytes checked against Expect), so no base package is patched.
    /// </summary>
    static void BuildOverrides(GameData game, IReadOnlyList<ModDefinition> mods, IReadOnlyDictionary<string, uint> refs, IdAllocator ids,
                               BuildResult r, Action<string> log, CancellationToken ct)
    {
        var costs = mods.SelectMany(m => m.FurnitureEdits).DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Where(e => ViaOverride(game.FindFurniture(e.Name)!.Package)).ToList();
        var fields = mods.SelectMany(m => m.FieldEdits)
            .DistinctBy(f => $"{f.Package}/{f.Tag}/{f.Object}/{f.Offset}".ToLowerInvariant()).Where(f => ViaOverride(f.Package)).ToList();
        var trees = mods.SelectMany(m => m.ResearchTrees.Select(t => (Mod: m, Tree: t))).ToList();
        var swaps = mods.SelectMany(m => m.ScriptSwaps.Where(x => !x.Object.StartsWith('@')).Select(x => (Mod: m, Swap: x))).ToList();
        var engTrees = mods.SelectMany(m => m.EngineeringTrees.Select(t => (Mod: m, Tree: t))).ToList();
        var reqs = mods.SelectMany(m => m.RequirementEdits.Where(x => !x.Object.StartsWith('@')).Select(x => (Mod: m, Edit: x))).ToList();
        var tasks = mods.SelectMany(m => m.TaskEdits.Where(x => !x.Object.StartsWith('@')).Select(x => (Mod: m, Edit: x))).ToList();
        var schemes = mods.SelectMany(m => m.SchemeEdits.Where(x => !x.Object.StartsWith('@')).Select(x => (Mod: m, Edit: x))).ToList();
        var pools = mods.SelectMany(m => m.PoolEdits.Select(x => (Mod: m, Edit: x))).ToList();
        var shapes = mods.SelectMany(m => m.ShapeEdits.Select(x => (Mod: m, Edit: x))).Where(x => game.FindFurniture(x.Edit.Name) is { } f && ViaOverride(f.Package)).ToList();
        var jobs = mods.SelectMany(m => m.JobEdits.Select(x => (Mod: m, Edit: x))).ToList();
        var lists = mods.SelectMany(m => m.ListEdits.Select(x => (Mod: m, Edit: x))).Where(x => ViaOverride(x.Edit.Package)).ToList();
        var graphEdits = mods.SelectMany(m => m.GraphEdits.Where(x => !x.Object.StartsWith('@')).Select(x => (Mod: m, Edit: x))).ToList();
        foreach (var x in mods.SelectMany(m => m.ShapeEdits.Select(e => (m, e))).Where(x => game.FindFurniture(x.e.Name) is null))
            r.Errors.Add($"{x.m.Id}: size/slots: no furniture called '{x.e.Name}'");
        if (costs.Count == 0 && fields.Count == 0 && trees.Count == 0 && swaps.Count == 0 && engTrees.Count == 0 && reqs.Count == 0 && tasks.Count == 0 && schemes.Count == 0 && pools.Count == 0
            && shapes.Count == 0 && jobs.Count == 0 && lists.Count == 0 && graphEdits.Count == 0) return;

        // furniture records: copies of the base records (from their packages), cost + record field edits applied
        var recordNames = costs.Select(e => e.Name).Concat(fields.Where(f => f.Tag == "fntr").Select(f => f.Object)).Concat(shapes.Select(x => x.Edit.Name)).Concat(lists.Where(x => x.Edit.Tag == "fntr").Select(x => x.Edit.Object))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var records = new Dictionary<string, FurnitureRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkgName in recordNames.Select(n => game.FindFurniture(n)?.Package).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            log($"reading {pkgName} furniture for overrides");
            var table = FurnitureTable.Parse(AsuraArchive.Load(game.Install.Package(pkgName)!.ContentPath).First("fntr")!.Body);
            foreach (var n in recordNames.Where(n => game.FindFurniture(n)!.Package.Equals(pkgName, StringComparison.OrdinalIgnoreCase)))
                records[n] = table.Records.First(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)).Clone();
        }
        foreach (var n in recordNames.Where(n => !records.ContainsKey(n))) r.Errors.Add($"override: furniture '{n}' not found");
        foreach (var e in costs.Where(e => records.ContainsKey(e.Name)))
        {
            var rec = records[e.Name];
            r.Report.Add($"cost {rec.Name}: {rec.Cost} -> {e.Cost} (override in mod package)");
            rec.Cost = e.Cost;
        }

        // footprint / slot count: structural, after the in-place edits' offsets were checked (field edits run first below)
        var shapeLater = shapes.Where(x => records.ContainsKey(x.Edit.Name)).ToList();

        // data objects: copies of the base bodies with their edits
        var objects = new Dictionary<string, (GameObject Base, byte[] Body)>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields)
        {
            var data = f.Encode(refs);
            var expect = Convert.FromHexString(f.Expect);
            string where = $"field {f.Package}/{f.Tag}/{f.Object}+{f.Offset}";
            if (f.Tag == "fntr")
            {
                if (!records.TryGetValue(f.Object, out var rec)) continue;
                var pl = rec.Payload;
                if (f.Offset < 0 || f.Offset + data.Length > pl.Length || !pl.AsSpan(f.Offset, data.Length).SequenceEqual(expect))
                { r.Errors.Add($"{where}: base bytes differ from expected {f.Expect} (game updated?)"); continue; }
                rec.SetBytes(f.Offset, data);
            }
            else
            {
                string key = $"{f.Package}/{f.Tag}/{f.Object}";
                if (!objects.TryGetValue(key, out var o))
                {
                    var src = game.Objects.FirstOrDefault(x => !x.IsRecord && x.Package.Equals(f.Package, StringComparison.OrdinalIgnoreCase)
                                                               && x.Tag == f.Tag && x.Key.Equals(f.Object, StringComparison.OrdinalIgnoreCase));
                    if (src is null) { r.Errors.Add($"{where}: object not found"); continue; }
                    objects[key] = o = (src, (byte[])src.Body.Clone());
                }
                if (f.Offset < ObjectHeader.Size || f.Offset + data.Length > o.Body.Length || !o.Body.AsSpan(f.Offset, data.Length).SequenceEqual(expect))
                { r.Errors.Add($"{where}: base bytes differ from expected {f.Expect} (or offset is in the header)"); continue; }
                Buffer.BlockCopy(data, 0, o.Body, f.Offset, data.Length);
            }
            r.Report.Add($"{where}: {f.Expect} -> {Convert.ToHexString(data)} ({f.Type} {f.Value}){(f.Note is { Length: > 0 } n ? "  " + n : "")} (override)");
        }

        // generic list edits: offsets refer to the game's layout, so before any other structural edit
        foreach (var grp in lists.GroupBy(x => $"{x.Edit.Package}/{x.Edit.Tag}/{x.Edit.Object}".ToLowerInvariant()))
        {
            var (m0, e0) = grp.First();
            string where = $"{m0.Id}: lists of {e0.Tag} {e0.Object}";
            var edits = grp.Select(x => (x.Edit.At, x.Edit.Count, (IReadOnlyList<int>)x.Edit.Order)).ToList();
            try
            {
                if (e0.Tag == "fntr")
                {
                    if (!records.TryGetValue(e0.Object, out var rec)) continue;
                    rec.SetPayload(ObjectLists.Apply(rec.Payload, 0, edits));
                }
                else
                {
                    string key = $"{e0.Package}/{e0.Tag}/{e0.Object}";
                    if (!objects.TryGetValue(key, out var ob))
                    {
                        var src = game.Objects.FirstOrDefault(x => !x.IsRecord && x.Package.Equals(e0.Package, StringComparison.OrdinalIgnoreCase)
                                                                   && x.Tag == e0.Tag && x.Key.Equals(e0.Object, StringComparison.OrdinalIgnoreCase));
                        if (src is null) { r.Errors.Add($"{where}: object not found"); continue; }
                        ob = (src, (byte[])src.Body.Clone());
                    }
                    objects[key] = (ob.Base, ObjectLists.Apply(ob.Body, ObjectHeader.Size, edits));
                }
                r.Report.Add($"{where}: {edits.Count} list(s) rebuilt (override)");
            }
            catch (InvalidOperationException ex) { r.Errors.Add($"{where}: {ex.Message}"); }
        }

        foreach (var (mod, e) in shapeLater)
        {
            var rec = records[e.Name];
            string where = $"{mod.Id}: {e.Name}";
            try
            {
                var p = rec.Payload;
                var fp = FurnitureShape.Footprint(p);
                if ((e.Width ?? e.Height) is not null)
                {
                    if (fp is null) throw new InvalidOperationException("this item has no footprint");
                    p = FurnitureShape.SetFootprint(p, e.Width ?? fp.Value.W, e.Height ?? fp.Value.H);
                }
                if (e.Slots is { } n) p = FurnitureShape.SetSlotCount(p, n);
                rec.SetPayload(p);
                r.Report.Add($"{where}: footprint {FurnitureShape.Footprint(p)}, slots {FurnitureShape.SlotCount(p)} (override)");
            }
            catch (InvalidOperationException ex) { r.Errors.Add($"{where}: {ex.Message}"); }
        }
        foreach (var grp in jobs.GroupBy(x => x.Edit.Object.ToLowerInvariant()))
        {
            var src = game.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == "rjob" && Bytes.Hex(x.ObjectId).Equals(grp.Key, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{grp.First().Mod.Id}: job {grp.Key} not found"); continue; }
            if (!ViaOverride(src.Package)) continue;
            string key = $"{src.Package}/{src.Tag}/{Bytes.Hex(src.ObjectId)}";
            var body = objects.TryGetValue(key, out var have) ? have.Body : (byte[])src.Body.Clone();
            foreach (var (mod, e) in grp)
            {
                var keys = e.Types.Select(t => ResolveId(t, refs)).ToList();
                if (keys.Any(k => k is null)) { r.Errors.Add($"{mod.Id}: job {e.Object}: types must be 0x... hashes"); continue; }
                try { body = JobTypes.Write(body, keys.Select(k => k!.Value).ToList()); r.Report.Add($"{mod.Id}: job {src.Name}: {keys.Count} character types (override)"); }
                catch (InvalidOperationException ex) { r.Errors.Add($"{mod.Id}: job {e.Object}: {ex.Message}"); }
            }
            objects[key] = (src, body);
        }

        // script swaps on game objects: after their value edits (an edit inside a replaced script is an error)
        foreach (var grp in swaps.GroupBy(x => x.Swap.Object.ToLowerInvariant()))
        {
            var src = game.Objects.FirstOrDefault(x => !x.IsRecord && Bytes.Hex(x.ObjectId).Equals(grp.Key, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{grp.First().Mod.Id}: script swap: object {grp.Key} not found"); continue; }
            string key = $"{src.Package}/{src.Tag}/{Bytes.Hex(src.ObjectId)}";
            var body = objects.TryGetValue(key, out var have) ? have.Body : (byte[])src.Body.Clone();
            var edited = fields.Where(f => f.Tag == src.Tag && f.Object.Equals(Bytes.Hex(src.ObjectId), StringComparison.OrdinalIgnoreCase)).Select(f => f.Offset);
            objects[key] = (src, ApplySwaps(game, body, grp.Select(x => (x.Mod, x.Swap)), edited, refs, r));
        }

        // requirement edits on game research / engineering items: after value edits and script swaps
        foreach (var grp in reqs.GroupBy(x => x.Edit.Object.ToLowerInvariant()))
        {
            var src = game.Objects.FirstOrDefault(x => !x.IsRecord && Requirements.Supports(x.Tag) && Bytes.Hex(x.ObjectId).Equals(grp.Key, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{grp.First().Mod.Id}: requirements: {grp.Key} is not a research or engineering item"); continue; }
            string key = $"{src.Package}/{src.Tag}/{Bytes.Hex(src.ObjectId)}";
            var body = objects.TryGetValue(key, out var have) ? have.Body : (byte[])src.Body.Clone();
            objects[key] = (src, ApplyRequirements(game, src.Tag, body, grp.Select(x => (x.Mod, x.Edit)), refs, r));
        }

        // objective task lists; a copied task comes from its objective as field edits left it (e.g. a lowered count)
        var beforeTasks = new Dictionary<string, (GameObject Base, byte[] Body)>(objects, StringComparer.OrdinalIgnoreCase);
        byte[] Edited(GameObject o) => beforeTasks.TryGetValue($"{o.Package}/{o.Tag}/{Bytes.Hex(o.ObjectId)}", out var c) ? c.Body : o.Body;
        foreach (var grp in tasks.GroupBy(x => x.Edit.Object.ToLowerInvariant()))
        {
            var src = game.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == "robj" && Bytes.Hex(x.ObjectId).Equals(grp.Key, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{grp.First().Mod.Id}: tasks: {grp.Key} is not an objective"); continue; }
            string key = $"{src.Package}/{src.Tag}/{Bytes.Hex(src.ObjectId)}";
            var body = objects.TryGetValue(key, out var have) ? have.Body : (byte[])src.Body.Clone();
            objects[key] = (src, ApplyTasks(game, body, grp.Select(x => (x.Mod, x.Edit)), r, Edited));
        }

        foreach (var grp in schemes.GroupBy(x => x.Edit.Object.ToLowerInvariant()))
        {
            var src = game.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == "rscm" && Bytes.Hex(x.ObjectId).Equals(grp.Key, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{grp.First().Mod.Id}: scheme: {grp.Key} is not a scheme"); continue; }
            string key = $"{src.Package}/{src.Tag}/{Bytes.Hex(src.ObjectId)}";
            var body = objects.TryGetValue(key, out var have) ? have.Body : (byte[])src.Body.Clone();
            objects[key] = (src, ApplySchemes(game, src.Tag, body, grp.Select(x => (x.Mod, x.Edit)), r));
        }

        // scheme pools: entries added/removed (after value edits, which use the game's offsets)
        foreach (var grp in pools.GroupBy(x => x.Edit.Pool.Trim().ToLowerInvariant()))
        {
            string where = $"{grp.First().Mod.Id}: scheme pool {grp.Key}";
            var src = game.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == "rspl" && Bytes.Hex(x.ObjectId).Equals(grp.Key, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{where}: not a scheme pool"); continue; }
            string key = $"{src.Package}/{src.Tag}/{Bytes.Hex(src.ObjectId)}";
            var body = objects.TryGetValue(key, out var have) ? have.Body : (byte[])src.Body.Clone();
            if (SchemePool.TryParse(src.Tag, body) is not { } pool) { r.Errors.Add($"{where}: this pool's layout isn't the known one"); continue; }
            bool ok = true;
            foreach (var (mod, e) in grp)
            {
                foreach (var s in e.Remove)
                {
                    if (ResolveId(s, refs) is not { } id || pool.Entries.RemoveAll(x => x.Scheme == id) == 0) { r.Errors.Add($"{mod.Id}: scheme pool {grp.Key}: {s} isn't in it"); ok = false; }
                }
                foreach (var s in e.Add)
                {
                    if (ResolveId(s, refs) is not { } id) { r.Errors.Add($"{mod.Id}: scheme pool {grp.Key}: can't resolve '{s}'"); ok = false; continue; }
                    // a game scheme must exist; a new one (@Key) is this build's own
                    if (!s.Trim().StartsWith('@') && !(game.ObjectsById.TryGetValue(id, out var l) && l[0].Tag == "rscm")) { r.Errors.Add($"{mod.Id}: scheme pool {grp.Key}: {s} is not a scheme"); ok = false; continue; }
                    if (pool.Entries.Any(x => x.Scheme == id)) continue;
                    pool.Entries.Add((id, 1));
                }
            }
            if (pool.Entries.Count == 0) { r.Errors.Add($"{where}: would be empty (the game may expect at least one scheme)"); ok = false; }
            if (!ok) continue;
            objects[key] = (src, pool.ToBytes());
            r.Report.Add($"{where}: {pool.Entries.Count} schemes (override)");
        }

        // structural script edits: last, since they move every later byte of the object
        foreach (var grp in graphEdits.GroupBy(x => x.Edit.Object.ToLowerInvariant()))
        {
            var src = game.Objects.FirstOrDefault(x => !x.IsRecord && Bytes.Hex(x.ObjectId).Equals(grp.Key, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{grp.First().Mod.Id}: script edit: object {grp.Key} not found"); continue; }
            string key = $"{src.Package}/{src.Tag}/{Bytes.Hex(src.ObjectId)}";
            var body = objects.TryGetValue(key, out var have) ? have.Body : (byte[])src.Body.Clone();
            objects[key] = (src, ApplyGraphEdits(body, grp.Select(x => (x.Mod, x.Edit)), r));
        }

        // research tree layouts: the whole node/link set rebuilt (can't be mixed with value edits on the same tree)
        foreach (var (mod, t) in trees)
        {
            string where = $"{mod.Id}: research tree {t.Tree}";
            var src = game.Objects.FirstOrDefault(x => x.Tag == "rttr" && Bytes.Hex(x.ObjectId).Equals(t.Tree, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{where}: not found"); continue; }
            string key = $"{src.Package}/rttr/{Bytes.Hex(src.ObjectId)}";
            if (objects.ContainsKey(key)) { r.Errors.Add($"{where}: also changed by another mod or a value edit; lay it out in one place"); continue; }
            var tree = new ResearchTree();
            foreach (var n in t.Nodes)
            {
                uint? id = ResolveId(n.Research, refs);
                if (id is not { } v || !(refs.Values.Contains(v) || game.ObjectsById.TryGetValue(v, out var l) && l.Any(x => x.Tag == "rtrp")))
                { r.Errors.Add($"{where}: '{n.Research}' is not a research item"); continue; }
                tree.Nodes.Add(new ResearchTree.Node { Research = v, Column = n.Column, Row = n.Row });
            }
            foreach (var l in t.Links) tree.Links.Add((l.From, l.To));
            if (!r.Ok) continue;
            var problems = tree.Check(i => game.ObjectsById.TryGetValue(tree.Nodes[i].Research, out var l) ? l[0].Name.Split("  (")[0] : t.Nodes[i].Research);
            if (problems.Count > 0) { r.Errors.AddRange(problems.Select(x => $"{where}: {x}")); continue; }
            try { objects[key] = (src, tree.Write(src.Body)); }
            catch (InvalidOperationException e) { r.Errors.Add($"{where}: {e.Message}"); continue; }
            r.Report.Add($"research tree {src.Name}: {tree.Nodes.Count} nodes, {tree.Links.Count} links ({mod.Id}, override)");
        }
        // engineering tree layouts: items + one prerequisite each; lines routed by EngineeringTree.SetItems
        foreach (var (mod, t) in engTrees)
        {
            string where = $"{mod.Id}: engineering tree {t.Tree}";
            var src = game.Objects.FirstOrDefault(x => x.Tag == "rctt" && Bytes.Hex(x.ObjectId).Equals(t.Tree, StringComparison.OrdinalIgnoreCase));
            if (src is null) { r.Errors.Add($"{where}: not found"); continue; }
            string key = $"{src.Package}/rctt/{Bytes.Hex(src.ObjectId)}";
            if (objects.ContainsKey(key)) { r.Errors.Add($"{where}: also changed by another mod or a value edit; lay it out in one place"); continue; }
            var tree = EngineeringTree.Parse(src.Body);
            var items = new List<EngineeringTree.Item>();
            for (int i = 0; i < t.Nodes.Count; i++)
            {
                var n = t.Nodes[i];
                uint? id = ResolveId(n.Research, refs);
                if (id is not { } v || !(refs.Values.Contains(v) || game.ObjectsById.TryGetValue(v, out var l) && l.Any(x => x.Tag == "rctr")))
                { r.Errors.Add($"{where}: '{n.Research}' is not an engineering item"); continue; }
                var before = t.Links.Where(k => k.To == i).ToList();
                if (before.Count > 1) r.Errors.Add($"{where}: '{n.Research}' has {before.Count} prerequisites; an engineering item can have one");
                items.Add(new EngineeringTree.Item(v, n.Column, n.Row, before.Count == 1 ? before[0].From : -1, n.Dx, n.Dy));
            }
            if (!r.Ok) continue;
            tree.SetItems(items);
            string Name(int i) => i >= 0 && i < t.Nodes.Count && game.ObjectsById.TryGetValue(tree.Nodes[i].Item, out var l) ? l[0].Name.Trim('"') : i < t.Nodes.Count && i >= 0 ? t.Nodes[i].Research : "?";
            var problems = tree.Check(Name);
            if (problems.Count > 0) { r.Errors.AddRange(problems.Select(x => $"{where}: {x}")); continue; }
            var body = tree.Write(src.Body);
            if (t.Background is { Length: > 0 } bg)
            {
                var file = Path.Combine(ModPackage.AssetDir(mod), bg);
                if (mod.FilePath is null || !File.Exists(file)) { r.Errors.Add($"{where}: background {bg} is missing from the mod's assets folder"); continue; }
                Bytes.PutU32(body, EngineeringTree.BackgroundKeyAt, FurnitureArt.GuiKey(EngineeringBackgroundPath(t.Tree)));
            }
            objects[key] = (src, body);
            r.Report.Add($"engineering tree {src.Name}: {items.Count} items, {tree.Nodes.Count - items.Count} line bends{(t.Background is { Length: > 0 } ? ", own background" : "")} ({mod.Id}, override)");
        }
        if (!r.Ok) return;
        AppendToDevSlot(game, refs, ids, r, log, ct, objects.Values.Select(o => new Chunk(o.Base.Tag, o.Body)).ToList(), records.Values.ToList(), null);
        r.Report.Add($"overrides in the mod package: {objects.Count} objects, {records.Count} furniture records (no base package patched)");
    }

    /// <summary>
    /// The text at <paramref name="offset"/> sits in a [1][0][table hash][key] reference (15,908 of 16,020 in the game do),
    /// so a copy can point it at ModKit's own table instead of patching the base one.
    /// </summary>
    static bool OwnTableRef(GameData game, byte[] body, int offset) =>
        offset >= 12 && offset + 4 <= body.Length && Bytes.U32(body, offset - 12) == 1 && Bytes.U32(body, offset - 8) == 0
        && game.Text.TryGetValue(Bytes.U32(body, offset), out var t) && Bytes.U32(body, offset - 4) == TextTable.KeyHash(t.Table);

    /// <summary>Game path of a mod's background picture for an engineering tree (found by name, like furniture icons).</summary>
    public static string EngineeringBackgroundPath(string tree) => $@"data\graphics\gui\techtree\crafting\modkit_bg_{tree.ToLowerInvariant()}.tga";

    static Dictionary<uint, uint>? _schemeCategories;

    static byte[] ApplySchemes(GameData game, string tag, byte[] body, IEnumerable<(ModDefinition Mod, SchemeEdit Edit)> edits, BuildResult r)
    {
        foreach (var (mod, e) in edits)
        {
            string where = $"{mod.Id}: scheme {e.Object}";
            if (Scheme.TryParse(tag, body) is not { } s) { r.Errors.Add($"{where}: this scheme's layout can't be edited safely"); continue; }
            if (e.Heat is { } h) s.Heat = h;
            if (e.Expiry is { } x) s.Expiry = x;
            if (e.Duration is { } du) s.Duration = du;
            if (e.Costs is { } cs)
            {
                s.Costs.Clear();
                s.Costs.AddRange(cs.Select(c => ResolveId(c.Ref, new Dictionary<string, uint>()) is { } id && game.ObjectsById.TryGetValue(id, out var l) && l[0].Tag == "rcns"
                    ? (id, c.Count) : (Fail($"unknown resource '{c.Ref}'"), c.Count)));
            }
            bool ok = true;
            if (e.Minions is { } ms)
            {
                s.Minions.Clear();
                foreach (var g in ms)
                    s.Minions.Add(g.Select(m => ResolveId(m.Ref, new Dictionary<string, uint>()) is { } id ? (id, m.Count)
                        : ((uint, uint))(Fail($"unknown minion type '{m.Ref}'"), m.Count)).ToList());
            }
            uint Fail(string msg) { r.Errors.Add($"{where}: {msg}"); ok = false; return 0; }
            if (!ok) continue;
            if (s.Problems() is { Count: > 0 } bad) { r.Errors.Add($"{where}: {string.Join("; ", bad)}"); continue; }
            try { body = s.Write(body, _schemeCategories ??= Scheme.Categories(game)); }
            catch (InvalidOperationException ex) { r.Errors.Add($"{where}: {ex.Message}"); continue; }
            r.Report.Add($"{where}: duration {s.Duration}, heat {s.Heat}, expiry {s.Expiry}, {s.Minions.Count} minion groups, {s.Costs.Count} costs ({mod.Id})");
        }
        return body;
    }

    /// <summary>Structural script edits (<see cref="GraphEdit"/>), in the mod's order.</summary>
    static byte[] ApplyGraphEdits(byte[] body, IEnumerable<(ModDefinition Mod, GraphEdit Edit)> edits, BuildResult r)
    {
        foreach (var (mod, e) in edits)
        {
            string where = $"{mod.Id}: script {e.Object}{(e.Graph is { Length: > 0 } g ? $" \"{g}\"" : "")} {e.Op}";
            string? graph = string.IsNullOrEmpty(e.Graph) ? null : e.Graph;
            uint Need(uint? v, string what) => v ?? throw new InvalidOperationException($"{what} is missing");
            try
            {
                body = e.Op switch
                {
                    "remove-node" => FlowGraphEdit.RemoveNode(body, graph, Need(e.Node, "Node")),
                    "copy-node" => FlowGraphEdit.CopyNode(body, graph, Need(e.Node, "Node")).Body,
                    "add-link" => FlowGraphEdit.AddLink(body, graph, Need(e.From, "From"), Need(e.FromPin, "FromPin"), Need(e.To, "To"), Need(e.ToPin, "ToPin")),
                    "remove-link" => FlowGraphEdit.RemoveLink(body, graph, Need(e.Link, "Link")),
                    _ => throw new InvalidOperationException("unknown operation (remove-node, copy-node, add-link, remove-link)"),
                };
                r.Report.Add(where);
            }
            catch (InvalidOperationException ex) { r.Errors.Add($"{where}: {ex.Message}"); }
        }
        return body;
    }

    /// <summary>Replaces an objective's task list; tasks are copied whole from the objectives they name.</summary>
    static byte[] ApplyTasks(GameData game, byte[] body, IEnumerable<(ModDefinition Mod, TaskEdit Edit)> edits, BuildResult r, Func<GameObject, byte[]>? current = null)
    {
        foreach (var (mod, e) in edits)
        {
            string where = $"{mod.Id}: tasks of {e.Object}";
            if (ObjectiveTasks.Parse(body) is null) { r.Errors.Add($"{where}: its task list can't be read safely"); continue; }
            if (e.Tasks.Count == 0) { r.Errors.Add($"{where}: an objective needs at least one task"); continue; }
            var picked = new List<byte[]>();
            foreach (var t in e.Tasks)
            {
                var parts = t.Split(':');
                var from = parts.Length == 2 ? game.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == "robj" && Bytes.Hex(x.ObjectId).Equals(parts[0], StringComparison.OrdinalIgnoreCase)) : null;
                var list = from is null ? null : ObjectiveTasks.Parse(current?.Invoke(from) ?? from.Body);
                if (list is null || !int.TryParse(parts[1], out int i) || i < 0 || i >= list.Count) { r.Errors.Add($"{where}: unknown task '{t}' (expected 0x<objective>:<index>)"); picked.Clear(); break; }
                picked.Add(list[i].Bytes);
            }
            if (picked.Count == 0) continue;
            body = ObjectiveTasks.Write(body, picked);
            r.Report.Add($"{where}: {picked.Count} tasks ({mod.Id})");
        }
        return body;
    }

    public static string FurnitureRef(string itemId) => "furniture:" + itemId;

    /// <summary>Replaces an object's requirement lists that the edit sets (null lists are left alone).</summary>
    static byte[] ApplyRequirements(GameData game, string tag, byte[] body, IEnumerable<(ModDefinition Mod, RequirementEdit Edit)> edits,
                                    IReadOnlyDictionary<string, uint> refs, BuildResult r)
    {
        Dictionary<string, uint>? fnasByName = null;
        foreach (var (mod, e) in edits)
        {
            string where = $"{mod.Id}: requirements of {e.Object}";
            var req = Requirements.TryParse(tag, body);
            if (req is null) { r.Errors.Add($"{where}: this one's requirement lists can't be read safely yet"); continue; }
            bool ok = true;
            uint Fail(string what, string name) { r.Errors.Add($"{where}: unknown {what} '{name}'"); ok = false; return 0; }
            uint? Furniture(string name)
            {
                if (refs.TryGetValue(FurnitureRef(name), out var id)) return id;
                fnasByName ??= Requirements.FurnitureFnas(game);
                return fnasByName.TryGetValue(name, out var f) ? f : null;
            }
            uint? Hex(string s) => ResolveId(s, refs);
            List<(uint, uint)> Resolve(List<CountedRef> list, Func<string, uint?> resolve, string what) =>
                list.Select(x => (resolve(x.Ref) is { } id ? id : Fail(what, x.Ref), x.Count)).ToList();
            if (e.Minions is { } ms) { req.Minions.Clear(); req.Minions.AddRange(Resolve(ms, Hex, "minion type")); }
            if (e.Furniture is { } fs) { req.Furniture.Clear(); req.Furniture.AddRange(Resolve(fs, Furniture, "furniture")); }
            if (e.Costs is { } cs) { req.Costs.Clear(); req.Costs.AddRange(Resolve(cs, Hex, "resource")); }
            if (e.Unlocks is { } us) { req.Unlocks.Clear(); req.Unlocks.AddRange(us.Select(u => Furniture(u) ?? Fail("furniture", u))); }
            if (!ok) continue;
            if (req.Problems() is { Count: > 0 } bad) { r.Errors.Add($"{where}: {string.Join("; ", bad)}"); continue; }
            body = req.Write(tag, body);
            r.Report.Add($"{where}: {req.Minions.Count} minion, {req.Furniture.Count} furniture, {req.Costs.Count} cost, {req.Unlocks.Count} unlock entries ({mod.Id})");
        }
        return body;
    }

    /// <summary>Replaces scripts of one object body by copies of donor scripts (<see cref="ScriptSwap"/>).</summary>
    static byte[] ApplySwaps(GameData game, byte[] body, IEnumerable<(ModDefinition Mod, ScriptSwap Swap)> swaps, IEnumerable<int> editedOffsets,
                             IReadOnlyDictionary<string, uint> refs, BuildResult r)
    {
        var list = swaps.ToList();
        foreach (var dup in list.GroupBy(x => x.Swap.Script).Where(x => x.Count() > 1))
            r.Errors.Add($"script {dup.Key + 1} of {dup.First().Swap.Object} is replaced by {string.Join(" and ", dup.Select(x => x.Mod.Id))}; pick one");
        var edited = editedOffsets.ToList();
        foreach (var (mod, s) in list.DistinctBy(x => x.Swap.Script))
        {
            string where = $"{mod.Id}: script {s.Script + 1} of {s.Object}";
            var donor = game.Objects.FirstOrDefault(o => !o.IsRecord && Bytes.Hex(o.ObjectId).Equals(s.From, StringComparison.OrdinalIgnoreCase));
            if (donor is null) { r.Errors.Add($"{where}: donor {s.From} not found"); continue; }
            var targets = FlowGraph.Swappable(body);
            var donors = FlowGraph.Swappable(donor.Body);
            if (s.Script < 0 || s.Script >= targets.Count) { r.Errors.Add($"{where}: the object has {targets.Count} replaceable scripts"); continue; }
            if (s.FromScript < 0 || s.FromScript >= donors.Count) { r.Errors.Add($"{where}: {donor.Name} has {donors.Count} replaceable scripts"); continue; }
            var t = targets[s.Script].Block;
            if (edited.FirstOrDefault(off => off > t.Offset && off < t.Offset + 9 + t.Payload().Length) is var hit and > 0)
            { r.Errors.Add($"{where}: a value edit at {hit} is inside the replaced script; change it in the swap instead"); continue; }
            var payload = donors[s.FromScript].Block.Payload();
            var values = new List<(int, byte[])>();
            foreach (var v in s.Values)
            {
                byte[] data;
                try { data = v.Encode(refs); }
                catch (Exception e) when (e is FormatException or OverflowException) { r.Errors.Add($"{where}: bad value '{v.Value}' ({e.Message})"); continue; }
                if (v.Offset < 8 || v.Offset + data.Length > payload.Length || !payload.AsSpan(v.Offset, data.Length).SequenceEqual(Convert.FromHexString(v.Expect)))
                { r.Errors.Add($"{where}: value at {v.Offset} doesn't match {donor.Name}'s script (game updated?)"); continue; }
                values.Add((v.Offset, data));
            }
            try { body = FlowGraph.Swap(body, s.Script, donor.Body, s.FromScript, values); }
            catch (InvalidOperationException e) { r.Errors.Add($"{where}: {e.Message}"); continue; }
            r.Report.Add($"{where}: script \"{targets[s.Script].Graph.Name}\" replaced by a copy of \"{donors[s.FromScript].Graph.Name}\" from {donor.Name} ({values.Count} values changed)");
        }
        return body;
    }

    /// <summary>"0x1234abcd" or "@Key" (a new object) to an id; null if it's neither.</summary>
    static uint? ResolveId(string s, IReadOnlyDictionary<string, uint> refs)
    {
        s = s.Trim();
        if (s.StartsWith('@')) return refs.TryGetValue(s[1..], out var id) ? id : null;
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
               && uint.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }

    /// <summary>Adds chunks (listed as data objects), furniture records and an inline HTXT to the dev slot the build wrote (or an empty one).</summary>
    static void AppendToDevSlot(GameData game, IReadOnlyDictionary<string, uint> refs, IdAllocator ids, BuildResult r, Action<string> log, CancellationToken ct,
                                List<Chunk> objects, List<FurnitureRecord> records, TextTable? htxt)
    {
        string dir = GameInstall.DevSlotDir, contentRel = $@"{dir}\{Slot}_Content.asr", manRel = $@"{dir}\{Slot}.asr";
        if (objects.Count == 0 && records.Count == 0 && htxt is null) return;
        if (!r.Files.ContainsKey(contentRel)) BuildDevSlot(game, new(), new(), refs, ids, r, log, ct, Array.Empty<ModDefinition>());
        var content = AsuraArchive.FromBytes(r.Files[contentRel]);
        if (htxt is not null) content.Chunks.Add(new Chunk("HTXT", htxt.ToBytes()));
        content.Chunks.AddRange(objects);
        if (records.Count > 0)
        {
            var fc = content.First("fntr");
            var table = fc is not null ? FurnitureTable.Parse(fc.Body) : new FurnitureTable();
            foreach (var rec in records) table.Add(rec);
            if (fc is not null) fc.Body = table.ToBytes(); else content.Chunks.Add(new Chunk("fntr", table.ToBytes()));
        }
        content.Compressed = true;
        r.Files[contentRel] = content.ToBytes();
        if (objects.Count == 0) return;
        var man = AsuraArchive.FromBytes(r.Files[manRel]);
        var rp = man.First("rpkg")!;
        var mp = Package.Parse(rp.Tag, rp.Body);
        var list = mp.IdArrays().TryGetValue(Package.ObjectsPath, out var a) ? a : new List<uint>();
        mp.SetArray(Package.ObjectsPath, list.Concat(objects.Select(c => Bytes.U32(c.Body, 8))).ToList());
        rp.Body = mp.ToBytes();
        var rsfl = man.First("RSFL")!;
        var rl = ResourceList.Parse(rsfl.Body);
        rl.Entries[0].Size = (uint)rp.Size;
        rsfl.Body = rl.ToBytes();
        r.Files[manRel] = man.ToBytes();
    }

    /// <summary>
    /// ModKit's own text table, text\pc\eg2modkit, for new items' texts. The game loads the tables listed in
    /// text\pc\localisation.asr (one HTPR [u32 1][u32 0][NAME\0 padded to 4] each), so the only base change is one
    /// more line there. Confirmed in-game 2026-09-24.
    /// </summary>
    public const string OwnTextTable = "eg2modkit";

    static Chunk Remap(Chunk c, uint old, uint now, string where, BuildResult r)
    {
        var o = Bytes.Le(old);
        if (Bytes.Count(c.Body, o) != 1) r.Errors.Add($"{where}: {c.Tag} holds its id more than once");
        return new Chunk(c.Tag, Bytes.Replace(c.Body, o, Bytes.Le(now)));
    }

    /// <summary>Cost edits + field edits, one full content .asrpatch per touched package.</summary>
    static void BuildPackagePatches(GameData game, IReadOnlyList<ModDefinition> mods, IReadOnlyDictionary<string, uint> refs, BuildResult r, Action<string> log, CancellationToken ct)
    {
        var costs = mods.SelectMany(m => m.FurnitureEdits).DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Where(e => !ViaOverride(game.FindFurniture(e.Name)!.Package)).ToList();
        var fields = mods.SelectMany(m => m.FieldEdits)
            .DistinctBy(f => $"{f.Package}/{f.Tag}/{f.Object}/{f.Offset}".ToLowerInvariant()).Where(f => !ViaOverride(f.Package)).ToList();
        var assetEdits = mods.SelectMany(m => m.Assets.Select(a => (Mod: m, A: a))).Where(x => !IsStreamed(x.A) && x.A.Tag != "ASTS" && x.A.Island is null).ToList();
        var contentOf = game.Install.Packages().ToDictionary(p => game.Install.Rel(p.ContentPath), p => p.Name, StringComparer.OrdinalIgnoreCase);
        string Norm(string f) => f.Replace('/', '\\');
        var packages = costs.Select(e => game.FindFurniture(e.Name)!.Package).Concat(fields.Select(f => f.Package))
            .Concat(assetEdits.Where(x => contentOf.ContainsKey(Norm(x.A.File))).Select(x => contentOf[Norm(x.A.File)]))
            .Concat(r.PatchObjects.Keys).Concat(r.PatchTextures.Keys)
            .Concat(mods.SelectMany(m => m.NewClips).Select(c => c.Package))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var newClips = mods.SelectMany(m => m.NewClips.Select(c => (Mod: m, Clip: c))).ToList();
        foreach (var dup in newClips.GroupBy(x => x.Clip.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            r.Errors.Add($"conflict: new clip {dup.Key} added by {string.Join(" and ", dup.Select(x => x.Mod.Id))}");

        int pn = 0;
        foreach (var name in packages)
        {
            ct.ThrowIfCancellationRequested();
            var pkg = game.Install.Package(name)!;
            log($"patching package {++pn}/{packages.Count}: {pkg.Name}");
            AsuraArchive? arc = AsuraArchive.Load(pkg.ContentPath);
            var fntr = arc.First("fntr");
            var table = fntr is not null && fntr.Body.Length > 13 ? FurnitureTable.Parse(fntr.Body) : null;
            FurnitureRecord Record(string n) => table?.Records.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                                                ?? throw new KeyNotFoundException($"{pkg.Name}: furniture '{n}' not found");

            foreach (var e in costs.Where(e => game.FindFurniture(e.Name)!.Package.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var rec = Record(e.Name);
                r.Report.Add($"cost {rec.Name}: {rec.Cost} -> {e.Cost}");
                rec.Cost = e.Cost;
            }

            var mine = fields.Where(f => f.Package.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var f in mine)
            {
                var data = f.Encode(refs);
                var expect = Convert.FromHexString(f.Expect);
                string where = $"field {f.Package}/{f.Tag}/{f.Object}+{f.Offset}";
                if (f.Tag == "fntr")
                {
                    var rec = Record(f.Object);
                    var p = rec.Payload;
                    if (f.Offset < 0 || f.Offset + data.Length > p.Length || !p.AsSpan(f.Offset, data.Length).SequenceEqual(expect))
                    { r.Errors.Add($"{where}: base bytes differ from expected {f.Expect} (game updated?)"); continue; }
                    rec.SetBytes(f.Offset, data);
                }
                else
                {
                    var chunk = FindObject(arc, f, r);
                    if (chunk is null) continue;
                    var body = (byte[])chunk.Body.Clone();
                    if (f.Offset < ObjectHeader.Size || f.Offset + data.Length > body.Length || !body.AsSpan(f.Offset, data.Length).SequenceEqual(expect))
                    { r.Errors.Add($"{where}: base bytes differ from expected {f.Expect} (or offset is in the header)"); continue; }
                    Buffer.BlockCopy(data, 0, body, f.Offset, data.Length);
                    chunk.Body = body;
                }
                r.Report.Add($"{where}: {f.Expect} -> {Convert.ToHexString(data)} ({f.Type} {f.Value}){(f.Note is { Length: > 0 } n ? "  " + n : "")}");
            }
            var mineAssets = assetEdits.Where(x => AssetReplacement.Same(x.A.File, game.Install.Rel(pkg.ContentPath))).ToList();
            ApplyAssets(arc, mineAssets, r);
            if (!r.Ok) return;
            // new clips go after the package's own; the game finds them by KeyHash of the name
            foreach (var (m, c) in newClips.Where(x => x.Clip.Package.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                string where = $"{m.Id}: new clip {c.Name}";
                if (c.Name.Length == 0 || c.Name.Any(ch => ch is < ' ' or > '~')) { r.Errors.Add($"{where}: the name must be plain ASCII"); continue; }
                if (arc.Chunks.Any(x => x.Tag == "HCAN" && AssetIndex.NameOf("HCAN", x.Body).Equals(c.Name, StringComparison.OrdinalIgnoreCase)))
                { r.Errors.Add($"{where}: {pkg.Name} already has a clip with that name"); continue; }
                AnimClip clip;
                try { clip = AnimClip.Parse(File.ReadAllBytes(Path.Combine(ModPackage.AssetDir(m), c.Source))); }
                catch (Exception e) when (e is IOException or ArgumentException or IndexOutOfRangeException or InvalidDataException or FormatException || e.Message is "ver" or "pre" or "ev0" or "len")
                { r.Errors.Add($"{where}: {c.Source} is not a clip ({e.Message})"); continue; }
                clip.Name = c.Name;
                clip.NameHash = TextTable.KeyHash(c.Name);
                int at = arc.Chunks.FindLastIndex(x => x.Tag == "HCAN") + 1;
                arc.Chunks.Insert(at > 0 ? at : arc.Chunks.Count, new Chunk("HCAN", clip.Encode()));
                r.Report.Add($"{where}: added to {pkg.Name} ({clip.Dur:0.##} s, {clip.Bones} bones, from {c.Source})");
            }
            if (!r.Ok) return;
            if (r.PatchTextures.TryGetValue(name, out var textures))
            {   // with the package's own textures (after its last RSCF), ahead of the objects that use them
                int at = arc.Chunks.FindLastIndex(c => c.Tag == "RSCF") + 1;
                arc.Chunks.InsertRange(at, textures);
                r.Report.Add($"{pkg.Name}: {textures.Count} new textures");
            }
            if (r.PatchObjects.TryGetValue(name, out var added))
            {
                arc.Chunks.AddRange(added);
                var man = AsuraArchive.Load(pkg.ManifestPath);
                var rp = man.First("rpkg") ?? throw new InvalidOperationException($"{pkg.Name}: manifest has no rpkg");
                var mp = Package.Parse(rp.Tag, rp.Body);
                var have = mp.IdArrays().TryGetValue(Package.ObjectsPath, out var a) ? a : new List<uint>();
                mp.SetArray(Package.ObjectsPath, have.Concat(added.Select(c => Bytes.U32(c.Body, 8))).ToList());
                rp.Body = mp.ToBytes();
                if (man.First("RSFL") is { } rsfl) { var rl = ResourceList.Parse(rsfl.Body); rl.Entries[0].Size = (uint)rp.Size; rsfl.Body = rl.ToBytes(); }
                r.Files[game.Install.Rel(pkg.ManifestPath) + GameInstall.PatchSuffix] = man.ToBytes();
                r.Report.Add($"{pkg.Name}: {added.Count} new objects listed in its manifest");
            }
            if (table is not null) fntr!.Body = table.ToBytes();

            log($"compressing {pkg.Name} content patch");
            string rel = game.Install.Rel(pkg.ContentPath) + GameInstall.PatchSuffix;
            // ~1 GB decompressed: keep at most two copies alive at once (chunks+payload, then payload+output)
            byte[]? raw = arc.Payload();
            arc = null;
            fntr = null;
            r.Files[rel] = AsuraArchive.Compress(raw);
            raw = null;
            r.Report.Add($"warning: {rel} is a full copy of the base package ({r.Files[rel].Length / 1048576} MB)");

            // self-check: every edit is present in the re-parsed patch
            var back = AsuraArchive.FromBytes(r.Files[rel]);
            var t2 = back.First("fntr") is { Body.Length: > 13 } fc ? FurnitureTable.Parse(fc.Body) : null;
            foreach (var e in costs.Where(e => game.FindFurniture(e.Name)!.Package.Equals(name, StringComparison.OrdinalIgnoreCase)))
                Check(r, t2!.Records.First(x => x.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase)).Cost == e.Cost, $"{e.Name} cost");
            foreach (var f in mine)
            {
                var bytes = f.Tag == "fntr"
                    ? t2!.Records.First(x => x.Name.Equals(f.Object, StringComparison.OrdinalIgnoreCase)).Payload
                    : FindObject(back, f, r)!.Body;
                Check(r, bytes.AsSpan(f.Offset, FieldEdit.SizeOf(f.Type)).SequenceEqual(f.Encode(refs)), $"field {f.Object}+{f.Offset}");
            }
            CheckAssets(back, mineAssets, r);
        }

        // asset stores that aren't package contents (sound banks): one whole-file patch each, same compression as the original
        var loose = assetEdits.Where(x => !contentOf.ContainsKey(Norm(x.A.File))).GroupBy(x => Norm(x.A.File), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var bars = mods.Where(m => m.HenchmanBarSlots is not null).ToList();
        if (bars.Select(m => m.HenchmanBarSlots).Distinct().Count() > 1)
            r.Errors.Add($"conflict: henchman bar slots set by {string.Join(" and ", bars.Select(m => $"{m.Id} ({m.HenchmanBarSlots})"))}");
        if (bars.FirstOrDefault()?.HenchmanBarSlots is { } slots and (< HenchmanBar.GameSlots or > 100))
            r.Errors.Add($"{bars[0].Id}: henchman bar slots must be {HenchmanBar.GameSlots} to 100");
        if (!r.Ok) return;
        int barSlots = bars.FirstOrDefault()?.HenchmanBarSlots ?? HenchmanBar.GameSlots;
        if (barSlots > HenchmanBar.GameSlots) loose.TryAdd(HenchmanBar.File, new());
        foreach (var (file, items) in loose)
        {
            ct.ThrowIfCancellationRequested();
            log($"patching {file}");
            AsuraArchive? arc = AsuraArchive.Load(game.Install.Full(file));
            ApplyAssets(arc, items, r);
            if (!r.Ok) return;
            if (barSlots > HenchmanBar.GameSlots && file.Equals(HenchmanBar.File, StringComparison.OrdinalIgnoreCase))
            {
                var guat = arc.First("GUAT") ?? throw new InvalidOperationException($"{file}: no GUAT chunk");
                guat.Body = HenchmanBar.Apply(guat.Body, barSlots);
                r.Report.Add($"henchman bar: {barSlots} slots");
            }
            bool compressed = arc.Compressed;
            byte[]? raw = arc.Payload();
            arc = null;
            string rel = file + GameInstall.PatchSuffix;
            r.Files[rel] = compressed ? AsuraArchive.Compress(raw) : raw;
            raw = null;
            CheckAssets(AsuraArchive.FromBytes(r.Files[rel]), items, r);
        }
    }

    /// <summary>Lair maps: one whole-file .asrpatch per touched envs\basedefinitions\*.base (<see cref="LairMap"/>). The engine
    /// loads maps without its .asrpatch lookup, so the runtime DLL redirects the file open (installed by BuildRuntime).</summary>
    static void BuildMaps(GameData game, IReadOnlyList<ModDefinition> mods, BuildResult r, Action<string> log, CancellationToken ct)
    {
        foreach (var group in mods.SelectMany(m => m.MapEdits.Select(e => (Mod: m, Edit: e))).GroupBy(x => x.Edit.File.Replace('/', '\\'), StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            string path = game.Install.Full(group.Key);
            bool isNew = r.Files.TryGetValue(group.Key, out var copy);   // a NewLair of this build: edit the copy itself
            if (!group.Key.EndsWith(".base", StringComparison.OrdinalIgnoreCase) || !isNew && !File.Exists(path)) { r.Errors.Add($"{group.First().Mod.Id}: map {group.Key}: not a lair map in this install"); continue; }
            log($"patching {group.Key}");
            var map = LairMap.Parse(copy ?? File.ReadAllBytes(path));
            foreach (var (mod, e) in group)
            {
                try
                {
                    string what = ApplyMapEdit(game, map, e);
                    r.Report.Add($"{mod.Id}: {group.Key} floor {e.Floor}: {what}{(e.Note is null ? "" : $" ({e.Note})")}");
                }
                catch (ArgumentException x) { r.Errors.Add($"{mod.Id}: map {group.Key}: {x.Message}"); }
            }
            if (!r.Ok) return;
            // placed agents need a raid record each, or they turn unkillable when the game registers a raid of its own
            try { if (Agents.AddRaidRecords(map) is > 0 and var n) r.Report.Add($"{group.Key}: world state with a raid record for {n} placed squad{(n == 1 ? "" : "s")}"); }
            catch (Exception x) when (x is ArgumentException or InvalidDataException) { r.Errors.Add($"map {group.Key}: {x.Message}"); return; }
            var bytes = map.ToBytes();
            LairMap.Parse(bytes);   // must still read back
            r.Files[isNew ? group.Key : group.Key + GameInstall.PatchSuffix] = bytes;
        }
    }

    static readonly Regex LairStemRx = new("^lair_[a-z0-9_]+$");

    /// <summary>New lairs: the source lair's files copied under the new stem (a real new file each, not an .asrpatch), the
    /// map's own lair id (bsnf +17) changed to KeyHash(new stem). The source island's texture replacements in this build follow.</summary>
    static void BuildNewLairs(GameData game, IReadOnlyList<ModDefinition> mods, IReadOnlyDictionary<string, uint> refs, BuildResult r, Action<string> log)
    {
        foreach (var (mod, n) in mods.SelectMany(m => m.NewLairs.Select(n => (m, n))))
        {
            string where = $"{mod.Id}: new lair {n.Stem}", src = game.Install.Full($@"envs\basedefinitions\{n.From}.base"), dest = $@"envs\basedefinitions\{n.Stem}.base";
            if (!LairStemRx.IsMatch(n.Stem)) { r.Errors.Add($"{where}: the name must be lair_ + lowercase letters, digits, _"); continue; }
            if (!File.Exists(src)) { r.Errors.Add($"{where}: no game lair {n.From}"); continue; }
            // taken: a game lair, or another mod's in this build (a copy ModKit installed before is fine)
            bool ours = Installer.Current(game.Install.Root)?.Files.Any(f => f.Path.Equals(dest, StringComparison.OrdinalIgnoreCase)) == true;
            if (File.Exists(game.Install.Full(dest)) && !ours || r.Files.ContainsKey(dest)) { r.Errors.Add($"{where}: that lair already exists"); continue; }
            log($"new lair {n.Stem} from {n.From}");
            // the source island's world-map region (its rmlr names the lair file at +83), swapped for the mod's own
            uint oldRegion = 0, newRegion = 0;
            if (n.Region is { } reg)
            {
                uint fileKey = TextTable.KeyHash(n.From + ".base");
                var old = game.Objects.FirstOrDefault(o => o.Tag == "rmlr" && !o.IsRecord && o.Body.Length >= 87 && Bytes.U32(o.Body, 83) == fileKey);
                if (old is null) { r.Errors.Add($"{where}: no world-map region names {n.From}"); continue; }
                try { (oldRegion, newRegion) = (old.ObjectId, BitConverter.ToUInt32(new FieldEdit { Value = reg }.Encode(refs))); }
                catch (FormatException x) { r.Errors.Add($"{where}: region: {x.Message}"); continue; }
            }
            if (LairMap.Renamed(File.ReadAllBytes(src), n.From, n.KeepId ? n.From : n.Stem, oldRegion, newRegion) is not { } copy) { r.Errors.Add($"{where}: {n.From}.base has no lair id where expected (bsnf +17)"); continue; }
            r.Files[dest] = copy;
            if (newRegion != 0) r.LairRegions[n.Stem] = newRegion;
            // the island (outside) is named by the lair's settings, found by the lair id: with KeepId the game asks for the
            // source's island files and the runtime DLL opens these copies instead while this lair's map is loaded
            foreach (var f in Directory.GetFiles(game.Install.Full("envs"), n.From + ".*").Where(f => !f.EndsWith(GameInstall.PatchSuffix, StringComparison.OrdinalIgnoreCase)))
            {
                // as this build patched it (texture replacements, e.g. an island recolour, run before this step)
                string rel = game.Install.Rel(f) + GameInstall.PatchSuffix;
                var bytes = r.Files.TryGetValue(rel, out var patched) ? patched : File.ReadAllBytes(f);
                if (f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) && r.IslandTextures.TryGetValue(n.Stem, out var own)) bytes = OwnTextures(f, bytes, own, where, r);
                var inPc = mods.SelectMany(m => m.Assets.Where(a => a.Island == n.Stem && AssetReplacement.Same(a.File, game.Install.Rel(f))).Select(a => (m, a))).ToList();
                bool edits = n.IslandEdits.Count > 0 && f.EndsWith(".pc", StringComparison.OrdinalIgnoreCase);
                if (inPc.Count > 0 || edits)
                {   // the island's own low-res / non-streamed textures and settings (fog), in this copy only
                    var arc = AsuraArchive.FromBytes(bytes);
                    ApplyAssets(arc, inPc, r);
                    if (edits) ApplyIslandEdits(arc, n, where, r);
                    bytes = arc.Compressed ? AsuraArchive.Compress(arc.Payload()) : arc.Payload();
                    CheckAssets(AsuraArchive.FromBytes(bytes), inPc, r);
                }
                r.Files[$@"envs\{n.Stem}{Path.GetFileName(f)[n.From.Length..]}"] = bytes;
            }
            r.Report.Add($"{where}: copy of {n.From} (lair id {TextTable.KeyHash(n.KeepId ? n.From : n.Stem):x8}{(n.KeepId ? ", kept" : "")}){(n.Note is null ? "" : $" ({n.Note})")}");
        }
    }

    /// <summary>NewLair.IslandEdits: values in the first chunk of each tag of the island copy (Expect checked when given).</summary>
    static void ApplyIslandEdits(AsuraArchive arc, NewLair n, string where, BuildResult r)
    {
        foreach (var e in n.IslandEdits)
        {
            string at = $"{where}: island {e.Tag.Trim()}+{e.Offset}";
            if (arc.First(e.Tag) is not { } chunk) { r.Errors.Add($"{at}: the island has no {e.Tag.Trim()} chunk"); continue; }
            byte[] data;
            try { data = e.Encode(); } catch (Exception x) when (x is FormatException or OverflowException) { r.Errors.Add($"{at}: {x.Message}"); continue; }
            if (e.Offset < 0 || e.Offset + data.Length > chunk.Body.Length) { r.Errors.Add($"{at}: past the end of the chunk"); continue; }
            if (e.Expect.Length > 0 && !chunk.Body.AsSpan(e.Offset, data.Length).SequenceEqual(Convert.FromHexString(e.Expect)))
            { r.Errors.Add($"{at}: base bytes differ from expected {e.Expect} (game updated?)"); continue; }
            var body = (byte[])chunk.Body.Clone();
            data.CopyTo(body, e.Offset);
            chunk.Body = body;
            r.Report.Add($"{at} = {e.Type} {e.Value}{(e.Note is null ? "" : "  " + e.Note)}");
        }
    }

    /// <summary>A new lair's copy of its source's texture table with the lair's own textures (AssetReplacement.Island)
    /// repointed. Entries are matched through the game's table (same order): the copy's may already point at ModKit's
    /// blob for a shared replacement.</summary>
    static byte[] OwnTextures(string gameTs, byte[] copy, Dictionary<(string, long), (uint Offset, uint Size)> own, string where, BuildResult r)
    {
        var orig = TextureTable.Parse(AsuraArchive.Load(gameTs).First("TXST")!.Body);
        var arc = AsuraArchive.FromBytes(copy);
        var chunk = arc.First("TXST")!;
        var t = TextureTable.Parse(chunk.Body);
        if (t.Entries.Count != orig.Entries.Count) { r.Errors.Add($"{where}: texture table layout differs from the game's"); return copy; }
        int idx = t.Blobs.FindIndex(b => b.Equals(TextureBlobTarget, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) { t.Blobs.Add(TextureBlobTarget); idx = t.Blobs.Count - 1; }
        int moved = 0;
        for (int i = 0; i < orig.Entries.Count; i++)
        {
            var o = orig.Entries[i];
            if (o[TextureTable.Blob] >= orig.Blobs.Count || !own.TryGetValue((orig.Blobs[(int)o[TextureTable.Blob]].ToLowerInvariant(), o[TextureTable.Offset]), out var p)) continue;
            t.Entries[i][TextureTable.Blob] = (uint)idx;
            t.Entries[i][TextureTable.Offset] = p.Offset;
            t.Entries[i][TextureTable.Size] = p.Size;
            moved++;
        }
        if (moved == 0) { r.Errors.Add($"{where}: none of its own textures is on this island"); return copy; }
        chunk.Body = t.ToBytes();
        r.Report.Add($"{where}: {moved} texture entries of its own");
        return arc.ToBytes();
    }

    /// <summary>
    /// New lairs with their own id (not KeepId): a settings entry in misc\common.asr (a copy of the source lair's, whole-file
    /// patch, ~260 MB) naming the new island files, which are renamed to the island name it picks (scenery edits target
    /// envs\&lt;stem&gt;.pc during the build). See <see cref="LairMap.WithLairSettings"/>: the chunk keeps its size.
    /// </summary>
    static void BuildLairSettings(GameData game, IReadOnlyList<ModDefinition> mods, BuildResult r, Action<string> log)
    {
        var own = mods.SelectMany(m => m.NewLairs.Select(n => (m, n))).Where(x => !x.n.KeepId && r.Files.ContainsKey($@"envs\basedefinitions\{x.n.Stem}.base")).ToList();
        string? probe = mods.Select(m => m.LairSettingsProbe).FirstOrDefault(p => p is not null);
        if (probe is not null) { LairSettingsProbe(game, probe, r); return; }
        var treeEdits = mods.SelectMany(m => m.TreeEdits.Select(e => (Mod: m, Edit: e))).ToList();
        foreach (var g in treeEdits.GroupBy(x => (Tree: x.Edit.Tree.ToLowerInvariant(), x.Edit.Step, What: x.Edit.Field ?? $"setting {x.Edit.Setting + 1}")).Where(g => g.Select(x => x.Edit.Value).Distinct().Count() > 1))
            r.Errors.Add($"conflict: behaviour tree {g.Key.Tree} step {g.Key.Step} {g.Key.What} set by {string.Join(" and ", g.Select(x => x.Mod.Id).Distinct())} to different values");
        var swaps = mods.SelectMany(m => m.ClipSwaps.Select(s => (Mod: m, Swap: s))).ToList();
        foreach (var g in swaps.GroupBy(x => x.Swap.From, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(x => x.Swap.To.ToLowerInvariant()).Distinct().Count() > 1))
            r.Errors.Add($"conflict: clip {g.Key} swapped by {string.Join(" and ", g.Select(x => x.Mod.Id).Distinct())} to different clips");
        var classTrees = mods.SelectMany(m => m.ClassTrees.Select(c => (Mod: m, Edit: c))).ToList();
        foreach (var g in classTrees.GroupBy(x => x.Edit.Class.ToLowerInvariant()).Where(g => g.Select(x => x.Edit.Tree.ToLowerInvariant()).Distinct().Count() > 1))
            r.Errors.Add($"conflict: class {g.Key} given different behaviour trees by {string.Join(" and ", g.Select(x => x.Mod.Id).Distinct())}");
        if (own.Count == 0 && treeEdits.Count == 0 && swaps.Count == 0 && classTrees.Count == 0 || !r.Ok) return;
        log("misc\\common.asr");
        var arc = AsuraArchive.Load(game.Install.Full(@"misc\common.asr"));
        foreach (var (mod, n) in own)
        {
            string where = $"{mod.Id}: new lair {n.Stem}";
            uint fromId = TextTable.KeyHash(n.From), id = TextTable.KeyHash(n.Stem);
            int at = arc.Chunks.FindIndex(c => c.Tag == "BLUE" && LairMap.WithLairSettings(c.Body, fromId, id, n.From, n.Stem) is not null);
            if (at < 0) { r.Errors.Add($"{where}: no settings for {n.From} in misc\\common.asr, or no room left for another lair's"); continue; }
            var (blue, island, dropped) = LairMap.WithLairSettings(arc.Chunks[at].Body, fromId, id, n.From, n.Stem)!.Value;
            if (blue.Length != arc.Chunks[at].Body.Length) throw new InvalidOperationException("lair settings chunk changed size");
            arc.Chunks[at] = new Chunk("BLUE", blue);
            foreach (var k in r.Files.Keys.Where(k => island != n.Stem && k.StartsWith($@"envs\{n.Stem}.", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                // (a patch of an earlier install's copy is left behind: the copy itself is new each build)
                if (!k.EndsWith(GameInstall.PatchSuffix, StringComparison.OrdinalIgnoreCase)) r.Files[$@"envs\{island}{k[(5 + n.Stem.Length)..]}"] = r.Files[k];
                r.Files.Remove(k);
            }
            r.Report.Add($"{where}: own lair settings {id:x8} (copy of {n.From}'s, in place of {dropped} test levels'), island envs\\{island}.*");
        }
        if (treeEdits.Count > 0)
        {
            var axbt = arc.First("AXBT") ?? throw new InvalidOperationException("misc\\common.asr has no AXBT chunk (behaviour trees)");
            foreach (var err in BehaviourTrees.Apply(axbt.Body, treeEdits.Select(x => x.Edit).DistinctBy(e => (e.Tree.ToLowerInvariant(), e.Step, e.Field ?? $"setting {e.Setting + 1}"))))
                r.Errors.Add($"behaviour trees: {err}");
            foreach (var (m, e) in treeEdits) r.Report.Add($"behaviour tree {e.Tree} step {e.Step} {e.Field ?? $"setting {e.Setting + 1}"} = {e.Value} ({m.Id})");
        }
        // clip swaps: the 4-byte name hash, in place (sizes never change, as this file needs)
        foreach (var (m, s) in swaps.DistinctBy(x => x.Swap.From.ToLowerInvariant()))
        {
            uint from = TextTable.KeyHash(s.From), to = TextTable.KeyHash(s.To);
            int n = 0;
            foreach (var c in arc.Chunks.Where(c => c.Tag is "BLUE" or "CPAN" or "RFLX" or "AALG"))
            {
                var b = c.Body;
                int hits = 0;
                for (int i = 0; i + 4 <= b.Length; i++)
                    if (Bytes.U32(b, i) == from) { Bytes.PutU32(b, i, to); hits++; i += 3; }
                if (hits > 0) { c.Body = b; n += hits; }
            }
            if (n == 0) r.Errors.Add($"{m.Id}: clip swap {s.From} -> {s.To}: nothing in misc\\common.asr names {s.From}");
            else r.Report.Add($"clip swap {s.From} -> {s.To}: {n} references ({m.Id})");
        }
        foreach (var (m, c) in classTrees.DistinctBy(x => x.Edit.Class.ToLowerInvariant()))
        {
            uint cls, tree;
            try { cls = Convert.ToUInt32(c.Class, 16); tree = Convert.ToUInt32(c.Tree, 16); }
            catch (FormatException) { r.Errors.Add($"{m.Id}: class tree {c.Class} -> {c.Tree}: not hex ids"); continue; }
            int at = arc.Chunks.FindIndex(x => x.Tag == "BLUE" && Agents.WithDefaultTree(x.Body, cls, tree) is not null);
            if (at < 0) { r.Errors.Add($"{m.Id}: class {c.Class}: not in misc\\common.asr, or it has no member that can carry a behaviour tree"); continue; }
            arc.Chunks[at] = new Chunk("BLUE", Agents.WithDefaultTree(arc.Chunks[at].Body, cls, tree)!);
            r.Report.Add($"class {c.Class} runs behaviour tree {c.Tree} ({m.Id})");
        }
        if (!r.Ok) return;
        // Any misc\common.asr.asrpatch (even one byte-identical outside its first 2 MB block) loses the menu art: the game
        // treats that file specially. So the file stays, and the runtime DLL swaps in the changed compressed block (padded
        // to its old size) as the game reads it (kind = file_block).
        var orig = File.ReadAllBytes(game.Install.Full(@"misc\common.asr"));
        if (AsuraArchive.ChangedBlocks(orig, arc.ToBytes(compressed: false)) is not { } blocks) { r.Errors.Add("misc\\common.asr: a changed part doesn't re-compress into its old space"); return; }
        foreach (var (offset, data) in blocks)
        {
            string rel = $@"{RuntimeBlocksDir}\common.asr.{offset}.bin";
            r.Files[rel] = data;
            r.FileBlocks.Add((@"misc\common.asr", offset, rel));
            r.Report.Add($"misc\\common.asr bytes {offset}..{offset + data.Length} swapped at run time ({rel})");
        }
    }

    /// <summary>ModDefinition.LairSettingsProbe: one run-time swap of common.asr's first compressed block (it holds the
    /// lair settings chunk): its own bytes, its content re-compressed, or one spare entry renamed.</summary>
    static void LairSettingsProbe(GameData game, string probe, BuildResult r)
    {
        var orig = File.ReadAllBytes(game.Install.Full(@"misc\common.asr"));
        int comp = (int)Bytes.U32(orig, 16), at = 24;
        byte[] data;
        switch (probe)
        {
            case "hook":
                data = orig[at..(at + comp)];
                break;
            case "recompress":
            {
                var raw = new byte[Bytes.U32(orig, 20)];
                using (var z = new System.IO.Compression.ZLibStream(new MemoryStream(orig, at, comp, writable: false), System.IO.Compression.CompressionMode.Decompress))
                    z.ReadExactly(raw);
                var fresh = Zlib4k.CompressSmall(raw);
                if (fresh.Length > comp) { r.Errors.Add("lair settings probe: block 0 doesn't re-compress into its old space"); return; }
                data = new byte[comp];
                fresh.CopyTo(data, 0);
                break;
            }
            case "spare-id":
            {
                var arc = AsuraArchive.Load(game.Install.Full(@"misc\common.asr"));
                int i = arc.Chunks.FindIndex(c => c.Tag == "BLUE" && LairMap.WithSpareRenamed(c.Body, TextTable.KeyHash("eg2modkit_probe")) is not null);
                if (i < 0) { r.Errors.Add("lair settings probe: no spare test level's settings found"); return; }
                arc.Chunks[i] = new Chunk("BLUE", LairMap.WithSpareRenamed(arc.Chunks[i].Body, TextTable.KeyHash("eg2modkit_probe"))!);
                var blocks = AsuraArchive.ChangedBlocks(orig, arc.ToBytes(compressed: false));
                if (blocks is not [var only] || only.Offset != at) { r.Errors.Add($"lair settings probe: expected one changed block at {at}, got {blocks?.Count.ToString() ?? "none"}"); return; }
                data = only.Data;
                break;
            }
            default: r.Errors.Add($"lair settings probe: unknown '{probe}' (hook, recompress or spare-id)"); return;
        }
        string rel = $@"{RuntimeBlocksDir}\common.asr.{at}.bin";
        r.Files[rel] = data;
        r.FileBlocks.Add((@"misc\common.asr", at, rel));
        r.Report.Add($"lair settings probe '{probe}': misc\\common.asr bytes {at}..{at + comp} swapped at run time" +
                     (probe == "hook" ? " (the original bytes)" : ""));
    }

    /// <summary>Island scenery: one whole-file .pc.asrpatch per touched island (loaded through the runtime DLL).</summary>
    static void BuildScenery(GameData game, IReadOnlyList<ModDefinition> mods, BuildResult r, Action<string> log, CancellationToken ct)
    {
        foreach (var group in mods.SelectMany(m => m.SceneryEdits.Select(e => (Mod: m, Edit: e))).GroupBy(x => x.Edit.File.Replace('/', '\\'), StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            string path = game.Install.Full(group.Key);
            bool isNew = r.Files.ContainsKey(group.Key);   // a NewLair of this build: edit the copy itself
            if (!group.Key.EndsWith(".pc", StringComparison.OrdinalIgnoreCase) || !isNew && !File.Exists(path)) { r.Errors.Add($"{group.First().Mod.Id}: scenery {group.Key}: not an island in this install"); continue; }
            log($"patching {group.Key}");
            // on top of this build's own copy when texture replacements (e.g. an island recolour) already patched the file
            string key = isNew ? group.Key : group.Key + GameInstall.PatchSuffix;
            var s = r.Files.TryGetValue(key, out var earlier) ? IslandScenery.FromArchive(AsuraArchive.FromBytes(earlier)) : IslandScenery.Load(path);
            foreach (var (mod, e) in group)
                try { r.Report.Add($"{mod.Id}: {group.Key}: {ApplySceneryEdit(s, e, mod.FilePath is null ? null : ModPackage.AssetDir(mod))}{(e.Note is null ? "" : $" ({e.Note})")}"); }
                catch (ArgumentException x) { r.Errors.Add($"{mod.Id}: scenery {group.Key}: {x.Message}"); }
            if (!r.Ok) return;
            var bytes = s.ToBytes();
            IslandScenery.FromArchive(AsuraArchive.FromBytes(bytes));   // must still read back
            r.Files[key] = bytes;
        }
    }

    /// <summary>Apply one scenery change; returns what it did (throws ArgumentException when it can't).</summary>
    public static string ApplySceneryEdit(IslandScenery s, SceneryEdit e, string? assetDir = null)
    {
        if (e.Group < 0 || e.Group >= s.Groups.Count) throw new ArgumentException($"no scenery group {e.Group}");
        var g = s.Groups[e.Group];
        if (e.GroupName is not null && e.GroupName != g.Name) throw new ArgumentException($"group {e.Group} is now \"{g.Name}\", not \"{e.GroupName}\" (the island changed)");
        bool In(IslandScenery.Instance i) => (e.X0 is not { } x0 || i.X >= Math.Min(x0, e.X1 ?? x0)) && (e.X1 is not { } x1 || i.X <= Math.Max(x1, e.X0 ?? x1))
                                             && (e.Z0 is not { } z0 || i.Z >= Math.Min(z0, e.Z1 ?? z0)) && (e.Z1 is not { } z1 || i.Z <= Math.Max(z1, e.Z0 ?? z1));
        string action = (e.Action ?? "hide").ToLowerInvariant();
        if (action == "mesh")
        {
            string file = Path.Combine(assetDir ?? "", e.Source ?? "");
            if (assetDir is null || e.Source is null || !File.Exists(file)) throw new ArgumentException($"mesh file {e.Source} is missing from the mod's assets folder");
            return $"{g.Name}: model replaced ({s.SetGroupObj(e.Group, File.ReadAllText(file))} vertices from {e.Source})";
        }
        var hit = s.Instances.Where(i => i.Group == e.Group && In(i)).Select(i => i.Index).ToList();
        foreach (var i in hit)
            switch (action)
            {
                case "move": s.Move(i, e.DX, e.DY, e.DZ); break;
                case "copy": s.Copy(i, e.DX, e.DY, e.DZ); break;
                default: s.Hide(i); break;
            }
        return $"{hit.Count} x {g.Name} {action switch { "move" => $"moved {e.DX},{e.DY},{e.DZ}", "copy" => $"copied to +{e.DX},{e.DY},{e.DZ}", _ => "hidden" }}";
    }

    static List<LairMap.PlacedObject>? _mapTemplates;

    /// <summary>Every pre-placed object in the game's lair maps (templates for "place").</summary>
    public static List<LairMap.PlacedObject> MapTemplates(GameData game) => _mapTemplates ??=
        Directory.GetFiles(game.Install.Full(@"envs\basedefinitions"), "lair_*.base").SelectMany(f => LairMap.Parse(File.ReadAllBytes(f)).Objects).ToList();

    /// <summary>Templates this map can take: object layouts differ between lairs (Crown Gold's objects are key 0x64 with
    /// their own inner layout, the others 0x68), and a copy from another layout crashes the game at load.</summary>
    public static IEnumerable<LairMap.PlacedObject> TemplatesFor(GameData game, LairMap map) =>
        map.Objects.Count == 0 ? Enumerable.Empty<LairMap.PlacedObject>() : MapTemplates(game).Where(o => o.Node.Key == map.Objects[0].Node.Key);

    static List<LairMap.PlacedObject>? _saveTemplates;

    /// <summary>Furniture the player has built in their EG2 saves (read only): one per item and facing, the shortest
    /// record (least live state). Saves from the current game use the same object format as every lair but Crown Gold.</summary>
    public static List<LairMap.PlacedObject> SaveTemplates(GameInstall install, Action<string> log)
    {
        if (_saveTemplates is not null) return _saveTemplates;
        var all = new List<LairMap.PlacedObject>();
        var root = install.SavesDir;
        var files = Directory.Exists(root) ? Directory.GetFiles(root, "slot*.sav", SearchOption.AllDirectories).Where(f => !Path.GetFileName(f).Equals("slot0.sav", StringComparison.OrdinalIgnoreCase)).ToList() : new();
        for (int i = 0; i < files.Count; i++)
        {
            log($"reading save {i + 1}/{files.Count}: {Path.GetFileName(files[i])}");
            try { all.AddRange(LairMap.FromArchive(SaveFile.Load(File.ReadAllBytes(files[i]))).Objects); }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or ArgumentException) { log($"  skipped: {ex.Message}"); }
        }
        return _saveTemplates = all.GroupBy(o => (o.Node.Key, o.Fnas, o.FaceX, o.FaceY)).Select(g => g.MinBy(o => o.Node.Payload().Length)!).ToList();
    }

    /// <summary>Furniture record name -> its fnas (asset set) id, as lair map objects name them.</summary>
    public static Dictionary<uint, string> FnasNames(GameData game)
    {
        var fnas = game.Objects.Where(o => o.Tag == "fnas").Select(o => o.ObjectId).ToHashSet();
        var names = new Dictionary<uint, string>();
        foreach (var rec in game.Objects.Where(o => o.IsRecord && o.Tag == "fntr"))
            for (int i = 0; i + 4 <= rec.Body.Length; i++)
                if (fnas.Contains(Bytes.U32(rec.Body, i))) names.TryAdd(Bytes.U32(rec.Body, i), rec.RecordName!);
        return names;
    }

    /// <summary>Apply one map change to a loaded map; returns what it did (throws ArgumentException when it can't).</summary>
    public static string ApplyMapEdit(GameData game, LairMap map, MapEdit e)
    {
        int x0 = Math.Min(e.X0, e.X1), y0 = Math.Min(e.Y0, e.Y1), x1 = Math.Max(e.X0, e.X1), y1 = Math.Max(e.Y0, e.Y1);
        return (e.Action ?? "tier").ToLowerInvariant() switch
        {
            "tier" => $"{map.SetTier(e.Floor, x0, y0, x1, y1, e.Tier)} rock cells -> tier {e.Tier}",
            "dig" => $"{map.Dig(e.Floor, x0, y0, x1, y1)} rock cells dug out",
            "room" => $"{map.Dig(e.Floor, x0, y0, x1, y1, LairMap.RoomType(e.Room ?? ""))} cells -> {e.Room}",
            "gold" => $"{map.Gold(e.Floor, x0, y0, x1, y1)} rock cells -> gold seam",
            "wall" => $"{map.Wall(e.Floor, x0, y0, x1, y1)} rock cells -> edge rock",
            "rock" => $"{map.Fill(e.Floor, x0, y0, x1, y1, e.Tier == 0 ? 1 : e.Tier)} cells -> tier {(e.Tier == 0 ? 1 : e.Tier)} rock",
            "place" => Place(game, map, e),
            "remove" => RemoveObjects(map, e.Floor, x0, y0, x1, y1),
            "character" => AddCharacters(map, e),
            "agent" => AddAgent(game, map, e),
            var a => throw new ArgumentException($"unknown map action '{a}'"),
        };
    }

    static string AddCharacters(LairMap map, MapEdit e)
    {
        int n = map.AddEntities((e.Template ?? "").Split(new[] { ' ', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(Convert.FromHexString).ToList());
        if (e.Squad is { Length: > 0 } sq) map.AddSquad(Agents.WithWave(Convert.FromHexString(sq), e.Wave));
        return $"{n} entities added{(e.Squad is { Length: > 0 } ? " + a squad" : "")}";
    }

    /// <summary>agent: Template = "entity companion" hex, Squad = its one-member squad, placed at column X0, row Y0.</summary>
    static string AddAgent(GameData? game, LairMap map, MapEdit e)
    {
        var parts = (e.Template ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || e.Squad is not { Length: > 0 }) throw new ArgumentException("agent: needs Template (entity + companion) and Squad");
        try
        {
            var v = (e.Vehicle ?? "0:0").Split(':');
            var (ent, comp, squad) = Agents.WithClass(Convert.FromHexString(parts[0]), Convert.FromHexString(parts[1]), Agents.WithWave(Convert.FromHexString(e.Squad), e.Wave), e.Class);
            if (e.Patrol is { Length: > 0 } route)
                squad = Agents.WithPatrol(squad, map, route.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToUInt32(x, 16)).ToList(), (byte?)e.PatrolState);
            return Agents.Place(map, Agents.WithTree(ent, e.Tree), comp, squad, e.X0, e.Y0, e.Floor,
                Convert.ToUInt32(v[0], 16), Convert.ToUInt32(v[^1], 16), game is null ? new Dictionary<uint, uint>() : Agents.IslandVehicles(game, map.LairId));
        }
        catch (FormatException x) { throw new ArgumentException($"agent: {x.Message}"); }
    }

    static string RemoveObjects(LairMap map, int floor, int x0, int y0, int x1, int y1)
    {
        var hit = map.Objects.Where(o => o.Floor == floor && o.Column >= x0 && o.Column <= x1 && o.Row >= y0 && o.Row <= y1).ToList();
        foreach (var o in hit) map.Remove(o);
        return $"{hit.Count} objects removed";
    }

    static Dictionary<uint, HashSet<uint>>? _allowedRooms;

    /// <summary>fnas -> the rooms its furniture record lists (empty = the record names none: allowed anywhere).</summary>
    public static Dictionary<uint, HashSet<uint>> AllowedRooms(GameData game)
    {
        if (_allowedRooms is not null) return _allowedRooms;
        var fnas = game.Objects.Where(o => o.Tag == "fnas").Select(o => o.ObjectId).ToHashSet();
        var rooms = LairMap.Rooms.Select(r => r.Type).ToHashSet();
        var map = new Dictionary<uint, HashSet<uint>>();
        foreach (var rec in game.Objects.Where(o => o.IsRecord && o.Tag == "fntr"))
        {
            uint? id = null;
            var set = new HashSet<uint>();
            for (int i = 0; i + 4 <= rec.Body.Length; i++)
            {
                uint v = Bytes.U32(rec.Body, i);
                if (id is null && fnas.Contains(v)) id = v;
                if (rooms.Contains(v)) set.Add(v);
            }
            if (id is { } f && !map.ContainsKey(f)) map[f] = set;
        }
        return _allowedRooms = map;
    }

    /// <summary>Why <paramref name="fnas"/> can't stand on (floor, row, column), or null when it can.</summary>
    public static string? RoomProblem(GameData game, LairMap map, uint fnas, int floor, int row, int column)
    {
        if (!AllowedRooms(game).TryGetValue(fnas, out var ok) || ok.Count == 0) return null;
        var f = map.Floors.FirstOrDefault(x => x.Index == floor);
        if (f is null || row < 0 || column < 0 || row >= f.Height || column >= f.Width) return "outside the floor";
        uint here = LairMap.TypeOf(f.Cells[row * f.Width + column]);
        return ok.Contains(here) ? null
            : $"it belongs in {string.Join(" / ", ok.Select(t => LairMap.RoomName(t)))}, and this cell is {LairMap.RoomName(here) ?? "not a room"} (build the room first)";
    }

    static string Place(GameData game, LairMap map, MapEdit e)
    {
        var names = FnasNames(game);
        if (e.Template is not null)
        {
            uint key = Convert.ToUInt32(e.TemplateKey ?? "0", 16);
            if (key != map.ObjectKey && !e.AnyVersion) throw new ArgumentException($"'{e.Item}' was copied from a save in another object format ({key:x}); this map uses {map.ObjectKey:x} (Crown Gold can only take its own objects)");
            var tr = LairMap.FromRecord(key, Convert.FromHexString(e.Template));
            if (RoomProblem(game, map, tr.Fnas, e.Floor, e.Y0, e.X0) is { } why) throw new ArgumentException($"can't place {e.Item} at column {e.X0}, row {e.Y0}: {why}");
            var sp = map.Place(tr, e.Floor, e.Y0, e.X0, e.AnyVersion);
            return $"placed {e.Item} (copied record, id {sp.Id:x}) at column {e.X0}, row {e.Y0}";
        }
        var face = (e.Facing ?? "0,1").Split(',').Select(s => int.Parse(s.Trim())).ToArray();
        var all = TemplatesFor(game, map).Where(o => names.GetValueOrDefault(o.Fnas)?.Equals(e.Item, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (all.Count == 0) throw new ArgumentException($"'{e.Item}' isn't placed in any lair map with this map's layout, so there's nothing to copy");
        var t = all.FirstOrDefault(o => o.FaceX == face[0] && o.FaceY == face[1])
                ?? throw new ArgumentException($"'{e.Item}' only exists facing {string.Join(" / ", all.Select(o => $"{o.FaceX},{o.FaceY}").Distinct())}");
        if (RoomProblem(game, map, t.Fnas, e.Floor, e.Y0, e.X0) is { } bad) throw new ArgumentException($"can't place {e.Item} at column {e.X0}, row {e.Y0}: {bad}");
        var p = map.Place(t, e.Floor, e.Y0, e.X0);
        return $"placed {e.Item} (id {p.Id:x}) at column {e.X0}, row {e.Y0}";
    }

    /// <summary>
    /// Streamed dialogue/music: one whole-store .asrpatch per touched .pc.streamsounds (HANDOFF round 27), plus the
    /// table-only .ssm copy when the store has one (its offsets point into the store).
    /// </summary>
    static void BuildStreamSounds(GameData game, List<(ModDefinition Mod, AssetReplacement A)> items, BuildResult r, Action<string> log, CancellationToken ct)
    {
        foreach (var group in items.GroupBy(x => x.A.File.Replace('/', '\\'), StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            log($"patching {group.Key}");
            string full = game.Install.Full(group.Key);
            var names = StreamSounds.ReadTable(full).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wavs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mod, a) in group)
            {
                if (!names.Contains(a.Name)) { r.Errors.Add($"streamed sound {a.File}: '{a.Name}' not found (game updated?)"); continue; }
                var data = File.ReadAllBytes(SourcePath(mod, a));
                if (AssetIndex.WavFormat(data) is null) { r.Errors.Add($"{mod.Id}: {a.Source} isn't a WAV file"); continue; }
                wavs[a.Name] = data;
                r.Report.Add($"streamed sound {a.File}: {a.Name} <- {a.Source} ({data.Length:N0} bytes)");
            }
            if (!r.Ok) return;
            var store = StreamSounds.Patch(full, wavs);
            string rel = group.Key + GameInstall.PatchSuffix;
            r.Files[rel] = store;
            const string asr = ".asr.pc.streamsounds";
            if (full.EndsWith(asr, StringComparison.OrdinalIgnoreCase) && full[..^asr.Length] + ".ssm.pc.streamsounds" is var ssm && File.Exists(ssm))
                r.Files[game.Install.Rel(ssm) + GameInstall.PatchSuffix] = StreamSounds.TableOnly(store);

            // self-check: every replaced name now holds the mod's bytes
            foreach (var e in StreamSounds.Parse(store).Where(e => wavs.ContainsKey(e.Name)))
                Check(r, store.AsSpan((int)e.Offset, (int)e.Size).SequenceEqual(wavs[e.Name]), $"{rel} {e.Name}");
        }
    }

    public const string TextureBlobTarget = @"textures\eg2modkit.pc_textures";
    /// <summary>After a build is done with: compact the large-object heap and collect, so the gigabytes a big build
    /// used go back to Windows instead of staying reserved by the app.</summary>
    public static void ReleaseMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    static bool IsStreamed(AssetReplacement a) => a.File.EndsWith(".pc_textures", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Streamed (high-res) textures: the replacements go into one new blob (TextureBlobTarget), and every .ts entry
    /// that streams a texture of the same name (from any blob) is repointed at it. The original blobs are untouched.
    /// </summary>
    static void BuildTextureStreams(GameData game, List<(ModDefinition Mod, AssetReplacement A)> items, BuildResult r, Action<string> log, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var assets = game.AllAssets(log, ct);
        var byKey = assets.ToDictionary(a => a.Key);
        var byData = assets.Where(a => a.DataOffset >= 0).ToDictionary(a => (a.File.ToLowerInvariant(), a.DataOffset));
        var byName = byData.Values.ToLookup(a => a.Name, StringComparer.OrdinalIgnoreCase);
        // The blob can be over a gigabyte, so it's written once into an array of its exact size: pass 1 sizes every
        // chunk (the original's header + the new data), pass 2 fills it. Only a hash of each texture is kept for the check.
        var sized = new List<(ModDefinition Mod, AssetReplacement A, AssetEntry E, long Size)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 8 + 4;
        foreach (var (mod, a) in items)
        {
            if (!byKey.TryGetValue(a.Slot, out var e)) { r.Errors.Add($"asset {a.File}: '{a.Name}' not found (game updated?)"); continue; }
            if (!names.Add($"{a.Island}|{a.Name}")) { r.Errors.Add($"{a.Name} is replaced twice (two copies of the same streamed texture)"); continue; }
            var orig = AssetIndex.ReadBody(game.Install, e);
            long size = 8 + orig.Length - AssetIndex.Payload("RSCF", orig).Length + new FileInfo(SourcePath(mod, a)).Length;
            sized.Add((mod, a, e, size));
            total += size;
        }
        if (!r.Ok) return;
        if (total > int.MaxValue) { r.Errors.Add($"streamed textures total {total:N0} bytes, over the 2 GB a single blob can hold here"); return; }
        var blobBytes = new byte[total];
        "Asura   "u8.CopyTo(blobBytes);
        int at = 8;
        var placed = new Dictionary<string, (uint Offset, uint Size, byte[] Hash)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mod, a, e, size) in sized)
        {
            ct.ThrowIfCancellationRequested();
            var data = File.ReadAllBytes(SourcePath(mod, a));
            var body = AssetIndex.Replace("RSCF", AssetIndex.ReadBody(game.Install, e), data);
            if (8 + body.Length != size) throw new InvalidOperationException($"{a.Name}: chunk is {8 + body.Length} bytes, sized as {size}");
            "RSCF"u8.CopyTo(blobBytes.AsSpan(at));
            BitConverter.TryWriteBytes(blobBytes.AsSpan(at + 4), (uint)body.Length + 8);
            body.CopyTo(blobBytes, at + 8);
            var spot = ((uint)(at + 8 + body.Length - data.Length), (uint)data.Length, System.Security.Cryptography.SHA1.HashData(data));
            if (a.Island is { } island)
            {   // repointed in that new lair's .ts copy only (BuildNewLairs); every blob's copy of the name, like shared ones
                if (!r.IslandTextures.TryGetValue(island, out var mine)) r.IslandTextures[island] = mine = new();
                foreach (var s in byName[a.Name]) mine[(s.File.ToLowerInvariant(), s.DataOffset)] = (spot.Item1, spot.Item2);
            }
            else placed[a.Name] = spot;
            at += (int)size;
            r.Report.Add($"streamed texture {a.Name} <- {a.Source} ({data.Length:N0} bytes)");
        }
        r.Files[TextureBlobTarget] = blobBytes;

        var streamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // a new lair's .ts that an earlier install put there is ModKit's, not the game's: BuildNewLairs copies it afresh
        var ours = Installer.Current(game.Install.Root)?.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new();
        foreach (var ts in Directory.EnumerateFiles(game.Install.Root, "*.ts", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) && !ours.Contains(game.Install.Rel(f))))
        {
            ct.ThrowIfCancellationRequested();
            var arc = AsuraArchive.Load(ts);
            if (arc.First("TXST") is not { } chunk) continue;
            var t = TextureTable.Parse(chunk.Body);
            int moved = 0;
            foreach (var w in t.Entries)
            {
                if (w[TextureTable.Blob] >= t.Blobs.Count
                    || !byData.TryGetValue((t.Blobs[(int)w[TextureTable.Blob]].ToLowerInvariant(), w[TextureTable.Offset]), out var src)
                    || !placed.TryGetValue(src.Name, out var p)) continue;
                if (w[TextureTable.Size] != src.Size) { r.Errors.Add($"{game.Install.Rel(ts)}: entry for {src.Name} has size {w[TextureTable.Size]}, blob has {src.Size}; .ts layout assumption broken"); continue; }
                int idx = t.Blobs.FindIndex(n => n.Equals(TextureBlobTarget, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) { t.Blobs.Add(TextureBlobTarget); idx = t.Blobs.Count - 1; }
                w[TextureTable.Blob] = (uint)idx;
                w[TextureTable.Offset] = p.Offset;
                w[TextureTable.Size] = p.Size;
                streamed.Add(src.Name);
                moved++;
            }
            if (moved == 0) continue;
            chunk.Body = t.ToBytes();
            string rel = game.Install.Rel(ts) + GameInstall.PatchSuffix;
            r.Files[rel] = arc.ToBytes();
            r.Report.Add($"{game.Install.Rel(ts)}: {moved} texture entr{(moved == 1 ? "y" : "ies")} now point at {TextureBlobTarget}");

            // self-check: every repointed entry lands on its replacement's bytes in the new blob
            var back = TextureTable.Parse(AsuraArchive.FromBytes(r.Files[rel]).First("TXST")!.Body);
            foreach (var w in back.Entries.Where(w => back.Blobs[(int)w[TextureTable.Blob]].Equals(TextureBlobTarget, StringComparison.OrdinalIgnoreCase)))
                Check(r, placed.Values.Any(p => p.Offset == w[TextureTable.Offset] && p.Size == w[TextureTable.Size]
                                               && System.Security.Cryptography.SHA1.HashData(blobBytes.AsSpan((int)p.Offset, (int)p.Size)).AsSpan().SequenceEqual(p.Hash)), $"{rel} entry");
        }
        foreach (var name in placed.Keys.Where(n => !streamed.Contains(n)))
            r.Errors.Add($"no .ts table streams {name}, so the game would never load the replacement");
    }

    /// <summary>A new item's texture entry that replaces its build-menu icon (any GUI icon path).</summary>
    static bool IsIcon(ArtTexture t) => t.Texture.Contains(@"\gui\icons\", StringComparison.OrdinalIgnoreCase);

    static string SourcePath(ModDefinition m, AssetReplacement a) => Path.Combine(ModPackage.AssetDir(m), a.Source);

    /// <summary>DDS for an own-art texture copy: the mod's replacement, else the game's high-res streamed copy, else null (keep the package copy).</summary>
    static byte[]? TextureData(GameData game, ModDefinition mod, NewFurniture item, string texture, Action<string> log, CancellationToken ct)
    {
        uint h = FurnitureArt.TextureHash(texture);
        return item.Textures.FirstOrDefault(t => FurnitureArt.TextureHash(t.Texture) == h) is { } mine
            ? File.ReadAllBytes(Path.Combine(ModPackage.AssetDir(mod), mine.Source))
            : FurnitureArt.HighRes(game, texture, log, ct);
    }

    static void ApplyAssets(AsuraArchive arc, List<(ModDefinition Mod, AssetReplacement A)> items, BuildResult r)
    {
        if (items.Count == 0) return;
        var targets = items.Select(x => x.A).ToList();
        var done = new HashSet<AssetReplacement>();
        foreach (var (chunk, a) in AssetIndex.Match(arc.Chunks, targets).ToList())
        {
            if (!done.Add(a)) continue;
            var data = File.ReadAllBytes(SourcePath(items.First(x => x.A == a).Mod, a));
            if (a.Tag == "RSCF" && MeshGeometry.IsMesh(chunk.Body) && !a.Name.Contains('#')) UpdateBounds(arc, a.Name, AssetIndex.Payload("RSCF", chunk.Body), data, r);
            chunk.Body = AssetIndex.Replace(a.Tag, chunk.Body, data);
            r.Report.Add($"asset {a.File}: {a.Name} <- {a.Source} ({data.Length:N0} bytes)");
            if (AssetIndex.WavFormat(data) is { } fmt and not 2 && !r.Report.Any(l => l.StartsWith($"warning: {a.Source} ")))
                r.Report.Add($"warning: {a.Source} is WAV format {fmt}; the game's own sounds are MS-ADPCM (2). Other formats are untested.");
        }
        foreach (var a in targets.Where(t => !done.Contains(t)))
            r.Errors.Add($"asset {a.File}: '{a.Name}' (#{a.Occurrence}) not found (game updated?)");
    }

    /// <summary>A replaced LOD0 mesh also moves its HSBB bounds, when those were simply the old mesh's bounds.</summary>
    static void UpdateBounds(AsuraArchive arc, string meshName, byte[] oldPayload, byte[] newPayload, BuildResult r)
    {
        var box = arc.Find("HSBB").FirstOrDefault(c => AssetIndex.NameOf("HSBB", c.Body).Equals(meshName, StringComparison.OrdinalIgnoreCase));
        if (box is null) return;
        try
        {
            if (MeshGeometry.UpdatedBounds(box.Body, MeshGeometry.Parse(oldPayload), MeshGeometry.Parse(newPayload)) is { } b)
            { box.Body = b; r.Report.Add($"bounds of {meshName} updated to the new mesh"); }
            else r.Report.Add($"warning: bounds of {meshName} were set by hand in the game (several boxes or clipped), kept as they are");
        }
        catch (InvalidDataException e) { r.Errors.Add($"mesh {meshName}: {e.Message}"); }
    }

    static void CheckAssets(AsuraArchive back, List<(ModDefinition Mod, AssetReplacement A)> items, BuildResult r)
    {
        if (items.Count == 0) return;
        var found = AssetIndex.Match(back.Chunks, items.Select(x => x.A).ToList()).ToList();
        foreach (var (mod, a) in items)
        {
            var hit = found.FirstOrDefault(f => f.Target == a).Chunk;
            Check(r, hit is not null && AssetIndex.Payload(a.Tag, hit.Body).AsSpan().SequenceEqual(File.ReadAllBytes(SourcePath(mod, a))), $"asset {a.Name}");
        }
    }

    static Chunk? FindObject(AsuraArchive arc, FieldEdit f, BuildResult r)
    {
        string hex = f.Object.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? f.Object[2..] : f.Object;
        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint id))
        { r.Errors.Add($"field {f.Package}/{f.Tag}: object must be a hex id, got '{f.Object}'"); return null; }
        var hits = arc.Find(f.Tag).Where(c => c.Body.Length >= ObjectHeader.Size && Bytes.U32(c.Body, 8) == id).ToList();
        if (hits.Count != 1) { r.Errors.Add($"field {f.Package}/{f.Tag}/{f.Object}: {hits.Count} matching objects (need exactly 1)"); return null; }
        return hits[0];
    }

    static void BuildTextPatches(GameData game, Dictionary<string, Dictionary<string, string>> text, BuildResult r,
                                 Action<string> log, CancellationToken ct)
    {
        int tn = 0;
        foreach (var (table, entries) in text)
        {
            ct.ThrowIfCancellationRequested();
            tn++;
            if (table == OwnTextTable) { BuildOwnTextTable(game, entries, r); continue; }
            log($"patching text table {tn}/{text.Count}: {table}");
            string path = game.Install.TextFile(table, game.Language);
            var arc = AsuraArchive.Load(path);
            var tables = arc.Find("HTXT").Select(c => (Chunk: c, Table: TextTable.Parse(c.Body))).ToList();
            if (tables.Count == 0) { r.Errors.Add($"{table}: no HTXT chunk"); continue; }
            foreach (var (key, value) in entries)
            {
                var target = tables.FirstOrDefault(x => x.Table.Get(key) is not null);
                r.Report.Add($"text {table}/{key}: {(target.Table is null ? "added" : "changed")}");
                (target.Table ?? tables[0].Table).Set(key, value);
            }
            foreach (var (c, t) in tables) c.Body = t.ToBytes();
            // whole-file replacement; uncompressed is proven for text patches
            string rel = game.Install.Rel(path) + GameInstall.PatchSuffix;
            r.Files[rel] = arc.ToBytes(compressed: false);

            var back = AsuraArchive.FromBytes(r.Files[rel]).Find("HTXT").Select(c => TextTable.Parse(c.Body)).ToList();
            Check(r, entries.All(kv => back.Any(t => t.Get(kv.Key) is { } e && e.Text == kv.Value && e.Hash == TextTable.KeyHash(kv.Key))),
                  $"{table} text");
        }
    }

    static void BuildOwnTextTable(GameData game, Dictionary<string, string> entries, BuildResult r)
    {
        var t = new TextTable { TableName = OwnTextTable };
        foreach (var (key, value) in entries) t.Add(key, value);
        var arc = new AsuraArchive { Compressed = false };
        arc.Chunks.Add(new Chunk("HTXT", t.ToBytes()));
        r.Files[$@"text\pc\{OwnTextTable}\{OwnTextTable}.asr_{game.Language}"] = arc.ToBytes();
        // register it in the table list (the base list is compressed; so is the patch)
        string loc = Path.Combine("text", "pc", "localisation.asr");
        var list = AsuraArchive.Load(game.Install.Full(loc));
        var nameField = new byte[Bytes.Pad4(OwnTextTable.Length + 1)];
        Bytes.Latin1.GetBytes(OwnTextTable.ToUpperInvariant()).CopyTo(nameField, 0);
        list.Chunks.Add(new Chunk("HTPR", Bytes.Concat(Bytes.Le(1u, 0u), nameField)));
        r.Files[loc + GameInstall.PatchSuffix] = list.ToBytes(compressed: true);
        r.Report.Add($"text: {entries.Count} texts in ModKit's own table text\\pc\\{OwnTextTable} (registered in localisation.asr)");
    }

    /// <summary>Folder next to the ModKit binaries holding the prebuilt xinput1_4.dll proxy (built from native\winmm).</summary>
    public static string RuntimeDll => Path.Combine(AppContext.BaseDirectory, "runtime", "xinput1_4.dll");
    public const string RuntimeDllTarget = @"bin\xinput1_4.dll", RuntimeConfigTarget = @"bin\eg2modkit.cfg", RuntimeLog = @"bin\eg2modkit.log";
    /// <summary>Replacement bytes the runtime DLL swaps into game files as they're read (kind = file_block).</summary>
    public const string RuntimeBlocksDir = @"bin\eg2modkit_blocks";

    /// <summary>xinput1_4.dll proxy + eg2modkit.cfg for every runtime patch (first mod wins on identical duplicates).</summary>
    static void BuildRuntime(IReadOnlyList<ModDefinition> mods, BuildResult r)
    {
        var patches = mods.SelectMany(m => m.Runtime.Select(p => (Mod: m.Id, Patch: p)))
            .DistinctBy(x => x.Patch.Name, StringComparer.OrdinalIgnoreCase).ToList();
        bool maps = mods.Any(m => m.MapEdits.Count > 0 || m.SkipVideos.Count > 0 || m.SceneryEdits.Count > 0 || m.NewLairs.Count > 0 || m.NewObjects.Any(o => o.Tag == "felr"));
        // the DLL redirects lair maps / videos to their .asrpatch; with new islands or lairs its log shows which lair loads
        if (patches.Count == 0 && !maps && r.FileBlocks.Count == 0) return;
        if (!File.Exists(RuntimeDll)) { r.Errors.Add($@"runtime DLL missing: {RuntimeDll} (an antivirus may have quarantined it; or run modkit\native\winmm\build.cmd, then rebuild ModKit)"); return; }
        if (Installer.Unreadable(RuntimeDll) is { } why) { r.Errors.Add("runtime DLL: " + why); return; }
        var cfg = new StringBuilder("# generated by Eg2 ModKit -- read by bin\\xinput1_4.dll at game start\n\n");
        foreach (var (mod, p) in patches)
        {
            cfg.Append($"# from mod {mod}\n").Append(p.ToConfig()).Append('\n');
            r.Report.Add($"runtime {p.Name} = {p.Value} ({mod})");
        }
        foreach (var (file, offset, source) in r.FileBlocks)
            cfg.Append($"# a changed compressed block of {file}, swapped in as the game reads it\n[block {file} {offset}]\nkind = file_block\nvalue = {file}\noffset = {offset}\nsource = {Path.GetFileName(source)}\n\n");
        foreach (var n in mods.SelectMany(m => m.NewLairs).Where(n => n.KeepId))
            cfg.Append($"# new lair {n.Stem}: its own island, though its settings name {n.From}'s\n[island {n.Stem}]\nkind = island_alias\nvalue = {n.From.ToLowerInvariant()}\nstem = {n.Stem.ToLowerInvariant()}\n{(r.LairRegions.TryGetValue(n.Stem, out var reg) ? $"region = 0x{reg:x8}\n" : "")}\n");
        r.Files[RuntimeDllTarget] = File.ReadAllBytes(RuntimeDll);
        r.Files[RuntimeConfigTarget] = Encoding.ASCII.GetBytes(cfg.ToString());
        r.Report.Add("note: runtime tweaks apply while the game runs; the files are removed by Uninstall");
    }

    static void Check(BuildResult r, bool ok, string what)
    {
        if (!ok) r.Errors.Add($"self-check failed: {what}");
    }
}
