using System.Text.RegularExpressions;
using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Reader for the game's scripts (flowgraphs) inside data objects. What's known (HANDOFF round 18):
/// a graph is a name, a template path "FlowGraph/...", then nodes. A node is [type hash] [u32 1 or
/// size-prefixed 0x8000000N blocks]... [00 ff ff 00][u32 3][u32 node id] ...; node ids are sequential per graph.
/// A link is [link id][from node][from pin][to node][to pin][00 ff ff 00], stored in the node that owns the
/// output (from node 0 = the graph's start event). Pins are [pin hash][u32 1][u8 0][name\0 pad 4].
/// Type and pin names aren't stored as such; they're learned from the strings next to them (<see cref="Names"/>).
/// </summary>
public sealed class FlowGraph
{
    public sealed record Node(uint Id, uint Type, int Start, int End);
    public sealed record Link(int Offset, uint Id, uint FromNode, uint FromPin, uint ToNode, uint ToPin);

    public string Name { get; init; } = "";
    public string Template { get; init; } = "";
    public int Start { get; init; }
    public int End { get; init; }
    public List<Node> Nodes { get; } = new();
    public List<Link> Links { get; } = new();

    static readonly byte[] Marker = { 0x00, 0xff, 0xff, 0x00 };
    static readonly Regex Str = new("[ -~]{2,120}\0", RegexOptions.Compiled);
    static readonly Regex NameLike = new(@"^\{?[A-Za-z][A-Za-z0-9 _()\-/\\.,:']*\}?$", RegexOptions.Compiled);

    static bool MarkerAt(byte[] b, int i) => i >= 0 && i + 4 <= b.Length && b[i] == 0 && b[i + 1] == 0xff && b[i + 2] == 0xff && b[i + 3] == 0;

    /// <summary>Every graph in an object body.</summary>
    public static List<FlowGraph> Find(byte[] b)
    {
        var graphs = new List<FlowGraph>();
        var text = Bytes.Latin1.GetString(b);
        var starts = new List<(int At, string Name, string Template)>();
        foreach (Match m in Regex.Matches(text, "FlowGraph/[ -~]+?\0"))
        {
            // the graph's name is the string just before the template
            var before = Regex.Match(text[..m.Index], "([ -~]{1,120})\0+$", RegexOptions.RightToLeft);
            int at = before.Success ? before.Groups[1].Index : m.Index;
            starts.Add((at, before.Success ? before.Groups[1].Value : "", m.Value.TrimEnd('\0')));
        }
        for (int k = 0; k < starts.Count; k++)
        {
            int end = k + 1 < starts.Count ? starts[k + 1].At : b.Length;
            var gr = new FlowGraph { Name = starts[k].Name, Template = starts[k].Template, Start = starts[k].At, End = end };
            var nodeMarks = new List<(int Start, uint Id, uint Type)>();
            for (int i = gr.Start; i + 12 <= end; i++)
                if (MarkerAt(b, i) && Bytes.U32(b, i + 4) == 3)
                {
                    int s = TypeStart(b, i, gr.Start);
                    nodeMarks.Add((s, Bytes.U32(b, i + 8), s >= gr.Start ? Bytes.U32(b, s) : 0));
                }
            var ids = nodeMarks.Select(n => n.Id).ToHashSet();
            for (int n = 0; n < nodeMarks.Count; n++)
                gr.Nodes.Add(new Node(nodeMarks[n].Id, nodeMarks[n].Type, nodeMarks[n].Start, n + 1 < nodeMarks.Count ? nodeMarks[n + 1].Start : end));
            for (int i = gr.Start; i + 24 <= end; i++)
                if (MarkerAt(b, i + 20) && Bytes.U32(b, i) != 0 && ids.Contains(Bytes.U32(b, i + 12)) && (Bytes.U32(b, i + 4) == 0 || ids.Contains(Bytes.U32(b, i + 4)))
                    && !gr.Nodes.Any(n => n.Start > i && n.Start < i + 24))   // a real link never straddles into the next node
                {
                    gr.Links.Add(new Link(i, Bytes.U32(b, i), Bytes.U32(b, i + 4), Bytes.U32(b, i + 8), Bytes.U32(b, i + 12), Bytes.U32(b, i + 16)));
                    i += 23;
                }
            graphs.Add(gr);
        }
        return graphs;
    }

