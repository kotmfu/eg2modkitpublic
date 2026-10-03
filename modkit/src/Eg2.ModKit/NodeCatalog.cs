using System.Text.RegularExpressions;
using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Every script node type in the game: how often it's used, the pins links use on it, the strings stored in it (value
/// and pin names), the kinds of objects it refers to, and where it's used. Built from all loaded objects.
/// </summary>
public sealed class NodeCatalog
{
    public sealed record Use(GameObject Object, string Script, uint Node);

    public sealed class Entry
    {
        public uint Type { get; init; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public Dictionary<string, int> Inputs { get; } = new();
        public Dictionary<string, int> Outputs { get; } = new();
        public Dictionary<string, int> Strings { get; } = new();
        public Dictionary<string, int> Refers { get; } = new();
        public Dictionary<string, int> Templates { get; } = new();
        public Dictionary<string, int> Owners { get; } = new();
        public List<Use> Uses { get; } = new();
        public HashSet<GameObject> Objects { get; } = new();
        /// <summary>Triggered by a flow link (an action step) rather than read for its data.</summary>
        public bool IsAction => Inputs.ContainsKey("InputFlow") || Inputs.Keys.Any(k => k.EndsWith("[InputFlow]"));

        public override string ToString() => $"{Name} ({Count})";
    }

    public List<Entry> Entries { get; } = new();
    public FlowGraph.Names Names { get; }

    NodeCatalog(FlowGraph.Names names) => Names = names;

    /// <summary>Uses kept per type; the rest are only counted.</summary>
    public const int MaxUses = 200;

    public static NodeCatalog Build(IReadOnlyList<GameObject> objects, FlowGraph.Names? names = null)
    {
        var scripted = objects.Where(o => o.Body.AsSpan().IndexOf("FlowGraph/"u8) >= 0).ToList();
        names ??= FlowGraph.Names.Learn(scripted.Select(o => o.Body));
        var byId = new Dictionary<uint, string>();
        foreach (var o in objects) if (!o.IsRecord) byId.TryAdd(o.ObjectId, o.Tag);
        var cat = new NodeCatalog(names);
        var map = new Dictionary<uint, Entry>();
        static void Add(Dictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
        string Pin(uint p)
        {
            var shown = names.Pins.TryGetValue(p, out var n) ? n.Trim('{', '}') : null;
            var inner = FlowGraph.Names.Internal.GetValueOrDefault(p);
            return shown is null ? inner ?? Bytes.Hex(p) : inner is null || inner.Equals(shown.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ? shown : $"{shown} [{inner}]";
        }

        foreach (var o in scripted)
        {
            var b = o.Body;
            foreach (var gr in FlowGraph.Find(b))
            {
                var typeOf = gr.Nodes.ToDictionary(n => n.Id, n => n.Type);
                foreach (var n in gr.Nodes)
                {
                    if (n.Id is 0 or 1) continue;   // the graph's own start and end
                    if (!map.TryGetValue(n.Type, out var e)) map[n.Type] = e = new Entry { Type = n.Type, Name = names.Type(n.Type) };
                    e.Count++;
                    e.Objects.Add(o);
                    if (e.Uses.Count < MaxUses) e.Uses.Add(new Use(o, gr.Name, n.Id));
                    Add(e.Templates, gr.Template);
                    Add(e.Owners, o.Tag);
                    foreach (var s in FlowGraph.Strings(b, n.Start, n.End).Select(s => s.Text).Where(Readable).Distinct()) Add(e.Strings, s);
                    var refs = new HashSet<string>();
                    for (int i = n.Start + 4; i + 4 <= n.End; i++)
                        if (byId.TryGetValue(Bytes.U32(b, i), out var tag)) { refs.Add(tag); i += 3; }
                    foreach (var t in refs) Add(e.Refers, t);
                }
                foreach (var l in gr.Links)
                {
                    if (typeOf.TryGetValue(l.FromNode, out var ft) && l.FromNode is not (0 or 1) && map.TryGetValue(ft, out var fe)) Add(fe.Outputs, Pin(l.FromPin));
                    if (typeOf.TryGetValue(l.ToNode, out var tt) && l.ToNode is not (0 or 1) && map.TryGetValue(tt, out var te)) Add(te.Inputs, Pin(l.ToPin));
                }
            }
        }
        foreach (var e in map.Values) e.Name = Label(e);
        cat.Entries.AddRange(map.Values.OrderByDescending(e => e.Count));
        return cat;
    }

    /// <summary>Node types identified by hand.</summary>
    public static readonly Dictionary<uint, string> Known = new()
    {
        [0xadaf25a2] = "Branch: if Condition, FlowTrue else FlowFalse",
        [0x5cd5544d] = "Constant value",
        [0xd4e0f0bc] = "Start objective",
        [0xc3376493] = "Lair temperature offset",
        [0x10e28e6e] = "Count henchmen in the lair ({MinionType} InLair)",
    };

    /// <summary>Strings worth showing: names and notes, not 2-3 letter runs that are really float bytes.</summary>
    static readonly Regex Word = new(@"^(?:[A-Z][a-z]+[A-Za-z0-9]*|[A-Z]{3,})(?:[ _\-.][A-Za-z0-9()]+)*$|^[A-Z][a-z]+\.?\s.{3,}$", RegexOptions.Compiled);
    static bool Readable(string s) { var t = s.Trim('{', '}'); return t.Length >= 3 && Word.IsMatch(t); }

    static string Label(Entry e)
    {
        if (Known.TryGetValue(e.Type, out var k)) return k;
        var words = e.Strings.Where(x => x.Value * 5 >= e.Count).OrderByDescending(x => x.Value).Select(x => x.Key.Trim('{', '}')).Where(w => !w.Contains(' ') || w.Length < 24)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        return (e.IsAction ? "Do: " : "Get: ") + (words.Count > 0 ? string.Join(", ", words) : Bytes.Hex(e.Type));
    }

    static string Top(Dictionary<string, int> d, int n = 12) =>
        d.Count == 0 ? "-" : string.Join(", ", d.OrderByDescending(x => x.Value).Take(n).Select(x => $"{x.Key} ({x.Value})"));

    /// <summary>A readable page for one node type.</summary>
    public static string Describe(Entry e) =>
        $"""
        {e.Name}
        Type {Bytes.Hex(e.Type)}: {e.Count:N0} uses in {e.Objects.Count:N0} objects

        Inputs (links into it): {Top(e.Inputs)}
        Outputs (links out of it): {Top(e.Outputs)}
        Stored strings (values, pin names): {Top(e.Strings, 20)}
        Refers to objects of class: {Top(e.Refers)}
        Used in object classes: {Top(e.Owners)}
        Script templates: {Top(e.Templates, 6)}

        Used in:
        {string.Join(Environment.NewLine, e.Uses.Take(40).Select(u => $"  {u.Object.Tag} {u.Object.Name}  /  {u.Script}  (node #{u.Node & 0xfff})"))}{(e.Count > 40 ? $"{Environment.NewLine}  … {e.Count - 40:N0} more" : "")}
        """;
}
