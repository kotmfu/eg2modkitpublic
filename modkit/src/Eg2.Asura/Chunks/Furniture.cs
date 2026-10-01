namespace Eg2.Asura.Chunks;

/// <summary>
/// One furniture item record inside an fntr table.
///
/// Payload: name\0 NUL-padded so the field is a multiple of 4 bytes (all 209 base
/// records); everything after is laid out from that boundary, so a rename MUST
/// re-pad (an unpadded rename hung the loader). Then three typed text refs
/// [u32 1][u32 0][u32 0xdd6757f2][u32 text_hash] (display name, description,
/// plural; 205/209 records), the gold cost (u32 right after a pi float
/// db 0f 49 40, ~110 bytes in), the item's fnas id (1:1), job/tag refs.
/// </summary>
public sealed class FurnitureRecord
{
    public const uint TextRefType = 0xDD6757F2;
    /// <summary>Text refs are [1][0][table hash][key hash]; this is ModKit's own table ("eg2modkit").</summary>
    public static readonly uint ModTextTable = TextTable.KeyHash("eg2modkit");
    static readonly byte[] Pi = { 0xDB, 0x0F, 0x49, 0x40 };

    public FurnitureRecord(Prop prop) => Prop = prop;

    public Prop Prop { get; private set; }
    public byte[] Payload => Prop.Payload();

    void Replace(byte[] payload) => Prop = new Prop(Prop.Key, Prop.Kind, PropStream.Parse(payload), Prop.Offset);

    public FurnitureRecord Clone() => new(new Prop(Prop.Key, Prop.Kind, PropStream.Parse(Payload), Prop.Offset));

    static byte[] NameField(byte[] name)
    {
        var f = new byte[Bytes.Pad4(name.Length + 1)];
        Buffer.BlockCopy(name, 0, f, 0, name.Length);
        return f;
    }

    int NameFieldLength()
    {
        var p = Payload;
        return Bytes.Pad4(Array.IndexOf(p, (byte)0) + 1);
    }

    public string Name
    {
        get
        {
            var p = Payload;
            return Bytes.Latin1.GetString(p, 0, Array.IndexOf(p, (byte)0));
        }
        set
        {
            var p = Payload;
            var rest = Bytes.Slice(p, NameFieldLength(), p.Length);
            Replace(Bytes.Concat(NameField(Bytes.Latin1.GetBytes(value)), rest));
        }
    }

    int TextRefOffset(int slot)
    {
        var p = Payload;
        int off = NameFieldLength() + slot * 16;
        if (off + 16 > p.Length || Bytes.U32(p, off) != 1 || Bytes.U32(p, off + 4) != 0 || (Bytes.U32(p, off + 8) != TextRefType && Bytes.U32(p, off + 8) != ModTextTable))
            throw new AsuraFormatException($"{Name}: no text reference in slot {slot}");
        return off + 12;
    }

    public bool HasTextRef(int slot)
    {
        try { TextRefOffset(slot); return true; } catch (AsuraFormatException) { return false; }
    }

    /// <summary>0 = display name, 1 = description, 2 = plural.</summary>
    public uint TextHash(int slot = 0) => Bytes.U32(Payload, TextRefOffset(slot));

    public void SetTextHash(uint value, int slot = 0, uint table = TextRefType)
    {
        var buf = Payload;
        int off = TextRefOffset(slot);
        Bytes.PutU32(buf, off, value);
        Bytes.PutU32(buf, off - 4, table);
        Replace(buf);
    }

    int CostOffset()
    {
        var p = Payload;
        int start = Array.IndexOf(p, (byte)0);
        int i = Bytes.Find(p, Pi, start, Math.Min(start + 200, p.Length));
        if (i < 0) throw new AsuraFormatException($"{Name}: cost marker not found");
        return i + 4;
    }

    public bool HasCost { get { try { CostOffset(); return true; } catch (AsuraFormatException) { return false; } } }

    /// <summary>Offset of the gold cost inside <see cref="Payload"/>, or -1.</summary>
    public int CostOffsetOrMissing => HasCost ? CostOffset() : -1;

    public uint Cost
    {
        get => Bytes.U32(Payload, CostOffset());
        set
        {
            var buf = Payload;
            Bytes.PutU32(buf, CostOffset(), value);
            Replace(buf);
        }
    }

