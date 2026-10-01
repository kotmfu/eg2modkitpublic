using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// One browsable thing: a data object (lowercase chunk) or a furniture record inside an fntr table.
/// Identity inside a package is (Tag, Key): the record name for furniture, the object id otherwise.
/// </summary>
public sealed class GameObject
{
    public string Name { get; set; } = "";
    public string Tag { get; init; } = "";
    public string Package { get; init; } = "";
    [Browsable(false)] public uint ObjectId { get; init; }
    [Browsable(false)] public string? RecordName { get; init; }
    [Browsable(false)] public byte[] Body { get; init; } = Array.Empty<byte>();
    [Browsable(false)] public string Key => RecordName ?? Bytes.Hex(ObjectId);
    [Browsable(false)] public bool IsRecord => RecordName is not null;
    /// <summary>Set when this is a mod's new object (a copy being edited), not base game data.</summary>
    [Browsable(false)] public NewObject? Draft { get; init; }
    [Browsable(false)] public ModDefinition? DraftMod { get; init; }
    public override string ToString() => $"{Tag} {Name}";
}

public sealed class AttrRow
{
    public string Path { get; init; } = "";
    public int Offset { get; init; }
    public string Hex { get; init; } = "";
    public string Int { get; init; } = "";
    public string Float { get; init; } = "";
    public string Meaning { get; init; } = "";
    [Browsable(false)] public int Size { get; init; }
    [Browsable(false)] public bool Editable { get; init; }
    /// <summary>A named, changeable number (price, stat, cost, script setting): shown bold.</summary>
    [Browsable(false)] public bool IsSetting { get; init; }
    /// <summary>Object this value points at, if it resolves to one (for navigation).</summary>
    [Browsable(false)] public GameObject? Target { get; init; }
}

/// <summary>
/// Best-effort decoding of an object's bytes into rows: property headers, typed references
/// ([1][0][type][id]), strings, and 4-byte words shown as int/float with hashes resolved to
/// text or objects. Structure comes from the lossless prop walker; semantics are guesses, so
/// every value row also carries its raw offset for a <see cref="FieldEdit"/>.
/// </summary>
public static class ObjectInspector
{
    static readonly Regex Printable = new("^[\\x20-\\x7e]{3,}$");
    static readonly Regex NameLike = new("^[A-Za-z{][A-Za-z0-9_ {}()\\\\/.:'-]{4,}$");
    static readonly Regex NodeName = new("^[A-Z][A-Za-z0-9 _]{2,}$");
    static readonly Regex Markup =new("[\\u0000-\\u001f\\ue000-\\uf8ff]");

    /// <summary>Text without the engine's inline markup codes, for display.</summary>
    public static string Clean(string s) => Markup.Replace(s, "");

    public static string DisplayName(GameObject o, GameData g)
    {
        if (o.IsRecord)
        {
            var r = new FurnitureRecord(new Prop(0, 0, PropStream.Parse(o.Body)));
            return r.HasTextRef(0) && g.Text.TryGetValue(r.TextHash(0), out var t) ? $"{o.RecordName}  ({Clean(t.Text)})" : o.RecordName!;
        }
        int start = Math.Min(ObjectHeader.Size, o.Body.Length);
        // research and engineering items: the in-game name (text ref at +33); the internal string is often a template like "Generic_Research_Blank"
        string? shown = o.Tag is "rtrp" or "rctr" && o.Body.Length >= 49 && IsTextRef(o.Body, 33) && g.Text.TryGetValue(Bytes.U32(o.Body, 45), out var rt) ? Clean(rt.Text) : null;
        // objectives: title text ref at +37 (key +49); the internal string is usually a condition name like "CanTutorialLitePlay"
        if (o.Tag == "robj" && o.Body.Length >= 53 && IsTextRef(o.Body, 37) && g.Text.TryGetValue(Bytes.U32(o.Body, 49), out var ot)) shown = Clean(ot.Text);
        var m = Regex.Match(Bytes.Latin1.GetString(o.Body, start, o.Body.Length - start), "[A-Za-z][A-Za-z0-9_ \\-]{3,}(?=\\0)");
        if (shown is not null) return m.Success ? $"{shown}  ({m.Value})" : shown;
        if (m.Success) return m.Value;
        for (int p = start; p + 16 <= o.Body.Length; p++)
            if (IsTextRef(o.Body, p) && g.Text.TryGetValue(Bytes.U32(o.Body, p + 12), out var t)) return $"\"{Clean(t.Text)}\"";
        return Bytes.Hex(o.ObjectId);
    }

