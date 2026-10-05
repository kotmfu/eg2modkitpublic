using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eg2.ModKit;

public sealed class NewFurniture
{
    /// <summary>Internal name of the new item ([A-Za-z0-9_]).</summary>
    public string Id { get; set; } = "";
    public string Donor { get; set; } = "";
    public uint Cost { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Give the item its own copy of the donor's models, materials and textures (so its textures can
    /// change without touching the donor). Off = shares the donor's art.</summary>
    public bool OwnArt { get; set; }
    /// <summary>Optional: another item (same package) whose look (model, fnas/COMA) the new item uses instead of the donor's.</summary>
    public string? ArtFrom { get; set; }
    /// <summary>With <see cref="OwnArt"/>: replacement DDS files for the copied textures.</summary>
    [System.ComponentModel.Browsable(false)] public List<ArtTexture> Textures { get; set; } = new();
}

/// <summary>A new texture in the mod package. Name = game path it's found by; Source = DDS in the mod's assets folder.</summary>
public sealed class NewAsset
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>"frontend": into the frontend package (menus and the island select load before ModKit's own package).
    /// Null = ModKit's package.</summary>
    public string? Package { get; set; }
}

/// <summary>A new DDS for one of an own-art item's textures. Texture = the donor's texture path; Source = file in the mod's assets folder.</summary>
public sealed class ArtTexture
{
    public string Texture { get; set; } = "";
    public string Source { get; set; } = "";
}

/// <summary>
/// A brand-new data object of any type (scheme, objective, research, trait, ...): a copy of
/// <see cref="Source"/> with its own id, placed in the mod package. Other edits refer to it as
/// "@Key" (a FieldEdit value of "@Key" writes its id), e.g. to put it in a research tree.
/// </summary>
public sealed class NewObject
{
    /// <summary>Name used to refer to it ([A-Za-z0-9_]); stable, so rebuilds keep the same id.</summary>
    public string Key { get; set; } = "";
    public string Package { get; set; } = "";
    public string Tag { get; set; } = "";
    /// <summary>Object id of the copy's source ("0x1234abcd").</summary>
    public string Source { get; set; } = "";
    /// <summary>Put the copy in this package instead of the source's (only "frontend": objects the island select needs,
    /// which loads before ModKit's own package); a same-id copy also goes in ModKit's package for the game proper. Null = the
    /// source's.</summary>
    public string? Into { get; set; }
    /// <summary>Replaced texts: Offset = the text hash inside a text reference of the source.</summary>
    [System.ComponentModel.Browsable(false)] public List<NewObjectText> Texts { get; set; } = new();
    /// <summary>Changed values, by offset into the object (Package/Tag/Object ignored; Value may be "@Key").</summary>
    [System.ComponentModel.Browsable(false)] public List<FieldEdit> Edits { get; set; } = new();
    public int TextCount => Texts.Count;
    public int ValueCount => Edits.Count;
    public string? Note { get; set; }
}

public sealed class NewObjectText
{
    public int Offset { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A research tree laid out by a mod: these nodes and links replace the game's for that tree.</summary>
public sealed class TreeLayout
{
    /// <summary>Tree object id ("0x06530449" = MINIONS).</summary>
    public string Tree { get; set; } = "";
    public List<TreeNode> Nodes { get; set; } = new();
    /// <summary>Prerequisites, as indexes into <see cref="Nodes"/>: From must be researched before To.</summary>
    public List<TreeLink> Links { get; set; } = new();
    /// <summary>Engineering trees: a DDS in the mod's assets folder shown as the tree's background (null = the game's).</summary>
    public string? Background { get; set; }
}

public sealed class TreeNode
{
    /// <summary>Research id ("0x...") or a new research object ("@Key").</summary>
    public string Research { get; set; } = "";
    /// <summary>Research trees: 1-based column (tier) and 0-based row. Engineering trees: both 0-based.</summary>
    public int Column { get; set; }
    public int Row { get; set; }
    /// <summary>Engineering (crafting) trees only: offset inside the cell.</summary>
    public float Dx { get; set; }
    public float Dy { get; set; }
}

public sealed class TreeLink
{
    public int From { get; set; }
    public int To { get; set; }
}

/// <summary>
/// Replace one of an object's scripts (e.g. what a research rewards) with a copy of another object's script.
/// Script numbers count the object's swappable scripts from 0 (<see cref="FlowGraph.Swappable"/>).
/// </summary>
public sealed class ScriptSwap
{
    /// <summary>Object whose script is replaced: "0x..." (game object) or "@Key" (this mod's new object).</summary>
    public string Object { get; set; } = "";
    public int Script { get; set; }
    /// <summary>Game object the script is copied from ("0x...").</summary>
    public string From { get; set; } = "";
    public int FromScript { get; set; }
    /// <summary>Changed numbers in the copy: Offset is relative to the copied script block, Expect = the donor's bytes.</summary>
    public List<FieldEdit> Values { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>
/// New requirement / unlock lists for a research or engineering item; a null list keeps the game's.
/// Refs: minion types and resources as "0x..." ids, furniture by name (a game item or one of your new furniture ids).
/// </summary>
public sealed class RequirementEdit
{
    /// <summary>"0x..." (game item) or "@Key" (this mod's new item).</summary>
    public string Object { get; set; } = "";
    public List<CountedRef>? Minions { get; set; }
    public List<CountedRef>? Furniture { get; set; }
    public List<CountedRef>? Costs { get; set; }
    /// <summary>Furniture that becomes buildable when it's done.</summary>
    public List<string>? Unlocks { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A new task list for an objective (robj): each entry "0x&lt;objective id&gt;:&lt;task index&gt;" (0-based) names a game task,
/// from this objective or another. Order = the order the game runs them.
/// </summary>
public sealed class TaskEdit
{
    /// <summary>"0x..." (game objective) or "@Key" (this mod's new objective).</summary>
    public string Object { get; set; } = "";
    public List<string> Tasks { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>Scheme (rscm) changes; null = keep the game's. Minions = alternatives (each offer uses one group, picked by the game).</summary>
/// <summary>A lair map change (<see cref="LairMap"/>): set the rock tier (1 = easiest, the start area; 4 = hardest) or dig out
/// the rock in a rectangle, or place an object. X = column, Y = row. Only new games read the map.</summary>
public sealed class MapEdit
{
    /// <summary>"envs\basedefinitions\lair_tropical_01_default.base".</summary>
    public string File { get; set; } = "";
    public int Floor { get; set; }
    public int X0 { get; set; }
    public int Y0 { get; set; }
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int Tier { get; set; }
    /// <summary>"tier" (default: set the rock tier), "dig" (dig out the rock), "gold" (rock -> gold seam), "wall" (rock ->
    /// edge rock), "rock" (outside/edge/dug cells -> rock of <see cref="Tier"/>), "room" (dig out / change to <see cref="Room"/>), "remove" (the pre-placed objects whose
    /// corner cell is in it) in the rectangle, or "place" (a copy
    /// of a pre-placed object: <see cref="Item"/> at column X0, row Y0), or "character" (experimental: the whole entities
    /// in <see cref="Template"/>, hex, space-separated, e.g. an agent copied from a save of the same lair), or "agent"
    /// (<see cref="Agents.Place"/>: Template = entity + companion, <see cref="Squad"/>, at column X0, row Y0).</summary>
    public string Action { get; set; } = "tier";
    /// <summary>room: the room to build (a <see cref="LairMap.Rooms"/> name, e.g. "Barracks"); rock and rooms in the
    /// rectangle become that room.</summary>
    public string? Room { get; set; }
    /// <summary>place: the furniture's record name (e.g. "Abandoned_Fountain"); a lair map must already have one facing this way.</summary>
    public string? Item { get; set; }
    /// <summary>place: facing as "x,y" (0,1 / 1,0 / 0,-1 / -1,0).</summary>
    public string? Facing { get; set; }
    /// <summary>place, for furniture copied from a save: the object's record as hex, so the mod carries it (players
    /// don't have the modder's saves). <see cref="TemplateKey"/> is its format key; it only fits maps with the same one.</summary>
    public string? Template { get; set; }
    public string? TemplateKey { get; set; }
    /// <summary>Experimental: place a copied record whose object version (TemplateKey, e.g. 0x68 = 104) differs from the
    /// map's (Crown Gold: 0x64 = 100) as it is, in case the game reads each object by its own version.</summary>
    public bool AnyVersion { get; set; }
    /// <summary>character: optional agent squad (hex: [01][u32 id][u32 1] + prop 0x3eb) added to the map's AI block, so
    /// the characters have orders (a character without a squad stands still and can't be targeted).</summary>
    public string? Squad { get; set; }
    /// <summary>agent, character: the wave (hex) the squad belongs to, in place of the template's (squad entry @31). A wave
    /// is a misc\common.asr BLUE object: story waves set DisableImmuneAutoLeaves, so their agents stay killable when
    /// they decide to leave (heat-raid waves don't). See <see cref="Agents.StoryWaves"/>.</summary>
    public string? Wave { get; set; }
    /// <summary>agent: the character class (hex, a misc\common.asr BLUE ActorBase object) in place of the template's
    /// (entity @56, squad member records). With a <see cref="ClassTree"/> it gives placed agents their own behaviour.</summary>
    public string? Class { get; set; }
    /// <summary>agent: the behaviour tree (hex, misc\common.asr AXBT) it runs, in place of the one its save stored (entity
    /// @240), e.g. 00313fd4 EG Idle. Only this agent changes.</summary>
    public string? Tree { get; set; }
    /// <summary>agent: map objects (hex grid object ids, space-separated) it walks between, Diver style (see
    /// <see cref="Agents.WithPatrol"/>); use with a WaveTargetType 2 <see cref="Wave"/>. <see cref="PatrolState"/>: the
    /// member's state byte (Divers 8), optional.</summary>
    public string? Patrol { get; set; }
    public int? PatrolState { get; set; }
    /// <summary>agent: the transport vehicle it came in, "id:type" hex (swapped for a same-type one on another island).</summary>
    public string? Vehicle { get; set; }
    public string? Note { get; set; }
}

/// <summary>A new lair: envs\basedefinitions\{From}.base and envs\{From}.* (island, sounds, textures) copied to {Stem}, the map's lair
/// id (bsnf +17, KeyHash of the stem) set to the new stem's. The exe registers every .base in that folder; which island-select
/// entry (felr) leads to it is not known yet (HANDOFF round 49).</summary>
public sealed class NewLair
{
    /// <summary>"lair_ural_01_default" (lowercase letters, digits, _; must start "lair_").</summary>
    public string Stem { get; set; } = "";
    /// <summary>The game lair it starts as, e.g. "lair_tropical_03_default".</summary>
    public string From { get; set; } = "";
    /// <summary>The new lair's world-map region ("@Key" of a new rmlr whose +83 is KeyHash("{Stem}.base")): replaces the
    /// source island's region in the copy's header list. The island select entry (felr) points at that region.</summary>
    public string? Region { get; set; }
    /// <summary>Keep the source lair's id in the map header (bsnf +17). The game fetches per-lair settings by that id
    /// (misc\common.asr BLUE: a map from lair id to settings). Without KeepId the build adds a copy of the source's
    /// settings under the new id (a whole-file common.asr patch) naming the new island. With KeepId the lair shares
    /// the source's settings (and "lair is X" conditions); the region still picks the new map, and the runtime DLL opens
    /// the new lair's island copies in place of the source's while the new map (or a save of it) is loaded.</summary>
    public bool KeepId { get; set; }
    /// <summary>Values changed in the new island's .pc copy: Tag = a chunk tag (the first chunk with it, e.g. "FOG "),
    /// Offset into its body; Package and Object unused. FOG: f32 RGBA tint at 21, start 37, end 41, haze RGB 126
    /// (tropical 1.6/2.0/3.0), scatter RGB 150 (tropical 16/14.6/10.7).</summary>
    public List<FieldEdit> IslandEdits { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>A change to an island's outdoor scenery (envs\<lair>.pc, <see cref="IslandScenery"/>): the instances of one
/// scenery group (optionally only those inside X0..X1 / Z0..Z1, world units) are hidden or moved.</summary>
public sealed class SceneryEdit
{
    /// <summary>"envs/lair_tropical_03_default.pc".</summary>
    public string File { get; set; } = "";
    public int Group { get; set; }
    /// <summary>The group's name when the edit was made (checked, so a game update that reorders groups is caught).</summary>
    public string? GroupName { get; set; }
    public float? X0 { get; set; }
    public float? Z0 { get; set; }
    public float? X1 { get; set; }
    public float? Z1 { get; set; }
    /// <summary>"hide" (default), "move" by DX/DY/DZ, or "copy" (a copy of each, shifted by DX/DY/DZ), or "mesh" (the group's model
    /// replaced by <see cref="Source"/>: an edited OBJ from the Island tab's Export mesh, same vertices, moved).</summary>
    public string Action { get; set; } = "hide";
    /// <summary>mesh: the OBJ file in the mod's assets folder.</summary>
    public string? Source { get; set; }
    public float DX { get; set; }
    public float DY { get; set; }
    public float DZ { get; set; }
    public string? Note { get; set; }
}

/// <summary>Schemes added to or removed from a scheme pool (rspl: what a region or objective step offers).</summary>
public sealed class PoolEdit
{
    /// <summary>"0x..." of the pool.</summary>
    public string Pool { get; set; } = "";
    /// <summary>Schemes to add: "0x..." (game scheme) or "@Key" (this mod's new scheme).</summary>
    public List<string> Add { get; set; } = new();
    /// <summary>Schemes to take out ("0x..."); the pool must keep at least one.</summary>
    public List<string> Remove { get; set; } = new();
    public string? Note { get; set; }

    /// <summary>The Add/Remove lists that turn the game's pool <paramref name="game"/> into <paramref name="wanted"/>
    /// (Remove takes out every copy of a scheme, so copies that stay are added back).</summary>
    public static (List<string> Add, List<string> Remove) Diff(IReadOnlyList<string> game, IReadOnlyList<string> wanted)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var add = new List<string>(wanted);
        var remove = new List<string>();
        foreach (var r in game)
        {
            int i = add.FindIndex(x => cmp.Equals(x, r));
            if (i >= 0) add.RemoveAt(i); else if (!remove.Contains(r, cmp)) remove.Add(r);
        }
        foreach (var r in remove) { add.RemoveAll(x => cmp.Equals(x, r)); add.AddRange(wanted.Where(x => cmp.Equals(x, r))); }
        return (add, remove);
    }
}

public sealed class SchemeEdit
{
    /// <summary>"0x..." (game scheme) or "@Key" (this mod's new scheme).</summary>
    public string Object { get; set; } = "";
    public List<List<CountedRef>>? Minions { get; set; }
    /// <summary>Heat gain to the region (negative = heat reduction).</summary>
    public float? Heat { get; set; }
    /// <summary>Launch cost: resources as "0x..." rcns ids (Intel, Gold, ...).</summary>
    public List<CountedRef>? Costs { get; set; }
    /// <summary>Seconds the scheme runs (7200 = 2 hours).</summary>
    public float? Duration { get; set; }
    /// <summary>Seconds the offer stays on the world map (-1 = stays).</summary>
    public float? Expiry { get; set; }
    public string? Note { get; set; }
}

/// <summary>Furniture footprint (tiles) and job slot count; null = keep the game's. Slots added copy the last slot.</summary>
public sealed class FurnitureShapeEdit
{
    /// <summary>Furniture record name (e.g. UniformRack_01).</summary>
    public string Name { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Slots { get; set; }
    public string? Note { get; set; }
}

/// <summary>Who may do a job (rjob): the full list of character keys ("0x..." minion type or other character hash).</summary>
public sealed class JobEdit
{
    public string Object { get; set; } = "";
    public List<string> Types { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>
/// A structural script change (HANDOFF round 31), applied in order after the object's other edits. Op: "remove-node"
/// (Node), "copy-node" (Node; the copy gets the next free id, which later ops can use), "add-link" (From, FromPin, To,
/// ToPin; the output must already have a link), "remove-link" (Link). Pins are the hashes shown in the Scripts window.
/// </summary>
/// <summary>
/// One behaviour tree setting changed (AXBT in misc\common.asr, <see cref="BehaviourTrees"/>): tree hash, step index, the
/// setting's position among the step's typed settings (0-based), and the new value (a number for floats and enums, true or
/// false for bools). Expect = the game's value as shown, to catch a game update moving things.
/// </summary>
public sealed class TreeEdit
{
    public string Tree { get; set; } = "";
    public int Step { get; set; }
    public int Setting { get; set; }
    /// <summary>A plain field instead of a typed setting: "mode" (Serial, Parallel), "count" / "time" (Loop), "watch" /
    /// "compare" (conditions). Setting is ignored then.</summary>
    public string? Field { get; set; }
    public string Value { get; set; } = "";
    public string Expect { get; set; } = "";
    public string? Note { get; set; }
}

public sealed class GraphEdit
{
    /// <summary>"0x..." (game object) or "@Key" (this mod's new object).</summary>
    public string Object { get; set; } = "";
    /// <summary>Script name; empty = the object's only script.</summary>
    public string? Graph { get; set; }
    public string Op { get; set; } = "";
    public uint? Node { get; set; }
    public uint? Link { get; set; }
    public uint? From { get; set; }
    public uint? FromPin { get; set; }
    public uint? To { get; set; }
    public uint? ToPin { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A generic list inside an object or furniture record, rebuilt from its original entries: Order lists original entry
/// indexes (0-based; leave one out to remove it, repeat one to duplicate it). At = the list prop's offset in the game's
/// body (records: in the payload); Count = its game entry count (checked).
/// </summary>
public sealed class ListEdit
{
    public string Package { get; set; } = "";
    public string Tag { get; set; } = "";
    /// <summary>Object key (as in FieldEdit) or furniture record name.</summary>
    public string Object { get; set; } = "";
    public int At { get; set; }
    public int Count { get; set; }
    public List<int> Order { get; set; } = new();
    public string? Note { get; set; }
}

public sealed class CountedRef
{
    public string Ref { get; set; } = "";
    public uint Count { get; set; }
}

public sealed class FurnitureEdit
{
    public string Name { get; set; } = "";
    public uint Cost { get; set; }
}

/// <summary>
/// Overwrite one value anywhere in the base game data. Object = furniture record name
/// (Tag "fntr") or object id ("0x1234abcd"); Offset is relative to that record/object body.
/// Expect = the original bytes as hex, so a game update that moves things is caught.
/// </summary>
public sealed class FieldEdit
{
    public string Package { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Object { get; set; } = "";
    public int Offset { get; set; }
    /// <summary>u8, u32, i32 or f32.</summary>
    public string Type { get; set; } = "u32";
    public string Value { get; set; } = "";
    public string Expect { get; set; } = "";
    public string? Note { get; set; }

    public static int SizeOf(string type) => type == "u8" ? 1 : 4;

    /// <summary>Encode <see cref="Value"/>; "@Key" is a new object's id (from <paramref name="refs"/>).
    /// Throws FormatException/OverflowException on bad input.</summary>
    public byte[] Encode(IReadOnlyDictionary<string, uint>? refs = null)
    {
        if (Value.Trim().StartsWith('@'))
        {
            if (Type is not ("u32" or "i32")) throw new FormatException("@references need type u32");
            string key = Value.Trim()[1..];
            return refs is not null && refs.TryGetValue(key, out uint id) ? BitConverter.GetBytes(id)
                : throw new FormatException($"no new object called '{key}'");
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string v = Value.Trim();
        return Type switch
        {
            "u8" => new[] { byte.Parse(v, inv) },
            "u32" => BitConverter.GetBytes(v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.Parse(v[2..], System.Globalization.NumberStyles.HexNumber, inv) : uint.Parse(v, inv)),
            "i32" => BitConverter.GetBytes(int.Parse(v, inv)),
            "f32" => BitConverter.GetBytes(float.Parse(v, inv)),
            _ => throw new FormatException($"unknown type '{Type}' (use u8, u32, i32, f32)"),
        };
    }
}

public sealed class TextEdit
{
    public string Table { get; set; } = "";
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>A new animation clip (HCAN) under its own name, added to a package's content. The game finds clips by
/// KeyHash of the name, so something must name it: a <see cref="ClipSwap"/>, or a copy that refers to it.</summary>
public sealed class NewClip
{
    public string Name { get; set; } = "";
    /// <summary>Package whose content gets the clip (misc\packages\required\&lt;package&gt;_content.asr).</summary>
    public string Package { get; set; } = "characters";
    /// <summary>The clip (.hcan) in the mod's assets folder; the builder gives it <see cref="Name"/>.</summary>
    public string Source { get; set; } = "";
    public string? Note { get; set; }
}

/// <summary>Every reference to clip <see cref="From"/> in misc\common.asr's character anim sets (BLUE), random and
/// directional picks (CPAN), reflexes (RFLX) and animation logic (AALG) pointed at <see cref="To"/>.</summary>
public sealed class ClipSwap
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? Note { get; set; }
}

/// <summary>A character class's DefaultBT (misc\common.asr BLUE ActorBase tree): the behaviour tree its characters run.
/// Hex ids. See <see cref="Agents.WithDefaultTree"/>.</summary>
public sealed class ClassTree
{
    public string Class { get; set; } = "";
    public string Tree { get; set; } = "";
    public string? Note { get; set; }
}

/// <summary>One mod = one JSON file in the mods folder.</summary>
public sealed class ModDefinition
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public List<NewFurniture> NewFurniture { get; set; } = new();
    public List<NewObject> NewObjects { get; set; } = new();
    public List<FurnitureEdit> FurnitureEdits { get; set; } = new();
    public List<TextEdit> TextEdits { get; set; } = new();
    public List<FieldEdit> FieldEdits { get; set; } = new();
    public List<RuntimePatch> Runtime { get; set; } = new();
    /// <summary>Sounds/textures/animations/models replaced by files in the mod's "&lt;id&gt;.assets" folder.</summary>
    public List<AssetReplacement> Assets { get; set; } = new();
    /// <summary>Videos (e.g. "fmv/rebellion.webm") the game skips: a 0-byte .asrpatch the runtime DLL treats as missing.</summary>
    public List<string> SkipVideos { get; set; } = new();
    /// <summary>Spaces in the HUD's genius and henchman bar (game: 6); null = unchanged. See <see cref="HenchmanBar"/>.</summary>
    public int? HenchmanBarSlots { get; set; }
    public List<SceneryEdit> SceneryEdits { get; set; } = new();
    /// <summary>Whole new lairs: copies of a game lair's files under a new stem (MapEdits / SceneryEdits can then target it).</summary>
    public List<NewLair> NewLairs { get; set; } = new();
    /// <summary>Experimental (own lair ids, HANDOFF round 56): swap misc\common.asr's first block at run time with
    /// "hook" = its own original bytes, "recompress" = the same content re-compressed, "spare-id" = one spare test
    /// level's settings under another id. Each tells which step loses the menu art.</summary>
    public string? LairSettingsProbe { get; set; }
    /// <summary>Brand-new files (textures) placed in the mod package under a game path, e.g. "data\graphics\gui\...\x.tga".</summary>
    public List<NewAsset> NewAssets { get; set; } = new();
    public List<TreeLayout> ResearchTrees { get; set; } = new();
    /// <summary>Engineering trees (rctt) laid out like research trees: Nodes hold engineering items (rctr),
    /// Links say which item comes first (one each); ModKit routes the lines. Background = optional new picture.</summary>
    public List<TreeLayout> EngineeringTrees { get; set; } = new();
    public List<ScriptSwap> ScriptSwaps { get; set; } = new();
    public List<RequirementEdit> RequirementEdits { get; set; } = new();
    public List<TaskEdit> TaskEdits { get; set; } = new();
    public List<SchemeEdit> SchemeEdits { get; set; } = new();
    public List<PoolEdit> PoolEdits { get; set; } = new();
    /// <summary>Lair map changes (rock tiers), applied to envs\basedefinitions\*.base.</summary>
    public List<MapEdit> MapEdits { get; set; } = new();
    public List<FurnitureShapeEdit> ShapeEdits { get; set; } = new();
    public List<JobEdit> JobEdits { get; set; } = new();
    public List<ListEdit> ListEdits { get; set; } = new();
    public List<GraphEdit> GraphEdits { get; set; } = new();
    public List<TreeEdit> TreeEdits { get; set; } = new();
    public List<NewClip> NewClips { get; set; } = new();
    public List<ClipSwap> ClipSwaps { get; set; } = new();
    public List<ClassTree> ClassTrees { get; set; } = new();
    [JsonIgnore] public string? FilePath { get; set; }

    public static ModDefinition Load(string path)
    {
        var m = JsonSerializer.Deserialize<ModDefinition>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"{path}: empty mod file");
        m.FilePath = path;
        m._stamp = File.GetLastWriteTimeUtc(path);
        return m;
    }

    public void Save(string? path = null)
    {
        FilePath = path ?? FilePath ?? throw new InvalidOperationException("mod has no file path");
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        _stamp = File.GetLastWriteTimeUtc(FilePath);
    }

    DateTime? _stamp;

    /// <summary>The file was written by someone else (a text editor, a script) since this copy was loaded or saved.</summary>
    [JsonIgnore] public bool ChangedOnDisk => FilePath is not null && _stamp is not null && File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _stamp;

    public override string ToString() => $"{(string.IsNullOrEmpty(Name) ? Id : Name)}  v{Version}  [{Id}]";

    /// <summary>"1.0.9" → "1.0.10"; anything without a trailing number gets ".1".</summary>
    public static string NextVersion(string v)
    {
        var m = System.Text.RegularExpressions.Regex.Match(v.Trim(), @"^(.*?)(\d+)$");
        return m.Success ? m.Groups[1].Value + (long.Parse(m.Groups[2].Value) + 1) : (v.Trim().Length == 0 ? "1.0.0" : v.Trim() + ".1");
    }
}

/// <summary>%APPDATA%\Eg2ModKit\settings.json</summary>
public sealed class Settings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Eg2ModKit", "settings.json");

    public string GamePath { get; set; } = "";
    public string ModsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Eg2ModKit", "Mods");
    public string Language { get; set; } = "en";
    /// <summary>Plain Windows colours instead of the Evil Genius 2 look.</summary>
    public bool ClassicLook { get; set; }
    /// <summary>ModManager: show the expert pages (Browse game data, Game files) in the sidebar.</summary>
    public bool AdvancedTools { get; set; }
    /// <summary>Mod ids in load order; enabled ones in <see cref="Enabled"/>.</summary>
    public List<string> Order { get; set; } = new();
    public List<string> Enabled { get; set; } = new();

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), ModDefinition.Json) ?? new();
        }
        catch (JsonException) { }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ModDefinition.Json));
    }
}
