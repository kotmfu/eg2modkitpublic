using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// Checks the C# port against reference/golden.json, produced by the Python
/// reference implementation (reference/make_golden.py). No game files needed.
/// </summary>
public static class SelfTest
{
    public static bool Run(Action<string> log)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("golden.json")
            ?? throw new InvalidOperationException("golden.json resource missing");
        using var doc = JsonDocument.Parse(s);
        var g = doc.RootElement;
        bool ok = true;

        void Check(string what, bool pass, string detail = "")
        {
            log($"{(pass ? "PASS" : "FAIL")}  {what}{(detail.Length > 0 ? "  " + detail : "")}");
            ok &= pass;
        }

        foreach (var kv in g.GetProperty("key_hash").EnumerateObject())
        {
            string got = Bytes.Hex(TextTable.KeyHash(kv.Name));
            Check($"key_hash {kv.Name}", got == kv.Value.GetString(), got);
        }

        // from the game: scheme pool 0xa00a2f7d (Recruit Workers, 0xa87d5dd8, Tourism Up; trailing 4) rebuilds exactly,
        // and adding a scheme grows count and size together
        var pool = Convert.FromHexString("0A000000000000007D2F0AA000000000E6BB16320934CBC8020000800020000000030000004" +
                                         "9D3BB6F01000000D85D7DA80100000029E568650100000004000000");
        var sp = SchemePool.TryParse("rspl", pool);
        bool poolOk = sp is not null && sp.Entries.Count == 3 && sp.ToBytes().AsSpan().SequenceEqual(pool);
        if (sp is not null)
        {
            sp.Entries.Add((0x12345678, 1));
            var grown = sp.ToBytes();
            poolOk &= grown.Length == pool.Length + 8 && SchemePool.TryParse("rspl", grown) is { Entries.Count: 4 } poolBack && poolBack.Entries[3].Scheme == 0x12345678
                      && Bytes.U32(grown, grown.Length - 4) == 4;
        }
        Check("scheme pool round-trip + add", poolOk);
        // pool editor: game [A, B, B, C] -> wanted [B, C, D, @N]: remove A, keep one B (all Bs removed, one added back)
        var (pAdd, pRemove) = PoolEdit.Diff(new[] { "A", "B", "B", "C" }, new[] { "B", "C", "D", "@N" });
        var rebuilt = new[] { "A", "B", "B", "C" }.Where(x => !pRemove.Contains(x)).Concat(pAdd).Order().ToList();
        Check("scheme pool edit diff", rebuilt.SequenceEqual(new[] { "@N", "B", "C", "D" }.Order()), $"add {string.Join(",", pAdd)} remove {string.Join(",", pRemove)}");

        // from the game: MARE and furniture_content.ts store this for the Bunk Bed's colour texture
        uint tex = FurnitureArt.TextureHash(@"\graphics\objects\base\barracks\bunks\bunk_tier_1_colour.tga");
        Check("texture_hash bunk_tier_1_colour", tex == 0x8edf29a6, Bytes.Hex(tex));
        // from the game: the Bunk Bed's record stores this as its build-menu icon key
        uint icon = FurnitureArt.GuiKey(@"data\graphics\gui\icons\furniture\bed_01_bunk.tga");
        Check("icon_key bed_01_bunk", icon == 0x9aac99ab && FurnitureArt.IconKey("bed_01_bunk") == icon, Bytes.Hex(icon));
        var mare = Materials.Build(new[] { Bytes.Concat(Bytes.Le(0x1234u, 4u), Bytes.Le(tex)) });
        Check("materials round-trip", Materials.Parse(mare).Records.Count == 1 && Bytes.U32(Materials.Parse(mare).Records[0], 8) == tex);
        // BC7 (checked against Pillow's decoder when written): a 6x5 gradient with alpha survives within a few levels
        var bcPx = new byte[6 * 5 * 4];
        for (int i = 0; i < bcPx.Length; i++) bcPx[i] = (byte)(i % 4 == 3 ? 255 - i : i * 2);
        var bcDds = Bc7.EncodeDds(bcPx, 6, 5);
        var bcBack = Bc7.DecodeDds(bcDds, out int bw, out int bh);
        Check("bc7 round-trip", bcDds.Length == 148 + 4 * 16 && bw == 6 && bh == 5 && bcPx.Zip(bcBack).All(p => Math.Abs(p.First - p.Second) <= 12),
              string.Join(",", bcPx.Zip(bcBack).Select(p => Math.Abs(p.First - p.Second)).Max()));
        // "encode like the original" keeps header, format and length (the full BC7 decoder was checked against Pillow
        // on 240 game textures using all eight modes)
        var like = Dds.EncodeLike(bcDds, bcBack, 6, 5);
        var likeBack = Dds.Decode(like, out _, out _)!;
        Check("dds encode-like", like.Length == bcDds.Length && like.AsSpan(0, 148).SequenceEqual(bcDds.AsSpan(0, 148))
              && bcBack.Zip(likeBack).All(p => Math.Abs(p.First - p.Second) <= 12));