    // any ref type: jobs name their activity with 0x5d3bbb29, research with 0x9fbd7b34, furniture with TextRefType
    static bool IsTextRef(byte[] b, int p) =>
        Bytes.U32(b, p) == 1 && Bytes.U32(b, p + 4) == 0 && Bytes.U32(b, p + 8) > 0xFFFF;

    /// <summary>What an object type is: List = category name, One = singular (for link labels), Help = what the player edits there.</summary>
    public sealed record TypeInfo(string List, string One, string Help);

    public static readonly Dictionary<string, TypeInfo> Types = new()
    {
        ["fntr"] = new("Furniture", "Furniture", "A buildable item: price, name/description and the jobs minions do at it. Its look comes from the linked furniture art."),
        ["rtrp"] = new("Research", "Research", "One research project: name, description, time and costs. Research trees list which projects appear and where."),
        ["rscm"] = new("Scheme logic & rewards", "Scheme logic", "What a scheme does when run: its script, requirements and rewards. \"Script setting\" rows are numbers inside that script (reward amounts, durations, counts). The world-map name/description is in the matching Scheme."),
        ["rsdv"] = new("Schemes (world map names)", "Scheme", "How a scheme appears on the world map: up to 3 title/description variants, and the tags that decide which regions it can appear in."),
        ["robj"] = new("Objectives", "Objective", "A story or side objective: title, description, task list, tracker text (the objective panel) and completion message. Links to its steps."),
        ["room"] = new("Objective steps", "Objective step", "One step of an objective, usually a script that runs when the step starts."),
        ["rant"] = new("Story chains", "Story chain", "An antagonist/super agent storyline: names, bios and the status lines shown as they idle, stake out and investigate."),
        ["rjob"] = new("Jobs (what minions do at furniture)", "Job", "Something a minion does at furniture. Stat change per tick while doing it: + restores, - drains. Jobs are shared by every furniture item that links them."),
        ["rcns"] = new("Resources", "Resource", "A resource or counter (Gold, Intel, Tech, Heat…) and its cap."),
        ["rtag"] = new("Tags", "Tag", "A label other things use to find each other, e.g. which schemes can appear in which regions. The game doesn't ship tag names, so each is named after what uses it."),
        ["fnas"] = new("Furniture art", "Furniture art", "Which model and material a furniture item uses."),
        ["rtrt"] = new("Traits & powers", "Trait/power", "A trait or power (geniuses, henchmen, agents): name and description. Its effect lives in its script."),
        ["fegd"] = new("Geniuses", "Genius", "A genius on the new-game screen: full name, bio, occupation, specialisation and tagline."),
        ["felr"] = new("Lairs (islands)", "Lair", "A lair island on the new-game screen: points of interest, photo captions, ocean/continent and the default lair name."),
        ["rmlr"] = new("World map regions", "Region", "A world-map region: name, description, its heat upgrades and the tags that decide which schemes appear there."),
        ["rsbs"] = new("Super agencies", "Super agency", "A Force of Justice: name, description, agents, threat levels and the loot you can steal from it."),
        ["rttr"] = new("Research trees", "Research tree", "A research tree: the research projects in it."),
        ["rtsc"] = new("Characters & targets", "Character", "A character, target or voice set: names and spoken lines."),
        ["rctt"] = new("Engineering trees", "Engineering tree", "Where engineering and crafting items sit and what needs what (edit them on the Trees tab), plus the tree's background picture."),
        ["rctr"] = new("Engineering & crafting items", "Engineering item", "An engineering or crafting project (oceans): name, description, costs, icon and what it rewards. Engineering trees place them (Trees tab)."),
        ["rrtl"] = new("Region upgrades", "Region upgrade", "A region upgrade (heat / infrastructure)."),
        ["rdfl"] = new("Doomsday levels", "Doomsday level", "A doomsday device stage."),
        ["mtex"] = new("Minion specialisations", "Minion specialisation", "Minion specialisation groups shown in the minion menu."),
    };

    public static string TypeHelp(string tag) => Types.TryGetValue(tag, out var t) ? t.Help
        : $"Type \"{tag}\" is not decoded yet (on the to-do list). Names, texts and links below are found automatically.";

