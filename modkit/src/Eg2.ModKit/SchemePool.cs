using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// A scheme pool (rspl, 288): the schemes a region rule (rrtl), objective step (room) or level list (rdfl) offers.
/// After the 24-byte object header: [u32 0x80000002][u8 0][u32 size = 4 + 8n + 4][u32 n][n x (u32 rscm, u32 weight)]
/// [u32 ?]. Weights are 1 in every pool seen; the trailing u32 (0, 4, 25...) isn't understood and is kept.
/// </summary>
public sealed class SchemePool
{
    const int Key = ObjectHeader.Size, SizeAt = Key + 5, CountAt = Key + 9, EntriesAt = Key + 13;
    const uint PropKey = 0x80000002;

    public List<(uint Scheme, uint Weight)> Entries { get; } = new();
    byte[] _tail = Array.Empty<byte>();
    byte[] _head = Array.Empty<byte>();

    public static SchemePool? TryParse(string tag, byte[] body)
    {
        if (tag != "rspl" || body.Length < EntriesAt + 4 || Bytes.U32(body, Key) != PropKey) return null;
        uint n = Bytes.U32(body, CountAt);
        if (n > 10_000 || Bytes.U32(body, SizeAt) != 8 + 8 * n || body.Length != EntriesAt + 8 * n + 4) return null;
        var p = new SchemePool { _head = body[..EntriesAt], _tail = body[(EntriesAt + 8 * (int)n)..] };
        for (int i = 0; i < n; i++) p.Entries.Add((Bytes.U32(body, EntriesAt + 8 * i), Bytes.U32(body, EntriesAt + 8 * i + 4)));
        return p;
    }

    public byte[] ToBytes()
    {
        var b = new byte[EntriesAt + 8 * Entries.Count + _tail.Length];
        _head.CopyTo(b, 0);
        BitConverter.TryWriteBytes(b.AsSpan(SizeAt), (uint)(8 + 8 * Entries.Count));
        BitConverter.TryWriteBytes(b.AsSpan(CountAt), (uint)Entries.Count);
        for (int i = 0; i < Entries.Count; i++)
        {
            BitConverter.TryWriteBytes(b.AsSpan(EntriesAt + 8 * i), Entries[i].Scheme);
            BitConverter.TryWriteBytes(b.AsSpan(EntriesAt + 8 * i + 4), Entries[i].Weight);
        }
        _tail.CopyTo(b, EntriesAt + 8 * Entries.Count);
        return b;
    }
}
