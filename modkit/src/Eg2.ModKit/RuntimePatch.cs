using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eg2.ModKit;

/// <summary>
/// One value the xinput1_4.dll proxy writes into the running game (see native/winmm/dllmain.cpp).
/// Self-contained: the pattern and how to resolve it travel with the mod, so new tweaks never
/// need a new DLL. Data (globals and struct fields), except kind code_bytes: <see cref="Value"/> is hex bytes written
/// once over game code at match + <see cref="At"/>, inside the pattern, which must match exactly once (so it pins the
/// bytes it replaces).
/// </summary>
public sealed class RuntimePatch
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Type { get; set; } = "u32";
    public string Pattern { get; set; } = "";
    /// <summary>Instruction start relative to the match.</summary>
    public int At { get; set; }
    /// <summary>Offset of the rel32 inside that instruction; -1 = not RIP-relative.</summary>
    public int Rel { get; set; } = -1;
    public int Len { get; set; }
    /// <summary>The resolved address holds a pointer; follow it (runtime objects).</summary>
    public bool Deref { get; set; }
    public int Offset { get; set; }
    /// <summary>Don't write while the current value equals this (e.g. 0xFFFFFFFF = not initialised yet).</summary>
    public string? SkipIf { get; set; }
    public string? Note { get; set; }

    /// <summary>"" = plain address. "hashmap_record" = a per-save record found through the game's resource
    /// hash map: the pattern's match number <see cref="Hit"/> holds `mov esi,[rip+id]` at <see cref="IdAt"/> and
    /// `mov [rip+mirror],eax` at <see cref="MirrorAt"/>; the record whose [current][max] equals the mirror is written
    /// at <see cref="Offset"/>.</summary>
    public string Kind { get; set; } = "";
    public int Hit { get; set; }
    public int IdAt { get; set; }
    public int MirrorAt { get; set; }
    /// <summary>table_lookup: `cmove r32,[rip+alt]` at <see cref="AltAt"/> (the id used instead when the id equals
    /// <see cref="AltIf"/>) and `call lookup` at <see cref="CallAt"/>; the id is `mov r32,[rip+id]` at <see cref="IdAt"/>.
    /// The lookup function's table (count, capacity, values, keys) is read from its code; the record found is written at
    /// <see cref="Offset"/>.</summary>
    public int AltAt { get; set; }
    public string? AltIf { get; set; }
    public int CallAt { get; set; }

    static readonly string[] Types = { "u8", "u16", "u32", "i32", "u64", "f32", "f64" };
    static readonly Regex PatternRx = new("^([0-9A-Fa-f]{2}|\\?\\?)( ([0-9A-Fa-f]{2}|\\?\\?))*$");

    /// <summary>Everything except the value, for conflict checks between mods.</summary>
    public string Where => $"{Kind}|{Hit}|{IdAt}|{MirrorAt}|{AltAt}|{CallAt}|{Pattern.ToUpperInvariant()}|{At}|{Rel}|{Len}|{Deref}|{Offset}|{Type}";

    public string? Error()
    {
        if (!Regex.IsMatch(Name, "^[A-Za-z0-9_]+$")) return "name must be letters, digits or _";
        if (!PatternRx.IsMatch(Pattern.Trim())) return "pattern must be hex bytes or ?? separated by spaces";
        if (!Types.Contains(Type)) return $"type must be one of {string.Join(", ", Types)}";
        if (Rel >= 0 && Len <= Rel + 3) return "len must cover the rel32 operand";
        if (Kind is not ("" or "hashmap_record" or "table_lookup" or "code_bytes")) return "kind must be empty, hashmap_record, table_lookup or code_bytes";
        if (Kind == "code_bytes")
        {
            string b = Value.Trim();
            if (!PatternRx.IsMatch(b) || b.Contains('?')) return "value must be hex bytes separated by spaces";
            int n = b.Split(' ').Length, len = Pattern.Trim().Split(' ').Length;
            return At >= 0 && At + n <= len ? null : "the bytes must lie inside the pattern";
        }
        var inv = CultureInfo.InvariantCulture;
        string v = Value.Trim();
        bool ok = Type.StartsWith('f') ? double.TryParse(v, NumberStyles.Float, inv, out _)
            : v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ulong.TryParse(v[2..], NumberStyles.HexNumber, inv, out _)
            : long.TryParse(v, NumberStyles.Integer, inv, out _);
        return ok ? null : $"value '{Value}' is not a valid {Type}";
    }

    public string ToConfig()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{Name}]");
        if (!string.IsNullOrWhiteSpace(Note)) sb.AppendLine($"# {Note.Replace('\n', ' ')}");
        sb.AppendLine($"pattern = {Pattern.Trim().ToUpperInvariant()}");
        if (Kind == "hashmap_record") sb.AppendLine($"kind = {Kind}").AppendLine($"hit = {Hit}").AppendLine($"id_at = {IdAt}").AppendLine($"mirror_at = {MirrorAt}");
        if (Kind == "table_lookup") sb.AppendLine($"kind = {Kind}").AppendLine($"id_at = {IdAt}").AppendLine($"alt_at = {AltAt}").AppendLine($"alt_if = {AltIf ?? "0"}").AppendLine($"call_at = {CallAt}");
        if (Kind == "code_bytes") sb.AppendLine($"kind = {Kind}");
        if (At != 0) sb.AppendLine($"at = {At}");
        if (Rel >= 0) sb.AppendLine($"rel = {Rel}").AppendLine($"len = {Len}");
        if (Deref) sb.AppendLine("deref = 1");
        if (Offset != 0) sb.AppendLine($"offset = 0x{Offset:X}");
        if (Kind != "code_bytes") sb.AppendLine($"type = {Type}");
        sb.AppendLine($"value = {(Kind == "code_bytes" ? Value.Trim().ToUpperInvariant() : Value.Trim())}");
        if (!string.IsNullOrWhiteSpace(SkipIf)) sb.AppendLine($"skip_if = {SkipIf.Trim()}");
        return sb.ToString();
    }

    public RuntimePatch Copy() => (RuntimePatch)MemberwiseClone();

    public override string ToString() => Name;
}

