using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// The front end's Mods screen: component 0xdc290d93, instanced in the front end (GUAT component 0x8f3f8b29 in
/// gui\main.asr) and shown by state 0xad82ddf4 of machine 0xabc03abf. The game ships the screen and its menu
/// transitions but no button for it. <see cref="Apply"/> copies the Options button group (group, button instance,
/// highlight group, 4 images) in front of Quit, points its press at the Mods state, labels it menu/FE_MODS and copies
/// its highlight state machine. <see cref="AddInfo"/> adds a button and info page per mod to the screen.
/// </summary>
public static class ModsMenu
{
    public const string File = @"gui\main.asr";
    public const string Table = "menu";

    const uint Frontend = 0x8f3f8b29, MainMachine = 0xabc03abf, ModsState = 0xad82ddf4, OptionsState = 0x94f54ffc;
    const uint GotoState = 0x61e0d5dc, OptionsGroup = 0x44a8f327, QuitGroup = 0x33192a02, MenuLayout = 0xdae655e3;
    const uint OptionsButton = 0x0a3ece9e, OptionsMachine = 0x7b6c4361;
    const uint ComponentKey = 0x80000008, WidgetKey = 0x80000009, InstanceKey = 0x80000002, MapKey = 0x80000001;
    const string OldGraph = "F_Frontend_MainMenu_ButtonOptions", NewGraph = "F_Frontend_MainMenu_ButtonMods";
    static readonly Dictionary<uint, int> TrackValue = new() { [0x551e1c95] = 4, [0x3c8b9591] = 1, [0x154ff737] = 16 };

    /// <summary>Per-mod text keys in the menu table: the button label and the info page.</summary>
    public static string LabelKey(int i) => $"EG2MK_MOD_{i}";
    public static string InfoKey(int i) => $"EG2MK_MODINFO_{i}";

    static string Line(ModDefinition m) => string.Join(" ", new[] { m.Name ?? m.Id, m.Version, string.IsNullOrWhiteSpace(m.Author) ? null : "by " + m.Author }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>The info page: the mod's name line, a blank line, then its description, broken into lines of at most
    /// <see cref="PageChars"/> characters (the text block does not wrap).</summary>
    public static string InfoText(ModDefinition m) => string.Join("\n", (string.IsNullOrWhiteSpace(m.Description) ? Line(m)
        : Line(m) + "\n\n" + m.Description.Trim().Replace("\r\n", "\n")).Split('\n').SelectMany(Wrap).Take(MaxLines));

    /// <summary>About 19.5 units per character at text scale 1 on a 600-unit text width; 36 lines fill the panel.</summary>
    const int PageChars = 30, MaxLines = 36;

    static IEnumerable<string> Wrap(string line)
    {
        var cur = "";
        foreach (var w in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur.Length > 0 && cur.Length + 1 + w.Length > PageChars) { yield return cur; cur = ""; }
            cur = cur.Length == 0 ? w : cur + " " + w;
        }
        yield return cur;
    }

    static InvalidOperationException Changed(string what) => new($"mods menu: {what} (game updated?)");
    static uint U(byte[] b, int o) => Bytes.U32(b, o);
    static bool IsProp(byte[] b, int p, uint key) => p + 9 <= b.Length && U(b, p) == key && b[p + 4] == 0;

    record Machine(uint Name, int Start, int End, List<(uint From, uint To)> Entries);
    record Comp((byte Kind, uint Kids, uint Parent)[] Rows, (int Start, int End)[] Ents, int MachinesAt, List<Machine> Machines);
    record Graph(List<(uint Type, int Body, int Size)> Nodes, string Name, int NameAt, int NameEnd, List<uint> Ids, int MapAt);

    static List<(int Header, int Start, int End)> Components(byte[] b)
    {
        var list = new List<(int, int, int)>();
        int p = 12;
        for (uint i = 0, n = U(b, 8); i < n; i++)
        {
            if (!IsProp(b, p, ComponentKey)) throw Changed("layout list");
            int len = (int)U(b, p + 5);
            list.Add((p, p + 9, p + 9 + len));
            p += 9 + len;
        }
        if (p != b.Length) throw Changed("layout list end");
        return list;
    }

