using NumberStyles = System.Globalization.NumberStyles;
using Eg2.Asura;
using Eg2.ModKit;
using Microsoft.VisualBasic;

namespace Eg2.ModManager;

/// <summary>Browse tab: list of things on the left, every attribute of the selected one on the right.</summary>
sealed partial class MainForm
{
    const string CatFurniture = "Furniture", CatText = "Text", CatAll = "Everything", NewTag = "@new";

    /// <summary>Category list entry; Tag "" = all objects, null = text.</summary>
    sealed record Category(string Label, string? Tag) { public override string ToString() => Label; }

    readonly ComboBox _category = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "filter (name, tag, package, hex id / key, text)" };
    readonly DataGridView _list = ReadOnlyGrid();
    readonly Label _title = new() { Dock = DockStyle.Top, Height = 30, Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f, FontStyle.Bold), Padding = new Padding(4, 6, 0, 0) };
    readonly Label _subtitle = new() { Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Padding = new Padding(4, 2, 0, 0) };
    readonly Label _typeHelp = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 2, 4, 0) };
    readonly DataGridView _attrs = ReadOnlyGrid();
    readonly ListBox _refs = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly Panel _objView = new() { Dock = DockStyle.Fill };
    readonly TextBox _textView = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false, Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f) };
    readonly Label _target = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
    Button _back = null!, _addNew = null!, _addCost = null!, _editText = null!, _editValue = null!, _editAt = null!, _copyObject = null!, _draftText = null!, _scripts = null!, _reqs = null!, _tasks = null!, _scheme = null!, _pool = null!, _shape = null!, _jobTypes = null!, _lists = null!;
    FlowGraph.Names? _fgNames;
    readonly CheckBox _raw = new() { Text = "Show raw data", AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
    readonly Stack<object> _history = new();
    object? _shown;
    List<TextInfo> _textRows = new();

    TabPage BrowsePage()
    {
        _category.SelectedIndexChanged += (_, _) => ApplyFilter();
        Debounce(_filter, ApplyFilter);
        _list.DataBindingComplete += (_, _) =>
        {
            if (_list.Columns["Tag"] is { } tag) { tag.Visible = _category.SelectedItem is Category { Tag: "" }; tag.HeaderText = "Type"; }
            if (_list.Columns["Package"] is { } pkg) pkg.HeaderText = "From";
        };
        _list.SelectionChanged += (_, _) => { if (_list.CurrentRow?.DataBoundItem is { } x && !ReferenceEquals(x, _shown)) Show(x, remember: false); };
        var left = new Panel { Dock = DockStyle.Left, Width = 440, Padding = new Padding(4) };
        left.Controls.Add(_list);
        left.Controls.Add(_filter);
        left.Controls.Add(_category);
        var split = new Splitter { Dock = DockStyle.Left };

        _back = Btn("◀ Back", (_, _) => { if (_history.Count > 0) Show(_history.Pop(), remember: false); });
        _addNew = Btn("Copy as a new item", (_, _) => AddNewFurniture());
        _addCost = Btn("Change price", (_, _) => AddCostEdit());
        _editText = Btn("Change this text", (_, _) => AddTextEdit());
        _raw.CheckedChanged += (_, _) => { if (_shown is GameObject o) Show(o, remember: false); };
        _editValue = Btn("Edit value…", (_, _) => EditValue());
        _copyObject = Btn("Copy as a new object", (_, _) => CopyAsNewObject());
        _draftText = Btn("Change text…", (_, _) => EditDraftText());
        _editAt = Btn("Edit at offset…", (_, _) => EditAtOffset());
        _scripts = Btn("Scripts…", (_, _) => ShowScripts());
        var findScripts = Btn("Find scripts…", (_, _) => FindScripts());
        _shape = Btn("Size and slots…", (_, _) => { if (_shown is GameObject o) ShowShape(o); });
        _jobTypes = Btn("Who can do it…", (_, _) => { if (_shown is GameObject o) ShowJobTypes(o); });
        _lists = Btn("Lists…", (_, _) => { if (_shown is GameObject o) ShowLists(o); });
        _scheme = Btn("Scheme…", (_, _) => { if (_shown is GameObject o) ShowScheme(o); });
        _tasks = Btn("Tasks…", (_, _) => { if (_shown is GameObject o) ShowTasks(o); });
        _pool = Btn("Schemes offered…", (_, _) => { if (_shown is GameObject o) ShowPool(o); });
        _reqs = Btn("Requirements…", (_, _) => { if (_shown is GameObject o) ShowRequirements(SwapRef(o), o, o.DraftMod); });

        _attrs.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _attrs.Rows[e.RowIndex].DataBoundItem is not AttrRow r) return;
            if (r.Target is not null) Show(r.Target);
            else if (r.Path is "price" or "Price") AddCostEdit();
            else EditValue();
        };
        _attrs.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _attrs.Rows[e.RowIndex].DataBoundItem is AttrRow r && e.CellStyle is { } st)
            {
                if (r.Target is not null) st.ForeColor = Theme.Link;
                else if (r.IsSetting || r.Path is "price" or "setting") st.Font = new Font(_attrs.Font, FontStyle.Bold);
                else if (r.Size == 9 || r.Path is "Not decoded yet" or "Script (internal)" or "Internal name") st.ForeColor = Theme.Muted;
            }
        };
        _attrs.DataBindingComplete += (_, _) =>
        {
            foreach (var (col, w) in new[] { ("Path", 22), ("Offset", 7), ("Hex", 18), ("Int", 10), ("Float", 10), ("Meaning", 50) })
                if (_attrs.Columns[col] is { } c) { c.FillWeight = w; c.Visible = _raw.Checked || col is "Meaning" or "Path"; }
            if (_attrs.Columns["Path"] is { } what) what.HeaderText = _raw.Checked ? "Path" : "What";
            if (_attrs.Columns["Meaning"] is { } m) m.HeaderText = _raw.Checked ? "Meaning" : "Value (double-click a blue row to open it, any other to change it)";
        };
        _refs.DoubleClick += (_, _) => { if (_refs.SelectedItem is GameObject o) Show(o); };

        var refPanel = new Panel { Dock = DockStyle.Bottom, Height = 150 };
        refPanel.Controls.Add(_refs);
        refPanel.Controls.Add(new Label { Text = "Referenced by (double-click to open):", Dock = DockStyle.Top, Height = 20 });
        _objView.Controls.Add(_attrs);
        _objView.Controls.Add(refPanel);
        _objView.Controls.Add(Hint("Blue = link (double-click to open). Bold = a number you can change. \"Script setting\" = a number in this thing's game script, " +
                                   "named after the step it feeds. Grey = internal names the game uses to find things."));

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        right.Controls.Add(_objView);
        right.Controls.Add(_textView);
        right.Controls.Add(Bar(_back, _addNew, _copyObject, _addCost, _editText, _draftText, _editValue, _editAt, _scripts, findScripts, _reqs, _tasks, _scheme, _pool, _shape, _jobTypes, _lists, _raw, _target));
        right.Controls.Add(_typeHelp);
        right.Controls.Add(_subtitle);
        right.Controls.Add(_title);

        var page = new TabPage("Browse");
        page.Controls.Add(right);
        page.Controls.Add(split);
        page.Controls.Add(left);
        UpdateButtons();
        return page;
    }

    void BrowseLoaded()
    {
        _textRows = _game!.Text.Values.OrderBy(t => t.Table).ThenBy(t => t.Key).ToList();
        _history.Clear();
        _category.Items.Clear();
        _category.Items.Add(new Category(CatFurniture, "fntr"));
        _category.Items.Add(new Category(CatText, null));
        _category.Items.Add(new Category("New objects (your mods)", NewTag));
        foreach (var t in _game.Objects.GroupBy(o => o.Tag).Where(t => t.Key != "fntr")
                     .OrderBy(t => ObjectInspector.Types.ContainsKey(t.Key) ? 0 : 1).ThenBy(t => ObjectInspector.Types.TryGetValue(t.Key, out var n) ? n.List : t.Key))
            _category.Items.Add(new Category(ObjectInspector.Types.TryGetValue(t.Key, out var n) ? $"{n.List}  ({t.Count()})" : $"other: {t.Key}  ({t.Count()})", t.Key));
        _category.Items.Add(new Category($"{CatAll}  ({_game.Objects.Count:N0})", ""));
        _category.SelectedIndex = 0;
    }

    void ApplyFilter()
    {
        if (_game is null || _category.SelectedItem is not Category cat) return;
        var f = _filter.Text.Trim();
        if (cat.Tag is null)
        {
            // ponytail: capped at 5000 rows so the grid stays responsive; VirtualMode if people need to scroll everything
            _list.DataSource = _textRows.Where(t => f.Length == 0 || Has(t.Key, f) || Has(t.Text, f) || Has(t.Table, f)).Take(5000).ToList();
            return;
        }
        string tag = cat.Tag;
        if (tag == NewTag) { _list.DataSource = Drafts().Where(o => f.Length == 0 || Has(o.Name, f)).ToList(); return; }
        uint.TryParse(f.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? f[2..] : f, NumberStyles.HexNumber, null, out uint id);
        _list.DataSource = _game.Objects
            .Where(o => tag.Length == 0 || o.Tag == tag)
            .Where(o => f.Length == 0 || Has(o.Name, f) || Has(o.Package, f) || (tag.Length == 0 && Has(o.Tag, f)) || (id != 0 && o.ObjectId == id))
            .ToList();
    }

    static bool Has(string s, string f) => s.Contains(f, StringComparison.OrdinalIgnoreCase);

    void Show(object item, bool remember = true)
    {
        if (remember && _shown is not null) _history.Push(_shown);
        _shown = item;
        _objView.Visible = item is GameObject;
        _textView.Visible = item is TextInfo;
        if (item is GameObject o)
        {
            _title.Text = o.Name;
            _typeHelp.Text = ObjectInspector.TypeHelp(o.Tag);
            _subtitle.Text = $"{o.Tag}  ·  package {o.Package}  ·  {(o.IsRecord ? "furniture record" : "id " + Bytes.Hex(o.ObjectId))}  ·  {o.Body.Length:N0} bytes";
            var rows = _raw.Checked ? null : ObjectInspector.FriendlyRows(o, _game!);
            rows = rows is { Count: > 0 } ? rows : ObjectInspector.Rows(o, _game!);
            if (o.Draft is { } d)
            {
                _subtitle.Text = $"new {o.Tag} in mod \"{o.DraftMod!.Name}\"  ·  refer to it as @{d.Key}  ·  copy of {d.Source}";
                rows = rows.Select(r => d.Texts.FirstOrDefault(t => t.Offset == r.Offset) is { } t
                    ? new AttrRow { Path = r.Path, Offset = r.Offset, Hex = r.Hex, Int = r.Int, Float = r.Float, Size = r.Size, Editable = r.Editable, Meaning = $"\"{t.Text}\"  (your new text)" }
                    : r).ToList();
            }
            _attrs.DataSource = rows;
            _refs.DataSource = o.Draft is null ? ObjectInspector.ReferencedBy(o, _game!).ToList() : new List<GameObject>();
        }
        else if (item is TextInfo t)
        {
            _title.Text = t.Key;
            _typeHelp.Text = $"Game text, used as: {ObjectInspector.TextLabel(t.Key)}. \"Change this text\" changes it everywhere it's shown.";
            _subtitle.Text = $"text table {t.Table}  ·  hash {Bytes.Hex(Eg2.Asura.Chunks.TextTable.KeyHash(t.Key))}";
            _textView.Text = ObjectInspector.Clean(t.Text);
        }
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var o = _shown as GameObject;
        _back.Enabled = _history.Count > 0;
        _addNew.Visible = _addCost.Visible = o is { IsRecord: true, Draft: null };
        _copyObject.Visible = o is { IsRecord: false, Draft: null };
        _draftText.Visible = o?.Draft is not null;
        _editValue.Visible = o is not null;
        _scripts.Visible = o is not null && o.Body.AsSpan().IndexOf("FlowGraph/"u8) >= 0;
        _reqs.Visible = o is not null && Requirements.Supports(o.Tag);
        _tasks.Visible = o is not null && o.Tag == "robj";
        _scheme.Visible = o is not null && o.Tag == "rscm";
        _pool.Visible = o is { Tag: "rspl", IsRecord: false, Draft: null };
        _shape.Visible = o is not null && o.IsRecord && o.Tag == "fntr";
        _lists.Visible = o is not null && o.Draft is null && ObjectLists.Tree(o.Body, ListStart(o)) is { } lt && ObjectLists.Find(lt).Count > 0;
        _jobTypes.Visible = o is not null && o.Tag == "rjob" && JobTypes.Parse(o.Body) is not null;
        _editAt.Visible = o is not null && _raw.Checked;
        _raw.Visible = o is not null;
        _editText.Visible = _shown is TextInfo;
    }

    void UpdateTargets() =>
        _target.Text = _current is null ? "" : $"changes go to: {_current}";

    /// <summary>The mod edits go into; offers to create one when none is selected.</summary>
    ModDefinition? TargetMod()
    {
        if (_current is not null) return _current;
        if (MessageBox.Show(this, "Changes are saved in a mod. Create a new mod called \"My changes\" for them?\n\n" +
                                  "(To use an existing mod instead, select it on the Mods tab first.)", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return null;
        var mod = NewMod("my-changes", "My changes", select: false);
        if (mod is not null) { _modList.SelectedItem = mod; SelectMod(mod); }
        return mod;
    }

    FurnitureRow? ShownFurniture() =>
        _shown is GameObject { IsRecord: true } o ? _game!.Furniture.FirstOrDefault(r => r.Package == o.Package && r.Name == o.RecordName) : null;

    void AddNewFurniture()
    {
        if (ShownFurniture() is not { } row || TargetMod() is not { } mod) return;
        var taken = new HashSet<string>(ModsInList().SelectMany(m => m.NewFurniture).Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
        string id = row.Name + "_Mod";
        for (int n = 2; taken.Contains(id) || _game!.FindFurniture(id) is not null; n++) id = $"{row.Name}_Mod{n}";
        EditMod(mod, 0, () => mod.NewFurniture.Add(new NewFurniture
        {
            Id = id, Donor = row.Name, Cost = row.Cost ?? 0,
            DisplayName = (row.DisplayName.Length > 0 ? row.DisplayName : row.Name) + " (Mod)",
        }));
    }

    void AddCostEdit()
    {
        if (ShownFurniture() is not { } row || TargetMod() is not { } mod) return;
        if (row.Cost is null) { Warn($"{row.Name} has no cost field."); return; }
        EditMod(mod, 1, () =>
        {
            if (!mod.FurnitureEdits.Any(e => e.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase)))
                mod.FurnitureEdits.Add(new FurnitureEdit { Name = row.Name, Cost = row.Cost.Value });
        });
    }

    void AddTextEdit()
    {
        if (_shown is not TextInfo row || TargetMod() is not { } mod) return;
        EditMod(mod, 2, () =>
        {
            if (!mod.TextEdits.Any(e => e.Table.Equals(row.Table, StringComparison.OrdinalIgnoreCase) && e.Key == row.Key))
                mod.TextEdits.Add(new TextEdit { Table = row.Table, Key = row.Key, Text = row.Text });
        });
    }

    /// <summary>For values the decoder shows mid-word (packed data): pick any offset, edit 4 bytes there.</summary>
    void EditAtOffset()
    {
        if (_shown is not GameObject o) return;
        int start = _attrs.CurrentRow?.DataBoundItem is AttrRow cur ? cur.Offset : 0;
        string s = Interaction.InputBox($"{o.Name}\nByte offset to edit (0-{o.Body.Length - 4}); 4 bytes are replaced:", Text, start.ToString()).Trim();
        if (!int.TryParse(s, out int off) || off < 0 || off + 4 > o.Body.Length) return;
        uint u = Bytes.U32(o.Body, off);
        float f = BitConverter.ToSingle(o.Body, off);
        EditValue(new AttrRow
        {
            Offset = off, Size = 4, Editable = true, Hex = Convert.ToHexString(o.Body, off, 4),
            Int = ((int)u).ToString(), Float = float.IsFinite(f) ? f.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) : "",
            Meaning = "raw 4 bytes at offset " + off,
        });
    }

    void EditValue(AttrRow? picked = null)
    {
        if (_shown is not GameObject o || (picked ?? _attrs.CurrentRow?.DataBoundItem) is not AttrRow row) return;
        if (!row.Editable || (!o.IsRecord && row.Offset < ObjectHeader.Size))
        { Warn("That row isn't an editable value (object header, property marker or string)."); return; }
        if (TargetMod() is not { } mod) return;

        string current = row.Size == 1 ? row.Int
            : row.Int.Length > 0 ? row.Int
            : row.Float.Length > 0 ? row.Float + "f"
            : "0x" + Bytes.U32(o.Body, row.Offset).ToString("x8");
        string input = Interaction.InputBox(
            $"{o.Name}\noffset {row.Offset} ({row.Size} bytes)   int: {row.Int}   float: {row.Float}\n{row.Meaning}\n\n" +
            "New value: whole number (5, -1, 0x1a2b), decimal with a trailing f (2.5f), or @Name for one of your new objects.", Text, current).Trim();
        if (input.Length == 0 || input == current) return;

        var fe = new FieldEdit
        {
            Package = o.Package, Tag = o.Tag, Object = o.Key, Offset = row.Offset,
            Expect = Convert.ToHexString(o.Body, row.Offset, row.Size),
            Note = $"{o.Name}: {row.Meaning}".Trim().TrimEnd(':'),
        };
        bool hex = input.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (input.StartsWith('@'))
        {
            if (row.Size != 4) { Warn("@references need a 4-byte value."); return; }
            if (!ModsInList().SelectMany(m => m.NewObjects).Any(x => x.Key.Equals(input[1..], StringComparison.OrdinalIgnoreCase)))
            { Warn($"No new object called {input[1..]} in your mods."); return; }
        }
        (fe.Type, fe.Value) = input.StartsWith('@') ? ("u32", input)
            : row.Size == 1 ? ("u8", input)
            : !hex && input.EndsWith('f') ? ("f32", input[..^1])
            : input.StartsWith('-') ? ("i32", input)
            : ("u32", input);
        try { if (!input.StartsWith('@')) fe.Encode(); }
        catch (Exception e) when (e is FormatException or OverflowException) { Warn($"'{input}' is not a valid {fe.Type} value."); return; }

        if (o.Draft is { } d)
        {
            d.Edits.RemoveAll(x => x.Offset == fe.Offset);
            d.Edits.Add(fe);
            SaveDraft(o);
            return;
        }
        EditMod(mod, 3, () =>
        {
            mod.FieldEdits.RemoveAll(x => x.Package == fe.Package && x.Tag == fe.Tag && x.Object == fe.Object && x.Offset == fe.Offset);
            mod.FieldEdits.Add(fe);
        });
    }

    // ------------------------------------------------------------------ new objects (copies of any data object)

    /// <summary>Every new object in the mods list, as browsable objects (source bytes with the draft's values applied).</summary>
    IEnumerable<GameObject> Drafts()
    {
        if (_game is null) yield break;
        foreach (var m in ModsInList())
            foreach (var d in m.NewObjects)
                if (DraftObject(m, d) is { } o) yield return o;
    }

    GameObject? DraftObject(ModDefinition m, NewObject d)
    {
        var src = _game!.Objects.FirstOrDefault(x => !x.IsRecord && x.Package.Equals(d.Package, StringComparison.OrdinalIgnoreCase) && x.Tag == d.Tag
                                                  && x.Key.Equals(d.Source, StringComparison.OrdinalIgnoreCase));
        if (src is null) return null;
        var body = (byte[])src.Body.Clone();
        foreach (var f in d.Edits)
            try { if (!f.Value.TrimStart().StartsWith('@')) f.Encode().CopyTo(body, f.Offset); } catch (Exception e) when (e is FormatException or OverflowException or ArgumentException) { }
        string title = d.Texts.OrderBy(t => t.Offset).FirstOrDefault()?.Text ?? d.Key;
        return new GameObject { Tag = d.Tag, Package = src.Package, ObjectId = 0, Body = body, Draft = d, DraftMod = m, Name = $"★ {title}  (@{d.Key}, copy of {src.Name})" };
    }

    void CopyAsNewObject()
    {
        if (_shown is not GameObject { IsRecord: false, Draft: null } o || TargetMod() is not { } mod) return;
        string suggested = new string(o.Name.Where(char.IsLetterOrDigit).Take(24).ToArray()) + "_Mod";
        string key = Interaction.InputBox(
            $"New {o.Tag} copied from {o.Name}.\n\nGive it a short name (letters, digits, _). Other edits refer to it as @Name, " +
            "e.g. type @Name as a research tree's value to put it in the tree.", Text, suggested).Trim();
        if (key.Length == 0) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z0-9_]+$")) { Warn("Letters, digits and _ only."); return; }
        if (mod.NewObjects.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) { Warn($"\"{mod.Name}\" already has a new object called {key}."); return; }
        if (ReferenceEquals(mod, _current)) CommitEditor();
        var d = new NewObject { Key = key, Package = o.Package, Tag = o.Tag, Source = o.Key, Note = $"copy of {o.Name}" };
        mod.NewObjects.Add(d);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: new {o.Tag} @{key} (copy of {o.Name})");
        if (DraftObject(mod, d) is { } draft) Show(draft);
        _status.Text = $"Made @{key}. Change its texts and values here; it goes into the game when you apply.";
    }

    void EditDraftText()
    {
        if (_shown is not GameObject { Draft: { } d } o || _attrs.CurrentRow?.DataBoundItem is not AttrRow row) return;
        uint hash = row.Offset + 4 <= o.Body.Length ? Bytes.U32(o.Body, row.Offset) : 0;
        var current = d.Texts.FirstOrDefault(t => t.Offset == row.Offset)?.Text
                      ?? (_game!.Text.TryGetValue(hash, out var ti) ? ObjectInspector.Clean(ti.Text) : null);
        if (current is null) { Warn("Pick a text row (one that shows text \"…\") first."); return; }
        string input = Interaction.InputBox($"New text for this slot of @{d.Key}:", Text, current);
        if (input.Length == 0 || input == current) return;
        d.Texts.RemoveAll(t => t.Offset == row.Offset);
        d.Texts.Add(new NewObjectText { Offset = row.Offset, Text = input });
        SaveDraft(o);
    }

    void SaveDraft(GameObject o)
    {
        var mod = o.DraftMod!;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        if (DraftObject(mod, o.Draft!) is { } fresh) Show(fresh, remember: false);
    }
}
