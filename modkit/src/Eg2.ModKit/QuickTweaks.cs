using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// The simple settings on the Quick Tweaks page. Each one is stored in an ordinary mod
/// ("quick-tweaks"), as a runtime patch or a field edit, so it builds, conflicts and
/// uninstalls like everything else.
/// </summary>
public sealed class Tweak
{
    public required string Key { get; init; }
    public required string Group { get; init; }
    public required string Label { get; init; }
    public string Help { get; init; } = "";
    /// <summary>Game's own value, shown as a hint; null = unknown.</summary>
    public string? Default { get; init; }
    /// <summary>On/off switch (no number).</summary>
    public bool IsFlag { get; init; }
    public decimal Min { get; init; }
    public decimal Max { get; init; } = 10_000_000;
    /// <summary>Decimal places the value takes (f32 values); 0 = whole numbers.</summary>
    public int Decimals { get; init; }

    // exactly one of these
    public RuntimePatch? Runtime { get; init; }
    public FieldEdit? Field { get; init; }
    /// <summary>Fixed field edits switched on and off together (values preset).</summary>
    public List<FieldEdit>? Fields { get; init; }
    /// <summary>With <see cref="Fields"/> holding the game's own i32 values: a number that moves them all by
    /// (value - <see cref="Default"/>), e.g. a limit written as both "&lt; 5" and "== 4" in the scripts.</summary>
    public bool Shift { get; init; }
    /// <summary>A video under the game folder to skip (on/off).</summary>
    public string? Video { get; init; }

    public bool IsIn(ModDefinition m) => Video is not null ? m.SkipVideos.Contains(Video, StringComparer.OrdinalIgnoreCase)
        : Fields is not null ? m.FieldEdits.Any(Shift ? InSlots : InFields)
        : Runtime is not null
        ? m.Runtime.Any(r => r.Name.Equals(Runtime.Name, StringComparison.OrdinalIgnoreCase))
        : m.FieldEdits.Any(Same);

    public string? ValueIn(ModDefinition m) => Shift
        ? Fields!.Select(f => (f, e: m.FieldEdits.FirstOrDefault(e => SameSlot(e, f)))).FirstOrDefault(x => x.e is not null) is ({ } f0, { } e0)
            ? (int.Parse(e0.Value) - int.Parse(f0.Value) + int.Parse(Default!)).ToString() : null
        : Video is not null || Fields is not null ? (IsIn(m) ? "1" : null)
        : Runtime is not null
        ? m.Runtime.FirstOrDefault(r => r.Name.Equals(Runtime.Name, StringComparison.OrdinalIgnoreCase))?.Value
        : m.FieldEdits.FirstOrDefault(Same)?.Value;

    public void Remove(ModDefinition m)
    {
        if (Video is not null) m.SkipVideos.RemoveAll(v => v.Equals(Video, StringComparison.OrdinalIgnoreCase));
        else if (Runtime is not null) m.Runtime.RemoveAll(r => r.Name.Equals(Runtime.Name, StringComparison.OrdinalIgnoreCase));
        else if (Fields is not null) m.FieldEdits.RemoveAll(Shift ? InSlots : InFields);
        else m.FieldEdits.RemoveAll(Same);
        if (Key == "minion_hard_cap")
        {
            m.Runtime.RemoveAll(r => r.Name == CapUi.Name);
            m.TextEdits.RemoveAll(e => e.Key == "MAXIMUM_CAPS");   // the old "300+" workaround
        }
        if (Key == "henchman_limit") m.HenchmanBarSlots = null;
    }

    /// <summary>The HUD's copy of the hard cap: the game copies it once at startup (`mov eax,[hardcap]` /
    /// `mov [copy],eax`, before the runtime DLL has raised it) and shows "Max" beside the minion count once the count
    /// reaches the copy. Written with the same value so "Max" means the real cap. Only other use: the UI registers it.</summary>
    static readonly RuntimePatch CapUi = new()
    {
        Name = "minion_hard_cap_ui", Type = "u32",
        Pattern = "89 B7 78 19 00 00 8B 05 ?? ?? ?? ?? 89 05 ?? ?? ?? ?? 89 35 ?? ?? ?? ?? 89 35", At = 12, Rel = 2, Len = 6,
        Note = "The HUD's copy of the minion hard cap (its \"Max\" label); follows the hard cap.",
    };

