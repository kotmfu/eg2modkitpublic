namespace Eg2.Asura.Chunks;

public sealed class ResourceEntry
{
    public string Name = "";
    public uint Hash, Size, Count;
}

/// <summary>
/// RSFL: editor/source manifest. u32 version, u32 pad, u32 count, then per entry
/// name\0 NUL-padded to a 4-byte boundary (relative to the body) + u32 hash, size, count.
/// In a package manifest file the single entry's Size equals the rpkg chunk size.
/// </summary>
public sealed class ResourceList
{
    public uint Version = 1, Pad;
    public List<ResourceEntry> Entries = new();

    public static ResourceList Parse(byte[] body)
    {
        var r = new ResourceList { Version = Bytes.U32(body, 0), Pad = Bytes.U32(body, 4) };
        uint count = Bytes.U32(body, 8);
        int off = 12;
        for (uint i = 0; i < count; i++)
        {
            int end = Array.IndexOf(body, (byte)0, off);
            var name = Bytes.Latin1.GetString(body, off, end - off);
            off = end + 1;
            off = Bytes.Pad4(off);
            r.Entries.Add(new ResourceEntry
            {
                Name = name, Hash = Bytes.U32(body, off), Size = Bytes.U32(body, off + 4), Count = Bytes.U32(body, off + 8),
            });
            off += 12;
        }
        return r;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        ms.Write(Bytes.Le(Version, Pad, (uint)Entries.Count));
        foreach (var e in Entries)
        {
            ms.Write(Bytes.Latin1.GetBytes(e.Name));
            ms.WriteByte(0);
            while (ms.Length % 4 != 0) ms.WriteByte(0);
            ms.Write(Bytes.Le(e.Hash, e.Size, e.Count));
        }
        return ms.ToArray();
    }
}
