using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Structural script edits (HANDOFF round 31): remove or copy a node, add or remove a link. Layout: after the template
/// string a graph has [u32 1][u32 node count] (all 2,854 graphs), then its nodes back to back. A node's output pin keeps
/// its links as [u32 count][u32 ?] + count x 49-byte link records ([id][from node][from pin][to node][to pin]
/// [00 ff ff 00] + 25 bytes). Nodes are wrapped in size-prefixed prop blocks, so every edit is a byte splice that also
/// grows/shrinks each enclosing prop's size field. Every edit is re-read and checked before it's accepted.
/// </summary>
public static class FlowGraphEdit
{
    public const int LinkSize = 49;

    /// <summary>Graph by name (or the only graph) in an object body, with its header offset; null if its layout isn't the checked one.</summary>
    static (FlowGraph G, int Header, int End)? Graph(byte[] body, string? name)
    {
        var gs = FlowGraph.Find(body);
        var g = name is null && gs.Count == 1 ? gs[0] : gs.FirstOrDefault(x => x.Name == name);
        if (g is null || g.Nodes.Count == 0) return null;
        int t = Bytes.Latin1.GetString(body, g.Start, g.End - g.Start).IndexOf(g.Template + "\0", StringComparison.Ordinal);
        if (t < 0) return null;
        int end = g.Start + t + g.Template.Length + 1;
        foreach (int h in new[] { g.Start + ((end - g.Start + 3) & ~3), (end + 3) & ~3 })
            if (h + 8 <= body.Length && Bytes.U32(body, h) == 1 && Bytes.U32(body, h + 4) == g.Nodes.Count && g.Nodes[0].Start == h + 8
                && g.Nodes.Zip(g.Nodes.Skip(1)).All(p => p.First.End == p.Second.Start))
                return (g, h, ContainerEnd(body, h, g.End));
        return null;
    }

    /// <summary>End of the innermost prop holding the graph header: the last node ends there, not at the next graph.</summary>
    static int ContainerEnd(byte[] body, int at, int fallback)
    {
        var top = ObjectLists.Tree(body, ObjectHeader.Size);
        int best = fallback;
        void Walk(IEnumerable<PropNode> ns)
        {
            foreach (var p in ns.OfType<Prop>())
            {
                int cs = p.Offset + 9, ce = cs + (int)Bytes.U32(body, p.Offset + 5);
                if (cs <= at && at < ce) { best = Math.Min(best, ce); Walk(p.Children); }
            }
        }
        if (top is not null) Walk(top);
        return best;
    }

    /// <summary>A node's byte range (the last node ends at the graph's container).</summary>
    static (int Start, int End) Range(FlowGraph g, FlowGraph.Node n, int graphEnd) => (n.Start, n == g.Nodes[^1] ? Math.Min(n.End, graphEnd) : n.End);

    public static string? Problem(byte[] body, string? graph) => Graph(body, graph) is null ? "this script's layout isn't one ModKit can restructure safely" : null;

    /// <summary>
    /// Replace body[at, at+remove) with insert, fixing the size of every prop that encloses the change. An insertion
    /// exactly at the end of blocks (appending a node) grows the blocks that contain <paramref name="anchor"/> instead.
    /// </summary>
    static byte[] Splice(byte[] body, int at, int remove, byte[] insert, int anchor = -1)
    {
        var top = ObjectLists.Tree(body, ObjectHeader.Size) ?? throw new InvalidOperationException("the object doesn't rebuild exactly");
        int end = at + remove, delta = insert.Length - remove;
        var enclosing = new List<Prop>();
        void Walk(IEnumerable<PropNode> ns)
        {
            foreach (var p in ns.OfType<Prop>())
            {
                int cs = p.Offset + 9, ce = cs + (int)Bytes.U32(body, p.Offset + 5);
                bool inside = p.Offset >= at && ce <= end && remove > 0;             // removed with the range
                bool encloses = cs <= at && (remove > 0 ? end <= ce : at < ce || anchor >= 0 && at == ce && cs <= anchor);
                bool disjoint = ce <= at || p.Offset >= end && (remove > 0 || p.Offset >= at);
                if (!inside && !encloses && !disjoint) throw new InvalidOperationException($"the change would cut a data block in half (@{p.Offset})");
                if (encloses) { enclosing.Add(p); Walk(p.Children); }
            }
        }
        Walk(top);
        var b = Bytes.Concat(body[..at], insert, body[end..]);
        foreach (var p in enclosing) Bytes.PutU32(b, p.Offset + 5, (uint)(Bytes.U32(b, p.Offset + 5) + delta));
        return b;
    }