    public void Set(ModDefinition m, string value)
    {
        Remove(m);
        if (Video is not null) m.SkipVideos.Add(Video);
        else if (Runtime is not null) { var r = Runtime.Copy(); r.Value = value; m.Runtime.Add(r); }
        else if (Fields is not null)
        {
            m.FieldEdits.RemoveAll(InSlots);   // manual values on the same slots give way
            int by = Shift ? int.Parse(value) - int.Parse(Default!) : 0;
            m.FieldEdits.AddRange(Fields.Select(f => Copy(f, Shift ? (int.Parse(f.Value) + by).ToString() : f.Value)));
        }
        else m.FieldEdits.Add(Copy(Field!, value));
        if (Key == "minion_hard_cap") { var ui = CapUi.Copy(); ui.Value = value; m.Runtime.Add(ui); }
        // the HUD bar gets a space per henchman plus the genius
        if (Key == "henchman_limit" && int.Parse(value) + 1 > HenchmanBar.GameSlots) m.HenchmanBarSlots = int.Parse(value) + 1;
    }

    static FieldEdit Copy(FieldEdit f, string value) => new()
    {
        Package = f.Package, Tag = f.Tag, Object = f.Object, Offset = f.Offset, Type = f.Type,
        Expect = f.Expect, Note = f.Note, Value = value,
    };

    static bool SameSlot(FieldEdit e, FieldEdit f) => e.Package == f.Package && e.Tag == f.Tag && e.Object == f.Object && e.Offset == f.Offset;
    bool Same(FieldEdit e) => Field is { } f && SameSlot(e, f);
    /// <summary>One of this switch's own edits (same slot and preset value), so switching off keeps manual values there.</summary>
    bool InFields(FieldEdit e) => Fields!.Any(f => SameSlot(e, f) && e.Value == f.Value);
    bool InSlots(FieldEdit e) => Fields!.Any(f => SameSlot(e, f));
}

public static class QuickTweaks
{
    public const string ModId = "quick-tweaks";

    static readonly Dictionary<string, (string Group, string Label, string Help, string? Default, bool Flag)> Info = new()
    {
        ["minion_hard_cap"] = ("Minions", "Minion hard cap", "The absolute limit, whatever lockers and research give.", "300", false),
        ["minion_base_capacity"] = ("Minions", "Starting minion capacity", "Capacity before lockers and research.", "20", false),
        ["gold_capacity"] = ("Gold", "Starting gold capacity", "Gold you can hold before building vaults (vaults add to it); earnings above the cap are lost.", "40000", false),
        ["unlimited_gold"] = ("Gold", "Unlimited gold", "Developer switch: spending no longer takes gold away (it doesn't add any).", "off", true),
        ["instant_minion_training"] = ("Developer switches", "Instant minion training", "", "off", true),
        ["freeze_minion_stats"] = ("Developer switches", "Freeze minion stats", "", "off", true),
        ["unlock_all"] = ("Developer switches", "Unlock everything (experimental)", "Untested; use a throwaway save.", "off", true),
        ["default_base_power"] = ("Developer switches", "Base power", "Read from the game: 1000000 (probably thousandths, so 1000 power). Lower values mean less power.", "1000000", false),
        ["minimum_power"] = ("Developer switches", "Minimum power", "Read from the game: -9999.", "-9999", false),
        ["salary_rate"] = ("Gold", "Salary rate (experimental)", "What payday multiplies your minions' salaries by, from the difficulty preset. Untested: the game's own value shows in bin\\eg2modkit.log as \"[salary_rate] found ...\" (if it isn't 1, it's a per-minion amount; set yours relative to it). 0 = no salaries.", "1", false),
        ["intel_cap_save"] = ("Intel and Tech", "Intel cap in existing saves", "Rewrites the cap stored in whatever save you load (runs with the game).", "99", false),
        ["tech_cap_save"] = ("Intel and Tech", "Tech cap in existing saves", "Rewrites the cap stored in whatever save you load (runs with the game).", "99", false),
    };

