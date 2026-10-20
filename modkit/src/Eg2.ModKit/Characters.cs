using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>Every class-tree object in misc\common.asr (all BLUE chunks) by id: characters, abilities, looks…
/// An id in two chunks resolves to the first, as <see cref="Blueprints"/> edits do at build time.</summary>
public sealed class ClassIndex
{
    public sealed record Entry(Blueprints.Obj Obj, byte[] Body);

    public static readonly ClassIndex Empty = new();
    readonly Dictionary<uint, Entry> _byId = new();
    readonly Dictionary<uint, List<Blueprints.Obj>> _children = new();

    public static ClassIndex Load(GameInstall install) => Load(AsuraArchive.Load(install.Full(@"misc\common.asr")).Chunks);

    public static ClassIndex Load(IEnumerable<Chunk> commonChunks)
    {
        var t = new ClassIndex();
        foreach (var c in commonChunks.Where(c => c.Tag == "BLUE"))
            foreach (var o in Blueprints.Parse(c.Body) ?? new())
                if (t._byId.TryAdd(o.Id, new Entry(o, c.Body)))
                {
                    if (!t._children.TryGetValue(o.Parent, out var l)) t._children[o.Parent] = l = new();
                    l.Add(o);
                }
        return t;
    }

    public Entry? this[uint id] => _byId.GetValueOrDefault(id);
    public IReadOnlyList<Blueprints.Obj> Children(uint id) => _children.TryGetValue(id, out var l) ? l : [];

    /// <summary>A member as the object sees it: its own, else the nearest parent's.</summary>
    public (Blueprints.Member Member, Entry Owner)? Find(uint id, uint key)
    {
        for (int depth = 0; depth < 64 && this[id] is { } e; depth++, id = e.Obj.Parent)
            if (e.Obj.Members.FirstOrDefault(m => m.Key == key) is { } m) return (m, e);
        return null;
    }

    public static uint U32(Entry e, Blueprints.Member m) => Bytes.U32(e.Body, m.At);
}

/// <summary>
/// The Simple-mode character pages (Henchmen, Geniuses, Minions, Agents) as <see cref="Tweak"/>s, one group per class in
/// the class tree: names and texts (text edits), numbers, ability buttons and look (class-tree edits). Only members a
/// class sets itself can change (the file is edited in place); inherited ones are listed in the help.
/// Member names: Name/Class/ClassRank/Type/Salary are KeyHash matches; the rest are read from what they hold.
/// </summary>
public static class Characters
{
    public const uint Henchmen = 0xd3a8b2e0, Geniuses = 0x6c221265, Minions = 0x3f7396bd, Agents = 0x05874305;

    const uint NameKey = 0x00337a8b, FullName = 0xe4245df5, DisplayName = 0x92cc512a, Look = 0x787c09f1, GeniusName = 0xf43aaf5e;
    static readonly uint[] Slots = { 0x020a9dc2, 0x020a9dc3, 0x020a9dc4 };
    const uint AbilityText = 0x00337a8b, AbilityKind = 0xd7bbdcc5;

    static readonly Dictionary<uint, (string Section, string Label)> Known = new()
    {
        [FullName] = ("Name", "Full name"),
        [DisplayName] = ("Name", "Name"),
        [0x6c92ba03] = ("Name", "Name (second copy)"),
        [0xe2198347] = ("Name", "Plural"),
        [0x576a902f] = ("Name", "Description"),
        [0x4bb44037] = ("Name", "Flavour line"),
        [0xc9c6c9ca] = ("Pay", "Salary (not confirmed in game)"),
        [0x05a5a978] = ("Class", "Class"),
        [0xff7979a4] = ("Class", "Class rank"),
        [0x00368f3a] = ("Class", "Type"),
        [0xe5369538] = ("Base stats (meaning not confirmed)", "Stat A"),
        [0x455376e0] = ("Base stats (meaning not confirmed)", "Stat B"),
        [0x0b559482] = ("Base stats (meaning not confirmed)", "Stat C"),
        [0x6a6f31a2] = ("Base stats (meaning not confirmed)", "Stat D"),
        [0x75f97b52] = ("Base stats (meaning not confirmed)", "Stat E"),
    };

