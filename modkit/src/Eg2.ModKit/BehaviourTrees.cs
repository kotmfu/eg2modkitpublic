using System.Text.RegularExpressions;
using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// AI behaviour trees: the AXBT chunk in misc\common.asr ([u32 0][u32 1][u32 0][u32 tree count], then trees). A tree starts
/// [u32 3][u32 tree hash][u32 next free node index]. Its nodes follow in a stream:
/// - a child list opens with [u32 type][u32 type][u32 count][u32 0] (701 Serial, 703 Parallel, 704 Continuous, 801 Timer,
///   802 Loop, 804 Always Succeed, 810 Set Variable), then its children,
/// - a node record is [u32 8][u32 node index][u16 0 or 1][name\0][pad][u16][u8 0][u8 flag][u32 tree hash] + settings (<see cref="Setting"/>);
///   action and condition records are preceded by their class id as [u32 id][u32 id].
/// A record that arrives when the innermost open list is full is that list's owner (a parent is stored after its children).
/// All 95 game trees parse to one root with every list exactly filled. Jobs (rjob) name their tree by hash.
/// </summary>
public static class BehaviourTrees
{
    public sealed class Node
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        public int Offset { get; init; }
        /// <summary>The child list type it owns (701 Serial, 703 Parallel, ...); 0 for a step with no children.</summary>
        public uint ListType { get; set; }
        public List<Node> Children { get; } = new();
        public List<Setting> Settings { get; } = new();
        /// <summary>Plain fields: byte runs that aren't typed settings, each with the number of settings before it.</summary>
        public List<(int Offset, int After, byte[] Bytes)> Plain { get; } = new();
    }

    /// <summary>
    /// One typed setting: [u32 7][u32 category][u32 4][u32 type] value [u32 3][u32 key][20 zero bytes]. Types: 3 f32,
    /// 4 bool (1 byte), 13 two u32, 14 variable reference ([999][0x800N]: the step reads blackboard variable Key),
    /// 17 enum ([u32 value][enum hash 0x576e3cdd]). Category is 3 for constants, 2 for variable references, 0 for type 13.
    /// Plain fields (a bool byte or a u32) can sit between settings; see <see cref="Describe"/>.
    /// </summary>
    public sealed record Setting(int Offset, uint Category, uint Type, string Value, uint Key)
    {
        public override string ToString() => Type switch
        {
            14 => $"variable {(Key == 0 ? "?" : VariableName(Key))}",
            _ => Key != 0 ? $"{Value} (variable {VariableName(Key)})" : Value,
        };
    }

    /// <summary>Variable key hashes whose names are known (KeyHash of the name).</summary>
    public static readonly Dictionary<uint, string> Variables =
        new[] { "IsAssignedVehicle", "IsEscorteeTrapped", "CanDestroyTraps" }.ToDictionary(TextTable.KeyHash);

    /// <summary>Tree hashes whose names are known: KeyHash of the job type name (strings in the exe).</summary>
    public static readonly Dictionary<uint, string> TreeNames =
        new[] { "ConstructFurniture", "RepairFurniture", "SabotageFurniture", "PaintTile", "ZalikaBackfireRevertToWorker", "DeconstructFurniture",
                "SpawnInLair", "StealFurniture", "JumpOffFurniture", "SabotageTrap", "Idle", "UseSmokeBomb" }.ToDictionary(TextTable.KeyHash);
    static string VariableName(uint key) => Variables.TryGetValue(key, out var n) ? n : Bytes.Hex(key);

    static int ValueSize(uint type) => type switch { 3 => 4, 4 => 1, 13 or 14 or 17 => 8, _ => -1 };

    static Setting? ReadSetting(byte[] b, int at, int end)
    {
        if (at + 16 > end || Bytes.U32(b, at) != 7 || Bytes.U32(b, at + 8) != 4) return null;
        uint cat = Bytes.U32(b, at + 4), type = Bytes.U32(b, at + 12);
        int size = ValueSize(type), v = at + 16;
        if (size < 0 || v + size + 8 > end || Bytes.U32(b, v + size) != 3) return null;
        string value = type switch
        {
            3 => BitConverter.ToSingle(b, v).ToString("G6", System.Globalization.CultureInfo.InvariantCulture),
            4 => b[v] != 0 ? "true" : "false",
            13 => $"{Bytes.U32(b, v)}, {Bytes.U32(b, v + 4)}",
            14 => $"kind {Bytes.Hex(Bytes.U32(b, v + 4))}",
            17 => $"enum {Bytes.U32(b, v)}",
            _ => "",
        };
        return new Setting(at, cat, type, value, Bytes.U32(b, v + size + 4));
    }

    /// <summary>Size of a setting record (its value plus the fixed parts), for stepping over it.</summary>
    static int SettingLength(Setting s) => 16 + ValueSize(s.Type) + 4 + 24;

    public sealed record Tree(uint Hash, Node Root, int NodeCount);

    public static readonly Dictionary<uint, string> ListTypes = new()
    {
        [701] = "Serial", [703] = "Parallel", [704] = "Continuous", [801] = "Timer", [802] = "Loop", [804] = "Always Succeed", [810] = "Set Variable",
    };

    static readonly Regex NodeRx = new(@"\x08\x00\x00\x00([\s\S]{4})[\x00\x01]\x00([ -~]{1,80})\x00", RegexOptions.Compiled);

    static bool IsListType(uint t) => t is 701 or 703 or 704 || t is >= 800 and < 830;

    public static List<Tree> Parse(byte[] axbt)
    {
        int count = (int)Bytes.U32(axbt, 12);
        var text = Bytes.Latin1.GetString(axbt);
        // tree headers [3][hash][next index]: real ones have their hash right after the first node record's name
        // (settings like [3][f32 -1][3] look the same otherwise)
        var heads = new List<(int At, uint Hash, int Next)>();
        for (int i = 16; i + 12 <= axbt.Length && heads.Count < count; i++)
        {
            if (Bytes.U32(axbt, i) != 3) continue;
            uint h = Bytes.U32(axbt, i + 4);
            int next = (int)Bytes.U32(axbt, i + 8);
            if (h < 0x10000 || next is < 1 or > 10000) continue;
            var first = NodeRx.Match(text, i + 12);
            int after = first.Index + first.Length;
            if (!first.Success || axbt.AsSpan(after, Math.Min(12, axbt.Length - after)).IndexOf(BitConverter.GetBytes(h)) < 0) continue;
            heads.Add((i, h, next));
            i += 11;
        }
        var trees = new List<Tree>();
        for (int k = 0; k < heads.Count; k++)
        {
            var (start, hash, next) = heads[k];
            int end = k + 1 < heads.Count ? heads[k + 1].At : axbt.Length;
            var records = new Dictionary<int, (Node Node, int Length)>();
            var hashAt = new Dictionary<Node, int>();
            var hb = BitConverter.GetBytes(hash);
            foreach (Match m in NodeRx.Matches(text[start..end]))
            {
                int idx = (int)Bytes.U32(axbt, start + m.Index + 4), after = start + m.Index + m.Length;
                int h = idx < 1 || idx > next ? -1 : axbt.AsSpan(after, Math.Min(12, end - after)).IndexOf(hb);
                if (h < 0) continue;
                var node = new Node { Index = idx, Name = m.Groups[2].Value, Offset = start + m.Index };
                records[start + m.Index] = (node, m.Length);
                hashAt[node] = after + h;
            }
            var stack = new List<(uint Type, int Count, List<Node> Kids)> { (0, -1, new List<Node>()) };
            for (int i = start + 12; i + 16 <= end;)
            {
                if (records.TryGetValue(i, out var rec))
                {
                    var top = stack[^1];
                    if (top.Count >= 0 && top.Kids.Count == top.Count)
                    {
                        stack.RemoveAt(stack.Count - 1);
                        rec.Node.ListType = top.Type;
                        rec.Node.Children.AddRange(top.Kids);
                    }
                    stack[^1].Kids.Add(rec.Node);
                    i += rec.Length;
                    continue;
                }
                uint t = Bytes.U32(axbt, i);
                if (IsListType(t) && Bytes.U32(axbt, i + 4) == t && Bytes.U32(axbt, i + 8) is var n and > 0 and < 64)
                {
                    stack.Add((t, (int)n, new List<Node>()));
                    i += 16;
                    continue;
                }
                i++;
            }
            if (stack.Count != 1 || stack[0].Kids.Count != 1)
                throw new InvalidOperationException($"behaviour tree {Bytes.Hex(hash)}: nesting didn't resolve to one root");
            // settings: the typed values between a step's tree hash and the next record; plain fields in between. A step's
            // own bytes end where the next step's prefix starts (a list header, or an action's [class id][class id][0])
            var starts = records.Keys.Order().ToList();
            foreach (var (node, _) in records.Values)
            {
                int i = hashAt[node] + 4, stop = starts.FirstOrDefault(x => x > node.Offset, end), run = i;
                void Plain(int to) { if (to > run) node.Plain.Add((run, node.Settings.Count, axbt[run..to])); }
                while (i < stop)
                {
                    if (ReadSetting(axbt, i, stop) is { } set) { Plain(i); node.Settings.Add(set); i += SettingLength(set); run = i; continue; }
                    if (i + 12 <= stop && Bytes.U32(axbt, i) is var a and > 0 && Bytes.U32(axbt, i + 4) == a
                        && (IsListType(a) && Bytes.U32(axbt, i + 8) is > 0 and < 64 || a is >= 100 and < 0x10000 && Bytes.U32(axbt, i + 8) == 0))
                        break;
                    i++;
                }
                Plain(i);
            }
            trees.Add(new Tree(hash, stack[0].Kids[0], records.Count));
        }
        return trees;
    }

    /// <summary>Settings an edit can change: floats, bools and enum values (not variable references).</summary>
    public static bool Editable(Setting s) => s.Type is 3 or 4 or 17;

    /// <summary>
    /// One value an edit can change: a typed setting (Field null, Setting = its position) or a plain field (Field = mode,
    /// count, time, watch or compare). Kind: f32, bool, u32, i32. Allowed: the only values the field takes, if limited.
    /// </summary>
    public sealed record Value(string Label, string? Field, int Setting, int Offset, string Kind, string Game, string Help, uint[]? Allowed = null);

    /// <summary>Every value of a step an edit can change.</summary>
    public static List<Value> Values(Node n)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var list = new List<Value>();
        for (int i = 0; i < n.Settings.Count; i++)
        {
            var s = n.Settings[i];
            if (!Editable(s)) continue;
            string kind = s.Type switch { 3 => "f32", 4 => "bool", _ => "u32" };
            list.Add(new Value($"setting {i + 1}", null, i, s.Offset + 16, kind, s.Value.Replace("enum ", ""),
                s.Type switch { 4 => "true or false", 17 => "a whole number (the choice)", _ => "a number, e.g. 2.5" }));
        }
        var first = n.Plain.Count > 0 && n.Plain[0].After == 0 ? n.Plain[0] : default;
        if (n.ListType == 701 && first.Bytes is { Length: >= 4 })
            list.Add(new Value("mode", "mode", 0, first.Offset, "u32", Bytes.U32(first.Bytes, 0).ToString(), "1 = sequence (stops at a failing step), 2 = fallback (tries the next step when one fails), 3 = unknown", new uint[] { 1, 2, 3 }));
        else if (n.ListType == 703 && first.Bytes is { Length: >= 4 })
            list.Add(new Value("mode", "mode", 0, first.Offset, "u32", Bytes.U32(first.Bytes, 0).ToString(), "0 to 3 (meanings unconfirmed; the usual job root uses 2)", new uint[] { 0, 1, 2, 3 }));
        else if (n.ListType == 802 && first.Bytes is { Length: >= 16 })
        {
            list.Add(new Value("count", "count", 0, first.Offset + 8, "i32", BitConverter.ToInt32(first.Bytes, 8).ToString(), "how many times it repeats; -1 = no limit"));
            list.Add(new Value("time", "time", 0, first.Offset + 12, "f32", BitConverter.ToSingle(first.Bytes, 12).ToString("G6", inv), "time limit in seconds; -1 = no limit"));
        }
        else if (n.Settings.Count >= 3 && n.Settings[0].Type == 3 && n.Settings[1].Type == 14 && first.Bytes is { Length: 1 }
                 && n.Plain.FirstOrDefault(p => p.After == 2 && p.Bytes.Length == 4) is { Bytes: { } op } cmp)
        {
            list.Add(new Value("keeps checking", "watch", 0, first.Offset, "bool", first.Bytes[0] != 0 ? "true" : "false", "true = re-checked the whole time its parent runs; false = checked once"));
            list.Add(new Value("comparison", "compare", 0, cmp.Offset, "u32", Bytes.U32(op, 0).ToString(), "4 = equals, 3 = not equals", new uint[] { 3, 4 }));
        }
        return list;
    }

    /// <summary>The value a TreeEdit names, or why it can't be found.</summary>
    public static (Value? Value, string? Error) Find(IReadOnlyList<Tree> trees, TreeEdit e)
    {
        if (trees.FirstOrDefault(t => Bytes.Hex(t.Hash).Equals(e.Tree, StringComparison.OrdinalIgnoreCase)) is not { } tree) return (null, $"tree {e.Tree} not found");
        if (All(tree.Root).FirstOrDefault(n => n.Index == e.Step) is not { } step) return (null, $"tree {e.Tree} has no step {e.Step}");
        string what = e.Field is { Length: > 0 } f ? f : $"setting {e.Setting + 1}";
        var v = Values(step).FirstOrDefault(x => e.Field is { Length: > 0 } ? x.Field == e.Field : x.Field is null && x.Setting == e.Setting);
        if (v is null) return (null, $"step {e.Step} ({step.Name}) has no editable {what}");
        string expect = e.Expect.Replace("enum ", "");
        if (expect.Length > 0 && expect != v.Game) return (null, $"step {e.Step} ({step.Name}) {what} is {v.Game} in the game, expected {expect} (game updated?)");
        return (v, null);
    }

    /// <summary>The new value's bytes (written at the value's Offset). Throws FormatException on a bad value.</summary>
    public static byte[] Encode(Value v, string value)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        value = value.Trim();
        byte[] bytes = v.Kind switch
        {
            "f32" => BitConverter.GetBytes(float.Parse(value, inv)),
            "bool" => new[] { (byte)(bool.Parse(value) ? 1 : 0) },
            "i32" => BitConverter.GetBytes(int.Parse(value, inv)),
            _ => BitConverter.GetBytes(uint.Parse(value.Replace("enum", "").Trim(), inv)),
        };
        if (v.Allowed is { } ok && !ok.Contains(Bytes.U32(bytes, 0))) throw new FormatException($"{v.Label} takes {string.Join(", ", ok)}");
        return bytes;
    }

    /// <summary>Apply edits to an AXBT body in place (it keeps its size). Returns the errors.</summary>
    public static List<string> Apply(byte[] axbt, IEnumerable<TreeEdit> edits)
    {
        var errors = new List<string>();
        var trees = Parse(axbt);
        foreach (var e in edits)
        {
            var (v, err) = Find(trees, e);
            if (v is null) { errors.Add(err!); continue; }
            try { Encode(v, e.Value).CopyTo(axbt, v.Offset); }
            catch (Exception x) when (x is FormatException or OverflowException) { errors.Add($"tree {e.Tree} step {e.Step}: '{e.Value}' isn't valid ({x.Message})"); }
        }
        return errors;
    }

    public static IEnumerable<Node> All(Node n) => new[] { n }.Concat(n.Children.SelectMany(All));

    /// <summary>
    /// Plain fields read by the step's shape. Serial: u32 mode (1 sequence, 2 fallback: the next child runs only if one fails,
    /// 3 unknown). Parallel: u32 mode 0-3. Loop: [u32 0][u32 0][i32 count][f32 time] (-1 = no limit). Conditions
    /// ([u8 watch] f32 variable [u32 compare] value): watch = 1 keeps checking while the parent runs (every condition
    /// under a Parallel), compare 4 = equals, 3 = not equals. Play Animation / Complete Job: u32 animation hash after the enum.
    /// </summary>
    public static string Describe(Node n)
    {
        byte[]? First() => n.Plain.Count > 0 && n.Plain[0].After == 0 ? n.Plain[0].Bytes : null;
        var types = n.Settings.Select(s => s.Type).ToList();
        if (n.ListType == 701 && First() is { Length: >= 4 } sm)
            return Bytes.U32(sm, 0) switch { 1 => "sequence", 2 => "fallback", var m => $"mode {m}" };
        if (n.ListType == 703 && First() is { Length: >= 4 } pm) return $"mode {Bytes.U32(pm, 0)}";
        if (n.ListType == 802 && First() is { Length: >= 16 } lm)
        {
            int count = BitConverter.ToInt32(lm, 8);
            float time = BitConverter.ToSingle(lm, 12);
            return $"{(count < 0 ? "no count limit" : $"{count} times")}, {(time < 0 ? "no time limit" : $"{time:G6} s")}";
        }
        if (types.Count >= 3 && types[0] == 3 && types[1] == 14 && First() is { Length: 1 } w
            && n.Plain.FirstOrDefault(p => p.After == 2 && p.Bytes.Length == 4) is { Bytes: { } op })
        {
            string cmp = Bytes.U32(op, 0) switch { 4 => "=", 3 => "≠", var c => $"compare {c}" };
            var value = n.Settings[2];
            string rhs = value.Type == 13 ? "nothing" : value.ToString();
            return $"{n.Settings[1]} {cmp} {rhs}{(w[0] == 1 ? ", keeps checking" : "")}";
        }
        if (types is [17] && n.Plain.FirstOrDefault(p => p.After == 1) is { Bytes.Length: >= 4 } anim && Bytes.U32(anim.Bytes, 0) != 0)
            return $"{n.Settings[0]}, animation {Bytes.Hex(Bytes.U32(anim.Bytes, 0))}";
        return n.Settings.Count > 0 ? string.Join("; ", n.Settings) : "";
    }

    /// <summary>The tree as indented lines.</summary>
    public static string Outline(Tree t)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(Node n, int depth)
        {
            string kind = n.ListType != 0 && ListTypes.TryGetValue(n.ListType, out var k) && !n.Name.StartsWith(k, StringComparison.OrdinalIgnoreCase) ? $"  ({k})" : "";
            string sets = Describe(n) is { Length: > 0 } d ? $"   [{d}]" : "";
            sb.AppendLine($"{new string(' ', depth * 3)}{n.Name}{kind}{sets}");
            foreach (var c in n.Children) Walk(c, depth + 1);
        }
        Walk(t.Root, 0);
        return sb.ToString();
    }

    /// <summary>Which objects name each tree (jobs mostly), by tree hash.</summary>
    public static Dictionary<uint, List<GameObject>> Users(IEnumerable<Tree> trees, IEnumerable<GameObject> objects)
    {
        var hashes = trees.Select(t => t.Hash).ToHashSet();
        var users = new Dictionary<uint, List<GameObject>>();
        foreach (var o in objects)
            for (int i = 24; i + 4 <= o.Body.Length; i++)   // past the header: an rjob's aux id can equal its tree hash
                if (hashes.Contains(Bytes.U32(o.Body, i)))
                {
                    var h = Bytes.U32(o.Body, i);
                    if (!users.TryGetValue(h, out var l)) users[h] = l = new();
                    if (!l.Contains(o)) l.Add(o);
                }
        return users;
    }
}
