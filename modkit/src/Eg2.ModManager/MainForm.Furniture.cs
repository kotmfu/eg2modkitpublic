using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Furniture page: pick an item on the left; the right shows its floor plan (tiles, keep-clear space, edges, where
/// minions stand) and its job slots with who can and can't use each (<see cref="FurnitureShape"/>, <see cref="JobTypes"/>).
/// </summary>
sealed partial class MainForm
{
    TabPage _furniturePage = null!;
    readonly TextBox _furnFilter = new() { Dock = DockStyle.Top, PlaceholderText = "Search furniture…" };
    readonly ListBox _furnList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly Label _furnTitle = new() { Dock = DockStyle.Top, AutoSize = false, Height = 34, Font = new Font(SystemFonts.DefaultFont.FontFamily, 13f, FontStyle.Bold), Padding = new Padding(4, 6, 0, 0) };
    readonly Label _furnInfo = new() { Dock = DockStyle.Top, AutoSize = false, Height = 24, Padding = new Padding(4, 2, 0, 0) };
    readonly FloorPlan _plan = new() { Dock = DockStyle.Fill };
    readonly DataGridView _slotGrid = ReadOnlyGrid();
    readonly ToolTip _planTip = new();
    GameObject? _furnObj;
    List<FurnitureShape.Slot> _furnSlots = new();

    sealed class FloorPlan : Panel
    {
        public FloorPlan() { DoubleBuffered = true; ResizeRedraw = true; }
        public List<FurnitureShape.Cell> Cells = new();
        public List<FurnitureShape.Slot> Slots = new();
        public int Selected = -1;
        public (int W, int H) Footprint;
        // tile size and where tile (0,0)'s top-left sits
        public float Tile { get; private set; }
        public PointF Origin { get; private set; }
        // stand points drawn as one circle per position: (position, the slot numbers and the points there)
        public List<((float X, float Y) Pos, List<(int Slot, FurnitureShape.Point Point)> Points)> Spots = new();
        // a circle being dragged: its index in Spots and where it is now (tile units)
        public int Dragging = -1;
        public (float X, float Y) DragTo;
        float Dot => Math.Clamp(Tile * 0.55f, 14, 30);

        public void SetData(List<FurnitureShape.Cell> cells, List<FurnitureShape.Slot> slots, (int W, int H) footprint)
        {
            Cells = cells; Slots = slots; Footprint = footprint; Selected = -1; Dragging = -1;
            Spots = slots.SelectMany((sl, i) => sl.Points.Select(pt => (i, pt))).GroupBy(x => (x.pt.X, x.pt.Y))
                .Select(g => (g.Key, g.Select(x => (x.i, x.pt)).ToList())).ToList();
            Invalidate();
        }

        public (float X, float Y) ToTile(System.Drawing.Point p) => ((p.X - Origin.X) / Tile - 0.5f, (p.Y - Origin.Y) / Tile - 0.5f);

        public int HitSpot(System.Drawing.Point p)
        {
            for (int k = Spots.Count - 1; k >= 0; k--)
            {
                var c = At(Spots[k].Pos.X, Spots[k].Pos.Y);
                if (Math.Abs(p.X - c.X) <= Dot / 2 && Math.Abs(p.Y - c.Y) <= Dot / 2) return k;
            }
            return -1;
        }

        public void Fit()
        {
            float minX = -0.5f, maxX = Footprint.W - 0.5f, minY = -0.5f, maxY = Footprint.H - 0.5f;
            foreach (var p in Slots.SelectMany(s => s.Points)) { minX = Math.Min(minX, p.X - 0.5f); maxX = Math.Max(maxX, p.X + 0.5f); minY = Math.Min(minY, p.Y - 0.5f); maxY = Math.Max(maxY, p.Y + 0.5f); }
            float spanX = maxX - minX + 1, spanY = maxY - minY + 1;
            Tile = Math.Clamp(Math.Min((Width - 20) / spanX, (Height - 20) / spanY), 6, 60);
            Origin = new PointF((Width - spanX * Tile) / 2 + (-0.5f - minX + 0.5f) * Tile, (Height - spanY * Tile) / 2 + (-0.5f - minY + 0.5f) * Tile);
        }

        public PointF At(float x, float y) => new(Origin.X + (x + 0.5f) * Tile, Origin.Y + (y + 0.5f) * Tile);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (Cells.Count == 0) { TextRenderer.DrawText(g, "No floor plan for this item.", Font, ClientRectangle, Theme.Muted); return; }
            if (Dragging < 0) Fit();   // keep the scale still while dragging
            using var grid = new Pen(Theme.Lines);
            foreach (var c in Cells)
            {
                var r = new RectangleF(Origin.X + c.X * Tile, Origin.Y + c.Y * Tile, Tile, Tile);
                var fill = c.Kind switch { 2 => Theme.Panel, 1 => Color.FromArgb(120, 90, 50), _ => Theme.Input };
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
                if (c.KeepClear) using (var h = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.WideUpwardDiagonal, Theme.Cyan, Color.Transparent)) g.FillRectangle(h, r);
                g.DrawRectangle(grid, r.X, r.Y, r.Width, r.Height);
                // edges: back (top), right, front (bottom), left; 1 = wall (thick grey), 2 = opening (green)
                var sides = new[] { (r.Left, r.Top, r.Right, r.Top), (r.Right, r.Top, r.Right, r.Bottom), (r.Left, r.Bottom, r.Right, r.Bottom), (r.Left, r.Top, r.Left, r.Bottom) };
                for (int s = 0; s < 4; s++)
                {
                    if (c.Edges[s] == 0) continue;
                    using var p = new Pen(c.Edges[s] == 1 ? Color.Silver : Theme.Good, Math.Max(3, Tile / 8));
                    g.DrawLine(p, sides[s].Item1, sides[s].Item2, sides[s].Item3, sides[s].Item4);
                }
            }
            using (var frame = new Pen(Theme.Gold, 2)) g.DrawRectangle(frame, Origin.X, Origin.Y, Footprint.W * Tile, Footprint.H * Tile);
            // where minions stand: numbered by slot (points shared by several slots are drawn once, numbers joined)
            float d = Dot;
            for (int k = 0; k < Spots.Count; k++)
            {
                var spot = Spots[k];
                var c = k == Dragging ? At(DragTo.X, DragTo.Y) : At(spot.Pos.X, spot.Pos.Y);
                bool sel = k == Dragging || spot.Points.Any(x => x.Slot == Selected);
                using (var b = new SolidBrush(sel ? Theme.Gold : Theme.Selection)) g.FillEllipse(b, c.X - d / 2, c.Y - d / 2, d, d);
                using (var p = new Pen(Theme.Text)) g.DrawEllipse(p, c.X - d / 2, c.Y - d / 2, d, d);
                string label = string.Join(",", spot.Points.Select(x => x.Slot + 1).Distinct());
                TextRenderer.DrawText(g, label, Font, Rectangle.Round(new RectangleF(c.X - 40, c.Y - d / 2, 80, d)), sel ? Color.Black : Theme.SelectionText,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    sealed record SlotRow(
        [property: System.ComponentModel.DisplayName("#")] int Number,
        [property: System.ComponentModel.DisplayName("Slot")] string Slot,
        [property: System.ComponentModel.DisplayName("Job")] string Job,
        [property: System.ComponentModel.DisplayName("Can use it")] string Can,
        [property: System.ComponentModel.DisplayName("Can't use it")] string Cannot,
        [property: System.ComponentModel.DisplayName("Stands at")] string Points);

    TabPage FurniturePage()
    {
        var page = new TabPage("Furniture");
        var left = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(4) };
        left.Controls.Add(_furnList);
        left.Controls.Add(_furnFilter);
        _furnList.FormattingEnabled = true;
        _furnList.Format += (_, e) => { if (e.ListItem is FurnitureRow r) e.Value = FurnLabel(r); };
        _furnList.SelectedIndexChanged += (_, _) => ShowFurnitureItem(_furnList.SelectedItem as FurnitureRow);
        Debounce(_furnFilter, FillFurnitureList);

        var legend = Hint("Grey tiles: the item; dark: open floor inside it; brown: door; cyan stripes: keep clear (minions use it from there); " +
                          "silver edge: probably needs a wall; green edge: probably needs an opening; circles: where a minion stands for slot n (hover a tile for its raw data).");
        legend.Dock = DockStyle.Bottom;
        _slotGrid.Dock = DockStyle.Bottom;
        _slotGrid.Height = 200;
        _slotGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _slotGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _slotGrid.DataBindingComplete += (_, _) =>
        {
            foreach (var (col, w) in new[] { ("Number", 5), ("Slot", 18), ("Job", 16), ("Can", 30), ("Cannot", 30), ("Points", 14) })
                if (_slotGrid.Columns[col] is { } c) c.FillWeight = w;
        };
        _slotGrid.SelectionChanged += (_, _) => { _plan.Selected = _slotGrid.CurrentRow?.Index ?? -1; _plan.Invalidate(); };
        _plan.MouseMove += (_, e) =>
        {
            if (_plan.Tile <= 0) return;
            int x = (int)Math.Floor((e.X - _plan.Origin.X) / _plan.Tile), y = (int)Math.Floor((e.Y - _plan.Origin.Y) / _plan.Tile);
            var c = _plan.Cells.FirstOrDefault(c => c.X == x && c.Y == y);
            string tip = c is null ? "" : $"Tile {x},{y}: {c.KindName}{(c.KeepClear ? ", keep clear" : "")}, edges back/right/front/left {string.Join("/", c.Edges)}\n" +
                                          $"raw (not decoded yet): {Convert.ToHexString(c.Raw)}";
            if (_planTip.GetToolTip(_plan) != tip) _planTip.SetToolTip(_plan, tip);
        };

        var who = Btn("Who can use this slot…", (_, _) =>
        {
            if (_slotGrid.CurrentRow?.Index is int i && i < _furnSlots.Count && JobObject(_furnSlots[i].Job) is { Tag: "rjob" } job) ShowJobTypes(job);
            else Warn("Pick a slot that has a job first.");
        });
        var shape = Btn("Size and slots…", (_, _) => { if (_furnObj is { } o) ShowShape(o); });
        var price = Btn("Change price…", (_, _) => { if (_furnList.SelectedItem is FurnitureRow r) ChangeFurniturePrice(r); });
        var job = Btn("Change job…", (_, _) => ChangeSlotJob());
        var reset = Btn("Back to the game's", (_, _) => ResetFurnitureLayout());
        _plan.MouseDown += PlanMouseDown;
        _plan.MouseMove += (_, e) => { if (_plan.Dragging >= 0) { _plan.DragTo = Snap(_plan.ToTile(e.Location)); _plan.Invalidate(); } };
        _plan.MouseUp += PlanMouseUp;

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_plan);
        right.Controls.Add(legend);
        right.Controls.Add(_slotGrid);
        right.Controls.Add(Bar(price, shape, who, job, reset));
        right.Controls.Add(_furnInfo);
        right.Controls.Add(_furnTitle);
        right.Controls.Add(Hint("Everything about a piece of furniture: its floor plan, its job slots and which minions can use each one. " +
                                "Changes (price, size, slot count, who can do a job) are saved in your mod like any other change. " +
                                "\"Who can use\" comes from each job's list: listed types are allowed or refused, other types get the game's default for that job. " +
                                "To edit: drag a circle to move where minions stand, right-click a tile to change it, or pick a slot and use \"Change job…\"."));
        page.Controls.Add(right);
        page.Controls.Add(new Splitter { Dock = DockStyle.Left });
        page.Controls.Add(left);
        return page;
    }

    // display names shared by several items (nine "Wall Mounted Chair"s) also show the internal name
    HashSet<string>? _sharedFurnNames;

    string FurnLabel(FurnitureRow r)
    {
        _sharedFurnNames ??= _game!.Furniture.GroupBy(x => x.DisplayName).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        if (string.IsNullOrEmpty(r.DisplayName)) return r.Name;
        return _settings.AdvancedTools || _sharedFurnNames.Contains(r.DisplayName) ? $"{r.DisplayName}  ({r.Name})" : r.DisplayName;
    }

    void LoadFurniturePage()
    {
        if (_game is null || _furnList.Items.Count > 0) return;
        FillFurnitureList();
    }

    void FillFurnitureList()
    {
        if (_game is null) return;
        string q = _furnFilter.Text.Trim();
        var keep = _furnList.SelectedItem as FurnitureRow;
        var rows = _game.Furniture.Where(r => _settings.AdvancedTools || !r.Name.StartsWith("Benchmark", StringComparison.OrdinalIgnoreCase))
            .Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => string.IsNullOrEmpty(r.DisplayName) ? 1 : 0).ThenBy(FurnLabel, StringComparer.OrdinalIgnoreCase).ToArray();
        _furnList.BeginUpdate();
        _furnList.Items.Clear();
        _furnList.Items.AddRange(rows);
        _furnList.EndUpdate();
        int at = keep is null ? -1 : Array.IndexOf(rows, keep);
        _furnList.SelectedIndex = at >= 0 ? at : rows.Length > 0 ? 0 : -1;
    }

    GameObject? JobObject(uint id) => _game?.ObjectsById.TryGetValue(id, out var l) == true ? l.FirstOrDefault() : null;

    void ShowFurnitureItem(FurnitureRow? r)
    {
        _furnObj = r is null || _game is null ? null : _game.Objects.FirstOrDefault(o => o.Tag == "fntr" && o.IsRecord && o.RecordName == r.Name && o.Package == r.Package);
        _furnTitle.Text = r is null ? "" : string.IsNullOrEmpty(r.DisplayName) ? r.Name : r.DisplayName;
        var body = _furnObj is null ? Array.Empty<byte>() : EditedFurnitureBody(_furnObj);
        var fp = FurnitureShape.Footprint(body);
        _furnSlots = FurnitureShape.Slots(body);
        var edit = r is null ? default : ModsInList().SelectMany(m => m.ShapeEdits.Select(e => (m, e))).FirstOrDefault(x => x.e.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase));
        _furnInfo.Text = r is null ? "" :
            $"Price {(r.Cost is { } c ? $"{c:N0}" : "—")}   ·   Size {(fp is { } f ? $"{f.W} x {f.H} tiles" : "—")}   ·   {_furnSlots.Count} slot{(_furnSlots.Count == 1 ? "" : "s")}" +
            (edit.m is { } m ? $"   ·   {m.Name} changes it ({(edit.e.Width ?? fp?.W)} x {(edit.e.Height ?? fp?.H)}{(edit.e.Slots is { } s ? $", {s} slots" : "")})" : "") +
            (_furnObj is { } o && ModsInList().Where(x => x.FieldEdits.Any(f => IsFurnEdit(f, o))).Select(x => x.Name).ToList() is { Count: > 0 } lm ? $"   ·   layout changed by {string.Join(", ", lm)}" : "");

        _plan.SetData(FurnitureShape.Cells(body), _furnSlots, fp ?? (0, 0));

        var types = _minionTypes ??= Requirements.MinionTypes(_game!);
        string Who(uint key) => types.TryGetValue(key, out var n) ? n : $"other character 0x{key:x8}";
        _slotGrid.DataSource = _furnSlots.Select((s, i) =>
        {
            var job = JobObject(s.Job);
            string jobName = job is null ? $"0x{s.Job:x8}" : job.Tag == "rjob" ? job.Name.Trim('"') : $"linked item: {job.Name}";
            var list = job is { Tag: "rjob" } ? JobTypes.Parse(job.Body) : null;
            string can = job is not { Tag: "rjob" } ? "" : list is null ? "anyone" : string.Join(", ", list.Where(e => e.Allowed).Select(e => Who(e.Key))) is { Length: > 0 } a ? a + " (others: game default)" : "game default";
            string cannot = list is null ? "" : string.Join(", ", list.Where(e => !e.Allowed).Select(e => Who(e.Key)));
            return new SlotRow(i + 1, s.Name.Length > 0 ? s.Name : "(unnamed)", jobName, can, cannot, string.Join("  ", s.Points.Select(p => $"({p.X:0.##}, {p.Y:0.##})")));
        }).ToList();
    }

    void ChangeFurniturePrice(FurnitureRow row)
    {
        if (TargetMod() is not { } mod) return;
        if (row.Cost is null) { Warn($"{row.Name} has no price."); return; }
        EditMod(mod, 1, () =>
        {
            if (!mod.FurnitureEdits.Any(e => e.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase)))
                mod.FurnitureEdits.Add(new FurnitureEdit { Name = row.Name, Cost = row.Cost.Value });
        });
        _status.Text = $"{row.Name}: set the new price in the Prices section of {mod.Name}.";
    }

    // ------------------------------------------------------------------ editing (value edits on the fntr record, same size so offsets stay put)

    bool IsFurnEdit(FieldEdit f, GameObject o) => f.Tag == "fntr" && f.Object.Equals(o.RecordName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The record as the mods leave it: every mod's value edits for it applied.</summary>
    byte[] EditedFurnitureBody(GameObject o)
    {
        var b = (byte[])o.Body.Clone();
        foreach (var f in ModsInList().SelectMany(m => m.FieldEdits).Where(f => IsFurnEdit(f, o)))
        {
            try { var d = f.Encode(); if (f.Offset >= 0 && f.Offset + d.Length <= b.Length) Buffer.BlockCopy(d, 0, b, f.Offset, d.Length); }
            catch (Exception e) when (e is FormatException or OverflowException) { }
        }
        return b;
    }

    /// <summary>Set one value of the shown item; setting it back to the game's value removes the edit.</summary>
    void SetFurnitureValue(ModDefinition mod, int offset, string type, string value, string note)
    {
        var o = _furnObj!;
        var game = o.Body.AsSpan(offset, FieldEdit.SizeOf(type)).ToArray();
        var edit = new FieldEdit { Package = o.Package, Tag = "fntr", Object = o.RecordName!, Offset = offset, Type = type, Value = value, Expect = Convert.ToHexString(game), Note = $"{o.RecordName}: {note}" };
        mod.FieldEdits.RemoveAll(f => IsFurnEdit(f, o) && f.Offset == offset);
        if (!edit.Encode().AsSpan().SequenceEqual(game)) mod.FieldEdits.Add(edit);
    }

    /// <summary>Make value changes in the mod that already changes this item (else the target mod), save, redraw.</summary>
    void EditFurniture(string what, Action<ModDefinition> change)
    {
        if (_furnObj is not { } o) return;
        var mod = ModsInList().FirstOrDefault(m => m.FieldEdits.Any(f => IsFurnEdit(f, o))) ?? TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        change(mod);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {o.RecordName}: {what}");
        _status.Text = $"{_furnTitle.Text}: {what} (saved in {mod.Name}).";
        int sel = _slotGrid.CurrentRow?.Index ?? -1;
        ShowFurnitureItem(_furnList.SelectedItem as FurnitureRow);
        if (sel >= 0 && sel < _slotGrid.Rows.Count) _slotGrid.CurrentCell = _slotGrid.Rows[sel].Cells[0];
    }

    static (float X, float Y) Snap((float X, float Y) p) => (MathF.Round(p.X * 4) / 4, MathF.Round(p.Y * 4) / 4);

    void PlanMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _plan.HitSpot(e.Location) is var k and >= 0)
        {
            _plan.Dragging = k;
            _plan.DragTo = _plan.Spots[k].Pos;
            int slot = _plan.Spots[k].Points[0].Slot;
            if (slot < _slotGrid.Rows.Count) _slotGrid.CurrentCell = _slotGrid.Rows[slot].Cells[0];
            _plan.Invalidate();
        }
        else if (e.Button == MouseButtons.Right && _plan.Tile > 0)
        {
            int x = (int)Math.Floor((e.X - _plan.Origin.X) / _plan.Tile), y = (int)Math.Floor((e.Y - _plan.Origin.Y) / _plan.Tile);
            if (_plan.Cells.FirstOrDefault(c => c.X == x && c.Y == y) is { } cell) TileMenu(cell).Show(_plan, e.Location);
        }
    }

    void PlanMouseUp(object? sender, MouseEventArgs e)
    {
        if (_plan.Dragging < 0) return;
        var spot = _plan.Spots[_plan.Dragging];
        var to = _plan.DragTo;
        _plan.Dragging = -1;
        if (to == spot.Pos) { _plan.Invalidate(); return; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        EditFurniture($"stand point ({spot.Pos.X:0.##}, {spot.Pos.Y:0.##}) moved to ({to.X:0.##}, {to.Y:0.##})", mod =>
        {
            foreach (var (slot, pt) in spot.Points)
            {
                SetFurnitureValue(mod, pt.XAt, "f32", to.X.ToString(inv), $"slot {slot + 1} stand point x");
                SetFurnitureValue(mod, pt.ZAt, "f32", (to.Y == 0 ? 0f : -to.Y).ToString(inv), $"slot {slot + 1} stand point z (= -y)");
            }
        });
    }

    ContextMenuStrip TileMenu(FurnitureShape.Cell c)
    {
        var menu = new ContextMenuStrip();
        void Item(ToolStripItemCollection into, string text, bool on, string what, int offset, int value)
        {
            var i = new ToolStripMenuItem(text) { Checked = on };
            i.Click += (_, _) => EditFurniture($"tile {c.X},{c.Y}: {what}", m => SetFurnitureValue(m, offset, "u8", value.ToString(), $"tile {c.X},{c.Y} {what}"));
            into.Add(i);
        }
        menu.Items.Add(new ToolStripLabel($"Tile {c.X},{c.Y}"));
        foreach (var (kind, name) in new[] { (2, "Solid (part of the item)"), (0, "Open floor inside the item"), (1, "Door") })
            Item(menu.Items, name, c.Kind == kind, $"kind {name.ToLowerInvariant()}", c.At, kind);
        Item(menu.Items, "Keep clear (minions use the item from here)", c.KeepClear, c.KeepClear ? "keep clear off" : "keep clear on",
             c.At + FurnitureShape.KeepClearOffset, c.KeepClear ? 0 : 2);
        menu.Items.Add(new ToolStripSeparator());
        string[] sides = { "Back edge", "Right edge", "Front edge", "Left edge" };
        for (int s = 0; s < 4; s++)
        {
            var sub = new ToolStripMenuItem(sides[s]);
            foreach (var (v, name) in new[] { (0, "Nothing"), (1, "Wall (probably)"), (2, "Opening (probably)") })
                Item(sub.DropDownItems, name, c.Edges[s] == v, $"{sides[s].ToLowerInvariant()} {name.ToLowerInvariant()}", c.At + FurnitureShape.EdgeOffsets[s], v);
            menu.Items.Add(sub);
        }
        return menu;
    }

    void ChangeSlotJob()
    {
        if (_game is null || _slotGrid.CurrentRow?.Index is not int i || i >= _furnSlots.Count) { Warn("Pick a slot first."); return; }
        var slot = _furnSlots[i];
        var types = _minionTypes ??= Requirements.MinionTypes(_game);
        string Summary(GameObject j) => JobTypes.Parse(j.Body) is { } l
            ? string.Join(", ", l.Select(e => (e.Allowed ? "" : "not ") + (types.TryGetValue(e.Key, out var n) ? n : "other")).Distinct())
            : "anyone";
        var jobs = _game.Objects.Where(o => o.Tag == "rjob").DistinctBy(o => o.ObjectId)
            .Select(o => (Id: o.ObjectId, Text: $"{o.Name.Trim('"')}  —  {Summary(o)}")).OrderBy(x => x.Text, StringComparer.OrdinalIgnoreCase).ToList();

        using var dlg = new ThemedForm { Text = $"Job for slot {i + 1} ({slot.Name})", Width = 760, Height = 560, StartPosition = FormStartPosition.CenterParent };
        var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search jobs…" };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        void Fill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(jobs.Where(j => j.Text.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Select(j => (object)j.Text).ToArray());
            list.EndUpdate();
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        list.SelectedIndex = jobs.FindIndex(j => j.Id == slot.Job);
        var ok = new Button { Text = "Use this job", DialogResult = DialogResult.OK, AutoSize = true };
        dlg.Controls.Add(list);
        dlg.Controls.Add(filter);
        dlg.Controls.Add(Hint("The job decides what a minion does in this slot and which minion types may (\"not X\" = refused, types not listed get the game's default). " +
                              "Jobs are shared: to change who may do a job everywhere, use \"Who can use this slot…\" instead."));
        var bar = Bar(ok);
        bar.Dock = DockStyle.Bottom;
        dlg.Controls.Add(bar);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not string pick) return;
        var chosen = jobs.First(j => j.Text == pick);
        if (chosen.Id == slot.Job) return;
        EditFurniture($"slot {i + 1} job changed to {pick.Split("  —")[0]}", m => SetFurnitureValue(m, slot.JobAt, "u32", $"0x{chosen.Id:x8}", $"slot {i + 1} ({slot.Name}) job"));
    }

    void ResetFurnitureLayout()
    {
        if (_furnObj is not { } o) return;
        var holders = ModsInList().Where(m => m.FieldEdits.Any(f => IsFurnEdit(f, o))).ToList();
        if (holders.Count == 0) { _status.Text = $"{_furnTitle.Text}: no tile, slot or stand point changes."; return; }
        if (MessageBox.Show(this, $"Remove every tile, job and stand point change to {_furnTitle.Text} from {string.Join(", ", holders.Select(m => m.Name))}?", Text,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        CommitEditor();
        foreach (var m in holders) { m.FieldEdits.RemoveAll(f => IsFurnEdit(f, o)); m.Save(); Log($"{m.Id}: {o.RecordName}: layout back to the game's"); }
        if (_current is not null && holders.Contains(_current)) ShowEditor();
        ShowFurnitureItem(_furnList.SelectedItem as FurnitureRow);
        _status.Text = $"{_furnTitle.Text}: back to the game's layout.";
    }
}
