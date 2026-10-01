using System.IO.Compression;
using System.Text;

namespace Eg2.Asura;

public sealed class AsuraFormatException : Exception
{
    public AsuraFormatException(string message) : base(message) { }
}

/// <summary>One tagged record inside an archive. <see cref="Body"/> excludes the 8-byte tag/size header.</summary>
public sealed class Chunk
{
    public Chunk(string tag, byte[] body, int offset = -1)
    {
        if (tag.Length != 4) throw new ArgumentException($"chunk tag must be 4 chars, got '{tag}'");
        Tag = tag;
        Body = body;
        Offset = offset;
    }

    public string Tag { get; }
    public byte[] Body { get; set; }
    /// <summary>Where the chunk started in the decompressed payload (informational).</summary>
    public int Offset { get; }
    public int Size => Body.Length + 8;
    public bool IsDataObjectTag => Tag.Length == 4 && Tag.All(char.IsLower);

    public byte[] ToBytes() => Bytes.Concat(Bytes.Latin1.GetBytes(Tag), Bytes.Le((uint)Size), Body);

    public override string ToString() => $"{Tag} size={Size}";
}

/// <summary>
/// An Asura container (.asr and friends).
///
/// Compressed form "AsuraZbb": u32 comp_total (filesize-16), u32 raw_total,
/// u32 comp_0, u32 raw_0, zlib block 0, then { u32 comp_n, u32 raw_n, zlib }*
/// with 2 MiB of payload per block. Uncompressed form is the payload itself:
/// "Asura   " + { tag[4], u32 size (incl. header), body }* + zero terminator.
/// </summary>
public sealed class AsuraArchive
{
    public const string MagicCompressed = "AsuraZbb";
    public const string MagicRaw = "Asura   ";
    public const int BlockSize = 2 * 1024 * 1024;
    static readonly byte[] MagicCompressedBytes = Encoding.ASCII.GetBytes(MagicCompressed);
    static readonly byte[] MagicRawBytes = Encoding.ASCII.GetBytes(MagicRaw);

    public List<Chunk> Chunks { get; set; } = new();
    public bool Compressed { get; set; } = true;
    public byte[] Trailer { get; set; } = new byte[4];
    public string? Path { get; set; }

    public static AsuraArchive Load(string path)
    {
        var a = FromBytes(File.ReadAllBytes(path));
        a.Path = path;
        return a;
    }

    public static AsuraArchive FromBytes(byte[] data)
    {
        var payload = Decompress(data, out bool compressed);
        var (chunks, trailer) = ParseChunks(payload);
        return new AsuraArchive { Chunks = chunks, Compressed = compressed, Trailer = trailer };
    }

    /// <summary>True if the file starts with an Asura magic.</summary>
    public static bool Sniff(string path)
    {
        using var fs = File.OpenRead(path);
        var head = new byte[8];
        if (fs.Read(head, 0, 8) != 8) return false;
        return head.AsSpan().SequenceEqual(MagicCompressedBytes) || head.AsSpan().SequenceEqual(MagicRawBytes);
    }

    public IEnumerable<Chunk> Find(string tag) => Chunks.Where(c => c.Tag == tag);
    public Chunk? First(string tag) => Chunks.FirstOrDefault(c => c.Tag == tag);

    /// <summary>The decompressed payload, with FNFO.payload_size kept in sync (= len(payload) - 4).</summary>
    public byte[] Payload()
    {
        // one exact-size array: a MemoryStream would double and copy a ~1 GB payload several times
        byte[] Build()
        {
            var raw = new byte[MagicRawBytes.Length + Chunks.Sum(c => 8L + c.Body.Length) + Trailer.Length];
            int pos = 0;
            void Put(ReadOnlySpan<byte> b) { b.CopyTo(raw.AsSpan(pos)); pos += b.Length; }
            Put(MagicRawBytes);
            foreach (var c in Chunks)
            {
                Bytes.Latin1.GetBytes(c.Tag, raw.AsSpan(pos));
                Bytes.PutU32(raw, pos + 4, (uint)c.Size);
                pos += 8;
                Put(c.Body);
            }
            Put(Trailer);
            return raw;
        }

        var raw = Build();
        var first = Chunks.Count > 0 ? Chunks[0] : null;
        if (first is not null && first.Tag == "FNFO" && first.Body.Length >= 12)
        {
            uint want = (uint)(raw.Length - 4);
            if (Bytes.U32(first.Body, 8) != want)
            {
                var body = (byte[])first.Body.Clone();
                Bytes.PutU32(body, 8, want);
                first.Body = body;
                raw = Build();
            }
        }
        return raw;
    }

    public byte[] ToBytes(bool? compressed = null)
    {
        var raw = Payload();
        return (compressed ?? Compressed) ? Compress(raw) : raw;
    }

    public void Save(string path, bool? compressed = null) => File.WriteAllBytes(path, ToBytes(compressed));

    // ------------------------------------------------------------------ codec

