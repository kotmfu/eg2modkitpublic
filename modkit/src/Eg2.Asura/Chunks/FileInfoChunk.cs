namespace Eg2.Asura.Chunks;

/// <summary>FNFO: u32 version (1), u32 flags (0/4/11/15 -- not a count), u32 payload_size (= len(payload)-4), u32 alignment (8). Always first.</summary>
public sealed class FileInfoChunk
{
    public uint Version = 1, Flags, PayloadSize, Alignment = 8;

    public static FileInfoChunk Parse(byte[] body) => new()
    {
        Version = Bytes.U32(body, 0), Flags = Bytes.U32(body, 4), PayloadSize = Bytes.U32(body, 8), Alignment = Bytes.U32(body, 12),
    };

    public byte[] ToBytes() => Bytes.Le(Version, Flags, PayloadSize, Alignment);
}