    /// <summary>Walks one component end to end: tree, entries, parameters, name map, state machines.</summary>
    static Comp Walk(byte[] b, int s, int e)
    {
        int n = (int)U(b, s + 8);
        var rows = Enumerable.Range(0, n).Select(i => (b[s + 12 + 9 * i], U(b, s + 13 + 9 * i), U(b, s + 17 + 9 * i))).ToArray();
        int q = s + 12 + 9 * n;
        var ents = new (int, int)[n];
        for (int i = 0; i < n; i++)
        {
            if (!IsProp(b, q, rows[i].Item1 == 0 ? WidgetKey : InstanceKey)) throw Changed("entry " + i);
            ents[i] = (q, q + 9 + (int)U(b, q + 5));
            q = ents[i].Item2;
        }
        uint pc = U(b, q); q += 4;
        for (uint i = 0; i < pc; i++)
        {
            if (!IsProp(b, q + 8, InstanceKey)) throw Changed("parameter");
            q += 17 + (int)U(b, q + 13);
        }
        if (!IsProp(b, q, MapKey) || U(b, q + 9) != pc) throw Changed("parameter names");
        int at = q + 9 + (int)U(b, q + 5), p = at + 4;
        var machines = new List<Machine>();
        for (uint m = 0, mc = U(b, at); m < mc; m++)
        {
            int start = p;
            if (U(b, p) != 3) throw Changed("state machine");
            uint name = U(b, p + 4), cnt = U(b, p + 8);
            p += 12;
            var entries = new List<(uint, uint)>();
            for (uint k = 0; k < cnt; k++)
            {
                if (U(b, p + 8) != 5) throw Changed("state machine entry");
                entries.Add((U(b, p), U(b, p + 4)));
                uint tracks = U(b, p + 17);
                p += 21;
                for (uint t = 0; t < tracks; t++)
                {
                    if (!TrackValue.TryGetValue(U(b, p), out int size)) throw Changed("track");
                    p += 24 + (int)U(b, p + 20) * (4 + size);
                }
            }
            p += 4;
            machines.Add(new Machine(name, start, p, entries));
        }
        if (p != e) throw Changed("component end");
        return new Comp(rows, ents, at, machines);
    }

    static Graph Fg3(byte[] b, int s)
    {
        if (U(b, s) != 5) throw Changed("button graph");
        int q = s + 8;
        var nodes = new List<(uint, int, int)>();
        for (uint i = 0, n = U(b, s + 4); i < n; i++)
        {
            int size = (int)U(b, q + 8);
            nodes.Add((U(b, q + 4), q + 12, size));
            q += 12 + size;
        }
        int z = Array.IndexOf(b, (byte)0, q);
        string name = Bytes.Latin1.GetString(b, q, z - q);
        int q2 = q + Bytes.Pad4(name.Length + 1), entry = (int)U(b, q2), q3 = q2 + 8 + 4 * entry;
        if (!IsProp(b, q3, MapKey) || U(b, q3 + 9) != nodes.Count) throw Changed("button graph map");
        var ids = Enumerable.Range(0, nodes.Count).Select(i => U(b, q3 + 13 + 8 * i)).ToList();
        return new Graph(nodes, name, q, q2, ids, q3 + 13);
    }