    /// <summary>Ability numbers (memory notes: 300, -80, 30, 3 on Flow): guesses, so marked with "?".</summary>
    static readonly Dictionary<uint, string> AbilityNumbers = new()
    {
        [0x56b1acb8] = "Cooldown?", [0xf83acb40] = "Cost?", [0x7b9a5198] = "Cost?", [0x79e2ded3] = "Duration?", [0x8082c12d] = "Charges?",
    };

    static readonly Dictionary<uint, string> RootLabel = new()
    {
        [Henchmen] = "★ All henchmen", [Geniuses] = "★ All geniuses", [Minions] = "★ All minions", [Agents] = "★ All agents",
    };

    public static List<Tweak> Page(GameData game, uint root)
    {
        var tree = game.Classes;
        if (tree[root] is null) return new();
        var classes = new List<Blueprints.Obj>();
        void Walk(Blueprints.Obj o, int depth)
        {
            classes.Add(o);
            // other chunks reuse ids as parents for unrelated objects (messages, settings): same chunk only
            if (depth < 8) foreach (var k in tree.Children(o.Id).Where(k => tree[k.Id]!.Body == tree[root]!.Body)) Walk(k, depth + 1);
        }
        Walk(tree[root]!.Obj, 0);

        // every character's name, so duplicates get their parent's
        var names = new Dictionary<uint, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in classes)
        {
            string n = o.Id == root ? RootLabel.GetValueOrDefault(root, "★ Shared values") : Name(game, o.Id);
            if (!taken.Add(n))
            {
                string Internal() => o.Members.FirstOrDefault(x => x.Key == NameKey && x.Kind == 4) is { } m ? Blueprints.Read(tree[o.Id]!.Body, m) : "";
                string tag = new[] { Internal(), Name(game, o.Parent) }.FirstOrDefault(x => x.Length > 0 && !x.Equals(n, StringComparison.OrdinalIgnoreCase)) ?? Bytes.Hex(o.Id);
                string plain = n;
                n = $"{plain} ({tag})";
                for (int i = 2; !taken.Add(n); i++) n = $"{plain} ({tag} {i})";
            }
            names[o.Id] = n;
        }