    static readonly (Regex Word, string Label)[] TextKinds =
    {
        (new("TITLE\\d*$"), "Title"), (new("^(DES|DESC|DESCRIPTION)\\d*$|DESC$"), "Description"), (new("NAME\\d*$"), "Name"),
        (new("^BIO\\d*$"), "Biography"), (new("^TAGLINE$"), "Tagline"), (new("^OCCUPATION$"), "Occupation"), (new("^SPECIALISATION$"), "Specialisation"),
        (new("^TRACKER(TEXT)?$"), "Tracker text (objective panel)"), (new("^(TASK\\d*|TASKTEXT)$"), "Task"),
        (new("^(COMP|COMPLETION|COMPLETED)$"), "Completion message"), (new("^SUMMARY$"), "Summary"), (new("^LOOT$"), "Loot"),
        (new("^PLURAL$"), "Plural name"), (new("^POSTFIX$"), "Name suffix"), (new("^THREATLEVEL$"), "Threat level"),
        (new("^(IDLE|STAKEOUT|INVESTIGATING|LAIR)\\d*$"), "Status line"), (new("^POINTOFINTEREST$"), "Point of interest"),
        (new("^PHOTOSTRING$"), "Photo caption"), (new("^(OCEAN|ISLANDSELECTION)$"), "Ocean"), (new("^(CONTINENT|ISLANDSELECT)$"), "Continent"),
    };

    /// <summary>What a text is for, from its key's words (EG2_..._OBJECTIVE_TITLE_SEQ01_IRIS_VAR01_L01 → Title), rightmost known word wins.</summary>
    public static string TextLabel(string key)
    {
        var words = key.ToUpperInvariant().Split('_');
        for (int i = words.Length - 1; i >= 0; i--)
            foreach (var (w, label) in TextKinds)
                if (w.IsMatch(words[i])) return label;
        return words.Any(w => Regex.IsMatch(w, "^L\\d+$")) ? "Spoken line" : "Text";
    }

    public static readonly string[] Stats = { "Smarts", "Vitality", "Morale" };

    public sealed record StatEffect(string Stat, int Offset, float Rate);

    /// <summary>
    /// A job's (rjob) effect on the minion doing it: prop 0x12 opens with three groups of 10 floats,
    /// Smarts / Vitality / Morale, whose first float is the change rate (+ restores, - drains).
    /// Order deduced from "Restoring Morale" (0/0/+20), "Restoring Vitality" (0/+10/0) and the
    /// "Operating Item (Drain Smarts)" tag (-10/0/0). The other 9 floats look like thresholds/clamps.
    /// </summary>
    public static List<StatEffect> JobEffects(GameObject job)
    {
        if (job.Tag != "rjob" || job.Body.Length <= ObjectHeader.Size) return new();
        var p = FindProp(PropStream.Parse(job.Body, ObjectHeader.Size), 0x12);
        if (p is null || p.Kind != 0 || p.Payload().Length < 120) return new();
        return Enumerable.Range(0, 3).Select(i => new StatEffect(Stats[i], p.Offset + 9 + 40 * i, BitConverter.ToSingle(job.Body, p.Offset + 9 + 40 * i))).ToList();
    }

    public static string EffectSummary(GameObject job)
    {
        var parts = JobEffects(job).Where(e => e.Rate != 0).Select(e => $"{e.Stat} {e.Rate:+0.##;-0.##}");
        return string.Join(", ", parts) is { Length: > 0 } s ? s : "no stat change";
    }

    static Prop? FindProp(IEnumerable<PropNode> nodes, uint id)
    {
        foreach (var p in nodes.OfType<Prop>())
        {
            if (p.Id == id) return p;
            if (FindProp(p.Children, id) is { } c) return c;
        }
        return null;
    }

    public sealed record Cost(string Resource, int Offset, uint Amount);

    /// <summary>Research (rtrp) costs: kind-0 props of 8 bytes, [resource (rcns) id][u32 amount]. All 211 have at least one.</summary>
    public static List<Cost> Costs(GameObject o, GameData g)
    {
        var list = new List<Cost>();
        var b = o.Body;
        for (int i = ObjectHeader.Size; i + 17 <= b.Length; i++)
            if (b[i + 2] == 0 && b[i + 3] == 0x80 && b[i + 4] == 0 && Bytes.U32(b, i + 5) == 8
                && g.ObjectsById.TryGetValue(Bytes.U32(b, i + 9), out var r) && r[0].Tag == "rcns")
                list.Add(new Cost(r[0].Name.Trim('"'), i + 13, Bytes.U32(b, i + 13)));
        return list;
    }

