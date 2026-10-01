using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// An objective's (robj) task list: the first prop's child prop 0x1 = [u32 n] + n task props (0x2d). A task is
/// self-contained: header [text ref task][text ref tracker or [1][2][hash]] then its condition blocks and
/// [1][room id] = the "_Activate" step that runs when it starts. Nothing points at a task by index or id (steps don't
/// point back at objectives), so tasks can be removed, reordered or copied from another objective (HANDOFF round 22).
/// </summary>
public static class ObjectiveTasks
{
    public const uint TaskKey = 0x8000002d;

    public sealed record Task(int Index, byte[] Bytes, uint TextKey, uint Room);

    static Prop? List(List<PropNode> top) =>
        top.OfType<Prop>().FirstOrDefault()?.Children.OfType<Prop>().FirstOrDefault(p => p.Id == 1 && p.Children.OfType<Prop>().Any() && p.Children.OfType<Prop>().All(c => c.Key == TaskKey));

    /// <summary>The tasks, or null when this object has no task list (or it doesn't rebuild byte-exact).</summary>
    public static List<Task>? Parse(byte[] body, IReadOnlySet<uint>? rooms = null)
    {
        try
        {
            var top = PropStream.Parse(body, ObjectHeader.Size);
            if (List(top) is not { } list) return null;
            if (!Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(top)).AsSpan().SequenceEqual(body)) return null;
            return list.Children.OfType<Prop>().Select((t, i) =>
            {
                var b = t.ToBytes();
                var head = ((RawNode)t.Children[0]).Data;
                uint text = head.Length >= 16 && Bytes.U32(head, 0) == 1 ? Bytes.U32(head, 12) : 0;
                uint room = 0;
                if (rooms is not null)
                    for (int k = 0; k + 4 <= b.Length && room == 0; k++) if (rooms.Contains(Bytes.U32(b, k))) room = Bytes.U32(b, k);
                return new Task(i, b, text, room);
            }).ToList();
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidCastException or InvalidOperationException or AsuraFormatException) { return null; }
    }

    /// <summary><paramref name="body"/> with its task list replaced by <paramref name="tasks"/> (each a whole task prop, from any objective).</summary>
    public static byte[] Write(byte[] body, IEnumerable<byte[]> tasks)
    {
        var top = PropStream.Parse(body, ObjectHeader.Size);
        var list = List(top) ?? throw new InvalidOperationException("no task list");
        var items = tasks.Select(t => PropStream.Parse(t, 0).Single()).ToList();
        list.Children.Clear();
        list.Children.Add(new RawNode(Bytes.Le((uint)items.Count)));
        list.Children.AddRange(items);
        return Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(top));
    }
}