    /// <summary>Start of the node whose marker is at <paramref name="marker"/>: back over version words (1) and 9-byte block headers to the type hash.</summary>
    static int TypeStart(byte[] b, int marker, int min)
    {
        int p = marker - 4;
        while (p - 4 >= min)
        {
            if (Bytes.U32(b, p) == 1) { p -= 4; continue; }
            if (p - 5 >= min && (Bytes.U32(b, p - 5) & 0xffff0000) == 0x80000000 && b[p - 1] == 0) { p -= 9; continue; }
            break;
        }
        return p;
    }

    /// <summary>Strings inside a node (its value names, pin names).</summary>
    public static IEnumerable<(int At, string Text)> Strings(byte[] b, int start, int end)
    {
        foreach (Match m in Str.Matches(Bytes.Latin1.GetString(b, start, end - start)))
        {
            var s = m.Value.TrimEnd('\0');
            if (NameLike.IsMatch(s)) yield return (start + m.Index, s);
        }
    }

    /// <summary>Learned names: node type → its most common first string; pin hash → the string stored right after it.</summary>
    public sealed class Names
    {
        public Dictionary<uint, string> Types { get; } = new();
        public Dictionary<uint, string> Pins { get; } = new();

        public string Type(uint t) => Types.TryGetValue(t, out var n) ? n : $"node {Bytes.Hex(t)}";
        /// <summary>A pin's learned name, else "in"/"out" (the real names aren't stored; only "Condition" etc. hash back).</summary>
        public string Pin(uint p, bool output) => Pins.TryGetValue(p, out var n) ? n.Trim('{', '}') : output ? "out" : "in";


        public static Names Learn(IEnumerable<byte[]> bodies)
        {
            var types = new Dictionary<uint, Dictionary<string, int>>();
            var pins = new Dictionary<uint, Dictionary<string, int>>();
            static void Count(Dictionary<uint, Dictionary<string, int>> d, uint k, string v)
            {
                if (!d.TryGetValue(k, out var c)) d[k] = c = new();
                c[v] = c.GetValueOrDefault(v) + 1;
            }
            foreach (var b in bodies)
                foreach (var gr in Find(b))
                    foreach (var n in gr.Nodes)
                    {
                        var strs = Strings(b, n.Start, n.End).ToList();
                        if (strs.Count > 0) Count(types, n.Type, strs[0].Text);
                        foreach (var (at, s) in strs)
                            if (at >= 9 && Bytes.U32(b, at - 5) == 1 && b[at - 1] == 0) Count(pins, Bytes.U32(b, at - 9), s);
                    }
            var names = new Names();
            foreach (var (k, c) in types) names.Types[k] = c.MaxBy(x => x.Value).Key;
            foreach (var (k, c) in pins) names.Pins[k] = c.MaxBy(x => x.Value).Key;
            return names;
        }
    }

