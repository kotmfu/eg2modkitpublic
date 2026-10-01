using System.Drawing.Drawing2D;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Island tab: an island's outdoor scenery (plants, palms, rocks, lamps...) seen from above; pick scenery groups,
/// drag a rectangle and hide or move them (saved as SceneryEdits, loaded by the runtime DLL).</summary>
sealed partial class MainForm
{
    readonly ListBox _islandList = new() { Dock = DockStyle.Top, Height = 110, IntegralHeight = false, FormattingEnabled = true };
    readonly ListBox _sceneryGroups = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
    readonly TreeCanvas _islandCanvas = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(12, 24, 40) };
    readonly Label _islandInfo = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = Theme.Muted };
    TabPage? _islandPage;
    IslandScenery? _island;
    string? _islandFile;           // "envs\lair_....pc"
    float _islandScale = 1f;        // pixels per world unit
    RectangleF _islandBounds;       // world x/z of the scenery
    PointF? _islandDrag;
    RectangleF? _islandSel;         // world x/z
    readonly List<(ModDefinition Mod, SceneryEdit Edit)> _sceneryRedo = new();

    TabPage IslandPage()
    {
        var page = new TabPage("Island");
        var left = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(4) };
        left.Controls.Add(_sceneryGroups);
        left.Controls.Add(_islandList);
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_islandCanvas);
        right.Controls.Add(Tools(
            ("Selected scenery", new Control[] { Btn("Hide in rectangle", (_, _) => AddSceneryEdit("hide", area: true)), Btn("Hide whole group", (_, _) => AddSceneryEdit("hide", area: false)),
                Btn("Move…", (_, _) => AddSceneryEdit("move", area: true)), Btn("Copy…", (_, _) => AddSceneryEdit("copy", area: true)) }),
            ("Island", new Control[] { Btn("Copy as new island…", (_, _) => CopyIsland()), Btn("Export mesh…", (_, _) => ExportSceneryMesh()), Btn("Replace mesh…", (_, _) => ReplaceSceneryMesh()) }),
            ("History", new Control[] { Btn("Undo", (_, _) => UndoSceneryEdit()), Btn("Redo", (_, _) => RedoSceneryEdit()), Btn("Reset island", (_, _) => ResetScenery()) }),
            ("Zoom", new Control[] { Btn("−", (_, _) => { _islandScale = Math.Max(0.25f, _islandScale / 1.5f); IslandResized(); }), Btn("+", (_, _) => { _islandScale = Math.Min(20, _islandScale * 1.5f); IslandResized(); }), _islandInfo })));
        var hint = Hint("The island's outdoor scenery from above. Pick one or more groups on the left (Ctrl/Shift for several; they light up), " +
                        "drag a rectangle, then Hide, Move or Copy them. Hidden scenery is drawn grey. Only looks change: where minions walk comes from the lair map (Lair maps tab), and the island keeps its own physics mesh. " +
                        "Applied by the ModKit runtime DLL; affects every game on that island.");
        right.Controls.Add(hint);
        page.Controls.Add(right);
        page.Controls.Add(new Splitter { Dock = DockStyle.Left });
        page.Controls.Add(left);
        _islandList.Format += (_, e) => { if (e.ListItem is string f) e.Value = LairName(f); };
        _islandList.SelectedIndexChanged += (_, _) => { if (_islandList.SelectedItem is string f) OpenIsland(f); };
        _sceneryGroups.SelectedIndexChanged += (_, _) => _islandCanvas.Invalidate();
        _islandCanvas.Paint += PaintIsland;
        _islandCanvas.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _islandDrag = IslandWorld(e.Location); _islandSel = null; _islandCanvas.Invalidate(); } };
        _islandCanvas.MouseMove += (_, e) =>
        {
            var w = IslandWorld(e.Location);
            if (_islandDrag is { } a && e.Button == MouseButtons.Left)
            {
                _islandSel = RectangleF.FromLTRB(Math.Min(a.X, w.X), Math.Min(a.Y, w.Y), Math.Max(a.X, w.X), Math.Max(a.Y, w.Y));
                _islandCanvas.Invalidate();
            }
            ShowIslandInfo(w);
        };
        _islandCanvas.MouseUp += (_, _) => _islandDrag = null;
        return page;
    }

    void LoadIslands()
    {
        if (_game is null || _islandList.Items.Count > 0) return;
        var dir = _game.Install.Full("envs");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "lair_*.pc").Order()) _islandList.Items.Add(Path.Combine("envs", Path.GetFileName(f)));
        foreach (var n in ModsInList().SelectMany(m => m.NewLairs)) AddIsland(n.Stem);
        if (_islandList.Items.Count > 0) _islandList.SelectedIndex = 0;
    }

    void AddIsland(string stem)
    {
        string f = Path.Combine("envs", stem + ".pc");
        if (!_islandList.Items.Contains(f)) _islandList.Items.Add(f);
    }

    /// <summary>The game file an island is read from: its own, or for a new lair (a mod's NewLairs) the island it copies.</summary>
    string IslandSource(string file)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        var n = ModsInList().SelectMany(m => m.NewLairs).FirstOrDefault(n => n.Stem.Equals(stem, StringComparison.OrdinalIgnoreCase));
        return _game!.Install.Full(n is null || File.Exists(_game.Install.Full(file)) && !IsOurs(file) ? file : Path.Combine("envs", n.From + ".pc"));
    }

    bool IsOurs(string rel) => Installer.Current(_game!.Install.Root)?.Files.Any(f => f.Path.Equals(rel, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>The open island copied as a new lair (map, island, sounds) in the target mod; edit it like any other.</summary>
    void CopyIsland()
    {
        if (_game is null || _islandFile is null) return;
        string from = Path.GetFileNameWithoutExtension(_islandFile);
        if (ModsInList().SelectMany(m => m.NewLairs).FirstOrDefault(n => n.Stem.Equals(from, StringComparison.OrdinalIgnoreCase)) is { } src) from = src.From;
        var stem = Prompt($"Name of the new lair (lair_ + lowercase letters, digits, _). It starts as a copy of {LairName(_islandFile)}; " +
                          "whether the island select can offer it is still being tested (HANDOFF round 50).", "lair_new_01_default")?.Trim();
        if (stem is null) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(stem, "^lair_[a-z0-9_]+$")) { Warn("lair_ + lowercase letters, digits, _ (e.g. lair_ural_01_default)."); return; }
        if (_islandList.Items.Contains(Path.Combine("envs", stem + ".pc"))) { Warn($"{stem} already exists."); return; }
        var mod = TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.NewLairs.Add(new NewLair { Stem = stem, From = from, Note = $"copy of {LairName(_islandFile)}" });
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: new lair {stem}, a copy of {LairName(_islandFile)} (about 90 MB in the build). Edit its island here and its map in Lair maps.");
        AddIsland(stem);
        if (_lairList.Items.Count > 0 && Path.Combine(MapDir, stem + ".base") is var map && !_lairList.Items.Contains(map)) _lairList.Items.Add(map);
        _islandList.SelectedItem = Path.Combine("envs", stem + ".pc");
    }

    IEnumerable<SceneryEdit> SceneryEditsFor(ModDefinition? m, string file) =>
        m?.SceneryEdits.Where(e => e.File.Replace('/', '\\').Equals(file, StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<SceneryEdit>();

    void OpenIsland(string file)
    {
        if (_game is null) return;
        try
        {
            _island = IslandScenery.Load(IslandSource(file));
            _islandFile = file;
            foreach (var e in SceneryEditsFor(_current, file))
                try { ModBuilder.ApplySceneryEdit(_island, e, _current!.FilePath is null ? null : ModPackage.AssetDir(_current)); } catch (ArgumentException x) { Log($"scenery change skipped: {x.Message}"); }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or Eg2.Asura.AsuraFormatException) { Warn($"{LairName(file)}: {e.Message}"); _island = null; }
        _sceneryGroups.Items.Clear();
        _islandSel = null;
        if (_island is null) { _islandCanvas.Invalidate(); return; }
        foreach (var g in _island.Groups)
        {
            int hidden = _island.Instances.Count(i => i.Group == g.Index && _island.IsHidden(i.Index));
            _sceneryGroups.Items.Add($"{g.Name}  ×{g.Count}{(hidden > 0 ? $"  ({hidden} hidden)" : "")}");
        }
        // the island itself, not the far-off stragglers (seagulls, distant rocks): the middle 98% of instances
        var xs = _island.Instances.Select(i => i.X).Order().ToList(); var zs = _island.Instances.Select(i => i.Z).Order().ToList();
        float Q(List<float> v, double q) => v[(int)Math.Clamp(q * (v.Count - 1), 0, v.Count - 1)];
        float x0 = Q(xs, 0.01), x1 = Q(xs, 0.99), z0 = Q(zs, 0.01), z1 = Q(zs, 0.99), pad = Math.Max(x1 - x0, z1 - z0) * 0.1f;
        _islandBounds = RectangleF.FromLTRB(x0 - pad, z0 - pad, x1 + pad, z1 + pad);
        _islandScale = Math.Max(0.25f, Math.Min(20, 900f / Math.Max(_islandBounds.Width, _islandBounds.Height)));
        IslandResized();
    }

    void IslandResized()
    {
        _islandCanvas.AutoScrollMinSize = new Size((int)(_islandBounds.Width * _islandScale), (int)(_islandBounds.Height * _islandScale));
        _islandCanvas.Invalidate();
    }

    PointF IslandWorld(Point p) => new((p.X - _islandCanvas.AutoScrollPosition.X) / _islandScale + _islandBounds.Left,
                                       (p.Y - _islandCanvas.AutoScrollPosition.Y) / _islandScale + _islandBounds.Top);

    static readonly Color[] SceneryColours = Enumerable.Range(0, 64).Select(i => ColorFromHue(i * 137.5 % 360)).ToArray();
    static Color ColorFromHue(double h)
    {
        double c = 0.55, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = 0.35;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    void PaintIsland(object? sender, PaintEventArgs e)
    {
        if (_island is null) return;
        var g = e.Graphics;
        g.TranslateTransform(_islandCanvas.AutoScrollPosition.X, _islandCanvas.AutoScrollPosition.Y);
        var picked = _sceneryGroups.SelectedIndices.Cast<int>().ToHashSet();
        using var hidden = new SolidBrush(Color.FromArgb(90, 90, 90));
        using var lit = new SolidBrush(Color.White);
        var brushes = SceneryColours.Select(c => new SolidBrush(picked.Count > 0 ? Color.FromArgb(70, c) : c)).ToArray();
        try
        {
            foreach (var i in _island.Instances.OrderBy(i => picked.Contains(i.Group)))
            {
                float x = (i.X - _islandBounds.Left) * _islandScale, y = (i.Z - _islandBounds.Top) * _islandScale;
                if (x < -4 || y < -4 || x > _islandCanvas.AutoScrollMinSize.Width + 4 || y > _islandCanvas.AutoScrollMinSize.Height + 4) continue;
                var b = _island.IsHidden(i.Index) ? hidden : picked.Contains(i.Group) ? lit : brushes[i.Group % brushes.Length];
                float sz = picked.Contains(i.Group) ? 4 : 3;
                g.FillRectangle(b, x - sz / 2, y - sz / 2, sz, sz);
            }
        }
        finally { foreach (var b in brushes) b.Dispose(); }
        if (_islandSel is { } s)
        {
            using var pen = new Pen(Color.White, 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, (s.Left - _islandBounds.Left) * _islandScale, (s.Top - _islandBounds.Top) * _islandScale, s.Width * _islandScale, s.Height * _islandScale);
        }
    }

    void ShowIslandInfo(PointF w)
    {
        if (_island is null) return;
        var near = _island.Instances.OrderBy(i => (i.X - w.X) * (i.X - w.X) + (i.Z - w.Y) * (i.Z - w.Y)).FirstOrDefault();
        string what = near is not null && Math.Abs(near.X - w.X) * _islandScale < 6 && Math.Abs(near.Z - w.Y) * _islandScale < 6
            ? $"{_island.Groups[near.Group].Name}{(_island.IsHidden(near.Index) ? " (hidden)" : "")}" : "";
        string sel = _islandSel is { } s ? $" | selected {s.Width:0}x{s.Height:0}" : "";
        _islandInfo.Text = $"{w.X:0},{w.Y:0} {what}{sel} | saving to {_current?.Name ?? "(pick a mod)"}";
    }

    void AddSceneryEdit(string action, bool area)
    {
        if (_game is null || _island is null || _islandFile is null) return;
        var groups = _sceneryGroups.SelectedIndices.Cast<int>().ToList();
        if (groups.Count == 0) { Warn("Pick one or more scenery groups on the left first."); return; }
        if (area && _islandSel is null) { Warn("Drag a rectangle on the island first."); return; }
        float dx = 0, dy = 0, dz = 0;
        if (action is "move" or "copy")
        {
            var input = Prompt(action == "move" ? "Move by x, height, z (world units; e.g. 0,-50,0 sinks it underground):"
                                                : $"Put the copies at x, height, z from the originals (world units):", "0,0,10");
            if (input is null) return;
            var p = input.Split(',').Select(v => float.TryParse(v.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : float.NaN).ToArray();
            if (p.Length != 3 || p.Any(float.IsNaN)) { Warn("Three numbers, like 0,0,10."); return; }
            (dx, dy, dz) = (p[0], p[1], p[2]);
        }
        var mod = TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        var said = new List<string>();
        foreach (var gi in groups)
        {
            var g = _island.Groups[gi];
            var edit = new SceneryEdit
            {
                File = _islandFile, Group = gi, GroupName = g.Name, Action = action, DX = dx, DY = dy, DZ = dz, Note = $"{action} {g.Name}",
                X0 = area ? _islandSel!.Value.Left : null, Z0 = area ? _islandSel!.Value.Top : null, X1 = area ? _islandSel!.Value.Right : null, Z1 = area ? _islandSel!.Value.Bottom : null,
            };
            string what;
            try { what = ModBuilder.ApplySceneryEdit(_island, edit); }
            catch (ArgumentException x) { Warn(x.Message); break; }
            if (what.StartsWith("0 ")) continue;
            mod.SceneryEdits.Add(edit);
            said.Add(what);
        }
        if (said.Count == 0) { Warn("None of those groups has scenery in that rectangle."); return; }
        _sceneryRedo.Clear();
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {LairName(_islandFile)}: {string.Join("; ", said)}");
        var keep = _sceneryGroups.SelectedIndices.Cast<int>().ToList();
        var sel = _islandSel;
        OpenIsland(_islandFile);
        foreach (var k in keep) _sceneryGroups.SetSelected(k, true);
        _islandSel = sel;
    }

    string? Prompt(string question, string value)
    {
        using var dlg = new ThemedForm { Text = question, Width = 520, Height = 150, StartPosition = FormStartPosition.CenterParent };
        var box = new TextBox { Dock = DockStyle.Top, Text = value };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        dlg.Controls.Add(Bar(ok));
        dlg.Controls.Add(box);
        dlg.Controls.Add(Hint(question));
        dlg.AcceptButton = ok;
        return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    int? OneGroup()
    {
        var groups = _sceneryGroups.SelectedIndices.Cast<int>().ToList();
        if (_island is null || groups.Count != 1) { Warn("Pick exactly one scenery group on the left first."); return null; }
        return groups[0];
    }

    /// <summary>The group's model as OBJ (model space, one vertex per game vertex) to reshape in Blender and bring back.</summary>
    void ExportSceneryMesh()
    {
        if (OneGroup() is not { } g || _islandFile is null) return;
        using var dlg = new SaveFileDialog { Filter = "Wavefront OBJ (*.obj)|*.obj", FileName = $"{Path.GetFileNameWithoutExtension(_islandFile)}_group{g}.obj" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, _island!.GroupObj(g));
        Log($"{_island.Groups[g].Name} exported to {dlg.FileName}: move vertices (don't add or delete any; keep the order), then Replace mesh…");
    }

    /// <summary>An edited OBJ from Export mesh… becomes the group's model: copied into the mod's assets folder, saved as a
    /// "mesh" scenery change (every copy of that model on the island changes).</summary>
    void ReplaceSceneryMesh()
    {
        if (OneGroup() is not { } g || _islandFile is null || _island is null) return;
        using var dlg = new OpenFileDialog { Filter = "Wavefront OBJ (*.obj)|*.obj", Title = $"Replace {_island.Groups[g].Name}" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var mod = TargetMod();
        if (mod is null) return;
        try { _island.SetGroupObj(g, File.ReadAllText(dlg.FileName)); }
        catch (ArgumentException x) { Warn(x.Message); OpenIsland(_islandFile); return; }
        if (ReferenceEquals(mod, _current)) CommitEditor();
        string name = $"{Path.GetFileNameWithoutExtension(_islandFile)}_group{g}.obj";
        Directory.CreateDirectory(ModPackage.AssetDir(mod));
        File.Copy(dlg.FileName, Path.Combine(ModPackage.AssetDir(mod), name), overwrite: true);
        foreach (var old in SceneryEditsFor(mod, _islandFile).Where(e => e.Action == "mesh" && e.Group == g).ToList()) mod.SceneryEdits.Remove(old);
        mod.SceneryEdits.Add(new SceneryEdit { File = _islandFile, Group = g, GroupName = _island.Groups[g].Name, Action = "mesh", Source = name, Note = $"new shape for {_island.Groups[g].Name}" });
        _sceneryRedo.Clear();
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {LairName(_islandFile)}: {_island.Groups[g].Name} gets the model in {name}");
        OpenIsland(_islandFile);
    }

    void UndoSceneryEdit()
    {
        if (_islandFile is null || _current is not { } mod) return;
        var last = SceneryEditsFor(mod, _islandFile).LastOrDefault();
        if (last is null) { Warn($"{mod.Name} has no scenery changes on this island."); return; }
        CommitEditor();
        mod.SceneryEdits.Remove(last);
        _sceneryRedo.Add((mod, last));
        mod.Save();
        ShowEditor();
        OpenIsland(_islandFile);
    }

    void RedoSceneryEdit()
    {
        if (_islandFile is null || _current is not { } mod) return;
        int i = _sceneryRedo.FindLastIndex(x => ReferenceEquals(x.Mod, mod) && x.Edit.File.Replace('/', '\\').Equals(_islandFile, StringComparison.OrdinalIgnoreCase));
        if (i < 0) { Warn("Nothing to redo on this island."); return; }
        CommitEditor();
        mod.SceneryEdits.Add(_sceneryRedo[i].Edit);
        _sceneryRedo.RemoveAt(i);
        mod.Save();
        ShowEditor();
        OpenIsland(_islandFile);
    }

    void ResetScenery()
    {
        if (_islandFile is null || _current is not { } mod) return;
        var mine = SceneryEditsFor(mod, _islandFile).ToList();
        if (mine.Count == 0) return;
        if (MessageBox.Show(this, $"Remove all {mine.Count} scenery changes to {LairName(_islandFile)} from {mod.Name}?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        CommitEditor();
        mod.SceneryEdits.RemoveAll(mine.Contains);
        mod.Save();
        ShowEditor();
        OpenIsland(_islandFile);
    }
}
