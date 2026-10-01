using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Who may do a job (rjob): a prop 0x1 list = [u32 n][u32 key] prop 0xE, [u32 key] prop 0xE, ... where key is a minion
/// type hash (<see cref="Requirements.MinionTypes"/>) or another character kind (geniuses, henchmen, agents: 60+
/// keys, unnamed), and prop 0xE (126 bytes) that character's settings for the job. 74 of 310 jobs have one; jobs
/// without it are open to anyone. Jobs are shared by every furniture slot that links them (HANDOFF round 24).
/// </summary>
public static class JobTypes
{
    public sealed record Entry(uint Key, Prop Settings)
    {
        /// <summary>
        /// Settings byte 77: 1 = this character may do the job. Read from the data, not confirmed in game: robots and the
        /// Abomination are 0 on Eating/Sleeping/stat restoring, Biologist and Scientist 1 on Researching, Engineer 1 on
        /// Crafting. Types not in the list get the game's default for the job.
        /// </summary>
        public bool Allowed => Settings.Payload() is { Length: > 77 } p && p[77] == 1;
    }

    static Prop? List(List<PropNode> top)
    {
        IEnumerable<Prop> All(IEnumerable<PropNode> ns) { foreach (var n in ns) if (n is Prop p) { yield return p; foreach (var c in All(p.Children)) yield return c; } }
        return All(top).FirstOrDefault(p => p.Id == 1 && p.Children.Count >= 2 && p.Children[0] is RawNode { Data.Length: 8 } && p.Children[1] is Prop { Id: 0xe });
    }

    /// <summary>The allowed characters, or null when the job has no list (or doesn't rebuild byte-exact).</summary>
    public static List<Entry>? Parse(byte[] body)
    {
        try
        {
            var top = PropStream.Parse(body, ObjectHeader.Size);
            if (List(top) is not { } l) return null;
            if (!Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(top)).AsSpan().SequenceEqual(body)) return null;
            var list = new List<Entry>();
            for (int i = 0; i + 1 < l.Children.Count; i += 2)
            {
                var d = ((RawNode)l.Children[i]).Data;
                list.Add(new Entry(Bytes.U32(d, d.Length - 4), (Prop)l.Children[i + 1]));
            }
            return list.Count == Bytes.U32(((RawNode)l.Children[0]).Data, 0) && l.Children.Count == list.Count * 2 ? list : null;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidCastException or InvalidOperationException or AsuraFormatException) { return null; }
    }

    /// <summary>
    /// <paramref name="body"/> allowing exactly <paramref name="keys"/> (in order). Kept keys keep their settings; new
    /// ones copy the settings of the job's first entry.
    /// </summary>
    public static byte[] Write(byte[] body, IReadOnlyList<uint> keys)
    {
        if (keys.Count == 0) throw new InvalidOperationException("a job needs at least one character type (or no list at all)");
        var top = PropStream.Parse(body, ObjectHeader.Size);
        var l = List(top) ?? throw new InvalidOperationException("job has no character list");
        var old = Parse(body) ?? throw new InvalidOperationException("job list can't be read safely");
        var template = old[0].Settings.ToBytes();
        l.Children.Clear();
        for (int i = 0; i < keys.Count; i++)
        {
            l.Children.Add(new RawNode(i == 0 ? Bytes.Le((uint)keys.Count, keys[0]) : Bytes.Le(keys[i])));
            var keep = old.FirstOrDefault(e => e.Key == keys[i]);
            l.Children.Add(PropStream.Parse(keep is null ? template : keep.Settings.ToBytes(), 0).Single());
        }
        return Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(top));
    }
}
