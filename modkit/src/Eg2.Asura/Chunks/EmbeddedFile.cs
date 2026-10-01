namespace Eg2.Asura.Chunks;

/// <summary>
/// RSCF: embedded file. u32 f0, f1, version, flags, size; path\0; filler (not always zero); data[size] at the end.
/// Textures are DDS even when the stored path ends in .tga.
/// </summary>
public sealed class EmbeddedFile
{
    public uint F0, F1, Version, Flags;
    public string Path = "";
    public byte[] Data = Array.Empty<byte>();
    public byte[] Filler = Array.Empty<byte>();

    public static EmbeddedFile Parse(byte[] body)
    {
        uint size = Bytes.U32(body, 16);
        int end = Array.IndexOf(body, (byte)0, 20);
        int dataStart = body.Length - (int)size;
        if (dataStart < end + 1) throw new AsuraFormatException("RSCF: declared size overruns the chunk");
        return new EmbeddedFile
        {
            F0 = Bytes.U32(body, 0), F1 = Bytes.U32(body, 4), Version = Bytes.U32(body, 8), Flags = Bytes.U32(body, 12),
            Path = Bytes.Latin1.GetString(body, 20, end - 20),
            Filler = Bytes.Slice(body, end + 1, dataStart),
            Data = Bytes.Slice(body, dataStart, body.Length),
        };
    }

    public bool IsDds => Data.Length >= 4 && Data[0] == (byte)'D' && Data[1] == (byte)'D' && Data[2] == (byte)'S' && Data[3] == (byte)' ';

    public byte[] ToBytes() => Bytes.Concat(
        Bytes.Le(F0, F1, Version, Flags, (uint)Data.Length),
        Bytes.Latin1.GetBytes(Path), new byte[] { 0 }, Filler, Data);
}