    /// <summary>Rewrite every occurrence of a u32 id in the record; returns the count.</summary>
    public int ReplaceU32(uint oldValue, uint newValue)
    {
        var p = Payload;
        var o = Bytes.Le(oldValue);
        int hits = Bytes.Count(p, o);
        if (hits > 0) Replace(Bytes.Replace(p, o, Bytes.Le(newValue)));
        return hits;
    }

    public bool ContainsU32(uint value) => Bytes.Contains(Payload, Bytes.Le(value));

    /// <summary>
    /// The item's build-menu icon: the u32 right before the first 0x80000001 header past the name/text block, =
    /// h31 of "data/graphics/gui/icons/furniture/&lt;icon name&gt;" (lower case, no extension; 384/517 match their own
    /// name, the rest point at another item's icon). A fresh random value shows a white square in-game. 0 if absent.
    /// </summary>
    public uint IconKey => IconKeyOffset is var at and >= 0 ? Bytes.U32(Payload, at) : 0;

    int IconKeyOffset
    {
        get
        {
            var b = Payload;
            for (int p = 100; p + 8 <= b.Length; p++)
                if (Bytes.U32(b, p + 4) == 0x80000001 && Bytes.U32(b, p) > 0xFFFF) return p;
            return -1;
        }
    }

    /// <summary>Overwrites the icon key's own slot (other copies of the value elsewhere in the record stay). False if absent.</summary>
    public bool SetIconKey(uint key)
    {
        int at = IconKeyOffset;
        if (at < 0) return false;
        var buf = (byte[])Payload.Clone();
        Bytes.PutU32(buf, at, key);
        Replace(buf);
        return true;
    }

    /// <summary>Replace the whole payload (structural edits: footprint, slots). The record keeps its key/kind.</summary>
    public void SetPayload(byte[] payload) => Prop = new Prop(Prop.Key, Prop.Kind, new List<PropNode> { new RawNode(payload) }, Prop.Offset);

    /// <summary>Overwrite bytes in place (same length, so the layout never shifts).</summary>
    public void SetBytes(int offset, byte[] data)
    {
        var buf = Payload;
        Buffer.BlockCopy(data, 0, buf, offset, data.Length);
        Replace(buf);
    }

    public byte[] ToBytes() => Prop.ToBytes();
}

/// <summary>
/// fntr: a package's furniture table (every content package has exactly one; tables
/// from all loaded packages are merged). u32 version (7), u32 pad, u32 count, u8 flag (1),
/// then per record u8 1 + property 0x8000009b.
/// </summary>
public sealed class FurnitureTable
{
    public uint Version = 7, Pad;
    public byte Flag = 1;
    public List<FurnitureRecord> Records = new();
    public List<byte[]> Separators = new();

    public static FurnitureTable Parse(byte[] body)
    {
        var t = new FurnitureTable { Version = Bytes.U32(body, 0), Pad = Bytes.U32(body, 4), Flag = body[12] };
        uint count = Bytes.U32(body, 8);
        var pending = Array.Empty<byte>();
        foreach (var n in PropStream.Parse(body, 13))
        {
            if (n is Prop p)
            {
                t.Records.Add(new FurnitureRecord(p));
                t.Separators.Add(pending);
                pending = Array.Empty<byte>();
            }
            else if (n is RawNode r)
            {
                pending = Bytes.Concat(pending, r.Data);
            }
        }
        if (pending.Length > 0) throw new AsuraFormatException($"{pending.Length} trailing bytes after last furniture record");
        if (t.Records.Count != count) throw new AsuraFormatException($"header says {count} records, found {t.Records.Count}");
        return t;
    }

    public FurnitureRecord? Get(string name) => Records.FirstOrDefault(r => r.Name == name);

    public void Add(FurnitureRecord record)
    {
        Records.Add(record);
        Separators.Add(new byte[] { 1 });
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        ms.Write(Bytes.Le(Version, Pad, (uint)Records.Count));
        ms.WriteByte(Flag);
        for (int i = 0; i < Records.Count; i++)
        {
            ms.Write(i < Separators.Count ? Separators[i] : new byte[] { 1 });
            ms.Write(Records[i].ToBytes());
        }
        return ms.ToArray();
    }
}
