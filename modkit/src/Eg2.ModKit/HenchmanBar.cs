using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// The HUD's genius and henchman bar: GUAT component F_characterSelect (name 0x85b32cbb) in gui\main.asr. The game
/// draws 6 fixed spaces (a 25x25 genius placeholder and five t_hudBar_button squares in a row with a 90 gap) on a
/// backplate, and the portraits fill them in order. The component header lists each widget as
/// [u8 0][u32 child count][u32 parent index] in widget order (a pre-order tree). More slots = more squares in the row,
/// and every backplate piece and both rows widened by one 115-unit pitch per extra slot.
/// </summary>
public static class HenchmanBar
{
    public const string File = @"gui\main.asr";
    public const int GameSlots = 6;
    const uint Component = 0x85b32cbb, Row = 0x02105993, LastSlot = 0x03d2fcc8;
    const uint ComponentKey = 0x80000008, WidgetKey = 0x80000009;
    const float Pitch = 115;

    /// <summary>Widgets whose width grows by (extra slots x pitch x factor): positioner, shadow plate and its mid piece,
    /// plate and its mid piece, indent, slot row, and the portrait row. The portrait row is centred in the indent and
    /// scaled 0.65, so it grows by 1/0.65 as much to keep its left end (the genius) in place.</summary>
    static readonly (uint Widget, float Width, float Factor)[] Widths =
    {
        (0xcee990c5, 826, 1), (0xdfa5aacd, 750, 1), (0x9b44ea84, 746, 1), (0x5d3c049f, 750, 1), (0x8c947d5a, 770, 1),
        (0xd2b7e0d8, 730, 1), (Row, 730, 1), (0x18db4456, 1060, 1 / 0.65f),
    };

    /// <summary>Root-level widgets placed by their centre (x at +28 of the transform): positioner, shadow plate, plate.</summary>
    static readonly (uint Widget, float X)[] Centred = { (0xcee990c5, 270), (0xdfa5aacd, 267), (0x5d3c049f, 270) };

    static uint Name(Prop widget) => widget.Children.OfType<Prop>().First().Children[0] is RawNode r ? Bytes.U32(r.Data, 0) : 0;

    /// <summary>The GUAT chunk body with the bar holding <paramref name="slots"/> spaces.</summary>
    public static byte[] Apply(byte[] guat, int slots)
    {
        var top = PropStream.Parse(guat, 12);
        var comp = top.OfType<Prop>().FirstOrDefault(p => p.Key == ComponentKey && p.Children[0] is RawNode h && Bytes.U32(h.Data, 0) == Component)
                   ?? throw new InvalidOperationException("henchman bar: HUD layout not found (game updated?)");
        var head = ((RawNode)comp.Children[0]).Data;
        var widgets = comp.Children.OfType<Prop>().Where(p => p.Key == WidgetKey).ToList();
        int n = widgets.Count, row = widgets.FindIndex(w => Name(w) == Row), last = widgets.FindIndex(w => Name(w) == LastSlot);
        if (Bytes.U32(head, 8) != n || head.Length < 12 + 9 * n || row < 0 || last < 0 || Bytes.U32(head, 12 + 9 * last + 5) != row)
            throw new InvalidOperationException("henchman bar: HUD layout changed (game updated?)");
        int extra = slots - GameSlots;
        if (extra <= 0) return guat;

        var table = Enumerable.Range(0, n).Select(i => head[(12 + 9 * i)..(21 + 9 * i)]).ToList();
        foreach (var e in table)
            if (Bytes.U32(e, 5) is var parent && parent != 0xffffffff && parent > last) Bytes.PutU32(e, 5, parent + (uint)extra);
        Bytes.PutU32(table[row], 1, Bytes.U32(table[row], 1) + (uint)extra);
        table.InsertRange(last + 1, Enumerable.Range(0, extra).Select(_ => (byte[])table[last].Clone()));
        var newHead = Bytes.Concat(head[..8], Bytes.Le((uint)(n + extra)), Bytes.Concat(table.ToArray()), head[(12 + 9 * n)..]);
        comp.Children[0] = new RawNode(newHead);

        int at = comp.Children.IndexOf(widgets[last]);
        for (int i = extra; i >= 1; i--)
        {
            var copy = (Prop)PropStream.Parse(widgets[last].ToBytes())[0];
            var name = (RawNode)copy.Children.OfType<Prop>().First().Children[0];
            Bytes.PutU32(name.Data, 0, TextTable.KeyHash($"eg2modkit_henchman_slot_{i}"));
            comp.Children.Insert(at + 1, copy);
        }

        foreach (var (id, width, factor) in Widths)
        {
            var w = widgets.First(x => Name(x) == id);
            var old = BitConverter.GetBytes(width);
            var hits = w.Children.OfType<RawNode>().SelectMany(r => Enumerable.Range(0, r.Data.Length - 3)
                .Where(i => r.Data.AsSpan(i, 4).SequenceEqual(old)).Select(i => (r, i))).ToList();
            if (hits.Count != 1) throw new InvalidOperationException($"henchman bar: width {width} of widget {id:x8} not found (game updated?)");
            BitConverter.GetBytes(width + extra * Pitch * factor).CopyTo(hits[0].r.Data, hits[0].i);
        }
        // the plates are placed by their centre, so they grew both ways: move them right by half the growth
        foreach (var (id, x) in Centred)
        {
            var t = (RawNode)widgets.First(w => Name(w) == id).Children.OfType<Prop>().First().Children[0];
            if (t.Data.Length != 39 || BitConverter.ToSingle(t.Data, 28) != x)
                throw new InvalidOperationException($"henchman bar: position of widget {id:x8} not found (game updated?)");
            BitConverter.GetBytes(x + extra * Pitch / 2).CopyTo(t.Data, 28);
        }
        return Bytes.Concat(guat[..12], PropStream.Serialize(top));
    }
}
