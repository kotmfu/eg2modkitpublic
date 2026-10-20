using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Who has a trait (rtrt): the tail of the last raw node in the trait prop (0x13) that ends in
/// [u8 0][u32 1][u32 n][n x u32 character key], keys being minion types, genius actor types or henchmen
/// (Jubei 0x2d3a2cdb owns Begin Flow alone). Found in 534 of 549 traits. Not confirmed in game yet.
/// </summary>
public static class TraitOwners
{
    static (RawNode Node, int At, List<uint> Keys)? Find(List<PropNode> top)
    {
        if (top.OfType<Prop>().FirstOrDefault(p => p.Id == 0x13) is not { } trait) return null;
        foreach (var raw in trait.Children.OfType<RawNode>().Reverse())
        {
            var d = raw.Data;
            for (int n = 1; n <= 200; n++)
            {
                int at = d.Length - 4 * n - 9;
                if (at < 0) break;
                if (d[at] == 0 && Bytes.U32(d, at + 1) == 1 && Bytes.U32(d, at + 5) == n)
                    return (raw, at, Enumerable.Range(0, n).Select(i => Bytes.U32(d, at + 9 + 4 * i)).ToList());
            }
        }
        return null;
    }

    /// <summary>The trait's owner keys, or null when it has no list (or doesn't rebuild byte-exact).</summary>
    public static List<uint>? Parse(byte[] body) =>
        ObjectLists.Tree(body, ObjectHeader.Size) is { } top ? Find(top)?.Keys : null;

    /// <summary>
    /// Adds an "On Spawn" earn condition (chance 0-1) to a trait with one effect and no conditions, whose effect settings
    /// end in a prop (e.g. kind 13 fire): the trait prop's payload is [u32 effects][effects][u32 conditions][conditions]...,
    /// and a condition is [u32 1]["On Spawn" NUL-padded][u32 7][u8 1][u32 1][u32 1][f32 chance], as in Pyromaniac
    /// (0xc1e8807e). Owners roll it when they spawn; the owner list alone grants nothing (in game 2026-10-19).
    /// ponytail: only that one-effect shape; decode effect settings per kind if other traits need it.
    /// </summary>
    public static byte[] AddSpawnRoll(byte[] body, float chance)
    {
        var top = ObjectLists.Tree(body, ObjectHeader.Size) ?? throw new InvalidOperationException("trait doesn't rebuild exactly");
        var trait = top.OfType<Prop>().FirstOrDefault(p => p.Id == 0x13) ?? throw new InvalidOperationException("no trait prop");
        int settings = trait.Children.FindIndex(c => c is Prop);
        if (trait.Children[0] is not RawNode head || Bytes.U32(head.Data, 0) != 1 || settings != 1
            || trait.Children.ElementAtOrDefault(2) is not RawNode { Data.Length: >= 4 } after || Bytes.U32(after.Data, 0) != 0)
            throw new InvalidOperationException("only a trait with one effect (settings in a prop) and no conditions can get a spawn roll");
        var cond = Bytes.Concat(Bytes.Le(1u), Bytes.Le(1u), Bytes.Latin1.GetBytes("On Spawn\0\0\0\0"), Bytes.Le(7u), new byte[] { 1 },
                                Bytes.Le(1u, 1u), BitConverter.GetBytes(chance));
        after.Data = Bytes.Concat(cond, after.Data[4..]);
        return Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(top));
    }

    /// <summary><paramref name="body"/> owned by exactly <paramref name="keys"/> (in order); prop sizes follow.</summary>
    public static byte[] Write(byte[] body, IReadOnlyList<uint> keys)
    {
        if (keys.Count == 0) throw new InvalidOperationException("a trait needs at least one owner");
        var top = ObjectLists.Tree(body, ObjectHeader.Size) ?? throw new InvalidOperationException("trait doesn't rebuild exactly");
        var (raw, at, _) = Find(top) ?? throw new InvalidOperationException("trait has no owner list");
        raw.Data = Bytes.Concat(raw.Data[..(at + 5)], Bytes.Le((uint)keys.Count), Bytes.Le(keys.ToArray()));
        return Bytes.Concat(body[..ObjectHeader.Size], PropStream.Serialize(top));
    }
}
