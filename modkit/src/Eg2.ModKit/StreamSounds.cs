using System.Text;
using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Streamed sounds (*.pc.streamsounds: dialogue, music; HANDOFF round 27): "Asura   " "ASTS" [u32 size = file length - 12]
/// [u32 2][u32 0][u32 n][u8 0] then n entries [name, NUL-padded to 4 from its start][u8 flag][u32 size][u32 offset],
/// then the WAVs back to back from the end of the table, then 4 zero bytes. streamingsounds.ssm.pc.streamsounds is a
/// table-only copy of streamingsounds.asr.pc.streamsounds (no data, no tail).
/// </summary>
public static class StreamSounds
{
    public sealed record Entry(string Name, byte Flag, uint Size, uint Offset);

    public static bool IsStore(string path) => path.EndsWith(".pc.streamsounds", StringComparison.OrdinalIgnoreCase)
                                              && !path.EndsWith(".ssm.pc.streamsounds", StringComparison.OrdinalIgnoreCase);

    /// <summary>The table, read from the start of the file only (the stores are up to 440 MB).</summary>
    public static List<Entry> ReadTable(string path)
    {
        using var fs = File.OpenRead(path);
        var head = new byte[29];
        fs.ReadExactly(head);
        // the table is at most a few hundred KB: read generously
        var buf = new byte[Math.Min(fs.Length, 29 + Bytes.U32(head, 24) * 300L + 4096)];
        fs.Position = 0;
        fs.ReadExactly(buf);
        return Parse(buf, path);
    }

    /// <summary>The table of a store (or of its first bytes).</summary>
    public static List<Entry> Parse(byte[] buf, string what = "store")
    {
        if (buf.Length < 29 || Encoding.ASCII.GetString(buf, 0, 12) != "Asura   ASTS") throw new InvalidDataException($"{what}: not a streamed sound store");
        int n = (int)Bytes.U32(buf, 24);
        var list = new List<Entry>(n);
        int p = 29;
        for (int i = 0; i < n; i++)
        {
            int e = Array.IndexOf(buf, (byte)0, p);
            if (e < 0) throw new InvalidDataException($"{what}: table longer than expected");
            int nameLen = (e - p + 1 + 3) & ~3;
            byte flag = buf[p + nameLen];
            list.Add(new Entry(Bytes.Latin1.GetString(buf, p, e - p), flag, Bytes.U32(buf, p + nameLen + 1), Bytes.U32(buf, p + nameLen + 5)));
            p += nameLen + 9;
        }
        return list;
    }

    /// <summary>
    /// Asset entries (Tag "ASTS"): Offset and DataOffset both point at the WAV. 531 names are listed twice in a store,
    /// always with identical data, so each name is listed once and a replacement replaces every copy.
    /// </summary>
    public static IEnumerable<AssetEntry> Assets(string root, string rel) =>
        ReadTable(Path.Combine(root, rel)).DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => new AssetEntry
        {
            File = rel, Tag = "ASTS", Name = e.Name, Kind = "Streamed sound", Offset = e.Offset, DataOffset = e.Offset, Size = e.Size,
        });

    /// <summary>The whole store with some sounds replaced (by name, case-insensitive), streamed from disk entry by entry.</summary>
    public static byte[] Patch(string path, IReadOnlyDictionary<string, byte[]> replaced)
    {
        var table = ReadTable(path);
        using var fs = File.OpenRead(path);
        return Build(table.Select(e =>
        {
            if (replaced.TryGetValue(e.Name, out var wav)) return (e.Name, e.Flag, wav);
            fs.Position = e.Offset;
            var d = new byte[e.Size];
            fs.ReadExactly(d);
            return (e.Name, e.Flag, d);
        }).ToList());
    }

    /// <summary>
    /// The table-only copy (streamingsounds.ssm.pc.streamsounds) of a store: same header and table, byte +28 = 1, then
    /// 4 zero bytes; its offsets point into the store.
    /// </summary>
    public static byte[] TableOnly(byte[] store)
    {
        int n = (int)Bytes.U32(store, 24);
        int end = n == 0 ? 29 : (int)Bytes.U32(store, 29 + TableEntryLength(store, 29) - 4);
        var t = Bytes.Concat(store[..end], new byte[4]);
        Bytes.PutU32(t, 12, (uint)(t.Length - 12));
        t[28] = 1;
        return t;
    }

    static int TableEntryLength(byte[] b, int p) => ((Array.IndexOf(b, (byte)0, p) - p + 1 + 3) & ~3) + 9;

    public static byte[] ReadData(string path, Entry e)
    {
        using var fs = File.OpenRead(path);
        fs.Position = e.Offset;
        var d = new byte[e.Size];
        fs.ReadExactly(d);
        return d;
    }

    /// <summary>A store holding exactly these sounds (name, flag, WAV bytes), in order.</summary>
    public static byte[] Build(IReadOnlyList<(string Name, byte Flag, byte[] Wav)> sounds)
    {
        var table = new MemoryStream();
        foreach (var (name, flag, _) in sounds)
        {
            var nb = Bytes.Latin1.GetBytes(name);
            table.Write(nb);
            table.Write(new byte[((nb.Length + 1 + 3) & ~3) - nb.Length]);
            table.WriteByte(flag);
            table.Write(new byte[8]);   // size, offset: filled below
        }
        var t = table.ToArray();
        long dataAt = 29 + t.Length;
        int p = 0; long at = dataAt;
        foreach (var (name, _, wav) in sounds)
        {
            p += ((Bytes.Latin1.GetByteCount(name) + 1 + 3) & ~3) + 1;
            Bytes.PutU32(t, p, (uint)wav.Length);
            Bytes.PutU32(t, p + 4, (uint)at);
            p += 8; at += wav.Length;
        }
        long total = at + 4;
        var head = Bytes.Concat("Asura   ASTS"u8.ToArray(), Bytes.Le((uint)(total - 12), 2u, 0u, (uint)sounds.Count), new byte[] { 0 });
        return Bytes.Concat(new[] { head, t }.Concat(sounds.Select(s => s.Wav)).Append(new byte[4]).ToArray());
    }
}