    /// <summary>
    /// Research time offset: every rtrp opens with prop 0xf holding two text refs (name, description),
    /// a u32 (usually 1), then this float (Minion Cap Increase 1-5 = 300..540). -1 if the layout doesn't match.
    /// </summary>
    public static int ResearchTimeOffset(GameObject o)
    {
        var b = o.Body;
        return o.Tag == "rtrp" && b.Length > 81 && Bytes.U32(b, 24) == 0x8000000f && Bytes.U32(b, 33) == 1 && Bytes.U32(b, 49) == 1 ? 77 : -1;
    }

    public sealed record Param(string Label, int Offset, string Type, string Value);

    /// <summary>
    /// Named flowgraph parameters ("Value" of reward nodes etc.): name\0, then 31 bytes on,
    /// [u8 1][u32 4][u32 type][value] with type 2 = int, 3 = float, 4 = bool (1 byte).
    /// 5,711 of these across the game; Label is the node's preceding string (e.g. "Increase").
    /// </summary>
    /// <summary>
    /// Trait / power (rtrt) numbers: each component is [u32 1]["name" + NUL][u32 kind] then its settings; floats with plain
    /// values (not hash-like) between one component name and the next are listed as "{component}: value N".
    /// Kinds seen (HANDOFF round 24): 4 damage, 6 salary mod (0.2 = +20%), 8 movement/disguise, 12 armour/resistance,
    /// 3 stat adjustment, 7 on spawn / max stat, 0 condition.
    /// </summary>
    public static List<Param> TraitValues(GameObject o)
    {
        var list = new List<Param>();
        if (o.Tag != "rtrt") return list;
        var b = o.Body;
        var comps = new List<(int NameAt, int ValuesAt, string Name, uint Kind)>();
        for (int i = ObjectHeader.Size + 4; i + 8 < b.Length; i++)
        {
            if (Bytes.U32(b, i - 4) != 1 || b[i] < 'A' || b[i] > 'Z') continue;
            int e = i; while (e < b.Length && b[e] >= 32 && b[e] < 127) e++;
            int kindAt = i + ((e - i + 1 + 3) & ~3);   // names are NUL-terminated and padded to 4 bytes
            if (kindAt + 4 > b.Length || b[e] != 0 || e - i < 3 || Bytes.U32(b, kindAt) > 200) continue;
            comps.Add((i, kindAt + 4, Bytes.Latin1.GetString(b, i, e - i), Bytes.U32(b, kindAt)));
            i = e;
        }
        // every trait ends with a shared block (3 x f32 500, ...) that isn't a component setting
        int tail = b.AsSpan().IndexOf(Bytes.Le(0x43fa0000u, 0x43fa0000u, 0x43fa0000u));
        if (tail < 0) tail = b.Length;
        for (int c = 0; c < comps.Count; c++)
        {
            int end = Math.Min(c + 1 < comps.Count ? comps[c + 1].NameAt - 4 : b.Length, tail);
            int n = 0;
            for (int p = comps[c].ValuesAt; p + 4 <= end; p++)
            {
                float f = BitConverter.ToSingle(b, p);
                if (f == 0 || !float.IsFinite(f) || Math.Abs(f) < 0.001f || Math.Abs(f) > 100000) continue;
                if (Math.Abs(f - MathF.Round(f, 3)) > 1e-6f * Math.Max(1, Math.Abs(f))) continue;   // hashes rarely land on round numbers
                if (Bytes.U32(b, p) < 0x01000000) continue;                                         // small ints read as tiny floats
                list.Add(new Param($"{comps[c].Name}: value {++n}", p, "f32", f.ToString("G6", CultureInfo.InvariantCulture)));
                p += 3;
            }
        }
        return list;
    }

