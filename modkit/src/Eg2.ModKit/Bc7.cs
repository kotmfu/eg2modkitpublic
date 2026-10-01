namespace Eg2.ModKit;

/// <summary>
/// BC7 texture encoding, the format of all the game's GUI textures (DX10 DDS, DXGI 98, no mips). Mode 6 only: one
/// RGBA endpoint pair per 4x4 block with 16 steps between. Plenty for icons and posters; noisy photos band a little.
/// </summary>
public static class Bc7
{
    static readonly int[] W4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

    /// <summary>DX10 DDS (BC7_UNORM, one mip) from RGBA pixels (width*height*4, row-major). Sizes needn't be multiples of 4.</summary>
    public static byte[] EncodeDds(byte[] rgba, int width, int height)
    {
        int bw = (width + 3) / 4, bh = (height + 3) / 4;
        var dds = new byte[148 + bw * bh * 16];
        "DDS "u8.CopyTo(dds);
        void U32(int at, uint v) => BitConverter.TryWriteBytes(dds.AsSpan(at), v);
        U32(4, 124); U32(8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000);      // caps, height, width, pixelformat, linearsize
        U32(12, (uint)height); U32(16, (uint)width); U32(20, (uint)(bw * bh * 16));
        U32(76, 32); U32(80, 0x4); "DX10"u8.CopyTo(dds.AsSpan(84));   // pixel format: fourcc
        U32(108, 0x1000);                                              // caps: texture
        U32(128, 98); U32(132, 3); U32(140, 1);                        // BC7_UNORM, 2D, array size 1
        var block = new byte[64];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int x = Math.Min(bx * 4 + i % 4, width - 1), y = Math.Min(by * 4 + i / 4, height - 1);
                    Array.Copy(rgba, (y * width + x) * 4, block, i * 4, 4);
                }
                EncodeBlock(block, dds.AsSpan(148 + (by * bw + bx) * 16, 16));
            }
        return dds;
    }

    /// <summary>One mode-6 block: endpoints from the colours' principal axis, best p-bits, then a least-squares refit.</summary>
    internal static void EncodeBlock(byte[] px, Span<byte> dst)
    {
        Span<float> mean = stackalloc float[4], axis = stackalloc float[4];
        for (int i = 0; i < 16; i++) for (int c = 0; c < 4; c++) mean[c] += px[i * 4 + c] / 16f;
        // principal axis by power iteration on the covariance
        var cov = new float[4, 4];
        for (int i = 0; i < 16; i++)
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++) cov[a, b] += (px[i * 4 + a] - mean[a]) * (px[i * 4 + b] - mean[b]);
        axis[0] = axis[1] = axis[2] = axis[3] = 1;
        Span<float> n = stackalloc float[4];
        for (int it = 0; it < 8; it++)
        {
            n.Clear();
            for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) n[a] += cov[a, b] * axis[b];
            float len = MathF.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2] + n[3] * n[3]);
            if (len < 1e-6f) break;
            for (int a = 0; a < 4; a++) axis[a] = n[a] / len;
        }
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float t = 0;
            for (int c = 0; c < 4; c++) t += (px[i * 4 + c] - mean[c]) * axis[c];
            lo = Math.Min(lo, t); hi = Math.Max(hi, t);
        }
        var e0 = new float[4]; var e1 = new float[4];
        for (int c = 0; c < 4; c++) { e0[c] = mean[c] + lo * axis[c]; e1[c] = mean[c] + hi * axis[c]; }

        var best = Try(px, e0, e1, out long bestErr);
        // refit endpoints to the chosen indices (least squares), keep it if it's better
        var idx = best.Idx;
        float aa = 0, ab = 0, bb = 0; var ax = new float[4]; var bx = new float[4];
        for (int i = 0; i < 16; i++)
        {
            float w = W4[idx[i]] / 64f, v = 1 - w;
            aa += v * v; ab += v * w; bb += w * w;
            for (int c = 0; c < 4; c++) { ax[c] += v * px[i * 4 + c]; bx[c] += w * px[i * 4 + c]; }
        }
        float det = aa * bb - ab * ab;
        if (Math.Abs(det) > 1e-6f)
        {
            var r0 = new float[4]; var r1 = new float[4];
            for (int c = 0; c < 4; c++) { r0[c] = (ax[c] * bb - bx[c] * ab) / det; r1[c] = (bx[c] * aa - ax[c] * ab) / det; }
            var refit = Try(px, r0, r1, out long err);
            if (err < bestErr) best = refit;
        }
        Pack(best.Q0, best.Q1, best.P0, best.P1, best.Idx, dst);
    }

    sealed record Fit(int[] Q0, int[] Q1, int P0, int P1, int[] Idx);

    /// <summary>Quantise the endpoints for each p-bit pair, index the pixels, return the lowest-error fit.</summary>
    static Fit Try(byte[] px, float[] e0, float[] e1, out long bestErr)
    {
        Fit? best = null; bestErr = long.MaxValue;
        for (int p0 = 0; p0 < 2; p0++)
            for (int p1 = 0; p1 < 2; p1++)
            {
                int[] q0 = new int[4], q1 = new int[4], c0 = new int[4], c1 = new int[4];
                for (int c = 0; c < 4; c++)
                {
                    q0[c] = Math.Clamp((int)MathF.Round((e0[c] - p0) / 2f), 0, 127); c0[c] = q0[c] << 1 | p0;
                    q1[c] = Math.Clamp((int)MathF.Round((e1[c] - p1) / 2f), 0, 127); c1[c] = q1[c] << 1 | p1;
                }
                var idx = new int[16]; long err = 0;
                for (int i = 0; i < 16; i++)
                {
                    long be = long.MaxValue;
                    for (int k = 0; k < 16; k++)
                    {
                        long e = 0;
                        for (int c = 0; c < 4; c++) { int d = Lerp(c0[c], c1[c], k) - px[i * 4 + c]; e += d * d; }
                        if (e < be) { be = e; idx[i] = k; }
                    }
                    err += be;
                }
                if (err < bestErr) { bestErr = err; best = new Fit(q0, q1, p0, p1, idx); }
            }
        return best!;
    }

    static int Lerp(int a, int b, int k) => ((64 - W4[k]) * a + W4[k] * b + 32) >> 6;

    static void Pack(int[] q0, int[] q1, int p0, int p1, int[] idx, Span<byte> dst)
    {
        if (idx[0] >= 8)   // the anchor index is stored in 3 bits: swap the ends so its top bit is 0
        {
            (q0, q1) = (q1, q0); (p0, p1) = (p1, p0);
            idx = idx.Select(i => 15 - i).ToArray();
        }
        var o = new byte[16];
        int bit = 0;
        void Put(int v, int n) { for (int i = 0; i < n; i++, bit++) if ((v >> i & 1) != 0) o[bit >> 3] |= (byte)(1 << (bit & 7)); }
        Put(1 << 6, 7);
        for (int c = 0; c < 4; c++) { Put(q0[c], 7); Put(q1[c], 7); }
        Put(p0, 1); Put(p1, 1);
        for (int i = 0; i < 16; i++) Put(idx[i], i == 0 ? 3 : 4);
        o.CopyTo(dst);
    }

    /// <summary>RGBA pixels of the top level of a BC7 DDS (DX10 header).</summary>
    public static byte[] DecodeDds(byte[] dds, out int width, out int height)
    {
        height = BitConverter.ToInt32(dds, 12); width = BitConverter.ToInt32(dds, 16);
        return Dds.DecodeLevel(dds.AsSpan(148), width, height, DecodeBlock, 16);
    }

    // ---- full decoder (all eight modes), used to recolour the game's own textures ----
    // mode: subsets, partition bits, rotation bits, index-selection bit, colour bits, alpha bits, endpoint p-bits,
    // shared p-bits, index bits, second index bits
    static readonly int[,] Modes =
    {
        { 3, 4, 0, 0, 4, 0, 1, 0, 3, 0 }, { 2, 6, 0, 0, 6, 0, 0, 1, 3, 0 }, { 3, 6, 0, 0, 5, 0, 0, 0, 2, 0 },
        { 2, 6, 0, 0, 7, 0, 1, 0, 2, 0 }, { 1, 0, 2, 1, 5, 6, 0, 0, 2, 3 }, { 1, 0, 2, 0, 7, 8, 0, 0, 2, 2 },
        { 1, 0, 0, 0, 7, 7, 1, 0, 4, 0 }, { 2, 6, 0, 0, 5, 5, 1, 0, 2, 0 },
    };
    static readonly int[] W2 = { 0, 21, 43, 64 }, W3 = { 0, 9, 18, 27, 37, 46, 55, 64 };

    /// <summary>2-subset partitions: bit i set = pixel i is in subset 1.</summary>
    static readonly ushort[] Part2 =
    {
        0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80, 0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
        0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE, 0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
        0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696, 0xA55A, 0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660,
        0x0272, 0x04E4, 0x4E40, 0x2720, 0xC936, 0x936C, 0x39C6, 0x639C, 0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22,
    };

    /// <summary>3-subset partitions, 16 subset numbers each (2 bits per pixel, pixel 0 lowest).</summary>
    static readonly string[] Part3 =
    {
        "0011001102212222", "0001001122112221", "0000200122112211", "0222002200110111", "0000000011221122", "0011001100220022",
        "0022002211111111", "0011001122112211", "0000000011112222", "0000111111112222", "0000111122222222", "0012001200120012",
        "0112011201120112", "0122012201220122", "0011011211221222", "0011200122002220", "0001001101121122", "0111001120012200",
        "0000112211221122", "0022002200221111", "0111011102220222", "0001000122212221", "0000001101220122", "0000110022102210",
        "0122012200110000", "0012001211222222", "0110122112210110", "0000011012211221", "0022110211020022", "0110011020022222",
        "0011012201220011", "0000200022112221", "0000000211221222", "0222002200120011", "0011001200220222", "0120012001200120",
        "0000111122220000", "0120120120120120", "0120201212010120", "0011220011220011", "0011112222000011", "0101010122222222",
        "0000000021212121", "0022112200221122", "0022001100220011", "0220122102201221", "0101222222220101", "0000212121212121",
        "0101010101012222", "0222011102220111", "0002111200021112", "0000211221122112", "0222011101110222", "0002111211120002",
        "0110011001102222", "0000000021122112", "0110011022222222", "0022001100110022", "0022112211220022", "0000000000002112",
        "0002000100020001", "0222122202221222", "0101222222222222", "0111201122012220",
    };

    static readonly byte[] Anchor2 =
    {
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
        15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6, 6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15,
    };
    static readonly byte[] Anchor3A =
    {
        3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3, 3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15,
        8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15, 3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3,
    };
    static readonly byte[] Anchor3B =
    {
        15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8, 15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8,
        15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8, 15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8,
    };

    static int Subset(int ns, int part, int i) => ns switch
    {
        1 => 0,
        2 => Part2[part] >> i & 1,
        _ => Part3[part][i] - '0',
    };

    static bool IsAnchor(int ns, int part, int i) => i == 0 || ns == 2 && i == Anchor2[part]
        || ns == 3 && (i == Anchor3A[part] || i == Anchor3B[part]);

    /// <summary>One 16-byte BC7 block to 16 RGBA pixels (64 bytes).</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> b, Span<byte> px)
    {
        int bit = 0;
        int Get(ReadOnlySpan<byte> s, int n) { int v = 0; for (int i = 0; i < n; i++, bit++) v |= (s[bit >> 3] >> (bit & 7) & 1) << i; return v; }
        int mode = 0;
        while (mode < 8 && Get(b, 1) == 0) mode++;
        if (mode == 8) { px.Clear(); return; }   // reserved: transparent black
        int ns = Modes[mode, 0], cb = Modes[mode, 4], ab = Modes[mode, 5], epb = Modes[mode, 6], spb = Modes[mode, 7];
        int ib = Modes[mode, 8], ib2 = Modes[mode, 9];
        int part = Get(b, Modes[mode, 1]), rot = Get(b, Modes[mode, 2]), isb = Get(b, Modes[mode, 3]);
        Span<int> ep = stackalloc int[6 * 4];   // [endpoint][channel], endpoint = subset*2 + 0/1
        for (int c = 0; c < 3; c++) for (int e = 0; e < ns * 2; e++) ep[e * 4 + c] = Get(b, cb);
        for (int e = 0; e < ns * 2; e++) ep[e * 4 + 3] = ab > 0 ? Get(b, ab) : 255;
        int cbits = cb, abits = ab;
        if (epb > 0 || spb > 0)
        {
            Span<int> p = stackalloc int[6];
            if (epb > 0) for (int e = 0; e < ns * 2; e++) p[e] = Get(b, 1);
            else for (int s = 0; s < ns; s++) p[s * 2] = p[s * 2 + 1] = Get(b, 1);
            for (int e = 0; e < ns * 2; e++)
            {
                for (int c = 0; c < 3; c++) ep[e * 4 + c] = ep[e * 4 + c] << 1 | p[e];
                if (ab > 0) ep[e * 4 + 3] = ep[e * 4 + 3] << 1 | p[e];
            }
            cbits++; if (ab > 0) abits++;
        }
        for (int e = 0; e < ns * 2; e++)
        {
            for (int c = 0; c < 3; c++) ep[e * 4 + c] = Expand(ep[e * 4 + c], cbits);
            if (ab > 0) ep[e * 4 + 3] = Expand(ep[e * 4 + 3], abits);
        }
        Span<int> i1 = stackalloc int[16], i2 = stackalloc int[16];
        for (int i = 0; i < 16; i++) i1[i] = Get(b, IsAnchor(ns, part, i) ? ib - 1 : ib);
        if (ib2 > 0) for (int i = 0; i < 16; i++) i2[i] = Get(b, i == 0 ? ib2 - 1 : ib2);
        for (int i = 0; i < 16; i++)
        {
            int s = Subset(ns, part, i);
            int ci = i1[i], ai = i1[i], cw = ib, aw = ib;
            if (ib2 > 0) { if (isb == 0) { ai = i2[i]; aw = ib2; } else { ci = i2[i]; cw = ib2; ai = i1[i]; aw = ib; } }
            for (int c = 0; c < 4; c++)
            {
                bool alpha = c == 3;
                int k = alpha ? ai : ci, bits = alpha ? aw : cw;
                int w = bits == 2 ? W2[k] : bits == 3 ? W3[k] : W4[k];
                px[i * 4 + c] = (byte)(((64 - w) * ep[s * 8 + c] + w * ep[s * 8 + 4 + c] + 32) >> 6);
            }
            if (rot > 0) { var t = px[i * 4 + 3]; px[i * 4 + 3] = px[i * 4 + rot - 1]; px[i * 4 + rot - 1] = t; }
        }
    }

    static int Expand(int v, int bits) => (v << (8 - bits)) | (v >> (2 * bits - 8));
}