        var inputs = Inputs();
        foreach (var kv in g.GetProperty("zlib4k").EnumerateObject())
        {
            var data = inputs[kv.Name];
            var z = Zlib4k.Compress(data);
            string sha = Sha(z);
            bool roundTrip = Inflate(z).AsSpan().SequenceEqual(data);
            Check($"zlib4k {kv.Name}",
                z.Length == kv.Value.GetProperty("len").GetInt32() && sha == kv.Value.GetProperty("sha256").GetString() && roundTrip,
                $"len={z.Length} roundtrip={roundTrip}");
        }

        foreach (var (name, data) in inputs)
        {
            var z = Zlib4k.CompressSmall(data);
            Check($"zlib4k small {name}", Inflate(z).AsSpan().SequenceEqual(data) && z[0] == 0x48 && z[1] == 0x89, $"{data.Length} -> {z.Length}");
        }

        foreach (var kv in g.GetProperty("zbb").EnumerateObject())
        {
            var data = kv.Name.StartsWith("mixed") ? inputs["mixed"] : inputs["fox"];
            int block = kv.Value.GetProperty("block").GetInt32();
            var z = AsuraArchive.Compress(data, block);
            Check($"zbb {kv.Name}",
                z.Length == kv.Value.GetProperty("len").GetInt32() && Sha(z) == kv.Value.GetProperty("sha256").GetString(),
                $"len={z.Length}");
        }

        // container + text table round trip on a synthetic archive
        var tt = new TextTable { TableName = "SELFTEST" };
        tt.Add("SELFTEST_KEY", "Hello, Genius");
        var arc = new AsuraArchive { Chunks = { new Chunk("HTXT", tt.ToBytes()) } };
        var back = AsuraArchive.FromBytes(arc.ToBytes(compressed: true));
        var tt2 = TextTable.Parse(back.First("HTXT")!.Body);
        Check("archive+HTXT round trip",
            tt2.Get("SELFTEST_KEY")?.Text == "Hello, Genius" && tt2.NameHash == TextTable.KeyHash("SELFTEST")
            && back.First("HTXT")!.Body.AsSpan().SequenceEqual(tt.ToBytes()));

        MapChecks(Check, log);

