namespace Eg2.ModKit;

/// <summary>
/// The game's DDS textures (HANDOFF round 33). Models: BC7 sRGB colour maps (DXGI 99), BC4 ("ATI1") masks, BC5 ("ATI2")
/// normal maps, all with full mip chains; GUI: BC7 (98) without mips. <see cref="Decode"/> reads the top level of any
/// of those (plus BC1/BC3); <see cref="EncodeLike"/> writes new pixels in an original's format, size and mip count,
/// keeping its header, so the result is byte-for-byte the original's length.
/// </summary>
public static class Dds
{
    public enum Format { Unknown, Bc1, Bc3, Bc4, Bc5, Bc7, Bc7Srgb }

    public static Format FormatOf(byte[] dds)
    {
        if (dds.Length < 128 || !dds.AsSpan().StartsWith("DDS "u8)) return Format.Unknown;
        string cc = System.Text.Encoding.ASCII.GetString(dds, 84, 4);
        return cc switch
        {
            "DXT1" => Format.Bc1, "DXT5" => Format.Bc3, "ATI1" or "BC4U" => Format.Bc4, "ATI2" or "BC5U" => Format.Bc5,
            "DX10" when dds.Length >= 148 => BitConverter.ToInt32(dds, 128) switch
            {
                71 => Format.Bc1, 77 => Format.Bc3, 80 => Format.Bc4, 83 => Format.Bc5, 98 => Format.Bc7, 99 => Format.Bc7Srgb, _ => Format.Unknown,
            },
            _ => Format.Unknown,
        };
    }

    static int HeaderSize(byte[] dds) => System.Text.Encoding.ASCII.GetString(dds, 84, 4) == "DX10" ? 148 : 128;
    static int BlockBytes(Format f) => f is Format.Bc1 or Format.Bc4 ? 8 : 16;
    public static int Mips(byte[] dds) => Math.Max(1, BitConverter.ToInt32(dds, 28));

    /// <summary>Top level as RGBA (BC4 comes back grey, BC5 as red/green with blue 0), or null for formats ModKit can't read.</summary>
    public static byte[]? Decode(byte[] dds, out int width, out int height)
    {
        height = BitConverter.ToInt32(dds, 12); width = BitConverter.ToInt32(dds, 16);
        var f = FormatOf(dds);
        BlockDecoder? dec = f switch
        {
            Format.Bc1 => (b, p) => Bc1(b, p, true), Format.Bc3 => Bc3, Format.Bc4 => Bc4Grey, Format.Bc5 => Bc5,
            Format.Bc7 or Format.Bc7Srgb => Bc7.DecodeBlock, _ => null,
        };
        return dec is null ? null : DecodeLevel(dds.AsSpan(HeaderSize(dds)), width, height, dec, BlockBytes(f));
    }

    public delegate void BlockDecoder(ReadOnlySpan<byte> block, Span<byte> px);

