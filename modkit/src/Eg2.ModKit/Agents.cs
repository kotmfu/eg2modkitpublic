using System.Text.Json;
using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Enemy agents for scenario maps (HANDOFF round 59): copies of live agents, each with its own squad (a character
/// without a squad stands still and can't be targeted). Templates come with ModKit (data\agents.json) or from the
/// player's saves.
/// Entity: [u32 1][u32 0][u32 id][u32 kind 0x8004] + props; type hash @56; position (x, height, z) floats @80, with more
/// copies (target, path) nearby; its 0x8007 companion holds x/z too; one link to an island entity: the transport
/// vehicle it came in (kind 0x800b in envs\&lt;lair&gt;.pc, its type after the first prop 0x0d header). Grid: row = x,
/// column = z; heights per floor in <see cref="Heights"/>. Squad: see <see cref="LairMap.AddSquad"/>.
/// </summary>
public static class Agents
{
    public sealed record Template(string Source, uint Lair, uint Type, string Kind, float Vitality, float X, float Y, float Z,
                                  string Entity, string Companion, string Squad, uint Vehicle, uint VehicleType)
    {
        public int Floor => FloorOf(Y);
    }

    /// <summary>Floor -> standing height, from minions in saves of four islands (floor 1 not seen).</summary>
    public static readonly IReadOnlyDictionary<int, float> Heights = new Dictionary<int, float> { [0] = 20, [2] = 0, [3] = -14, [4] = -28, [5] = -52 };

    public static int FloorOf(float y) => Heights.MinBy(h => Math.Abs(h.Value - y)).Key;

    static float F(byte[] b, int at) => BitConverter.ToSingle(b, at);

    /// <summary>Story waves (misc\common.asr BLUE "Waves" tree) with DisableImmuneAutoLeaves = 1 and WaveTargetType 0, no DLC,
    /// by the class they spawn: their agents stay killable when they decide to leave, unlike heat-raid waves'.</summary>
    public static readonly IReadOnlyDictionary<string, uint> StoryWaves = new Dictionary<string, uint>
    {
        ["Soldier"] = 0xd4414112,        // KingSolomonsMineshaft soldiers
        ["Investigator"] = 0xd49a2d2e,   // ColombianEmerald investigators
    };

    // ------------------------------------------------------------------ world state

    const uint WorldStateKind = 0x8009, WaveEvent = 0x1ee01607;

    /// <summary>
    /// Gives every squad on the map a raid record in the world state (ENTI kind 0x8009), as a real raid has: without one,
    /// placed agents turn unkillable once the game registers a raid of its own. A .base has no world state, so the first
    /// call adds one (a fresh Caine Key game's, embedded as data\worldstate.bin, under a new id). Returns the records added.
    /// <para>Layout: [1][0][id][0x8009] prop 0x10 { prop (regions), u32, prop 0xf { [u32 n agencies] + n x (9-byte header,
    /// agency, ...), prop 3 (current heat raid) }, event block [u32 size][u32 2][u32 count] + events, 11-byte trailer }.
    /// A raid record is event 0x1ee01607 { prop 3 (55 bytes): prop 6 [1][f32 0][0][spawn time 0][01], wave, prop 1
    /// [0][agency], squad id, state 5 (in the lair) }.</para>
    /// </summary>
    public static int AddRaidRecords(LairMap map)
    {
        var squads = map.Squads();
        if (squads.Count == 0) return 0;
        var ws = map.Entities.FirstOrDefault(e => Bytes.U32(e, 12) == WorldStateKind);
        bool fresh = ws is null;
        if (ws is null)
        {
            using var s = typeof(Agents).Assembly.GetManifestResourceStream("worldstate.bin") ?? throw new InvalidOperationException("worldstate.bin is not built into ModKit");
            ws = new byte[s.Length];
            s.ReadExactly(ws);
            Bytes.PutU32(ws, 8, map.MaxId() + 1);
        }
        if (Bytes.U32(ws, 16) != 0x80000010) throw new InvalidDataException("world state: no prop 0x10");
        int p = 29;
        p += 9 + (int)Bytes.U32(ws, p + 5) + 4;
        if (Bytes.U32(ws, p) != 0x8000000f) throw new InvalidDataException("world state: no prop 0xf");
        int fp = p + 9, block = fp + (int)Bytes.U32(ws, p + 5);
        var agencies = new HashSet<uint>();
        for (int i = 0, n = (int)Bytes.U32(ws, fp), q = fp + 4; i < n; i++, q += 9 + (int)Bytes.U32(ws, q + 5)) agencies.Add(Bytes.U32(ws, q + 9));
        int size = (int)Bytes.U32(ws, block), count = (int)Bytes.U32(ws, block + 8), end = block + size;
        var recorded = new HashSet<uint>();
        for (int i = 0, q = block + 12; i < count; i++, q += 13 + (int)Bytes.U32(ws, q + 9))
            if (Bytes.U32(ws, q) == WaveEvent) recorded.Add(Bytes.U32(ws, q + 60));

        var records = new List<byte[]>();
        foreach (var sq in squads.Where(s => !recorded.Contains(Bytes.U32(s, 1))))
        {
            uint agency = Enumerable.Range(0, sq.Length - 3).Select(i => Bytes.U32(sq, i)).FirstOrDefault(agencies.Contains);
            if (agency == 0) throw new ArgumentException($"squad {Bytes.U32(sq, 1):x}: names no agency the world state knows");
            var body = Bytes.Concat(Bytes.Le(0x80000006), new byte[1], Bytes.Le(17u, 1u), BitConverter.GetBytes(0f), Bytes.Le(0u, 0u), new byte[] { 1 },
                                    Bytes.Le(Bytes.U32(sq, 31)), Bytes.Le(0x80000001), new byte[1], Bytes.Le(8u, 0u, agency), Bytes.Le(Bytes.U32(sq, 1), 5u));
            records.Add(Bytes.Concat(Bytes.Le(WaveEvent, 0x80000003), new byte[1], Bytes.Le((uint)body.Length), body));
        }
        if (records.Count == 0) return 0;
        var add = Bytes.Concat(records.ToArray());
        var outp = Bytes.Concat(ws[..end], add, ws[end..]);
        Bytes.PutU32(outp, block, (uint)(size + add.Length));
        Bytes.PutU32(outp, block + 8, (uint)(count + records.Count));
        Bytes.PutU32(outp, 21, Bytes.U32(ws, 21) + (uint)add.Length);
        if (fresh) map.AddEntities(new[] { outp }); else map.SetEntity(Bytes.U32(outp, 8), outp);
        return records.Count;
    }

    /// <summary>A squad entry ([01][u32 id][u32 1] + prop 0x3eb { prop 0x28 { [u32 squad id][u32 wave] ...) with its wave
    /// set to <paramref name="wave"/> (hex); unchanged when that's null or empty.</summary>
    public static byte[] WithWave(byte[] entry, string? wave)
    {
        if (wave is not { Length: > 0 }) return entry;
        if (entry.Length < 35 || entry[0] != 1 || Bytes.U32(entry, 9) != 0x800003eb || Bytes.U32(entry, 18) != 0x80000028)
            throw new ArgumentException("wave: the squad entry isn't [01][id][1] + prop 0x3eb { prop 0x28 }");
        var e = (byte[])entry.Clone();
        Bytes.PutU32(e, 31, Convert.ToUInt32(wave, 16));
        return e;
    }

    /// <summary>An agent entity, its companion and squad with the template's character class (entity @56) replaced by
    /// <paramref name="cls"/> (hex) wherever it appears (entity, squad member records); unchanged when that's empty.</summary>
    public static (byte[] Entity, byte[] Companion, byte[] Squad) WithClass(byte[] entity, byte[] companion, byte[] squad, string? cls)
    {
        if (cls is not { Length: > 0 }) return (entity, companion, squad);
        uint from = Bytes.U32(entity, 56), to = Convert.ToUInt32(cls, 16);
        var r = (Entity: (byte[])entity.Clone(), Companion: (byte[])companion.Clone(), Squad: (byte[])squad.Clone());
        foreach (var b in new[] { r.Entity, r.Companion, r.Squad }) Replace(b, new HashSet<uint> { from }, to);
        return r;
    }

    /// <summary>A character entity with its running behaviour tree set to <paramref name="tree"/> (hex); unchanged when that's
    /// empty. Characters store the tree they run at @240 after a u32 999 (saved agents keep it: the class's DefaultBT
    /// only applies at spawn). Minions store 0x00313fd4 (EG Idle) there.</summary>
    /// <summary>A tree of one plain Idle step: an agent running it stands where it's placed and still fights.</summary>
    public const string GuardTree = "b37c4130";

    public static byte[] WithTree(byte[] entity, string? tree)
    {
        if (tree is not { Length: > 0 }) return entity;
        if (entity.Length < 244 || Bytes.U32(entity, 236) != 999) throw new ArgumentException("tree: the entity has no behaviour tree at @240");
        var e = (byte[])entity.Clone();
        Bytes.PutU32(e, 240, Convert.ToUInt32(tree, 16));
        return e;
    }

    const uint DefaultBT = 0xa6775653, TreeType = 0x81b4b921;

    /// <summary>
    /// A class tree BLUE chunk with class <paramref name="cls"/>'s DefaultBT set to <paramref name="tree"/>, same size;
    /// null when the chunk has no such object, or the class neither sets DefaultBT itself nor has a 4-byte override equal
    /// to its parent's (which then becomes the DefaultBT member).
    /// <para>BLUE: [1][0][1][root][u32 count] + objects [id][0x0d][id][parent][u32 n] + n members [key][flag][type][0]
    /// [kind] + value (kind 0, 1, 3: 4 bytes; 2: 1 byte; 4: [u32 len] + chars).</para>
    /// </summary>
    public static byte[]? WithDefaultTree(byte[] blue, uint cls, uint tree)
    {
        if (blue.Length < 20 || Bytes.U32(blue, 0) != 1) return null;
        var objs = new Dictionary<uint, (uint Parent, List<(uint Key, int At, uint Kind)> Members)>();
        try
        {
            for (int i = 0, n = (int)Bytes.U32(blue, 16), p = 20; i < n; i++)
            {
                if (Bytes.U32(blue, p + 4) != 0x0d) return null;
                uint id = Bytes.U32(blue, p), parent = Bytes.U32(blue, p + 12);
                int m = (int)Bytes.U32(blue, p + 16);
                p += 20;
                var members = new List<(uint, int, uint)>();
                for (int k = 0; k < m; k++)
                {
                    uint kind = Bytes.U32(blue, p + 16);
                    members.Add((Bytes.U32(blue, p), p, kind));
                    p += 20 + kind switch { 0 or 1 or 3 => 4, 2 => 1, 4 => 4 + (int)Bytes.U32(blue, p + 20), _ => throw new InvalidDataException() };
                }
                objs.TryAdd(id, (parent, members));
            }
        }
        catch (Exception x) when (x is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException) { return null; }
        if (!objs.TryGetValue(cls, out var obj)) return null;
        var outp = (byte[])blue.Clone();
        if (obj.Members.FirstOrDefault(x => x.Key == DefaultBT) is { At: > 0 } own) { Bytes.PutU32(outp, own.At + 20, tree); return outp; }
        uint? Inherited(uint key, uint from)
        {
            for (uint o = from; objs.TryGetValue(o, out var x); o = x.Parent)
                if (x.Members.FirstOrDefault(y => y.Key == key) is { At: > 0 } hit) return hit.Kind is 0 or 1 or 3 ? Bytes.U32(blue, hit.At + 20) : null;
            return null;
        }
        // ponytail: reuses a redundant override; a class without one would need the chunk to grow (it can't: common.asr is memory-mapped)
        foreach (var (key, at, kind) in obj.Members)
        {
            if (kind is not (0 or 1 or 3) || Bytes.U32(blue, at + 4) != 3 || Inherited(key, obj.Parent) != Bytes.U32(blue, at + 20)) continue;
            Bytes.PutU32(outp, at, DefaultBT); Bytes.PutU32(outp, at + 8, TreeType); Bytes.PutU32(outp, at + 16, 3); Bytes.PutU32(outp, at + 20, tree);
            return outp;
        }
        return null;
    }

    /// <summary>
    /// A one-member squad set up the way Divers patrol: the member's target furniture types (record +36 after its current
    /// targets: [u32 m][u32 16] + m x fnas) are the types of <paramref name="objects"/> (grid object ids on the map), and
    /// the squad's target list (prop 0x28's prop 4: [u32 n] + n x ([01] + prop 0xa {u32 index, u32 object id, u32 fnas,
    /// u32 0, u32 1, u32 flags 0, u32 member, u32 999, u32 0})) names those objects. <paramref name="state"/> sets the
    /// member's state byte (Soldiers 7, Divers 8) when given. Works with a wave of WaveTargetType 2.
    /// </summary>
    public static byte[] WithPatrol(byte[] entry, LairMap map, IReadOnlyList<uint> objects, byte? state = null)
    {
        var byId = map.Objects.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
        var missing = objects.Where(o => !byId.ContainsKey(o)).ToList();
        if (missing.Count > 0) throw new ArgumentException($"patrol: no object {string.Join(", ", missing.Select(m => m.ToString("x")))} on this map");
        var (p3eb, lists) = Parse(entry);
        var mem = (RawNode)lists[0].Children[0];
        if (Bytes.U32(mem.Data, 0) != 1) throw new ArgumentException("patrol: the squad must have one member");
        var rec = Records(entry)[0];
        int at = rec.Start + 32;
        at += 4 + 9 * (int)Bytes.U32(mem.Data, at);
        int m = (int)Bytes.U32(mem.Data, at);
        if (Bytes.U32(mem.Data, at + 4) != 16) throw new ArgumentException("patrol: the member record isn't the layout we know");
        var types = objects.Select(o => byId[o].Fnas).Distinct().ToList();
        var tail = mem.Data[(at + 8 + 4 * m)..];
        if (state is { } st) tail[8] = st;   // f32, f32, u8 state
        mem.Data = Bytes.Concat(mem.Data[..at], Bytes.Le((uint)types.Count, 16u), Bytes.Le(types.ToArray()), tail);

        var p28 = p3eb.Children.OfType<Prop>().First(p => p.Id == 0x28);
        int i4 = p28.Children.FindIndex(c => c is Prop { Id: 4 });
        if (i4 < 0) throw new ArgumentException("patrol: the squad has no target list");
        var targets = objects.Select((o, k) => Bytes.Concat(new byte[] { 1 }, Bytes.Le(0x8000000a), new byte[1], Bytes.Le(36u,
            (uint)k, o, byId[o].Fnas, 0u, 1u, 0u, rec.Id, 999u, 0u)));
        var old = (Prop)p28.Children[i4];
        p28.Children[i4] = new Prop(old.Key, old.Kind, new List<PropNode> { new RawNode(Bytes.Concat(targets.Prepend(Bytes.Le((uint)objects.Count)).ToArray())) });
        return Bytes.Concat(entry[..9], p3eb.ToBytes());
    }

    // ------------------------------------------------------------------ templates

    static List<Template>? _bundled, _saved;

    /// <summary>The agents that come with ModKit (Eg2.ModKit\data\agents.json, embedded; empty when it wasn't built in).</summary>
    public static List<Template> Bundled()
    {
        if (_bundled is not null) return _bundled;
        using var s = typeof(Agents).Assembly.GetManifestResourceStream("agents.json");
        try { _bundled = s is null ? new() : JsonSerializer.Deserialize<List<Template>>(s) ?? new(); }
        catch (JsonException) { _bundled = new(); }
        return _bundled;
    }

    /// <summary>Every live agent in a squad in the player's saves (read only), the fittest per (island, type, floor).</summary>
    public static List<Template> FromSaves(GameData game, Action<string> log)
    {
        if (_saved is not null) return _saved;
        var all = new List<Template>();
        var root = game.Install.SavesDir;
        var files = Directory.Exists(root) ? Directory.GetFiles(root, "slot*.sav", SearchOption.AllDirectories).Where(f => !Path.GetFileName(f).Equals("slot0.sav", StringComparison.OrdinalIgnoreCase)).ToList() : new();
        var classes = game.Objects.Where(o => o.Tag == "rsbs").ToList();
        string Kind(uint type) => classes.FirstOrDefault(o => o.Body.AsSpan().IndexOf(BitConverter.GetBytes(type)) >= 0)?.Name.Trim('"') is { Length: > 0 } n && n != "cAc2"
            ? n.TrimEnd('s') : $"Agent {type:x8}";
        for (int i = 0; i < files.Count; i++)
        {
            log($"reading save {i + 1}/{files.Count}: {Path.GetFileName(files[i])}");
            try
            {
                var save = LairMap.FromArchive(SaveFile.Load(File.ReadAllBytes(files[i])));
                all.AddRange(FromSave(save, Path.GetFileName(files[i]), Kind, IslandVehicles(game, save.LairId)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or ArgumentException) { log($"  skipped: {ex.Message}"); }
        }
        return _saved = all.GroupBy(t => (t.Lair, t.Type, t.Floor)).Select(g => g.MaxBy(t => t.Vitality)!).ToList();
    }

    public static List<Template> FromSave(LairMap save, string name, Func<uint, string> kind, IReadOnlyDictionary<uint, uint> vehicles)
    {
        var ents = save.Entities.GroupBy(e => Bytes.U32(e, 8)).ToDictionary(g => g.Key, g => g.First());
        var list = new List<Template>();
        foreach (var entry in save.Squads())
        {
            var members = Members(entry);
            foreach (uint id in members)
            {
                if (!ents.TryGetValue(id, out var e) || e.Length < 100 || Bytes.U32(e, 12) != 0x8004) continue;
                float vit = Vitality(e);
                if (vit <= 0) continue;
                uint comp = 0, vehicle = 0;
                for (int i = 16; i + 4 <= e.Length; i++)
                {
                    uint u = Bytes.U32(e, i);
                    if (comp == 0 && ents.TryGetValue(u, out var c) && Bytes.U32(c, 12) == 0x8007) comp = u;
                    if (vehicle == 0 && vehicles.ContainsKey(u)) vehicle = u;
                }
                if (comp == 0) continue;
                uint type = Bytes.U32(e, 56);
                list.Add(new Template(name, save.LairId, type, kind(type), vit, F(e, 80), F(e, 84), F(e, 88),
                    Convert.ToHexString(e), Convert.ToHexString(ents[comp]), Convert.ToHexString(SquadOf(entry, id, members)),
                    vehicle, vehicles.GetValueOrDefault(vehicle)));
            }
        }
        return list;
    }

    /// <summary>Vitality: first raw of prop /0x13, f32 @6 (not aligned).</summary>
    public static float Vitality(byte[] entity)
    {
        var p13 = PropStream.Parse(entity, 16).OfType<Prop>().FirstOrDefault(p => p.Id == 0x13);
        return p13?.Children.OfType<RawNode>().FirstOrDefault() is { Data.Length: >= 10 } r ? BitConverter.ToSingle(r.Data, 6) : 0;
    }

    // ------------------------------------------------------------------ islands

    static readonly Dictionary<uint, Dictionary<uint, uint>> _vehicles = new();

    /// <summary>An island's transport vehicles (entity id -> vehicle type), found through the lair map whose id matches.</summary>
    public static Dictionary<uint, uint> IslandVehicles(GameData game, uint lairId)
    {
        if (_vehicles.TryGetValue(lairId, out var v)) return v;
        v = new();
        var dir = game.Install.Full(@"envs\basedefinitions");
        foreach (var f in Directory.Exists(dir) ? Directory.GetFiles(dir, "lair_*.base") : Array.Empty<string>())
        {
            string pc = game.Install.Full($@"envs\{Path.GetFileNameWithoutExtension(f)}.pc");
            if (!File.Exists(pc)) continue;
            try
            {
                if (AsuraArchive.Load(f).First("bsnf") is not { Body.Length: >= 21 } bsnf || Bytes.U32(bsnf.Body, 17) != lairId) continue;
                foreach (var c in AsuraArchive.Load(pc).Chunks.Where(c => c.Tag == "ENTI" && c.Body.Length > 64 && Bytes.U32(c.Body, 12) == 0x800b))
                    v[Bytes.U32(c.Body, 8)] = VehicleType(c.Body);
                break;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
        }
        return _vehicles[lairId] = v;
    }

    /// <summary>A vehicle entity's type: the u32 after its first prop 0x0d header.</summary>
    static uint VehicleType(byte[] b)
    {
        for (int i = 16; i + 13 <= Math.Min(b.Length, 120); i++)
            if (Bytes.U32(b, i) == 0x8000000d && b[i + 4] == 0) return Bytes.U32(b, i + 9);
        return 0;
    }

    // ------------------------------------------------------------------ squads

    static (Prop P3eb, List<Prop> Lists) Parse(byte[] entry)
    {
        var p3eb = (Prop)PropStream.Parse(entry, 9)[0];
        var p28 = p3eb.Children.OfType<Prop>().First(p => p.Id == 0x28);
        return (p3eb, p28.Children.OfType<Prop>().Where(p => p.Id == 1).ToList());
    }

    /// <summary>Member records in the squad's first list: u32 n, then n records starting [id][u32 0x0d][id][squad id]
    /// (81 bytes usually, longer with extra entries); returned as (id, start, end) in that list's payload.</summary>
    static List<(uint Id, int Start, int End)> Records(byte[] entry)
    {
        uint squad = Bytes.U32(entry, 1);
        var d = ((RawNode)Parse(entry).Lists[0].Children[0]).Data;
        var starts = new List<int>();
        for (int i = 4; i + 16 <= d.Length; i++)
            if (Bytes.U32(d, i + 4) == 0x0d && Bytes.U32(d, i) == Bytes.U32(d, i + 8) && Bytes.U32(d, i + 12) == squad) starts.Add(i);
        return starts.Select((s, k) => (Bytes.U32(d, s), s, k + 1 < starts.Count ? starts[k + 1] : d.Length)).ToList();
    }

    static List<uint> Members(byte[] entry) => Records(entry).Select(r => r.Id).ToList();

    /// <summary>The squad cut down to one member: his record only, no lost members, every other member id -> him.</summary>
    static byte[] SquadOf(byte[] entry, uint me, List<uint> members)
    {
        var (p3eb, lists) = Parse(entry);
        var mem = (RawNode)lists[0].Children[0];
        var rec = Records(entry).First(r => r.Id == me);
        var lost0 = ((RawNode)lists[1].Children[0]).Data.ToArray();
        mem.Data = Bytes.Concat(Bytes.Le(1u), mem.Data[rec.Start..rec.End]);
        if (lists[1].Children[0] is RawNode lost) lost.Data = Bytes.Le(0u);
        var bytes = Bytes.Concat(entry[..9], p3eb.ToBytes());
        var others = members.Concat(Enumerable.Range(0, (int)Bytes.U32(lost0, 0)).Select(k => Bytes.U32(lost0, 4 + 4 * k))).Where(x => x != me).ToHashSet();
        Replace(bytes, others, me);
        return bytes;
    }

    /// <summary>
    /// The squad made fresh for a map: its [6] (per floor: size and what it has searched) rebuilt empty for that map's
    /// floors ([u32 n][n x (height, width)][n x FF]; n counts the 1x1 floor the game adds after the real ones), and its
    /// remembered points ([u32 a][u32 n] + n x ([01] + prop 5), just after [6]) dropped.
    /// </summary>
    public static byte[] FreshSquad(byte[] entry, LairMap map, bool resetMap = true, bool dropPoints = true, (int Row, int Floor, int Column)? at = null)
    {
        var p3eb = (Prop)PropStream.Parse(entry, 9)[0];
        int i6 = p3eb.Children.FindIndex(c => c is Prop { Id: 6 });
        if (i6 < 0) return entry;
        var six = KnownArea(map, at);
        var before = Bytes.Concat(p3eb.Children.Take(i6).Select(c => c.ToBytes()).ToArray());
        var after = Bytes.Concat(p3eb.Children.Skip(i6 + 1).Select(c => c.ToBytes()).ToArray());
        int n = (int)Bytes.U32(after, 4), pos = 8;
        for (int k = 0; k < n; k++)
        {
            if (after[pos] != 1 || Bytes.U32(after, pos + 1) != 0x80000005) return entry;   // not the layout we know: leave it
            pos += 1 + 9 + (int)Bytes.U32(after, pos + 1 + 5);
        }
        var old6 = (Prop)p3eb.Children[i6];
        var payload = Bytes.Concat(before, resetMap ? new Prop(old6.Key, old6.Kind, new List<PropNode> { new RawNode(six) }).ToBytes() : old6.ToBytes(),
                                   dropPoints ? Bytes.Concat(after[..4], Bytes.Le(0u), after[pos..]) : after);
        var fresh = new Prop(p3eb.Key, p3eb.Kind, PropStream.Parse(payload, 0));
        return Bytes.Concat(entry[..9], fresh.ToBytes());
    }

    /// <summary>
    /// A squad's [6], the area it knows, built for this map: [u32 n] + n x (height, width) indexed by floor number
    /// (1x1 for floors the lair doesn't have, and one more 1x1 at the end), then per floor [u8 kind][u16 column][u16 row]
    /// entries ending FF. Kinds seen in saves: 4 room, 7 corridor, 5 other walkable ground, 6 a few door-like cells,
    /// 2 the border (rock, edge, outside). Here: the walkable area connected to the agent's cell on its floor, plus its
    /// border. An EMPTY map left agents unkillable after their first hit (squad-reset-probe).
    /// </summary>
    static byte[] KnownArea(LairMap map, (int Row, int Floor, int Column)? at)
    {
        int top = map.Floors.Max(f => f.Index);
        var dims = Enumerable.Range(0, top + 1).Select(i => map.Floors.FirstOrDefault(f => f.Index == i) is { } f ? (f.Height, f.Width) : (1, 1)).Append((1, 1)).ToList();
        var outp = new List<byte[]> { Bytes.Le((uint)dims.Count) };
        outp.AddRange(dims.Select(d => Bytes.Le((uint)d.Item1, (uint)d.Item2)));
        for (int i = 0; i < dims.Count; i++)
        {
            if (at is { } a && a.Floor == i && map.Floors.FirstOrDefault(f => f.Index == i) is { } fl)
                foreach (var (kind, c, r) in Flood(fl, a.Row, a.Column, KnownRadius))
                    outp.Add(Bytes.Concat(new[] { kind }, BitConverter.GetBytes((ushort)c), BitConverter.GetBytes((ushort)r)));
            outp.Add(new byte[] { 0xff });
        }
        return Bytes.Concat(outp.ToArray());
    }

    /// <summary>
    /// How far (cells, walking) a placed agent's squad already "knows" round it. The known area reads as searched: the
    /// whole connected area sent Investigators home at once, an empty one left agents unkillable after a hit.
    /// </summary>
    public static int KnownRadius = 3;

    static IEnumerable<(byte Kind, int Column, int Row)> Flood(LairMap.Floor f, int row, int col, int radius)
    {
        byte? KindAt(int r, int c)
        {
            var cell = f.Cells[r * f.Width + c];
            uint t = LairMap.TypeOf(cell);
            if (LairMap.TierOf(cell) > 0 || t is 0x430bd860 or 0x0cf2f3d8 or 0x080b0101 or 0x48f218d8) return null;   // rock, outside, edge, lift, gold
            return LairMap.RoomName(t) switch { null => 5, "Corridor" => 7, _ => 4 };
        }
        if (row < 0 || col < 0 || row >= f.Height || col >= f.Width || KindAt(row, col) is null) yield break;
        var seen = new HashSet<(int, int)> { (row, col) };
        var border = new HashSet<(int, int)>();
        var queue = new Queue<(int R, int C)>();
        queue.Enqueue((row, col));
        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            yield return (KindAt(r, c)!.Value, c, r);
            foreach (var (nr, nc) in new[] { (r - 1, c), (r + 1, c), (r, c - 1), (r, c + 1) })
            {
                if (nr < 0 || nc < 0 || nr >= f.Height || nc >= f.Width || seen.Contains((nr, nc))) continue;
                seen.Add((nr, nc));
                if (KindAt(nr, nc) is null) border.Add((nr, nc));
                else if (Math.Abs(nr - row) + Math.Abs(nc - col) <= radius) queue.Enqueue((nr, nc));
            }
        }
        foreach (var (r, c) in border) yield return (2, c, r);
    }

    static void Replace(byte[] b, IReadOnlySet<uint> from, uint to)
    {
        for (int i = 0; i + 4 <= b.Length; i++)
            if (from.Contains(Bytes.U32(b, i))) Bytes.PutU32(b, i, to);
    }

    // ------------------------------------------------------------------ placing

    /// <summary>
    /// Put a copy of an agent on the map at a cell and floor: fresh ids above everything on the map (and above 0x9fffff,
    /// clear of the game's own numbering); every position near the old spot moved there; its vehicle swapped for one of
    /// this island's (same type if it has one) when it came from another island; its squad made fresh for this map.
    /// </summary>
    public static string Place(LairMap map, byte[] entity, byte[] companion, byte[] squad, int column, int row, int floor,
                               uint vehicle, uint vehicleType, IReadOnlyDictionary<uint, uint> islandVehicles, bool rebuildSquad = true)
    {
        if (!Heights.TryGetValue(floor, out float height)) throw new ArgumentException($"agents can't go on floor {floor} yet (its height isn't known)");
        entity = (byte[])entity.Clone(); companion = (byte[])companion.Clone();
        // the squad's memories (known area, points) belong to where the agent stood in its save: rebuilt around the new
        // spot (kept, an agent walked through rock heading back to its old area; emptied, agents were unkillable after a hit)
        bool otherIsland = vehicle != 0 && islandVehicles.Count > 0 && !islandVehicles.ContainsKey(vehicle);
        // its remembered points ([id][cell]->[cell] rectangles + kind; island landmarks such as where the raid entered and
        // leaves) stay on its own island; dropped they left at once (and leaving agents can't be hurt); another island's
        // are meaningless there
        squad = rebuildSquad ? FreshSquad(squad, map, dropPoints: otherIsland, at: (row, floor, column)) : (byte[])squad.Clone();
        uint e = Bytes.U32(entity, 8), c = Bytes.U32(companion, 8), s = Bytes.U32(squad, 1);
        uint next = Math.Max(map.MaxId(), 0x9fffffu) + 1;
        foreach (var b in new[] { entity, companion, squad })
        {
            Replace(b, new HashSet<uint> { e }, next);
            Replace(b, new HashSet<uint> { c }, next + 1);
            Replace(b, new HashSet<uint> { s }, next + 2);
        }
        string ride = "";
        if (otherIsland)
        {
            uint to = islandVehicles.FirstOrDefault(v => v.Value == vehicleType).Key is var same and not 0 ? same : islandVehicles.Keys.Min();
            Replace(entity, new HashSet<uint> { vehicle }, to);
            ride = $", vehicle {vehicle:x} -> {to:x}";
        }
        float x = F(entity, 80), y = F(entity, 84), z = F(entity, 88);
        float dx = row + 0.5f - x, dy = height - y, dz = column + 0.5f - z;
        int moved = Move(entity, x, y, z, dx, dy, dz) + Move(companion, x, y, z, dx, dy, dz);
        int cells = MoveCells(entity, (int)x, FloorOf(y), (int)z, row, floor, column) + MoveCells(companion, (int)x, FloorOf(y), (int)z, row, floor, column);
        map.AddEntities(new[] { entity, companion });
        map.AddSquad(squad);
        return $"agent placed at column {column}, row {row} (ids {next:x}..{next + 2:x}, {moved} positions, {cells} cells moved{ride})";
    }

    /// <summary>
    /// The agent's grid cell as whole numbers, [u32 row][u32 floor][u32 column] (as furniture stores it): every one on
    /// the old floor within 30 cells of the old cell goes to the new cell. A stale cell had an Investigator walking
    /// through rock (it planned from where it stood in the save).
    /// </summary>
    static int MoveCells(byte[] b, int row, int floor, int col, int newRow, int newFloor, int newCol)
    {
        int n = 0;
        for (int i = 16; i + 12 <= b.Length; i++)
        {
            int r = (int)Bytes.U32(b, i), f = (int)Bytes.U32(b, i + 4), c = (int)Bytes.U32(b, i + 8);
            if (f != floor || Math.Abs(r - row) > 30 || Math.Abs(c - col) > 30) continue;
            Bytes.PutU32(b, i, (uint)newRow); Bytes.PutU32(b, i + 4, (uint)newFloor); Bytes.PutU32(b, i + 8, (uint)newCol);
            n++; i += 11;
        }
        return n;
    }

    /// <summary>
    /// Every (x, height, z) float triple within 30 cells and 2 height of the old spot goes to the new spot (keeping its
    /// small height offset, e.g. the companion's -1): position, target and the old walking path all collapse onto it, so
    /// the agent plans a new route (a shifted path ran straight through rock).
    /// </summary>
    static int Move(byte[] b, float x, float y, float z, float dx, float dy, float dz)
    {
        int n = 0;
        for (int i = 16; i + 12 <= b.Length; i++)
        {
            float fx = F(b, i), fy = F(b, i + 4), fz = F(b, i + 8);
            if (!(Math.Abs(fx - x) <= 30 && Math.Abs(fz - z) <= 30 && Math.Abs(fy - y) <= 2)) continue;
            BitConverter.GetBytes(x + dx).CopyTo(b, i);
            BitConverter.GetBytes(fy + dy).CopyTo(b, i + 4);
            BitConverter.GetBytes(z + dz).CopyTo(b, i + 8);
            n++; i += 11;
        }
        return n;
    }
}