/// <summary>Known tweaks (patterns from the community CE table, verified against the DX12 and Vulkan exes).</summary>
public static class RuntimePresets
{
    const string MinionCap = "8B 3D ?? ?? ?? ?? 8B 8B ?? ?? ?? ?? 03 0D ?? ?? ?? ?? 03 8B ?? ?? ?? ?? 3B F9 0F 43 F9";
    const string Vault = "44 8B 05 ?? ?? ?? ?? B8 FF FF FF FF";
    const string Settings = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 80 78 ?? 00 75 ?? 0F B7";
    /// <summary>Resource HUD update (both exes): match 0 = Intel, 1 = Tech. lea rdx,[ref]; lea rcx,[rbx+1730h]; call;
    /// mov esi,[rip+id] (+19); mov [rip+mirror],eax (+25) with mirror = [current][max].</summary>
    /// <summary>Economy tick (7007e0 in the 2026 dx12 exe): mov ecx,[rip+preset id]; cmp ecx,hash("custom"); cmove
    /// ecx,[rip+custom id]; call lookup; lea rcx,[rax+44h] ... mov eax,[rcx+14h] -> the salary rate the payday code
    /// multiplies category 1 (salaries) by. So the preset record's f32 at +0x58.</summary>
    const string Economy = "8B 0D ?? ?? ?? ?? 81 F9 71 8B 96 AF 0F 44 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 33 ED 48 85 C0 48 8D 48 44 48 0F 44 CD 48 85 C9 74 ?? 8B 41 14";
    const string ResourceHud = "48 8D 15 ?? ?? ?? ?? 48 8D 8B 30 17 00 00 E8 ?? ?? ?? ?? 8B 35 ?? ?? ?? ?? 89 05 ?? ?? ?? ?? 85 F6";