    /// <summary>The link list (count offset, first link offset, count) that holds the link at <paramref name="offset"/>.</summary>
    static (int CountAt, int First, int Count)? ListOf(FlowGraph g, byte[] body, FlowGraph.Link link)
    {
        var same = g.Links.Where(l => l.FromNode == link.FromNode && l.FromPin == link.FromPin).Select(l => l.Offset).ToHashSet();
        int first = link.Offset;
        while (same.Contains(first - LinkSize)) first -= LinkSize;
        int n = 0;
        while (same.Contains(first + n * LinkSize)) n++;
        return first - 8 >= 0 && Bytes.U32(body, first - 8) == n ? (first - 8, first, n) : null;
    }

    static uint NewId(FlowGraph g, byte[] body, int k = 1) =>
        g.Nodes.Select(n => n.Id).Concat(g.Links.Select(l => l.Id)).Max() + (uint)k;

    public static byte[] RemoveLink(byte[] body, string? graph, uint linkId)
    {
        var (g, _, _) = Graph(body, graph) ?? throw new InvalidOperationException(Problem(body, graph));
        var l = g.Links.FirstOrDefault(x => x.Id == linkId) ?? throw new InvalidOperationException($"no link {linkId}");
        var list = ListOf(g, body, l) ?? throw new InvalidOperationException($"link {linkId}: its list layout isn't the checked one");
        var b = Splice(body, l.Offset, LinkSize, Array.Empty<byte>());
        Bytes.PutU32(b, list.CountAt, (uint)(list.Count - 1));
        return Check(b, graph, g.Nodes.Count, g.Links.Count - 1);
    }

    /// <summary>A new link on an output that already has at least one link (the new one is a copy with new ends).</summary>
    public static byte[] AddLink(byte[] body, string? graph, uint fromNode, uint fromPin, uint toNode, uint toPin)
    {
        var (g, _, _) = Graph(body, graph) ?? throw new InvalidOperationException(Problem(body, graph));
        if (g.Nodes.All(n => n.Id != toNode)) throw new InvalidOperationException($"no node {toNode}");
        var model = g.Links.FirstOrDefault(l => l.FromNode == fromNode && l.FromPin == fromPin)
                    ?? throw new InvalidOperationException("that output has no links yet; ModKit can only add links to outputs that already have one");
        var list = ListOf(g, body, model) ?? throw new InvalidOperationException("this output's link list isn't the checked layout");
        var rec = body.AsSpan(list.First, LinkSize).ToArray();
        Bytes.PutU32(rec, 0, NewId(g, body));
        Bytes.PutU32(rec, 12, toNode);
        Bytes.PutU32(rec, 16, toPin);
        var b = Splice(body, list.First, 0, rec);
        Bytes.PutU32(b, list.CountAt, (uint)(list.Count + 1));
        return Check(b, graph, g.Nodes.Count, g.Links.Count + 1);
    }

