using System.Text;

namespace Eg2.Asura.Chunks;

public sealed class TextEntry
{
    public TextEntry(uint hash, byte[] raw, string? key = null) { Hash = hash; Raw = raw; Key = key; }

    public uint Hash { get; set; }
    /// <summary>UTF-16LE including terminator(s), kept verbatim.</summary>
    public byte[] Raw { get; set; }
    public string? Key { get; set; }

    public string Text
    {
        get => Encoding.Unicode.GetString(Raw).TrimEnd('\0');
        set => Raw = Encoding.Unicode.GetBytes(value + "\0");
    }

    public int Units => Raw.Length / 2;
}

/// <summary>
/// HTXT localisation table (text/pc/&lt;t&gt;/&lt;t&gt;.asr_&lt;lang&gt;).
///   u32 version (4), u32 pad, u32 count, u32 name_hash (= KeyHash(table name)), u32 text_bytes, u32 pad2
///   count x { u32 hash, u32 units (incl. NUL), u16 text[units] }
///   key table: name\0 NUL-padded to a multiple of 4, u32 size, keys\0... (same order as entries)
/// Entry hash = KeyHash(key); inventing a hash is not safe. Entries are not sorted.
/// </summary>
public sealed class TextTable
{
    public uint Version = 4, Pad, NameHash, TextBytes, Pad2;
    public List<TextEntry> Entries = new();
    public string TableName = "";
    public byte[] KeysBlob = Array.Empty<byte>();

    /// <summary>h = h*31 + c over the lower-cased ASCII key (matches 53,819/53,820 shipped entries).</summary>
    public static uint KeyHash(string key)
    {
        uint h = 0;
        foreach (byte c in Encoding.ASCII.GetBytes(key.ToLowerInvariant()))
            h = unchecked(h * 31 + c);
        return h;
    }

    public static TextTable Parse(byte[] body)
    {
        var t = new TextTable
        {
            Version = Bytes.U32(body, 0), Pad = Bytes.U32(body, 4), NameHash = Bytes.U32(body, 12),
            TextBytes = Bytes.U32(body, 16), Pad2 = Bytes.U32(body, 20),
        };
        uint count = Bytes.U32(body, 8);
        int off = 24;
        for (uint i = 0; i < count; i++)
        {
            uint h = Bytes.U32(body, off);
            int units = (int)Bytes.U32(body, off + 4);
            off += 8;
            t.Entries.Add(new TextEntry(h, Bytes.Slice(body, off, off + units * 2)));
            off += units * 2;
        }
        if (off + 12 <= body.Length)
        {
            int end = Array.IndexOf(body, (byte)0, off);
            t.TableName = Bytes.Latin1.GetString(body, off, end - off);
            off += Bytes.Pad4(end + 1 - off);          // the name field is padded to a multiple of 4
            int size = (int)Bytes.U32(body, off);
            t.KeysBlob = Bytes.Slice(body, off + 4, off + 4 + size);
            var keys = Bytes.Latin1.GetString(t.KeysBlob).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (keys.Length == t.Entries.Count)
                for (int i = 0; i < keys.Length; i++) t.Entries[i].Key = keys[i];
        }
        return t;
    }

    public byte[] ToBytes()
    {
        uint nameHash = TableName.Length > 0 ? KeyHash(TableName) : NameHash;
        int total = Entries.Sum(e => e.Raw.Length);
        using var ms = new MemoryStream();
        ms.Write(Bytes.Le(Version, Pad, (uint)Entries.Count, nameHash, (uint)total, Pad2));
        foreach (var e in Entries)
        {
            ms.Write(Bytes.Le(e.Hash, (uint)e.Units));
            ms.Write(e.Raw);
        }
        if (KeysBlob.Length > 0 || TableName.Length > 0)
        {
            var name = Bytes.Latin1.GetBytes(TableName);
            ms.Write(name);
            int fieldLen = Bytes.Pad4(name.Length + 1);
            ms.Write(new byte[fieldLen - name.Length]);
            ms.Write(Bytes.Le((uint)KeysBlob.Length));
            ms.Write(KeysBlob);
        }
        return ms.ToArray();
    }

    public TextEntry? Get(string key) => Entries.FirstOrDefault(e => e.Key == key);

    /// <summary>Append a new entry, hashing the key the way the engine does. Refuses duplicates and hash collisions.</summary>
    public TextEntry Add(string key, string text)
    {
        if (Get(key) is not null) throw new InvalidOperationException($"{key} already present");
        uint h = KeyHash(key);
        if (Entries.Any(e => e.Hash == h)) throw new InvalidOperationException($"hash {Bytes.Hex(h)} of {key} collides with an existing entry");
        var e = new TextEntry(h, Array.Empty<byte>(), key) { Text = text };
        Entries.Add(e);
        KeysBlob = Bytes.Concat(KeysBlob, Bytes.Latin1.GetBytes(key), new byte[] { 0 });
        return e;
    }

    /// <summary>Set an existing key's text, or add it.</summary>
    public void Set(string key, string text)
    {
        var e = Get(key);
        if (e is null) Add(key, text);
        else e.Text = text;
    }
}
