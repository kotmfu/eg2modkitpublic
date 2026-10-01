using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// What a research (rtrp) or engineering (rctr) project needs and gives: minions, furniture, costs, and the furniture it
/// unlocks. They're lists inside the first prop (rtrp 0xf, rctr 0x6), in a fixed order per type (HANDOFF round 21):
/// rtrp = [unlocks][?][?][costs][minions][furniture]..., rctr = [minions][furniture][costs]...[unlocks] (after its script).
/// Entries: minion = prop4 [u32 type hash][u32 count][u32 3][u8 1]; furniture = prop1 [fnas id][u32 count];
/// cost = prop1 [rcns id][u32 amount]; unlocks = [u32 n][fnas ids].
/// </summary>
public sealed class Requirements
{
    public List<(uint Type, uint Count)> Minions { get; } = new();
    public List<(uint Fnas, uint Count)> Furniture { get; } = new();
    public List<(uint Resource, uint Amount)> Costs { get; } = new();
    public List<uint> Unlocks { get; } = new();

    const uint ListKey = 0x80000001, MinionKey = 0x80000004, EntryKey = 0x80000001;

    /// <summary>Indexes (among the first prop's list props) of unlocks, costs, minions, furniture.</summary>
    static (int Unlocks, int Costs, int Minions, int Furniture) Slots(string tag) => tag switch
    {
        "rtrp" => (0, 3, 4, 5),
        "rctr" => (3, 2, 0, 1),
        _ => throw new InvalidOperationException($"{tag} has no requirements"),
    };

    /// <summary>
    /// What the game can't handle; empty = fine. Every game item needs at least one minion and one piece of furniture
    /// (the device it's worked on): without furniture the project never progresses (research speed x0.0, seen in-game).
    /// </summary>
    public static List<string> Problems(int minions, int furniture, bool zeroCount)
    {
        var p = new List<string>();
        if (furniture == 0) p.Add("needs at least one furniture item to be worked on (without one it never progresses)");
        if (minions == 0) p.Add("needs at least one minion type");
        if (zeroCount) p.Add("minion and furniture counts must be 1 or more");
        return p;
    }

    public List<string> Problems() => Problems(Minions.Count, Furniture.Count, Minions.Any(m => m.Count == 0) || Furniture.Any(f => f.Count == 0));

    public static bool Supports(string tag) => tag is "rtrp" or "rctr";

    /// <summary>The first prop's direct list children (prop 0x1), in order; for rctr the unlock list follows the script.</summary>
    static List<Prop> Lists(List<PropNode> top) =>
        top.OfType<Prop>().First().Children.OfType<Prop>().Where(p => p.Id == 1).ToList();

    static byte[] Raw(PropNode n) => ((RawNode)n).Data;

    public static Requirements Parse(string tag, byte[] body)
    {
        var r = new Requirements();
        var lists = Lists(PropStream.Parse(body, ObjectHeader.Size));
        var s = Slots(tag);
        foreach (var e in lists[s.Minions].Props()) { var d = Raw(e.Children[0]); r.Minions.Add((Bytes.U32(d, 0), Bytes.U32(d, 4))); }
        foreach (var e in lists[s.Furniture].Props()) { var d = Raw(e.Children[0]); r.Furniture.Add((Bytes.U32(d, 0), Bytes.U32(d, 4))); }
        foreach (var e in lists[s.Costs].Props()) { var d = Raw(e.Children[0]); r.Costs.Add((Bytes.U32(d, 0), Bytes.U32(d, 4))); }
        var u = Raw(lists[s.Unlocks].Children[0]);
        for (int i = 0; i < Bytes.U32(u, 0); i++) r.Unlocks.Add(Bytes.U32(u, 4 + i * 4));
        return r;
    }

    /// <summary>Parse, but only if rebuilding reproduces the object exactly (278/283 do; the rest are misread story research).</summary>
    public static Requirements? TryParse(string tag, byte[] body)
    {
        if (!Supports(tag)) return null;
        try
        {
            var r = Parse(tag, body);
            return r.Write(tag, body).AsSpan().SequenceEqual(body) ? r : null;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidCastException or InvalidOperationException or AsuraFormatException) { return null; }
    }

