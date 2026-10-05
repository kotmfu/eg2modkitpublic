using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>A model's bone hierarchy and bind pose (HSKN, docs/formats/hskn.md).</summary>
public sealed class Skeleton
{
    public string Name = "";
    public string[] Bones = Array.Empty<string>();
    public int[] Parents = Array.Empty<int>();
    public float[][] Pos = Array.Empty<float[]>();
    public float[][] Rot = Array.Empty<float[]>();   // x, y, z, w
    public uint[] Hashes = Array.Empty<uint>();

    /// <summary>[29][version][x][bones][name] parents, bind pose (pos xyz, quat xyzw), [u8], then per bone
    /// [name][u8][u32 size][size bytes]. Null when the body isn't a skeleton.</summary>
    public static Skeleton? Parse(byte[] b)
    {
        try
        {
            if (b.Length < 20 || Bytes.U32(b, 0) != 29) return null;
            int n = (int)Bytes.U32(b, 12);
            if (n <= 0 || n > 1000) return null;
            int e = Array.IndexOf(b, (byte)0, 16);
            var s = new Skeleton { Name = Encoding.Latin1.GetString(b, 16, e - 16) };
            int p = 16 + Bytes.Pad4(e - 16 + 1);
            s.Parents = Enumerable.Range(0, n).Select(i => (int)Bytes.U32(b, p + 4 * i)).ToArray();
            p += 4 * n;
            s.Pos = new float[n][]; s.Rot = new float[n][];
            for (int i = 0; i < n; i++, p += 28)
            {
                s.Pos[i] = new[] { BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8) };
                s.Rot[i] = new[] { BitConverter.ToSingle(b, p + 12), BitConverter.ToSingle(b, p + 16), BitConverter.ToSingle(b, p + 20), BitConverter.ToSingle(b, p + 24) };
            }
            p += 1;
            s.Bones = new string[n];
            for (int i = 0; i < n; i++)
            {
                e = Array.IndexOf(b, (byte)0, p);
                s.Bones[i] = Encoding.Latin1.GetString(b, p, e - p);
                p += Bytes.Pad4(e - p + 1) + 1;
                p += 4 + (int)Bytes.U32(b, p);
            }
            s.Hashes = s.Bones.Select(TextTable.KeyHash).ToArray();
            return s;
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (ArgumentException) { return null; }
    }
}

/// <summary>
/// Animation clips (HCAN) to and from glTF 2.0, for editing in Blender and similar tools.
/// <para>Export writes the clip's skeleton at bind pose as a skinned armature (a one-triangle stand-in mesh makes
/// importers build an armature), the root-motion track as bone "RootMotion" above the skeleton root, and the clip as
/// one animation. The engine is -Y up; node "Asura" turns the scene Y-up and is not animated.</para>
/// <para>Import starts from the game's own clip (rig, bone list, events, effects and bounds stay) and replaces the
/// tracks of the bones it finds by name, resampled at 30 fps with redundant keys dropped.</para>
/// </summary>
public static class AnimationGltf
{
    public const string RootMotion = "RootMotion";
    const double Fps = 30;

