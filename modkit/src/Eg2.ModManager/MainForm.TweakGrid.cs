using System.ComponentModel;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Research and Furniture Effects tabs: items on the left, the selected item's values on the right.
/// Type a new value to change it, clear it to go back to the game's. Stored in the "quick-tweaks" mod
/// like the Quick Tweaks page. A value can appear under several items (a shared job); it's one setting.
/// </summary>
sealed partial class MainForm
{
    sealed class TweakRow
    {
        public string Setting { get; init; } = "";
        [DisplayName("Game value")] public string Game { get; init; } = "";
        [DisplayName("Your value")] public string Value { get; set; } = "";
        [Browsable(false)] public Tweak Tweak { get; init; } = null!;
        [Browsable(false)] public bool Changed => IsChange(Value, Tweak.Default ?? "");
    }

    static bool IsChange(string value, string game) => value.Trim().Length > 0 && value.Trim() != game;

    sealed class TweakGrid
    {
        public required TabPage Page { get; init; }
        public required Func<GameData, List<Tweak>> Source { get; init; }
        public ListBox Items { get; } = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20 };
        public DataGridView Grid { get; } = EditGrid();
        public TextBox Filter { get; } = new() { Dock = DockStyle.Top, PlaceholderText = "Search…" };
        public CheckBox ChangedOnly { get; } = new() { Text = "Only show changed", Dock = DockStyle.Top };
        public Label Title { get; } = new() { Dock = DockStyle.Top, Height = 32, Font = new Font(SystemFonts.DefaultFont.FontFamily, 13f, FontStyle.Bold) };
        public Label Note { get; } = new() { Dock = DockStyle.Top, Height = 72, ForeColor = Theme.Muted };
        /// <summary>Opens the selected item's recordings in Game files (shown when its rows have an <see cref="Tweak.AssetSearch"/>).</summary>
        public Button Voice { get; set; } = null!;
        /// <summary>Opens the selected character's combat and body sounds in Game files (<see cref="Tweak.SoundFiles"/>).</summary>
        public Button Sounds { get; set; } = null!;
        /// <summary>The page lists data objects you can copy (New) and your copies (★, Delete).</summary>
        public bool Copies { get; init; }
        public GameData? LoadedFor { get; set; }
        public List<Tweak> Cache { get; set; } = new();
        /// <summary>Your value per <see cref="Tweak.Key"/> (shared by every item that shows it).</summary>
        public Dictionary<string, string> Values { get; } = new();
        public bool Changed(string item) => Cache.Any(t => t.Group == item && IsChange(Values.GetValueOrDefault(t.Key, ""), t.Default ?? ""));
    }

    readonly List<TweakGrid> _grids = new();

    TabPage TweakGridPage(string title, string hint, Func<GameData, List<Tweak>> source, bool copies = false)
    {
        var tg = new TweakGrid { Page = new TabPage(title), Source = source, Copies = copies };
        var g = tg.Grid;
        g.AllowUserToAddRows = g.AllowUserToDeleteRows = false;
        g.RowHeadersVisible = false;
        g.BackgroundColor = Theme.Input;
        g.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewColumn c in g.Columns) c.ReadOnly = c.DataPropertyName != nameof(TweakRow.Value);
            foreach (var (col, w) in new[] { ("Setting", 50), ("Game", 20), ("Value", 20) })
                if (g.Columns[col] is { } c) c.FillWeight = w;
            // drop-downs (abilities, looks): "" = the game's own
            if (g.Columns["Value"] is not { } vc) return;
            foreach (DataGridViewRow row in g.Rows)
                if (row.DataBoundItem is TweakRow { Tweak.Choices: { } choices } r)
                {
                    var items = choices.Select(c => new KeyValuePair<string, string>(c.Value, c.Label)).Prepend(new("", "")).ToList();
                    if (r.Value.Length > 0 && items.All(i => i.Key != r.Value)) items.Add(new(r.Value, r.Value));
                    row.Cells[vc.Index] = new DataGridViewComboBoxCell
                    {
                        DataSource = items, ValueMember = "Key", DisplayMember = "Value", FlatStyle = FlatStyle.Flat,
                        DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing, Value = r.Value,
                    };
                }
        };
        g.CurrentCellDirtyStateChanged += (_, _) => { if (g.CurrentCell is DataGridViewComboBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        g.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && g.Rows[e.RowIndex].DataBoundItem is TweakRow r && e.CellStyle is { } st && r.Changed)
                st.Font = new Font(g.Font, FontStyle.Bold);
        };
        g.CellValidating += (_, e) =>
        {
            if (g.Columns[e.ColumnIndex].DataPropertyName != nameof(TweakRow.Value) || g.Rows[e.RowIndex].DataBoundItem is not TweakRow r) return;
            string v = e.FormattedValue?.ToString()?.Trim() ?? "";
            if (v.Length > 0 && Invalid(r.Tweak, v) is { } err) { Warn($"{r.Setting}: {err}"); e.Cancel = true; }
        };
        g.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || g.Rows[e.RowIndex].DataBoundItem is not TweakRow r) return;
            tg.Values[r.Tweak.Key] = g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
            tg.Items.Invalidate();   // re-bold the item
        };
        g.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && g.Rows[e.RowIndex].DataBoundItem is TweakRow r) e.ToolTipText = r.Tweak.Help;
        };

        var list = tg.Items;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            string item = (string)list.Items[e.Index];
            using var font = tg.Changed(item) ? new Font(list.Font, FontStyle.Bold) : null;
            TextRenderer.DrawText(e.Graphics, Plain(item), font ?? list.Font, e.Bounds, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        list.SelectedIndexChanged += (_, _) => ShowGridItem(tg);
        Debounce(tg.Filter, () => ShowGridItems(tg));
        tg.ChangedOnly.CheckedChanged += (_, _) => ShowGridItems(tg);

        var left = new Panel { Dock = DockStyle.Left, Width = 340, Padding = new Padding(4) };
        left.Controls.Add(list);
        left.Controls.Add(tg.ChangedOnly);
        left.Controls.Add(tg.Filter);

        tg.Voice = Btn("Hear or replace voice…", (_, _) => OpenRecordings(tg));
        tg.Voice.Visible = false;
        tg.Sounds = Btn("Hear or replace combat and body sounds…", (_, _) => OpenSounds(tg));
        tg.Sounds.Visible = false;
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 4, 4) };
        right.Controls.Add(g);
        var bar = Bar(tg.Voice, tg.Sounds);
        if (copies) bar.Controls.AddRange(new Control[]
        {
            Btn("New (copy of this)…", (_, _) => CopyGridItem(tg)), Btn("Use this copy…", (_, _) => UseGridCopy(tg)), Btn("Delete my copy", (_, _) => DeleteGridItem(tg)),
        });
        right.Controls.Add(bar);
        right.Controls.Add(tg.Note);
        right.Controls.Add(tg.Title);

        tg.Page.Controls.Add(right);
        tg.Page.Controls.Add(new Splitter { Dock = DockStyle.Left });
        tg.Page.Controls.Add(left);
        tg.Page.Controls.Add(Hint(hint));
        _grids.Add(tg);
        return tg.Page;
    }

    /// <summary>Error message for a value this setting can't hold, or null.</summary>
    static string? Invalid(Tweak t, string v)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (t.Text is not null) return null;
        if (t.SchemeField is not null) return float.TryParse(v, System.Globalization.NumberStyles.Float, inv, out _) ? null : "needs a number, e.g. 3600";
        if (t.Blue is not null) return t.BlueType switch
        {
            "f32" => float.TryParse(v, System.Globalization.NumberStyles.Float, inv, out _) ? null : "needs a number, e.g. 12.5",
            "u8" => byte.TryParse(v, out _) ? null : "needs a whole number from 0 to 255",
            "u32" => uint.TryParse(v, out _) ? null : "needs a whole number, 0 or more",
            _ => v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(v[2..], System.Globalization.NumberStyles.HexNumber, inv, out _)
                ? null : "needs a hex id like 0x1a2b3c4d",
        };
        try { new FieldEdit { Type = t.Field!.Type, Value = v }.Encode(); return null; }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            return t.Field!.Type switch { "f32" => "needs a number, e.g. 12.5", "u8" => "needs 0 or 1", "u32" => "needs a whole number, 0 or more", _ => "needs a whole number" };
        }
    }

    /// <summary>Game files, showing the streamed recordings of the selected item (listen, extract, replace).</summary>
    void OpenRecordings(TweakGrid tg)
    {
        if (tg.Cache.FirstOrDefault(t => t.Group == tg.Items.SelectedItem as string && t.AssetSearch is not null)?.AssetSearch is not { } search) return;
        _assetKind.SelectedItem = "Streamed sound";
        _assetSearch.Text = search;
        _tabs.SelectedTab = _assetsPage;   // loads the asset index on first use, then filters
    }

    /// <summary>Game files, showing exactly the WAVs the selected character's attacks and body play.</summary>
    void OpenSounds(TweakGrid tg)
    {
        if (tg.Cache.FirstOrDefault(t => t.Group == tg.Items.SelectedItem as string && t.SoundFiles is not null)?.SoundFiles is not { } files) return;
        _settingAssetFilter = true;
        _assetKind.SelectedItem = "Sound";
        _assetSearch.Text = "";
        _settingAssetFilter = false;
        _assetOnly = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)!;
        _assetOnlyFor = Plain(tg.Items.SelectedItem as string ?? "");
        if (_tabs.SelectedTab == _assetsPage) FilterAssets();
        else _tabs.SelectedTab = _assetsPage;   // loads the asset index on first use, then filters
    }

    /// <summary>New: a copy of the selected object (asks for its name), listed with ★ and edited right here.</summary>
    void CopyGridItem(TweakGrid tg)
    {
        if (tg.Cache.FirstOrDefault(t => t.Group == tg.Items.SelectedItem as string && t.Object is not null)?.Object is not { } o) return;
        if (o.Draft is { } d)   // a copy of a copy starts from the same game object
            o = _game!.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == d.Tag && x.Package.Equals(d.Package, StringComparison.OrdinalIgnoreCase)
                                                && x.Key.Equals(d.Source, StringComparison.OrdinalIgnoreCase)) ?? o;
        SaveGrid(tg);
        if (MakeCopy(o) is not ({ } mod, { } copy)) return;
        LoadGrid(tg);
        tg.Items.SelectedItem = tg.Cache.FirstOrDefault(t => t.Draft == copy)?.Group;
        _status.Text = $"Made @{copy.Key} in {mod.Name}. Change its texts and numbers here. It shows in the game once something uses it: " +
                       "type @" + copy.Key + " as the value of a link (a scheme pool, research tree, objective step…) on Browse game data.";
    }

    /// <summary>
    /// Use: every place in the game data that links to the copy's original; ticked ones link to the copy instead (a value edit
    /// "@Key" in the copy's mod). Scheme pools can take the copy as well as the original (a pool edit).
    /// </summary>
    void UseGridCopy(TweakGrid tg)
    {
        if (tg.Cache.FirstOrDefault(t => t.Group == tg.Items.SelectedItem as string && t.Object is not null)?.Object is not { Draft: { } d, DraftMod: { } mod })
        { Warn("Pick one of your copies (★) first, or make one with \"New (copy of this)…\"."); return; }
        var src = _game!.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == d.Tag && x.Package.Equals(d.Package, StringComparison.OrdinalIgnoreCase)
                                                    && x.Key.Equals(d.Source, StringComparison.OrdinalIgnoreCase));
        if (src is null) return;
        string me = "@" + d.Key;
        var needle = Eg2.Asura.Bytes.Le(src.ObjectId);
        var places = new List<(GameObject Obj, int At)>();
        foreach (var x in ObjectInspector.ReferencedBy(src, _game))
            for (int i = x.IsRecord ? 0 : Eg2.Asura.ObjectHeader.Size; (i = x.Body.AsSpan(i).IndexOf(needle) is var k and >= 0 ? i + k : -1) >= 0; i += 4)
                places.Add((x, i));
        if (places.Count == 0) { Warn($"Nothing in the game links to {src.Name}, so there's no place to put the copy."); return; }

        bool Wired((GameObject Obj, int At) p) => p.Obj.Tag == "rspl"
            ? mod.PoolEdits.Any(e => e.Pool.Equals(Eg2.Asura.Bytes.Hex(p.Obj.ObjectId), StringComparison.OrdinalIgnoreCase) && e.Add.Contains(me, StringComparer.OrdinalIgnoreCase))
              || mod.FieldEdits.Any(f => f.Value == me && f.Object == p.Obj.Key && f.Offset == p.At)
            : mod.FieldEdits.Any(f => f.Value == me && f.Package == p.Obj.Package && f.Tag == p.Obj.Tag && f.Object == p.Obj.Key && f.Offset == p.At);
        using var dlg = new ThemedForm { Text = $"Use @{d.Key}", Width = 760, Height = 560, StartPosition = FormStartPosition.CenterParent };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        foreach (var p in places)
        {
            string what = ObjectInspector.Types.TryGetValue(p.Obj.Tag, out var ti) ? ti.One : p.Obj.Tag;
            list.Items.Add($"{what}: {p.Obj.Name.Trim('"')}  (link at byte {p.At})", Wired(p));
        }
        var addToPools = new CheckBox { Text = "Scheme pools: offer the copy as well as the original (instead of replacing it)", AutoSize = true, Checked = true, Padding = new Padding(4) };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        dlg.Controls.Add(list);
        dlg.Controls.Add(Hint($"Ticked places link to your copy @{d.Key} instead of {src.Name.Trim('"')}. Saved in \"{mod.Name}\"."));
        dlg.Controls.Add(Bar(ok, addToPools));
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.FieldEdits.RemoveAll(f => f.Value == me);   // this copy's earlier wiring, redone from the ticks
        foreach (var e in mod.PoolEdits) e.Add.RemoveAll(a => a.Equals(me, StringComparison.OrdinalIgnoreCase));
        mod.PoolEdits.RemoveAll(e => e.Add.Count == 0 && e.Remove.Count == 0);
        for (int i = 0; i < places.Count; i++)
        {
            if (!list.GetItemChecked(i)) continue;
            var (x, at) = places[i];
            if (x.Tag == "rspl" && addToPools.Checked)
            {
                string pool = Eg2.Asura.Bytes.Hex(x.ObjectId);
                var pe = mod.PoolEdits.FirstOrDefault(e => e.Pool.Equals(pool, StringComparison.OrdinalIgnoreCase));
                if (pe is null) mod.PoolEdits.Add(pe = new PoolEdit { Pool = pool, Note = $"offer @{d.Key}" });
                if (!pe.Add.Contains(me, StringComparer.OrdinalIgnoreCase)) pe.Add.Add(me);
            }
            else mod.FieldEdits.Add(new FieldEdit
            {
                Package = x.Package, Tag = x.Tag, Object = x.Key, Offset = at, Type = "u32", Value = me,
                Expect = Convert.ToHexString(x.Body, at, 4), Note = $"{x.Name.Trim('"')}: use @{d.Key} instead of {src.Name.Trim('"')}",
            });
        }
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        _status.Text = $"@{d.Key} is used in {list.CheckedItems.Count} place(s). Install mods to try it.";
    }

    /// <summary>Delete: removes one of your copies (game entries can't be deleted).</summary>
    void DeleteGridItem(TweakGrid tg)
    {
        if (tg.Cache.FirstOrDefault(t => t.Group == tg.Items.SelectedItem as string && t.Object is not null)?.Object is not { Draft: { } d, DraftMod: { } mod })
        { Warn("Only your own copies (★) can be deleted; the game's entries stay. Clear your values to put one back as it was."); return; }
        if (MessageBox.Show(this, $"Delete your copy @{d.Key} from \"{mod.Name}\"? Anything that refers to @{d.Key} will stop building.", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.NewObjects.Remove(d);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: deleted new object @{d.Key}");
        LoadGrid(tg);
    }

    TweakGrid? GridFor(TabPage? page) => _grids.FirstOrDefault(g => g.Page == page);

    void LoadGrid(TweakGrid tg)
    {
        if (_game is null) { tg.Cache = new(); tg.Values.Clear(); ShowGridItems(tg); return; }
        if (tg.LoadedFor != _game || tg.Copies) { tg.Cache = tg.Source(_game); tg.LoadedFor = _game; }   // copies can change on other pages
        var mod = TweaksMod();
        if (mod is not null && ReferenceEquals(mod, _current)) CommitEditor();
        tg.Values.Clear();
        foreach (var t in tg.Cache) tg.Values[t.Key] = (t.Draft is not null ? t.ValueIn(t.DraftMod!) : mod is null ? null : t.ValueIn(mod)) ?? "";
        ShowGridItems(tg);
    }

    void ShowGridItems(TweakGrid tg)
    {
        string q = tg.Filter.Text.Trim();
        var keep = tg.Items.SelectedItem as string;
        var items = tg.Cache
            .Where(t => q.Length == 0 || t.Group.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Label.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Group).Distinct()
            .Where(i => !tg.ChangedOnly.Checked || tg.Changed(i))
            .ToArray();
        tg.Items.BeginUpdate();
        tg.Items.Items.Clear();
        tg.Items.Items.AddRange(items);
        tg.Items.EndUpdate();
        int at = keep is null ? -1 : Array.IndexOf(items, keep);
        tg.Items.SelectedIndex = at >= 0 ? at : items.Length > 0 ? 0 : -1;
        if (items.Length == 0) ShowGridItem(tg);
    }

    void ShowGridItem(TweakGrid tg)
    {
        tg.Grid.EndEdit();
        string? item = tg.Items.SelectedItem as string;
        var tweaks = item is null ? new() : tg.Cache.Where(t => t.Group == item).ToList();
        tg.Title.Text = (item is null ? null : Plain(item)) ?? (_game is null ? "Game data is still loading…" : "Nothing matches.");
        tg.Note.Text = string.Join("\n", tweaks.Select(t => t.Help).Where(h => h.Length > 0).Distinct());
        tg.Voice.Visible = tweaks.Any(t => t.AssetSearch is not null);
        tg.Sounds.Visible = tweaks.Any(t => t.SoundFiles is not null);
        tg.Grid.DataSource = tweaks.Select(t => new TweakRow
        {
            Setting = t.Label, Tweak = t, Value = tg.Values.GetValueOrDefault(t.Key, ""),
            Game = t.Choices?.FirstOrDefault(c => c.Value == t.Default).Label ?? (t.Text is not null ? ObjectInspector.Clean(t.Default ?? "") : t.Default ?? ""),
        }).ToList();
    }

    void SaveGrid(TweakGrid tg)
    {
        tg.Grid.EndEdit();
        if (tg.Cache.Count == 0) return;
        bool Change(Tweak t) => tg.Values.GetValueOrDefault(t.Key, "") is var v && IsChange(v, t.Default ?? "") && Invalid(t, v.Trim()) is null;
        // your copies' rows go into the copy, in the mod that has it
        var drafts = tg.Cache.Where(t => t.Draft is not null).DistinctBy(t => t.Key).ToList();
        foreach (var t in drafts)
        {
            if (ReferenceEquals(t.DraftMod, _current)) CommitEditor();
            t.Remove(t.DraftMod!);
            if (Change(t)) t.Set(t.DraftMod!, tg.Values[t.Key].Trim());
        }
        foreach (var m in drafts.Select(t => t.DraftMod!).Distinct())
        {
            m.Save();
            if (ReferenceEquals(m, _current)) ShowEditor();
        }
        var unique = tg.Cache.Where(t => t.Draft is null).DistinctBy(t => t.Key).ToList();
        bool any = unique.Any(Change);
        var mod = TweaksMod();
        if (mod is null && !any) return;
        if (mod is null && (mod = NewTweaksMod()) is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        foreach (var t in unique)
        {
            if (Change(t)) t.Set(mod, tg.Values[t.Key].Trim());
            else t.Remove(mod);
        }
        FinishTweaksMod(mod, any);
    }

    /// <summary>Save whichever settings page is showing (Quick Tweaks or a grid) into the quick-tweaks mod.</summary>
    void SaveSettingsPage(TabPage? page)
    {
        if (page == _tweaksPage) SaveTweaks();
        else if (GridFor(page) is { } tg) SaveGrid(tg);
    }

    /// <summary>"Aim Changer  (Generic_Research_Blank)" -> "Aim Changer" unless advanced tools are on (internal record names are noise for most people).</summary>
    string Plain(string item) =>
        _settings.AdvancedTools ? item : System.Text.RegularExpressions.Regex.Replace(item, @"  \([^()]*_[^()]*\)$", "");
}
