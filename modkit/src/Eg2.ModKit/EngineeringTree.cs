using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// An engineering tree (rctt). Unlike research trees, lines are routed through junction nodes (item 0), so links are
/// line segments: [from node, to node, source item node, target item nodes reached through this segment].
/// Layout: fixed header up to +130 (name, background/select icon keys, colours), then prop 0x2 =
/// [links: n + prop2(u32 from, to, source + prop1 list of targets)] [nodes: n + prop4(u32 index, i32 prev item node,
/// next item nodes, outgoing link list, u32 item id, u8 available from start, u32 column, u32 row, f32 dx, f32 dy)]
/// [u32 columns, u32 rows] [grid: n + item id per cell, row by row]. Read at fixed offsets: the generic prop walker
/// mistakes the colour floats for block headers here.
/// </summary>
public sealed class EngineeringTree
{
    public const int BlockAt = 130;
    /// <summary>Background image and tree-selector icon keys (GUI keys, see <see cref="FurnitureArt.GuiKey"/>).</summary>
    public const int SelectIconKeyAt = 49, BackgroundKeyAt = 53;

    public sealed class Node
    {
        /// <summary>Engineering item (rctr id); 0 = a junction that only routes lines.</summary>
        public uint Item { get; set; }
        /// <summary>Node index of the item that must be done first; -1 = none.</summary>
        public int Prev { get; set; } = -1;
        public bool Root { get; set; }
        public int Column { get; set; }
        public int Row { get; set; }
        /// <summary>Offset inside the cell (crafting trees use it; engineering trees keep 0).</summary>
        public float Dx { get; set; }
        public float Dy { get; set; }
        /// <summary>Item nodes that follow this one (its direct successors, or for a junction the items it leads to).</summary>
        public List<int> Next { get; set; } = new();
    }

    public sealed class Link
    {
        public int From { get; set; }
        public int To { get; set; }
        /// <summary>The item node the line starts at.</summary>
        public int Source { get; set; }
        /// <summary>Item nodes this segment leads to.</summary>
        public List<int> Targets { get; set; } = new();
    }

    public List<Node> Nodes { get; } = new();
    public List<Link> Links { get; } = new();
    public int Columns { get; set; }
    public int Rows { get; set; }

    const uint ListKey = 0x80000001, LinkKey = 0x80000002, NodeKey = 0x80000004, BlockKey = 0x80000002;

    static Prop Block(byte[] body)
    {
        if (body.Length < BlockAt + 9 || Bytes.U32(body, BlockAt) != BlockKey) throw new AsuraFormatException("engineering tree: no node block at +130");
        return PropStream.Parse(body, BlockAt, BlockAt + 9 + (int)Bytes.U32(body, BlockAt + 5)).OfType<Prop>().Single();
    }

    static byte[] Data(PropNode n) => ((RawNode)n).Data;
    static List<int> IntList(Prop p) { var d = Data(p.Children[0]); return Enumerable.Range(1, (int)Bytes.U32(d, 0)).Select(i => (int)Bytes.U32(d, i * 4)).ToList(); }

    public static EngineeringTree Parse(byte[] body)
    {
        var t = new EngineeringTree();
        var parts = Block(body).Children;
        var props = parts.OfType<Prop>().ToList();
        foreach (var l in props[0].Props())
        {
            var d = Data(l.Children[0]);
            t.Links.Add(new Link { From = (int)Bytes.U32(d, 0), To = (int)Bytes.U32(d, 4), Source = (int)Bytes.U32(d, 8), Targets = IntList((Prop)l.Children[1]) });
        }
        foreach (var n in props[1].Props())
        {
            var head = Data(n.Children[0]);
            var d = Data(n.Children[^1]);
            t.Nodes.Add(new Node
            {
                Prev = (int)Bytes.U32(head, 4), Item = Bytes.U32(d, 0), Root = d[4] != 0, Column = (int)Bytes.U32(d, 5), Row = (int)Bytes.U32(d, 9),
                Dx = BitConverter.ToSingle(d, 13), Dy = BitConverter.ToSingle(d, 17), Next = IntList((Prop)n.Children[1]),
            });
        }
        var size = parts.OfType<RawNode>().First(r => r.Data.Length == 8).Data;
        t.Columns = (int)Bytes.U32(size, 0);
        t.Rows = (int)Bytes.U32(size, 4);
        return t;
    }