    internal static byte[] DecodeLevel(ReadOnlySpan<byte> data, int width, int height, BlockDecoder dec, int blockBytes)
    {
        int bw = (width + 3) / 4, bh = (height + 3) / 4;
        var rgba = new byte[width * height * 4];
        Span<byte> px = stackalloc byte[64];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                dec(data.Slice((by * bw + bx) * blockBytes, blockBytes), px);
                for (int i = 0; i < 16; i++)
                {
                    int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                    if (x < width && y < height) px.Slice(i * 4, 4).CopyTo(rgba.AsSpan((y * width + x) * 4));
                }
            }
        return rgba;
    }

    // ---- block decoders ----
    static void Bc1(ReadOnlySpan<byte> b, Span<byte> px, bool allowAlpha)
    {
        int c0 = b[0] | b[1] << 8, c1 = b[2] | b[3] << 8;
        var pal = new int[16];
        void Rgb(int c, int at) { pal[at] = (c >> 11) * 255 / 31; pal[at + 1] = (c >> 5 & 63) * 255 / 63; pal[at + 2] = (c & 31) * 255 / 31; pal[at + 3] = 255; }
        Rgb(c0, 0); Rgb(c1, 4);
        bool four = c0 > c1 || !allowAlpha;
        for (int c = 0; c < 3; c++)
        {
            pal[8 + c] = four ? (2 * pal[c] + pal[4 + c]) / 3 : (pal[c] + pal[4 + c]) / 2;
            pal[12 + c] = four ? (pal[c] + 2 * pal[4 + c]) / 3 : 0;
        }
        pal[11] = 255; pal[15] = four ? 255 : 0;
        uint idx = BitConverter.ToUInt32(b[4..]);
        for (int i = 0; i < 16; i++) { int k = (int)(idx >> 2 * i & 3); for (int c = 0; c < 4; c++) px[i * 4 + c] = (byte)pal[k * 4 + c]; }
    }

    /// <summary>BC4 block (8 bytes) to 16 values.</summary>
    static void Bc4(ReadOnlySpan<byte> b, Span<byte> v)
    {
        int e0 = b[0], e1 = b[1];
        Span<int> pal = stackalloc int[8];
        pal[0] = e0; pal[1] = e1;
        if (e0 > e1) for (int i = 1; i < 7; i++) pal[i + 1] = ((7 - i) * e0 + i * e1) / 7;
        else { for (int i = 1; i < 5; i++) pal[i + 1] = ((5 - i) * e0 + i * e1) / 5; pal[6] = 0; pal[7] = 255; }
        ulong idx = 0;
        for (int i = 0; i < 6; i++) idx |= (ulong)b[2 + i] << 8 * i;
        for (int i = 0; i < 16; i++) v[i] = (byte)pal[(int)(idx >> 3 * i & 7)];
    }

    static void Bc3(ReadOnlySpan<byte> b, Span<byte> px)
    {
        Bc1(b[8..], px, false);
        Span<byte> a = stackalloc byte[16];
        Bc4(b, a);
        for (int i = 0; i < 16; i++) px[i * 4 + 3] = a[i];
    }

    static void Bc4Grey(ReadOnlySpan<byte> b, Span<byte> px)
    {
        Span<byte> r = stackalloc byte[16];
        Bc4(b, r);
        for (int i = 0; i < 16; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = r[i]; px[i * 4 + 3] = 255; }
    }

    static void Bc5(ReadOnlySpan<byte> b, Span<byte> px)
    {
        Span<byte> r = stackalloc byte[16], g = stackalloc byte[16];
        Bc4(b, r); Bc4(b[8..], g);
        for (int i = 0; i < 16; i++) { px[i * 4] = r[i]; px[i * 4 + 1] = g[i]; px[i * 4 + 2] = 0; px[i * 4 + 3] = 255; }
    }

    // ---- encoding ----
    /// <summary>BC4 block from 16 values: 8-step mode between the block's min and max.</summary>
    static void EncodeBc4(ReadOnlySpan<byte> v, Span<byte> dst)
    {
        int lo = 255, hi = 0;
        for (int i = 0; i < 16; i++) { lo = Math.Min(lo, v[i]); hi = Math.Max(hi, v[i]); }
        dst[0] = (byte)hi; dst[1] = (byte)lo;
        ulong idx = 0;
        if (hi > lo)
            for (int i = 0; i < 16; i++)
            {
                // position 0 = hi .. 7 = lo along the ramp, then to the BC4 index order (0, 2, 3, 4, 5, 6, 7, 1)
                int t = (int)Math.Round((hi - v[i]) * 7.0 / (hi - lo));
                ulong k = t == 0 ? 0u : t == 7 ? 1u : (ulong)(t + 1);
                idx |= k << 3 * i;
            }
        for (int i = 0; i < 6; i++) dst[2 + i] = (byte)(idx >> 8 * i);
    }

    /// <summary>
    /// New pixels (RGBA, the original's top-level size) encoded the way <paramref name="original"/> is: same format,
    /// mip count and header. Mips are box-filtered (in linear light for sRGB; normal maps are renormalised).
    /// </summary>
    public static byte[] EncodeLike(byte[] original, byte[] rgba, int width, int height)
    {
        var f = FormatOf(original);
        if (f is not (Format.Bc4 or Format.Bc5 or Format.Bc7 or Format.Bc7Srgb)) throw new NotSupportedException($"textures in {f} format can't be written yet");
        int h0 = BitConverter.ToInt32(original, 12), w0 = BitConverter.ToInt32(original, 16);
        if (w0 != width || h0 != height) throw new ArgumentException($"the picture is {width}x{height}; the game's is {w0}x{h0}");
        int header = HeaderSize(original), mips = Mips(original), bb = BlockBytes(f);
        var outp = new List<byte[]> { original[..header] };
        var level = rgba; int w = width, h = height;
        for (int m = 0; m < mips; m++)
        {
            outp.Add(EncodeLevel(level, w, h, f, bb));
            if (m + 1 < mips) (level, w, h) = Half(level, w, h, f);
        }
        var result = outp.SelectMany(x => x).ToArray();
        if (result.Length != original.Length) throw new InvalidOperationException($"internal: encoded {result.Length} bytes, the game's texture has {original.Length}");
        return result;
    }

    static byte[] EncodeLevel(byte[] rgba, int w, int h, Format f, int bb)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        var data = new byte[bw * bh * bb];
        var block = new byte[64];
        Span<byte> ch = stackalloc byte[16];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int x = Math.Min(bx * 4 + i % 4, w - 1), y = Math.Min(by * 4 + i / 4, h - 1);
                    Array.Copy(rgba, (y * w + x) * 4, block, i * 4, 4);
                }
                var dst = data.AsSpan((by * bw + bx) * bb, bb);
                switch (f)
                {
                    case Format.Bc4:
                        for (int i = 0; i < 16; i++) ch[i] = block[i * 4];
                        EncodeBc4(ch, dst);
                        break;
                    case Format.Bc5:
                        for (int i = 0; i < 16; i++) ch[i] = block[i * 4];
                        EncodeBc4(ch, dst[..8]);
                        for (int i = 0; i < 16; i++) ch[i] = block[i * 4 + 1];
                        EncodeBc4(ch, dst[8..]);
                        break;
                    default:
                        Bc7.EncodeBlock(block, dst);
                        break;
                }
            }
        return data;
    }

    static readonly float[] ToLinear = Enumerable.Range(0, 256).Select(i => { double c = i / 255.0; return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)); }).ToArray();
    static byte ToSrgb(float l)
    {
        double c = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(c * 255), 0, 255);
    }

    /// <summary>Next mip level (2x2 box filter; odd sizes clamp).</summary>
    static (byte[], int, int) Half(byte[] src, int w, int h, Format f)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var dst = new byte[nw * nh * 4];
        Span<float> sum = stackalloc float[4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                sum.Clear();
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(x * 2 + dx, w - 1), sy = Math.Min(y * 2 + dy, h - 1), at = (sy * w + sx) * 4;
                        if (f == Format.Bc5)
                        {
                            float nx = src[at] / 127.5f - 1, ny = src[at + 1] / 127.5f - 1;
                            sum[0] += nx; sum[1] += ny; sum[2] += MathF.Sqrt(Math.Max(0, 1 - nx * nx - ny * ny));
                            continue;
                        }
                        for (int c = 0; c < 4; c++) sum[c] += f == Format.Bc7Srgb && c < 3 ? ToLinear[src[at + c]] : src[at + c];
                    }
                int o = (y * nw + x) * 4;
                if (f == Format.Bc5)
                {
                    // average of unit normals, renormalised (z is rebuilt by the shader)
                    float nx = sum[0], ny = sum[1], len = MathF.Sqrt(nx * nx + ny * ny + sum[2] * sum[2]);
                    if (len > 0) { nx /= len; ny /= len; }
                    dst[o] = (byte)Math.Clamp((int)Math.Round((nx + 1) * 127.5f), 0, 255);
                    dst[o + 1] = (byte)Math.Clamp((int)Math.Round((ny + 1) * 127.5f), 0, 255);
                    dst[o + 3] = 255;
                    continue;
                }
                for (int c = 0; c < 4; c++)
                    dst[o + c] = f == Format.Bc7Srgb && c < 3 ? ToSrgb(sum[c] / 4) : (byte)Math.Round(sum[c] / 4);
            }
        return (dst, nw, nh);
    }
}