    /// <summary>The GUAT chunk body of gui\main.asr with a Mods button in the main menu and, on its panel, a button and info page per mod
    /// (<paramref name="lines"/>: each info page's line count).</summary>
    public static byte[] Apply(byte[] b, IReadOnlyList<int> lines)
    {
        var (hdr, s, e) = Components(b).SingleOrDefault(c => U(b, c.Start) == Frontend);
        if (s == 0) throw Changed("front end not found");
        var c = Walk(b, s, e);
        var names = c.Ents.Select(x => U(b, x.Start + 18)).ToList();
        if (names.Contains(TextTable.KeyHash($"modsbutton_{OptionsButton:x8}"))) return b;   // already there
        int g0 = names.IndexOf(OptionsGroup), quit = names.IndexOf(QuitGroup), layout = names.IndexOf(MenuLayout);
        if (g0 < 0 || quit < 0 || layout < 0 || c.Rows[g0].Parent != layout || c.Rows[quit].Parent != layout
            || names[g0 + 1] != OptionsButton || c.Rows[g0 + 1].Kind != 1)
            throw Changed("main menu");
        bool Under(int i) { for (uint k = (uint)i; k != 0xffffffff; k = c.Rows[k].Parent) if (k == g0) return true; return false; }
        int nsub = Enumerable.Range(0, names.Count).Count(Under);
        if (!Enumerable.Range(g0, nsub).All(Under)) throw Changed("options group");

        var button = c.Ents[g0 + 1];
        if (U(b, button.Start + 42) != 1) throw Changed("options button");
        var g = Fg3(b, button.Start + 46);
        var machine = c.Machines.SingleOrDefault(m => m.Name == OptionsMachine) ?? throw Changed("highlight machine");
        if (g.Name != OldGraph) throw Changed("options graph");

        // new ids: KeyHash("modsbutton_<old>") for widget names, graph node ids, the highlight machine and its states
        var old = names.Skip(g0).Take(nsub).Concat(g.Ids).Append(OptionsMachine)
            .Concat(machine.Entries.Where(x => x.From == 0).Select(x => x.To)).Distinct().ToList();
        var map = old.ToDictionary(o => o, o => TextTable.KeyHash($"modsbutton_{o:x8}"));
        if (map.Values.Distinct().Count() != map.Count || map.Values.Any(v => Bytes.Contains(b, Bytes.Le(v))))
            throw Changed("id clash");
        byte[] Remap(byte[] blob) { foreach (var (o, v) in map) blob = Bytes.Replace(blob, Bytes.Le(o), Bytes.Le(v)); return blob; }

        int a0 = c.Ents[g0].Start, z0 = c.Ents[g0 + nsub - 1].End;
        var clone = Bytes.Slice(b, a0, z0);
        var gotos = g.Nodes.Where(x => x.Type == GotoState && U(b, x.Body + x.Size - 8) == MainMachine).ToList();
        if (gotos.Count != 1 || U(b, gotos[0].Body + gotos[0].Size - 4) != OptionsState) throw Changed("options press");
        Bytes.PutU32(clone, gotos[0].Body + gotos[0].Size - 4 - a0, ModsState);
        var label = Bytes.Le(1, 0, TextTable.KeyHash(Table), TextTable.KeyHash("FE_OPTIONS"));
        int li = Bytes.Find(clone, label);
        if (li < 0 || Bytes.Find(clone, label, li + 1) >= 0) throw Changed("options label");
        Bytes.PutU32(clone, li + 12, TextTable.KeyHash("FE_MODS"));

        var newName = new byte[Bytes.Pad4(NewGraph.Length + 1)];
        Bytes.Latin1.GetBytes(NewGraph).CopyTo(newName, 0);
        int delta = newName.Length - (g.NameEnd - g.NameAt), bo = button.Start - a0;
        Bytes.PutU32(clone, bo + 5, U(clone, bo + 5) + (uint)delta);
        Bytes.PutU32(clone, bo + 14, U(clone, bo + 14) + (uint)delta);
        clone = Bytes.Concat(clone[..(g.NameAt - a0)], newName, clone[(g.NameEnd - a0)..]);
        clone = Remap(clone);
        var mblob = Remap(Bytes.Slice(b, machine.Start, machine.End));

        // tree: the copy goes in front of Quit; later parents move up by the copy's size; the layout gains a child
        var rows = c.Rows.Select(r => r with { Parent = r.Parent != 0xffffffff && r.Parent >= quit ? r.Parent + (uint)nsub : r.Parent }).ToList();
        rows[layout] = rows[layout] with { Kids = rows[layout].Kids + 1 };
        rows.InsertRange(quit, Enumerable.Range(g0, nsub).Select(i =>
            c.Rows[i] with { Parent = i == g0 ? (uint)layout : c.Rows[i].Parent - (uint)g0 + (uint)quit }));
        var table = Bytes.Concat(rows.Select(r => Bytes.Concat(new[] { r.Kind }, Bytes.Le(r.Kids, r.Parent))).ToArray());

        int n = c.Rows.Length, ents = s + 12 + 9 * n, insAt = c.Ents[quit].Start;
        var body = Bytes.Concat(Bytes.Slice(b, s, s + 8), Bytes.Le((uint)(n + nsub)), table,
            Bytes.Slice(b, ents, insAt), clone, Bytes.Slice(b, insAt, c.MachinesAt),
            Bytes.Le(U(b, c.MachinesAt) + 1), Bytes.Slice(b, c.MachinesAt + 4, e), mblob);
        var result = Bytes.Concat(b[..hdr], Bytes.Le(ComponentKey), new byte[1], Bytes.Le((uint)body.Length), body, b[e..]);

        // the new front end must walk to its end, and the copied press must name the Mods state
        var nc = Walk(result, hdr + 9, hdr + 9 + body.Length);
        var ng = Fg3(result, nc.Ents[quit + 1].Start + 46);
        if (ng.Name != NewGraph || !ng.Nodes.Any(x => x.Type == GotoState && U(result, x.Body + x.Size - 4) == ModsState))
            throw Changed("check failed");
        return AddInfo(result, lines);
    }

