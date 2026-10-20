using System.Globalization;
using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Class-tree (BLUE) objects in misc\common.asr: [1][0][1][root][u32 count] + objects [id][0x0d][id][parent][u32 n] + n
/// members [key][flag][type][0][kind] + value (kind 0 u32, 1 f32, 3 hash: 4 bytes; 2: 1 byte; 4: [u32 len] + chars).
/// A member missing from an object is inherited from its parent. The file is memory-mapped, so values change in place only.
/// </summary>
public static class Blueprints
{
    public sealed record Member(uint Key, uint Kind, int At);
    public sealed record Obj(uint Id, uint Parent, List<Member> Members);

    /// <summary>The chunk's objects, or null when it isn't a class tree of this layout.</summary>
    public static List<Obj>? Parse(byte[] blue)
    {
        if (blue.Length < 20 || Bytes.U32(blue, 0) != 1) return null;
        var objs = new List<Obj>();
        try
        {
            for (int i = 0, n = (int)Bytes.U32(blue, 16), p = 20; i < n; i++)
            {
                if (Bytes.U32(blue, p + 4) != 0x0d) return null;
                var o = new Obj(Bytes.U32(blue, p), Bytes.U32(blue, p + 12), new List<Member>());
                int m = (int)Bytes.U32(blue, p + 16);
                p += 20;
                for (int k = 0; k < m; k++)
                {
                    uint kind = Bytes.U32(blue, p + 16);
                    o.Members.Add(new Member(Bytes.U32(blue, p), kind, p + 20));
                    p += 20 + kind switch { 0 or 1 or 3 => 4, 2 => 1, 4 => 4 + (int)Bytes.U32(blue, p + 20), _ => throw new InvalidDataException() };
                }
                objs.Add(o);
            }
        }
        catch (Exception x) when (x is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException) { return null; }
        return objs;
    }

    /// <summary>A member's value as text: u32 decimal, f32, hash as 0x…, byte, or the string.</summary>
    public static string Read(byte[] blue, Member m) => m.Kind switch
    {
        0 => Bytes.U32(blue, m.At).ToString(CultureInfo.InvariantCulture),
        1 => BitConverter.ToSingle(blue, m.At).ToString(CultureInfo.InvariantCulture),
        3 => Bytes.Hex(Bytes.U32(blue, m.At)),
        2 => blue[m.At].ToString(CultureInfo.InvariantCulture),
        _ => System.Text.Encoding.ASCII.GetString(blue, m.At + 4, (int)Bytes.U32(blue, m.At)),
    };

    /// <summary>
    /// Sets object <paramref name="obj"/>'s own member <paramref name="key"/> to <paramref name="value"/> in place.
    /// Returns the old value, or an error starting "!" (no such object or member here, a string member, a bad number).
    /// </summary>
    public static string Set(byte[] blue, uint obj, uint key, string value)
    {
        var objs = Parse(blue);
        if (objs?.FirstOrDefault(o => o.Id == obj) is not { } o) return "!no such object";
        if (o.Members.FirstOrDefault(x => x.Key == key) is not { } m) return "!the object doesn't set this member itself (inherited values can't be changed here)";
        string old = Read(blue, m);
        var inv = CultureInfo.InvariantCulture;
        switch (m.Kind)
        {
            case 0 when uint.TryParse(value, NumberStyles.Integer, inv, out var u): Bytes.PutU32(blue, m.At, u); break;
            case 1 when float.TryParse(value, NumberStyles.Float, inv, out var f): BitConverter.GetBytes(f).CopyTo(blue, m.At); break;
            case 2 when byte.TryParse(value, NumberStyles.Integer, inv, out var b): blue[m.At] = b; break;
            case 3:
                try { Bytes.PutU32(blue, m.At, Convert.ToUInt32(value, 16)); break; }
                catch (Exception x) when (x is FormatException or OverflowException or ArgumentException) { return $"!{value}: not a hex id"; }
            case 4: return "!string members can't be changed (they'd change size)";
            default: return $"!{value}: not a {(m.Kind switch { 0 => "whole number", 1 => "number", _ => "byte (0-255)" })}";
        }
        return old;
    }
}
