using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// An island's outdoor scenery (envs\&lt;lair&gt;.pc, INST chunk, version 18): u32 version, 0, instance count; per instance
/// 64 bytes = position (3 floats), a 3x3 rotation*scale matrix as 12 half floats (rows padded to 4), filler, u16 group
/// at +60, u8 at +62, u8 at +63 (not decoded yet); then u32 group count and 64 bytes per group (u32 instance count,
/// u32 model-part count, u32 first part, ...); then u32 part count and 64 bytes per part (u32 material hash at +0, then
/// buffer ranges and bounds); then the geometry. A group's instances sum to the group's count. Groups are named from
/// their materials (MARE in the same file) -> texture hashes -> the file's own texture names.
/// </summary>
public sealed class IslandScenery
{
    public sealed record Instance(int Index, float X, float Y, float Z, int Group);
    public sealed record Group(int Index, int Count, List<uint> Materials, string Name);

    public List<Instance> Instances { get; } = new();
    public List<Group> Groups { get; } = new();

    readonly AsuraArchive _arc;
    readonly Chunk _inst;

    IslandScenery(AsuraArchive arc, Chunk inst) { _arc = arc; _inst = inst; }

    public static IslandScenery Load(string path) => FromArchive(AsuraArchive.Load(path));

    public static IslandScenery FromArchive(AsuraArchive arc)
    {
        var inst = arc.Chunks.FirstOrDefault(c => c.Tag == "INST") ?? throw new InvalidOperationException("no INST chunk: not an island file");
        var b = inst.Body;
        if (b.Length < 16 || Bytes.U32(b, 0) != 18) throw new InvalidOperationException($"INST version {Bytes.U32(b, 0)}: only 18 is known");
        int n = (int)Bytes.U32(b, 8), groupsAt = 12 + n * 64;
        int m = (int)Bytes.U32(b, groupsAt), partsAt = groupsAt + 4 + m * 64, parts = (int)Bytes.U32(b, partsAt);
        if (partsAt + 4 + parts * 64 > b.Length) throw new InvalidOperationException("INST: tables overrun the chunk");
        var s = new IslandScenery(arc, inst);
        for (int i = 0; i < n; i++)
        {
            int at = 12 + i * 64;
            s.Instances.Add(new Instance(i, BitConverter.ToSingle(b, at), BitConverter.ToSingle(b, at + 4), BitConverter.ToSingle(b, at + 8), BitConverter.ToUInt16(b, at + 60)));
        }
        var names = TextureNames(arc);
        var mats = arc.Chunks.FirstOrDefault(c => c.Tag == "MARE") is { } mare ? Materials.Parse(mare.Body) : null;
        for (int g = 0; g < m; g++)
        {
            int at = groupsAt + 4 + g * 64;
            int count = (int)Bytes.U32(b, at), partCount = (int)Bytes.U32(b, at + 4), first = (int)Bytes.U32(b, at + 8);
            var ms = Enumerable.Range(first, Math.Max(0, Math.Min(partCount, parts - first))).Select(p => Bytes.U32(b, partsAt + 4 + p * 64)).Distinct().ToList();
            s.Groups.Add(new Group(g, count, ms, Name(ms, mats, names) ?? $"group {g}"));
        }
        if (s.Groups.Sum(g => g.Count) != n) throw new InvalidOperationException("INST: group counts don't add up to the instances");
        return s;
    }

