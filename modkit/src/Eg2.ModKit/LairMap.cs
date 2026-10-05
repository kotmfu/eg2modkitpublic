using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// The diggable grid of a lair: envs\basedefinitions\&lt;lair&gt;.base, one big ENTI (HANDOFF round 41). Its property
/// tree holds 7 floors (keys vary by lair): 12 raw bytes (u32 floor, width, height), then width*height cells
/// (row-major): u32 flags, u32 type, u32 row, u32 floor, u32 column, then a tail that repeats the rock type as a
/// (type, companion) pair. Only rock tiers are editable so far.
/// </summary>
public sealed class LairMap
{
    /// <summary>The rock rings round the start, easiest (centre) to hardest (outside): cell type and its tail companion.</summary>
    public static readonly (int Tier, uint Type, uint Pair)[] Tiers =
    {
        (1, 0x497004c7, 0x8f47c667), (2, 0x48f78ca2, 0x48013bd3), (3, 0x48f25963, 0xd0d4ba54), (4, 0xd55a674f, 0xf3a34ec0),
    };

    public sealed record Floor(int Index, int Width, int Height, List<Prop> Cells);

    readonly AsuraArchive _arc;
    readonly Chunk _enti;
    readonly List<PropNode> _top;
    Prop? _grid;
    public List<Floor> Floors { get; } = new();
    /// <summary>Pre-placed furniture (the ruined casinos): [row][floor][column] like cells, fnas = the item's asset set.</summary>
    public List<PlacedObject> Objects { get; } = new();

    public sealed record PlacedObject(uint Id, uint Fnas, int Row, int Floor, int Column, int FaceX, int FaceY, Prop Node);

    LairMap(AsuraArchive arc, Chunk enti, List<PropNode> top) { _arc = arc; _enti = enti; _top = top; }

    public static LairMap Parse(byte[] file) => FromArchive(AsuraArchive.FromBytes(file));

    /// <summary>Class of the per-lair settings objects in misc\common.asr's lair BLUE chunk.</summary>
    public const uint LairSettingsClass = 0x6431F764;

    /// <summary>Settings entries of the dev test levels in the lair BLUE chunk (JustAPlane, TileGallery, Island_Test,
    /// lighting and VFX tests...): no island select or save names them, so a new lair's entry can take their bytes.</summary>
    public static readonly uint[] SpareSettings =
        { 0x22ff087c, 0xd973b485, 0x380c8287, 0xcfe6d8b0, 0x8e4ad0bc, 0x26c264e4, 0xb1f2b514, 0x3c2c5565, 0x3be28d84, 0x66ce95d9, 0x518165e1 };

    /// <summary>Spares used only when no pair of <see cref="SpareSettings"/> has room: large_island_200x200 (entry 0, beside spare
    /// Island_Test), named by nothing outside its own entry.</summary>
    public static readonly uint[] LastResortSettings = { 0x85201846 };

    const uint StringClass = 0x55f89b99;

    /// <summary>Probe: the settings chunk with the first spare test level's entry under another id (same size, same place).</summary>
    public static byte[]? WithSpareRenamed(byte[] blue, uint newId)
    {
        for (int i = 20; i + 16 <= blue.Length; i++)
            if (Bytes.U32(blue, i + 4) == 0x0D && Bytes.U32(blue, i) == Bytes.U32(blue, i + 8) && Bytes.U32(blue, i) == SpareSettings[0])
            {
                var outp = (byte[])blue.Clone();
                Bytes.PutU32(outp, i, newId);
                Bytes.PutU32(outp, i + 8, newId);
                return outp;
            }
        return null;
    }

