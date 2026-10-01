using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Generic lists inside any object: a prop whose children are [u32 n] + n props with the same key (HANDOFF round 26;
/// 15,273 lists in 3,298 of 8,180 objects, every object round-trips). Entries can be removed, duplicated and reordered;
/// what an entry means depends on the type, so the type-specific editors (tasks, slots, requirements...) are safer.
/// </summary>
public static class ObjectLists
{
    public sealed record ListInfo(int At, uint EntryKey, int Count, Prop Node);

    static IEnumerable<Prop> All(IEnumerable<PropNode> ns) { foreach (var n in ns) if (n is Prop p) { yield return p; foreach (var c in All(p.Children)) yield return c; } }

    public static bool IsList(Prop p) => p.Children.Count >= 2 && p.Children[0] is RawNode { Data.Length: 4 } r && Bytes.U32(r.Data, 0) == p.Children.Count - 1
                                        && p.Children[1] is Prop first && p.Children.Skip(1).All(c => c is Prop q && q.Key == first.Key);

    /// <summary>Parse <paramref name="data"/> (an object body from <paramref name="start"/>, or a record payload from 0); null if it doesn't rebuild byte-exact.</summary>
    public static List<PropNode>? Tree(byte[] data, int start)
    {
        try
        {
            var top = PropStream.Parse(data, start);
            return Bytes.Concat(data[..start], PropStream.Serialize(top)).AsSpan().SequenceEqual(data) ? top : null;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidCastException or InvalidOperationException or AsuraFormatException) { return null; }
    }

    public static List<ListInfo> Find(List<PropNode> top) =>
        All(top).Where(IsList).Select(p => new ListInfo(p.Offset, ((Prop)p.Children[1]).Key, p.Children.Count - 1, p)).ToList();

    /// <summary>
    /// Rebuild lists: each edit = (list offset, expected count, new order of original entry indexes; repeats duplicate).
    /// Inner lists (higher offsets) are applied first so duplicated outer entries carry their edited inner lists.
    /// </summary>
    public static byte[] Apply(byte[] data, int start, IEnumerable<(int At, int Count, IReadOnlyList<int> Order)> edits)
    {
        var top = Tree(data, start) ?? throw new InvalidOperationException("this object doesn't rebuild exactly, so its lists can't be edited");
        var lists = Find(top).ToDictionary(l => l.At);
        foreach (var (at, count, order) in edits.OrderByDescending(e => e.At))
        {
            if (!lists.TryGetValue(at, out var l)) throw new InvalidOperationException($"no list at offset {at} (game updated?)");
            if (l.Count != count) throw new InvalidOperationException($"list at {at} has {l.Count} entries, the mod expects {count} (game updated?)");
            if (order.Count == 0) throw new InvalidOperationException($"list at {at}: can't remove every entry (the game may expect at least one)");
            if (order.Any(i => i < 0 || i >= count)) throw new InvalidOperationException($"list at {at}: entry index out of range");
            var entries = l.Node.Children.Skip(1).Cast<Prop>().ToList();
            var seen = new HashSet<int>();
            var rebuilt = order.Select(i => seen.Add(i) ? entries[i] : (Prop)PropStream.Parse(entries[i].ToBytes(), 0).Single()).ToList();
            l.Node.Children.Clear();
            l.Node.Children.Add(new RawNode(Bytes.Le((uint)rebuilt.Count)));
            l.Node.Children.AddRange(rebuilt);
        }
        return Bytes.Concat(data[..start], PropStream.Serialize(top));
    }

    /// <summary>A short readable label for a list entry: its first text, else its first name-like string, else its first numbers.</summary>
    public static string EntryLabel(Prop entry, GameData g)
    {
        var b = entry.ToBytes();
        for (int i = 0; i + 16 <= b.Length; i++)
            if (Bytes.U32(b, i) == 1 && Bytes.U32(b, i + 4) == 0 && g.Text.TryGetValue(Bytes.U32(b, i + 12), out var t))
            {
                var s = ObjectInspector.Clean(t.Text).Replace('\n', ' ');
                return $"\"{(s.Length > 80 ? s[..80] + "…" : s)}\"";
            }
        for (int i = 0; i + 4 <= b.Length; i++)
            if (g.ObjectsById.TryGetValue(Bytes.U32(b, i), out var l)) return $"→ {l[0].Tag} {l[0].Name}";
        var m = System.Text.RegularExpressions.Regex.Match(Bytes.Latin1.GetString(b), "[A-Za-z][A-Za-z0-9_ ]{3,}");
        return m.Success ? m.Value : Convert.ToHexString(b.AsSpan(9, Math.Min(16, b.Length - 9)));
    }
}