    /// <summary>Texture hash -> a short name ("island_elephantgrass") from the file's own textures.</summary>
    static Dictionary<uint, string> TextureNames(AsuraArchive arc)
    {
        var map = new Dictionary<uint, string>();
        foreach (var c in arc.Chunks.Where(c => c.Tag == "RSCF"))
        {
            string path;
            try { path = EmbeddedFile.Parse(c.Body).Path; } catch (AsuraFormatException) { continue; }
            string stem = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(path), "_(c|n|a|h|s|albedo|normal|colour|color|diffuse|spec|mask)$", "");
            map.TryAdd(FurnitureArt.TextureHash(path), stem);
        }
        return map;
    }

    static string? Name(List<uint> materials, Materials? mats, Dictionary<uint, string> textures)
    {
        if (mats is null) return null;
        var found = new List<string>();
        foreach (var h in materials)
            if (mats.Find(h) is { } rec)
                for (int i = 8; i + 4 <= rec.Length; i += 4)
                    if (textures.TryGetValue(Bytes.U32(rec, i), out var t) && !found.Contains(t)) found.Add(t);
        return found.Count == 0 ? null : string.Join(" + ", found.Take(2));
    }

    /// <summary>Hide an instance: its matrix becomes all zero (scale 0), so nothing is drawn. The record stays.</summary>
    public void Hide(int index)
    {
        int at = 12 + index * 64;
        Array.Clear(_inst.Body, at + 12, 24);
    }

    public void Move(int index, float dx, float dy, float dz)
    {
        int at = 12 + index * 64;
        var b = _inst.Body;
        BitConverter.TryWriteBytes(b.AsSpan(at), BitConverter.ToSingle(b, at) + dx);
        BitConverter.TryWriteBytes(b.AsSpan(at + 4), BitConverter.ToSingle(b, at + 4) + dy);
        BitConverter.TryWriteBytes(b.AsSpan(at + 8), BitConverter.ToSingle(b, at + 8) + dz);
        var i = Instances[index];
        Instances[index] = i with { X = i.X + dx, Y = i.Y + dy, Z = i.Z + dz };
        var (node, slot) = Leaf(index);
        var box = Box(node, slot);
        Grow(node, slot, box.Select((v, k) => v + (k % 3 == 0 ? dx : k % 3 == 1 ? dy : dz)).ToArray());   // old spot + new spot
    }

    /// <summary>Add a copy of an instance (same model, rotation and scale), shifted. The copy goes at the end of the
    /// instance table and into a free slot of the culling tree (a 4-wide bounding-box tree after the part table; every
    /// instance is one leaf in it, and anything not in it is never drawn). When the leaf-level nodes are full, the
    /// source's leaf moves into a new node at the end with room for 3 more. Returns the new instance's index.</summary>
    public int Copy(int source, float dx, float dy, float dz)
    {
        var (node, slot) = Leaf(source);
        var box = Box(node, slot).Select((v, k) => v + (k % 3 == 0 ? dx : k % 3 == 1 ? dy : dz)).ToArray();
        int leafNodes = FirstLeafNode;   // nodes before this one hold only nodes (the game's files keep it that way)
        var free = Slots().Where(x => x.Ref == 0 && x.Node >= leafNodes).Select(x => ((int Node, int Slot)?)(x.Node, x.Slot)).FirstOrDefault()
                   ?? (Split(node, slot), 1);
        var b = _inst.Body;
        int n = Instances.Count, groupsAt = 12 + n * 64, group = BitConverter.ToUInt16(b, 12 + source * 64 + 60);
        var rec = b.AsSpan(12 + source * 64, 64).ToArray();
        var nb = new byte[b.Length + 64];
        b.AsSpan(0, groupsAt).CopyTo(nb);
        rec.CopyTo(nb, groupsAt);
        b.AsSpan(groupsAt).CopyTo(nb.AsSpan(groupsAt + 64));
        _inst.Body = nb;
        BitConverter.TryWriteBytes(nb.AsSpan(8), n + 1);
        int g = groupsAt + 64 + 4 + group * 64;
        BitConverter.TryWriteBytes(nb.AsSpan(g), BitConverter.ToUInt32(nb, g) + 1);
        var src = Instances[source];
        Instances.Add(src with { Index = n, X = src.X + dx, Y = src.Y + dy, Z = src.Z + dz });
        Groups[group] = Groups[group] with { Count = Groups[group].Count + 1 };
        BitConverter.TryWriteBytes(nb.AsSpan(groupsAt), src.X + dx);
        BitConverter.TryWriteBytes(nb.AsSpan(groupsAt + 4), src.Y + dy);
        BitConverter.TryWriteBytes(nb.AsSpan(groupsAt + 8), src.Z + dz);
        SetRef(free.Node, free.Slot, (uint)(n << 8) | 1);
        SetBox(free.Node, free.Slot, box);
        Grow(free.Node, free.Slot, box);
        return n;
    }

    // The culling tree: u32 tree count, then u32 node count, u32 (unknown), nodes of 128 bytes: min x/y/z and max x/y/z
    // for 4 children (6 x 4 floats), 4 refs (index << 8 | kind: 0 = node, 1 = instance; 0 = empty slot), 16 bytes zero.
    int NodesAt
    {
        get
        {
            var b = _inst.Body;
            int n = Instances.Count, groupsAt = 12 + n * 64, m = (int)Bytes.U32(b, groupsAt), partsAt = groupsAt + 4 + m * 64;
            return partsAt + 4 + (int)Bytes.U32(b, partsAt) * 64 + 12;
        }
    }
    int NodeCount => (int)Bytes.U32(_inst.Body, NodesAt - 8);
    int FirstLeafNode => (int)Bytes.U32(_inst.Body, NodesAt - 4);

    /// <summary>Move the leaf at (node, slot) into a new node at the end of tree 0 (its slot 0; slots 1-3 empty, with the
    /// inverted box empty slots have) and point the slot at it. Returns the new node's index.</summary>
    int Split(int node, int slot)
    {
        int count = NodeCount, at = NodesAt + count * 128;
        var box = Box(node, slot);
        uint r = Bytes.U32(_inst.Body, NodesAt + node * 128 + 96 + slot * 4);
        var b = _inst.Body;
        var nb = new byte[b.Length + 128];
        b.AsSpan(0, at).CopyTo(nb);
        b.AsSpan(at).CopyTo(nb.AsSpan(at + 128));
        for (int k = 0; k < 6; k++)
            for (int s = 0; s < 4; s++)
                BitConverter.TryWriteBytes(nb.AsSpan(at + k * 16 + s * 4), s == 0 ? box[k] : k < 3 ? 1e30f : -1e30f);
        BitConverter.TryWriteBytes(nb.AsSpan(at + 96), r);
        BitConverter.TryWriteBytes(nb.AsSpan(NodesAt - 8), count + 1);
        _inst.Body = nb;
        SetRef(node, slot, (uint)count << 8);
        return count;
    }

    /// <summary>What's wrong with the culling tree, or null: every instance exactly one leaf whose box holds its position,
    /// the first nodes only nodes, every node reachable once.</summary>
    public string? TreeProblems()
    {
        var seen = new int[Instances.Count];
        var parents = new int[NodeCount];
        foreach (var (node, slot, r) in Slots())
        {
            if (r == 0) continue;
            int i = (int)(r >> 8);
            if ((r & 0xff) == 0)
            {
                if (i >= parents.Length) return $"node {node} points past the nodes";
                parents[i]++;
                var outer = Box(node, slot);   // must hold every box of the child (boxes are model bounds, not pivots)
                for (int s = 0; s < 4; s++)
                    if (Bytes.U32(_inst.Body, NodesAt + i * 128 + 96 + s * 4) != 0 && Box(i, s) is var inner
                        && Enumerable.Range(0, 3).Any(k => inner[k] < outer[k] - 0.01f || inner[k + 3] > outer[k + 3] + 0.01f))
                        return $"node {i} slot {s} sticks out of its parent's box";
                continue;
            }
            if (node < FirstLeafNode) return $"node {node} holds an instance but is before the leaf nodes";
            if (i >= seen.Length) return $"leaf {i} past the instances";
            seen[i]++;
        }
        if (seen.Any(c => c != 1)) return $"{seen.Count(c => c != 1)} instances not exactly once in the tree";
        if (parents.Skip(1).Any(c => c != 1)) return "a node isn't reachable exactly once";
        return null;
    }

    IEnumerable<(int Node, int Slot, uint Ref)> Slots()
    {
        int at = NodesAt, count = NodeCount;
        for (int i = 0; i < count; i++)
            for (int s = 0; s < 4; s++) yield return (i, s, Bytes.U32(_inst.Body, at + i * 128 + 96 + s * 4));
    }

    (int Node, int Slot) Leaf(int index)
    {
        uint want = (uint)(index << 8) | 1;
        foreach (var x in Slots()) if (x.Ref == want) return (x.Node, x.Slot);
        throw new InvalidOperationException($"instance {index} is not in the culling tree");
    }

    float[] Box(int node, int slot)
    {
        int at = NodesAt + node * 128 + slot * 4;
        return Enumerable.Range(0, 6).Select(k => BitConverter.ToSingle(_inst.Body, at + k * 16)).ToArray();   // minX minY minZ maxX maxY maxZ
    }

    void SetBox(int node, int slot, float[] box)
    {
        int at = NodesAt + node * 128 + slot * 4;
        for (int k = 0; k < 6; k++) BitConverter.TryWriteBytes(_inst.Body.AsSpan(at + k * 16), box[k]);
    }

    void SetRef(int node, int slot, uint r) => BitConverter.TryWriteBytes(_inst.Body.AsSpan(NodesAt + node * 128 + 96 + slot * 4), r);

    /// <summary>Widen a slot's box (and every ancestor's) to take in <paramref name="box"/>.</summary>
    void Grow(int node, int slot, float[] box)
    {
        var parent = new Dictionary<int, (int Node, int Slot)>();
        foreach (var x in Slots()) if (x.Ref != 0 && (x.Ref & 0xff) == 0) parent[(int)(x.Ref >> 8)] = (x.Node, x.Slot);
        while (true)
        {
            var old = Box(node, slot);
            SetBox(node, slot, old.Select((v, k) => k < 3 ? Math.Min(v, box[k]) : Math.Max(v, box[k])).ToArray());
            if (!parent.TryGetValue(node, out var up)) return;
            (node, slot) = up;
        }
    }

    public bool IsHidden(int index) => _inst.Body.AsSpan(12 + index * 64 + 12, 24).IndexOfAnyExcept((byte)0) < 0;

    // Geometry: the embedded file "inst (static)" = [u32 vertices][u32 indices][u32 instances], 24-byte vertices, u16
    // indices. Vertex: position 3 x u16 (min + q / 65535 * extent of its part; u16 @6 not decoded), uv 2 x half @8,
    // second uv @12, normal 10:10:10 @16 (unsigned, 512 = 0), @20 not decoded. Part record: [material][vertex count]
    // [index count][0][first vertex][0][first index]... f32 min x/y/z @40, f32 extent x/y/z @52. An instance draws its
    // model at position + M * v (M = the 3x3 as stored, rows padded).
    (Chunk Chunk, EmbeddedFile File) Geometry()
    {
        foreach (var c in _arc.Chunks.Where(c => c.Tag == "RSCF"))
        {
            EmbeddedFile f;
            try { f = EmbeddedFile.Parse(c.Body); } catch (AsuraFormatException) { continue; }
            if (f.Path == "inst (static)") return (c, f);
        }
        throw new InvalidOperationException("no \"inst (static)\" geometry in this island file");
    }

    int PartsAt { get { var b = _inst.Body; int groupsAt = 12 + Instances.Count * 64; return groupsAt + 4 + (int)Bytes.U32(b, groupsAt) * 64; } }

    List<int> PartsOf(int group)
    {
        int at = 12 + Instances.Count * 64 + 4 + group * 64;
        int count = (int)Bytes.U32(_inst.Body, at + 4), first = (int)Bytes.U32(_inst.Body, at + 8);
        return Enumerable.Range(first, count).ToList();
    }

    (int Vertices, int VertexStart, int Indices, int IndexStart, System.Numerics.Vector3 Min, System.Numerics.Vector3 Extent) Part(int q)
    {
        var b = _inst.Body; int at = PartsAt + 4 + q * 64;
        System.Numerics.Vector3 V(int o) => new(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
        return ((int)Bytes.U32(b, at + 4), (int)Bytes.U32(b, at + 16), (int)Bytes.U32(b, at + 8), (int)Bytes.U32(b, at + 24), V(at + 40), V(at + 52));
    }

    /// <summary>A scenery group's model as Wavefront OBJ, in model space: one "g" per part, one OBJ vertex per game
    /// vertex (so an edited copy maps straight back; move vertices, don't add or delete any).</summary>
    public string GroupObj(int group)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var d = Geometry().File.Data;
        int ib = 12 + (int)Bytes.U32(d, 0) * 24;
        var sb = new System.Text.StringBuilder($"# {Groups[group].Name} (island scenery group {group}); Eg2 ModKit. Move vertices only: same count and order.\n");
        int baseV = 1;
        foreach (int q in PartsOf(group))
        {
            var p = Part(q);
            sb.Append($"g part{q}\n");
            for (int v = 0; v < p.Vertices; v++)
            {
                int a = 12 + (p.VertexStart + v) * 24;
                var pos = p.Min + p.Extent * new System.Numerics.Vector3(BitConverter.ToUInt16(d, a), BitConverter.ToUInt16(d, a + 2), BitConverter.ToUInt16(d, a + 4)) / 65535f;
                uint n = Bytes.U32(d, a + 16);
                sb.Append(inv, $"v {pos.X:R} {pos.Y:R} {pos.Z:R}\n");
                sb.Append(inv, $"vt {(float)BitConverter.ToHalf(d, a + 8):R} {1 - (float)BitConverter.ToHalf(d, a + 10):R}\n");
                sb.Append(inv, $"vn {(n & 0x3ff) / 511.5f - 1:R} {((n >> 10) & 0x3ff) / 511.5f - 1:R} {((n >> 20) & 0x3ff) / 511.5f - 1:R}\n");
            }
            for (int i = 0; i + 2 < p.Indices; i += 3)
            {
                int F(int k) => baseV + BitConverter.ToUInt16(d, ib + (p.IndexStart + i + k) * 2);
                sb.Append($"f {F(0)}/{F(0)}/{F(0)} {F(1)}/{F(1)}/{F(1)} {F(2)}/{F(2)}/{F(2)}\n");
            }
            baseV += p.Vertices;
        }
        return sb.ToString();
    }

    /// <summary>Put an edited <see cref="GroupObj"/> back: vertex positions (and normals, when the OBJ has one per
    /// vertex) in the same order. Each part gets new bounds (every part record sharing its vertices too), and every
    /// instance drawing those vertices gets its culling box widened. Returns the number of vertices written.</summary>
    public int SetGroupObj(int group, string obj)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var pos = new List<System.Numerics.Vector3>(); var nrm = new List<System.Numerics.Vector3>();
        foreach (var raw in obj.Split('\n'))
        {
            var t = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 4 || t[0] is not ("v" or "vn")) continue;
            float P(string x) => float.TryParse(x, System.Globalization.NumberStyles.Float, inv, out var f) ? f : throw new ArgumentException($"bad number '{x}' in the OBJ");
            var v = new System.Numerics.Vector3(P(t[1]), P(t[2]), P(t[3]));
            (t[0] == "v" ? pos : nrm).Add(v);
        }
        var parts = PartsOf(group);
        int want = parts.Sum(q => Part(q).Vertices);
        if (pos.Count != want) throw new ArgumentException($"the OBJ has {pos.Count} vertices; {Groups[group].Name} has {want} (move vertices only, don't add or delete)");
        var (chunk, file) = Geometry();
        var d = file.Data;
        var b = _inst.Body;
        int at0 = 0;
        var touched = new HashSet<int>();   // first vertex of each edited range
        foreach (int q in parts)
        {
            var p = Part(q);
            var mine = pos.Skip(at0).Take(p.Vertices).ToList();
            var min = mine.Aggregate(System.Numerics.Vector3.Min);
            var ext = System.Numerics.Vector3.Max(mine.Aggregate(System.Numerics.Vector3.Max) - min, new System.Numerics.Vector3(1e-4f));
            for (int v = 0; v < p.Vertices; v++)
            {
                int a = 12 + (p.VertexStart + v) * 24;
                var u = (mine[v] - min) / ext * 65535f;
                BitConverter.TryWriteBytes(d.AsSpan(a), (ushort)Math.Clamp(MathF.Round(u.X), 0, 65535));
                BitConverter.TryWriteBytes(d.AsSpan(a + 2), (ushort)Math.Clamp(MathF.Round(u.Y), 0, 65535));
                BitConverter.TryWriteBytes(d.AsSpan(a + 4), (ushort)Math.Clamp(MathF.Round(u.Z), 0, 65535));
                if (nrm.Count == want && nrm[at0 + v] is var n && n.LengthSquared() > 0)
                {
                    n = System.Numerics.Vector3.Normalize(n);
                    uint C(float f) => (uint)Math.Clamp(MathF.Round((f + 1) * 511.5f), 0, 1023);
                    BitConverter.TryWriteBytes(d.AsSpan(a + 16), C(n.X) | C(n.Y) << 10 | C(n.Z) << 20);
                }
            }
            for (int r = 0; r < Bytes.U32(b, PartsAt); r++)   // every part record drawing these vertices
                if (Part(r).VertexStart == p.VertexStart)
                {
                    int ra = PartsAt + 4 + r * 64;
                    for (int k = 0; k < 3; k++) { BitConverter.TryWriteBytes(b.AsSpan(ra + 40 + k * 4), min[k]); BitConverter.TryWriteBytes(b.AsSpan(ra + 52 + k * 4), ext[k]); }
                }
            touched.Add(p.VertexStart);
            at0 += p.Vertices;
        }
        chunk.Body = file.ToBytes();
        // culling boxes of every instance whose group draws any edited vertices
        for (int g = 0; g < Groups.Count; g++)
        {
            if (!PartsOf(g).Any(q => touched.Contains(Part(q).VertexStart))) continue;
            foreach (var i in Instances.Where(x => x.Group == g && !IsHidden(x.Index)))
                if (WorldBox(i.Index, d) is { } box) { var (node, slot) = Leaf(i.Index); Grow(node, slot, box); }
        }
        return want;
    }

    /// <summary>Tight world box of an instance's model (position + M * v over its vertices).</summary>
    float[]? WorldBox(int index, byte[] d)
    {
        var b = _inst.Body; int at = 12 + index * 64;
        var m = new float[9];
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) m[r * 3 + c] = (float)BitConverter.ToHalf(b, at + 12 + (r * 4 + c) * 2);
        var o = new System.Numerics.Vector3(BitConverter.ToSingle(b, at), BitConverter.ToSingle(b, at + 4), BitConverter.ToSingle(b, at + 8));
        var lo = new System.Numerics.Vector3(float.MaxValue); var hi = new System.Numerics.Vector3(float.MinValue);
        foreach (int q in PartsOf(Instances[index].Group))
        {
            var p = Part(q);
            for (int v = 0; v < p.Vertices; v++)
            {
                int a = 12 + (p.VertexStart + v) * 24;
                var l = p.Min + p.Extent * new System.Numerics.Vector3(BitConverter.ToUInt16(d, a), BitConverter.ToUInt16(d, a + 2), BitConverter.ToUInt16(d, a + 4)) / 65535f;
                var w = o + new System.Numerics.Vector3(m[0] * l.X + m[1] * l.Y + m[2] * l.Z, m[3] * l.X + m[4] * l.Y + m[5] * l.Z, m[6] * l.X + m[7] * l.Y + m[8] * l.Z);
                lo = System.Numerics.Vector3.Min(lo, w); hi = System.Numerics.Vector3.Max(hi, w);
            }
        }
        return lo.X > hi.X ? null : new[] { lo.X, lo.Y, lo.Z, hi.X, hi.Y, hi.Z };
    }

    /// <summary>The whole .pc again (same compression as the original).</summary>
    public byte[] ToBytes() => _arc.ToBytes(compressed: _arc.Compressed);
}