    /// <summary>
    /// One button and one info page per mod on the panel, and a state machine (the panel has none) switching pages:
    /// states none (initial, all pages hidden) and info_i, each holding a visibility track (0x3c8b9591 bool on
    /// 0xb78ead2a) per page group, plus a 0 s keyless transition for every ordered pair, as the game's page machines do.
    /// <list type="bullet">
    /// <item>Row: entry 19 of component 0x097fcbd5 ("Load last Save...", an instance of the 522 x 74 row button
    /// 0x4e9304f5) drawn at scale 0.75, with its graph swapped for the press then go-to-state
    /// [own component][own machine][state] graph of entry 61 of 0x8585883e, and its two links to its own component's
    /// parameters (ParamIDToToggleOnPress, isDisabled) dropped. Buttons have no size parameter; a 221-wide one
    /// squashes its label.</item>
    /// <item>Page: a copy of the panel title (group 0xda82747f: x +46, y +50, width +74, height +78; bar 0x7d94263e:
    /// width +137, height +141; text 0xc3b789bc: scale +86, key +102, width +306, height +310). The panel's own
    /// FE_MOD_CONFLICT text never shows (the game hides it), the title does.</item>
    /// </list>
    /// </summary>
    static byte[] AddInfo(byte[] b, IReadOnlyList<int> lines)
    {
        const uint Panel = 0xdc290d93, TitleGroup = 0xda82747f, Template = 0x8585883e, TemplateMachine = 0x7ce900d6;
        const uint TemplateButton = 0xe24997ff, Host = 0x097fcbd5, HostRow = 0xe8492c83, RowComponent = 0x4e9304f5;
        const uint Link = 0xed974765, Bool = 0x3c8b9591, Visible = 0xb78ead2a;
        const float Left = -550, Scale = 0.75f, RowW = 522 * Scale, RowH = 74 * Scale, Step = RowH + 8, Top = -860;
        int n = lines.Count;

        // the press graph (entry 61 of 0x8585883e) and the row (entry 19 of 0x097fcbd5)
        (int Start, int End) Entry(uint comp, uint name)
        {
            var (_, cs, ce) = Components(b).SingleOrDefault(c => U(b, c.Start) == comp);
            if (cs == 0) throw Changed($"component {comp:x8} not found");
            return Walk(b, cs, ce).Ents.FirstOrDefault(x => U(b, x.Start + 18) == name);
        }
        var (ta, _) = Entry(Template, TemplateButton);
        var (ha, hz) = Entry(Host, HostRow);
        if (ta == 0 || ha == 0) throw Changed("row template");
        var g = Fg3(b, ta + 46);
        if (!g.Nodes.Select(x => x.Type).SequenceEqual(new uint[] { 0xd6660e58, 0xdefe9eb1, GotoState })) throw Changed("press graph");
        int gEnd = g.MapAt + 8 * g.Ids.Count, go = g.Nodes[2].Body + g.Nodes[2].Size - 12 - (ta + 46);
        if (U(b, ta + 46 + go) != Template || U(b, ta + 46 + go + 4) != TemplateMachine) throw Changed("press graph target");
        var graph = Bytes.Slice(b, ta + 46, gEnd);
        int Props(int a) => a + 18 + (int)U(b, a + 14);
        if (U(b, ha + 42) != 1) throw Changed("row graph");
        var hg = Fg3(b, ha + 46);
        int hEnd = hg.MapAt + 8 * hg.Ids.Count, p7e = Props(ha);
        if (U(b, p7e) != RowComponent || hEnd + 37 != p7e) throw Changed("row component");
        var keep = new List<byte[]>();
        int q = p7e + 8;
        for (uint i = 0, cnt = U(b, p7e + 4); i < cnt; i++)
        {
            int len = 17 + (int)U(b, q + 13);
            uint last = U(b, q + len - 4);
            if (!(U(b, q + 4) == Link && last != 0 && last != 0xe4f7a6ff)) keep.Add(Bytes.Slice(b, q, q + len));
            q += len;
        }
        if (q != hz || U(b, p7e + 4) - keep.Count != 2) throw Changed("row links");
        var rowHead = Bytes.Slice(b, ha, ha + 46);
        var rowTail = Bytes.Concat(Bytes.Slice(b, hEnd, p7e + 4), Bytes.Le((uint)keep.Count), Bytes.Concat(keep.ToArray()));
        var label = Bytes.Le(1, 0, TextTable.KeyHash(Table), TextTable.KeyHash("GAMEOVER_LOAD_LAST_SAVE"));
        int lo = Bytes.Find(rowTail, label);
        if (lo < 0 || Bytes.Find(rowTail, label, lo + 1) >= 0) throw Changed("row label");

        // the panel and its title
        var (hdr, s, e) = Components(b).SingleOrDefault(c => U(b, c.Start) == Panel);
        if (s == 0) throw Changed("mods panel not found");
        var pc = Walk(b, s, e);
        if (pc.Machines.Count > 0) return b;                                    // already there
        if (pc.MachinesAt + 4 != e) throw Changed("mods panel machines");
        var names = pc.Ents.Select(x => U(b, x.Start + 18)).ToList();
        int t = names.IndexOf(TitleGroup);
        if (t < 0 || pc.Rows[t] != (0, 2, 0) || pc.Rows[t + 1].Parent != t || pc.Rows[t + 2].Parent != t) throw Changed("mods panel title");
        float F(int at) => BitConverter.ToSingle(b, at);
        var (ga, ba, xa) = (pc.Ents[t].Start, pc.Ents[t + 1].Start, pc.Ents[t + 2].Start);
        if (F(ga + 50) != -910 || F(ga + 74) != 1140 || F(ga + 78) != 80 || F(ba + 137) != 1140 || F(ba + 141) != 80
            || F(xa + 86) != 1.5f || U(b, xa + 102) != TextTable.KeyHash("FE_MODS") || F(xa + 306) != 1100 || F(xa + 310) != 80)
            throw Changed("mods panel title layout");

        var minted = new HashSet<uint>();
        uint Mint(string tag)
        {
            uint v = TextTable.KeyHash(tag);
            if (!minted.Add(v) || Bytes.Contains(b, Bytes.Le(v))) throw Changed("id clash " + tag);
            return v;
        }
        uint machine = Mint("modsinfo_machine");
        var states = new[] { Mint("modsinfo_none") }.Concat(Enumerable.Range(0, n).Select(i => Mint($"modsinfo_info_{i}"))).ToArray();

        // new rows go after root 0's subtree
        int Root(int i) { while (pc.Rows[i].Parent != 0xffffffff) i = (int)pc.Rows[i].Parent; return i; }
        int cnt0 = pc.Rows.Length, ins = Enumerable.Range(0, cnt0).Last(i => Root(i) == 0) + 1;
        const float pageL = -130, pageW = 640;                                  // clear of the scrollbar (x 530 to 570)
        var newRows = new List<(byte Kind, uint Kids, uint Parent)>();
        var data = new List<byte[]>();
        var pages = new List<uint>();
        for (int i = 0; i < n; i++)
        {
            var press = (byte[])graph.Clone();
            Bytes.Le(Panel, machine, states[1 + i]).CopyTo(press, go);
            for (int k = 0; k < g.Ids.Count; k++)
                Bytes.PutU32(press, g.MapAt - (ta + 46) + 8 * k, Mint($"modsinfo_{i}_{g.Ids[k]:x8}"));
            var tail = (byte[])rowTail.Clone();
            Bytes.PutU32(tail, lo + 12, TextTable.KeyHash(LabelKey(i)));
            var btn = Bytes.Concat(rowHead, press, tail);
            int p7len = 46 - 18 + press.Length + (p7e + 4 - hEnd) - 4;
            Bytes.PutU32(btn, 5, (uint)(btn.Length - 9));
            Bytes.PutU32(btn, 14, (uint)p7len);
            Bytes.PutU32(btn, 18, Mint($"modsinfo_btn_{i}"));
            int tf = 18 + p7len - 24;
            foreach (var (k, v) in new[] { (0, Left + RowW / 2), (4, Top + RowH / 2 + i * Step), (8, Scale), (12, Scale) })
                BitConverter.GetBytes(v).CopyTo(btn, tf + k);
            newRows.Add((1, 0, 0)); data.Add(btn);

            float h = 20 + 45 * Math.Max(lines[i], 1);
            var page = Bytes.Slice(b, ga, pc.Ents[t + 2].End);
            void Put(int at, float v) => BitConverter.GetBytes(v).CopyTo(page, at - ga);
            Put(ga + 46, pageL + pageW / 2); Put(ga + 50, Top + h / 2); Put(ga + 74, pageW); Put(ga + 78, h);
            Put(ba + 137, pageW); Put(ba + 141, h);
            Put(xa + 86, 1); Put(xa + 306, pageW - 40); Put(xa + 310, h);
            Bytes.PutU32(page, xa + 102 - ga, TextTable.KeyHash(InfoKey(i)));
            foreach (var (a, k) in new[] { (ga, 0), (ba, 1), (xa, 2) })
                Bytes.PutU32(page, a + 18 - ga, Mint($"modsinfo_{i}_{names[t + k]:x8}"));
            pages.Add(U(page, 18));
            uint group = (uint)(ins + newRows.Count);
            newRows.AddRange(new (byte, uint, uint)[] { (0, 2, 0), (0, 0, group), (0, 0, group) });
            data.Add(page);
        }

        // the machine: state definitions set each page's visibility at time 0, transitions carry keyless tracks
        byte[] Track(uint w, int? key) => key is int v
            ? Bytes.Concat(Bytes.Le(Bool, w, Visible, 1, 1, 1), BitConverter.GetBytes(0f), new[] { (byte)v })
            : Bytes.Le(Bool, w, Visible, 1, 1, 0);
        byte[] MEntry(uint from, uint to, Func<int, int?> key) => Bytes.Concat(Bytes.Le(from, to, 5), BitConverter.GetBytes(0f),
            new byte[1], Bytes.Le((uint)n), Bytes.Concat(Enumerable.Range(0, n).Select(j => Track(pages[j], key(j))).ToArray()));
        var entries = states.Select((st, si) => MEntry(0, st, j => si == j + 1 ? 1 : 0))
            .Concat(states.SelectMany(f => states.Where(x => x != f).Select(x => MEntry(f, x, _ => null)))).ToArray();
        var mblob = Bytes.Concat(Bytes.Le(3, machine, (uint)entries.Length), Bytes.Concat(entries), Bytes.Le(states[0]));

        int k4 = newRows.Count;
        var rows = pc.Rows.Select(r => r with { Parent = r.Parent != 0xffffffff && r.Parent >= ins ? r.Parent + (uint)k4 : r.Parent }).ToList();
        rows[0] = rows[0] with { Kids = rows[0].Kids + (uint)(2 * n) };
        rows.InsertRange(ins, newRows);
        var table = Bytes.Concat(rows.Select(r => Bytes.Concat(new[] { r.Kind }, Bytes.Le(r.Kids, r.Parent))).ToArray());
        int insAt = ins < cnt0 ? pc.Ents[ins].Start : pc.Ents[^1].End;
        var body = Bytes.Concat(Bytes.Slice(b, s, s + 8), Bytes.Le((uint)(cnt0 + k4)), table,
            Bytes.Slice(b, s + 12 + 9 * cnt0, insAt), Bytes.Concat(data.ToArray()), Bytes.Slice(b, insAt, pc.MachinesAt),
            Bytes.Le(1), mblob);
        var result = Bytes.Concat(b[..hdr], Bytes.Le(ComponentKey), new byte[1], Bytes.Le((uint)body.Length), body, b[e..]);
        var check = Walk(result, hdr + 9, hdr + 9 + body.Length);
        if (check.Machines.Count != 1 || check.Machines[0].Entries.Count != (n + 1) * (n + 1)) throw Changed("check failed");
        return result;
    }
}