        var abilities = Choices(game, Slots, a => AbilityName(game, a));
        var looks = LookChoices(game, Choices(game, [Look], _ => ""));
        var list = new List<Tweak>();
        foreach (var o in classes)
        {
            string group = names[o.Id];
            string help = Inherited(game, o, names);
            var e = tree[o.Id]!;
            var rows = new List<(int Order, Tweak T)>();
            foreach (var m in e.Obj.Members)
            {
                if (m.Kind == 4) continue;   // strings would change the file's size
                uint v = m.Kind is 0 or 3 ? ClassIndex.U32(e, m) : 0;
                int slot = Array.IndexOf(Slots, m.Key);
                if (slot >= 0)
                {
                    rows.Add((10 + slot * 10, Blue(group, o.Id, m, e, $"Ability {slot + 1}", help, abilities)));
                    if (v != 0) rows.AddRange(AbilityRows(game, group, v, slot, help).Select(t => (11 + slot * 10, t)));
                    continue;
                }
                if (m.Key == Look)
                {
                    rows.Add((50, Blue(group, o.Id, m, e, "Look: model and animations (untested in game)", help, looks)));
                    continue;
                }
                var known = Known.GetValueOrDefault(m.Key);
                if (m.Kind == 3 && TextOf(game, v) is { } text)
                {
                    rows.Add((known.Section == "Name" ? 0 : 60, Text(group, text, $"{known.Section ?? "Text"}: {known.Label ?? Bytes.Hex(m.Key)}")));
                    continue;
                }
                if (known.Section is not null) rows.Add((known.Section == "Pay" ? 1 : known.Section == "Class" ? 2 : 3, Blue(group, o.Id, m, e, $"{known.Section}: {known.Label}", help)));
                else rows.Add((90, Blue(group, o.Id, m, e, $"Not decoded yet: {Bytes.Hex(m.Key)}{(m.Kind == 3 ? " (link)" : "")}", help)));
            }
            if (root == Geniuses && o.Id != root) rows.AddRange(FrontEnd(game, group).Select(t => (5, t)));
            rows.AddRange(VoiceRows(game, group, VoiceFolders(game, group, o.Id == root && root == Minions)).Select(t => (95, t)));
            var sounds = LookSounds(game, o.Id);
            foreach (var (_, t) in rows) t.SoundFiles = sounds.Count > 0 ? sounds : null;
            list.AddRange(rows.OrderBy(r => r.Order).Select(r => r.T).DistinctBy(t => t.Key));   // one text can fill two members
        }
        return list;
    }

    /// <summary>
    /// Combat and body sounds: the look's animation set (3d480e42, rcan) and models (6b17bc64/1663cc0a, rmcc), and objects
    /// they link to (two steps), mention sound sets by KeyHash(name): attacks after the animation, foley after the body model.
    /// </summary>
    public static SortedSet<string> LookSounds(GameData game, uint character)
    {
        var tree = game.Classes;
        var sets = new HashSet<uint>();
        if (tree.Find(character, Look) is not var (lm, lo)) return game.Sounds.Files(sets);
        uint look = ClassIndex.U32(lo, lm);
        var queue = new Queue<(uint Id, int Depth)>();
        foreach (var key in new uint[] { 0x3d480e42, 0x6b17bc64, 0x1663cc0a })
            if (tree.Find(look, key) is var (m, owner)) queue.Enqueue((ClassIndex.U32(owner, m), 0));
        var seen = new HashSet<uint>();
        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            if (!seen.Add(id) || !game.ObjectsById.TryGetValue(id, out var objects)) continue;
            foreach (var ob in objects)
                for (int i = 0; i + 4 <= ob.Body.Length; i++)   // set hashes aren't always 4-aligned
                {
                    uint v = Bytes.U32(ob.Body, i);
                    if (game.Sounds.Sets.ContainsKey(v)) sets.Add(v);
                    else if (depth < 2 && i % 4 == 0 && v != id && game.ObjectsById.ContainsKey(v)) queue.Enqueue((v, depth + 1));
                }
        }
        return game.Sounds.Files(sets);
    }

    /// <summary>Its own name text, else its internal name, else a name text it inherits.</summary>
    static string Name(GameData game, uint id)
    {
        var tree = game.Classes;
        string? Text(bool own)
        {
            foreach (var key in new[] { FullName, DisplayName, 0x6c92ba03u })
                if (tree.Find(id, key) is ({ Kind: 3 } m, var owner) && (owner.Obj.Id == id) == own
                    && TextOf(game, ClassIndex.U32(owner, m)) is { } t && t.Text.Trim() is { Length: > 0 } s && s != "None")
                    return s;
            return null;
        }
        if (Text(own: true) is { } mine) return mine;
        foreach (var key in new[] { NameKey, GeniusName })
            if (tree[id]?.Obj.Members.FirstOrDefault(x => x.Key == key && x.Kind == 4) is { } m && Blueprints.Read(tree[id]!.Body, m) is { Length: > 0 } s)
                return s;
        return Text(own: false) ?? Bytes.Hex(id);
    }

    /// <summary>Game text behind a hash, unless it's an icon code (no letters) or the hash isn't text.</summary>
    internal static TextInfo? TextOf(GameData game, uint hash) =>
        hash != 0 && game.Text.TryGetValue(hash, out var t) && t.Text.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ? t : null;

    static string AbilityName(GameData game, uint id, bool kind = true)
    {
        var tree = game.Classes;
        if (tree[id] is not { } e) return Bytes.Hex(id);
        string name = tree.Find(id, AbilityText) is ({ Kind: 3 } m, var owner) && TextOf(game, ClassIndex.U32(owner, m)) is { } t
            ? ObjectInspector.Clean(t.Text).Trim() : Bytes.Hex(id);
        string what = kind && tree.Find(id, AbilityKind) is ({ Kind: 3 } k, var ko) && TextOf(game, ClassIndex.U32(ko, k)) is { } kt ? kt.Text : "";
        return what.Length > 0 ? $"{name} ({what})" : name;
    }

    /// <summary>Every value any class gives these members, for a drop-down.</summary>
    static List<(string Value, string Label)> Choices(GameData game, uint[] keys, Func<uint, string> label)
    {
        var tree = game.Classes;
        var seen = new SortedDictionary<uint, string>();
        foreach (var root in new[] { Henchmen, Geniuses, Minions, Agents })
        {
            var stack = new Stack<uint>([root]);
            for (int guard = 0; stack.Count > 0 && guard < 5000; guard++)
            {
                uint id = stack.Pop();
                if (tree[id] is not { } e) continue;
                foreach (var m in e.Obj.Members.Where(m => m.Kind == 3 && keys.Contains(m.Key)))
                    if (ClassIndex.U32(e, m) is var v && !seen.ContainsKey(v)) seen[v] = v == 0 ? "(none)" : label(v);
                foreach (var k in tree.Children(id)) stack.Push(k.Id);
            }
        }
        return seen.Select(x => (Bytes.Hex(x.Key), x.Value)).OrderBy(x => x.Value == "(none)" ? "" : x.Value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Looks are named after the first character that wears them.</summary>
    static List<(string Value, string Label)> LookChoices(GameData game, List<(string Value, string Label)> looks)
    {
        var tree = game.Classes;
        var wearer = new Dictionary<string, string>();
        foreach (var root in new[] { Henchmen, Geniuses, Minions, Agents })
        {
            var stack = new Stack<uint>([root]);
            for (int guard = 0; stack.Count > 0 && guard < 5000; guard++)
            {
                uint id = stack.Pop();
                if (tree[id] is not { } e) continue;
                if (e.Obj.Members.FirstOrDefault(m => m.Key == Look && m.Kind == 3) is { } m) wearer.TryAdd(Bytes.Hex(ClassIndex.U32(e, m)), Name(game, id));
                foreach (var k in tree.Children(id)) stack.Push(k.Id);
            }
        }
        return looks.Select(l => (l.Value, l.Label == "(none)" ? l.Label : $"{wearer.GetValueOrDefault(l.Value, l.Value)}'s look"))
            .OrderBy(l => l.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<Tweak> AbilityRows(GameData game, string group, uint ability, int slot, string help)
    {
        if (game.Classes[ability] is not { } e) yield break;
        string prefix = $"Ability {slot + 1} ({AbilityName(game, ability, kind: false)})";
        foreach (var m in e.Obj.Members)
        {
            if (m.Kind == 3 && TextOf(game, ClassIndex.U32(e, m)) is { } t)
                yield return Text(group, t, $"{prefix}: {(m.Key == AbilityText ? "name" : m.Key == 0x993583fc ? "description" : Bytes.Hex(m.Key))}");
            else if (m.Kind is 0 or 1 or 2)
                yield return Blue(group, ability, m, e, $"{prefix}: {AbilityNumbers.GetValueOrDefault(m.Key, $"not decoded yet ({Bytes.Hex(m.Key)})")}", help);
        }
    }

    /// <summary>A genius's new-game screen texts (fegd), matched by name.</summary>
    static IEnumerable<Tweak> FrontEnd(GameData game, string name)
    {
        var o = game.Objects.FirstOrDefault(x => x.Tag == "fegd" && x.Name.Trim('"') is var n && n.Length > 0
                                                 && (n.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(n, StringComparison.OrdinalIgnoreCase)));
        if (o is null) yield break;
        foreach (var r in ObjectInspector.FriendlyRows(o, game))
            if (r.Offset >= 0 && r.Offset <= o.Body.Length - 4 && TextOf(game, Bytes.U32(o.Body, r.Offset)) is { } t)
                yield return Text(name, t, $"New-game screen: {r.Path}");
    }

    /// <summary>
    /// Speaker folders of a character's recorded lines (Data/Dialogue/Sounds/en/&lt;folder&gt;), matched on its shown name:
    /// Eli_Barracuda = "Eli Barracuda Jnr.", Max = "Maximilian", IRIS_EventLog = "IRIS", Magnolia_Ming = "Dr. Magnolia Ming".
    /// Minion types share voices (Minions_And_Bit_Parts_…), so those go on ★ All minions.
    /// </summary>
    public static List<string> VoiceFolders(GameData game, string name, bool minionRoot)
    {
        if (minionRoot) return game.Voices.Keys.Where(f => f.StartsWith("Minions", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        string n = Norm(name.Split(" (")[0]);
        if (n.Length < 3) return new();
        return game.Voices.Keys.Where(f => VoiceOf(f, n)).OrderBy(f => Norm(f) == n ? 0 : 1).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static readonly string[] Titles = { "dr", "prof", "sir", "ms", "mr", "mrs" }, Generations = { "jnr", "snr", "jr", "sr" };

    /// <summary>Folder f belongs to the (normalised) name n: same name; the name plus Jnr/Snr ("Eli Barracuda Jnr."); a title
    /// plus the name ("Dr. Magnolia Ming"); the name plus a variant part (IRIS_EventLog, but not John_Steele_Snr for John Steele).</summary>
    static bool VoiceOf(string f, string n)
    {
        string v = Norm(f);
        if (v == n || v == "max" && n == "maximilian") return true;
        if (n.StartsWith(v) && Generations.Contains(n[v.Length..])) return true;
        if (n.EndsWith(v) && Titles.Contains(n[..^v.Length])) return true;
        var parts = f.Split('_');
        for (int k = 1; k < parts.Length; k++)
            if (Norm(string.Concat(parts.Take(k))) == n)
                return !Generations.Contains(Norm(string.Concat(parts.Skip(k))));
        return false;
    }

    static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Subtitles of a character's recorded lines (text keyed by the WAV's name), each pointing Game files at the folder.</summary>
    static IEnumerable<Tweak> VoiceRows(GameData game, string group, List<string> folders)
    {
        if (folders.Count == 0) yield break;
        var lines = folders.SelectMany(f => game.Voices[f]).ToList();
        string search = "/en/" + folders[0], help = $"Voice: {lines.Count} recorded lines ({string.Join(", ", folders)}). " +
            "\"Hear or replace voice…\" opens them in Game files to listen or replace. Subtitle edits leave the recording as it is.";
        foreach (var path in lines)
        {
            string line = Path.GetFileNameWithoutExtension(path);
            if (game.Text.GetValueOrDefault(Eg2.Asura.Chunks.TextTable.KeyHash(line)) is not { } t) continue;
            string label = line.StartsWith("EG2_", StringComparison.OrdinalIgnoreCase) ? line[4..] : line;
            yield return new Tweak
            {
                Key = $"text:{t.Table}:{t.Key}", Group = group, Label = $"Voice line: {label}", Default = t.Text, Help = help,
                Text = new TextEdit { Table = t.Table, Key = t.Key, Text = t.Text }, AssetSearch = search,
            };
        }
    }

    /// <summary>One line naming what a class inherits (so its row is missing): change those on the entry that sets them.</summary>
    static string Inherited(GameData game, Blueprints.Obj o, Dictionary<uint, string> names)
    {
        var tree = game.Classes;
        var parts = new List<string>();
        foreach (var (key, (section, label)) in Known.Where(k => k.Value.Section is not "Name"))
            if (o.Members.All(m => m.Key != key) && tree.Find(o.Parent, key) is var (m, owner))
                parts.Add($"{label.Replace(" (not confirmed in game)", "")} {Blueprints.Read(owner.Body, m)} (from {names.GetValueOrDefault(owner.Obj.Id, Name(game, owner.Obj.Id))})");
        return parts.Count == 0 ? "" : "Inherited, change on the entry it comes from: " + string.Join(", ", parts) + ".";
    }

    static Tweak Blue(string group, uint obj, Blueprints.Member m, ClassIndex.Entry e, string label, string help, List<(string Value, string Label)>? choices = null) => new()
    {
        Key = $"blue:{obj:x8}:{m.Key:x8}", Group = group, Label = label, Help = help,
        Default = Blueprints.Read(e.Body, m),
        Blue = new BlueEdit { Object = Bytes.Hex(obj), Member = Bytes.Hex(m.Key), Value = Blueprints.Read(e.Body, m) },
        BlueType = m.Kind switch { 0 => "u32", 1 => "f32", 2 => "u8", _ => "hash" },
        Choices = choices,
    };

    internal static Tweak Text(string group, TextInfo t, string label) => new()
    {
        Key = $"text:{t.Table}:{t.Key}", Group = group, Label = label, Default = t.Text,
        Help = "Texts change everywhere the game shows them.",
        Text = new TextEdit { Table = t.Table, Key = t.Key, Text = t.Text },
    };
}
