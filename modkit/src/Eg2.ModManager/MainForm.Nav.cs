namespace Eg2.ModManager;

/// <summary>
/// Sidebar navigation: the pages stay in <see cref="_tabs"/> (its header strip is hidden), grouped here as
/// Start / Play / Make a mod / Advanced. Advanced pages only show when Settings → "Show advanced tools" is on.
/// </summary>
sealed partial class MainForm
{
    readonly FlowLayoutPanel _nav = new()
    {
        Dock = DockStyle.Left, Width = 196, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        Padding = new Padding(8, 10, 4, 8),
    };
    readonly List<(TabPage Page, Button Button)> _navItems = new();
    readonly List<Control> _advancedNav = new();

    void BuildNav()
    {
        _tabs.Appearance = TabAppearance.FlatButtons;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(0, 1);

        Group("Start", false, (_homePage, "Home"));
        Group("Play", false, (_tweaksPage, "Quick tweaks"), (_applyPage, "Install mods"));
        Group("Make a mod", false, (_modsPage, "My mods"), (GridPage("Research"), "Research numbers"),
              (_furniturePage, "Furniture"), (GridPage("Furniture Effects"), "Furniture effects"), (GridPage("Temperature"), "Temperature"), (_treesPage!, "Research trees"), (_mapsPage!, "Lair maps"),
              (_islandPage!, "Islands"));
        Group("Advanced", true, (_browsePage, "Browse game data"), (_assetsPage!, "Game files"));
        Group("", false, (_settingsPage, "Settings"));

        _tabs.Selected += (_, _) => StyleNav();
        ShowAdvanced(_settings.AdvancedTools);
    }

    TabPage GridPage(string title) => _tabs.TabPages.Cast<TabPage>().First(p => p.Text == title);

    void Group(string title, bool advanced, params (TabPage Page, string Label)[] items)
    {
        var controls = new List<Control>();
        if (title.Length > 0)
            controls.Add(new Label { Text = title.ToUpperInvariant(), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 14, 0, 4) });
        else controls.Add(new Label { AutoSize = false, Height = 14, Width = 1 });
        foreach (var (page, label) in items)
        {
            var b = new Button { Text = label, Width = 176, Height = 32, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1), Padding = new Padding(8, 0, 0, 0) };
            b.Click += (_, _) => _tabs.SelectedTab = page;
            _navItems.Add((page, b));
            controls.Add(b);
        }
        _nav.Controls.AddRange(controls.ToArray());
        if (advanced) _advancedNav.AddRange(controls);
    }

    void ShowAdvanced(bool on)
    {
        foreach (var c in _advancedNav) c.Visible = on;
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
            bool sel = _tabs.SelectedTab == page;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = sel ? Theme.Selection : Theme.Panel;
            b.ForeColor = sel ? Theme.SelectionText : Theme.Text;
            b.Font = new Font(Font.FontFamily, 10f, sel ? FontStyle.Bold : FontStyle.Regular);
        }
    }
}
