namespace Eg2.Asura;

/// <summary>Raised for a chunk too small to be a data object (e.g. the 13-byte empty fntr).</summary>
public sealed class NotAnObjectException : Exception
{
    public NotAnObjectException(string message) : base(message) { }
}

/// <summary>
/// Fixed 24-byte header of every EG2 data object (lowercase chunk):
/// u32 version (10), u32 pad0, u32 object_id, u32 pad1 (usually 0), u32 package_id, u32 aux_id.
/// The chunk tag is the class; aux_id is a near-unique per-object id, not a class id.
/// </summary>
public sealed class ObjectHeader
{
    public const int Size = 24;
    public uint Version, Pad0, ObjectId, Pad1, PackageId, AuxId;

    public static ObjectHeader Parse(byte[] body)
    {
        if (body.Length < Size) throw new NotAnObjectException($"body of {body.Length} bytes is too short for an object header");
        return new ObjectHeader
        {
            Version = Bytes.U32(body, 0), Pad0 = Bytes.U32(body, 4), ObjectId = Bytes.U32(body, 8),
            Pad1 = Bytes.U32(body, 12), PackageId = Bytes.U32(body, 16), AuxId = Bytes.U32(body, 20),
        };
    }

    public byte[] ToBytes() => Bytes.Le(Version, Pad0, ObjectId, Pad1, PackageId, AuxId);
}

public abstract class PropNode
{
    public int Offset { get; init; } = -1;
    public abstract byte[] ToBytes();
}

/// <summary>Bytes that are not a recognisable property header.</summary>
public sealed class RawNode : PropNode
{
    public RawNode(byte[] data, int offset = -1) { Data = data; Offset = offset; }
    public byte[] Data { get; set; }
    public override byte[] ToBytes() => Data;
}

/// <summary>A property: u32 key (0x8000NNNN), u8 kind, u32 length, payload. Length is recomputed on write.</summary>
public sealed class Prop : PropNode
{
    public const uint KeyMask = 0x80000000;

    public Prop(uint key, byte kind, List<PropNode> children, int offset = -1)
    {
        Key = key; Kind = kind; Children = children; Offset = offset;
    }

    public uint Key { get; }
    public byte Kind { get; }
    public List<PropNode> Children { get; }
    public uint Id => Key & ~KeyMask;

    public byte[] Payload() => Bytes.Concat(Children.Select(c => c.ToBytes()).ToArray());

    public override byte[] ToBytes()
    {
        var body = Payload();
        var head = new byte[9];
        Bytes.PutU32(head, 0, Key);
        head[4] = Kind;
        Bytes.PutU32(head, 5, (uint)body.Length);
        return Bytes.Concat(head, body);
    }

    public IEnumerable<Prop> Props() => Children.OfType<Prop>();
}

/// <summary>
/// The self-describing property stream used by EG2 data objects. Payloads mix
/// nested properties with plain scalars/arrays, so this is a heuristic, LOSSLESS
/// walker: anything that looks like a valid property header is recursed into,
/// everything else is kept as a <see cref="RawNode"/>.
/// serialize(parse(x)) == x for every object in a retail install.
/// </summary>
public static class PropStream
{
    static long? LooksLikeProp(byte[] data, int pos, int end)
    {
        if (pos + 9 > end) return null;
        if (data[pos + 2] != 0x00 || data[pos + 3] != 0x80) return null;
        long length = Bytes.U32(data, pos + 5);
        if (length > end - (pos + 9)) return null;
        return length;
    }

    public static List<PropNode> Parse(byte[] data, int start = 0, int end = -1)
    {
        if (end < 0) end = data.Length;
        var nodes = new List<PropNode>();
        int pos = start, rawStart = start;
        while (pos < end)
        {
            var length = LooksLikeProp(data, pos, end);
            if (length is null) { pos++; continue; }
            if (pos > rawStart) nodes.Add(new RawNode(Bytes.Slice(data, rawStart, pos), rawStart));
            uint key = Bytes.U32(data, pos);
            byte kind = data[pos + 4];
            int bodyStart = pos + 9;
            int bodyEnd = bodyStart + (int)length.Value;
            nodes.Add(new Prop(key, kind, Parse(data, bodyStart, bodyEnd), pos));
            pos = rawStart = bodyEnd;
        }
        if (end > rawStart) nodes.Add(new RawNode(Bytes.Slice(data, rawStart, end), rawStart));
        return nodes;
    }

    public static byte[] Serialize(IEnumerable<PropNode> nodes) => Bytes.Concat(nodes.Select(n => n.ToBytes()).ToArray());
}

/// <summary>A lowercase game-data chunk: fixed header + property stream.</summary>
public sealed class DataObject
{
    public DataObject(string tag, ObjectHeader header, List<PropNode> nodes) { Tag = tag; Header = header; Nodes = nodes; }

    public string Tag { get; }
    public ObjectHeader Header { get; }
    public List<PropNode> Nodes { get; }

    public static DataObject Parse(string tag, byte[] body) =>
        new(tag, ObjectHeader.Parse(body), PropStream.Parse(body, ObjectHeader.Size));

    public byte[] ToBytes() => Bytes.Concat(Header.ToBytes(), PropStream.Serialize(Nodes));

    public IEnumerable<Prop> Props() => Nodes.OfType<Prop>();
}