    /// <summary>The smallest skeleton holding every bone the clip animates. Failing that, the skeleton sharing the most
    /// bones, with the clip's other bones added under its root at their first key (named by hash). Null when none share a bone.</summary>
    public static Skeleton? SkeletonFor(AnimClip clip, IEnumerable<Skeleton> skeletons)
    {
        var list = skeletons.ToList();
        var full = list.Where(s => s.Hashes.Length >= clip.BoneHashes.Length && clip.BoneHashes.All(new HashSet<uint>(s.Hashes).Contains))
                       .OrderBy(s => s.Hashes.Length).FirstOrDefault();
        if (full is not null) return full;
        var best = list.Select(s => (s, n: clip.BoneHashes.Count(new HashSet<uint>(s.Hashes).Contains)))
                       .Where(x => x.n > 0).OrderByDescending(x => x.n).ThenBy(x => x.s.Hashes.Length).FirstOrDefault().s;
        if (best is null) return null;
        var missing = Enumerable.Range(0, clip.Bones).Where(i => !best.Hashes.Contains(clip.BoneHashes[i])).ToList();
        var names = best.Bones.ToList();
        var parents = best.Parents.ToList();
        var pos = best.Pos.ToList();
        var rot = best.Rot.ToList();
        foreach (var i in missing)
        {
            names.Add($"bone_{clip.BoneHashes[i]:x8}");
            parents.Add(0);
            pos.Add(clip.Tracks[i].Pos[0]);
            rot.Add(clip.Tracks[i].Rot[0].Select(v => (float)v).ToArray());
        }
        return new Skeleton
        {
            Name = best.Name + " (+" + missing.Count + " bones)", Bones = names.ToArray(), Parents = parents.ToArray(),
            Pos = pos.ToArray(), Rot = rot.ToArray(),
            Hashes = best.Hashes.Concat(missing.Select(i => clip.BoneHashes[i])).ToArray(),
        };
    }

    // ---- export

