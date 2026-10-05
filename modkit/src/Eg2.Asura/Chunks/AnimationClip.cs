using System.Text;

namespace Eg2.Asura.Chunks;

public class AnimTrack
{
    public double[][] Rot = Array.Empty<double[]>();   // x,y,z,w
    public double[] RotT = Array.Empty<double>();      // seconds
    public float[][] Pos = Array.Empty<float[]>();
    public double[] PosT = Array.Empty<double>();
}
public class AnimMask { public ulong Bits; public float Start, End; public uint Type; }
public class AnimEvent { public float T0; public uint Id; public float T1; public uint Kind; public uint Point; public float Value; public uint Last; }
public class AnimObject { public uint Ver, Cls; public byte[] Data = Array.Empty<byte>(); }
/// <summary>An animation clip (HCAN chunk body; docs/formats/hcan.md), decoded to float tracks. <see cref="AnimClip.Encode"/>
/// writes it back; every clip in the game re-encodes byte for byte.</summary>
public class AnimClip
{
    public uint Flags; public int Bones; public float Dur, Speed; public string Name = "";
    public List<AnimTrack> Tracks = new();
    public float[]? BoneF; public List<AnimMask> Masks = new(); public float[]? Bounds;
    public List<AnimEvent> Events = new(); public uint Rig; public uint[] BoneHashes = Array.Empty<uint>();
    public uint[] Trailer = new uint[4]; public List<uint> Rel = new(); public uint NameHash; public List<AnimObject> Objects = new();
    // raw quantised values for analysis
    public List<ushort[]> RawRotT = new(), RawPosT = new();

    static uint U(byte[] b, int o) => Bytes.U32(b, o);
    static ushort H(byte[] b, int o) => BitConverter.ToUInt16(b, o);
    static float F(byte[] b, int o) => BitConverter.ToSingle(b, o);

    public static AnimClip Parse(byte[] b)
    {
        var c = new AnimClip();
        if (U(b, 0) != 22) throw new Exception("ver");
        c.Flags = U(b, 4); c.Bones = (int)U(b, 8); c.Dur = F(b, 12); c.Speed = F(b, 16); int nmask = (int)U(b, 20);
        int e = Array.IndexOf(b, (byte)0, 24); c.Name = Encoding.Latin1.GetString(b, 24, e - 24);
        int p = 24 + ((e - 24) / 4 + 1) * 4;
        if (U(b, p) != 0 || U(b, p + 4) != 0xffff0000) throw new Exception("pre");
        int nt = (int)U(b, p + 8), rk = (int)U(b, p + 12), pk = (int)U(b, p + 16); p += 20;
        int tab = p; p += 12 * nt;
        int rq = p, rt = rq + 8 * rk, pq = rt + 2 * rk, pt = pq + 12 * pk; p = pt + 2 * pk;
        for (int t = 0; t < nt; t++)
        {
            int o = tab + 12 * t; int nr = H(b, o), np = H(b, o + 2), fr = (int)U(b, o + 4), fp = (int)U(b, o + 8);
            var tr = new AnimTrack { Rot = new double[nr][], RotT = new double[nr], Pos = new float[np][], PosT = new double[np] };
            var rawr = new ushort[nr]; var rawp = new ushort[np];
            for (int i = 0; i < nr; i++)
            {
                int q = rq + 8 * (fr + i);
                tr.Rot[i] = new[] { H(b, q) / 32767.0 - 1, H(b, q + 2) / 32767.0 - 1, H(b, q + 4) / 32767.0 - 1, H(b, q + 6) / 65535.0 };
                rawr[i] = H(b, rt + 2 * (fr + i)); tr.RotT[i] = rawr[i] / 65535.0 * c.Dur;
            }
            for (int i = 0; i < np; i++)
            {
                int q = pq + 12 * (fp + i);
                tr.Pos[i] = new[] { F(b, q), F(b, q + 4), F(b, q + 8) };
                rawp[i] = H(b, pt + 2 * (fp + i)); tr.PosT[i] = rawp[i] / 65535.0 * c.Dur;
            }
            c.Tracks.Add(tr); c.RawRotT.Add(rawr); c.RawPosT.Add(rawp);
        }
        if ((c.Flags & 1) != 0) { c.BoneF = new float[c.Bones]; for (int i = 0; i < c.Bones; i++) c.BoneF[i] = F(b, p + 4 * i); p += 4 * c.Bones; }
        for (int i = 0; i < nmask; i++) { int o = p + 20 * i; c.Masks.Add(new AnimMask { Bits = U(b, o) | ((ulong)U(b, o + 4) << 32), Start = F(b, o + 8), End = F(b, o + 12), Type = U(b, o + 16) }); }
        p += 20 * nmask;
        if ((c.Flags & 8) != 0) { c.Bounds = new float[6]; for (int i = 0; i < 6; i++) c.Bounds[i] = F(b, p + 4 * i); p += 24; }
        if (U(b, p) != 0) throw new Exception("ev0");
        int ne = (int)U(b, p + 4); p += 8;
        for (int i = 0; i < ne; i++, p += 28) c.Events.Add(new AnimEvent { T0 = F(b, p), Id = U(b, p + 4), T1 = F(b, p + 8), Kind = U(b, p + 12), Point = U(b, p + 16), Value = F(b, p + 20), Last = U(b, p + 24) });
        c.Rig = U(b, p); p += 4;
        c.BoneHashes = new uint[c.Bones]; for (int i = 0; i < c.Bones; i++) c.BoneHashes[i] = U(b, p + 4 * i); p += 4 * c.Bones;
        for (int i = 0; i < 4; i++) c.Trailer[i] = U(b, p + 4 * i); p += 16;
        int nr2 = (int)U(b, p); p += 4; for (int i = 0; i < nr2; i++) c.Rel.Add(U(b, p + 4 * i)); p += 4 * nr2;
        c.NameHash = U(b, p); p += 4;
        int nx = (int)U(b, p); p += 4;
        for (int i = 0; i < nx; i++) { int sz = (int)U(b, p + 8); c.Objects.Add(new AnimObject { Ver = U(b, p), Cls = U(b, p + 4), Data = b[(p + 12)..(p + 12 + sz)] }); p += 12 + sz; }
        if (p != b.Length) throw new Exception("len");
        return c;
    }

