namespace Eg2.Asura;

/// <summary>
/// zlib encoder with a 4 KiB window -- the window Rebellion's archives declare
/// (header 0x48 0x89). .NET's ZLibStream always writes 32 KiB-window streams,
/// and a loader initialised for 4 KiB would reject back-references beyond that.
///
/// Byte-for-byte port of modkit/reference/deflate4k.py (the executable spec);
/// Diagnostics &gt; Self-test checks the output against reference/golden.json.
/// Greedy LZ77 (3-byte hash, chain 32, distance &lt;= 4096) + one final
/// fixed-Huffman block + Adler-32.
/// </summary>
public static class Zlib4k
{
    static readonly int[] LenBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
    static readonly int[] LenExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
    static readonly int[] DistBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
    static readonly int[] DistExtra = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };

    const int Window = 4096;
    const int MinMatch = 3, MaxMatch = 258;
    const int HashBits = 15, HashMask = (1 << HashBits) - 1;
    const int MaxChain = 32;

    sealed class BitWriter
    {
        byte[] _buf;
        int _len;
        ulong _acc;
        int _n;

        public BitWriter(int capacity) => _buf = new byte[Math.Max(capacity, 64)];

        void Put(byte b)
        {
            if (_len == _buf.Length) Array.Resize(ref _buf, _buf.Length * 2);
            _buf[_len++] = b;
        }

        public void Bits(uint value, int count)
        {
            _acc |= (ulong)value << _n;
            _n += count;
            while (_n >= 8)
            {
                Put((byte)(_acc & 0xFF));
                _acc >>= 8;
                _n -= 8;
            }
        }

        public void Huff(uint code, int length)
        {
            uint rev = 0;
            for (int i = 0; i < length; i++)
            {
                rev = (rev << 1) | (code & 1);
                code >>= 1;
            }
            Bits(rev, length);
        }

        public byte[] Flush()
        {
            if (_n > 0)
            {
                Put((byte)(_acc & 0xFF));
                _acc = 0;
                _n = 0;
            }
            return _buf.AsSpan(0, _len).ToArray();
        }
    }

    static void Lit(BitWriter w, int v)
    {
        if (v < 144) w.Huff((uint)(0x30 + v), 8);
        else if (v < 256) w.Huff((uint)(0x190 + v - 144), 9);
        else if (v < 280) w.Huff((uint)(v - 256), 7);
        else w.Huff((uint)(0xC0 + v - 280), 8);
    }

    static void Length(BitWriter w, int length)
    {
        int i = 0;
        while (i + 1 < LenBase.Length && LenBase[i + 1] <= length) i++;
        Lit(w, 257 + i);
        if (LenExtra[i] != 0) w.Bits((uint)(length - LenBase[i]), LenExtra[i]);
    }

    static void Dist(BitWriter w, int dist)
    {
        int i = 0;
        while (i + 1 < DistBase.Length && DistBase[i + 1] <= dist) i++;
        w.Huff((uint)i, 5);
        if (DistExtra[i] != 0) w.Bits((uint)(dist - DistBase[i]), DistExtra[i]);
    }

    static int Hash(byte[] d, int i) => ((d[i] << 10) ^ (d[i + 1] << 5) ^ d[i + 2]) & HashMask;

    public static byte[] DeflateFixed(byte[] data)
    {
        int n = data.Length;
        var head = new int[1 << HashBits];
        Array.Fill(head, -1);
        var prev = new int[n];
        Array.Fill(prev, -1);
        var w = new BitWriter(n / 2 + 64);
        w.Bits(1, 1); // BFINAL
        w.Bits(1, 2); // BTYPE = 01, fixed Huffman
        int i = 0;
        while (i < n)
        {
            int bestLen = 0, bestDist = 0;
            if (i + MinMatch <= n)
            {
                int h = Hash(data, i);
                int cand = head[h];
                int chain = 0;
                int limit = Math.Min(MaxMatch, n - i);
                while (cand >= 0 && i - cand <= Window && chain < MaxChain)
                {
                    int l = 0;
                    while (l < limit && data[cand + l] == data[i + l]) l++;
                    if (l > bestLen)
                    {
                        bestLen = l;
                        bestDist = i - cand;
                        if (l == limit) break;
                    }
                    cand = prev[cand];
                    chain++;
                }
            }
            if (bestLen >= MinMatch)
            {
                Length(w, bestLen);
                Dist(w, bestDist);
                int end = i + bestLen;
                while (i < end)
                {
                    if (i + MinMatch <= n)
                    {
                        int h = Hash(data, i);
                        prev[i] = head[h];
                        head[h] = i;
                    }
                    i++;
                }
            }
            else
            {
                Lit(w, data[i]);
                if (i + MinMatch <= n)
                {
                    int h = Hash(data, i);
                    prev[i] = head[h];
                    head[h] = i;
                }
                i++;
            }
        }
        Lit(w, 256);
        return w.Flush();
    }

    public static uint Adler32(byte[] data)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        int i = 0;
        while (i < data.Length)
        {
            int chunk = Math.Min(5552, data.Length - i);
            for (int k = 0; k < chunk; k++)
            {
                a += data[i + k];
                b += a;
            }
            a %= Mod;
            b %= Mod;
            i += chunk;
        }
        return (b << 16) | a;
    }

    /// <summary>
    /// Smaller output, for a block that must fit back into its old space (<see cref="AsuraArchive.PatchBlocks"/>):
    /// lazy LZ77 with long hash chains, and dynamic-Huffman blocks of up to 16K symbols. Same window and header.
    /// </summary>
    public static byte[] CompressSmall(byte[] data)
    {
        // tokens: (0, literal) or (length, distance)
        int n = data.Length;
        var tokens = new List<(int Len, int Val)>(n / 3);
        var head = new int[1 << HashBits];
        Array.Fill(head, -1);
        var prev = new int[n];
        void Insert(int i) { if (i + MinMatch <= n) { int h = Hash(data, i); prev[i] = head[h]; head[h] = i; } }
        (int Len, int Dist) Find(int i)
        {
            int best = 0, dist = 0;
            if (i + MinMatch > n) return (0, 0);
            int limit = Math.Min(MaxMatch, n - i), chain = 0;
            for (int c = head[Hash(data, i)]; c >= 0 && i - c <= Window && chain < 1024; c = prev[c], chain++)
            {
                if (data[c + best] != data[i + best]) continue;
                int l = 0;
                while (l < limit && data[c + l] == data[i + l]) l++;
                if (l > best) { best = l; dist = i - c; if (l == limit) break; }
            }
            return best >= MinMatch ? (best, dist) : (0, 0);
        }
        int pos = 0;
        var cur = Find(0);
        while (pos < n)
        {
            if (cur.Len == 0) { tokens.Add((0, data[pos])); Insert(pos); pos++; cur = Find(pos); continue; }
            Insert(pos);
            var next = pos + 1 < n ? Find(pos + 1) : (Len: 0, Dist: 0);
            if (next.Len > cur.Len) { tokens.Add((0, data[pos])); pos++; cur = next; continue; }   // lazy: a better match starts next
            tokens.Add((cur.Len, cur.Dist));
            for (int k = 1; k < cur.Len; k++) Insert(pos + k);
            pos += cur.Len;
            cur = Find(pos);
        }
        var w = new BitWriter(n / 2 + 64);
        const int PerBlock = 16384;
        for (int s = 0; s < tokens.Count || s == 0; s += PerBlock)
            EmitDynamic(w, tokens, s, Math.Min(tokens.Count, s + PerBlock), s + PerBlock >= tokens.Count);
        return Wrap(w.Flush(), data);
    }

    static int LenCode(int length) { int i = 0; while (i + 1 < LenBase.Length && LenBase[i + 1] <= length) i++; return i; }
    static int DistCode(int dist) { int i = 0; while (i + 1 < DistBase.Length && DistBase[i + 1] <= dist) i++; return i; }

    static void EmitDynamic(BitWriter w, List<(int Len, int Val)> tokens, int from, int to, bool last)
    {
        var lf = new int[286]; var df = new int[30];
        for (int t = from; t < to; t++)
            if (tokens[t].Len == 0) lf[tokens[t].Val]++;
            else { lf[257 + LenCode(tokens[t].Len)]++; df[DistCode(tokens[t].Val)]++; }
        lf[256]++;
        // every alphabet gets at least two used symbols (a one-symbol code isn't complete)
        if (df.Count(f => f > 0) < 2) { if (df[0] == 0) df[0] = 1; else df[1] = 1; if (df.Count(f => f > 0) < 2) df[1] = 1; }
        var ll = Lengths(lf, 15); var dl = Lengths(df, 15);
        int hlit = 286; while (hlit > 257 && ll[hlit - 1] == 0) hlit--;
        int hdist = 30; while (hdist > 1 && dl[hdist - 1] == 0) hdist--;
        // code lengths, run-length coded: (symbol, extra bits value)
        var all = ll.Take(hlit).Concat(dl.Take(hdist)).ToArray();
        var rl = new List<(int Sym, int Extra)>();
        for (int i = 0; i < all.Length;)
        {
            int v = all[i], run = 1;
            while (i + run < all.Length && all[i + run] == v) run++;
            int left = run;
            if (v == 0)
                while (left > 0)
                {
                    if (left >= 11) { int r = Math.Min(left, 138); rl.Add((18, r - 11)); left -= r; }
                    else if (left >= 3) { rl.Add((17, left - 3)); left = 0; }
                    else { rl.Add((0, 0)); left--; }
                }
            else
            {
                rl.Add((v, 0)); left--;
                while (left >= 3) { int r = Math.Min(left, 6); rl.Add((16, r - 3)); left -= r; }
                while (left-- > 0) rl.Add((v, 0));
            }
            i += run;
        }
        var cf = new int[19];
        foreach (var (sym, _) in rl) cf[sym]++;
        if (cf.Count(f => f > 0) < 2) cf[cf[0] == 0 ? 0 : 1] = Math.Max(1, cf[cf[0] == 0 ? 0 : 1]);
        var cl = Lengths(cf, 7);
        int[] order = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };
        int hclen = 19; while (hclen > 4 && cl[order[hclen - 1]] == 0) hclen--;
        var lc = Codes(ll); var dc = Codes(dl); var cc = Codes(cl);
        w.Bits(last ? 1u : 0u, 1);
        w.Bits(2, 2);
        w.Bits((uint)(hlit - 257), 5);
        w.Bits((uint)(hdist - 1), 5);
        w.Bits((uint)(hclen - 4), 4);
        for (int i = 0; i < hclen; i++) w.Bits((uint)cl[order[i]], 3);
        foreach (var (sym, extra) in rl)
        {
            w.Huff(cc[sym], cl[sym]);
            if (sym == 16) w.Bits((uint)extra, 2); else if (sym == 17) w.Bits((uint)extra, 3); else if (sym == 18) w.Bits((uint)extra, 7);
        }
        for (int t = from; t < to; t++)
        {
            var (len, val) = tokens[t];
            if (len == 0) { w.Huff(lc[val], ll[val]); continue; }
            int li = LenCode(len), di = DistCode(val);
            w.Huff(lc[257 + li], ll[257 + li]);
            if (LenExtra[li] != 0) w.Bits((uint)(len - LenBase[li]), LenExtra[li]);
            w.Huff(dc[di], dl[di]);
            if (DistExtra[di] != 0) w.Bits((uint)(val - DistBase[di]), DistExtra[di]);
        }
        w.Huff(lc[256], ll[256]);
    }

    /// <summary>Huffman code lengths for the frequencies, at most <paramref name="max"/> bits (frequencies are halved
    /// until the tree is shallow enough).</summary>
    static int[] Lengths(int[] freq, int max)
    {
        var f = (int[])freq.Clone();
        while (true)
        {
            var len = new int[f.Length];
            var used = Enumerable.Range(0, f.Length).Where(i => f[i] > 0).ToList();
            if (used.Count == 1) { len[used[0]] = 1; return len; }
            var parent = new Dictionary<int, int>();
            var pq = new PriorityQueue<int, long>();
            int next = f.Length;
            foreach (int i in used) pq.Enqueue(i, (long)f[i] * 1024 + i);
            var weight = used.ToDictionary(i => i, i => (long)f[i]);
            while (pq.Count > 1)
            {
                pq.TryDequeue(out int a, out _); pq.TryDequeue(out int b, out _);
                int node = next++;
                weight[node] = weight[a] + weight[b];
                parent[a] = node; parent[b] = node;
                pq.Enqueue(node, weight[node] * 1024 + node % 1024);
            }
            int deepest = 0;
            foreach (int i in used) { int d = 0; for (int x = i; parent.TryGetValue(x, out var p); x = p) d++; len[i] = d; deepest = Math.Max(deepest, d); }
            if (deepest <= max) return len;
            for (int i = 0; i < f.Length; i++) if (f[i] > 0) f[i] = Math.Max(1, f[i] / 2);
        }
    }

    /// <summary>Canonical codes for the lengths (deflate order).</summary>
    static uint[] Codes(int[] len)
    {
        var count = new int[16];
        foreach (int l in len) if (l > 0) count[l]++;
        var nextCode = new uint[16];
        uint code = 0;
        for (int b = 1; b < 16; b++) { code = (code + (uint)count[b - 1]) << 1; nextCode[b] = code; }
        var codes = new uint[len.Length];
        for (int i = 0; i < len.Length; i++) if (len[i] > 0) codes[i] = nextCode[len[i]]++;
        return codes;
    }

    static byte[] Wrap(byte[] body, byte[] data)
    {
        var o = new byte[body.Length + 6];
        o[0] = 0x48;
        o[1] = 0x89;
        Buffer.BlockCopy(body, 0, o, 2, body.Length);
        uint ad = Adler32(data);
        o[^4] = (byte)(ad >> 24);
        o[^3] = (byte)(ad >> 16);
        o[^2] = (byte)(ad >> 8);
        o[^1] = (byte)ad;
        return o;
    }

    public static byte[] Compress(byte[] data)
    {
        var body = DeflateFixed(data);
        var o = new byte[body.Length + 6];
        o[0] = 0x48;
        o[1] = 0x89;
        Buffer.BlockCopy(body, 0, o, 2, body.Length);
        uint ad = Adler32(data);
        o[^4] = (byte)(ad >> 24);
        o[^3] = (byte)(ad >> 16);
        o[^2] = (byte)(ad >> 8);
        o[^1] = (byte)ad;
        return o;
    }
}