    /// <summary>All tweaks; research-based ones need game data (pass null before it has loaded).</summary>
    public static List<Tweak> All(GameData? game)
    {
        var list = new List<Tweak>();
        foreach (var p in RuntimePresets.All)
        {
            var (group, label, help, def, flag) = Info.TryGetValue(p.Name, out var i) ? i : ("Other", p.Name, p.Note ?? "", null, false);
            list.Add(new Tweak { Key = p.Name, Group = group, Label = label, Help = help, Default = def, IsFlag = flag, Runtime = p, Min = p.Name == "minimum_power" ? -10_000_000 : 0,
                                 Decimals = p.Type.StartsWith('f') ? 2 : 0, Max = p.Type.StartsWith('f') ? 100 : 10_000_000 });
        }
        list.Add(new Tweak { Key = "skip_intro", Group = "Startup", Label = "Skip the Rebellion intro video", Default = "off", IsFlag = true, Video = "fmv/rebellion.webm",
                             Help = "The studio logo video at launch (runs with the game)." });
        if (game is null) return list;

        // "Minion Cap Increase 1..5" research: the Increase node's Value (10/15/20/25/30 in the base game)
        foreach (var o in game.Objects.Where(o => o.Tag == "rtrp" && o.Name.Contains("(Minion Cap Increase")).OrderBy(o => o.Name[o.Name.IndexOf("(Minion", StringComparison.Ordinal)..]))
        {
            var p = ObjectInspector.Params(o).FirstOrDefault(p => p.Label.StartsWith("Increase") && p.Type == "i32");
            if (p is null) continue;
            list.Add(new Tweak
            {
                Key = $"research:{o.Key}", Group = "Minions", Label = $"Research \"{o.Name}\" bonus",
                Help = "Capacity gained when this research completes.", Default = p.Value, Max = 100_000,
                Field = new FieldEdit
                {
                    Package = o.Package, Tag = o.Tag, Object = o.Key, Offset = p.Offset, Type = p.Type,
                    Expect = Convert.ToHexString(o.Body, p.Offset, 4), Note = $"{o.Name}: minion capacity bonus",
                },
            });
        }

        // Intel/Tech caps: rcns resource objects keep their maximum as a u32 at +41 (99 for both; Henchman 10;
        // Gold 0 = vault capacity instead). Found from the user's CE table (current, max adjacent in memory).
        foreach (var (name, label) in new[] { ("\"Intel\"", "Intel cap"), ("\"Tech\"", "Tech cap") })
            if (game.Objects.FirstOrDefault(o => o.Tag == "rcns" && o.Name == name && o.Body.Length > 45) is { } res)
                list.Add(FieldTweak(res, "Intel and Tech", label, 41, "u32", Bytes.U32(res.Body, 41).ToString(),
                    "Most you can hold, for NEW games (a save keeps the cap it started with; use the \"existing saves\" setting for those)."));
        if (NoTemperature(game) is { } temp) list.Add(temp);
        if (HenchmanLimit(game) is { } hench) list.Add(hench);
        return list;
    }