    static RuntimePatch SavedCap(string name, int hit, string what) => new()
    {
        Name = name, Kind = "hashmap_record", Pattern = ResourceHud, Hit = hit, IdAt = 19, MirrorAt = 25, Offset = 4, Value = "999",
        Note = $"{what} maximum stored in the loaded save (the data value only seeds new games). Written into the save's resource record.",
    };

    static RuntimePatch Setting(string name, int offset, string type, string value, string note) => new()
    {
        Name = name, Pattern = Settings, Rel = 3, Len = 7, Deref = true, Offset = offset, Type = type, Value = value, Note = note,
    };

    static RuntimePatch Code(string name, string pattern, int at, string value, string note) => new()
    {
        Name = name, Kind = "code_bytes", Pattern = pattern, At = at, Value = value, Note = note,
    };

    /// <summary>Several side stories at once (all three needed; patterns unique in the dx12 and vulkan exes). Each refuses
    /// while another side story isn't paused (state 6).</summary>
    public static readonly IReadOnlyList<RuntimePatch> SideStories = new[]
    {
        Code("sidestory_start_any", "4B 8D 0C 40 48 C1 E1 05 42 83 7C 09 14 06 74 0B 42 39 5C 09 08 0F 85 ?? ?? ?? ??", 21, "66 0F 1F 44 00 00",
             "Objective manager, start message 0x80b8 (dx12 0x92c615): jne -> nop"),
        Code("sidestory_resume_any", "8B C2 48 6B C8 58 42 83 7C 11 14 06 74 0B 46 39 4C 11 08 0F 85 ?? ?? ?? ??", 19, "66 0F 1F 44 00 00",
             "Objectives screen resume button (dx12 0x8612c3): jne -> nop"),
        Code("sidestory_none_active_flag", "41 8B C0 48 6B C8 58 42 8B 54 09 14 83 EA 01 0F 84 ?? ?? ?? ?? 83 EA 01 0F 84 ?? ?? ?? ?? 83 FA 02 0F 84 ?? ?? ?? ??", 7, "31 D2 0F 1F 00",
             "Objectives screen (dx12 0x8614a7): GUI flag 'a side story is active' (GUI variable 0xc7bfa2ac) stays 0"),
    };

    public static readonly IReadOnlyList<RuntimePatch> All = new[]
    {
        new RuntimePatch { Name = "minion_hard_cap", Pattern = MinionCap, Rel = 2, Len = 6, Value = "1000",
            Note = "Absolute minion limit (300). Capacity = min(this, base + lockers + research)." },
        new RuntimePatch { Name = "minion_base_capacity", Pattern = MinionCap, At = 12, Rel = 2, Len = 6, Value = "20",
            Note = "Minion capacity before lockers/research (20 in the exe)." },
        new RuntimePatch { Name = "gold_capacity", Pattern = Vault, Rel = 3, Len = 7, Value = "1000000", SkipIf = "0xFFFFFFFF",
            Note = "Base gold capacity before vaults (40000 at runtime)." },
        Setting("unlimited_gold", 0x05, "u8", "1", "Developer setting: unlimited gold (1 = on)."),
        Setting("instant_minion_training", 0x2D, "u8", "1", "Developer setting: instant minion training (1 = on)."),
        Setting("unlock_all", 0x10, "u8", "1", "Developer setting: unlock all (1 = on). Unverified."),
        Setting("freeze_minion_stats", 0x12, "u8", "1", "Developer setting: freeze minion stats (1 = on)."),
        Setting("default_base_power", 0x34, "u32", "100", "Developer setting: default base power."),
        Setting("minimum_power", 0x3C, "u32", "0", "Developer setting: minimum power."),
        SavedCap("intel_cap_save", 0, "Intel"),
        SavedCap("tech_cap_save", 1, "Tech"),
        new() { Name = "salary_rate", Kind = "table_lookup", Pattern = Economy, IdAt = 0, AltAt = 12, AltIf = "0xAF968B71", CallAt = 19, Offset = 0x58, Type = "f32", Value = "1",
                Note = "Salary rate of the game-setup preset in use (payday = salaries x this). Written into the preset every second." },
    };
}