    /// <summary>
    /// misc\common.asr's lair BLUE chunk with a settings entry for a new lair: <paramref name="fromId"/>'s copied as
    /// <paramref name="newId"/>, its island (file name and folder path) renamed to <paramref name="stem"/>. The chunk:
    /// u32 1, 0, 1, root class, count; then count objects [id][0x0D][id][class][member count][members...] back to back,
    /// with no byte lengths. Strings: [key][3][0x55f89b99][0][4][len][chars].
    /// Nothing else in the chunk may move or the menu art is lost (round 53: dropping spares and appending shifted the
    /// entries after them; round 56: a spare renamed in place is fine). So the new entry takes the place of a run of
    /// adjacent spare test-level entries (<see cref="SpareSettings"/>): the entry, padded to fit with an unused string
    /// member, then a 65-byte stub (a copy of one of the small entries under an unused id) so the count stays the same.
    /// Null when the chunk has no <paramref name="fromId"/>, or no spare run is big enough.
    /// </summary>
    public static (byte[] Blue, string Island, int Dropped)? WithLairSettings(byte[] blue, uint fromId, uint newId, string fromIsland, string stem)
    {
        var starts = new List<int>();
        for (int i = 20; i + 16 <= blue.Length; i++)
            if (Bytes.U32(blue, i + 4) == 0x0D && Bytes.U32(blue, i) == Bytes.U32(blue, i + 8) && Bytes.U32(blue, i) != 0) starts.Add(i);
        if (starts.Count != Bytes.U32(blue, 16)) return null;
        int End(int k) => k + 1 < starts.Count ? starts[k + 1] : blue.Length;
        var entries = starts.Select((s, k) => blue[s..End(k)]).ToList();
        int at = entries.FindIndex(e => Bytes.U32(e, 0) == fromId && Bytes.U32(e, 12) == LairSettingsClass);
        if (at < 0) return null;
        if (entries.Any(e => Bytes.U32(e, 0) == newId)) throw new InvalidOperationException($"lair settings {newId:x8} already exist");
        var stub = entries.FirstOrDefault(e => e.Length == 65 && Bytes.U32(e, 12) == LairSettingsClass);
        if (stub is null) return null;
        var entry = Renamed(entries[at], fromIsland, stem);
        Bytes.PutU32(entry, 0, newId);
        Bytes.PutU32(entry, 8, newId);
        // a string member to copy the layout of (key, type 3 before the string class)
        int str = -1;
        for (int i = 20; i + 16 <= entry.Length && str < 0; i++)
            if (Bytes.U32(entry, i) == StringClass && Bytes.U32(entry, i + 4) == 0 && Bytes.U32(entry, i + 8) == 4) str = i - 8;
        if (str < 0) return null;
        const int PadHeader = 24;   // key, 3, string class, 0, 4, length
        // two adjacent spares (the entry and the stub take their two slots) that hold entry + pad member + stub;
        // pairs of the original spares first, then pairs using a last-resort one
        bool Spare(int k, bool last) => SpareSettings.Contains(Bytes.U32(entries[k], 0)) || last && LastResortSettings.Contains(Bytes.U32(entries[k], 0));
        foreach (var (k0, last) in Enumerable.Range(0, entries.Count - 1).Select(k => (k, false)).Concat(Enumerable.Range(0, entries.Count - 1).Select(k => (k, true))))
        {
            int k1 = k0 + 1;
            if (!Spare(k0, last) || !Spare(k1, last)) continue;
            int room = End(k1) - starts[k0], pad = room - entry.Length - stub.Length - PadHeader;
            if (pad < 0) continue;
            var padMember = new byte[PadHeader + pad];
            Array.Copy(entry, str, padMember, 0, 20);
            Bytes.PutU32(padMember, 0, Eg2.Asura.Chunks.TextTable.KeyHash("eg2modkit_padding"));
            Bytes.PutU32(padMember, 20, (uint)pad);
            for (int i = 0; i < pad; i++) padMember[PadHeader + i] = (byte)'_';
            var mine = Bytes.Concat(entry, padMember);
            Bytes.PutU32(mine, 16, Bytes.U32(entry, 16) + 1);
            var filler = (byte[])stub.Clone();
            uint fillerId = Eg2.Asura.Chunks.TextTable.KeyHash(stem + "_unused");
            Bytes.PutU32(filler, 0, fillerId);
            Bytes.PutU32(filler, 8, fillerId);
            return (Bytes.Concat(blue[..starts[k0]], mine, filler, blue[End(k1)..]), stem, 2);
        }
        return null;

        // every string member naming the source island, with the name swapped (length prefix rewritten)
        static byte[] Renamed(byte[] e, string from, string to)
        {
            var parts = new List<byte[]>();
            int last = 0;
            for (int i = 0; i + 16 <= e.Length; i++)
            {
                if (Bytes.U32(e, i) != StringClass || Bytes.U32(e, i + 4) != 0 || Bytes.U32(e, i + 8) != 4) continue;
                int n = (int)Bytes.U32(e, i + 12);
                if (n <= 0 || i + 16 + n > e.Length) continue;
                string text = System.Text.Encoding.ASCII.GetString(e, i + 16, n);
                if (text.IndexOf(from, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var repl = System.Text.Encoding.ASCII.GetBytes(System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(from), to.Replace("$", "$$"), System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                parts.Add(e[last..(i + 12)]);
                parts.Add(Bytes.Le((uint)repl.Length));
                parts.Add(repl);
                last = i + 16 + n;
                i = last - 1;
            }
            parts.Add(e[last..]);
            return Bytes.Concat(parts.ToArray());
        }
    }

    /// <summary>A .base file as another lair: its own lair id (bsnf +17, KeyHash of the file stem) changed from
    /// <paramref name="from"/>'s to <paramref name="stem"/>'s, and optionally the world-map region its header lists for the
    /// source island (rmlr id) swapped for another. Null when the id isn't where expected.</summary>
    public static byte[]? Renamed(byte[] file, string from, string stem, uint oldRegion = 0, uint newRegion = 0)
    {
        var arc = AsuraArchive.FromBytes(file);
        var bsnf = arc.First("bsnf");
        if (bsnf is null || bsnf.Body.Length < 21 || BitConverter.ToUInt32(bsnf.Body, 17) != Eg2.Asura.Chunks.TextTable.KeyHash(from)) return null;
        BitConverter.GetBytes(Eg2.Asura.Chunks.TextTable.KeyHash(stem)).CopyTo(bsnf.Body, 17);
        if (oldRegion != 0)
            for (int i = 21; i + 4 <= bsnf.Body.Length; i++)
                if (BitConverter.ToUInt32(bsnf.Body, i) == oldRegion) BitConverter.GetBytes(newRegion).CopyTo(bsnf.Body, i);
        return arc.ToBytes(compressed: arc.Compressed);
    }

    /// <summary>A lair .base, or a loaded save (<see cref="SaveFile.Load"/>): saves carry the same grid, with the game's progress.</summary>
    public static LairMap FromArchive(AsuraArchive arc)
    {
        var enti = arc.Chunks.Where(c => c.Tag == "ENTI").MaxBy(c => c.Body.Length) ?? throw new InvalidOperationException("no ENTI chunk: not a lair map");
        var top = ObjectLists.Tree(enti.Body, 0) ?? throw new InvalidOperationException("the lair map doesn't rebuild exactly");
        // block keys differ between lairs (1d/44/6, 1e/45/7...): root -> its first block -> the grid; a floor is a child
        // starting with [u32 floor, width, height] followed by width*height cells with one key
        var root = top.OfType<Prop>().FirstOrDefault() ?? throw new InvalidOperationException("lair map: empty");
        var grid = root.Props().FirstOrDefault()?.Props().FirstOrDefault() ?? throw new InvalidOperationException("lair map: no grid block");
        var map = new LairMap(arc, enti, top);
        foreach (var f in grid.Props())
        {
            if (f.Children.Count == 0 || f.Children[0] is not RawNode { Data.Length: 12 } h) continue;
            int w = (int)Bytes.U32(h.Data, 4), hgt = (int)Bytes.U32(h.Data, 8);
            var key = f.Props().GroupBy(c => c.Key).MaxBy(g => g.Count());
            if (key is null || (long)w * hgt != key.Count()) continue;
            map.Floors.Add(new Floor((int)Bytes.U32(h.Data, 0), w, hgt, key.ToList()));
        }
        if (map.Floors.Count == 0) throw new InvalidOperationException("lair map: no floors found");
        map._grid = grid;
        // objects: [u32 count][u8 1][u32 id] before the first, [u8 1][u32 id] before each other one
        for (int i = 1; i < grid.Children.Count; i++)
            if (grid.Children[i] is Prop o && grid.Children[i - 1] is RawNode { Data: { Length: 5 or 9 } r } && r[^5] == 1 && o.Payload() is { Length: > 28 } q
                && Bytes.U32(q, 12) < 16)
                map.Objects.Add(new PlacedObject(Bytes.U32(r, r.Length - 4), Bytes.U32(q, 4), (int)Bytes.U32(q, 8), (int)Bytes.U32(q, 12), (int)Bytes.U32(q, 16),
                                                 (int)Bytes.U32(q, 20), (int)Bytes.U32(q, 24), o));
        // keep only the main list (the most common key)
        if (map.Objects.Count > 0)
        {
            uint key = map.Objects.GroupBy(o => o.Node.Key).MaxBy(g => g.Count())!.Key;
            map.Objects.RemoveAll(o => o.Node.Key != key);
        }
        return map;
    }

    public static uint TypeOf(Prop cell) => Bytes.U32(cell.Payload(), 4);
    public static uint FlagsOf(Prop cell) => Bytes.U32(cell.Payload(), 0);
    /// <summary>1-4 for rock, 0 for anything else.</summary>
    public static int TierOf(Prop cell) { uint t = TypeOf(cell); return Tiers.FirstOrDefault(x => x.Type == t).Tier; }

    /// <summary>Set the rock tier of every rock cell in the rectangle (inclusive); dug cells, deposits and anything
    /// that isn't one of the four tiers stay as they are. Returns how many cells changed.</summary>
    public int SetTier(int floor, int x0, int y0, int x1, int y1, int tier)
    {
        var f = Floors.FirstOrDefault(x => x.Index == floor) ?? throw new ArgumentException($"no floor {floor}");
        var to = Tiers.FirstOrDefault(t => t.Tier == tier);
        if (to.Tier == 0) throw new ArgumentException($"tier {tier}: must be 1-4");
        int changed = 0;
        for (int y = Math.Max(0, y0); y <= Math.Min(f.Height - 1, y1); y++)
            for (int x = Math.Max(0, x0); x <= Math.Min(f.Width - 1, x1); x++)
            {
                var cell = f.Cells[y * f.Width + x];
                var p = cell.Payload();
                var from = Tiers.FirstOrDefault(t => t.Type == Bytes.U32(p, 4));
                if (from.Tier == 0 || from.Tier == tier || (Bytes.U32(p, 0) & 8) != 0) continue;   // not rock, same, or dug (flag bit 3)
                Bytes.PutU32(p, 4, to.Type);
                // tropical lairs repeat the type in the tail as (type, companion); the arctic one has a per-cell hash there
                int at = p.AsSpan(20).IndexOf(Bytes.Le(from.Type, from.Pair));
                if (at >= 0) { Bytes.PutU32(p, 20 + at, to.Type); Bytes.PutU32(p, 24 + at, to.Pair); }
                cell.Children.Clear();
                cell.Children.Add(new RawNode(p));
                changed++;
            }
        return changed;
    }

    /// <summary>The type of a cell the player has dug out (from a save: rock cells become this when excavated).</summary>
    /// <summary>A cell's type is its room (from saves: beds stand on barracks cells, safes on vault cells...; furniture
    /// records list the rooms they're allowed in). Plain dug-out space is corridor.</summary>
    public static readonly (string Name, uint Type)[] Rooms =
    {
        ("Corridor", 0x2a5b912a), ("Power Station", 0xecacefcf), ("Barracks", 0xdddf7f89), ("Mess Hall", 0x29eb7171),
        ("Vault", 0xe58aa2f2), ("Control Room", 0xa3275078), ("Armoury", 0x731de7c9), ("Prison", 0xc276dccb),
        ("Laboratory", 0x6f00f6f3), ("Training Room", 0x627743f5), ("Archive", 0x728da182), ("Infirmary", 0xa92c5365),
        ("Staff Room", 0xd4db647b), ("Casino (Hotel)", 0xe4cbb274), ("Inner Sanctum", 0x6b9b4ad3),
        ("Workshop (Oceans)", 0xd36ac811), ("Test Chamber (Oceans)", 0xbac20f8c),
    };
    public const uint Corridor = 0x2a5b912a, PowerStation = 0xecacefcf;
    public static string? RoomName(uint type) => Rooms.FirstOrDefault(r => r.Type == type).Name;
    public static uint RoomType(string name) => Rooms.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Type is var t and not 0
        ? t : throw new ArgumentException($"unknown room '{name}' (one of: {string.Join(", ", Rooms.Select(r => r.Name))})");
    // an excavated cell's tail as the game saves it, after the u32 region id: u32 3, 81 00 14, u32 0x0a, then the
    // (type, companion) pair (always tier 1 + 0x7e1d10d0, whatever the rock was), u32 0, u8 1
    static readonly byte[] DugTail = Convert.FromHexString("03000000810014" + "0A000000" + "C7047049" + "D0101D7E" + "00000000" + "01");

    /// <summary>Dig out every rock cell in the rectangle (X = column, Y = row, inclusive) as <paramref name="room"/> (corridor =
    /// plain dug space), the way saves store a dug cell: the room type, flags bit 9 off and bit 4 on, the dug tail with the
    /// region id of an already dug neighbour when there is one (the game recomputes regions anyway). Cells that are
    /// already a room change to this room. Returns the count.</summary>
    public int Dig(int floor, int x0, int y0, int x1, int y1, uint room = Corridor)
    {
        var f = Floors.FirstOrDefault(x => x.Index == floor) ?? throw new ArgumentException($"no floor {floor}");
        uint region = 0;
        for (int y = Math.Max(0, y0 - 1); y <= Math.Min(f.Height - 1, y1 + 1) && region == 0; y++)
            for (int x = Math.Max(0, x0 - 1); x <= Math.Min(f.Width - 1, x1 + 1); x++)
            {
                var c = f.Cells[y * f.Width + x];
                if (TierOf(c) == 0 && TypeOf(c) != 0x430bd860 && TypeOf(c) != 0x0cf2f3d8) { region = Bytes.U32(c.Payload(), 20); break; }
            }
        int changed = 0;
        for (int y = Math.Max(0, y0); y <= Math.Min(f.Height - 1, y1); y++)
            for (int x = Math.Max(0, x0); x <= Math.Min(f.Width - 1, x1); x++)
            {
                var cell = f.Cells[y * f.Width + x];
                var p = cell.Payload();
                if (RoomName(TypeOf(cell)) is not null)
                {
                    if (TypeOf(cell) == room) continue;
                    Bytes.PutU32(p, 4, room);
                    cell.Children.Clear();
                    cell.Children.Add(new RawNode(p));
                    changed++;
                    continue;
                }
                if (TierOf(cell) == 0) continue;   // only rock
                uint flags = (Bytes.U32(p, 0) & ~0x200u) | 0x10;
                var dug = Bytes.Concat(Bytes.Le(flags, room), p[8..20], Bytes.Le(region == 0 ? Bytes.U32(p, 20) : region), DugTail);
                cell.Children.Clear();
                cell.Children.Add(new RawNode(dug));
                changed++;
            }
        return changed;
    }

    public const uint Outside = 0x430bd860, Edge = 0x0cf2f3d8, Spot = 0x48f218d8;

    /// <summary>A cell of <paramref name="type"/> from this map to copy (the most common length, so the usual form).</summary>
    Prop? TemplateCell(uint type) => Floors.SelectMany(f => f.Cells).Where(c => TypeOf(c) == type)
        .GroupBy(c => c.Payload().Length).MaxBy(g => g.Count())?.First();

    /// <summary>Turn cells in the rectangle into copies of a cell of another kind from this map, keeping each cell's own
    /// row/floor/column and region id. <paramref name="which"/> picks the cells to change. Returns the count.</summary>
    int Paint(int floor, int x0, int y0, int x1, int y1, uint type, Func<Prop, bool> which)
    {
        var f = Floors.FirstOrDefault(x => x.Index == floor) ?? throw new ArgumentException($"no floor {floor}");
        var t = TemplateCell(type) ?? throw new ArgumentException($"this map has no {type:x8} cell to copy");
        var tp = t.Payload();
        int changed = 0;
        for (int y = Math.Max(0, y0); y <= Math.Min(f.Height - 1, y1); y++)
            for (int x = Math.Max(0, x0); x <= Math.Min(f.Width - 1, x1); x++)
            {
                var cell = f.Cells[y * f.Width + x];
                if (!which(cell) || TypeOf(cell) == type) continue;
                var p = cell.Payload();
                var n = (byte[])tp.Clone();
                Array.Copy(p, 8, n, 8, 12);                 // row, floor, column
                Bytes.PutU32(n, 20, Bytes.U32(p, 20));      // region
                cell.Children.Clear();
                cell.Children.Add(new RawNode(n));
                changed++;
            }
        return changed;
    }

    static bool IsRock(Prop c) => TierOf(c) > 0;

    /// <summary>Rock -> gold seam cells (the yellow spots the game places in the outer rock).</summary>
    public int Gold(int floor, int x0, int y0, int x1, int y1) => Paint(floor, x0, y0, x1, y1, Spot, IsRock);

    /// <summary>Rock -> the lair's edge rock (probably undiggable): seals an area off.</summary>
    public int Wall(int floor, int x0, int y0, int x1, int y1) => Paint(floor, x0, y0, x1, y1, Edge, IsRock);

    /// <summary>Outside, edge, dug or seam cells -> rock of a tier (grows the lair or refills a dug area).</summary>
    public int Fill(int floor, int x0, int y0, int x1, int y1, int tier)
    {
        var to = Tiers.FirstOrDefault(t => t.Tier == tier);
        if (to.Tier == 0) throw new ArgumentException($"tier {tier}: must be 1-4");
        return Paint(floor, x0, y0, x1, y1, to.Type, c => TypeOf(c) is Outside or Edge or Spot || RoomName(TypeOf(c)) is not null);
    }

    /// <summary>Place a copy of <paramref name="template"/> (an object from any lair map, same item and facing) at
    /// row/column on a floor: every [row][floor][column] triple near the template's spot moves with it.</summary>
    public PlacedObject Place(PlacedObject template, int floor, int row, int column, bool keepVersion = false)
    {
        if (_grid is null || Objects.Count == 0) throw new InvalidOperationException("this map has no object list to add to");
        var p = template.Node.Payload();
        int dr = row - template.Row, dc = column - template.Column;
        for (int i = 0; i + 12 <= p.Length; i++)
        {
            int r = (int)Bytes.U32(p, i), fl = (int)Bytes.U32(p, i + 4), c = (int)Bytes.U32(p, i + 8);
            if (fl != template.Floor || Math.Abs(r - template.Row) > 16 || Math.Abs(c - template.Column) > 16) continue;
            Bytes.PutU32(p, i, (uint)(r + dr));
            Bytes.PutU32(p, i + 4, (uint)floor);
            Bytes.PutU32(p, i + 8, (uint)(c + dc));
            i += 11;
        }
        uint id = Objects.Max(o => o.Id) + 1;
        var node = new Prop(keepVersion ? template.Node.Key : Objects[0].Node.Key, template.Node.Kind, new List<PropNode> { new RawNode(p) });   // the key is the object version
        int last = _grid.Children.IndexOf(Objects[^1].Node);
        _grid.Children.Insert(last + 1, new RawNode(Bytes.Concat(new byte[] { 1 }, Bytes.Le(id))));
        _grid.Children.Insert(last + 2, node);
        // the count sits in front of the first object's id
        var head = (RawNode)_grid.Children[_grid.Children.IndexOf(Objects[0].Node) - 1];
        Bytes.PutU32(head.Data, head.Data.Length - 9, (uint)(Objects.Count + 1));
        var placed = new PlacedObject(id, template.Fnas, row, floor, column, template.FaceX, template.FaceY, node);
        Objects.Add(placed);
        return placed;
    }

    /// <summary>The cells an object covers, relative to its anchor (column, row offsets): the [row][floor][column] triples
    /// in its record near the anchor (the same ones <see cref="Place"/> moves).</summary>
    public static List<(int DX, int DY)> Footprint(PlacedObject o)
    {
        var p = o.Node.Payload();
        var cells = new HashSet<(int, int)> { (0, 0) };
        for (int i = 0; i + 12 <= p.Length; i++)
        {
            int r = (int)Bytes.U32(p, i), fl = (int)Bytes.U32(p, i + 4), c = (int)Bytes.U32(p, i + 8);
            if (fl != o.Floor || Math.Abs(r - o.Row) > 16 || Math.Abs(c - o.Column) > 16) continue;
            cells.Add((c - o.Column, r - o.Row));
            i += 11;
        }
        return cells.ToList();
    }

    /// <summary>An object record read from elsewhere (e.g. a save), usable as a <see cref="Place"/> template.</summary>
    public static PlacedObject FromRecord(uint key, byte[] p) =>
        new(0, Bytes.U32(p, 4), (int)Bytes.U32(p, 8), (int)Bytes.U32(p, 12), (int)Bytes.U32(p, 16), (int)Bytes.U32(p, 20), (int)Bytes.U32(p, 24),
            new Prop(key, 0, new List<PropNode> { new RawNode(p) }));

    public uint ObjectKey => Objects.Count > 0 ? Objects[0].Node.Key : 0;

    /// <summary>Take a pre-placed object out of the map (the list's count and the id in front of the first object follow).</summary>
    public void Remove(PlacedObject o)
    {
        if (_grid is null || !Objects.Contains(o)) throw new ArgumentException("not an object of this map");
        if (Objects.Count == 1) throw new InvalidOperationException("can't remove the map's last object");
        var head = (RawNode)_grid.Children[_grid.Children.IndexOf(Objects[0].Node) - 1];
        if (o == Objects[0])
        {
            // the next object's [01][id] moves into the head (count, 01, id)
            var next = (RawNode)_grid.Children[_grid.Children.IndexOf(Objects[1].Node) - 1];
            Array.Copy(next.Data, 1, head.Data, head.Data.Length - 4, 4);
            _grid.Children.Remove(next);
        }
        else _grid.Children.RemoveAt(_grid.Children.IndexOf(o.Node) - 1);
        _grid.Children.Remove(o.Node);
        Objects.Remove(o);
        Bytes.PutU32(head.Data, head.Data.Length - 9, (uint)Objects.Count);
    }

    /// <summary>Floor sizes, e.g. "0:260x230 1:100x150 ..."; a save matches the lair whose .base has the same shape.</summary>
    public string Shape => string.Join(" ", Floors.Select(f => $"{f.Index}:{f.Width}x{f.Height}"));

    /// <summary>
    /// Add whole entities (ENTI bodies: [u32 1][u32 0][u32 id][u32 kind]...; characters are kind 0x8003 minions / 0x8004
    /// others, type hash @56) after the map's own, e.g. a character copied from a save with its 0x8007 companion.
    /// Experimental (HANDOFF round 59).
    /// </summary>
    public int AddEntities(IReadOnlyList<byte[]> bodies)
    {
        var ids = _arc.Chunks.Where(c => c.Tag == "ENTI" && c.Body.Length >= 12).Select(c => Bytes.U32(c.Body, 8)).ToHashSet();
        foreach (var b in bodies)
            if (b.Length < 16 || Bytes.U32(b, 0) != 1) throw new ArgumentException("not an entity record");
            else if (!ids.Add(Bytes.U32(b, 8))) throw new ArgumentException($"entity id {Bytes.U32(b, 8):x} is already on this map");
        int at = _arc.Chunks.FindLastIndex(c => c.Tag == "ENTI") + 1;
        _arc.Chunks.InsertRange(at, bodies.Select(b => new Chunk("ENTI", b)));
        return bodies.Count;
    }

    /// <summary>Replaces the body of entity <paramref name="id"/> (the body keeps that id).</summary>
    public void SetEntity(uint id, byte[] body)
    {
        var c = _arc.Chunks.FirstOrDefault(c => c.Tag == "ENTI" && c.Body.Length >= 12 && Bytes.U32(c.Body, 8) == id)
                ?? throw new ArgumentException($"entity {id:x} is not on this map");
        if (Bytes.U32(body, 8) != id) throw new ArgumentException("the new body has another id");
        c.Body = body;
    }

    /// <summary>The map's lair id (bsnf +17 = KeyHash of the lair stem); a save carries its lair's.</summary>
    public uint LairId => Bytes.U32(_arc.First("bsnf")!.Body, 17);

    /// <summary>Every entity on the map (ENTI bodies).</summary>
    public IEnumerable<byte[]> Entities => _arc.Chunks.Where(c => c.Tag == "ENTI" && c.Body.Length >= 16).Select(c => c.Body);

    /// <summary>The AI block (/1e/1b in Caine Key, Montañas Gemelas, Icicle Point and their saves); null where it isn't
    /// known (Crown Gold's older map format).</summary>
    Prop? AiBlock => _top.OfType<Prop>().FirstOrDefault(p => p.Id == 0x1e)?.Children.OfType<Prop>().FirstOrDefault(p => p.Id == 0x1b);

    /// <summary>The agent squads in the AI block: each whole entry ([01][u32 id][u32 1] + prop 0x3eb), as AddSquad takes.</summary>
    public List<byte[]> Squads()
    {
        var list = new List<byte[]>();
        if (AiBlock?.Payload() is not { Length: >= 5 } b || b[0] == 0) return list;
        int n = (int)Bytes.U32(b, 1), pos = 5;
        for (int k = 0; k < n; k++)
        {
            int len = 9 + 9 + (int)Bytes.U32(b, pos + 9 + 5);
            list.Add(b[pos..(pos + len)]);
            pos += len;
        }
        return list;
    }

    /// <summary>The highest entity or squad id on the map (new ones go above it).</summary>
    public uint MaxId() => Entities.Select(e => Bytes.U32(e, 8)).Concat(Squads().Select(s => Bytes.U32(s, 1))).DefaultIfEmpty(0u).Max();

    /// <summary>
    /// Add an agent squad to the map's AI block (grid /1e/1b). The block starts [u8 has groups]; with groups:
    /// [u32 squad count] + squads ([01][u32 id][u32 1] + prop 0x3eb), then [u32 civilian group count] + groups
    /// ([01][id][u32 0] + prop 0x28), then the rest (what a fresh map has after its 00). <paramref name="entry"/> is one
    /// whole squad: the 9-byte head + the 0x3eb prop. Experimental (HANDOFF round 59).
    /// </summary>
    public void AddSquad(byte[] entry)
    {
        var ai = AiBlock ?? throw new ArgumentException("agents can't go on this map yet (its AI block isn't known: Crown Gold)");
        var b = ai.Payload();
        byte[] outp;
        if (b[0] == 0) outp = Bytes.Concat(new byte[] { 1 }, Bytes.Le(1u), entry, Bytes.Le(0u), b[1..]);
        else
        {
            int n = (int)Bytes.U32(b, 1), pos = 5;
            for (int k = 0; k < n; k++) pos += 9 + 9 + (int)Bytes.U32(b, pos + 9 + 5);
            outp = Bytes.Concat(b[..1], Bytes.Le((uint)(n + 1)), b[5..pos], entry, b[pos..]);
        }
        ai.Children.Clear();
        ai.Children.AddRange(PropStream.Parse(outp, 0));
        if (!ai.Payload().AsSpan().SequenceEqual(outp)) throw new ArgumentException("squad didn't rebuild cleanly");
    }

    /// <summary>The archive with the edits written into it (for a save: pass to <see cref="SaveFile.Save"/>).</summary>
    public AsuraArchive Archive()
    {
        _enti.Body = PropStream.Serialize(_top);
        return _arc;
    }

    /// <summary>The whole .base file again, with the edits (same compression as the original).</summary>
    public byte[] ToBytes() => Archive().ToBytes(compressed: _arc.Compressed);
}
