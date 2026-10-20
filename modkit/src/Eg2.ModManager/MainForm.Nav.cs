using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Sidebar navigation: the pages stay in <see cref="_tabs"/> (its header strip is hidden). Two layouts, switched at the top:
/// Simple groups pages by what they change (Henchmen, Lair, Research…),
/// Advanced is the tool-by-tool layout with Browse game data and Game files. The mode is Settings.AdvancedTools.
/// </summary>
sealed partial class MainForm
{
    readonly FlowLayoutPanel _nav = new()
    {
        // wide enough for 176 px buttons + padding + the vertical scrollbar: a horizontal scrollbar would hide the last entry
        Dock = DockStyle.Left, Width = 216, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        Padding = new Padding(8, 10, 4, 8),
    };
    readonly List<(TabPage Page, Button Button)> _navItems = new();
    readonly Button _simpleBtn = new() { Text = "Simple", Width = 86, Height = 28, Margin = new Padding(0, 0, 4, 0) };
    readonly Button _advancedBtn = new() { Text = "Advanced", Width = 86, Height = 28, Margin = Padding.Empty };
    /// <summary>The mod every page saves into (also the one open on My mods); see <see cref="FillEditing"/>.</summary>
    readonly ComboBox _editing = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 176, Margin = new Padding(0, 0, 0, 2) };
    readonly Button _newModBtn = new() { Text = "New mod…", Width = 176, Height = 28, Margin = new Padding(0, 0, 0, 4) };
    Button? _navSel;

    void BuildNav()
    {
        _tabs.Appearance = TabAppearance.FlatButtons;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(0, 1);
        _simpleBtn.Click += (_, _) => SetMode(false);
        _advancedBtn.Click += (_, _) => SetMode(true);
        _editing.SelectionChangeCommitted += (_, _) => PickEditingMod(_editing.SelectedItem as ModDefinition);
        _editing.FormattingEnabled = true;
        _editing.Format += (_, e) => { if (e.ListItem is ModDefinition m) e.Value = ModLabel(m); };
        // the list opens as wide as the longest mod name (the box itself stays sidebar-wide)
        _editing.DropDown += (_, _) => _editing.DropDownWidth = Math.Max(_editing.Width, _editing.Items.Cast<object>()
            .Select(i => TextRenderer.MeasureText(_editing.GetItemText(i), _editing.Font).Width).DefaultIfEmpty(0).Max() + SystemInformation.VerticalScrollBarWidth + 8);
        _newModBtn.Click += (_, _) => NewEditingMod();
        _tabs.Selected += (_, _) =>
        {
            if (_navSel is null || !_navItems.Any(n => n.Button == _navSel && n.Page == _tabs.SelectedTab))
                _navSel = _navItems.FirstOrDefault(n => n.Page == _tabs.SelectedTab).Button;
            StyleNav();
        };
        ShowAdvanced(_settings.AdvancedTools);
    }

    void SetMode(bool advanced)
    {
        if (_settings.AdvancedTools == advanced) return;
        _settings.AdvancedTools = advanced;
        _settings.Save();
        ShowAdvanced(advanced);
        StyleNav();
    }

    void FillNav(bool advanced)
    {
        _nav.SuspendLayout();
        _nav.Controls.Clear();
        _navItems.Clear();
        var mode = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        mode.Controls.AddRange(new Control[] { _simpleBtn, _advancedBtn });
        _nav.Controls.Add(mode);
        _nav.Controls.Add(new Label { Text = "SAVING CHANGES TO", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 10, 0, 4) });
        _nav.Controls.Add(_editing);
        _nav.Controls.Add(_newModBtn);

        Group("Start", (_homePage, "Home"));
        Group("Play", (_tweaksPage, "Quick tweaks"), (_applyPage, "Install mods"));
        if (advanced)
        {
            Group("Make a mod", (_modsPage, "My mods"), (GridPage("Research"), "Research numbers"),
                  (_furniturePage, "Furniture"), (GridPage("Furniture Effects"), "Furniture effects"), (GridPage("Temperature"), "Temperature"), (GridPage("Henchmen"), "Henchmen"), (GridPage("Doomsday"), "Doomsday devices"), (_treesPage!, "Research trees"), (_mapsPage!, "Lair maps"), (_scenarioPage!, "Scenario"),
                  (_islandPage!, "Islands"));
            Group("Characters", (GridPage("Characters: Henchmen"), "Henchmen"), (GridPage("Characters: Geniuses"), "Geniuses"),
                  (GridPage("Characters: Minions"), "Minions"), (GridPage("Characters: Agents"), "Agents"));
            Group("Advanced", (_browsePage, "Browse game data"), (_assetsPage!, "Game files"));
        }
        else
        {
            Group("Henchmen", (GridPage("Characters: Henchmen"), "Henchmen"), (GridPage("Henchmen"), "Hiring limit"), (GridPage("Traits and powers"), "Traits & powers"), (GridPage("Spoken lines"), "Spoken lines"));
            Group("Geniuses", (GridPage("Characters: Geniuses"), "Geniuses"));
            Group("Minions", (GridPage("Characters: Minions"), "Minion types"), (GridPage("Minion specialisations"), "Specialisations"), (GridPage("Jobs"), "Jobs & stat changes"));
            Group("Agents", (GridPage("Characters: Agents"), "Agent types"), (GridPage("Super agencies"), "Super agencies"), (GridPage("Story chains"), "Story chains"));
            Group("Lair", (_furniturePage, "Furniture"), (GridPage("Furniture Effects"), "Furniture effects"), (GridPage("Temperature"), "Temperature"),
                  (_mapsPage!, "Lair maps"), (_islandPage!, "Islands"));
            Group("Research", (GridPage("Research"), "Research numbers"), (_treesPage!, "Research trees"));
            Group("World & story", (_scenarioPage!, "Scenario"), (GridPage("Scheme names"), "Schemes"),
                  (GridPage("Scheme rewards"), "Scheme rewards"), (GridPage("Regions"), "Regions"), (GridPage("Objectives"), "Objectives"));
            Group("Doomsday", (GridPage("Doomsday"), "Doomsday devices"));
            Group("Your mods", (_modsPage, "My mods"));
        }
        Group("", (_settingsPage, "Settings"));
        _nav.Controls.Add(BottomSpace());
        _navSel = _navItems.FirstOrDefault(n => n.Page == _tabs.SelectedTab).Button;
        _nav.ResumeLayout();
    }

    /// <summary>The mods in the picker; the selected one is the mod open on My mods, else the Quick tweaks target.</summary>
    void FillEditing()
    {
        _editing.BeginUpdate();
        _editing.Items.Clear();
        _editing.Items.AddRange(ModsInList().OrderBy(m => m.Id != QuickTweaks.ModId).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        if (TweaksMod() is null) _editing.Items.Insert(0, "Quick tweaks (new mod)");
        _editing.SelectedItem = (object?)TweaksMod() ?? _editing.Items[0];
        _editing.EndUpdate();
    }

    /// <summary>Every page saves into <paramref name="mod"/> from now on (null = the Quick tweaks mod, made on first change).</summary>
    void PickEditingMod(ModDefinition? mod)
    {
        SaveSettingsPage(_tabs.SelectedTab);   // into the mod picked before
        _tweakTargetId = mod?.Id ?? QuickTweaks.ModId;
        if (mod is not null) _modList.SelectedItem = mod;   // opens it on My mods too: Browse, Furniture and copies use that one
        else SelectMod(null);
        FillEditing();
        ReloadPage();
    }

    void NewEditingMod()
    {
        string name = Microsoft.VisualBasic.Interaction.InputBox("Name of the new mod (you can change it later on My mods):", Text, "My mod").Trim();
        if (name.Length == 0) return;
        string id = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (NewMod(id.Length > 0 ? id : "new-mod", name, select: false) is { } mod) PickEditingMod(mod);
    }

    /// <summary>Re-reads the page on show after the target mod changed (its values come from that mod).</summary>
    void ReloadPage()
    {
        var page = _tabs.SelectedTab;
        if (page == _tweaksPage) LoadTweaks();
        else if (GridFor(page) is { } tg) LoadGrid(tg);
        else if (page == _furniturePage) FillFurnitureList();
        UpdateTargets();
    }

    /// <summary>Last control of a scrolling flow panel: AutoScroll stops at the last control and ignores the panel's bottom
    /// padding, so without this the end of the list sits half under the status bar.</summary>
    static Control BottomSpace() => new Label { AutoSize = false, Width = 1, Height = 24, Margin = Padding.Empty };

    TabPage GridPage(string title) => _tabs.TabPages.Cast<TabPage>().First(p => p.Text == title);

    void Group(string title, params (TabPage Page, string Label)[] items)
    {
        if (title.Length > 0)
            _nav.Controls.Add(new Label { Text = title.ToUpperInvariant(), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 14, 0, 4), UseMnemonic = false });
        else _nav.Controls.Add(new Label { AutoSize = false, Height = 14, Width = 1 });
        foreach (var (page, label) in items)
        {
            var b = new Button { Text = label, Width = 176, Height = 32, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1), Padding = new Padding(8, 0, 0, 0), UseMnemonic = false };
            b.Click += (_, _) => { _navSel = b; if (_tabs.SelectedTab == page) StyleNav(); else _tabs.SelectedTab = page; };
            _navItems.Add((page, b));
            _nav.Controls.Add(b);
        }
    }

    void ShowAdvanced(bool on)
    {
        FillNav(on);
        // labels that depend on it: mod names (with/without version and id), research names (with/without record names)
        int sel = _modList.SelectedIndex;
        _modList.BeginUpdate();
        for (int i = 0; i < _modList.Items.Count; i++)
        {
            bool ticked = _modList.GetItemChecked(i);
            _modList.Items[i] = _modList.Items[i];   // re-runs Format
            _modList.SetItemChecked(i, ticked);
        }
        _modList.EndUpdate();
        _modList.SelectedIndex = sel;
        FitModList();
        if (_furnList.Items.Count > 0) FillFurnitureList();
        foreach (var tg in _grids) { tg.Items.Invalidate(); if (tg.Items.SelectedItem is string item) tg.Title.Text = Plain(item); }
    }

    /// <summary>Flat sidebar look (after <see cref="Theme.Apply"/>, which gives every button a gold frame).</summary>
    void StyleNav()
    {
        _nav.BackColor = Theme.Panel;
        foreach (var (page, b) in _navItems)
        {
            bool sel = b == _navSel;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = sel ? Theme.Selection : Theme.Panel;
            b.ForeColor = sel ? Theme.SelectionText : Theme.Text;
            b.Font = new Font(Font.FontFamily, 10f, sel ? FontStyle.Bold : FontStyle.Regular);
        }
        foreach (var b in new[] { _simpleBtn, _advancedBtn })
        {
            bool on = (b == _advancedBtn) == _settings.AdvancedTools;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Theme.Muted;
            b.BackColor = on ? Theme.Selection : Theme.Panel;
            b.ForeColor = on ? Theme.SelectionText : Theme.Muted;
        }
    }
}