    public static string Export(byte[] hcan, Skeleton sk)
    {
        var clip = AnimClip.Parse(hcan);
        bool root = clip.Tracks.Count == clip.Bones + 1;
        int n = sk.Bones.Length;
        var bin = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        int Add(float[] data, string type, bool minMax = false)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            int at = (int)bin.Length;
            foreach (var f in data) bin.Write(BitConverter.GetBytes(f));
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = at, ["byteLength"] = data.Length * 4 });
            int comps = type switch { "SCALAR" => 1, "VEC3" => 3, "VEC4" => 4, _ => 16 };
            var acc = new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5126, ["count"] = data.Length / comps, ["type"] = type };
            if (minMax) { acc["min"] = new JsonArray(data.Min()); acc["max"] = new JsonArray(data.Max()); }
            accessors.Add(acc);
            return accessors.Count - 1;
        }

        // nodes: 0 Asura (Y-up), 1 RootMotion (when the clip has root motion), then the bones, then the stand-in mesh
        var nodes = new JsonArray();
        int first = root ? 2 : 1;
        nodes.Add(new JsonObject { ["name"] = "Asura", ["rotation"] = new JsonArray(1f, 0f, 0f, 0f), ["children"] = new JsonArray(root ? 1 : first) });
        if (root) nodes.Add(new JsonObject { ["name"] = RootMotion, ["children"] = new JsonArray(first) });
        for (int i = 0; i < n; i++)
        {
            var node = new JsonObject
            {
                ["name"] = sk.Bones[i],
                ["translation"] = new JsonArray(sk.Pos[i][0], sk.Pos[i][1], sk.Pos[i][2]),
                ["rotation"] = new JsonArray(sk.Rot[i][0], sk.Rot[i][1], sk.Rot[i][2], sk.Rot[i][3]),
            };
            var kids = Enumerable.Range(1, n - 1).Where(k => sk.Parents[k] == i).Select(k => (JsonNode)(first + k)).ToArray();
            if (kids.Length > 0) node["children"] = new JsonArray(kids);
            nodes.Add(node);
        }

        // skin: joints are RootMotion and every bone; inverse bind matrices from the world bind pose
        var joints = Enumerable.Range(root ? 1 : first, n + (root ? 1 : 0)).ToArray();
        var world = new double[nodes.Count][];
        world[0] = Mat.Trs(new double[3], new double[] { 1, 0, 0, 0 });
        if (root) world[1] = world[0];
        for (int i = 0; i < n; i++)
            world[first + i] = Mat.Mul(i == 0 ? world[root ? 1 : 0] : world[first + sk.Parents[i]],
                                       Mat.Trs(sk.Pos[i].Select(v => (double)v).ToArray(), sk.Rot[i].Select(v => (double)v).ToArray()));
        var ibm = joints.SelectMany(j => Mat.Invert(world[j]).Select(v => (float)v)).ToArray();
        int ibmAcc = Add(ibm, "MAT4");
        int posAcc = Add(new[] { 0f, 0, 0, 0.01f, 0, 0, 0, 0.01f, 0 }, "VEC3");
        ((JsonObject)accessors[posAcc]!)["min"] = new JsonArray(0f, 0f, 0f);
        ((JsonObject)accessors[posAcc]!)["max"] = new JsonArray(0.01f, 0.01f, 0f);
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        int jat = (int)bin.Length;
        bin.Write(new byte[12]);                                       // JOINTS_0: 3 x u8 x 4, all joint 0
        views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = jat, ["byteLength"] = 12 });
        accessors.Add(new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5121, ["count"] = 3, ["type"] = "VEC4" });
        int jointsAcc = accessors.Count - 1;
        int weightsAcc = Add(new[] { 1f, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }, "VEC4");
        nodes.Add(new JsonObject { ["name"] = "Proxy", ["mesh"] = 0, ["skin"] = 0 });
        int proxy = nodes.Count - 1;

        // animation: one rotation and one translation channel per animated track
        var samplers = new JsonArray();
        var channels = new JsonArray();
        void Channel(int node, AnimTrack t)
        {
            int tin = Add(t.RotT.Select(x => (float)x).ToArray(), "SCALAR", true);
            int tout = Add(t.Rot.SelectMany(q => q.Select(v => (float)v)).ToArray(), "VEC4");
            samplers.Add(new JsonObject { ["input"] = tin, ["output"] = tout, ["interpolation"] = "LINEAR" });
            channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = node, ["path"] = "rotation" } });
            int pin = Add(t.PosT.Select(x => (float)x).ToArray(), "SCALAR", true);
            int pout = Add(t.Pos.SelectMany(v => v).ToArray(), "VEC3");
            samplers.Add(new JsonObject { ["input"] = pin, ["output"] = pout, ["interpolation"] = "LINEAR" });
            channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = node, ["path"] = "translation" } });
        }
        var index = sk.Hashes.Select((h, i) => (h, i)).GroupBy(x => x.h).ToDictionary(g => g.Key, g => g.First().i);
        for (int t = 0; t < clip.Bones; t++) Channel(first + index[clip.BoneHashes[t]], clip.Tracks[t]);
        if (root) Channel(1, clip.Tracks[^1]);

        var doc = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "Eg2 ModKit" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0, proxy) }),
            ["nodes"] = nodes,
            ["meshes"] = new JsonArray(new JsonObject
            {
                ["name"] = "Proxy",
                ["primitives"] = new JsonArray(new JsonObject
                {
                    ["attributes"] = new JsonObject { ["POSITION"] = posAcc, ["JOINTS_0"] = jointsAcc, ["WEIGHTS_0"] = weightsAcc },
                }),
            }),
            ["skins"] = new JsonArray(new JsonObject { ["name"] = sk.Name, ["inverseBindMatrices"] = ibmAcc, ["skeleton"] = joints[0], ["joints"] = new JsonArray(joints.Select(j => (JsonNode)j).ToArray()) }),
            ["animations"] = new JsonArray(new JsonObject { ["name"] = clip.Name, ["samplers"] = samplers, ["channels"] = channels }),
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bin.ToArray()) }),
        };
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    // ---- import

    /// <summary>The game's clip <paramref name="hcan"/> with the motion from a .gltf/.glb file; reports what it matched.</summary>
    public static byte[] Import(byte[] hcan, string path, out string summary)
    {
        var clip = AnimClip.Parse(hcan);
        var (doc, bin) = Load(path);
        var nodes = (JsonArray)doc["nodes"]!;
        var anims = doc["animations"] as JsonArray;
        if (anims is null || anims.Count == 0) throw new InvalidDataException("the file has no animation");
        var anim = anims.FirstOrDefault(a => (string?)a!["name"] == clip.Name) ?? anims[0]!;
        var samplers = (JsonArray)anim["samplers"]!;

        // node index -> its rotation and translation samplers
        var chans = new Dictionary<(int Node, string Path), Sampler>();
        foreach (var ch in (JsonArray)anim["channels"]!)
        {
            var target = ch!["target"]!;
            if (target["node"] is null) continue;
            var s = samplers[(int)ch["sampler"]!]!;
            chans[((int)target["node"]!, (string)target["path"]!)] =
                new Sampler(Floats(doc, bin, (int)s["input"]!), Floats(doc, bin, (int)s["output"]!), (string?)s["interpolation"] ?? "LINEAR");
        }
        double end = chans.Values.Select(s => s.Times.Length > 0 ? s.Times[^1] : 0).DefaultIfEmpty(0).Max();
        int frames = Math.Max(1, (int)Math.Round(end * Fps));
        double dur = frames / Fps;

        var byHash = new Dictionary<uint, int>();
        for (int i = 0; i < nodes.Count; i++)
            if ((string?)nodes[i]!["name"] is { } nm)
                byHash.TryAdd(nm.StartsWith("bone_") && uint.TryParse(nm[5..], System.Globalization.NumberStyles.HexNumber, null, out uint hx) ? hx : TextTable.KeyHash(nm), i);
        bool root = clip.Tracks.Count == clip.Bones + 1;
        int matched = 0, animated = 0;
        for (int t = 0; t < clip.Tracks.Count; t++)
        {
            uint h = t < clip.Bones ? clip.BoneHashes[t] : TextTable.KeyHash(RootMotion);
            if (!byHash.TryGetValue(h, out int ni)) { clip.Tracks[t] = Clamp(clip.Tracks[t], dur); continue; }
            matched++;
            var node = nodes[ni]!;
            chans.TryGetValue((ni, "rotation"), out var rs);
            chans.TryGetValue((ni, "translation"), out var ps);
            if (rs is not null || ps is not null) animated++;
            double[] rest = node["rotation"] is JsonArray r ? r.Select(v => (double)v!).ToArray() : new double[] { 0, 0, 0, 1 };
            double[] restP = node["translation"] is JsonArray tp ? tp.Select(v => (double)v!).ToArray() : new double[3];
            var rot = new List<double[]>();
            var pos = new List<double[]>();
            for (int f = 0; f <= frames; f++)
            {
                double time = f / Fps;
                rot.Add(rs is null ? rest : rs.Quat(time));
                pos.Add(ps is null ? restP : ps.Vec(time, 3));
            }
            // one sign per track, then w >= 0 per key as the game stores it
            for (int k = 1; k < rot.Count; k++) if (Dot(rot[k], rot[k - 1]) < 0) rot[k] = rot[k].Select(v => -v).ToArray();
            var times = Enumerable.Range(0, frames + 1).Select(f => f / Fps).ToArray();
            var rk = Reduce(rot, times, 0.0004, Slerp);
            var pk = Reduce(pos, times, 0.0005, Lerp);
            clip.Tracks[t] = new AnimTrack
            {
                Rot = rk.Select(i => Positive(Normal(rot[i]))).ToArray(),
                RotT = rk.Select(i => times[i]).ToArray(),
                Pos = pk.Select(i => pos[i].Select(v => (float)v).ToArray()).ToArray(),
                PosT = pk.Select(i => times[i]).ToArray(),
            };
        }
        if (matched == 0) throw new InvalidDataException($"no bone of {clip.Name} is in the file (bones are matched by name)");
        clip.Dur = (float)dur;
        if (root)
        {
            var rp = clip.Tracks[^1].Pos;
            double d = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(rp[^1][k] - rp[0][k], 2)));
            clip.Speed = d < 1e-6 ? 0 : (float)(d / dur);
        }
        var bytes = clip.Encode();
        if (!AnimClip.Parse(bytes).Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("the rebuilt clip does not read back");
        summary = $"{matched} of {clip.Tracks.Count} tracks matched by name ({animated} animated), {frames + 1} frames ({dur:0.##} s)";
        return bytes;
    }

    sealed record Sampler(float[] Times, float[] Values, string Interp)
    {
        int Width(int n) => Interp == "CUBICSPLINE" ? 3 * n : n;
        int Offset(int n) => Interp == "CUBICSPLINE" ? n : 0;   // cubic stores in-tangent, value, out-tangent
        (int I, double F) Find(double t)
        {
            if (Times.Length == 0 || t <= Times[0]) return (0, 0);
            if (t >= Times[^1]) return (Times.Length - 1, 0);
            int i = Array.FindLastIndex(Times, x => x <= t);
            double span = Times[i + 1] - Times[i];
            return (i, Interp == "STEP" || span <= 0 ? 0 : (t - Times[i]) / span);
        }
        double[] At(int i, int n) => Enumerable.Range(0, n).Select(k => (double)Values[i * Width(n) + Offset(n) + k]).ToArray();
        public double[] Vec(double t, int n) { var (i, f) = Find(t); return f == 0 ? At(i, n) : Lerp(At(i, n), At(i + 1, n), f); }
        public double[] Quat(double t) { var (i, f) = Find(t); return f == 0 ? Normal(At(i, 4)) : Slerp(At(i, 4), At(i + 1, 4), f); }
    }

    /// <summary>Keys to keep: first, last, and every key its neighbours' interpolation misses by more than <paramref name="tol"/>.
    /// A track that never moves keeps one key.</summary>
    static List<int> Reduce(List<double[]> v, double[] times, double tol, Func<double[], double[], double, double[]> interp)
    {
        if (v.All(x => Diff(x, v[0]) <= tol)) return new List<int> { 0 };
        var keep = new List<int> { 0 };
        int a = 0;
        for (int b = 2; b < v.Count; b++)
        {
            bool fits = true;
            for (int k = a + 1; k < b && fits; k++)
                fits = Diff(interp(v[a], v[b], (times[k] - times[a]) / (times[b] - times[a])), v[k]) <= tol;
            if (!fits) { keep.Add(b - 1); a = b - 1; }
        }
        keep.Add(v.Count - 1);
        return keep;
    }

    static double Diff(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
    static double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();
    static double[] Normal(double[] q) { double n = Math.Sqrt(Dot(q, q)); return n < 1e-12 ? new double[] { 0, 0, 0, 1 } : q.Select(v => v / n).ToArray(); }
    static double[] Positive(double[] q) => q[3] < 0 ? q.Select(v => -v).ToArray() : q;
    static double[] Lerp(double[] a, double[] b, double f) => a.Zip(b, (x, y) => x + (y - x) * f).ToArray();
    static double[] Slerp(double[] a, double[] b, double f)
    {
        double d = Dot(a, b);
        if (d < 0) { b = b.Select(v => -v).ToArray(); d = -d; }
        if (d > 0.9995) return Normal(Lerp(a, b, f));
        double th = Math.Acos(d), s = Math.Sin(th);
        double wa = Math.Sin((1 - f) * th) / s, wb = Math.Sin(f * th) / s;
        return a.Zip(b, (x, y) => wa * x + wb * y).ToArray();
    }

    /// <summary>An unmatched track kept as it was, its keys clipped to the new duration.</summary>
    static AnimTrack Clamp(AnimTrack t, double dur) => new()
    {
        Rot = t.Rot.Where((_, i) => i == 0 || t.RotT[i] <= dur + 1e-6).ToArray(),
        RotT = t.RotT.Where((x, i) => i == 0 || x <= dur + 1e-6).Select(x => Math.Min(x, dur)).ToArray(),
        Pos = t.Pos.Where((_, i) => i == 0 || t.PosT[i] <= dur + 1e-6).ToArray(),
        PosT = t.PosT.Where((x, i) => i == 0 || x <= dur + 1e-6).Select(x => Math.Min(x, dur)).ToArray(),
    };

    /// <summary>.glb (binary container) or .gltf (JSON; buffers as data URIs or files beside it).</summary>
    static (JsonNode Doc, byte[][] Buffers) Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        JsonNode doc;
        byte[]? glbBin = null;
        if (raw.Length >= 12 && Bytes.U32(raw, 0) == 0x46546C67)        // "glTF"
        {
            int p = 12;
            doc = null!;
            while (p + 8 <= raw.Length)
            {
                int len = (int)Bytes.U32(raw, p);
                uint type = Bytes.U32(raw, p + 4);
                if (type == 0x4E4F534A) doc = JsonNode.Parse(Encoding.UTF8.GetString(raw, p + 8, len))!;   // JSON
                else if (type == 0x004E4942) glbBin = raw[(p + 8)..(p + 8 + len)];                          // BIN
                p += 8 + len;
            }
            if (doc is null) throw new InvalidDataException("not a glTF file");
        }
        else doc = JsonNode.Parse(Encoding.UTF8.GetString(raw)) ?? throw new InvalidDataException("not a glTF file");
        var buffers = ((JsonArray?)doc["buffers"] ?? new JsonArray()).Select(b =>
        {
            var uri = (string?)b!["uri"];
            if (uri is null) return glbBin ?? throw new InvalidDataException("missing binary buffer");
            if (uri.StartsWith("data:")) return Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
            return File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, Uri.UnescapeDataString(uri)));
        }).ToArray();
        return (doc, buffers);
    }

    /// <summary>An accessor's values as floats (float, or normalised integer components).</summary>
    static float[] Floats(JsonNode doc, byte[][] bin, int accessor)
    {
        var a = doc["accessors"]![accessor]!;
        int count = (int)a["count"]!;
        int comps = (string)a["type"]! switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => 16 };
        int ct = (int)a["componentType"]!;
        int size = ct switch { 5126 => 4, 5122 or 5123 => 2, _ => 1 };
        var outp = new float[count * comps];
        if (a["bufferView"] is null) return outp;
        var v = doc["bufferViews"]![(int)a["bufferView"]!]!;
        var b = bin[(int)v["buffer"]!];
        int start = ((int?)v["byteOffset"] ?? 0) + ((int?)a["byteOffset"] ?? 0);
        int stride = (int?)v["byteStride"] ?? comps * size;
        for (int i = 0; i < count; i++)
            for (int k = 0; k < comps; k++)
            {
                int o = start + i * stride + k * size;
                outp[i * comps + k] = ct switch
                {
                    5126 => BitConverter.ToSingle(b, o),
                    5122 => Math.Max(BitConverter.ToInt16(b, o) / 32767f, -1),
                    5123 => BitConverter.ToUInt16(b, o) / 65535f,
                    5120 => Math.Max((sbyte)b[o] / 127f, -1),
                    _ => b[o] / 255f,
                };
            }
        return outp;
    }

    /// <summary>Column-major 4x4 matrices, as glTF stores them.</summary>
    static class Mat
    {
        public static double[] Trs(double[] t, double[] q)
        {
            double x = q[0], y = q[1], z = q[2], w = q[3];
            return new[]
            {
                1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
                t[0], t[1], t[2], 1,
            };
        }

        public static double[] Mul(double[] a, double[] b)
        {
            var m = new double[16];
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    m[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
            return m;
        }

        /// <summary>Inverse of a rotation-translation matrix: transpose the rotation, rotate the negated translation.</summary>
        public static double[] Invert(double[] m)
        {
            var r = new double[16];
            for (int c = 0; c < 3; c++) for (int k = 0; k < 3; k++) r[c * 4 + k] = m[k * 4 + c];
            for (int k = 0; k < 3; k++) r[12 + k] = -(r[k] * m[12] + r[4 + k] * m[13] + r[8 + k] * m[14]);
            r[15] = 1;
            return r;
        }
    }
}