    public static List<Param> Params(GameObject o)
    {
        var list = new List<Param>();
        var b = o.Body;
        var text = Bytes.Latin1.GetString(b);
        string label = "";
        foreach (Match m in Regex.Matches(text, "([A-Za-z{][ -~]{2,80})\0"))
        {
            string name = m.Groups[1].Value;
            int v = m.Index + m.Length + 31;
            if (v + 4 <= b.Length && b[v - 9] == 1 && Bytes.U32(b, v - 8) == 4 && Bytes.U32(b, v - 4) is >= 2 and <= 4)
            {
                uint type = Bytes.U32(b, v - 4);
                string t = type == 2 ? "i32" : type == 3 ? "f32" : "u8";
                string value = type == 2 ? ((int)Bytes.U32(b, v)).ToString()
                    : type == 3 ? BitConverter.ToSingle(b, v).ToString("G6", CultureInfo.InvariantCulture)
                    : (b[v] != 0 ? "1" : "0");
                list.Add(new Param(label.Length > 0 ? $"{label} → {name}" : name, v, t, value));
            }
            else if (!name.StartsWith('{')) label = name[(name.LastIndexOfAny(new[] { '/', (char)92 }) + 1)..] is var n && NodeName.IsMatch(n) ? n : "";   // pins are {braced}; keep node names, skip hash noise like "i)chO"
        }
        return list;
    }

    /// <summary>Rows a player cares about: text, references, names and named parameters (no raw words).</summary>
    public static List<AttrRow> FriendlyRows(GameObject o, GameData g)
    {
        var rows = Rows(o, g).Where(r => r.Path != "header" && r.Size != 9 && r.Meaning.Length > 0 && r.Meaning != "byte")
            .Where(r => r.Target is not null || r.Meaning.StartsWith("text") || r.Meaning.Contains("text hash")   // links and text
                        || (r.Meaning.StartsWith('"') && r.Meaning != "\"Value\"" && NameLike.IsMatch(r.Meaning.Trim('"'))))   // names, not noise like "X+K>"
            .ToList();
        if (o.IsRecord && new FurnitureRecord(new Prop(0, 0, PropStream.Parse(o.Body))).CostOffsetOrMissing is var cost and >= 0)
            rows.Add(new AttrRow
            {
                Path = "price", Offset = cost, Size = 4, Editable = true, Hex = Convert.ToHexString(o.Body, cost, 4),
                Int = Bytes.U32(o.Body, cost).ToString(), Meaning = $"Price = {Bytes.U32(o.Body, cost):N0} gold",
            });
        foreach (var e in JobEffects(o)) rows.Add(Setting(o, e.Offset, "f32", $"{e.Stat} change (+ restores, - drains)", Fmt(e.Rate)));
        if (ResearchTimeOffset(o) is var rt and >= 0) rows.Add(Setting(o, rt, "f32", "Research time", Fmt(BitConverter.ToSingle(o.Body, rt))));
        foreach (var c in Costs(o, g)) rows.Add(Setting(o, c.Offset, "u32", $"Cost ({c.Resource})", c.Amount.ToString()));
        foreach (var p in Params(o).Concat(TraitValues(o)))
        {
            rows.Add(new AttrRow
            {
                Path = "setting", Offset = p.Offset, Size = p.Type == "u8" ? 1 : 4, Editable = true,
                Hex = Convert.ToHexString(o.Body, p.Offset, p.Type == "u8" ? 1 : 4),
                Int = p.Type == "f32" ? "" : p.Value, Float = p.Type == "f32" ? p.Value : "",
                Meaning = $"{p.Label} = {p.Value}" + (p.Type == "u8" ? " (on/off)" : ""),
            });
        }
        if (o.Tag == "rcns" && o.Body.Length > 45 && Bytes.U32(o.Body, 41) < 1_000_000)
            rows.Add(Setting(o, 41, "u32", "Maximum you can hold (new games; 0 = no cap)", Bytes.U32(o.Body, 41).ToString()));
        // named settings first, then everything else in file order
        rows = rows.OrderBy(r => r.Path is "price" or "setting" ? 0 : 1).ThenBy(r => r.Offset).ToList();
        int shown = rows.Count, raw = Rows(o, g).Count(r => r.Size is 1 or 4 && r.Path != "header");
        rows = Explain(rows, o, g);
        if (raw > shown)
            rows.Add(new AttrRow { Path = "Not decoded yet", Offset = int.MaxValue, Meaning = $"{raw - shown} more values whose meaning isn't known yet (tick \"Show raw data\" to see and change them)" });
        return rows;
    }

