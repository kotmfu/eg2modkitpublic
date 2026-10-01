using Eg2.Asura;
using Eg2.ModKit;
using TreeNode = Eg2.ModKit.TreeNode;

namespace Eg2.ModManager;

/// <summary>
/// Trees tab: research trees (rttr, tiers 1-5 x 7 rows, links forward within a row) and engineering trees (rctt,
/// their own grid, one prerequisite per item, lines routed by <see cref="EngineeringTree.SetItems"/>).
/// </summary>
sealed partial class MainForm
{
    const int CellW = 190, CellH = 64, Pad = 16, BoxW = 170, BoxH = 48;

    readonly ListBox _treeList = new() { Dock = DockStyle.Fill, IntegralHeight = false, FormattingEnabled = true };
    readonly TreeCanvas _canvas = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Input };
    readonly Label _treeInfo = new() { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(4) };
    readonly Label _treeOwner = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = Theme.Muted };
    readonly Label _treeHint = Hint("");
    Button _treeBg = null!;
    TabPage? _treesPage;

    // the tree being edited: nodes hold "0x..." or "@Key"; links are prerequisites (From before To)
    GameObject? _tree;
    List<TreeNode> _nodes = new();
    List<TreeLink> _links = new();
    int _sel = -1, _drag = -1;
    Dictionary<uint, string>? _guiNames;
    EngineeringTree? _engBase;

    sealed class TreeCanvas : Panel
    {
        public TreeCanvas() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    bool Eng => _tree?.Tag == "rctt";
    string ItemTag => Eng ? "rctr" : "rtrp";
    int ColMin => Eng ? 0 : 1;
    int Cols => Eng ? _engBase!.Columns : ResearchTree.MaxColumns;
    int Rows => Eng ? _engBase!.Rows : ResearchTree.MaxRows;

    TabPage TreesPage()
    {
        var page = new TabPage("Trees");
        var left = new Panel { Dock = DockStyle.Left, Width = 240, Padding = new Padding(4) };
        left.Controls.Add(_treeList);
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_canvas);
        right.Controls.Add(_treeInfo);
        _treeBg = Btn("Background…", (_, _) => TreeBackground());
        right.Controls.Add(Bar(
            Btn("Add…", (_, _) => AddTreeNode(FreeCell())),
            Btn("Remove selected", (_, _) => RemoveTreeNode()),
            Btn("Icon of selected…", (_, _) => TreeNodeIcon()),
            Btn("Requirements of selected…", (_, _) => TreeNodeRequirements()),
            _treeBg,
            Btn("Back to the game's layout", (_, _) => ResetTree()),
            _treeOwner));
        page.Controls.Add(right);
        page.Controls.Add(new Splitter { Dock = DockStyle.Left });
        page.Controls.Add(left);
        page.Controls.Add(_treeHint);
        _treeList.Format += (_, e) => { if (e.ListItem is GameObject o) e.Value = (o.Tag == "rctt" ? "Engineering: " : "Research: ") + TreeName(o); };
        _treeList.SelectedIndexChanged += (_, _) => { if (_treeList.SelectedItem is GameObject t) OpenTree(t); };
        _canvas.Paint += PaintTree;
        _canvas.MouseDown += TreeMouseDown;
        _canvas.MouseUp += TreeMouseUp;
        _canvas.MouseDoubleClick += (_, e) => { var cell = CellAt(e.Location); if (InGrid(cell) && NodeAt(cell.Col, cell.Row) < 0) AddTreeNode(cell); };
        _canvas.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveTreeNode(); };
        return page;
    }

    /// <summary>A tree's in-game name (engineering trees keep it as a text ref at +45; the generic name picker finds noise there).</summary>
    string TreeName(GameObject o) =>
        o.Tag == "rctt" && o.Body.Length > 49 && _game!.Text.TryGetValue(Bytes.U32(o.Body, 45), out var t) ? ObjectInspector.Clean(t.Text) : o.Name.Trim('"');

    static bool IsEngineeringTree(GameObject o)
    {
        try { return EngineeringTree.Parse(o.Body).Nodes.Count > 0; }
        catch (AsuraFormatException) { return false; }
    }

    void LoadTrees()
    {
        if (_game is null) return;
        var keep = _tree;
        _treeList.Items.Clear();
        foreach (var t in _game.Objects.Where(o => o.Tag == "rttr" && o.Body.Length > 300)) _treeList.Items.Add(t);
        foreach (var t in _game.Objects.Where(o => o.Tag == "rctt" && IsEngineeringTree(o))) _treeList.Items.Add(t);
        _treeList.SelectedItem = keep is not null && _treeList.Items.Contains(keep) ? keep : _treeList.Items.Count > 0 ? _treeList.Items[0] : null;
        if (_treeList.SelectedItem is GameObject cur) OpenTree(cur);
    }

    List<TreeLayout> Layouts(ModDefinition m) => Eng ? m.EngineeringTrees : m.ResearchTrees;

    TreeLayout? LayoutIn(ModDefinition? m) =>
        m is null ? null : Layouts(m).FirstOrDefault(t => t.Tree.Equals(Bytes.Hex(_tree!.ObjectId), StringComparison.OrdinalIgnoreCase));

    void OpenTree(GameObject t)
    {
        _tree = t;
        _sel = -1;
        _engBase = Eng ? EngineeringTree.Parse(t.Body) : null;
        _treeBg.Visible = Eng;
        _treeHint.Text = Eng
            ? "Drag an item to move it (dropping on another swaps them). Click one item, then Shift+click another to make the first its prerequisite " +
              "(one each; Shift+click again removes it). Lines and their bends are drawn for you. Double-click an empty cell to add an engineering item."
            : "Drag a research box to move it (dropping on another swaps them). Click one box, then Shift+click a later one in the same row to make " +
              "the first its prerequisite (Shift+click again removes it); the game allows one prerequisite each. Double-click an empty cell to add research.";
        var owner = ModsInList().FirstOrDefault(m => LayoutIn(m) is not null);
        if (LayoutIn(owner) is { } lay)
        {
            _nodes = lay.Nodes.Select(n => new TreeNode { Research = n.Research, Column = n.Column, Row = n.Row, Dx = n.Dx, Dy = n.Dy }).ToList();
            _links = lay.Links.Select(l => new TreeLink { From = l.From, To = l.To }).ToList();
            _treeOwner.Text = $"layout from mod \"{owner!.Name}\"";
        }
        else if (Eng)
        {
            var items = _engBase!.Items();
            _nodes = items.Select(i => new TreeNode { Research = Bytes.Hex(i.Id), Column = i.Column, Row = i.Row, Dx = i.Dx, Dy = i.Dy }).ToList();
            _links = items.Select((i, k) => (i, k)).Where(x => x.i.After >= 0).Select(x => new TreeLink { From = x.i.After, To = x.k }).ToList();
            _treeOwner.Text = "the game's layout";
        }
        else
        {
            var tree = ResearchTree.Parse(t.Body);
            _nodes = tree.Nodes.Select(n => new TreeNode { Research = Bytes.Hex(n.Research), Column = n.Column, Row = n.Row }).ToList();
            _links = tree.Links.Select(l => new TreeLink { From = l.From, To = l.To }).ToList();
            _treeOwner.Text = "the game's layout";
        }
        UpdateTreeInfo();
        SizeCanvas();
    }

    /// <summary>The engineering tree the current layout routes to (for drawing and checks).</summary>
    EngineeringTree RoutedEngineering()
    {
        var t = new EngineeringTree { Columns = _engBase!.Columns, Rows = _engBase.Rows };
        t.SetItems(_nodes.Select((n, i) => new EngineeringTree.Item((uint)i + 1, n.Column, n.Row, _links.FirstOrDefault(l => l.To == i)?.From ?? -1, n.Dx, n.Dy)).ToList());
        return t;
    }

    List<string> TreeProblems()
    {
        if (Eng) return RoutedEngineering().Check(i => i >= 0 && i < _nodes.Count ? ItemLabel(_nodes[i].Research) : "?");
        var check = new ResearchTree();
        check.Nodes.AddRange(_nodes.Select(n => new ResearchTree.Node { Column = n.Column, Row = n.Row }));
        check.Links.AddRange(_links.Select(l => (l.From, l.To)));
        return check.Check(i => ItemLabel(_nodes[i].Research));
    }

    /// <summary>Writes the edited layout into the target mod (the mod already holding this tree's layout wins).</summary>
    void SaveTree()
    {
        if (_tree is null) return;
        if (TreeProblems() is [var problem, ..])
        {
            Warn($"Not changed: {problem}.");
            OpenTree(_tree);   // back to the last saved layout
            return;
        }
        var mod = ModsInList().FirstOrDefault(m => LayoutIn(m) is not null) ?? TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        var lay = LayoutIn(mod);
        if (lay is null) Layouts(mod).Add(lay = new TreeLayout { Tree = Bytes.Hex(_tree.ObjectId) });
        lay.Nodes = _nodes.Select(n => new TreeNode { Research = n.Research, Column = n.Column, Row = n.Row, Dx = n.Dx, Dy = n.Dy }).ToList();
        lay.Links = _links.Select(l => new TreeLink { From = l.From, To = l.To }).ToList();
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        _treeOwner.Text = $"layout from mod \"{mod.Name}\"";
        UpdateTreeInfo();
        SizeCanvas();
    }

    void ResetTree()
    {
        if (_tree is null) return;
        foreach (var m in ModsInList().Where(m => LayoutIn(m) is not null).ToList())
        {
            if (ReferenceEquals(m, _current)) CommitEditor();
            Layouts(m).RemoveAll(t => t.Tree.Equals(Bytes.Hex(_tree.ObjectId), StringComparison.OrdinalIgnoreCase));
            m.Save();
            if (ReferenceEquals(m, _current)) ShowEditor();
            Log($"{m.Id}: tree {_tree.Name} back to the game's layout");
        }
        OpenTree(_tree);
    }

    /// <summary>Engineering trees: a new background picture (DDS), shown instead of the game's.</summary>
    void TreeBackground()
    {
        if (_tree is null || !Eng) return;
        string current = GuiName(Bytes.U32(_tree.Body, EngineeringTree.BackgroundKeyAt)) is { } bg ? Path.GetFileName(bg) : "unknown";
        using var dlg = new OpenFileDialog { Filter = "DDS texture (*.dds)|*.dds", Title = $"Background for {_tree.Name.Trim('"')} (the game's is {current}; extract it on the Assets tab for its size)" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!File.ReadAllBytes(dlg.FileName).AsSpan().StartsWith("DDS "u8)) { Warn("That isn't a DDS texture."); return; }
        SaveTree();   // makes sure a layout exists in a mod
        var mod = ModsInList().FirstOrDefault(m => LayoutIn(m) is not null);
        if (mod is null || LayoutIn(mod) is not { } lay) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        string rel = Path.Combine("art", "trees", Bytes.Hex(_tree.ObjectId), "background.dds");
        var dest = Path.Combine(ModPackage.AssetDir(mod), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(dlg.FileName, dest, overwrite: true);
        lay.Background = rel;
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {_tree.Name} gets background {dlg.FileName}");
        UpdateTreeInfo();
    }

    // ------------------------------------------------------------------ item lookups

    /// <summary>The research / engineering item behind "0x..." / "@Key" (a mod's new one is shown as its source).</summary>
    GameObject? ItemObject(string r, out NewObject? draft, out ModDefinition? draftMod)
    {
        draft = null; draftMod = null;
        if (r.StartsWith('@'))
        {
            foreach (var m in ModsInList())
                if (m.NewObjects.FirstOrDefault(o => o.Tag == ItemTag && o.Key.Equals(r[1..], StringComparison.OrdinalIgnoreCase)) is { } o)
                {
                    draft = o; draftMod = m;
                    return _game!.Objects.FirstOrDefault(x => x.Tag == ItemTag && Bytes.Hex(x.ObjectId).Equals(o.Source, StringComparison.OrdinalIgnoreCase));
                }
            return null;
        }
        return _game!.Objects.FirstOrDefault(x => x.Tag == ItemTag && Bytes.Hex(x.ObjectId).Equals(r, StringComparison.OrdinalIgnoreCase));
    }

    string ItemLabel(string r)
    {
        var o = ItemObject(r, out var draft, out _);
        string name = o is null ? r : o.Name.Split("  (")[0].Trim('"');
        if (draft is not null)
            name = draft.Texts.FirstOrDefault()?.Text is { Length: > 0 } t ? $"{t} (new)" : $"{draft.Key} (new, copy of {name})";
        return name;
    }

    string? GuiName(uint key)
    {
        _guiNames ??= _game!.PackageAssets.Where(a => a.Tag == "RSCF" && a.Name.Contains(@"\gui\", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => FurnitureArt.GuiKey(a.Name)).ToDictionary(x => x.Key, x => x.First().Name);
        return _guiNames.TryGetValue(key, out var n) ? n : null;
    }

    /// <summary>GUI texture path of an item's tree icon and the offset of its key, if it has one.</summary>
    (int Offset, string Path)? ItemIcon(byte[] body)
    {
        for (int i = ObjectHeader.Size; i + 4 <= body.Length; i++)
            if (GuiName(Bytes.U32(body, i)) is { } n && (Eng ? n.Contains(@"\icons\", StringComparison.OrdinalIgnoreCase) : n.Contains("techtree_node_icons", StringComparison.OrdinalIgnoreCase)))
                return (i, n);
        return null;
    }

    // ------------------------------------------------------------------ editing

    (int Col, int Row) CellAt(Point p) =>
        ((p.X - _canvas.AutoScrollPosition.X - Pad) / CellW + ColMin, (p.Y - _canvas.AutoScrollPosition.Y - Pad) / CellH);

    int NodeAt(int col, int row) => _nodes.FindIndex(n => n.Column == col && n.Row == row);

    (int Col, int Row) FreeCell()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = ColMin; c < ColMin + Cols; c++)
                if (NodeAt(c, r) < 0) return (c, r);
        return (-1, -1);
    }

    bool InGrid((int Col, int Row) c) => c.Col >= ColMin && c.Col < ColMin + Cols && c.Row >= 0 && c.Row < Rows;

    void TreeMouseDown(object? sender, MouseEventArgs e)
    {
        _canvas.Focus();
        var (c, r) = CellAt(e.Location);
        int hit = NodeAt(c, r);
        if (hit >= 0 && ModifierKeys.HasFlag(Keys.Shift) && _sel >= 0 && _sel != hit)
        {
            // toggle "selected is a prerequisite of clicked"; engineering items have one, so a new one replaces the old
            int existing = _links.FindIndex(l => l.From == _sel && l.To == hit);
            if (existing >= 0) _links.RemoveAt(existing);
            else if (Reaches(hit, _sel)) { Warn("That would make a loop: the second item already leads to the first."); return; }
            else
            {
                if (Eng) _links.RemoveAll(l => l.To == hit);
                _links.Add(new TreeLink { From = _sel, To = hit });
            }
            SaveTree();
            return;
        }
        _sel = hit;
        _drag = hit;
        UpdateTreeInfo();
        _canvas.Invalidate();
    }

    bool Reaches(int from, int to)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>(new[] { from });
        while (stack.TryPop(out var n))
        {
            if (n == to) return true;
            if (seen.Add(n)) foreach (var l in _links.Where(l => l.From == n)) stack.Push(l.To);
        }
        return false;
    }

    void TreeMouseUp(object? sender, MouseEventArgs e)
    {
        if (_drag < 0) return;
        var (c, r) = CellAt(e.Location);
        int from = _drag;
        _drag = -1;
        var n = _nodes[from];
        if (!InGrid((c, r)) || (c == n.Column && r == n.Row)) return;
        int other = NodeAt(c, r);
        if (other >= 0) { _nodes[other].Column = n.Column; _nodes[other].Row = n.Row; }
        n.Column = c; n.Row = r;
        SaveTree();
    }

    void AddTreeNode((int Col, int Row) cell)
    {
        if (_game is null || _tree is null) return;
        if (!InGrid(cell)) { Warn($"The tree is full ({Cols} x {Rows})."); return; }
        var used = _nodes.Select(n => n.Research).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = ModsInList().SelectMany(m => m.NewObjects.Where(o => o.Tag == ItemTag).Select(o => "@" + o.Key))
            .Concat(_game.Objects.Where(o => o.Tag == ItemTag).Select(o => Bytes.Hex(o.ObjectId)))
            .Where(r => !used.Contains(r)).Distinct().Select(r => (Id: r, Label: ItemLabel(r))).OrderBy(x => x.Label).ToList();
        using var dlg = new ThemedForm { Text = Eng ? "Add engineering item" : "Add research", Width = 520, Height = 560, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search" };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        void Fill() { list.Items.Clear(); foreach (var x in choices.Where(x => x.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase))) list.Items.Add($"{x.Label}   [{x.Id}]"); }
        search.TextChanged += (_, _) => Fill();
        Fill();
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(list);
        dlg.Controls.Add(search);
        dlg.Controls.Add(ok);
        dlg.Controls.Add(new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Theme.Muted,
            Text = $"Items already in another tree can be added too (they then show in both). New ones: make them in Browse game data game data → {(Eng ? "Engineering & crafting items" : "Research")} → \"Copy as a new object\"." });
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not string pick) return;
        string id = pick[(pick.LastIndexOf('[') + 1)..^1];
        _nodes.Add(new TreeNode { Research = id, Column = cell.Col, Row = cell.Row });
        _sel = _nodes.Count - 1;
        SaveTree();
    }

    void RemoveTreeNode()
    {
        if (_sel < 0) return;
        int i = _sel;
        _links = _links.Where(l => l.From != i && l.To != i)
            .Select(l => new TreeLink { From = l.From > i ? l.From - 1 : l.From, To = l.To > i ? l.To - 1 : l.To }).ToList();
        _nodes.RemoveAt(i);
        _sel = -1;
        SaveTree();
    }

    /// <summary>Game item: its icon is a game texture, replaced (everywhere) on the Assets tab. New item: its own icon.</summary>
    void TreeNodeRequirements()
    {
        if (_sel < 0 || _game is null) { Warn("Select an item first."); return; }
        var r = _nodes[_sel].Research;
        if (ItemObject(r, out _, out var draftMod) is { } o) ShowRequirements(r, o, draftMod);
    }

    void TreeNodeIcon()
    {
        if (_sel < 0 || _game is null) { Warn("Select an item first."); return; }
        var r = _nodes[_sel].Research;
        var o = ItemObject(r, out var draft, out var draftMod);
        if (o is null) return;
        var icon = ItemIcon(o.Body);
        if (icon is null) { Warn($"{ItemLabel(r)} has no tree icon of its own in the game data."); return; }
        if (draft is null)
        {
            if (MessageBox.Show(this, $"Its icon is {Path.GetFileName(icon.Value.Path)}. Replacing it changes it everywhere the game uses it.\n\n" +
                                      "Open it on the Assets tab (Extract / Replace with my file)?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _tabs.SelectedTab = _assetsPage;
            _assetSearch.Text = Path.GetFileNameWithoutExtension(icon.Value.Path);
            return;
        }
        using var pickDlg = new OpenFileDialog { Filter = "DDS texture (*.dds)|*.dds", Title = $"Icon for {draft.Key} (same size/format as {Path.GetFileName(icon.Value.Path)})" };
        if (pickDlg.ShowDialog(this) != DialogResult.OK) return;
        if (!File.ReadAllBytes(pickDlg.FileName).AsSpan().StartsWith("DDS "u8)) { Warn("That isn't a DDS texture."); return; }
        var mod = draftMod!;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        string rel = Path.Combine("art", draft.Key, "icon.dds");
        var dest = Path.Combine(ModPackage.AssetDir(mod), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(pickDlg.FileName, dest, overwrite: true);
        string path = Path.Combine(Path.GetDirectoryName(icon.Value.Path)!, $"modkit_{draft.Key.ToLowerInvariant()}.tga");
        mod.NewAssets.RemoveAll(a => a.Name.Equals(path, StringComparison.OrdinalIgnoreCase));
        mod.NewAssets.Add(new NewAsset { Name = path, Source = rel, Note = $"tree icon of {draft.Key}" });
        draft.Edits.RemoveAll(f => f.Offset == icon.Value.Offset);
        draft.Edits.Add(new FieldEdit { Offset = icon.Value.Offset, Type = "u32", Value = Bytes.Hex(FurnitureArt.GuiKey(path)), Note = "tree icon" });
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {draft.Key} gets its own icon {path}");
        UpdateTreeInfo();
    }

    // ------------------------------------------------------------------ drawing

    void SizeCanvas()
    {
        if (_tree is null) return;
        _canvas.AutoScrollMinSize = new Size(Pad * 2 + Cols * CellW, Pad * 2 + Rows * CellH);
        _canvas.Invalidate();
    }

    Rectangle BoxAt(int col, int row, Point scroll) => new(scroll.X + Pad + (col - ColMin) * CellW, scroll.Y + Pad + row * CellH, BoxW, BoxH);

    void PaintTree(object? sender, PaintEventArgs e)
    {
        if (_tree is null) return;
        var gfx = e.Graphics;
        gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var scroll = _canvas.AutoScrollPosition;
        using var gridPen = new Pen(Theme.Lines) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        for (int c = ColMin; c < ColMin + Cols; c++)
            for (int r = 0; r < Rows; r++)
                gfx.DrawRectangle(gridPen, BoxAt(c, r, scroll));
        for (int c = ColMin; c < ColMin + Cols; c++)
            TextRenderer.DrawText(gfx, Eng ? $"column {c + 1}" : $"tier {c}", Font, new Point(scroll.X + Pad + (c - ColMin) * CellW, scroll.Y + 1), Theme.Muted);

        using var linkPen = new Pen(Theme.Cyan, 2) { CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4, 5) };
        using var bendPen = new Pen(Theme.Cyan, 2);
        if (Eng)
        {
            // the routed lines the game will draw: through bends (junctions) in the column after each source
            var t = RoutedEngineering();
            Point Mid(EngineeringTree.Node n) { var b = BoxAt(n.Column, n.Row, scroll); return new Point(b.Left + b.Width / 2, b.Top + b.Height / 2); }
            Point Edge(EngineeringTree.Node n, bool right) { var b = BoxAt(n.Column, n.Row, scroll); return new Point(right ? b.Right : b.Left, b.Top + b.Height / 2); }
            foreach (var l in t.Links)
            {
                var a = t.Nodes[l.From]; var b = t.Nodes[l.To];
                var p1 = a.Item == 0 ? Mid(a) : Edge(a, true);
                var p2 = b.Item == 0 ? Mid(b) : Edge(b, false);
                gfx.DrawLine(b.Item == 0 ? bendPen : linkPen, p1, p2);
            }
            foreach (var j in t.Nodes.Where(n => n.Item == 0)) { var m = Mid(j); using var dot = new SolidBrush(Theme.Cyan); gfx.FillEllipse(dot, m.X - 4, m.Y - 4, 8, 8); }
        }
        else
            foreach (var l in _links)
            {
                if (l.From >= _nodes.Count || l.To >= _nodes.Count) continue;
                Rectangle a = BoxAt(_nodes[l.From].Column, _nodes[l.From].Row, scroll), b = BoxAt(_nodes[l.To].Column, _nodes[l.To].Row, scroll);
                gfx.DrawLine(linkPen, a.Right, a.Top + a.Height / 2, b.Left, b.Top + b.Height / 2);
            }
        for (int i = 0; i < _nodes.Count; i++)
        {
            var box = BoxAt(_nodes[i].Column, _nodes[i].Row, scroll);
            bool root = !_links.Any(l => l.To == i);
            using var fill = new SolidBrush(i == _sel ? Theme.Selection : root ? Theme.Highlight : Theme.Panel);
            gfx.FillRectangle(fill, box);
            using (var edge = new Pen(i == _sel ? Theme.Gold : Theme.Lines)) gfx.DrawRectangle(edge, box);
            TextRenderer.DrawText(gfx, ItemLabel(_nodes[i].Research), Font, Rectangle.Inflate(box, -4, -2),
                i == _sel ? Theme.SelectionText : Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    void UpdateTreeInfo()
    {
        _canvas.Invalidate();
        string what = Eng ? "items" : "research";
        if (_sel < 0 || _sel >= _nodes.Count)
        {
            string bg = "";
            if (Eng && _tree is not null)
                bg = LayoutIn(ModsInList().FirstOrDefault(m => LayoutIn(m) is not null))?.Background is { Length: > 0 } mine ? $" Background: your picture ({mine})."
                   : GuiName(Bytes.U32(_tree.Body, EngineeringTree.BackgroundKeyAt)) is { } g ? $" Background: {Path.GetFileName(g)}." : "";
            _treeInfo.Text = $"{_nodes.Count} {what}, {_links.Count} prerequisite links. Green = available from the start (no prerequisites).{bg}";
            return;
        }
        var n = _nodes[_sel];
        var o = ItemObject(n.Research, out var draft, out _);
        var needs = _links.Where(l => l.To == _sel).Select(l => ItemLabel(_nodes[l.From].Research)).ToList();
        var unlocks = _links.Where(l => l.From == _sel).Select(l => ItemLabel(_nodes[l.To].Research)).ToList();
        string icon = o is null ? "" : ItemIcon(o.Body) is { } ic
            ? $"Icon: {Path.GetFileName(ic.Path)}{(draft?.Edits.Any(f => f.Offset == ic.Offset) == true ? " (replaced by its own)" : "")}" : "No tree icon.";
        string where = Eng ? $"column {n.Column + 1}, row {n.Row + 1}" : $"tier {n.Column}, row {n.Row + 1}";
        _treeInfo.Text = $"{ItemLabel(n.Research)}  [{n.Research}]  {where}\n" +
                         $"Needs: {(needs.Count == 0 ? "nothing (available from the start)" : string.Join(", ", needs))}\n" +
                         $"Leads to: {(unlocks.Count == 0 ? "nothing" : string.Join(", ", unlocks))}\n{icon}";
    }
}