    /// <summary><paramref name="original"/> with its node block rebuilt from this model (grid recomputed).</summary>
    public byte[] Write(byte[] original)
    {
        int end = BlockAt + 9 + (int)Bytes.U32(original, BlockAt + 5);
        static RawNode Raw(params uint[] v) => new(Bytes.Le(v));
        static Prop List(uint key, IEnumerable<PropNode> items) { var l = items.ToList(); return new Prop(key, 0, l.Prepend(Raw((uint)l.Count)).ToList()); }
        static Prop Ints(IEnumerable<int> v) { var l = v.Select(i => (uint)i).ToList(); return new Prop(ListKey, 0, new List<PropNode> { Raw(l.Prepend((uint)l.Count).ToArray()) }); }

        var grid = new uint[Columns * Rows];
        foreach (var n in Nodes.Where(n => n.Item != 0))
        {
            if (n.Column < 0 || n.Column >= Columns || n.Row < 0 || n.Row >= Rows) throw new InvalidOperationException($"engineering tree: item at {n.Column},{n.Row} is outside {Columns}x{Rows}");
            grid[n.Row * Columns + n.Column] = n.Item;
        }
        var links = Links.Select(l => (PropNode)new Prop(LinkKey, 0, new List<PropNode> { Raw((uint)l.From, (uint)l.To, (uint)l.Source), Ints(l.Targets) }));
        var nodes = Nodes.Select((n, i) =>
        {
            var tail = new byte[21];
            Bytes.PutU32(tail, 0, n.Item);
            tail[4] = (byte)(n.Root ? 1 : 0);
            Bytes.PutU32(tail, 5, (uint)n.Column);
            Bytes.PutU32(tail, 9, (uint)n.Row);
            BitConverter.GetBytes(n.Dx).CopyTo(tail, 13);
            BitConverter.GetBytes(n.Dy).CopyTo(tail, 17);
            return (PropNode)new Prop(NodeKey, 0, new List<PropNode>
            {
                Raw((uint)i, (uint)n.Prev),
                Ints(n.Next),
                Ints(Links.Select((l, k) => (l, k)).Where(x => x.l.From == i).Select(x => x.k)),
                new RawNode(tail),
            });
        });
        var block = new Prop(BlockKey, 0, new List<PropNode>
        {
            List(ListKey, links), List(ListKey, nodes), Raw((uint)Columns, (uint)Rows),
            new Prop(ListKey, 0, new List<PropNode> { Raw(grid.Prepend((uint)grid.Length).ToArray()) }),
        });
        return Bytes.Concat(original[..BlockAt], block.ToBytes(), original[end..]);
    }

    public sealed record Item(uint Id, int Column, int Row, int After, float Dx = 0, float Dy = 0);

    /// <summary>The placed items (junctions left out), with After = index of the item that must be done first.</summary>
    public List<Item> Items()
    {
        var itemIndex = new Dictionary<int, int>();
        for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Item != 0) itemIndex[i] = itemIndex.Count;
        return Nodes.Where(n => n.Item != 0).Select(n => new Item(n.Item, n.Column, n.Row, n.Prev >= 0 && itemIndex.TryGetValue(n.Prev, out var a) ? a : -1, n.Dx, n.Dy)).ToList();
    }

    /// <summary>
    /// Nodes and links for <paramref name="items"/>, routed like the game does: a target in the source's row gets a
    /// straight line when it's the only one; otherwise the line goes to a junction one column right of the source, runs
    /// up/down a chain of junctions in that column, and turns right at each target's row.
    /// </summary>
    public void SetItems(IReadOnlyList<Item> items)
    {
        Nodes.Clear(); Links.Clear();
        foreach (var it in items)
            Nodes.Add(new Node { Item = it.Id, Column = it.Column, Row = it.Row, Dx = it.Dx, Dy = it.Dy, Prev = it.After, Root = it.After < 0 });
        for (int src = 0; src < items.Count; src++)
        {
            var targets = Enumerable.Range(0, items.Count).Where(i => items[i].After == src).ToList();
            Nodes[src].Next = targets;
            if (targets.Count == 0) continue;
            var s = items[src];
            if (targets.All(t => items[t].Row == s.Row) && targets.Count == 1)
            {
                Links.Add(new Link { From = src, To = targets[0], Source = src, Targets = targets });
                continue;
            }
            int Junction(int row, int prev)
            {
                Nodes.Add(new Node { Item = 0, Column = s.Column + 1, Row = row, Prev = src });
                return Nodes.Count - 1;
            }
            int trunk = Junction(s.Row, src);
            Links.Add(new Link { From = src, To = trunk, Source = src, Targets = targets });
            // same-row targets straight off the trunk, then one chain up and one down
            foreach (var t in targets.Where(t => items[t].Row == s.Row))
                Links.Add(new Link { From = trunk, To = t, Source = src, Targets = new() { t } });
            foreach (int dir in new[] { -1, 1 })
            {
                var side = targets.Where(t => Math.Sign(items[t].Row - s.Row) == dir).ToList();
                int at = trunk, row = s.Row;
                while (side.Count > 0)
                {
                    row += dir;
                    int j = Junction(row, src);
                    Links.Add(new Link { From = at, To = j, Source = src, Targets = side.ToList() });
                    foreach (var t in side.Where(t => items[t].Row == row).ToList())
                    {
                        Links.Add(new Link { From = j, To = t, Source = src, Targets = new() { t } });
                        side.Remove(t);
                    }
                    at = j;
                }
            }
        }
        for (int i = 0; i < Nodes.Count; i++)
            if (Nodes[i].Item == 0) Nodes[i].Next = Links.Where(l => l.From == i).SelectMany(l => l.Targets).Distinct().ToList();
    }

    /// <summary>What would break the game's tree screen; empty = fine.</summary>
    public List<string> Check(Func<int, string> name)
    {
        var p = new List<string>();
        var cells = new Dictionary<(int, int), string>();
        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            string what = n.Item == 0 ? $"a line bend for {name(n.Prev)}" : name(i);
            if (n.Column < 0 || n.Column >= Columns || n.Row < 0 || n.Row >= Rows) p.Add($"{what} is at column {n.Column + 1}, row {n.Row + 1}, outside the {Columns} x {Rows} tree");
            else if (cells.TryGetValue((n.Column, n.Row), out var other) && !(n.Item == 0 && other.StartsWith("a line bend"))) p.Add($"{what} and {other} share column {n.Column + 1}, row {n.Row + 1}");
            else cells[(n.Column, n.Row)] = what;
            if (n.Item != 0 && n.Prev >= 0 && Nodes[n.Prev].Column >= n.Column - (Nodes[n.Prev].Row == n.Row ? 0 : 1))
                p.Add($"{name(i)} must be further right than {name(n.Prev)}{(Nodes[n.Prev].Row == n.Row ? "" : " (two columns when it's in another row)")}");
        }
        return p;
    }
}