    /// <summary>
    /// Oceans DLC lair temperature off. Temperature bands are rtlv objects: i32 min at +35, max at +39 (Freezing -128..-13,
    /// Cold, Chilly, Neutral 0..0, Warm, Hot, Melting ..127; +43 f32 1/1.5/3 = likely the furniture wear multiplier). Every
    /// temperature trait (stat drain, slow, agent immunity, "Comfortable") is an rtrt with "Enter/Exit a X Tile" parts
    /// linking a band. Neutral is widened to every value and the other bands get min &gt; max, so no tile leaves Neutral;
    /// the one trait that fires on Neutral ("Comfortable") is pointed at a dead band instead.
    /// </summary>
    static Tweak? NoTemperature(GameData game)
    {
        var bands = game.Objects.Where(o => o.Tag == "rtlv" && o.Body.Length >= 47).ToList();
        var neutral = bands.FirstOrDefault(o => BitConverter.ToInt32(o.Body, 35) == 0 && BitConverter.ToInt32(o.Body, 39) == 0);
        var dead = bands.FirstOrDefault(o => o != neutral);
        if (neutral is null || dead is null) return null;   // no Oceans DLC, or the layout moved

        var fields = new List<FieldEdit>();
        void Edit(GameObject o, int at, string type, string value, string note) => fields.Add(new FieldEdit
        {
            Package = o.Package, Tag = o.Tag, Object = o.Key, Offset = at, Type = type, Value = value,
            Expect = Convert.ToHexString(o.Body, at, 4), Note = note,
        });
        foreach (var b in bands)
        {
            bool n = b == neutral;
            Edit(b, 35, "i32", n ? "-128" : "127", $"temperature off: {b.Name} band min");
            Edit(b, 39, "i32", n ? "127" : "-128", $"temperature off: {b.Name} band max");
        }
        foreach (var t in game.Objects.Where(o => o.Tag == "rtrt"))
            for (int i = 0; i + 4 <= t.Body.Length; i++)
                if (Bytes.U32(t.Body, i) == neutral.ObjectId)
                    Edit(t, i, "u32", Bytes.Hex(dead.ObjectId), $"temperature off: {t.Name} no longer fires on Neutral");
        // the sources too, so every tile reads 0 (not just "neutral"): furniture output and story-wide offsets
        foreach (var f in game.Objects.Where(o => o.Tag == "fntr"))
            foreach (var at in TemperatureOutputs(f.Body).Where(at => BitConverter.ToInt32(f.Body, at) != 0))
                Edit(f, at, "i32", "0", $"temperature off: {f.Name} gives off no heat/cold");
        foreach (var (o, at) in TemperatureOffsets(game))
            Edit(o, at, "i32", "0", $"temperature off: {o.Name} no longer shifts the lair's temperature");
        return new Tweak
        {
            Key = "no_temperature", Group = "Lair", Label = "Turn off temperature", IsFlag = true, Default = "off", Fields = fields,
            Help = "Every tile on every island stays at 0: furniture gives off no heat or cold, the Polar story no longer chills the lair, and minions, agents and furniture get no Cold/Hot/Freezing/Melting effects or \"Comfortable\" bonus.",
        };
    }

    /// <summary>Script node type "{MinionType} InLair": counts minions in the lair. Game scripts use it only for henchmen.</summary>
    const uint HenchmenInLair = 0x10e28e6e;

    /// <summary>
    /// Where the scripts cap henchmen: every mission whose condition counts henchmen in the lair (node type
    /// <see cref="HenchmenInLair"/>) against a number. The number is the next i32 setting after the node type:
    /// 5 = "fewer than 5 henchmen" (recruit missions vanish at 5), 4 = "not at 4 with a recruit already on the way"
    /// (crime-lord story starts). Not "Has5Henchmen" (an optional objective for HAVING 5), minion-swap steps or loot.
    /// </summary>
    static IEnumerable<(GameObject Obj, ObjectInspector.Param P)> HenchmanChecks(GameData game)
    {
        var bp = BitConverter.GetBytes(HenchmenInLair);
        foreach (var o in game.Objects.Where(o => o.Tag == "robj" && o.Name != "Has5Henchmen" && o.Body.AsSpan().IndexOf(bp) >= 0))
        {
            var ps = ObjectInspector.Params(o);
            for (int i = 0; i + 4 <= o.Body.Length; i++)
                if (Bytes.U32(o.Body, i) == HenchmenInLair
                    && ps.Where(p => p.Offset > i).MinBy(p => p.Offset) is { Type: "i32", Value: "4" or "5" } p && p.Offset - i <= 700)
                    yield return (o, p);
        }
    }

    /// <summary>The "Henchman" resources (rcns): their cap at +41 is 10, the same slot as the Intel/Tech caps.</summary>
    static IEnumerable<GameObject> HenchmanResources(GameData game) =>
        game.Objects.Where(o => o.Tag == "rcns" && o.Name == "\"Henchman\"" && o.Body.Length > 45);

