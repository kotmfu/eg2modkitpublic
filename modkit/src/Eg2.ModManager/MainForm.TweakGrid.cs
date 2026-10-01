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
        [Browsable(false)] public bool Changed => IsChange(Value, Game);
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
        public GameData? LoadedFor { get; set; }
        public List<Tweak> Cache { get; set; } = new();
        /// <summary>Your value per <see cref="Tweak.Key"/> (shared by every item that shows it).</summary>
        public Dictionary<string, string> Values { get; } = new();
        public bool Changed(string item) => Cache.Any(t => t.Group == item && IsChange(Values.GetValueOrDefault(t.Key, ""), t.Default ?? ""));
    }

    readonly List<TweakGrid> _grids = new();

    TabPage TweakGridPage(string title, string hint, Func<GameData, List<Tweak>> source)
    {
        var tg = new TweakGrid { Page = new TabPage(title), Source = source };
        var g = tg.Grid;
        g.AllowUserToAddRows = g.AllowUserToDeleteRows = false;
        g.RowHeadersVisible = false;
        g.BackgroundColor = Theme.Input;
        g.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewColumn c in g.Columns) c.ReadOnly = c.DataPropertyName != nameof(TweakRow.Value);
            foreach (var (col, w) in new[] { ("Setting", 50), ("Game", 20), ("Value", 20) })
                if (g.Columns[col] is { } c) c.FillWeight = w;
        };
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

        var apply = Theme.Primary(Btn("Install mods", async (_, _) => await Apply()));
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 4, 4) };
        right.Controls.Add(g);
        right.Controls.Add(tg.Note);
        right.Controls.Add(tg.Title);

        tg.Page.Controls.Add(right);
        tg.Page.Controls.Add(new Splitter { Dock = DockStyle.Left });
        tg.Page.Controls.Add(left);
        tg.Page.Controls.Add(Bar(apply, LaunchButton()));
        tg.Page.Controls.Add(Hint(hint));
        _grids.Add(tg);
        return tg.Page;
    }

    /// <summary>Error message for a value this setting can't hold, or null.</summary>
    static string? Invalid(Tweak t, string v)
    {
        try { new FieldEdit { Type = t.Field!.Type, Value = v }.Encode(); return null; }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            return t.Field!.Type switch { "f32" => "needs a number, e.g. 12.5", "u8" => "needs 0 or 1", "u32" => "needs a whole number, 0 or more", _ => "needs a whole number" };
        }
    }

    TweakGrid? GridFor(TabPage? page) => _grids.FirstOrDefault(g => g.Page == page);

    void LoadGrid(TweakGrid tg)
    {
        if (_game is null) { tg.Cache = new(); tg.Values.Clear(); ShowGridItems(tg); return; }
        if (tg.LoadedFor != _game) { tg.Cache = tg.Source(_game); tg.LoadedFor = _game; }
        var mod = TweaksMod();
        if (mod is not null && ReferenceEquals(mod, _current)) CommitEditor();
        tg.Values.Clear();
        foreach (var t in tg.Cache) tg.Values[t.Key] = (mod is null ? null : t.ValueIn(mod)) ?? "";
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
        tg.Grid.DataSource = tweaks.Select(t => new TweakRow
        {
            Setting = t.Label, Game = t.Default ?? "", Tweak = t, Value = tg.Values.GetValueOrDefault(t.Key, ""),
        }).ToList();
    }

    void SaveGrid(TweakGrid tg)
    {
        tg.Grid.EndEdit();
        if (tg.Cache.Count == 0) return;
        var unique = tg.Cache.DistinctBy(t => t.Key).ToList();
        bool Change(Tweak t) => tg.Values.GetValueOrDefault(t.Key, "") is var v && IsChange(v, t.Default ?? "") && Invalid(t, v.Trim()) is null;
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
