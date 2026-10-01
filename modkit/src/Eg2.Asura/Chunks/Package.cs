namespace Eg2.Asura.Chunks;

/// <summary>
/// rpkg: the package manifest (misc/packages/&lt;group&gt;/&lt;name&gt;.asr). A data object whose
/// outer property 0x12 starts with an 18-byte load profile:
///   u32 kind (1 required / 3 managed), u32 entitlement (0 base game; per-DLC hash),
///   u32 genius (managed genius_* only), u8[6] flags
/// then length-prefixed u32 id arrays addressed by path ("18.0.1.0" data objects,
/// "18.0.1.1" fnas ids, "18.0.1.2.1.N" typed asset groups), then ONE byte
/// <see cref="Deferred"/> (0 = load content at boot, 1 = at game start).
/// Dev-slot mods must use Entitlement = 0 and Deferred = true.
/// </summary>
public sealed class Package
{
    const int KindOff = 0, EntitlementOff = 4, GeniusOff = 8;

    public Package(DataObject obj) => Obj = obj;

    public DataObject Obj { get; }

    public static Package Parse(string tag, byte[] body) => new(DataObject.Parse(tag, body));

    public uint PackageId => Obj.Header.ObjectId;
    public uint AuxId => Obj.Header.AuxId;

    Prop Top => Obj.Props().First();

    RawNode Profile()
    {
        var head = Top.Children[0] as RawNode;
        if (head is null || head.Data.Length < 12) throw new AsuraFormatException("manifest has no load-profile header");
        return head;
    }

    uint Get(int off) => Bytes.U32(Profile().Data, off);

    void Set(int off, uint value)
    {
        var raw = Profile();
        var buf = (byte[])raw.Data.Clone();
        Bytes.PutU32(buf, off, value);
        raw.Data = buf;
    }

    public uint Kind { get => Get(KindOff); set => Set(KindOff, value); }
    public uint Entitlement { get => Get(EntitlementOff); set => Set(EntitlementOff, value); }
    public uint Genius { get => Get(GeniusOff); set => Set(GeniusOff, value); }

    RawNode DeferredNode()
    {
        var singles = Top.Children.OfType<RawNode>().Where(r => r.Data.Length == 1).ToList();
        if (singles.Count == 0) throw new AsuraFormatException("manifest has no deferred-load byte");
        return singles[^1];
    }

    public bool Deferred
    {
        get => DeferredNode().Data[0] != 0;
        set => DeferredNode().Data = new[] { value ? (byte)1 : (byte)0 };
    }

    public byte[] ToBytes() => Obj.ToBytes();

    /// <summary>Every non-empty length-prefixed u32 array, keyed by property path (e.g. "18.0.1.1").</summary>
    public Dictionary<string, List<uint>> IdArrays()
    {
        var o = new Dictionary<string, List<uint>>();
        void Visit(List<PropNode> nodes, string path)
        {
            int idx = 0;
            foreach (var n in nodes)
            {
                if (n is Prop p)
                {
                    Visit(p.Children, Join(path, p.Id, idx));
                    idx++;
                }
                else if (n is RawNode r && r.Data.Length >= 8)
                {
                    uint count = Bytes.U32(r.Data, 0);
                    if (count != 0 && r.Data.Length == 4 + count * 4)
                    {
                        var ids = new List<uint>((int)count);
                        for (int i = 0; i < count; i++) ids.Add(Bytes.U32(r.Data, 4 + i * 4));
                        o[path] = ids;
                    }
                }
            }
        }
        Visit(Obj.Nodes, "");
        return o;
    }

    /// <summary>Replace the array at <paramref name="path"/> (may grow, shrink or become empty).</summary>
    public void SetArray(string path, IReadOnlyList<uint> ids)
    {
        bool Visit(List<PropNode> nodes, string here)
        {
            int idx = 0;
            foreach (var n in nodes)
            {
                if (n is Prop p)
                {
                    if (Visit(p.Children, Join(here, p.Id, idx))) return true;
                    idx++;
                }
                else if (n is RawNode r && here == path && r.Data.Length >= 4)
                {
                    uint count = Bytes.U32(r.Data, 0);
                    if (r.Data.Length == 4 + count * 4)
                    {
                        var arr = new uint[ids.Count + 1];
                        arr[0] = (uint)ids.Count;
                        for (int i = 0; i < ids.Count; i++) arr[i + 1] = ids[i];
                        r.Data = Bytes.Le(arr);
                        return true;
                    }
                }
            }
            return false;
        }
        if (!Visit(Obj.Nodes, "")) throw new KeyNotFoundException($"no array at {path}");
    }

    public List<uint> ObjectIds()
    {
        var seen = new List<uint>();
        foreach (var kv in IdArrays().OrderBy(kv => kv.Key, PathComparer.Instance))
            foreach (var id in kv.Value)
                if (!seen.Contains(id)) seen.Add(id);
        return seen;
    }

    static string Join(string path, uint id, int idx) => path.Length == 0 ? $"{id}.{idx}" : $"{path}.{id}.{idx}";

    public const string ObjectsPath = "18.0.1.0";
    public const string FnasPath = "18.0.1.1";
    /// <summary>Model name hashes (h*31 of the HSKN name) in furniture's asset groups.</summary>
    public const string ModelsPath = "18.0.1.2.1.6";

    sealed class PathComparer : IComparer<string>
    {
        public static readonly PathComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            var x = (a ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            var y = (b ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return x.Length.CompareTo(y.Length);
        }
    }
}