    static Tweak? HenchmanLimit(GameData game)
    {
        var fields = HenchmanChecks(game).Select(x => new FieldEdit
        {
            Package = x.Obj.Package, Tag = x.Obj.Tag, Object = x.Obj.Key, Offset = x.P.Offset, Type = "i32", Value = x.P.Value,
            Expect = Convert.ToHexString(x.Obj.Body, x.P.Offset, 4), Note = $"henchman limit: {x.Obj.Name}",
        }).Concat(HenchmanResources(game).Select(o => new FieldEdit
        {
            Package = o.Package, Tag = o.Tag, Object = o.Key, Offset = 41, Type = "i32", Value = Bytes.U32(o.Body, 41).ToString(),
            Expect = Convert.ToHexString(o.Body, 41, 4), Note = "henchman limit: Henchman resource cap",
        })).ToList();
        if (fields.Count == 0) return null;
        return new Tweak
        {
            Key = "henchman_limit", Group = "Henchmen", Label = "Henchman limit", Default = "5", Min = 1, Max = 99, Shift = true, Fields = fields,
            Help = "How many henchmen you can hire. Recruit missions and crime-lord stories stop appearing once you have this many. The HUD bar gets a space for each.",
        };
    }

    /// <summary>The henchman limit by hand: each mission's check, and the Henchman resource caps.</summary>
    public static List<Tweak> Henchmen(GameData game)
    {
        var list = HenchmanChecks(game).OrderBy(x => x.Obj.Name, StringComparer.OrdinalIgnoreCase).Select(x => FieldTweak(x.Obj, $"Mission: {x.Obj.Name}",
            x.P.Value == "5" ? "Henchman limit" : "Henchman limit - 1 (a recruit already on the way)", x.P.Offset, "i32", x.P.Value,
            x.P.Value == "5" ? "The mission only appears with fewer henchmen than this." : "Should stay one below this mission's henchman limit.")).ToList();
        foreach (var o in HenchmanResources(game))
            list.Add(FieldTweak(o, $"Resource: Henchman ({o.Package})", "Cap (unconfirmed)", 41, "i32", Bytes.U32(o.Body, 41).ToString(),
                "The Henchman resource's maximum, like the Intel/Tech caps. Keep it at or above the henchman limit."));
        return list;
    }

    /// <summary>
    /// Offsets of a furniture record's temperature outputs. The list is a prop [u32 n] + n entries, each a prop whose
    /// first i32 is the output: 24 bytes ([v][0][1][2][1][2], e.g. Generator 4) or 40 (player-picked High/Medium/Low:
    /// [v][band key][2 texts], e.g. Furnace 8/6/4). It always ends at 00000000 00000080 3F010001.
    /// </summary>
    public static List<int> TemperatureOutputs(byte[] b)
    {
        var tail = Bytes.Le(1u, 2u, 1u, 2u);
        for (int i = 0; i + 13 <= b.Length; i++)
        {
            if (Bytes.U32(b, i) != 0x80000001 || b[i + 4] != 0) continue;
            int size = (int)Bytes.U32(b, i + 5), end = i + 9 + size;
            uint n = Bytes.U32(b, i + 9);
            if (n is 0 or > 8 || end + 9 > b.Length || Bytes.U32(b, end) != 0 || Bytes.U32(b, end + 4) != 0x80000000 || b[end + 8] != 0x3f) continue;
            var outs = new List<int>();
            for (int e = i + 13; e < end && outs.Count < n; )
            {
                if (Bytes.U32(b, e) != 0x80000001 || b[e + 4] != 0) break;
                int es = (int)Bytes.U32(b, e + 5);
                if (es is not (24 or 40) || es == 24 && !b.AsSpan(e + 9 + 8, 16).SequenceEqual(tail)) break;
                outs.Add(e + 9);
                e += 9 + es;
            }
            if (outs.Count == n && outs[^1] - 9 + Bytes.U32(b, outs[^1] - 4) + 9 == end) return outs;
        }
        return new List<int>();
    }

