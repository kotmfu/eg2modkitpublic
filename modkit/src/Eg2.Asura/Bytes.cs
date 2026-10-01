using System.Buffers.Binary;
using System.Text;

namespace Eg2.Asura;

/// <summary>Little-endian and byte-sequence helpers mirroring the Python library's idioms.</summary>
public static class Bytes
{
    public static readonly Encoding Latin1 = Encoding.Latin1;

    public static uint U32(byte[] b, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset, 4));

    public static void PutU32(byte[] b, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset, 4), value);

    public static byte[] Le(uint value)
    {
        var b = new byte[4];
        PutU32(b, 0, value);
        return b;
    }

    public static byte[] Le(params uint[] values)
    {
        var b = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) PutU32(b, i * 4, values[i]);
        return b;
    }

    /// <summary>Index of <paramref name="needle"/> lying entirely within [start, end), or -1 (Python bytes.find).</summary>
    public static int Find(byte[] hay, byte[] needle, int start = 0, int end = -1)
    {
        if (end < 0 || end > hay.Length) end = hay.Length;
        if (start < 0) start = 0;
        if (start > end) return -1;
        int i = hay.AsSpan(start, end - start).IndexOf(needle);
        return i < 0 ? -1 : i + start;
    }

    public static bool Contains(byte[] hay, byte[] needle) => hay.AsSpan().IndexOf(needle) >= 0;

    /// <summary>Non-overlapping occurrence count (Python bytes.count).</summary>
    public static int Count(byte[] hay, byte[] needle)
    {
        int n = 0, pos = 0;
        while (true)
        {
            int i = Find(hay, needle, pos);
            if (i < 0) return n;
            n++;
            pos = i + needle.Length;
        }
    }

    /// <summary>Non-overlapping left-to-right replace (Python bytes.replace).</summary>
    public static byte[] Replace(byte[] hay, byte[] oldValue, byte[] newValue)
    {
        using var ms = new MemoryStream(hay.Length);
        int pos = 0;
        while (true)
        {
            int i = Find(hay, oldValue, pos);
            if (i < 0) break;
            ms.Write(hay, pos, i - pos);
            ms.Write(newValue, 0, newValue.Length);
            pos = i + oldValue.Length;
        }
        ms.Write(hay, pos, hay.Length - pos);
        return ms.ToArray();
    }

    public static byte[] Concat(params byte[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var o = new byte[n];
        int at = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, o, at, p.Length); at += p.Length; }
        return o;
    }

    public static byte[] Slice(byte[] b, int start, int end) => b.AsSpan(start, end - start).ToArray();

    public static bool Equal(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

    /// <summary>Round <paramref name="n"/> up to a multiple of 4.</summary>
    public static int Pad4(int n) => (n + 3) & ~3;

    public static string Hex(uint v) => "0x" + v.ToString("x8");
}
