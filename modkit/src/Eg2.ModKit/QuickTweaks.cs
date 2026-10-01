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
    /// <summary>A video under the game folder to skip (on/off).</summary>
    public string? Video { get; init; }

    public bool IsIn(ModDefinition m) => Video is not null ? m.SkipVideos.Contains(Video, StringComparer.OrdinalIgnoreCase)
        : Runtime is not null
        ? m.Runtime.Any(r => r.Name.Equals(Runtime.Name, StringComparison.OrdinalIgnoreCase))
        : m.FieldEdits.Any(Same);

    public string? ValueIn(ModDefinition m) => Video is not null ? (IsIn(m) ? "1" : null)
        : Runtime is not null
        ? m.Runtime.FirstOrDefault(r => r.Name.Equals(Runtime.Name, StringComparison.OrdinalIgnoreCase))?.Value
        : m.FieldEdits.FirstOrDefault(Same)?.Value;

    public void Remove(ModDefinition m)
    {
        if (Video is not null) m.SkipVideos.RemoveAll(v => v.Equals(Video, StringComparison.OrdinalIgnoreCase));
        else if (Runtime is not null) m.Runtime.RemoveAll(r => r.Name.Equals(Runtime.Name, StringComparison.OrdinalIgnoreCase));
        else m.FieldEdits.RemoveAll(Same);
        if (Key == "minion_hard_cap")
        {
            m.Runtime.RemoveAll(r => r.Name == CapUi.Name);
            m.TextEdits.RemoveAll(e => e.Key == "MAXIMUM_CAPS");   // the old "300+" workaround
        }
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
        else
        {
            var f = Field!;
            m.FieldEdits.Add(new FieldEdit
            {
                Package = f.Package, Tag = f.Tag, Object = f.Object, Offset = f.Offset, Type = f.Type,
                Expect = f.Expect, Note = f.Note, Value = value,
            });
        }
        if (Key == "minion_hard_cap") { var ui = CapUi.Copy(); ui.Value = value; m.Runtime.Add(ui); }
    }

    bool Same(FieldEdit e) => Field is { } f && e.Package == f.Package && e.Tag == f.Tag && e.Object == f.Object && e.Offset == f.Offset;
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