    /// <summary>Story steps that shift the whole lair's temperature (Polar campaign; node 0xc3376493, its Offset fed by one i32 Value).</summary>
    static IEnumerable<(GameObject Obj, int At)> TemperatureOffsets(GameData game)
    {
        foreach (var o in game.Objects.Where(o => o.Body.AsSpan().IndexOf(BitConverter.GetBytes(0xc3376493u)) >= 0))
            if (ObjectInspector.Params(o).Where(p => p.Label == "Value" && p.Type == "i32").ToList() is [var p])
                yield return (o, p.Offset);
    }

    /// <summary>Temperature by hand: bands (range, wear multiplier), furniture outputs, story offsets and the numbers in every trait a band triggers.</summary>
    public static List<Tweak> Temperature(GameData game)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var bands = game.Objects.Where(o => o.Tag == "rtlv" && o.Body.Length >= 47).OrderBy(o => BitConverter.ToInt32(o.Body, 35)).ToList();
        string BandName(GameObject b) => b.Name.StartsWith("0x") ? "Neutral" : b.Name.Trim('"');
        var list = new List<Tweak>();
        foreach (var b in bands)
        {
            string item = $"Band: {BandName(b)}";
            list.Add(FieldTweak(b, item, "Lowest temperature", 35, "i32", BitConverter.ToInt32(b.Body, 35).ToString(),
                "A tile whose temperature is in this band's range gets its effects. Bands run -128..127 and shouldn't overlap."));
            list.Add(FieldTweak(b, item, "Highest temperature", 39, "i32", BitConverter.ToInt32(b.Body, 39).ToString(),
                "A tile whose temperature is in this band's range gets its effects. Bands run -128..127 and shouldn't overlap."));
            list.Add(FieldTweak(b, item, "Furniture wear multiplier (unconfirmed)", 43, "f32", BitConverter.ToSingle(b.Body, 43).ToString("G6", inv),
                "1 in the mild bands, 1.5 Cold/Hot, 3 Freezing/Melting; probably how much faster furniture wears out there."));
        }
        foreach (var f in game.Objects.Where(o => o.Tag == "fntr").OrderBy(o => Furniture(o.Name), StringComparer.OrdinalIgnoreCase))
        {
            var outs = TemperatureOutputs(f.Body);
            for (int k = 0; k < outs.Count; k++)
                list.Add(FieldTweak(f, $"Furniture: {Furniture(f.Name)}", outs.Count > 1 ? $"Output setting {k + 1} (High, Medium, Low)" : "Temperature output",
                    outs[k], "i32", BitConverter.ToInt32(f.Body, outs[k]).ToString(), "Heat (+) or cold (-) this item gives off around it. 0 = none."));
        }
        foreach (var (o, at) in TemperatureOffsets(game))
            list.Add(FieldTweak(o, $"Story: {o.Name}", "Lair temperature change", at, "i32", BitConverter.ToInt32(o.Body, at).ToString(),
                "How much this story step shifts the whole lair's temperature (Polar campaign). 0 = none."));
        var ids = bands.ToDictionary(b => b.ObjectId, BandName);
        foreach (var t in game.Objects.Where(o => o.Tag == "rtrt"))
        {
            var used = new SortedSet<string>();
            for (int i = 0; i + 4 <= t.Body.Length; i++) if (ids.TryGetValue(Bytes.U32(t.Body, i), out var n)) used.Add(n);
            if (used.Count == 0) continue;
            string item = $"Trait: {t.Name} ({string.Join(", ", used)}) {t.Key}";
            foreach (var p in ObjectInspector.TraitValues(t).Where(p => !p.Label.StartsWith("Enter ") && !p.Label.StartsWith("Exit ")))
                list.Add(FieldTweak(t, item, p.Label, p.Offset, p.Type, p.Value,
                    "A number in the trait a minion or agent gets on these bands' tiles, named after the trait part it sits in. What each one does is unconfirmed."));
        }
        return list;
    }

    static Tweak FieldTweak(GameObject o, string group, string label, int offset, string type, string value, string help = "") => new()
    {
        Key = $"{o.Package}:{o.Tag}:{o.Key}:{offset}", Group = group, Label = label, Help = help, Default = value,
        Min = type == "u32" ? 0 : -10_000_000,
        Field = new FieldEdit
        {
            Package = o.Package, Tag = o.Tag, Object = o.Key, Offset = offset, Type = type,
            Expect = Convert.ToHexString(o.Body, offset, type == "u8" ? 1 : 4), Note = $"{group}: {label}",
        },
    };

    /// <summary>Every research project: time, costs and its flowgraph values (e.g. the minion cap bonus).</summary>
    public static List<Tweak> Research(GameData game)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var list = new List<Tweak>();
        var named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in game.Objects.Where(o => o.Tag == "rtrp").OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            int k = named[o.Name] = named.GetValueOrDefault(o.Name) + 1;
            string item = k > 1 ? $"{o.Name} #{k}" : o.Name;   // "Research H.A.V.O.C." x3
            if (ObjectInspector.ResearchTimeOffset(o) is var t and >= 0)
                list.Add(FieldTweak(o, item, "Research time", t, "f32", BitConverter.ToSingle(o.Body, t).ToString("G6", inv),
                    "How long it takes, in the game's own time units (lower = faster)."));
            foreach (var c in ObjectInspector.Costs(o, game))
                list.Add(FieldTweak(o, item, $"Cost ({c.Resource})", c.Offset, "u32", c.Amount.ToString(), $"{c.Resource} needed to research it."));
            var seen = new Dictionary<string, int>();
            foreach (var p in ObjectInspector.Params(o))
            {
                int n = seen[p.Label] = seen.GetValueOrDefault(p.Label) + 1;   // two "Increase" nodes -> "Increase → Value #2"
                string label = "Reward: " + p.Label.Replace(" → Value", "");
                list.Add(FieldTweak(o, item, n > 1 ? $"{label} #{n}" : label, p.Offset, p.Type, p.Value,
                    "A number in the script that runs when the research completes, named after the script step it feeds (e.g. Increase = amount added)."
                    + (p.Type == "u8" ? " 0 = off, 1 = on." : "")));
            }
        }
        return list;
    }

    /// <summary>
    /// Every furniture item's jobs and their Smarts/Vitality/Morale rates, grouped by item. A job used by
    /// several items appears under each (same <see cref="Tweak.Key"/>, so an edit applies to all of them).
    /// </summary>
    public static List<Tweak> FurnitureEffects(GameData game)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var jobs = game.Objects.Where(o => o.Tag == "rjob").ToList();   // one id ("Listening") exists in two genius packages
        var ids = jobs.Select(j => j.ObjectId).ToHashSet();
        var itemJobs = new SortedDictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in game.Objects.Where(o => o.IsRecord))
            for (int i = 0; i + 4 <= f.Body.Length; i++)
                if (ids.Contains(Bytes.U32(f.Body, i)))
                {
                    var name = Furniture(f.Name);
                    (itemJobs.TryGetValue(name, out var set) ? set : itemJobs[name] = new()).Add(Bytes.U32(f.Body, i));
                }
        var users = new Dictionary<uint, List<string>>();
        foreach (var (item, set) in itemJobs)
            foreach (var id in set) (users.TryGetValue(id, out var l) ? l : users[id] = new()).Add(item);

        var list = new List<Tweak>();
        foreach (var (item, set) in itemJobs)
            foreach (var j in jobs.Where(j => set.Contains(j.ObjectId)).OrderBy(j => j.Name))
            {
                string job = j.Name.Trim('"');
                var others = users[j.ObjectId].Where(u => u != item).ToList();
                string help = others.Count == 0 ? "" : $"\"{job}\" is shared with {string.Join(", ", others)}; changing it changes those too.";
                foreach (var e in ObjectInspector.JobEffects(j))
                    list.Add(FieldTweak(j, item, set.Count > 1 ? $"{job}: {e.Stat}" : e.Stat, e.Offset, "f32", e.Rate.ToString("G6", inv), help));
            }
        return list;
    }

    // "Bunk_Bed_01  (Bunk Bed)" -> "Bunk Bed"
    static string Furniture(string display) => display.IndexOf("  (", StringComparison.Ordinal) is var i and >= 0 ? display[(i + 3)..^1] : display;
}