    public static byte[] Decompress(byte[] data, out bool compressed)
    {
        var magic = data.AsSpan(0, Math.Min(8, data.Length));
        if (magic.SequenceEqual(MagicRawBytes)) { compressed = false; return data; }
        if (!magic.SequenceEqual(MagicCompressedBytes))
            throw new AsuraFormatException("not an Asura container");
        compressed = true;

        uint compTotal = Bytes.U32(data, 8), rawTotal = Bytes.U32(data, 12);
        uint comp0 = Bytes.U32(data, 16), raw0 = Bytes.U32(data, 20);
        if (compTotal - 8 != (uint)(data.Length - 24))
            throw new AsuraFormatException($"header says {compTotal - 8} compressed bytes, file has {data.Length - 24}");

        var payload = new byte[rawTotal];
        int outPos = 0;
        int off = 24;
        uint compN = comp0, rawN = raw0;
        while (true)
        {
            if (outPos + rawN > rawTotal) throw new AsuraFormatException("blocks exceed the declared payload size");
            using (var src = new MemoryStream(data, off, (int)compN, writable: false))
            using (var z = new ZLibStream(src, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < rawN)
                {
                    int r = z.Read(payload, outPos + got, (int)rawN - got);
                    if (r == 0) break;
                    got += r;
                }
                if (got != rawN) throw new AsuraFormatException($"block at 0x{off:x}: got {got} bytes, header said {rawN}");
            }
            outPos += (int)rawN;
            off += (int)compN;
            if (off >= data.Length) break;
            compN = Bytes.U32(data, off);
            rawN = Bytes.U32(data, off + 4);
            off += 8;
        }
        if (outPos != rawTotal) throw new AsuraFormatException($"decompressed {outPos} bytes, header said {rawTotal}");
        if (!payload.AsSpan(0, 8).SequenceEqual(MagicRawBytes)) throw new AsuraFormatException("decompressed payload has a bad magic");
        return payload;
    }

    /// <summary>
    /// The blocks of <paramref name="original"/> (an AsuraZbb file) whose content differs in <paramref name="payload"/>
    /// (same length), re-compressed and padded with zeros to their original compressed size: (file offset of the block's
    /// data, bytes to put there). Every other byte of the file stays as it is. Null when a changed block won't fit.
    /// </summary>
    public static List<(long Offset, byte[] Data)>? ChangedBlocks(byte[] original, byte[] payload)
    {
        if (!original.AsSpan(0, 8).SequenceEqual(MagicCompressedBytes) || Bytes.U32(original, 12) != payload.Length) return null;
        var changed = new List<(long, byte[])>();
        int off = 16, rawAt = 0;
        while (off + 8 <= original.Length)
        {
            int comp = (int)Bytes.U32(original, off), raw = (int)Bytes.U32(original, off + 4), data = off + 8;
            var old = new byte[raw];
            using (var z = new ZLibStream(new MemoryStream(original, data, comp, writable: false), CompressionMode.Decompress))
            {
                int got = 0;
                while (got < raw) { int r = z.Read(old, got, raw - got); if (r == 0) break; got += r; }
            }
            if (!payload.AsSpan(rawAt, raw).SequenceEqual(old))
            {
                var fresh = Zlib4k.CompressSmall(payload[rawAt..(rawAt + raw)]);
                if (fresh.Length > comp) return null;
                var padded = new byte[comp];
                fresh.CopyTo(padded, 0);
                changed.Add((data, padded));
            }
            rawAt += raw;
            off = data + comp;
        }
        return rawAt == payload.Length ? changed : null;
    }

    /// <summary>Wrap a payload into a block-compressed AsuraZbb using the 4 KiB-window encoder.</summary>
    public static byte[] Compress(byte[] payload, int blockSize = BlockSize)
    {
        int count = Math.Max(1, (payload.Length + blockSize - 1) / blockSize);
        var blocks = new (byte[] stream, int rawLen)[count];
        Parallel.For(0, count, i =>
        {
            int start = i * blockSize;
            var raw = Bytes.Slice(payload, start, Math.Min(payload.Length, start + blockSize));
            blocks[i] = (Zlib4k.Compress(raw), raw.Length);
        });
        long bodyLen = blocks.Sum(x => (long)x.stream.Length) + 8L * (count - 1);
        var outp = new byte[24 + bodyLen];
        MagicCompressedBytes.CopyTo(outp, 0);
        Bytes.Le((uint)(bodyLen + 8), (uint)payload.Length, (uint)blocks[0].stream.Length, (uint)blocks[0].rawLen).CopyTo(outp, 8);
        int pos = 24;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (i > 0) { Bytes.PutU32(outp, pos, (uint)blocks[i].stream.Length); Bytes.PutU32(outp, pos + 4, (uint)blocks[i].rawLen); pos += 8; }
            blocks[i].stream.CopyTo(outp, pos);
            pos += blocks[i].stream.Length;
        }
        return outp;
    }

    public static (List<Chunk> chunks, byte[] trailer) ParseChunks(byte[] payload)
    {
        if (!payload.AsSpan(0, 8).SequenceEqual(MagicRawBytes))
            throw new AsuraFormatException("payload magic is not 'Asura   '");
        var chunks = new List<Chunk>();
        int off = 8, end = payload.Length;
        while (off + 8 <= end)
        {
            string tag = Bytes.Latin1.GetString(payload, off, 4);
            uint size = Bytes.U32(payload, off + 4);
            if (tag == "\0\0\0\0" || size == 0) break;
            if (size < 8 || off + size > end)
                throw new AsuraFormatException($"chunk '{tag}' at 0x{off:x} has bad size {size} ({end - off} bytes left)");
            chunks.Add(new Chunk(tag, Bytes.Slice(payload, off + 8, off + (int)size), off));
            off += (int)size;
        }
        var trailer = Bytes.Slice(payload, off, end);
        if (trailer.Any(b => b != 0)) throw new AsuraFormatException("non-zero trailer after last chunk");
        return (chunks, trailer);
    }
}