    /// <summary><paramref name="body"/> with the four lists rebuilt from this model (everything else kept).</summary>
    public byte[] Write(string tag, byte[] body)
    {
        var top = PropStream.Parse(body, ObjectHeader.Size);
        var lists = Lists(top);
        var s = Slots(tag);
        static RawNode R(params uint[] v) => new(Bytes.Le(v));
        void Set(Prop list, IEnumerable<PropNode> items)
        {
            var l = items.ToList();
            list.Children.Clear();
            list.Children.Add(R((uint)l.Count));
            list.Children.AddRange(l);
        }
        Set(lists[s.Minions], Minions.Select(m => (PropNode)new Prop(MinionKey, 0, new List<PropNode> { new RawNode(Bytes.Concat(Bytes.Le(m.Type, m.Count, 3u), new byte[] { 1 })) })));
        Set(lists[s.Furniture], Furniture.Select(f => (PropNode)new Prop(EntryKey, 0, new List<PropNode> { R(f.Fnas, f.Count) })));
        Set(lists[s.Costs], Costs.Select(c => (PropNode)new Prop(EntryKey, 0, new List<PropNode> { R(c.Resource, c.Amount) })));
        lists[s.Unlocks].Children.Clear();
        lists[s.Unlocks].Children.Add(R(Unlocks.Prepend((uint)Unlocks.Count).ToArray()));
        return Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(top));
    }

    /// <summary>
    /// Minion types (hash → name). The specialisation trees (mtex) hold [x][y][type][parent type] entries rooted at
    /// Worker (h31 "worker", the only name that hashes back); each type's name comes from its trainer furniture
    /// ("Scientist Trainer" → Scientist). 14 types.
    /// </summary>
    public static Dictionary<uint, string> MinionTypes(GameData g)
    {
        const uint worker = 0xd162537e;
        var mtex = g.Objects.Where(o => o.Tag == "mtex").Select(o => o.Body).ToList();
        var types = new HashSet<uint> { worker };
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var b in mtex)
                for (int i = 8; i + 8 <= b.Length; i++)
                {
                    uint v = Bytes.U32(b, i);
                    // [x][y][type][parent]: parent known, type not a float (floats here have 16 zero low bits)
                    if (types.Contains(Bytes.U32(b, i + 4)) && (v & 0xffff) != 0 && v != 0 && types.Add(v)) grew = true;
                }
        }
        // trees not rooted at Worker (Mechanical Minions: robots, the Abomination) list their types with parent 0;
        // keep those made at a creation station / trainer furniture
        var makers = g.Objects.Where(o => o.IsRecord && (o.RecordName!.StartsWith("Train_", StringComparison.OrdinalIgnoreCase)
                                                         || o.RecordName.Contains("Creation_Station", StringComparison.OrdinalIgnoreCase))).ToList();
        var jobs = g.Objects.Where(o => o.Tag == "rjob").Select(o => o.Body).ToList();
        foreach (var b in mtex)
            for (int i = 8; i + 8 <= b.Length; i++)
            {
                uint v = Bytes.U32(b, i);
                if (Bytes.U32(b, i + 4) != 0 || v < 0x01000000 || (v & 0xff) == 0 || ((v >> 8) & 0xffff) == 0 || types.Contains(v)) continue;   // full-width hashes only
                var bytes = BitConverter.GetBytes(v);
                if (makers.Any(m => m.Body.AsSpan().IndexOf(bytes) >= 0) && jobs.Any(j => j.AsSpan().IndexOf(bytes) >= 0)) types.Add(v);
            }
        var names = new Dictionary<uint, string> { [worker] = "Worker" };
        var trainers = makers;
        foreach (var t in types.Where(t => t != worker))
        {
            var best = trainers.Select(o => (o, n: Enumerable.Range(0, o.Body.Length - 3).Select(i => Bytes.U32(o.Body, i)).Count(types.Contains),
                                                 has: Enumerable.Range(0, o.Body.Length - 3).Any(i => Bytes.U32(o.Body, i) == t)))
                .Where(x => x.has).OrderBy(x => x.n).Select(x => x.o).FirstOrDefault();
            if (best is null) continue;
            var disp = best.Name.IndexOf("  (") is > 0 and var k ? best.Name[(k + 3)..^1] : "";
            var rec = best.RecordName!;
            names[t] = disp.EndsWith(" Trainer") ? disp[..^8]
                : rec.Contains("Creation_Station", StringComparison.OrdinalIgnoreCase)
                    ? (rec.Split("_Creation_Station")[0] + " " + rec.Split("Creation_Station")[1].Trim('_')).Trim()   // Robot_Creation_Station_Guard -> Robot Guard
                : rec.Replace("_Nrm", "", StringComparison.OrdinalIgnoreCase).Split('_')[^1];
        }
        return names;
    }

    /// <summary>Furniture name (fntr record) -> its fnas id (the fnas object in the same package the record points at).</summary>
    public static Dictionary<string, uint> FurnitureFnas(GameData g)
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in g.Objects.Where(o => o.IsRecord))
            for (int i = 0; i + 4 <= rec.Body.Length; i++)
                if (g.ObjectsById.TryGetValue(Bytes.U32(rec.Body, i), out var l) && l.Any(x => x.Tag == "fnas" && x.Package == rec.Package))
                {
                    map.TryAdd(rec.RecordName!, Bytes.U32(rec.Body, i));
                    break;
                }
        return map;
    }
}
