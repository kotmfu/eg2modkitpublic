using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Furniture record (fntr payload) footprint and job slots (HANDOFF round 24).
/// Footprint: at top level (no enclosing prop in any of the 517 records) [u32 n] + n cells, each prop 0x10 with a 44-byte
/// payload [u32 (y &lt;&lt; 16) | x][u32 2][flags...]; always a row-major W x H rectangle starting at (0,0).
/// Slots: prop 1 / 5 / 1 = [u32 n] + n prop 0x11 (job link, animation points, slot name); 229 records have one.
/// Locker capacity = slot count (round 3), so more slots = more minions per item.
/// </summary>
public static class FurnitureShape
{
    const uint CellKey = 0x80000010, SlotKey = 0x80000011;
    const int CellSize = 9 + 44;

    public static int CellListAt(byte[] p)
    {
        for (int at = 0; at + 13 <= p.Length; at++)
        {
            uint n = Bytes.U32(p, at);
            if (n is 0 or > 1024 || Bytes.U32(p, at + 4) != CellKey || p[at + 8] != 0 || Bytes.U32(p, at + 9) != 44 || at + 4 + n * CellSize > p.Length) continue;
            bool ok = true;
            for (int k = 0; k < n && ok; k++) ok = Bytes.U32(p, at + 4 + k * CellSize) == CellKey;
            if (ok) return at;
        }
        return -1;
    }

    /// <summary>(width, height) in tiles, or null.</summary>
    public static (int W, int H)? Footprint(byte[] p)
    {
        int at = CellListAt(p);
        if (at < 0) return null;
        int n = (int)Bytes.U32(p, at);
        var cells = Enumerable.Range(0, n).Select(k => Bytes.U32(p, at + 4 + k * CellSize + 9)).ToList();
        return ((int)cells.Max(c => c & 0xffff) + 1, (int)cells.Max(c => c >> 16) + 1);
    }

