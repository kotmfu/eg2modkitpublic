using System.IO.Compression;
using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// A game save (%LOCALAPPDATA%\Evil Genius 2\PC_ProfileSaves\&lt;steam id&gt;\slotN.sav; slot0 is the profile: an
/// uncompressed 8 KB prop block zero-padded to 2 MB). Game saves: "Asura   " "AsuraZlb" [u32 compressed length]
/// [u32 decompressed length - 16] + zlib stream = an ordinary Asura archive: bsnf, DYMG, DLIG, SMXG, ATIG, ARNM (world
/// map), dtvs, stsy, ttsy and ~500 ENTI (entities) — all prop streams of hashes and numbers, no names (HANDOFF round 27).
/// </summary>
public static class SaveFile
{
    static readonly byte[] Magic = "Asura   AsuraZlb"u8.ToArray();

    public static bool IsCompressed(byte[] file) => file.Length > 24 && file.AsSpan(0, 16).SequenceEqual(Magic);

    public static AsuraArchive Load(byte[] file)
    {
        if (!IsCompressed(file)) throw new InvalidDataException("not a compressed game save (slot0 is the profile)");
        int comp = (int)Bytes.U32(file, 16);
        using var z = new ZLibStream(new MemoryStream(file, 24, comp), CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        return AsuraArchive.FromBytes(raw.ToArray());
    }

    public static byte[] Save(AsuraArchive archive)
    {
        var raw = archive.ToBytes();
        var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
        return Bytes.Concat(Magic, Bytes.Le((uint)comp.Length, (uint)(raw.Length - 16)), comp.ToArray());
    }
}
