using Eg2.Asura.Chunks;
using Eg2.Asura;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Lair maps tab: a lair's floors drawn cell by cell; drag a rectangle and set its rock tier (saved as MapEdits).</summary>
sealed partial class MainForm
{
    const string MapDir = @"envs\basedefinitions";
    readonly ListBox _lairList = new() { Dock = DockStyle.Top, Height = 110, IntegralHeight = false, FormattingEnabled = true };
    readonly ListBox _floorList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly TreeCanvas _mapCanvas = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Black };
    readonly Label _mapInfo = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = Theme.Muted };
    TabPage? _mapsPage;
    LairMap? _map;              // the chosen lair with the target mod's edits applied
    string? _mapFile;           // "envs\basedefinitions\lair_....base"
    Bitmap? _floorImage;
    int _zoom = 3;
    Point? _dragFrom;
    Rectangle? _mapSel;         // in cells, inclusive
    readonly List<(ModDefinition Mod, List<MapEdit> Edits)> _mapRedo = new();   // a pasted stamp is one entry

    static readonly (int Tier, Color Colour, string Name)[] TierLook =
    {
        (1, Color.FromArgb(214, 196, 150), "1 (easiest)"), (2, Color.FromArgb(206, 120, 60), "2"),
        (3, Color.FromArgb(150, 50, 40), "3"), (4, Color.FromArgb(70, 60, 130), "4 (hardest)"),
    };

    TabPage MapsPage()
    {
        var page = new TabPage("Lair maps");
        var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(4) };
        left.Controls.Add(_floorList);
        left.Controls.Add(_lairList);
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_mapCanvas);
        right.Controls.Add(Tools(
            ("Rock", TierLook.Select(t => (Control)Btn($"Tier {t.Name}", (_, _) => SetMapTier(t.Tier)))
                .Append(Btn("Fill with rock", (_, _) => AddMapEdit(new MapEdit { Action = "rock", Tier = 1 }, needsArea: true)))
                .Append(Btn("Edge rock", (_, _) => AddMapEdit(new MapEdit { Action = "wall" }, needsArea: true)))
                .Append(Btn("Gold seam", (_, _) => AddMapEdit(new MapEdit { Action = "gold" }, needsArea: true))).ToArray()),
            ("Dig and build", new Control[] { Btn("Dig out", (_, _) => DigMap()), Btn("Build room…", (_, _) => BuildMapRoom()),
                Btn("Place object…", (_, _) => PlaceMapObject()), Btn("Place agent…", (_, _) => PlaceMapAgent()), Btn("Remove objects", (_, _) => AddMapEdit(new MapEdit { Action = "remove" }, needsArea: true)) }),
            ("Stamps", new Control[] { Btn("Save stamp…", (_, _) => SaveStamp()), Btn("Paste stamp…", (_, _) => PasteStamp()) }),
            ("History", new Control[] { Btn("Undo", (_, _) => UndoMapEdit()), Btn("Redo", (_, _) => RedoMapEdit()), Btn("Reset lair", (_, _) => ResetMap()) }),
            ("Zoom", new Control[] { Btn("−", (_, _) => { _zoom = Math.Max(1, _zoom - 1); MapResized(); }), Btn("+", (_, _) => { _zoom = Math.Min(12, _zoom + 1); MapResized(); }), _mapInfo })));
        var mapHint = Hint("Reshape the underground of an island for new games: drag a rectangle on the map, then press a button. Buttons: a rock tier (1 easy round the start, 4 hardest), Dig out (corridor), Build room… (dig out as a room, or change a room), " +
                           "Gold seam / Edge rock (edge rock can't be dug: walls), Fill with rock (grow the lair; underground only, the island hides it on the surface), Remove objects, Save stamp (keep an area to Paste elsewhere or in another lair). " +
                           "Click a cell and Place object…: only furniture made for the room under it is offered. Rock = tier colours, black = outside, cyan = lift, brown = edge, " +
                           "yellow spots = gold, rooms in their own colours (hover for names), white squares = objects, red dots = enemy agents. " +
                           "Place agent… (experimental) puts an enemy agent there (they act like a normal raid). Applied with the other mods by the ModKit runtime DLL; only NEW games use it.");
        right.Controls.Add(mapHint);
        page.Controls.Add(right);
        page.Controls.Add(new Splitter { Dock = DockStyle.Left });
        page.Controls.Add(left);
        _lairList.Format += (_, e) => { if (e.ListItem is string f) e.Value = LairName(f); };
        _lairList.SelectedIndexChanged += (_, _) => { if (_lairList.SelectedItem is string f) OpenLair(f); };
        _floorList.SelectedIndexChanged += (_, _) => DrawFloor();
        _mapCanvas.Paint += PaintMap;
        _mapCanvas.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && MapCell(e.Location) is { } c) { _dragFrom = c; _mapSel = new Rectangle(c, new Size(1, 1)); _mapCanvas.Invalidate(); } };
        _mapCanvas.MouseMove += (_, e) =>
        {
            if (MapCell(e.Location) is not { } c) return;
            if (_dragFrom is { } a && e.Button == MouseButtons.Left)
            {
                _mapSel = Rectangle.FromLTRB(Math.Min(a.X, c.X), Math.Min(a.Y, c.Y), Math.Max(a.X, c.X) + 1, Math.Max(a.Y, c.Y) + 1);
                _mapCanvas.Invalidate();
            }
            ShowMapInfo(c);
        };
        _mapCanvas.MouseUp += (_, _) => _dragFrom = null;
        return page;
    }

    /// <summary>The island's in-game name (menu text LAIR_&lt;NAME&gt;_DEFAULT_NAME: Crown Gold, Icicle Point...).</summary>
    string LairName(string file)
    {
        string key = Path.GetFileNameWithoutExtension(file).ToUpperInvariant() + "_NAME";
        if (_game is null) return Path.GetFileNameWithoutExtension(file);
        // the Oceans table's copy of the arctic key is a "Moved to MENU page." placeholder, and it shadows the menu table's
        _menuText ??= _game.Install.TextFiles(_game.Language).Where(t => t.Table.Equals("menu", StringComparison.OrdinalIgnoreCase))
            .SelectMany(t => AsuraArchive.Load(t.Path).Find("HTXT")).SelectMany(c => TextTable.Parse(c.Body).Entries)
            .Where(e => e.Key is not null).GroupBy(e => e.Key!).ToDictionary(g => g.Key, g => g.First().Text);
        if (_menuText.TryGetValue(key, out var name)) return ObjectInspector.Clean(name);
        // an island a mod added names itself in that mod's text
        return ModsInList().SelectMany(m => m.TextEdits).FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Text
               ?? Path.GetFileNameWithoutExtension(file);
    }

    Dictionary<string, string>? _menuText;

    void LoadMaps()
    {
        if (_game is null || _lairList.Items.Count > 0) return;
        var dir = _game.Install.Full(MapDir);
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "lair_*.base").Order()) _lairList.Items.Add(Path.Combine(MapDir, Path.GetFileName(f)));
        foreach (var f in ModsInList().SelectMany(m => m.NewLairs).Select(n => Path.Combine(MapDir, n.Stem + ".base")))
            if (!_lairList.Items.Contains(f)) _lairList.Items.Add(f);
        if (_lairList.Items.Count > 0) _lairList.SelectedIndex = 0;
    }

    IEnumerable<MapEdit> EditsFor(ModDefinition? m, string file) =>
        m?.MapEdits.Where(e => e.File.Replace('/', '\\').Equals(file, StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<MapEdit>();

    void OpenLair(string file)
    {
        if (_game is null) return;
        try
        {
            // a new lair (a mod's NewLairs) is edited on the map it copies
            var n = ModsInList().SelectMany(m => m.NewLairs).FirstOrDefault(n => n.Stem.Equals(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase));
            string src = n is null || File.Exists(_game.Install.Full(file)) && !IsOurs(file) ? file : Path.Combine(MapDir, n.From + ".base");
            _map = LairMap.Parse(File.ReadAllBytes(_game.Install.Full(src)));
            _mapFile = file;
            foreach (var e in EditsFor(_current, file)) Apply(e);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException) { Warn($"{LairName(file)}: {e.Message}"); _map = null; }
        int keep = Math.Max(0, _floorList.SelectedIndex);
        _floorList.Items.Clear();
        if (_map is null) return;
        foreach (var f in _map.Floors)
        {
            var tiers = TierLook.Select(t => f.Cells.Count(c => LairMap.TierOf(c) == t.Tier));
            _floorList.Items.Add($"Floor {f.Index} ({f.Width}x{f.Height}){(tiers.Sum() == 0 ? " — no rock" : "")}");
        }
        // the first floor with the lift and rock (where the game starts) unless one was picked
        int start = _map.Floors.FindIndex(f => f.Cells.Any(c => LairMap.TypeOf(c) == 0x080b0101) && f.Cells.Any(c => LairMap.TierOf(c) > 0));
        _floorList.SelectedIndex = keep > 0 && keep < _floorList.Items.Count ? keep : Math.Max(0, start);
    }

    void Apply(MapEdit e) { try { ModBuilder.ApplyMapEdit(_game!, _map!, e); } catch (ArgumentException x) { Log($"map change skipped: {x.Message}"); } }

    LairMap.Floor? MapFloor => _map is { } m && _floorList.SelectedIndex >= 0 && _floorList.SelectedIndex < m.Floors.Count ? m.Floors[_floorList.SelectedIndex] : null;

    static Color CellColour(Prop cell)
    {
        uint type = LairMap.TypeOf(cell), flags = LairMap.FlagsOf(cell);
        if (LairMap.TierOf(cell) is > 0 and var tier) return TierLook[tier - 1].Colour;
        if (type == 0x430bd860) return Color.FromArgb(18, 18, 22);             // outside the lair
        if (type == 0x080b0101) return Color.DarkCyan;                         // lift shaft
        if (type == 0x0cf2f3d8) return Color.FromArgb(120, 95, 40);            // the edge round the lair (probably undiggable)
        if (type == 0x48f218d8) return Color.Gold;                             // small spots (deposits?)
        int room = Array.FindIndex(LairMap.Rooms, r => r.Type == type);
        if (room >= 0) return RoomColours[room];
        if ((flags & 8) != 0) return Color.FromArgb(150, 150, 150);            // other built floor (hotel exterior...)
        return Color.FromArgb(70, 90, 70);
    }

    /// <summary>One colour per <see cref="LairMap.Rooms"/> entry (corridor light grey).</summary>
    static readonly Color[] RoomColours =
    {
        Color.FromArgb(170, 170, 170), Color.FromArgb(230, 200, 60), Color.FromArgb(90, 140, 220), Color.FromArgb(230, 140, 60),
        Color.FromArgb(240, 220, 120), Color.FromArgb(80, 200, 200), Color.FromArgb(200, 70, 70), Color.FromArgb(150, 90, 60),
        Color.FromArgb(120, 220, 120), Color.FromArgb(200, 120, 200), Color.FromArgb(140, 120, 220), Color.FromArgb(240, 240, 240),
        Color.FromArgb(160, 200, 90), Color.FromArgb(235, 110, 190), Color.FromArgb(120, 40, 160), Color.FromArgb(90, 110, 140),
        Color.FromArgb(60, 160, 120),
    };

    void DrawFloor()
    {
        _mapSel = null;
        _floorImage?.Dispose();
        _floorImage = null;
        if (MapFloor is not { } f) { _mapCanvas.Invalidate(); return; }
        var img = new Bitmap(f.Width, f.Height, PixelFormat.Format32bppArgb);
        var d = img.LockBits(new Rectangle(0, 0, f.Width, f.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var px = new int[f.Width * f.Height];
        for (int i = 0; i < px.Length; i++) px[i] = CellColour(f.Cells[i]).ToArgb();
        System.Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length);
        img.UnlockBits(d);
        _floorImage = img;
        MapResized();
    }

    void MapResized()
    {
        if (_floorImage is { } img) _mapCanvas.AutoScrollMinSize = new Size(img.Width * _zoom, img.Height * _zoom);
        _mapCanvas.Invalidate();
    }

    Point? MapCell(Point p)
    {
        if (MapFloor is not { } f) return null;
        int x = (p.X - _mapCanvas.AutoScrollPosition.X) / _zoom, y = (p.Y - _mapCanvas.AutoScrollPosition.Y) / _zoom;
        return x >= 0 && y >= 0 && x < f.Width && y < f.Height ? new Point(x, y) : null;
    }

    void PaintMap(object? sender, PaintEventArgs e)
    {
        if (_floorImage is not { } img) return;
        var g = e.Graphics;
        g.TranslateTransform(_mapCanvas.AutoScrollPosition.X, _mapCanvas.AutoScrollPosition.Y);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(img, new Rectangle(0, 0, img.Width * _zoom, img.Height * _zoom));
        if (_map is not null && MapFloor is { } fl)
        {
            using var fill = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            foreach (var o in _map.Objects.Where(o => o.Floor == fl.Index))
            {
                int x = o.Column * _zoom, y = o.Row * _zoom, sz = Math.Max(4, _zoom * 2);
                g.FillRectangle(fill, x, y, sz, sz);
                g.DrawRectangle(Pens.Black, x, y, sz, sz);
            }
        }
        if (_map is not null && MapFloor is { } fa)
        {
            // enemy agents: row = x, column = z, floor from the height
            foreach (var ag in _map.Entities.Where(b => Bytes.U32(b, 12) == 0x8004 && b.Length > 100))
            {
                float x = BitConverter.ToSingle(ag, 80), y = BitConverter.ToSingle(ag, 84), z = BitConverter.ToSingle(ag, 88);
                if (2 - (int)MathF.Round(y / 14f) != fa.Index) continue;
                int sz = Math.Max(6, _zoom * 2);
                g.FillEllipse(Brushes.Red, z * _zoom - sz / 2f, x * _zoom - sz / 2f, sz, sz);
                g.DrawEllipse(Pens.White, z * _zoom - sz / 2f, x * _zoom - sz / 2f, sz, sz);
            }
        }
        if (_mapSel is { } s)
        {
            using var pen = new Pen(Color.White, 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, s.X * _zoom, s.Y * _zoom, s.Width * _zoom, s.Height * _zoom);
        }
    }

    void ShowMapInfo(Point c)
    {
        if (MapFloor is not { } f) return;
        var cell = f.Cells[c.Y * f.Width + c.X];
        string what = LairMap.TierOf(cell) is > 0 and var t ? $"rock tier {t}" : LairMap.RoomName(LairMap.TypeOf(cell)) is { } rn ? rn
            : (LairMap.FlagsOf(cell) & 8) != 0 ? "floor" : $"type {LairMap.TypeOf(cell):x8}";
        var obj = _map?.Objects.FirstOrDefault(o => o.Floor == f.Index && Math.Abs(o.Column - c.X) <= 1 && Math.Abs(o.Row - c.Y) <= 1);
        if (obj is not null && _game is not null) what += $" | {FnasNames().GetValueOrDefault(obj.Fnas, "object")} facing {FacingName($"{obj.FaceX},{obj.FaceY}")}";
        string sel = _mapSel is { } s ? $" | selected {s.Width}x{s.Height}" : "";
        _mapInfo.Text = $"{c.X},{c.Y}: {what}{sel} | saving to {_current?.Name ?? "(pick a mod)"}";
    }

    void SetMapTier(int tier) => AddMapEdit(new MapEdit { Action = "tier", Tier = tier }, needsArea: true);
    void DigMap() => AddMapEdit(new MapEdit { Action = "dig" }, needsArea: true);

    void BuildMapRoom()
    {
        using var dlg = new ThemedForm { Text = "Build which room?", Width = 360, Height = 480, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            using (var b = new SolidBrush(RoomColours[e.Index])) e.Graphics.FillRectangle(b, e.Bounds.X + 4, e.Bounds.Y + 4, 14, 14);
            TextRenderer.DrawText(e.Graphics, LairMap.Rooms[e.Index].Name, list.Font, new Point(e.Bounds.X + 24, e.Bounds.Y + 3), e.ForeColor);
        };
        foreach (var r in LairMap.Rooms) list.Items.Add(r.Name);
        var ok = new Button { Text = "Build", DialogResult = DialogResult.OK, AutoSize = true };
        list.DoubleClick += (_, _) => dlg.DialogResult = DialogResult.OK;
        dlg.Controls.Add(list);
        dlg.Controls.Add(Hint("Rock in the rectangle is dug out as this room; rooms already there change to it. Corridor = plain dug space. " +
                              "Furniture only goes in the rooms it's made for."));
        dlg.Controls.Add(Bar(ok));
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var room = LairMap.Rooms[list.SelectedIndex].Name;
        AddMapEdit(new MapEdit { Action = "room", Room = room, Note = room }, needsArea: true);
    }

    /// <summary>Fill in file/floor/selection, apply to the drawing, save to the target mod.</summary>
    void AddMapEdit(MapEdit edit, bool needsArea)
    {
        if (_game is null || _map is null || _mapFile is null || MapFloor is not { } f) return;
        if (_mapSel is not { } s) { Warn(needsArea ? "Drag a rectangle on the floor first." : "Click where it should go first."); return; }
        var mod = TargetMod();
        if (mod is null) return;
        edit.File = _mapFile; edit.Floor = f.Index; edit.Note ??= "drawn in ModManager";
        edit.X0 = s.Left; edit.Y0 = s.Top; edit.X1 = needsArea ? s.Right - 1 : s.Left; edit.Y1 = needsArea ? s.Bottom - 1 : s.Top;
        string what;
        try { what = ModBuilder.ApplyMapEdit(_game, _map, edit); }
        catch (ArgumentException x) { Warn(x.Message); return; }
        if (what.StartsWith("0 ")) { Warn("Nothing in that rectangle changes (already like that, dug, or not rock)."); return; }
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.MapEdits.Add(edit);
        _mapRedo.Clear();
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {LairName(_mapFile)} floor {f.Index}: {what}");
        var sel = _mapSel;
        DrawFloor();
        _mapSel = sel;
    }

    Dictionary<uint, string>? _fnasNames;
    Dictionary<uint, string> FnasNames() => _fnasNames ??= ModBuilder.FnasNames(_game!);

    async void PlaceMapObject()
    {
        if (_game is null || _map is null) return;
        if (_mapSel is null) { Warn("Click where it should go first (the object's corner goes on the selected cell)."); return; }
        var map = _map;
        List<LairMap.PlacedObject>? saved = null;
        if (!await Run("Reading your saves for furniture", (log, _) => saved = ModBuilder.SaveTemplates(_game.Install, log)) || saved is null) return;
        var names = FnasNames();
        var f0 = MapFloor!;
        var sel = _mapSel.Value;
        bool Fits(LairMap.PlacedObject o) => ModBuilder.RoomProblem(_game, map, o.Fnas, f0.Index, sel.Y, sel.X) is null;
        string here = LairMap.RoomName(LairMap.TypeOf(f0.Cells[sel.Y * f0.Width + sel.X])) ?? "not a room";
        string Name(uint fnas) => names.GetValueOrDefault(fnas, $"fnas {fnas:x8}");
        var fromMaps = ModBuilder.TemplatesFor(_game, map).Where(Fits).Select(o => (Name: Name(o.Fnas), Face: $"{o.FaceX},{o.FaceY}", Save: (LairMap.PlacedObject?)null)).Distinct().ToList();
        var have = fromMaps.Select(c => (c.Name, c.Face)).ToHashSet();
        var choices = fromMaps.Concat(saved.Where(o => o.Node.Key == map.ObjectKey && Fits(o))
                .Select(o => (Name: Name(o.Fnas), Face: $"{o.FaceX},{o.FaceY}", Save: (LairMap.PlacedObject?)o)).Where(c => !have.Contains((c.Name, c.Face))))
            .OrderBy(c => c.Name).ThenBy(c => c.Face).ToList();
        if (choices.Count == 0) { Warn($"Nothing fits here ({here}). Build a room first (Build room…), then place its furniture in it."); return; }
        using var dlg = new ThemedForm { Text = $"Place which object? (this cell: {here})", Width = 560, Height = 600, StartPosition = FormStartPosition.CenterParent };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search" };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var shown = new List<int>();
        void Fill()
        {
            list.BeginUpdate(); list.Items.Clear(); shown.Clear();
            for (int i = 0; i < choices.Count; i++)
                if (search.Text.Length == 0 || choices[i].Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase))
                { shown.Add(i); list.Items.Add($"{choices[i].Name}   facing {FacingName(choices[i].Face)}{(choices[i].Save is null ? "" : "   (from your saves)")}"); }
            list.EndUpdate();
        }
        search.TextChanged += (_, _) => Fill();
        var ok = new Button { Text = "Place", DialogResult = DialogResult.OK, AutoSize = true };
        list.DoubleClick += (_, _) => dlg.DialogResult = DialogResult.OK;
        dlg.Controls.Add(list);
        dlg.Controls.Add(search);
        dlg.Controls.Add(Hint("Objects the game's lairs have pre-placed, plus furniture you've built in your saves (copied into the mod, in the facings you built them). " +
                              "Crown Gold's map uses an older format, so it can only take its own objects."));
        dlg.Controls.Add(Bar(ok));
        Fill();
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var pick = choices[shown[list.SelectedIndex]];
        AddMapEdit(new MapEdit
        {
            Action = "place", Item = pick.Name, Facing = pick.Face, Note = pick.Name,
            Template = pick.Save is null ? null : Convert.ToHexString(pick.Save.Node.Payload()), TemplateKey = pick.Save?.Node.Key.ToString("x8"),
        }, needsArea: false);
    }

    async void PlaceMapAgent()
    {
        if (_game is null || _map is null || MapFloor is not { } f) return;
        if (_mapSel is not { } sel) { Warn("Click where the agent should stand first."); return; }
        if (LairMap.RoomName(LairMap.TypeOf(f.Cells[sel.Y * f.Width + sel.X])) is null) { Warn("Agents need floor to stand on: pick a room or corridor cell."); return; }
        if (!Agents.Heights.ContainsKey(f.Index)) { Warn($"Agents can't go on floor {f.Index} yet (its height isn't known)."); return; }
        List<Agents.Template>? saved = null;
        if (!await Run("Reading your saves for agents", (log, _) => saved = Agents.FromSaves(_game, log)) || saved is null) return;
        var map = _map;
        // ModKit's own agents first, then this island's from saves, then other islands' (their vehicle is swapped)
        var choices = Agents.Bundled().Select(t => (T: t, From: "comes with ModKit"))
            .Concat(saved.OrderBy(t => t.Lair == map.LairId ? 0 : 1).Select(t => (T: t, From: t.Lair == map.LairId ? $"from {t.Source}" : $"from {t.Source}, another island")))
            .ToList();
        if (choices.Count == 0) { Warn("No agents to place: ModKit's data\\agents.json is missing and none of your saves has a raid in progress."); return; }
        using var dlg = new ThemedForm { Text = "Place which agent?", Width = 560, Height = 440, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var c in choices) list.Items.Add($"{c.T.Kind}   vitality {c.T.Vitality:0}   ({c.From})");
        list.SelectedIndex = 0;
        var ok = new Button { Text = "Place", DialogResult = DialogResult.OK, AutoSize = true };
        list.DoubleClick += (_, _) => dlg.DialogResult = DialogResult.OK;
        dlg.Controls.Add(list);
        dlg.Controls.Add(Hint("Experimental. Agents act like a normal raid of their kind (Soldiers fight, Investigators snoop), each in a squad of their own, " +
                              "starting where you put them. Place several for a bigger raid."));
        dlg.Controls.Add(Bar(ok));
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var pick = choices[list.SelectedIndex].T;
        AddMapEdit(new MapEdit
        {
            Action = "agent", Note = $"{pick.Kind} ({choices[list.SelectedIndex].From})",
            Template = pick.Entity + " " + pick.Companion, Squad = pick.Squad, Vehicle = $"{pick.Vehicle:x}:{pick.VehicleType:x}",
        }, needsArea: false);
    }

    static string FacingName(string face) => face switch { "0,1" => "down", "0,-1" => "up", "1,0" => "right", "-1,0" => "left", _ => face };

    void UndoMapEdit()
    {
        if (_mapFile is null || MapFloor is not { } f || _current is not { } mod) return;
        var last = EditsFor(mod, _mapFile).LastOrDefault(e => e.Floor == f.Index);
        if (last is null) { Warn($"{mod.Name} has no map changes on this floor."); return; }
        CommitEditor();
        var group = last.Note?.StartsWith("stamp ") == true ? mod.MapEdits.Where(e => e.Note == last.Note).ToList() : new List<MapEdit> { last };
        mod.MapEdits.RemoveAll(group.Contains);
        _mapRedo.Add((mod, group));
        mod.Save();
        ShowEditor();
        OpenLair(_mapFile);
    }

    void RedoMapEdit()
    {
        if (_mapFile is null || MapFloor is not { } f || _current is not { } mod) return;
        int i = _mapRedo.FindLastIndex(x => ReferenceEquals(x.Mod, mod) && x.Edits[0].Floor == f.Index
                                            && x.Edits[0].File.Replace('/', '\\').Equals(_mapFile, StringComparison.OrdinalIgnoreCase));
        if (i < 0) { Warn("Nothing to redo on this floor."); return; }
        CommitEditor();
        mod.MapEdits.AddRange(_mapRedo[i].Edits);
        _mapRedo.RemoveAt(i);
        mod.Save();
        ShowEditor();
        OpenLair(_mapFile);
    }

    string StampsDir => Path.Combine(Path.GetDirectoryName(_settings.ModsFolder) ?? "", "Stamps");

    /// <summary>Save the rectangle (rooms, seams, edge rock, furniture) as a stamp in Documents\Eg2ModKit\Stamps.</summary>
    void SaveStamp()
    {
        if (_game is null || _map is null || _mapFile is null || MapFloor is not { } f) return;
        if (_mapSel is not { } s || s.Width * s.Height < 2) { Warn("Drag a rectangle round what to save first."); return; }
        var name = Prompt("Name for this stamp:", $"{LairName(_mapFile)} {s.Width}x{s.Height}")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var names = ModBuilder.FnasNames(_game);
        var st = MapStamp.Capture(_map, f.Index, s.Left, s.Top, s.Right - 1, s.Bottom - 1, name, id => names.GetValueOrDefault(id));
        st.Save(StampsDir);
        Log($"stamp \"{st.Name}\" saved: {st.Width}x{st.Height} cells, {st.Edits.Count(e => e.Action == "place")} objects -> {StampsDir}");
    }

    /// <summary>Paste a stamp with its top-left corner at the clicked cell: its changes go into the target mod as one undo step.</summary>
    void PasteStamp()
    {
        if (_game is null || _map is null || _mapFile is null || MapFloor is not { } f) return;
        if (_mapSel is not { } s) { Warn("Click where the stamp's top-left corner should go first."); return; }
        var stamps = MapStamp.LoadAll(StampsDir);
        if (stamps.Count == 0) { Warn("No stamps yet: drag a rectangle and use Save stamp… first."); return; }
        using var dlg = new ThemedForm { Text = "Paste which stamp?", Width = 420, Height = 420, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var st in stamps) list.Items.Add($"{st.Name}  ({st.Width}x{st.Height}, {st.Edits.Count(e => e.Action == "place")} objects)");
        list.SelectedIndex = 0;
        var ok = new Button { Text = "Paste", DialogResult = DialogResult.OK, AutoSize = true };
        list.DoubleClick += (_, _) => dlg.DialogResult = DialogResult.OK;
        dlg.Controls.Add(list);
        dlg.Controls.Add(Hint("Everything in the stamp's area is replaced: objects removed, rock filled, the stamp's rooms dug and furnished. " +
                              "Furniture only pastes into a lair with the same object format (not between Crown Gold and the others)."));
        dlg.Controls.Add(Bar(ok));
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var stamp = stamps[list.SelectedIndex];
        var mod = TargetMod();
        if (mod is null) return;
        var edits = stamp.At(_mapFile, f.Index, s.Left, s.Top);
        string tag = $"stamp {stamp.Name} ({DateTime.Now:HH:mm:ss})";
        foreach (var e in edits) e.Note = tag;
        try { foreach (var e in edits) ModBuilder.ApplyMapEdit(_game, _map, e); }
        catch (ArgumentException x) { Warn($"Can't paste {stamp.Name} here: {x.Message}"); OpenLair(_mapFile); return; }
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.MapEdits.AddRange(edits);
        _mapRedo.Clear();
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {LairName(_mapFile)} floor {f.Index}: pasted {tag}");
        DrawFloor();
    }

    void ResetMap()
    {
        if (_mapFile is null || _current is not { } mod) return;
        var mine = EditsFor(mod, _mapFile).ToList();
        if (mine.Count == 0) return;
        if (MessageBox.Show(this, $"Remove all {mine.Count} map changes to {LairName(_mapFile)} from {mod.Name}?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        CommitEditor();
        mod.MapEdits.RemoveAll(mine.Contains);
        mod.Save();
        ShowEditor();
        OpenLair(_mapFile);
    }
}