    /// <summary>Payload with a W x H footprint; each new cell copies the nearest old cell's flags.</summary>
    public static byte[] SetFootprint(byte[] p, int w, int h)
    {
        if (w is < 1 or > 32 || h is < 1 or > 32) throw new InvalidOperationException($"footprint {w} x {h}: each side must be 1..32 tiles");
        int at = CellListAt(p);
        var (ow, oh) = Footprint(p) ?? throw new InvalidOperationException("no footprint in this record");
        int n = (int)Bytes.U32(p, at);
        byte[] Old(int x, int y) => p.AsSpan(at + 4 + (Math.Min(y, oh - 1) * ow + Math.Min(x, ow - 1)) * CellSize, CellSize).ToArray();
        var cells = new List<byte[]>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = Old(x, y);
                Bytes.PutU32(c, 9, (uint)(y << 16 | x));
                cells.Add(c);
            }
        return Bytes.Concat(p[..at], Bytes.Le((uint)cells.Count), Bytes.Concat(cells.ToArray()), p[(at + 4 + n * CellSize)..]);
    }

    static Prop? SlotList(List<PropNode> top) =>
        top.OfType<Prop>().Where(a => a.Id == 1).SelectMany(a => a.Children.OfType<Prop>().Where(b => b.Id == 5))
            .SelectMany(b => b.Children.OfType<Prop>().Where(c => c.Id == 1))
            .FirstOrDefault(c => c.Children.Count >= 2 && c.Children[0] is RawNode { Data.Length: 4 } r && c.Children.Skip(1).All(x => x is Prop { Key: SlotKey })
                                 && Bytes.U32(r.Data, 0) == c.Children.Count - 1);

    /// <summary>
    /// One footprint tile. The 40 bytes after the (y&lt;&lt;16|x) word: [0] kind (2 solid, 0 open floor inside the item,
    /// 1 door); [36] 2 = keep clear (where minions stand to use it: the row in front of a Whiteboard, the ring round a
    /// shelf); [20]/[24]/[28]/[32] one per edge (back/right/front/left): 1 on beds' back edge, 2 on doors' faces and
    /// access edges, so probably "needs a wall" / "needs an opening" (not confirmed). The rest are undecoded flags.
    /// </summary>
    /// <remarks><see cref="At"/> = payload offset of byte [0]; kind is At, keep clear At+36, edges At+20/24/28/32.</remarks>
    public sealed record Cell(int X, int Y, byte Kind, bool KeepClear, byte[] Edges, byte[] Raw, int At)
    {
        public string KindName => Kind switch { 2 => "solid", 0 => "open floor", 1 => "door", _ => $"kind {Kind}" };
    }

    /// <summary>A job slot: the job (rjob id) done there, the points a minion stands at to do it (tile units: x across,
    /// y into the item, 0 = the first row's centre line), and the slot's name.</summary>
    public sealed record Slot(string Name, uint Job, List<Point> Points, int JobAt);

    /// <summary>A stand point; <see cref="XAt"/>/<see cref="ZAt"/> are the payload offsets of its x and z floats (y = -z).</summary>
    public sealed record Point(float X, float Y, int XAt, int ZAt);

    public static readonly int[] EdgeOffsets = { 20, 24, 28, 32 };
    public const int KeepClearOffset = 36;

    public static List<Cell> Cells(byte[] p)
    {
        var list = new List<Cell>();
        int at = CellListAt(p);
        if (at < 0) return list;
        for (int k = 0, n = (int)Bytes.U32(p, at); k < n; k++)
        {
            int c = at + 4 + k * CellSize + 9;
            uint xy = Bytes.U32(p, c);
            var raw = p[(c + 4)..(c + 44)];
            list.Add(new Cell((int)(xy & 0xffff), (int)(xy >> 16), raw[0], raw[36] == 2, new[] { raw[20], raw[24], raw[28], raw[32] }, raw, c + 4));
        }
        return list;
    }

    /// <summary>The job slots with their use points; a slot whose layout doesn't match is left out.</summary>
    public static List<Slot> Slots(byte[] p)
    {
        var list = new List<Slot>();
        // an item can have several slot lists (the Whiteboard: one per "Researching" job, each for different minions)
        static IEnumerable<Prop> All(IEnumerable<PropNode> nodes) =>
            nodes.OfType<Prop>().SelectMany(q => q.Key == SlotKey ? new[] { q } : All(q.Children));
        foreach (var slot in All(PropStream.Parse(p)))
        {
            var b = slot.Payload();
            int at0 = slot.Offset + 9;   // payload offset in the record
            if (b.Length < 32 || Bytes.U32(b, 24) != 1) continue;
            int n = (int)Bytes.U32(b, 28), pos = 32;
            var points = new List<Point>();
            for (int k = 0; k < n && pos + 9 <= b.Length; k++)
            {
                if (Bytes.U32(b, pos) != 0x80000004) break;
                int len = (int)Bytes.U32(b, pos + 5);
                if (len >= 32 && pos + 9 + len <= b.Length)   // [2][2][1][2][0][x][height][z]
                    points.Add(new Point(BitConverter.ToSingle(b, pos + 9 + 20), -BitConverter.ToSingle(b, pos + 9 + 28), at0 + pos + 9 + 20, at0 + pos + 9 + 28));
                pos += 9 + len;
            }
            int s = pos + 1, e = s;
            while (e < b.Length && b[e] >= 32 && b[e] < 127) e++;
            list.Add(new Slot(Bytes.Latin1.GetString(b, s, e - s), Bytes.U32(b, 4), points, at0 + 4));
        }
        return list;
    }

    /// <summary>Number of job slots, or -1 when the item has none.</summary>
    public static int SlotCount(byte[] p) => SlotList(PropStream.Parse(p)) is { } l ? l.Children.Count - 1 : -1;

    /// <summary>Payload with <paramref name="n"/> slots: extra slots are copies of the last one, removed ones come off the end.</summary>
    public static byte[] SetSlotCount(byte[] p, int n)
    {
        if (n is < 1 or > 64) throw new InvalidOperationException($"{n} slots: must be 1..64");
        var top = PropStream.Parse(p);
        var l = SlotList(top) ?? throw new InvalidOperationException("this item has no job slots");
        var slots = l.Children.Skip(1).Cast<Prop>().ToList();
        while (slots.Count > n) slots.RemoveAt(slots.Count - 1);
        while (slots.Count < n) slots.Add((Prop)PropStream.Parse(slots[^1].ToBytes(), 0).Single());
        l.Children.Clear();
        l.Children.Add(new RawNode(Bytes.Le((uint)n)));
        l.Children.AddRange(slots);
        var outp = PropStream.Serialize(top);
        if (SlotCount(outp) != n || Footprint(outp) != Footprint(p)) throw new InvalidOperationException("slot edit didn't rebuild cleanly");
        return outp;
    }
}