    public static ushort QRot(double v) => (ushort)Math.Clamp(Math.Round((v + 1) * 32767), 0, 65535);
    public static ushort QW(double v) => (ushort)Math.Clamp(Math.Round(v * 65535), 0, 65535);
    public static ushort QT(double t, double dur) => dur <= 0 ? (ushort)0 : (ushort)Math.Clamp(Math.Round(t / dur * 65535), 0, 65535);

    public byte[] Encode()
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(22u); w.Write(Flags); w.Write(Bones); w.Write(Dur); w.Write(Speed); w.Write(Masks.Count);
        var nb = Encoding.Latin1.GetBytes(Name); w.Write(nb); w.Write(new byte[4 - nb.Length % 4]);
        w.Write(0u); w.Write(0xffff0000u);
        w.Write(Tracks.Count); w.Write(Tracks.Sum(t => t.Rot.Length)); w.Write(Tracks.Sum(t => t.Pos.Length));
        int fr = 0, fp = 0;
        foreach (var t in Tracks) { w.Write((ushort)t.Rot.Length); w.Write((ushort)t.Pos.Length); w.Write(fr); w.Write(fp); fr += t.Rot.Length; fp += t.Pos.Length; }
        foreach (var t in Tracks) foreach (var q in t.Rot) { w.Write(QRot(q[0])); w.Write(QRot(q[1])); w.Write(QRot(q[2])); w.Write(QW(q[3])); }
        foreach (var t in Tracks) foreach (var x in t.RotT) w.Write(QT(x, Dur));
        foreach (var t in Tracks) foreach (var v in t.Pos) { w.Write(v[0]); w.Write(v[1]); w.Write(v[2]); }
        foreach (var t in Tracks) foreach (var x in t.PosT) w.Write(QT(x, Dur));
        if ((Flags & 1) != 0) foreach (var f in BoneF!) w.Write(f);
        foreach (var m in Masks) { w.Write(m.Bits); w.Write(m.Start); w.Write(m.End); w.Write(m.Type); }
        if ((Flags & 8) != 0) foreach (var f in Bounds!) w.Write(f);
        w.Write(0u); w.Write(Events.Count);
        foreach (var v in Events) { w.Write(v.T0); w.Write(v.Id); w.Write(v.T1); w.Write(v.Kind); w.Write(v.Point); w.Write(v.Value); w.Write(v.Last); }
        w.Write(Rig); foreach (var h in BoneHashes) w.Write(h);
        foreach (var x in Trailer) w.Write(x);
        w.Write(Rel.Count); foreach (var r in Rel) w.Write(r);
        w.Write(NameHash); w.Write(Objects.Count);
        foreach (var o in Objects) { w.Write(o.Ver); w.Write(o.Cls); w.Write(o.Data.Length); w.Write(o.Data); }
        return ms.ToArray();
    }
}
