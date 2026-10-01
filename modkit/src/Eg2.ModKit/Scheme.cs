using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Scheme logic (rscm): first prop 0x38 = [raw6: f32 + u16][reward scripts][raw28][list][raw16: 4 floats][minion
/// alternatives][raw8: f32 expiry, u32][p1][raw1][p1 costs: n + prop1 (rcns, amount)]... then the name/description refs. 1372 of 1387 have this layout.
/// Confirmed in-game (HANDOFF round 23): raw16[3] = heat gain to region, raw16[1] = payout interval of "while running"
/// schemes (s), raw6 f32 = duration (s), raw8 f32 = how long the offer stays on the map (s), minions = alternatives (each offer shows one group, picked by the game) of
/// [type][count][category][u8 1] entries.
/// </summary>
public sealed class Scheme
{
    /// <summary>Alternatives: each offer uses one group (seen in-game: only the first showed). Each group = (minion type, count).</summary>
    public List<List<(uint Type, uint Count)>> Minions { get; } = new();
    public float Heat { get; set; }
    /// <summary>What launching costs (intel versions of schemes: Intel; some Gold): (rcns id, amount).</summary>
    public List<(uint Resource, uint Amount)> Costs { get; } = new();
    public float Expiry { get; set; }
    /// <summary>Seconds the scheme runs (raw6 f32; 7200 = "2h:00m"). Most story schemes keep 10 here.</summary>
    public float Duration { get; set; }

    const uint GroupKey = 0x80000001, MinionKey = 0x80000004;
    const int Raw16 = 4, Groups = 5, Raw8 = 6, CostList = 9;

    static Prop Top(byte[] body) => (Prop)PropStream.Parse(body, ObjectHeader.Size)[0];

    static bool Fits(Prop top) => top.Children.Count > Raw8 && top.Children[Raw16] is RawNode { Data.Length: 16 }
                                  && top.Children[Groups] is Prop && top.Children[Raw8] is RawNode { Data.Length: 8 }
                                  && top.Children.Count > CostList && top.Children[CostList - 1] is RawNode { Data.Length: 1 } && top.Children[CostList] is Prop;

    /// <summary>Null when it isn't a scheme of the known layout or doesn't rebuild byte-exact.</summary>
    public static Scheme? TryParse(string tag, byte[] body)
    {
        if (tag != "rscm") return null;
        try
        {
            var top = Top(body);
            if (!Fits(top)) return null;
            var s = new Scheme
            {
                Heat = BitConverter.ToSingle(((RawNode)top.Children[Raw16]).Data, 12),
                Expiry = BitConverter.ToSingle(((RawNode)top.Children[Raw8]).Data, 0),
                Duration = BitConverter.ToSingle(((RawNode)top.Children[0]).Data, 0),
            };
            foreach (var g in ((Prop)top.Children[Groups]).Props())
                s.Minions.Add(g.Props().Select(e => { var d = ((RawNode)e.Children[0]).Data; return (Bytes.U32(d, 0), Bytes.U32(d, 4)); }).ToList());
            foreach (var e in ((Prop)top.Children[CostList]).Props()) { var d = ((RawNode)e.Children[0]).Data; s.Costs.Add((Bytes.U32(d, 0), Bytes.U32(d, 4))); }
            return s.Write(body, null).AsSpan().SequenceEqual(body) ? s : null;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidCastException or InvalidOperationException or AsuraFormatException) { return null; }
    }

    /// <summary>
    /// <paramref name="body"/> with these values. <paramref name="category"/> gives each minion type's category
    /// (0 Worker, 1 muscle, 2 social, 3 science; see <see cref="Categories"/>); null = keep what the entries had.
    /// </summary>
    public byte[] Write(byte[] body, IReadOnlyDictionary<uint, uint>? category)
    {
        var top = Top(body);
        var r6 = ((RawNode)top.Children[0]).Data.ToArray();
        BitConverter.GetBytes(Duration).CopyTo(r6, 0);
        top.Children[0] = new RawNode(r6);
        var r16 = ((RawNode)top.Children[Raw16]).Data.ToArray();
        BitConverter.GetBytes(Heat).CopyTo(r16, 12);
        top.Children[Raw16] = new RawNode(r16);
        var r8 = ((RawNode)top.Children[Raw8]).Data.ToArray();
        BitConverter.GetBytes(Expiry).CopyTo(r8, 0);
        top.Children[Raw8] = new RawNode(r8);
        var old = ((Prop)top.Children[Groups]).Props().SelectMany(g => g.Props()).Select(e => ((RawNode)e.Children[0]).Data)
            .GroupBy(d => Bytes.U32(d, 0)).ToDictionary(x => x.Key, x => Bytes.U32(x.First(), 8));
        var groups = (Prop)top.Children[Groups];
        groups.Children.Clear();
        groups.Children.Add(new RawNode(Bytes.Le((uint)Minions.Count)));
        foreach (var g in Minions)
        {
            var entries = g.Select(m =>
            {
                uint cat = category is not null && category.TryGetValue(m.Type, out var c) ? c : old.TryGetValue(m.Type, out var o) ? o
                    : throw new InvalidOperationException($"unknown category for minion type {Bytes.Hex(m.Type)}");
                return (PropNode)new Prop(MinionKey, 0, new List<PropNode> { new RawNode(Bytes.Concat(Bytes.Le(m.Type, m.Count, cat), new byte[] { 1 })) });
            }).ToList();
            groups.Children.Add(new Prop(GroupKey, 0, entries.Prepend(new RawNode(Bytes.Le((uint)entries.Count))).ToList()));
        }
        var costs = (Prop)top.Children[CostList];
        costs.Children.Clear();
        costs.Children.Add(new RawNode(Bytes.Le((uint)Costs.Count)));
        costs.Children.AddRange(Costs.Select(c => (PropNode)new Prop(GroupKey, 0, new List<PropNode> { new RawNode(Bytes.Le(c.Resource, c.Amount)) })));
        return Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(new PropNode[] { top }));
    }

    /// <summary>Minion type → category, as the game's schemes use them.</summary>
    public static Dictionary<uint, uint> Categories(GameData g)
    {
        var map = new Dictionary<uint, uint>();
        foreach (var o in g.Objects.Where(o => o.Tag == "rscm" && !o.IsRecord))
        {
            try
            {
                var top = Top(o.Body);
                if (!Fits(top)) continue;
                foreach (var e in ((Prop)top.Children[Groups]).Props().SelectMany(x => x.Props()))
                {
                    var d = ((RawNode)e.Children[0]).Data;
                    map.TryAdd(Bytes.U32(d, 0), Bytes.U32(d, 8));
                }
            }
            catch (AsuraFormatException) { }
        }
        return map;
    }

    /// <summary>Empty = fine. The game has schemes with no minions, negative heat (heat reduction) and expiry -1 (stays).</summary>
    public List<string> Problems()
    {
        var p = new List<string>();
        if (Minions.Any(g => g.Count == 0)) p.Add("a minion group can't be empty (remove the group instead)");
        if (Minions.SelectMany(g => g).Any(m => m.Count == 0)) p.Add("minion counts must be 1 or more");
        if (Duration <= 0) p.Add("the duration must be more than 0 seconds");
        if (Expiry == 0) p.Add("an offer that expires after 0 seconds never shows (use -1 to keep it)");
        return p;
    }
}