    /// <summary>Remove a node and every link into or out of it (not the start/end nodes 0 and 1).</summary>
    public static byte[] RemoveNode(byte[] body, string? graph, uint nodeId)
    {
        if (nodeId is 0 or 1) throw new InvalidOperationException("the start and end of a script can't be removed");
        var (g, h, ge) = Graph(body, graph) ?? throw new InvalidOperationException(Problem(body, graph));
        if (g.Nodes.All(n => n.Id != nodeId)) throw new InvalidOperationException($"no node {nodeId}");
        var own = g.Nodes.First(x => x.Id == nodeId);
        var (os, oe) = Range(g, own, ge);
        // links into the node, and links out of it that are stored in other nodes
        foreach (var l in g.Links.Where(l => (l.ToNode == nodeId || l.FromNode == nodeId) && (l.Offset < os || l.Offset >= oe)).ToList())
            body = RemoveLink(body, graph, l.Id);
        (g, h, ge) = Graph(body, graph)!.Value;
        var (s, e) = Range(g, g.Nodes.First(x => x.Id == nodeId), ge);
        int links = g.Links.Count(l => l.Offset >= s && l.Offset < e);
        var b = Splice(body, s, e - s, Array.Empty<byte>());
        Bytes.PutU32(b, h + 4, (uint)(g.Nodes.Count - 1));
        return Check(b, graph, g.Nodes.Count - 1, g.Links.Count - links);
    }

    /// <summary>
    /// Copy a node (with its settings and its outgoing links) under a new id; the copy goes last in the graph and has no
    /// incoming links yet (add them with <see cref="AddLink"/>). Returns the new body and node id.
    /// </summary>
    public static (byte[] Body, uint NewNode) CopyNode(byte[] body, string? graph, uint nodeId)
    {
        if (nodeId is 0 or 1) throw new InvalidOperationException("the start and end of a script can't be copied");
        var (g, h, ge) = Graph(body, graph) ?? throw new InvalidOperationException(Problem(body, graph));
        var n = g.Nodes.FirstOrDefault(x => x.Id == nodeId) ?? throw new InvalidOperationException($"no node {nodeId}");
        var (s, e) = Range(g, n, ge);
        if (g.Links.Any(l => l.Offset >= s && l.Offset < e && l.FromNode != nodeId))
            throw new InvalidOperationException("this node also stores other nodes' links, so it can't be copied");
        var blob = body.AsSpan(s, e - s).ToArray();
        uint id = NewId(g, body);
        // the node's own id: [00 ff ff 00][u32 3][u32 id]
        int idAt = -1;
        for (int i = 0; i + 12 <= blob.Length; i++)
            if (blob[i] == 0 && blob[i + 1] == 0xff && blob[i + 2] == 0xff && blob[i + 3] == 0 && Bytes.U32(blob, i + 4) == 3 && Bytes.U32(blob, i + 8) == nodeId) { idAt = i + 8; break; }
        if (idAt < 0) throw new InvalidOperationException("couldn't find the node's id");
        Bytes.PutU32(blob, idAt, id);
        int k = 2, links = 0;
        foreach (var l in g.Links.Where(l => l.Offset >= s && l.Offset < e))
        {
            int at = l.Offset - s;
            Bytes.PutU32(blob, at, NewId(g, body, k++));
            if (l.FromNode == nodeId) Bytes.PutU32(blob, at + 4, id);
            links++;
        }
        // appended after the last node: the game's graphs always start with Start (#0) and End (#1)
        if (Range(g, g.Nodes[^1], ge).End != ge) throw new InvalidOperationException("this script has data after its last node, so a copy can't be appended");
        var b = Splice(body, ge, 0, blob, anchor: h);
        Bytes.PutU32(b, h + 4, (uint)(g.Nodes.Count + 1));
        return (Check(b, graph, g.Nodes.Count + 1, g.Links.Count + links), id);
    }

    static byte[] Check(byte[] b, string? graph, int nodes, int links)
    {
        if (ObjectLists.Tree(b, ObjectHeader.Size) is null) throw new InvalidOperationException("internal: the edited object doesn't re-read");
        var r = Graph(b, graph) ?? throw new InvalidOperationException("internal: the edited script doesn't re-read");
        if (r.G.Nodes.Count != nodes || r.G.Links.Count != links)
            throw new InvalidOperationException($"internal: expected {nodes} nodes/{links} links, re-read {r.G.Nodes.Count}/{r.G.Links.Count}");
        return b;
    }
}
