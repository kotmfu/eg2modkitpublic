using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// A research tree (rttr): research nodes on a column/row grid plus prerequisite links. Layout (prop 0xa → prop 0x4):
/// [links: count + (from, to, 0)] [the same links reversed] [nodes: count + prop 5 each = index, out-link list, in-link
/// list, rtrp id, u32 column (1-based), u8 starts-available, u32 row] [u32 columns, u32 rows] [grid: count + node index
/// per cell, row by row, -1 = empty]. Everything else (name, colours) is kept as is.
/// </summary>
public sealed class ResearchTree
{
    public sealed class Node
    {
        public uint Research { get; set; }
        public int Column { get; set; }
        public int Row { get; set; }
    }

    public List<Node> Nodes { get; } = new();
    /// <summary>Prerequisite links as node indexes: From must be researched before To.</summary>
    public List<(int From, int To)> Links { get; } = new();

    const uint ListKey = 0x80000001, NodeKey = 0x80000005;

    /// <summary>
    /// The shape every game tree keeps: 5 tiers x 7 rows, links forward within a row, at most one prerequisite.
    /// A 6th tier plus a row-changing link crashed the game when opening the tree (2026-09-24).
    /// ponytail: row-changing links / several prerequisites are refused untested; relax once one is tried alone.
    /// </summary>
    public const int MaxColumns = 5, MaxRows = 7;

    /// <summary>Problems that would break the game's tree screen; empty = fine.</summary>
    public List<string> Check(Func<int, string> name)
    {
        var p = new List<string>();
        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            if (n.Column < 1 || n.Column > MaxColumns || n.Row < 0 || n.Row >= MaxRows)
                p.Add($"{name(i)} is at tier {n.Column}, row {n.Row + 1}; the game's tree has tiers 1-{MaxColumns} and rows 1-{MaxRows}");
            if (Links.Count(l => l.To == i) > 1) p.Add($"{name(i)} has more than one prerequisite (the game's trees never do)");
        }
        for (int i = 0; i < Nodes.Count; i++)
            for (int j = i + 1; j < Nodes.Count; j++)
                if (Nodes[i].Column == Nodes[j].Column && Nodes[i].Row == Nodes[j].Row) p.Add($"{name(i)} and {name(j)} are in the same cell");
        foreach (var (f, t) in Links)
        {
            if (f < 0 || t < 0 || f >= Nodes.Count || t >= Nodes.Count) { p.Add($"link {f} -> {t} points at no item"); continue; }
            if (Nodes[f].Row != Nodes[t].Row) p.Add($"{name(f)} -> {name(t)}: a prerequisite must be in the same row");
            else if (Nodes[t].Column <= Nodes[f].Column) p.Add($"{name(f)} -> {name(t)}: a prerequisite must be in an earlier tier");
        }
        return p;
    }

    static Prop Body(List<PropNode> top) =>
        top.OfType<Prop>().First(p => p.Id == 0xa).Props().FirstOrDefault(p => p.Id == 0x4)
        ?? throw new AsuraFormatException("research tree: no node block");

    static uint U32(PropNode n, int at = 0) => Bytes.U32(((RawNode)n).Data, at);

    public static ResearchTree Parse(byte[] body)
    {
        var t = new ResearchTree();
        var block = Body(PropStream.Parse(body, ObjectHeader.Size)).Children;
        var lists = block.OfType<Prop>().ToList();
        foreach (var e in lists[0].Props())
            t.Links.Add(((int)U32(e.Children[0]), (int)U32(e.Children[0], 4)));
        foreach (var n in lists[2].Props())
        {
            var d = ((RawNode)n.Children[^1]).Data;
            // d[8] = available from the start; in every game tree that is exactly "no prerequisites", so Write derives it
            t.Nodes.Add(new Node { Research = Bytes.U32(d, 0), Column = (int)Bytes.U32(d, 4), Row = (int)Bytes.U32(d, 9) });
        }
        return t;
    }

    /// <summary><paramref name="original"/> with its node block rebuilt from this model.</summary>
    public byte[] Write(byte[] original)
    {
        var top = PropStream.Parse(original, ObjectHeader.Size);
        var block = Body(top);
        static RawNode Raw(params uint[] v) => new(Bytes.Le(v));
        static Prop List(IEnumerable<PropNode> items)
        {
            var l = items.ToList();
            return new Prop(ListKey, 0, l.Prepend(Raw((uint)l.Count)).ToList());
        }
        Prop IndexList(IEnumerable<int> idx) => new(ListKey, 0, new List<PropNode> { Raw(idx.Select(i => (uint)i).Prepend((uint)idx.Count()).ToArray()) });

        int cols = Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Column), rows = Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Row) + 1;
        var grid = Enumerable.Repeat(uint.MaxValue, cols * rows).ToArray();
        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            if (n.Column < 1 || n.Row < 0) throw new InvalidOperationException($"research tree: node {i} has column {n.Column}, row {n.Row}");
            ref uint cell = ref grid[n.Row * cols + n.Column - 1];
            if (cell != uint.MaxValue) throw new InvalidOperationException($"research tree: two nodes at column {n.Column}, row {n.Row}");
            cell = (uint)i;
        }
        var nodes = Nodes.Select((n, i) =>
        {
            var tail = new byte[13];
            Bytes.PutU32(tail, 0, n.Research);
            Bytes.PutU32(tail, 4, (uint)n.Column);
            tail[8] = (byte)(Links.Any(l => l.To == i) ? 0 : 1);
            Bytes.PutU32(tail, 9, (uint)n.Row);
            return (PropNode)new Prop(NodeKey, 0, new List<PropNode>
            {
                Raw((uint)i),
                IndexList(Links.Select((l, k) => (l, k)).Where(x => x.l.From == i).Select(x => x.k)),
                IndexList(Links.Select((l, k) => (l, k)).Where(x => x.l.To == i).Select(x => x.k)),
                new RawNode(tail),
            });
        });
        block.Children.Clear();
        block.Children.Add(List(Links.Select(l => (PropNode)new Prop(ListKey, 0, new List<PropNode> { Raw((uint)l.From, (uint)l.To, 0) }))));
        block.Children.Add(List(Links.Select(l => (PropNode)new Prop(ListKey, 0, new List<PropNode> { Raw((uint)l.To, (uint)l.From, 0) }))));
        block.Children.Add(List(nodes));
        block.Children.Add(Raw((uint)cols, (uint)rows));
        block.Children.Add(new Prop(ListKey, 0, new List<PropNode> { Raw(grid.Prepend((uint)grid.Length).ToArray()) }));
        return Bytes.Concat(original[..ObjectHeader.Size], PropStream.Serialize(top));
    }
}
