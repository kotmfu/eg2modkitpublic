using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// What plays which WAV (docs/formats hsnd.md, sdev.md, sdsm.md): sound sets (HSND, named after a body model or an animation,
/// triggered by the animation) list events; events (SDEV) list sample variations; samples (SDSM) name the WAV, an RSCF in
/// the sound bank beside their file. Filled from misc\common.asr and the packages as game data loads.
/// </summary>
public sealed class SoundIndex
{
    /// <summary>KeyHash(set name) -> name and the event ids its records trigger.</summary>
    public Dictionary<uint, (string Name, List<uint> Events)> Sets { get; } = new();
    /// <summary>Event id -> name and its variations: (hash, kind) with kind 0 = KeyHash(sample name), 1 = another event.</summary>
    public Dictionary<uint, (string Name, List<(uint Hash, uint Kind)> Parts)> Events { get; } = new();
    /// <summary>KeyHash(sample name) -> WAV path ("Sounds\Vocalisation\Zalika\VOC_….wav").</summary>
    public Dictionary<uint, string> Samples { get; } = new();

    /// <summary>A string: name + NUL, padded to 4 bytes from its own start.</summary>
    static string Str(byte[] b, ref int p)
    {
        int nul = Array.IndexOf(b, (byte)0, p);
        string s = Bytes.Latin1.GetString(b, p, nul - p);
        p += (nul - p + 1 + 3) & ~3;
        return s;
    }

    public void Add(IEnumerable<Chunk> chunks)
    {
        foreach (var c in chunks)
        {
            if (c.Tag is not ("HSND" or "SDEV" or "SDSM")) continue;
            var b = c.Body;
            try
            {
                if (c.Tag == "HSND")
                {
                    int n = (int)Bytes.U32(b, 8), p = 12;
                    string name = Str(b, ref p);
                    var events = new List<uint>();
                    for (int i = 0; i < n && p + 64 <= b.Length; i++, p += 64)
                        if (Bytes.U32(b, p + 20) is var e and not 0) events.Add(e);
                    Sets.TryAdd(TextTable.KeyHash(name), (name, events));
                }
                else if (c.Tag == "SDEV")
                {
                    int p = 8;
                    string name = Str(b, ref p);
                    uint id = Bytes.U32(b, p);
                    p += 4 + 32 + 4 + 4 + 1 + 16 + 16;   // id, volume/pitch/delay floats, m, flags, loop, distances, 4 floats
                    int n = (int)Bytes.U32(b, p);
                    var parts = new List<(uint, uint)>();
                    for (int i = 0; i < n && n <= 64; i++) parts.Add((Bytes.U32(b, p + 4 + 8 * i), Bytes.U32(b, p + 8 + 8 * i)));
                    Events.TryAdd(id, (name, parts));
                }
                else
                {
                    int p = 8;
                    string name = Str(b, ref p);
                    p += 4;   // KeyHash of the path
                    Samples.TryAdd(TextTable.KeyHash(name), Str(b, ref p));
                }
            }
            catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or ArgumentException) { }   // a layout this doesn't know
        }
    }

    /// <summary>Every WAV the sets can play (following event-to-event links).</summary>
    public SortedSet<string> Files(IEnumerable<uint> sets)
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<uint>();
        void Event(uint id, int depth)
        {
            if (depth > 4 || !seen.Add(id) || !Events.TryGetValue(id, out var ev)) return;
            foreach (var (hash, kind) in ev.Parts)
                if (kind == 1) Event(hash, depth + 1);
                else if (Samples.TryGetValue(hash, out var path)) files.Add(path);
        }
        foreach (var s in sets)
            if (Sets.TryGetValue(s, out var set))
                foreach (var e in set.Events) Event(e, 0);
        return files;
    }
}
