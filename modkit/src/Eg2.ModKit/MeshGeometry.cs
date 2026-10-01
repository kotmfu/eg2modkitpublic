using System.Globalization;
using System.Numerics;
using System.Text;
using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Model geometry: the payload of an RSCF with type 8 (one per LOD: "name", "l1#name".."l5#name"; HANDOFF round 28).
/// [u32 submeshes][u32 verts][u32 indices][u32 triangles], per submesh [u32 material hash][u32 0*][u32 index count]
/// [u32 0][u32 group] (starts are cumulative), [f32 scale x3][f32 min x3], verts x 48 bytes, u16 indices.
/// Vertex: +0 u16 x3 position = min + q/65536*scale, +6 u16 0xffff; +8 tangent, +12 bitangent, +16 normal as
/// unorm 10:10:10:2 ((v-512)/511); +20 u32 (0x20080200 = a zero vector); +24 half2 UV0; +28 half2 UV1; +32 u32
/// (0xff); +36 12 zero bytes. Checked on all 2,350 furniture/common meshes. (*rarely non-zero; kept as is.)
/// </summary>
public sealed class MeshGeometry
{
    public const int Stride = 48;
    public sealed record Submesh(uint Material, uint Unknown1, int IndexCount, uint Unknown3, uint Group);

    public List<Submesh> Submeshes { get; } = new();
    public Vector3 Scale, Min;
    public byte[] Vertices = Array.Empty<byte>();
    public ushort[] Indices = Array.Empty<ushort>();
    public int VertexCount => Vertices.Length / Stride;

    public static bool IsMesh(byte[] rscfBody) => rscfBody.Length >= 4 && Bytes.U32(rscfBody, 0) == 8;

    public static MeshGeometry Parse(byte[] d)
    {
        int sub = (int)Bytes.U32(d, 0), nv = (int)Bytes.U32(d, 4), ni = (int)Bytes.U32(d, 8);
        var m = new MeshGeometry();
        for (int i = 0; i < sub; i++)
        {
            int p = 16 + 20 * i;
            m.Submeshes.Add(new Submesh(Bytes.U32(d, p), Bytes.U32(d, p + 4), (int)Bytes.U32(d, p + 8), Bytes.U32(d, p + 12), Bytes.U32(d, p + 16)));
        }
        int q = 16 + 20 * sub;
        m.Scale = new Vector3(F(d, q), F(d, q + 4), F(d, q + 8));
        m.Min = new Vector3(F(d, q + 12), F(d, q + 16), F(d, q + 20));
        q += 24;
        if (q + nv * Stride + ni * 2 != d.Length || m.Submeshes.Sum(s => s.IndexCount) != ni)
            throw new InvalidDataException("not a model geometry payload (sizes don't add up)");
        m.Vertices = d.AsSpan(q, nv * Stride).ToArray();
        m.Indices = new ushort[ni];
        Buffer.BlockCopy(d, q + nv * Stride, m.Indices, 0, ni * 2);
        return m;
    }

    static float F(byte[] d, int p) => BitConverter.ToSingle(d, p);