    /// <summary>
    /// Puts a plain label in Path for the friendly view: what a text is for (from its key), what a link points at,
    /// "Script setting" for flowgraph numbers and "Script (internal)" for flowgraph paths. Repeated labels are numbered.
    /// </summary>
    static List<AttrRow> Explain(List<AttrRow> rows, GameObject o, GameData g)
    {
        var seen = new Dictionary<string, int>();
        var list = new List<AttrRow>();
        bool named = false;   // the first string is the object's own name; later ones in scripted objects are script node/variable names
        foreach (var r in rows)
        {
            string label, meaning = r.Meaning;
            if (r.Path == "price") { label = "Price"; meaning = meaning.Replace("Price = ", ""); }
            else if (r.Path == "setting")
            {
                int eq = meaning.LastIndexOf(" = ", StringComparison.Ordinal);
                label = meaning.Contains(" → ") ? "Script setting" : meaning[..eq];
                meaning = meaning.Contains(" → ") ? meaning.Replace(" → Value", "").Replace(" → Count", " (count)") : meaning[(eq + 3)..];
            }
            else if (r.Target is { } t)
            {
                string one = Types.TryGetValue(t.Tag, out var ti) ? ti.One : t.Tag;
                label = "Link: " + one;
                meaning = t.Tag == "rjob" ? $"{t.Name}  ({EffectSummary(t)})"
                    : t.Name.StartsWith("0x") ? $"{one} {t.Name}  (unnamed in the game files; open it to see what shares it)" : t.Name;
            }
            else if (r.Meaning.StartsWith("text") && r.Offset + 4 <= o.Body.Length && g.Text.TryGetValue(Bytes.U32(o.Body, r.Offset), out var text))
            {
                label = TextLabel(text.Key);
                meaning = $"\"{Clean(text.Text)}\"";
            }
            else if (meaning.StartsWith('"'))
            {
                string s = meaning.Trim('"');
                if (s.StartsWith('{')) continue;   // script pin names, already shown with their setting
                label = s.Contains('/') || s.Contains('\\') ? "Script (internal)" : !named || o.IsRecord ? "Internal name" : "Script part (internal)";
                named = true;
            }
            else label = "Value";
            if (label is not ("Script setting" or "Script (internal)" or "Script part (internal)" or "Internal name" or "Price"))
                label = (seen[label] = seen.GetValueOrDefault(label) + 1) is var n and > 1 ? $"{label} {n}" : label;
            list.Add(new AttrRow
            {
                Path = label, Offset = r.Offset, Hex = r.Hex, Int = r.Int, Float = r.Float, Size = r.Size,
                Editable = r.Editable, Target = r.Target, Meaning = meaning, IsSetting = r.Path is "price" or "setting",
            });
        }
        return list;
    }

    static string Fmt(float f) => f.ToString("G6", CultureInfo.InvariantCulture);

    static AttrRow Setting(GameObject o, int off, string type, string label, string value) => new()
    {
        Path = "setting", Offset = off, Size = 4, Editable = true, Hex = Convert.ToHexString(o.Body, off, 4),
        Int = type == "f32" ? "" : value, Float = type == "f32" ? value : "", Meaning = $"{label} = {value}",
    };

    public static List<AttrRow> Rows(GameObject o, GameData g)
    {
        var rows = new List<AttrRow>();
        var b = o.Body;
        int start = 0;
        if (!o.IsRecord && b.Length >= ObjectHeader.Size)
        {
            string[] names = { "version", "pad0", "object id", "pad1", "package id", "aux id" };
            for (int i = 0; i < 6; i++) rows.Add(Labeled(Word(b, i * 4, "header", g), names[i]));
            start = ObjectHeader.Size;
        }
        Walk(PropStream.Parse(b, start), b, "", rows, g);
        return rows;
    }

    static AttrRow Labeled(AttrRow r, string m) => new()
    {
        Path = r.Path, Offset = r.Offset, Hex = r.Hex, Int = r.Int, Float = r.Float, Size = r.Size,
        Editable = r.Editable, Target = r.Target, Meaning = m + (r.Meaning.Length > 0 ? "  " + r.Meaning : ""),
    };

    static void Walk(List<PropNode> nodes, byte[] b, string indent, List<AttrRow> rows, GameData g)
    {
        foreach (var n in nodes)
        {
            if (n is Prop p)
            {
                int len = p.Payload().Length;
                rows.Add(new AttrRow
                {
                    Path = $"{indent}▸ prop {p.Id:x}", Offset = p.Offset, Hex = Bytes.Hex(p.Key)[2..],
                    Meaning = $"kind {p.Kind}, {len} bytes", Size = 9,
                });
                Walk(p.Children, b, indent + "   ", rows, g);
            }
            else if (n is RawNode r)
                Tokens(b, r.Offset, r.Offset + r.Data.Length, indent + "·", rows, g);
        }
    }