    /// <summary>A readable outline: nodes with their values and objects, then what connects to what.</summary>
    public string Describe(byte[] b, Names names, Func<uint, string?> objectName, IReadOnlyList<ObjectInspector.Param> pars)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Script \"{Name}\"  ({Template})");
        // node 0 is the graph's entry and node 1 its exit in every graph; their stored strings are noise
        string NodeLabel(uint id) => id switch
        {
            0 => "Start",
            1 => "End",
            _ => Nodes.FirstOrDefault(n => n.Id == id) is { } n ? $"{names.Type(n.Type)} #{id & 0xfff}" : $"#{id & 0xfff}",
        };
        foreach (var n in Nodes)
        {
            var extra = new List<string>();
            foreach (var p in pars.Where(p => p.Offset >= n.Start && p.Offset < n.End)) extra.Add($"{p.Label.Split(" → ")[^1]} = {p.Value}");
            for (int i = n.Start + 4; i + 4 <= n.End; i++)
                if (objectName(Bytes.U32(b, i)) is { } on) { extra.Add(on); i += 3; }
            sb.AppendLine($"  {NodeLabel(n.Id)}{(extra.Count > 0 ? ": " + string.Join(", ", extra.Distinct()) : "")}");
        }
        foreach (var l in Links)
            sb.AppendLine($"    {NodeLabel(l.FromNode)}.{names.Pin(l.FromPin, true)}  →  {NodeLabel(l.ToNode)}.{names.Pin(l.ToPin, false)}");
        return sb.ToString();
    }

    /// <summary>
    /// Scripts that sit alone in a sized block (prop 0x1, payload [u32 1][u32 5][name]...: 2,496 of 2,854, incl. all
    /// reward scripts). Only these can be swapped; condition scripts inside objectives are stored inline.
    /// </summary>
    public static List<(FlowGraph Graph, Prop Block)> Swappable(byte[] body)
    {
        var blocks = AllProps(PropStream.Parse(body, ObjectHeader.Size))
            .Where(p => p.Id == 1 && p.Kind == 0 && p.Payload() is { Length: > 8 } d && Bytes.U32(d, 0) == 1 && Bytes.U32(d, 4) == 5).ToList();
        var list = new List<(FlowGraph, Prop)>();
        foreach (var g in Find(body))
            if (blocks.FirstOrDefault(p => p.Offset + 9 + 8 == g.Start) is { } b) list.Add((g, b));
        return list;
    }

    static IEnumerable<Prop> AllProps(IEnumerable<PropNode> nodes)
    {
        foreach (var n in nodes)
            if (n is Prop p)
            {
                yield return p;
                foreach (var c in AllProps(p.Children)) yield return c;
            }
    }

    /// <summary>
    /// <paramref name="target"/> with its swappable script <paramref name="script"/> replaced by a copy of the donor's
    /// <paramref name="fromScript"/>, with <paramref name="values"/> (offset relative to the donor block's payload) written in.
    /// Enclosing sizes are recomputed. ponytail: node/link ids are copied as-is (ids are per-graph in every game graph seen).
    /// </summary>
    public static byte[] Swap(byte[] target, int script, byte[] donor, int fromScript, IEnumerable<(int Offset, byte[] Data)> values)
    {
        var top = PropStream.Parse(target, ObjectHeader.Size);
        var targets = SwappableIn(top, target);
        var donors = Swappable(donor);
        if (script < 0 || script >= targets.Count) throw new InvalidOperationException($"no script {script + 1} to replace (it has {targets.Count})");
        if (fromScript < 0 || fromScript >= donors.Count) throw new InvalidOperationException($"the donor has no script {fromScript + 1} (it has {donors.Count})");
        var payload = donors[fromScript].Block.Payload();
        foreach (var (off, data) in values)
        {
            if (off < 8 || off + data.Length > payload.Length) throw new InvalidOperationException($"value at {off} is outside the copied script");
            Buffer.BlockCopy(data, 0, payload, off, data.Length);
        }
        var block = targets[script];
        block.Children.Clear();
        block.Children.Add(new RawNode(payload));
        return Bytes.Concat(target[..ObjectHeader.Size], PropStream.Serialize(top));
    }

    /// <summary>The swappable blocks as nodes of an already parsed tree (so they can be changed in place).</summary>
    static List<Prop> SwappableIn(List<PropNode> top, byte[] body)
    {
        var starts = Find(body).Select(g => g.Start).ToHashSet();
        return AllProps(top).Where(p => p.Id == 1 && p.Kind == 0 && starts.Contains(p.Offset + 9 + 8)
                                        && p.Payload() is { Length: > 8 } d && Bytes.U32(d, 0) == 1 && Bytes.U32(d, 4) == 5).ToList();
    }
}