        log(ok ? "Self-test passed." : "Self-test FAILED.");
        return ok;
    }

    /// <summary>Lair map edits on the installed game's maps (read only; skipped without a game folder): every action,
    /// then the file must read back with the same changes.</summary>
    static void MapChecks(Action<string, bool, string> check, Action<string> log)
    {
        string dir;
        try { dir = Path.Combine(Settings.Load().GamePath, "envs", "basedefinitions"); }
        catch (Exception e) when (e is IOException or JsonException) { dir = ""; }
        if (!Directory.Exists(dir)) { log("SKIP  lair maps (no game folder in Settings)"); return; }
        // island scenery: hide a group and move an instance, then the file must read back with both
        string island = Path.Combine(Path.GetDirectoryName(dir)!, "lair_tropical_03_default.pc");
        if (File.Exists(island))
        {
            var s = IslandScenery.Load(island);
            var g = s.Groups.OrderBy(x => x.Count).First(x => x.Count > 1);
            string hid = ModBuilder.ApplySceneryEdit(s, new SceneryEdit { Group = g.Index, GroupName = g.Name });
            var first = s.Instances.First(i => i.Group != g.Index);
            s.Move(first.Index, 1, 2, 3);
            var back = IslandScenery.FromArchive(Eg2.Asura.AsuraArchive.FromBytes(s.ToBytes()));
            var moved = back.Instances[first.Index];
            check("island scenery: hide a group, move one, read back",
                back.Instances.Where(i => i.Group == g.Index).All(i => back.IsHidden(i.Index)) && !back.IsHidden(first.Index)
                && Math.Abs(moved.X - first.X - 1) < 1e-3 && Math.Abs(moved.Z - first.Z - 3) < 1e-3, hid);
            string? before = IslandScenery.Load(island).TreeProblems();
            int n = back.Instances.Count, gc = back.Groups[first.Group].Count;
            int copy = back.Copy(first.Index, 10, 0, 0);
            for (int k = 1; k < 30; k++) back.Copy(first.Index, 10 + k, 0, k);   // past the free slots: new tree nodes
            var again = IslandScenery.FromArchive(Eg2.Asura.AsuraArchive.FromBytes(back.ToBytes()));
            var c = again.Instances[copy];
            string? after = again.TreeProblems();
            check("island scenery: 30 copies, read back, culling tree sound", before is null && after is null && again.Instances.Count == n + 30
                && again.Groups[first.Group].Count == gc + 30 && c.Group == first.Group && Math.Abs(c.X - moved.X - 10) < 1e-3,
                before ?? after ?? "");
            // mesh: the big rock at the origin exported, raised 5 units, put back; must read back raised with a sound tree
            var isl = IslandScenery.Load(island);
            int rock = isl.Groups.FindIndex(x => x.Name.Contains("rocklarge3") && x.Count == 1);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string obj = isl.GroupObj(rock);
            string raised = string.Join("\n", obj.Split('\n').Select(l => l.StartsWith("v ") && l.Split(' ') is var t
                ? $"v {t[1]} {(float.Parse(t[2], inv) + 5).ToString("R", inv)} {t[3]}" : l));
            int written = isl.SetGroupObj(rock, raised);
            var back2 = IslandScenery.FromArchive(Eg2.Asura.AsuraArchive.FromBytes(isl.ToBytes()));
            float Y(string o, int k) => float.Parse(o.Split('\n').Where(l => l.StartsWith("v ")).ElementAt(k).Split(' ')[2], inv);
            string obj2 = back2.GroupObj(rock);
            check("island scenery: mesh export, raise 5, import, read back", rock >= 0 && written > 0 && back2.TreeProblems() is null
                && Enumerable.Range(0, 20).All(k => Math.Abs(Y(obj2, k * written / 20) - Y(obj, k * written / 20) - 5) < 0.05f), $"{written} vertices of {isl.Groups[Math.Max(0, rock)].Name}");
        }
        foreach (var name in new[] { "lair_tropical_03_default.base", "lair_tropical_01_default.base" })
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            var map = LairMap.Parse(File.ReadAllBytes(path));
            var f = map.Floors.First(x => x.Cells.Any(c => LairMap.TierOf(c) > 0) && x.Cells.Any(c => LairMap.TypeOf(c) == 0x080b0101));
            // a 6x6 block of rock (tier 1-4) somewhere on the lift floor
            (int X, int Y)? Block(int n)
            {
                for (int y = 1; y + n < f.Height; y += 3)
                    for (int x = 1; x + n < f.Width; x += 3)
                        if (Enumerable.Range(0, n * n).All(i => LairMap.TierOf(f.Cells[(y + i / n) * f.Width + x + i % n]) > 0)) return (x, y);
                return null;
            }
            var b = Block(14);
            if (b is not { } at) { check($"lair map {name}: rock to test in", false, ""); continue; }
            int x0 = at.X, y0 = at.Y;
            int objects = map.Objects.Count;
            int dug = map.Dig(f.Index, x0, y0, x0 + 5, y0 + 5);
            int room = map.Dig(f.Index, x0, y0, x0 + 5, y0 + 2, LairMap.RoomType("Barracks"));
            int gold = map.Gold(f.Index, x0 + 7, y0, x0 + 9, y0 + 2);
            int wall = map.Wall(f.Index, x0 + 7, y0 + 4, x0 + 9, y0 + 6);
            int fill = map.Fill(f.Index, x0 + 7, y0 + 4, x0 + 9, y0 + 6, 2);
            var placed = map.Place(map.Objects[^1], f.Index, y0 + 4, x0 + 1);
            map.Remove(map.Objects[0]);
            var back = LairMap.Parse(map.ToBytes());
            var g = back.Floors.First(x => x.Index == f.Index);
            uint T(int x, int y) => LairMap.TypeOf(g.Cells[y * g.Width + x]);
            check($"lair map {name}: dig / room / gold / wall / fill / place / remove read back",
                dug == 36 && room == 18 && gold == 9 && wall == 9 && fill == 9
                && T(x0, y0) == LairMap.RoomType("Barracks") && T(x0 + 5, y0 + 5) == LairMap.Corridor && T(x0 + 8, y0 + 1) == LairMap.Spot
                && LairMap.TierOf(g.Cells[(y0 + 5) * g.Width + x0 + 8]) == 2
                && back.Objects.Count == objects && back.Objects.Any(o => o.Id == placed.Id && o.Row == y0 + 4 && o.Column == x0 + 1),
                $"dug {dug} room {room} gold {gold} wall {wall} fill {fill}, objects {objects} -> {back.Objects.Count}");
            // stamp: the 10x7 just built, pasted into the rock below it, must come out cell for cell with the same objects
            var stamp = MapStamp.Capture(back, f.Index, x0, y0, x0 + 9, y0 + 6, "test", _ => null);
            foreach (var e in stamp.At(name, f.Index, x0, y0 + 7))
                if (e.Action == "place") back.Place(LairMap.FromRecord(Convert.ToUInt32(e.TemplateKey, 16), Convert.FromHexString(e.Template!)), e.Floor, e.Y0, e.X0);
                else ModBuilder.ApplyMapEdit(null!, back, e);   // only "place" needs game data
            var pasted = LairMap.Parse(back.ToBytes()).Floors.First(x => x.Index == f.Index);
            bool same = Enumerable.Range(0, 70).All(i => LairMap.TypeOf(g.Cells[(y0 + i / 10) * g.Width + x0 + i % 10]) is var t
                && LairMap.TypeOf(pasted.Cells[(y0 + 7 + i / 10) * pasted.Width + x0 + i % 10]) == t);   // exact, rock tiers included
            int inStamp = back.Objects.Count(o => o.Floor == f.Index && o.Row >= y0 + 7 && o.Row <= y0 + 13 && o.Column >= x0 && o.Column <= x0 + 9);
            check($"lair map {name}: stamp copy and paste", same && inStamp == stamp.Edits.Count(e => e.Action == "place") && inStamp > 0,
                $"{stamp.Edits.Count} changes, {inStamp} objects");
        }
        // new lair: a renamed copy carries the new stem's id and the same map
        string src = Path.Combine(dir, "lair_tropical_03_default.base");
        if (File.Exists(src))
        {
            var raw = File.ReadAllBytes(src);
            var copy = LairMap.Renamed(raw, "lair_tropical_03_default", "lair_test_01_default");
            uint id = copy is null ? 0 : BitConverter.ToUInt32(Eg2.Asura.AsuraArchive.FromBytes(copy).First("bsnf")!.Body, 17);
            var (a, b) = (LairMap.Parse(raw), copy is null ? null : LairMap.Parse(copy));
            check("new lair: renamed copy", id == Eg2.Asura.Chunks.TextTable.KeyHash("lair_test_01_default") && b is not null
                && a.Floors.Zip(b.Floors).All(p => p.First.Cells.Select(LairMap.TypeOf).SequenceEqual(p.Second.Cells.Select(LairMap.TypeOf))) && a.Objects.Count == b.Objects.Count, Bytes.Hex(id));
        }
        // own lair settings: Caine Key's common.asr entry copied under a new id, naming the new island
        string common = Path.GetFullPath(Path.Combine(dir, "..", "..", "misc", "common.asr"));
        if (File.Exists(common))
        {
            uint from = Eg2.Asura.Chunks.TextTable.KeyHash("lair_tropical_03_default"), to = Eg2.Asura.Chunks.TextTable.KeyHash("lair_test_01_default");
            byte[]? before = null;
            (byte[] Blue, string Island, int Dropped)? got = null;
            foreach (var c in Eg2.Asura.AsuraArchive.Load(common).Chunks.Where(c => c.Tag == "BLUE"))
                if (LairMap.WithLairSettings(c.Body, from, to, "lair_tropical_03_default", "lair_test_01_default") is { } x) (before, got) = (c.Body, x);
            bool ok = false;
            string info = "no entry";
            if (got is { } g)
            {
                var after = g.Blue;
                // the new entry sits where two spares were: [to][0x0D][to][settings class]; nothing before or after it moves
                int at = after.AsSpan().IndexOf(Bytes.Concat(Bytes.Le(to, 0x0Du, to), Bytes.Le(LairMap.LairSettingsClass)));
                int diffFrom = Enumerable.Range(0, after.Length).FirstOrDefault(i => after[i] != before![i], -1);
                int diffTo = Enumerable.Range(0, after.Length).Reverse().FirstOrDefault(i => after[i] != before![i], -1);
                string mine = at < 0 ? "" : Encoding.ASCII.GetString(after, at, diffTo + 1 - at);
                ok = after.Length == before!.Length && after.AsSpan(0, 20).SequenceEqual(before.AsSpan(0, 20)) && at == diffFrom
                    && mine.Contains($@"Lair\{g.Island}\{g.Island}") && !mine.Contains("Tropical_03");
                info = $"island {g.Island}, {g.Dropped} spare entries replaced at {at}..{diffTo}, chunk {after.Length} bytes";
            }
            check("new lair: own settings entry (same chunk size)", ok, info);
        }
    }

    static Dictionary<string, byte[]> Inputs()
    {
        var fox = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 200)));
        var lcg = new byte[5000];
        long x = 12345;
        for (int i = 0; i < lcg.Length; i++)
        {
            x = (x * 1103515245 + 12345) & 0x7FFFFFFF;
            lcg[i] = (byte)(0x41 + ((x >> 16) & 0x0F));
        }
        return new Dictionary<string, byte[]>
        {
            ["empty"] = Array.Empty<byte>(),
            ["one"] = new[] { (byte)'a' },
            ["fox"] = fox,
            ["lcg"] = lcg,
            ["mixed"] = Bytes.Concat(lcg, fox),
        };
    }

    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    static byte[] Inflate(byte[] z)
    {
        using var src = new MemoryStream(z);
        using var zs = new ZLibStream(src, CompressionMode.Decompress);
        using var dst = new MemoryStream();
        zs.CopyTo(dst);
        return dst.ToArray();
    }
}