    public byte[] ToBytes()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Submeshes.Count); w.Write(VertexCount); w.Write(Indices.Length); w.Write(Indices.Length / 3);
        foreach (var s in Submeshes) { w.Write(s.Material); w.Write(s.Unknown1); w.Write(s.IndexCount); w.Write(s.Unknown3); w.Write(s.Group); }
        w.Write(Scale.X); w.Write(Scale.Y); w.Write(Scale.Z); w.Write(Min.X); w.Write(Min.Y); w.Write(Min.Z);
        w.Write(Vertices);
        foreach (var i in Indices) w.Write(i);
        return ms.ToArray();
    }

    public Vector3 Position(int v) => Min + new Vector3(U16(v, 0), U16(v, 2), U16(v, 4)) / 65536f * Scale;
    public Vector3 Normal(int v) => Unpack(BitConverter.ToUInt32(Vertices, v * Stride + 16));
    public Vector2 Uv(int v) => new((float)BitConverter.ToHalf(Vertices, v * Stride + 24), (float)BitConverter.ToHalf(Vertices, v * Stride + 26));
    ushort U16(int v, int o) => BitConverter.ToUInt16(Vertices, v * Stride + o);

    static Vector3 Unpack(uint p) => new(((p & 0x3ff) - 512f) / 511f, (((p >> 10) & 0x3ff) - 512f) / 511f, (((p >> 20) & 0x3ff) - 512f) / 511f);
    static uint Pack(Vector3 n)
    {
        n = n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.Zero;
        uint C(float f) => (uint)Math.Clamp(MathF.Round(f * 511f + 512f), 0, 1023);
        return C(n.X) | C(n.Y) << 10 | C(n.Z) << 20;
    }

    /// <summary>Wavefront OBJ (one group per submesh, usemtl = material hash). V is flipped (OBJ's origin is bottom-left).</summary>
    public string ToObj(string name)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder($"# {name}: {VertexCount} vertices, {Indices.Length / 3} triangles, {Submeshes.Count} submeshes (Eg2 ModKit)\n");
        for (int v = 0; v < VertexCount; v++) { var p = Position(v); sb.Append(inv, $"v {p.X:R} {p.Y:R} {p.Z:R}\n"); }
        for (int v = 0; v < VertexCount; v++) { var t = Uv(v); sb.Append(inv, $"vt {t.X:R} {1 - t.Y:R}\n"); }
        for (int v = 0; v < VertexCount; v++) { var n = Normal(v); sb.Append(inv, $"vn {n.X:R} {n.Y:R} {n.Z:R}\n"); }
        int at = 0;
        for (int s = 0; s < Submeshes.Count; s++)
        {
            sb.Append($"g submesh{s}\nusemtl mat_{Submeshes[s].Material:x8}\n");
            for (int i = at; i + 2 < at + Submeshes[s].IndexCount; i += 3)
                sb.Append($"f {F3(Indices[i])} {F3(Indices[i + 1])} {F3(Indices[i + 2])}\n");
            at += Submeshes[s].IndexCount;
        }
        return sb.ToString();
        static string F3(int i) => $"{i + 1}/{i + 1}/{i + 1}";
    }

    /// <summary>
    /// New geometry from an OBJ, keeping this mesh's submesh records (materials): OBJ "usemtl mat_xxxxxxxx" (as exported)
    /// or group order picks the submesh; faces with no match go to submesh 0. Polygons are fanned into triangles.
    /// Missing normals are computed; tangents come from the UVs.
    /// </summary>
    public MeshGeometry WithObj(string obj)
    {
        var inv = CultureInfo.InvariantCulture;
        var pos = new List<Vector3>(); var uvs = new List<Vector2>(); var nrm = new List<Vector3>();
        var faces = Submeshes.Select(_ => new List<(int P, int T, int N)[]>()).ToList();
        int cur = 0, groups = -1;
        foreach (var raw in obj.Split('\n'))
        {
            var t = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            float P(int i) => float.Parse(t[i], inv);
            switch (t[0])
            {
                case "v": pos.Add(new Vector3(P(1), P(2), P(3))); break;
                case "vt": uvs.Add(new Vector2(P(1), t.Length > 2 ? 1 - P(2) : 0)); break;
                case "vn": nrm.Add(new Vector3(P(1), P(2), P(3))); break;
                case "g" or "o": groups++; cur = Math.Clamp(groups, 0, Submeshes.Count - 1); break;
                case "usemtl":
                    int k = Submeshes.FindIndex(s => t.Length > 1 && t[1].Equals($"mat_{s.Material:x8}", StringComparison.OrdinalIgnoreCase));
                    if (k >= 0) cur = k;
                    break;
                case "f":
                    var corners = t.Skip(1).Select(c =>
                    {
                        var s = c.Split('/');
                        int I(int j, int count) => j < s.Length && s[j].Length > 0 ? (int.Parse(s[j], inv) is var x && x < 0 ? count + x : x - 1) : -1;
                        return (I(0, pos.Count), I(1, uvs.Count), I(2, nrm.Count));
                    }).ToArray();
                    for (int i = 1; i + 1 < corners.Length; i++) faces[cur].Add(new[] { corners[0], corners[i], corners[i + 1] });
                    break;
            }
        }
        if (pos.Count == 0 || faces.All(f => f.Count == 0)) throw new InvalidDataException("the OBJ has no faces");

        // one game vertex per distinct (position, uv, normal) corner
        var map = new Dictionary<(int, int, int), int>();
        var vp = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>();
        var idx = new List<int>(); var counts = new List<int>();
        foreach (var list in faces)
        {
            counts.Add(list.Count * 3);
            foreach (var f in list)
            foreach (var c in f)
            {
                if (!map.TryGetValue(c, out int v))
                {
                    map[c] = v = vp.Count;
                    vp.Add(pos[c.P]); vt.Add(c.T >= 0 ? uvs[c.T] : Vector2.Zero); vn.Add(c.N >= 0 ? nrm[c.N] : Vector3.Zero);
                }
                idx.Add(v);
            }
        }
        if (vp.Count > 65535) throw new InvalidDataException($"too many vertices ({vp.Count:N0}; the game's limit is 65,535 per mesh)");

        // normals where the OBJ had none; tangents/bitangents from UV derivatives
        var accN = new Vector3[vp.Count]; var accT = new Vector3[vp.Count]; var accB = new Vector3[vp.Count];
        for (int i = 0; i < idx.Count; i += 3)
        {
            int a = idx[i], b = idx[i + 1], c = idx[i + 2];
            Vector3 e1 = vp[b] - vp[a], e2 = vp[c] - vp[a];
            var fn = Vector3.Cross(e1, e2);
            Vector2 d1 = vt[b] - vt[a], d2 = vt[c] - vt[a];
            float r = d1.X * d2.Y - d2.X * d1.Y;
            var tan = r != 0 ? (e1 * d2.Y - e2 * d1.Y) / r : e1;
            var bit = r != 0 ? (e2 * d1.X - e1 * d2.X) / r : e2;
            foreach (int v in new[] { a, b, c }) { accN[v] += fn; accT[v] += tan; accB[v] += bit; }
        }

        var min = new Vector3(vp.Min(p => p.X), vp.Min(p => p.Y), vp.Min(p => p.Z));
        var max = new Vector3(vp.Max(p => p.X), vp.Max(p => p.Y), vp.Max(p => p.Z));
        // the game's meshes use scale = extent (slightly padded), never below 1
        var scale = Vector3.Max((max - min) * (65536f / 65535f), Vector3.One);
        var template = Vertices.Length >= Stride ? Vertices.AsSpan(0, Stride).ToArray() : DefaultVertex();
        var verts = new byte[vp.Count * Stride];
        for (int v = 0; v < vp.Count; v++)
        {
            var o = verts.AsSpan(v * Stride, Stride);
            template.CopyTo(o);
            var q = (vp[v] - min) / scale * 65536f;
            BitConverter.TryWriteBytes(o[0..], (ushort)Math.Clamp(MathF.Round(q.X), 0, 65535));
            BitConverter.TryWriteBytes(o[2..], (ushort)Math.Clamp(MathF.Round(q.Y), 0, 65535));
            BitConverter.TryWriteBytes(o[4..], (ushort)Math.Clamp(MathF.Round(q.Z), 0, 65535));
            BitConverter.TryWriteBytes(o[6..], (ushort)0xffff);
            var n = vn[v].LengthSquared() > 0 ? vn[v] : accN[v];
            n = n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitZ;
            var tg = accT[v] - n * Vector3.Dot(n, accT[v]);   // Gram-Schmidt
            if (tg.LengthSquared() < 1e-12f) tg = Vector3.Cross(n, MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
            tg = Vector3.Normalize(tg);
            var bt = Vector3.Cross(n, tg);
            if (Vector3.Dot(bt, accB[v]) < 0) bt = -bt;
            BitConverter.TryWriteBytes(o[8..], Pack(tg));
            BitConverter.TryWriteBytes(o[12..], Pack(bt));
            BitConverter.TryWriteBytes(o[16..], Pack(n));
            BitConverter.TryWriteBytes(o[24..], (Half)vt[v].X); BitConverter.TryWriteBytes(o[26..], (Half)vt[v].Y);
            BitConverter.TryWriteBytes(o[28..], (Half)vt[v].X); BitConverter.TryWriteBytes(o[30..], (Half)vt[v].Y);
        }
        var m = new MeshGeometry { Scale = scale, Min = min, Vertices = verts, Indices = idx.Select(i => (ushort)i).ToArray() };
        // ponytail: every material record is kept, even with 0 faces; untested whether the engine minds an empty submesh
        for (int s = 0; s < Submeshes.Count; s++) m.Submeshes.Add(Submeshes[s] with { IndexCount = counts[s] });
        return m;
    }

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        for (int v = 0; v < VertexCount; v++) { var p = Position(v); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        return (lo, hi);
    }

    /// <summary>
    /// A model's HSBB (bounds) chunk body: [u32 1][u32 0][name NUL-padded to 4][u32 n][n x (xmin xmax ymin ymax zmin zmax)]
    /// [u32]. In common all 752 equal their LOD0 mesh's bounds; furniture has multi-box and clipped ones (187/262 equal).
    /// Returns the body with the new mesh's bounds, or null when the chunk isn't a single box equal to the old mesh's
    /// bounds (then it was authored by hand and is left alone).
    /// </summary>
    public static byte[]? UpdatedBounds(byte[] hsbb, MeshGeometry old, MeshGeometry neu)
    {
        int e = Array.IndexOf(hsbb, (byte)0, 8);
        if (e < 0) return null;
        int at = 8 + ((e - 8 + 1 + 3) & ~3);
        if (at + 4 + 24 + 4 != hsbb.Length || Bytes.U32(hsbb, at) != 1) return null;
        at += 4;
        var (lo, hi) = old.Bounds();
        float[] was = { lo.X, hi.X, lo.Y, hi.Y, lo.Z, hi.Z };
        for (int i = 0; i < 6; i++) if (MathF.Abs(BitConverter.ToSingle(hsbb, at + 4 * i) - was[i]) > 0.01f) return null;
        var (nlo, nhi) = neu.Bounds();
        var b = (byte[])hsbb.Clone();
        float[] now = { nlo.X, nhi.X, nlo.Y, nhi.Y, nlo.Z, nhi.Z };
        for (int i = 0; i < 6; i++) BitConverter.TryWriteBytes(b.AsSpan(at + 4 * i), now[i]);
        return b;
    }

    static byte[] DefaultVertex()
    {
        var v = new byte[Stride];
        BitConverter.TryWriteBytes(v.AsSpan(20), 0x20080200u);
        v[32] = 0xff;
        return v;
    }
}