    static void Tokens(byte[] b, int pos, int end, string path, List<AttrRow> rows, GameData g)
    {
        while (pos < end)
        {
            if (pos + 16 <= end && Bytes.U32(b, pos) == 1 && Bytes.U32(b, pos + 4) == 0 && Bytes.U32(b, pos + 8) > 0xFFFF)
            {
                uint type = Bytes.U32(b, pos + 8), id = Bytes.U32(b, pos + 12);
                var w = Word(b, pos + 12, path, g);
                string what = type == FurnitureRecord.TextRefType
                    ? (g.Text.TryGetValue(id, out var t) ? $"text \"{Clean(t.Text)}\" ({t.Table}/{t.Key})" : "text (unknown hash)")
                    : w.Meaning.StartsWith("text hash ") ? "text " + w.Meaning["text hash ".Length..]   // e.g. 0x9fbd7b34 (research names)
                    : $"ref type {Bytes.Hex(type)}  {w.Meaning}";
                rows.Add(new AttrRow
                {
                    Path = path, Offset = pos + 12, Hex = Convert.ToHexString(b, pos, 16), Int = id.ToString(),
                    Meaning = what, Size = 4, Editable = true, Target = w.Target,
                });
                pos += 16;
                continue;
            }
            if (StringAt(b, pos, end) is { } s)
            {
                rows.Add(new AttrRow { Path = path, Offset = pos, Meaning = $"\"{s}\"", Size = s.Length + 1 });
                pos += s.Length + 1;
                while (pos < end && pos % 4 != 0 && b[pos] == 0) pos++;   // NUL padding to a 4-byte field
                continue;
            }
            // a string starting 1-3 bytes ahead: emit the gap as bytes rather than eat the string's start
            bool strAhead = false;
            for (int k = 1; k < 4 && !strAhead; k++) strAhead = StringAt(b, pos + k, end) is not null;
            if (pos + 4 <= end && !strAhead) { rows.Add(Word(b, pos, path, g)); pos += 4; continue; }
            rows.Add(new AttrRow { Path = path, Offset = pos, Hex = b[pos].ToString("x2"), Int = b[pos].ToString(), Size = 1, Editable = true, Meaning = "byte" });
            pos++;
        }
    }

    static string? StringAt(byte[] b, int pos, int end)
    {
        if (pos >= end) return null;
        int nul = Array.IndexOf(b, (byte)0, pos, Math.Min(end - pos, 256));
        if (nul < pos + 4) return null;
        var s = Bytes.Latin1.GetString(b, pos, nul - pos);
        return Printable.IsMatch(s) ? s : null;
    }

    static AttrRow Word(byte[] b, int pos, string path, GameData g)
    {
        uint u = Bytes.U32(b, pos);
        float f = BitConverter.ToSingle(b, pos);
        string meaning = "";
        GameObject? target = null;
        if (u > 0xFFFF)
        {
            if (g.ObjectsById.TryGetValue(u, out var hits)) { target = hits[0]; meaning = $"→ {target.Tag} {target.Name}"; }
            else if (g.Text.TryGetValue(u, out var t)) meaning = $"text hash \"{Clean(t.Text)}\"";
        }
        return new AttrRow
        {
            Path = path, Offset = pos, Hex = Convert.ToHexString(b, pos, 4), Size = 4, Editable = true, Target = target, Meaning = meaning,
            Int = u < 0x01000000 || (int)u is > -100000 and < 0 ? ((int)u).ToString() : "",
            Float = float.IsFinite(f) && Math.Abs(f) is > 1e-4f and < 1e7f ? f.ToString("G6", CultureInfo.InvariantCulture) : "",
        };
    }

    /// <summary>Objects whose bytes contain this object's id (what points at it).</summary>
    public static IEnumerable<GameObject> ReferencedBy(GameObject o, GameData g)
    {
        if (o.IsRecord || o.ObjectId == 0) yield break;
        var needle = Bytes.Le(o.ObjectId);
        foreach (var x in g.Objects)
            if (!ReferenceEquals(x, o) && x.Body.AsSpan(x.IsRecord ? 0 : 12).IndexOf(needle) >= 0) yield return x;
    }
}
