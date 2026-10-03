using System.ComponentModel;
using System.Diagnostics;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Code-only WinForms shell: Quick Tweaks, Mods, Browse, Apply, Settings, plus a log that
/// stays collapsed until wanted (or something fails).
/// </summary>
sealed partial class MainForm : Form
{
    readonly Settings _settings = Settings.Load();
    GameData? _game;
    CancellationTokenSource? _cts;
    ModDefinition? _current;

    readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
    readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel2Collapsed = true };
    readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Font = new Font("Consolas", 9f),
    };
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Visible = false };
    readonly ToolStripStatusLabel _cancel = new() { Text = "Cancel", IsLink = true, Visible = false };
    readonly ToolStripStatusLabel _logToggle = new() { Text = "Show log", IsLink = true };

    TabPage _tweaksPage = null!, _modsPage = null!, _browsePage = null!, _applyPage = null!, _settingsPage = null!;

    // settings
    readonly TextBox _gamePath = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly Label _gameStatus = new() { AutoSize = true };
    readonly TextBox _modsPath = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly ComboBox _lang = new() { Width = 80 };
    readonly Label _dataStatus = new() { AutoSize = true, Text = "Game data not loaded." };

    // mods
    readonly CheckedListBox _modList = new() { Dock = DockStyle.Fill, IntegralHeight = false, CheckOnClick = true };
    readonly TextBox _modId = new() { Dock = DockStyle.Fill }, _modName = new() { Dock = DockStyle.Fill },
        _modVersion = new() { Dock = DockStyle.Fill }, _modAuthor = new() { Dock = DockStyle.Fill },
        _modDesc = new() { Dock = DockStyle.Fill, Multiline = true, Height = 50, ScrollBars = ScrollBars.Vertical };
    readonly Panel _editor = new() { Dock = DockStyle.Fill, Visible = false };
    readonly Label _noMod = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f),
        Text = "Pick a mod on the left to see everything it changes.\n\nTick a mod to use it in the game (then Install mods).\n" +
               "New starts an empty mod of your own; Import… adds a mod file someone sent you (or drop it on the list).\n\n" +
               "Changes are made on the pages in the sidebar. The expert page, Browse game data, is under Settings → Show advanced tools.",
    };
    readonly TabControl _modTabs = new() { Dock = DockStyle.Fill };
    readonly DataGridView _newGrid = EditGrid(), _costGrid = EditGrid(), _textEditGrid = EditGrid(), _fieldGrid = EditGrid(), _runtimeGrid = EditGrid(), _assetGrid = EditGrid(), _objectGrid = EditGrid();
    readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly CheckBox _technical = new() { Text = "Show technical columns", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    // apply
    readonly ListView _files = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    readonly ListView _installedMods = new() { Dock = DockStyle.Top, Height = 200, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    Panel _modsLeft = null!;
    readonly Label _installed = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 0), Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f) };

    public MainForm()
    {
        Text = "Evil Genius 2 ModKit";
        Size = new Size(1200, 850);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        _split.Panel1.Controls.Add(_tabs);
        _split.Panel1.Controls.Add(_nav);
        _split.Panel2.Controls.Add(_log);
        var strip = new StatusStrip();
        strip.Items.AddRange(new ToolStripItem[] { _status, _progress, _cancel, _logToggle });
        _cancel.Click += (_, _) => _cts?.Cancel();
        _logToggle.Click += (_, _) => ShowLog(_split.Panel2Collapsed);
        Controls.Add(_split);
        Controls.Add(strip);

        _tabs.TabPages.Add(_tweaksPage = TweaksPage());
        _tabs.TabPages.Add(TweakGridPage("Research",
            "Pick a research project on the left; its time, cost and rewards are on the right. Type a new value in \"Your value\", clear it to go back to the game's. " +
            "Research time is in the game's own units (Larger Storage Bays = 300). Changed items are bold. Saved in the \"Quick tweaks\" mod.",
            QuickTweaks.Research));
        _tabs.TabPages.Add(TweakGridPage("Furniture Effects",
            "Pick an item on the left to see what using it does to a minion: + restores the stat, - drains it. Type a new value in \"Your value\", " +
            "clear it to go back to the game's. Changed items are bold. Saved in the \"Quick tweaks\" mod.",
            QuickTweaks.FurnitureEffects));
        _tabs.TabPages.Add(TweakGridPage("Temperature",
            "Lair temperature. Furniture: heat (+) or cold (-) each item gives off. Story: steps that shift the whole lair. Bands: the temperature range each " +
            "level (Freezing … Melting) covers. Traits: what minions and agents get on those tiles. " +
            "Type a new value in \"Your value\", clear it to go back to the game's. To switch temperature off entirely, use the tick box in Quick tweaks. Saved in the \"Quick tweaks\" mod.",
            QuickTweaks.Temperature));
        _tabs.TabPages.Add(TweakGridPage("Henchmen",
            "How many henchmen you can hire. Each recruit mission and crime-lord story checks it separately: \"Henchman limit\" (5 in the game) and, " +
            "in the stories, \"limit - 1\" for when a recruit is already on the way. For the usual case use \"Henchman limit\" in Quick tweaks. Saved in the \"Quick tweaks\" mod.",
            QuickTweaks.Henchmen));
        _tabs.TabPages.Add(_furniturePage = FurniturePage());
        _tabs.TabPages.Add(_treesPage = TreesPage());
        _tabs.TabPages.Add(_mapsPage = MapsPage());
        _tabs.TabPages.Add(_islandPage = IslandPage());
        _tabs.TabPages.Add(_modsPage = ModsPage());
        _tabs.TabPages.Add(_browsePage = BrowsePage());
        _tabs.TabPages.Add(_assetsPage = AssetsPage());
        _tabs.TabPages.Add(_applyPage = ApplyPage());
        _tabs.TabPages.Add(_settingsPage = SettingsPage());
        _tabs.TabPages.Insert(0, _homePage = HomePage());   // built last: its links point at the other pages
        _tabs.SelectedIndex = 0;
        BuildNav();
        _tabs.Deselecting += (_, e) => SaveSettingsPage(e.TabPage);
        _tabs.Selected += (_, e) =>
        {
            if (e.TabPage == _homePage) RefreshHome();
            if (e.TabPage == _tweaksPage) LoadTweaks();
            if (GridFor(e.TabPage) is { } tg) LoadGrid(tg);
            if (e.TabPage == _applyPage) RefreshInstalled();
            if (e.TabPage == _assetsPage) _ = LoadAssets();
            if (e.TabPage == _treesPage) LoadTrees();
            if (e.TabPage == _furniturePage) LoadFurniturePage();
            if (e.TabPage == _mapsPage) LoadMaps();
            if (e.TabPage == _islandPage) LoadIslands();
            UpdateTargets();
        };

        _gamePath.Text = string.IsNullOrEmpty(_settings.GamePath) ? GameInstall.FindDefault() ?? "" : _settings.GamePath;
        _modsPath.Text = _settings.ModsFolder;
        _lang.Text = _settings.Language;
        ValidateGame();
        LoadMods();
        LoadTweaks();
        RefreshInstalled();
        Activated += (_, _) => ReloadChangedMods();   // picks up mods edited outside while the window was in the background
        Shown += async (_, _) =>
        {
            if (GameInstall.Validate(_gamePath.Text) is null) await LoadGameData();
            else { _tabs.SelectedTab = _settingsPage; Warn("Couldn't find Evil Genius 2 automatically. Pick its folder on the Settings tab."); }
        };
        FormClosing += OnClosing;
        Theme.Apply(this);
        StyleNav();
        RefreshHome();
    }

    // ------------------------------------------------------------------ pages

    TabPage SettingsPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lang.Items.AddRange(new object[] { "en", "fr", "ge", "it", "sp", "ru", "pb", "cs", "ct", "am" });
        _gamePath.Leave += (_, _) => ValidateGame();
        _modsPath.Leave += (_, _) => ModsFolderChanged();

        t.Controls.Add(Lbl("Game folder"), 0, 0);
        t.Controls.Add(_gamePath, 1, 0);
        t.Controls.Add(Btn("Browse…", (_, _) => { if (PickFolder(_gamePath)) ValidateGame(); }), 2, 0);
        t.Controls.Add(_gameStatus, 1, 1);
        t.Controls.Add(Lbl("Mods folder"), 0, 2);
        t.Controls.Add(_modsPath, 1, 2);
        t.Controls.Add(Btn("Browse…", (_, _) => { if (PickFolder(_modsPath)) ModsFolderChanged(); }), 2, 2);
        t.Controls.Add(Lbl("Text language"), 0, 3);
        t.Controls.Add(_lang, 1, 3);
        t.Controls.Add(Btn("Reload game data", async (_, _) => await LoadGameData()), 0, 4);
        t.Controls.Add(_dataStatus, 1, 4);
        var classic = new CheckBox { Text = "Classic Windows look (takes effect next start)", AutoSize = true, Checked = _settings.ClassicLook };
        classic.CheckedChanged += (_, _) => { _settings.ClassicLook = classic.Checked; _settings.Save(); };
        t.Controls.Add(classic, 1, 5);
        var advanced = new CheckBox { Text = "Show advanced tools (Browse game data, Game files: every object, texture and sound)", AutoSize = true, Checked = _settings.AdvancedTools };
        advanced.CheckedChanged += (_, _) => { _settings.AdvancedTools = advanced.Checked; _settings.Save(); ShowAdvanced(advanced.Checked); };
        t.Controls.Add(advanced, 1, 6);

        var diag = new GroupBox { Text = "Diagnostics", Dock = DockStyle.Top, Height = 110, Padding = new Padding(8) };
        diag.Controls.Add(Bar(
            Btn("Self-test", async (_, _) => { ShowLog(true); await Run("Self-test", (log, _) => SelfTest.Run(log)); }),
            Btn("Verify game files", async (_, _) =>
            {
                if (GameRoot() is not { } root) return;
                ShowLog(true);
                await Run("Verify game files", (log, ct) => InstallVerifier.Run(root, log, ct));
            }),
            Btn("Clear log", (_, _) => _log.Clear())));
        diag.Controls.Add(Hint("Self-test checks ModKit's file-format code. Verify re-reads every game archive and checks ModKit can rebuild it exactly (takes a few minutes)."));

        var page = new TabPage("Settings");
        page.Controls.Add(diag);
        page.Controls.Add(t);
        return page;
    }

    TabPage ModsPage()
    {
        _modsLeft = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(4) };
        var left = _modsLeft;
        _modList.HorizontalScrollbar = true;
        left.Controls.Add(_modList);
        left.Controls.Add(Hint("Ticked mods are applied to the game. Order only matters for display; clashing edits are reported instead of silently overriding."));
        left.Controls.Add(Bar(
            Btn("New", (_, _) => NewMod()), Btn("Import…", (_, _) => ImportMod()), Btn("Export for players…", (_, _) => ExportMod()),
            Btn("Delete", (_, _) => DeleteMod()),
            Btn("Untick all", (_, _) => { for (int i = 0; i < _modList.Items.Count; i++) _modList.SetItemChecked(i, false); }),
            Btn("Refresh", (_, _) => RefreshMods()),
            Btn("Open folder", (_, _) => OpenFolder(_modsPath.Text))));
        _modList.SelectedIndexChanged += (_, _) => SelectMod(_modList.SelectedItem as ModDefinition);
        _modList.FormattingEnabled = true;
        _modList.Format += (_, e) => { if (e.ListItem is ModDefinition m) e.Value = ModLabel(m); };

        var meta = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(4) };
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        meta.Controls.Add(Lbl("Name"), 0, 0); meta.Controls.Add(_modName, 1, 0);
        meta.Controls.Add(Lbl("Id"), 2, 0); meta.Controls.Add(_modId, 3, 0);
        meta.Controls.Add(Lbl("Author"), 0, 1); meta.Controls.Add(_modAuthor, 1, 1);
        var version = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        version.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        version.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        version.Controls.Add(_modVersion, 0, 0);
        version.Controls.Add(Btn("Next version", (_, _) => _modVersion.Text = ModDefinition.NextVersion(_modVersion.Text)), 1, 0);
        meta.Controls.Add(Lbl("Version"), 2, 1); meta.Controls.Add(version, 3, 1);
        meta.Controls.Add(Lbl("Description"), 0, 2); meta.Controls.Add(_modDesc, 1, 2);
        meta.SetColumnSpan(_modDesc, 3);

        foreach (var (title, grid) in new[] { ("New furniture", _newGrid), ("Prices", _costGrid), ("Text", _textEditGrid), ("Data values", _fieldGrid), ("Runtime", _runtimeGrid), ("Assets", _assetGrid), ("New objects", _objectGrid) })
        {
            var p = new TabPage(title);
            p.Controls.Add(grid);
            _modTabs.TabPages.Add(p);
            grid.DataBindingComplete += (_, _) => ApplyColumnVisibility(grid);
        }
        _technical.CheckedChanged += (_, _) => { foreach (var g in new[] { _newGrid, _costGrid, _textEditGrid, _fieldGrid, _runtimeGrid, _assetGrid, _objectGrid }) ApplyColumnVisibility(g); };
        _modTabs.TabPages.Add(ChangesPage());
        _modTabs.TabPages[0].Controls.Add(Bar(Btn("Textures of selected item…", async (_, _) => await EditItemTextures())));
        _modTabs.TabPages[2].Controls.Add(Bar(Btn("Export all game text…", (_, _) => ExportText()), Btn("Import text sheet…", (_, _) => ImportText())));
        _modTabs.TabPages[0].Controls.Add(Hint("Copies of existing items with their own name, price and description. Easiest to add from Browse game data → Furniture → \"Copy as a new item\". " +
                                               "Tick OwnArt to give an item its own copy of the model and textures, then change them with \"Textures of selected item…\"."));
        _modTabs.TabPages[1].Controls.Add(Hint("Price changes for existing furniture. Add from Browse game data → Furniture → \"Change cost\"."));
        _modTabs.TabPages[2].Controls.Add(Hint("Changed in-game text. Add from Browse game data → Text → \"Edit in mod\"."));
        _modTabs.TabPages[3].Controls.Add(Hint("Individual values changed from Browse game data (\"Edit value…\"). The Note says what each one is."));
        _preset.Items.AddRange(RuntimePresets.All.ToArray<object>());
        _preset.SelectedIndex = 0;
        _modTabs.TabPages[4].Controls.Add(Bar(_preset, Btn("Add preset", (_, _) => AddPreset())));
        _modTabs.TabPages[4].Controls.Add(Hint("Values changed while the game runs (limits such as the minion hard cap). Usually easier from the Quick Tweaks tab."));
        _modTabs.TabPages[5].Controls.Add(Hint("Sounds, textures, animations and models this mod replaces. Add them from the Assets tab (\"Replace with my file…\"); " +
                                               "the files live in the mod's \"<id>.assets\" folder."));
        _modTabs.TabPages[6].Controls.Add(Hint("Brand-new objects of any type (schemes, objectives, research, traits…) copied from the game. Make them in Browse game data (\"Copy as a new object\"), " +
                                               "edit their texts and values there, and point existing objects at them by typing @Name as a value."));
        _editor.Controls.Add(_modTabs);
        _editor.Controls.Add(_sections);
        BuildSections();
        _editor.Controls.Add(Bar(_technical));
        _editor.Controls.Add(meta);

        var page = new TabPage("Mods");
        page.Controls.Add(_editor);
        page.Controls.Add(_noMod);
        page.Controls.Add(new Splitter { Dock = DockStyle.Left, MinSize = 200 });
        page.Controls.Add(left);
        return page;
    }

    TabPage ApplyPage()
    {
        _files.Columns.Add("File written to the game folder", 700);
        _files.Columns.Add("Size", 120, HorizontalAlignment.Right);
        var apply = Theme.Primary(Btn("Install mods", async (_, _) => await Apply()));
        var remove = Btn("Remove mods from game", async (_, _) => await Uninstall());
        remove.Font = new Font(remove.Font.FontFamily, 11f);

        _installedMods.Columns.Add("Mod", 320);
        _installedMods.Columns.Add("In the game", 110);
        _installedMods.Columns.Add("Yours", 110);
        _installedMods.Columns.Add("Status", 420);
        var page = new TabPage("Apply");
        page.Controls.Add(_files);
        page.Controls.Add(new Splitter { Dock = DockStyle.Top });
        page.Controls.Add(_installedMods);
        page.Controls.Add(Hint("Apply builds every ticked mod and installs the result; nothing in the base game is overwritten. " +
                               "Remove takes everything back out. Saves made while mods are active may depend on them."));
        page.Controls.Add(Bar(apply, remove, LaunchButton(), Btn("Open game folder", (_, _) => OpenFolder(_gamePath.Text))));
        page.Controls.Add(_installed);
        return page;
    }

    // ------------------------------------------------------------------ settings

    void ValidateGame()
    {
        var err = GameInstall.Validate(_gamePath.Text);
        _gameStatus.Text = err is null ? "✓ Evil Genius 2 found" : "✗ " + err;
        _gameStatus.ForeColor = err is null ? Theme.Good : Theme.Bad;
        RefreshInstalled();
    }

    Button LaunchButton() => Btn("Launch game", (_, _) =>
    {
        if (GameRoot() is { } root && GameInstall.Launch(root) is { } err) Warn(err);
    });

    string? GameRoot()
    {
        var err = GameInstall.Validate(_gamePath.Text);
        if (err is null) return Path.GetFullPath(_gamePath.Text);
        _tabs.SelectedTab = _settingsPage;
        Warn($"Game folder: {err}. Pick the Evil Genius 2 folder on the Settings tab.");
        return null;
    }

    async Task LoadGameData()
    {
        if (GameRoot() is not { } root) return;
        SyncSettings();
        _settings.Save();
        string lang = _lang.Text.Trim();
        GameData? g = null;
        if (!await Run("Loading game data", (log, ct) => g = GameData.Load(new GameInstall(root), lang, log, ct))) return;
        _game = g!;
        _assets = null;
        _guiNames = null;
        _nodeCatalog = null;
        _dataStatus.Text = $"{_game.Furniture.Count} furniture items, {_game.Objects.Count:N0} objects, {_game.Text.Count:N0} text entries ({lang}).";
        _status.Text = "Ready.";
        BrowseLoaded();
        LoadTweaks();
        if (GridFor(_tabs.SelectedTab) is { } tg) LoadGrid(tg);
    }

    /// <summary>Every game string (plus this mod's changes) as a spreadsheet to fill in.</summary>
    void ExportText()
    {
        if (_game is null) { Warn("Game data is still loading."); return; }
        CommitEditor();
        using var dlg = new SaveFileDialog { Filter = "Spreadsheet (*.csv)|*.csv", FileName = $"{_current?.Id ?? "game"}-text-{_game.Language}.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        TextCsv.Export(_game, _current, dlg.FileName);
        _status.Text = $"Wrote {_game.Text.Count:N0} lines to {dlg.FileName}. Fill in the \"New text\" column, then Import.";
    }

    void ImportText()
    {
        if (_game is null) { Warn("Game data is still loading."); return; }
        if (_current is not { } mod) { Warn("Select or create the mod to put the text changes in first."); return; }
        using var dlg = new OpenFileDialog { Filter = "Spreadsheet (*.csv)|*.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        CommitEditor();
        TextCsv.ImportResult res;
        try { res = TextCsv.Import(_game, mod, dlg.FileName); }
        catch (Exception e) when (e is InvalidDataException or IOException) { Warn($"Couldn't import it: {e.Message}"); return; }
        mod.Save();
        ShowEditor();
        _modTabs.SelectedIndex = 2;
        Log($"{mod.Id}: text sheet {dlg.FileName}: {res.Changed} changed, {res.Removed} back to the game's, {res.Problems.Count} problems");
        foreach (var p in res.Problems.Take(50)) Log("  " + p);
        _status.Text = $"{mod.Name}: {res.Changed:N0} lines changed, {res.Removed:N0} back to the game's text" + (res.Problems.Count > 0 ? $", {res.Problems.Count} rows skipped (see log)" : ".");
    }

    /// <summary>Apply a change to a mod's lists, then show it in the editor with the new row selected.</summary>
    void EditMod(ModDefinition mod, int tab, Action change)
    {
        CommitEditor();
        change();
        ShowEditor();
        _tabs.SelectedTab = _modsPage;
        _modTabs.SelectedIndex = tab;
        var grid = new[] { _newGrid, _costGrid, _textEditGrid, _fieldGrid, _runtimeGrid, _assetGrid, _objectGrid }[tab];
        int last = grid.Rows.Count - (grid.AllowUserToAddRows ? 2 : 1);
        if (last >= 0) grid.CurrentCell = grid.Rows[last].Cells.Cast<DataGridViewCell>().First(c => c.Visible);
    }

    // ------------------------------------------------------------------ mods

    static readonly HashSet<string> TechnicalColumns = new(StringComparer.OrdinalIgnoreCase)
        { "Package", "Tag", "Object", "Offset", "Expect", "Type", "Pattern", "At", "Rel", "Len", "Deref", "SkipIf", "Where", "File", "Occurrence" };

    void ApplyColumnVisibility(DataGridView g)
    {
        foreach (DataGridViewColumn c in g.Columns)
            c.Visible = _technical.Checked || !TechnicalColumns.Contains(c.DataPropertyName);
        if (g.Columns["Note"] is { } note) note.FillWeight = 250;
    }

    /// <summary>The mod's name; version and id only with advanced tools on.</summary>
    string ModLabel(ModDefinition m) => _settings.AdvancedTools ? m.ToString() : string.IsNullOrEmpty(m.Name) ? m.Id : m.Name;

    IEnumerable<ModDefinition> ModsInList() => _modList.Items.Cast<ModDefinition>();

    // the mod list is alphabetical by name (order doesn't change what's built)
    static string SortKey(ModDefinition m) => (string.IsNullOrEmpty(m.Name) ? m.Id : m.Name) + "\u0001" + m.Id;

    int AddSorted(ModDefinition m, bool check)
    {
        int i = 0;
        while (i < _modList.Items.Count && string.Compare(SortKey((ModDefinition)_modList.Items[i]), SortKey(m), StringComparison.OrdinalIgnoreCase) < 0) i++;
        _modList.Items.Insert(i, m);
        _modList.SetItemChecked(i, check);
        return i;
    }

    void LoadMods()
    {
        _modList.Items.Clear();
        SelectMod(null);
        var dir = _modsPath.Text;
        if (!Directory.Exists(dir)) return;
        var mods = new List<ModDefinition>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try { mods.Add(ModDefinition.Load(f)); }
            catch (Exception e) { Log($"skipped {Path.GetFileName(f)}: {e.Message}"); }
        }
        foreach (var m in mods.OrderBy(SortKey, StringComparer.OrdinalIgnoreCase))
            _modList.Items.Add(m, _settings.Enabled.Contains(m.Id));
        Log($"{mods.Count} mods in {dir}");
        FitModList();
    }

    /// <summary>Fit the mod list to its longest name (the splitter still lets you drag it).</summary>
    void FitModList()
    {
        int widest = ModsInList().Select(m => TextRenderer.MeasureText(ModLabel(m), _modList.Font).Width).DefaultIfEmpty(200).Max();
        _modsLeft.Width = Math.Clamp(widest + 48, 260, 380);
    }

    /// <summary>Re-read the mods folder (mods added, removed or regenerated outside the ModKit). Ticks, order and the
    /// open mod's edits are kept; a mod whose file was deleted isn't written back.</summary>
    void RefreshMods()
    {
        ReloadChangedMods();
        CommitEditor();
        if (_current is { FilePath: { } path } cur && File.Exists(path)) cur.Save();
        string? open = _current?.Id;
        SyncSettings();
        _settings.Save();
        LoadMods();
        if (ModsInList().FirstOrDefault(m => m.Id == open) is { } again) _modList.SelectedItem = again;
    }

    void ModsFolderChanged()
    {
        if (string.Equals(_modsPath.Text, _settings.ModsFolder, StringComparison.OrdinalIgnoreCase)) return;
        SaveAll();                       // saves the current mods to their own files first
        _settings.ModsFolder = _modsPath.Text;
        _settings.Save();
        LoadMods();
        LoadTweaks();
    }

    void SelectMod(ModDefinition? mod)
    {
        if (ReferenceEquals(mod, _current)) return;
        CommitEditor();
        _current = mod;
        ShowEditor();
        UpdateTargets();
    }

    void ShowEditor()
    {
        var m = _current;
        _editor.Visible = m is not null;
        _noMod.Visible = m is null;
        RefreshSections(picked: true);
        _modId.Text = m?.Id ?? "";
        _modName.Text = m?.Name ?? "";
        _modVersion.Text = m?.Version ?? "";
        _modAuthor.Text = m?.Author ?? "";
        _modDesc.Text = m?.Description ?? "";
        _newGrid.DataSource = m is null ? null : new BindingList<NewFurniture>(m.NewFurniture);
        _costGrid.DataSource = m is null ? null : new BindingList<FurnitureEdit>(m.FurnitureEdits);
        _textEditGrid.DataSource = m is null ? null : new BindingList<TextEdit>(m.TextEdits);
        _fieldGrid.DataSource = m is null ? null : new BindingList<FieldEdit>(m.FieldEdits);
        _runtimeGrid.DataSource = m is null ? null : new BindingList<RuntimePatch>(m.Runtime);
        _assetGrid.DataSource = m is null ? null : new BindingList<AssetReplacement>(m.Assets);
        _objectGrid.DataSource = m is null ? null : new BindingList<NewObject>(m.NewObjects);
        FillChanges();
    }

    void CommitEditor()
    {
        if (_current is null) return;
        foreach (var g in new[] { _newGrid, _costGrid, _textEditGrid, _fieldGrid, _runtimeGrid, _assetGrid, _objectGrid }) g.EndEdit();
        _current.Id = _modId.Text.Trim();
        _current.Name = _modName.Text.Trim();
        _current.Version = _modVersion.Text.Trim();
        _current.Author = _modAuthor.Text.Trim();
        _current.Description = _modDesc.Text;
        _modList.Invalidate();           // item text comes from ToString()
    }

    ModDefinition? NewMod(string id = "new-mod", string name = "New mod", bool select = true)
    {
        if (!EnsureModsFolder()) return null;
        string baseId = id;
        for (int n = 2; ModsInList().Any(m => m.Id == id) || File.Exists(Path.Combine(_modsPath.Text, id + ".json")); n++) id = $"{baseId}-{n}";
        var mod = new ModDefinition { Id = id, Name = name, Author = Environment.UserName };
        mod.Save(Path.Combine(_modsPath.Text, id + ".json"));
        int i = AddSorted(mod, true);
        if (select) { _modList.SelectedIndex = i; _modName.Focus(); }
        return mod;
    }

    void ImportMod()
    {
        if (!EnsureModsFolder()) return;
        using var dlg = new OpenFileDialog { Filter = $"Mods ({ModPackage.Patterns})|{ModPackage.Patterns}" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var peek = ModPackage.Peek(dlg.FileName);
            if (ModsInList().Any(m => m.Id == peek.Id)) { Warn($"A mod with id '{peek.Id}' already exists. Delete it first to replace it."); return; }
            var mod = ModPackage.Install(dlg.FileName, _modsPath.Text);
            _modList.SelectedIndex = AddSorted(mod, false);
            Log($"imported {mod.Id}");
        }
        catch (Exception e) { Warn($"Import failed: {e.Message}"); }
    }

    /// <summary>One file for players: a .eg2mod (zip of the mod's json and its assets folder) for the Mod Installer.</summary>
    void ExportMod()
    {
        if (_current is not { } mod) { Warn("Select a mod first."); return; }
        SaveAll();
        using var dlg = new SaveFileDialog
        {
            Filter = $"Evil Genius 2 mod (*{ModPackage.Extension})|*{ModPackage.Extension}", FileName = mod.Id + ModPackage.Extension,
            Title = "Export for players",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ModPackage.Export(mod, dlg.FileName);
            Log($"exported {mod.Id} to {dlg.FileName} ({new FileInfo(dlg.FileName).Length:N0} bytes)");
            _status.Text = $"Exported {Path.GetFileName(dlg.FileName)}. Players add it with the Mod Installer (Add mod… or drag and drop).";
        }
        catch (Exception e) { Warn($"Export failed: {e.Message}"); }
    }

    void DeleteMod()
    {
        if (_current is not { } mod) return;
        if (MessageBox.Show(this, $"Delete \"{mod}\"? Its file will be removed from the mods folder.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        ModPackage.Delete(mod);          // json + its .assets folder
        _current = null;                 // don't commit the editor back into a deleted mod
        _modList.Items.Remove(mod);
        ShowEditor();
        UpdateTargets();
        Log($"deleted {mod.Id}");
    }

    void AddPreset()
    {
        if (_current is not { } mod || _preset.SelectedItem is not RuntimePatch p) return;
        EditMod(mod, 4, () =>
        {
            mod.Runtime.RemoveAll(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            mod.Runtime.Add(p.Copy());
        });
    }

    bool EnsureModsFolder()
    {
        try { Directory.CreateDirectory(_modsPath.Text); return true; }
        catch (Exception e) { Warn($"Mods folder: {e.Message}"); return false; }
    }

    /// <summary>Mods whose file was changed outside the ModKit are reloaded (the file wins) instead of being saved over.</summary>
    void ReloadChangedMods()
    {
        for (int i = 0; i < _modList.Items.Count; i++)
        {
            if (_modList.Items[i] is not ModDefinition m || !m.ChangedOnDisk) continue;
            ModDefinition fresh;
            try { fresh = ModDefinition.Load(m.FilePath!); }
            catch (Exception e) { Log($"{m.Id} changed on disk but can't be read ({e.Message}); keeping the ModKit's copy"); continue; }
            bool check = _modList.GetItemChecked(i), current = ReferenceEquals(m, _current);
            if (current) { _current = null; }
            _modList.Items[i] = fresh;
            _modList.SetItemChecked(i, check);
            if (current) { _current = fresh; ShowEditor(); UpdateTargets(); }
            Log($"{fresh.Id}: changed on disk, reloaded (v{fresh.Version})");
        }
    }

    void SaveAll()
    {
        ReloadChangedMods();
        CommitEditor();
        foreach (var m in ModsInList())
        {
            try { m.Save(); }
            catch (Exception e) { Log($"could not save {m.Id}: {e.Message}"); }
        }
        SyncSettings();
        _settings.Save();
    }

    void SyncSettings()
    {
        _settings.GamePath = _gamePath.Text;
        _settings.ModsFolder = _modsPath.Text;
        _settings.Language = _lang.Text.Trim();
        _settings.Order = ModsInList().Select(m => m.Id).ToList();
        _settings.Enabled = _modList.CheckedItems.Cast<ModDefinition>().Select(m => m.Id).ToList();
    }

    // ------------------------------------------------------------------ apply / remove

    /// <summary>Apply, then give the build's memory (gigabytes with big texture mods) back to Windows.</summary>
    async Task Apply()
    {
        try { await ApplyBuilt(); }
        finally { ModBuilder.ReleaseMemory(); }
    }

    /// <summary>Build every ticked mod, then install the result. One click, one question at most.</summary>
    async Task ApplyBuilt()
    {
        if (_game is null) { Warn("Game data hasn't loaded yet. Check the Settings tab."); return; }
        if (!string.Equals(_game.Language, _lang.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_game.Install.Root, Path.GetFullPath(_gamePath.Text), StringComparison.OrdinalIgnoreCase))
        { Warn("The game folder or language changed; press \"Reload game data\" on the Settings tab first."); return; }
        SaveSettingsPage(_tabs.SelectedTab);
        SaveAll();
        var mods = _modList.CheckedItems.Cast<ModDefinition>().ToList();
        if (mods.Count == 0)
        {
            if (Installer.Current(_game.Install.Root) is not null
                && MessageBox.Show(this, "No mods are ticked. Remove the mods currently in the game?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                await Uninstall(confirm: false);
            else if (Installer.Current(_game.Install.Root) is null) Warn("No mods are ticked on the Mods tab (and nothing is installed).");
            return;
        }

        BuildResult? r = null;
        var game = _game;
        // r is captured by the lambdas, so the finished task would keep gigabytes alive: cleared on the way out
        try
        {
            if (!await Run("Building mods", (log, ct) => r = ModBuilder.Build(game, mods, log, ct))) { ShowLog(true); return; }
            foreach (var line in r!.Report) Log("  " + line);
            foreach (var e in r.Errors) Log("ERROR " + e);
            if (!r.Ok)
            {
                ShowLog(true);
                Warn("The mods couldn't be built:\n\n" + string.Join("\n", r.Errors.Take(8)) + (r.Errors.Count > 8 ? $"\n… and {r.Errors.Count - 8} more (see log)" : ""));
                return;
            }
            if (r.Files.Count == 0) { Warn("The ticked mods don't change anything yet."); return; }

            string root = game.Install.Root;
            var foreign = Installer.Foreign(root, r.Files.Keys);
            bool overwrite = false;
            if (foreign.Count > 0)
            {
                var msg = "These files are already in the game folder but weren't put there by ModKit (older test files or another tool):\n\n"
                          + string.Join("\n", foreign) + "\n\nReplace them? They will be removed again by \"Remove mods from game\".";
                if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                overwrite = true;
            }
            var wine = GameInstall.WineNote(root, r.Files.Keys);
            if (await Run("Installing", (log, _) => Installer.Install(root, r.Files, mods, overwrite, log)))
            {
                _status.Text = $"Applied {mods.Count} mod{(mods.Count == 1 ? "" : "s")}. Start the game from Steam as usual.";
                if (wine is not null) { try { Clipboard.SetText(GameInstall.WineLaunchOption); } catch (System.Runtime.InteropServices.ExternalException) { } Warn(wine); }
            }
            RefreshInstalled();
        }
        finally { r = null; }
    }

    async Task Uninstall(bool confirm = true)
    {
        if (GameRoot() is not { } root) return;
        if (Installer.Current(root) is null) { Warn("No ModKit mods are installed."); return; }
        if (confirm && MessageBox.Show(this, "Remove all ModKit mods from the game?", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        if (await Run("Removing mods", (log, _) => Installer.Uninstall(root, log))) _status.Text = "Mods removed; the game is back to normal.";
        RefreshInstalled();
    }

    void RefreshInstalled()
    {
        InstallRecord? rec = null;
        try { if (GameInstall.Validate(_gamePath.Text) is null) rec = Installer.Current(_gamePath.Text); }
        catch (Exception e) { Log($"install record unreadable: {e.Message}"); }
        _installed.Text = rec is null ? "No mods are in the game right now." : $"Last applied {rec.InstalledAt:g}. Ticked mods and what's in the game:";
        _installedMods.BeginUpdate();
        _installedMods.Items.Clear();
        var ticked = _modList.CheckedItems.Cast<ModDefinition>().ToHashSet();
        foreach (var m in ModsInList())
        {
            var status = Installer.Status(rec, m, ticked.Contains(m));
            if (status.Length == 0) continue;
            var e = rec?.Installed.FirstOrDefault(x => x.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
            _installedMods.Items.Add(new ListViewItem(new[] { string.IsNullOrEmpty(m.Name) ? m.Id : m.Name, e is null ? (status.StartsWith("in the game") ? "?" : "-") : "v" + e.Version, "v" + m.Version, status })
                { ForeColor = status == "up to date" ? Theme.Text : Theme.Warn });
        }
        // the ModKit runtime DLL: rebuilt with ModKit, so a new one is a change to apply too
        if (rec?.Files.FirstOrDefault(f => f.Path.Equals(ModBuilder.RuntimeDllTarget, StringComparison.OrdinalIgnoreCase)) is { } dll && File.Exists(ModBuilder.RuntimeDll))
        {
            string mine = Installer.RuntimeVersion(ModBuilder.RuntimeDll), inGame = Installer.RuntimeVersion(Path.Combine(_gamePath.Text, dll.Path));
            string sha = Installer.Sha(ModBuilder.RuntimeDll);
            bool same = dll.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase);
            string state = sha.Length == 0 ? "blocked: ModKit's copy can't be read (Windows Defender? see Protection history)"
                : !File.Exists(Path.Combine(_gamePath.Text, dll.Path)) ? "missing from the game (Windows Defender? see Protection history)"
                : same ? "up to date" : "ModKit has a newer runtime DLL: Apply to update";
            same &= state == "up to date";
            _installedMods.Items.Add(new ListViewItem(new[] { "ModKit runtime DLL (" + ModBuilder.RuntimeDllTarget + ")", inGame, mine, state })
                { ForeColor = same ? Theme.Text : Theme.Warn });
        }
        foreach (var e in rec?.Installed.Where(e => !ModsInList().Any(m => m.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase))) ?? Enumerable.Empty<InstalledMod>())
            _installedMods.Items.Add(new ListViewItem(new[] { e.Name, "v" + e.Version, "-", "in the game but no longer in your mods folder: Apply to take it out" }) { ForeColor = Theme.Warn });
        _installedMods.EndUpdate();
        _files.Items.Clear();
        foreach (var f in rec?.Files ?? new()) _files.Items.Add(new ListViewItem(new[] { f.Path, $"{f.Size:N0}" }));
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Runs work off the UI thread with the UI locked, logging and cancellable. Returns true on success.</summary>
    async Task<bool> Run(string what, Action<Action<string>, CancellationToken> work)
    {
        if (_cts is not null) return false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        ShowBusy(what);
        _tabs.Enabled = false;   // behind the busy cover, so its grey never shows
        _progress.Visible = _cancel.Visible = true;
        _status.Text = what + "…";
        IProgress<string> progress = new Progress<string>(line => { Log(line); BusyStep(line); });
        var sw = Stopwatch.StartNew();
        bool ok = false;
        try
        {
            await Task.Run(() => work(progress.Report, token), token);
            _status.Text = $"{what}: done ({sw.Elapsed.TotalSeconds:0.0}s)";
            ok = true;
        }
        catch (OperationCanceledException) { _status.Text = $"{what}: cancelled"; Log($"{what} cancelled"); }
        catch (Exception e)
        {
            _status.Text = $"{what}: failed";
            Log($"ERROR {what}: {e}");
            ShowLog(true);
            Warn($"{what} failed: {e.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _tabs.Enabled = true;
            HideBusy();
            _progress.Visible = _cancel.Visible = false;
            // loading/building churns through GBs of package bytes; compact and hand the memory back to Windows
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        return ok;
    }

    void ShowLog(bool show)
    {
        _split.Panel2Collapsed = !show;
        if (show) _split.SplitterDistance = Math.Max(200, _split.Height - 220);
        _logToggle.Text = show ? "Hide log" : "Show log";
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            e.Cancel = true;
            Log("cancelling; close again once it stops");
            return;
        }
        SaveSettingsPage(_tabs.SelectedTab);
        SaveAll();
    }

    void Log(string line) => _log.AppendText(line + Environment.NewLine);

    void Warn(string msg) => MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    static void Debounce(TextBox box, Action apply)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) => { timer.Stop(); apply(); };
        box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
    }

    bool PickFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = target.Text, UseDescriptionForTitle = true, Description = "Choose folder" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        target.Text = dlg.SelectedPath;
        return true;
    }

    void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else Warn($"{path} does not exist.");
    }

    static Button Btn(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2) };
        b.Click += click;
        return b;
    }

    static Label Lbl(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 8, 0) };

    static Label Hint(string text) => new HintLabel(text);

    /// <summary>A page's help text: the first sentence, with "more…" to show the rest (click again to fold it).</summary>
    sealed class HintLabel : Label
    {
        readonly string _full, _short;
        bool _open;

        public HintLabel(string text)
        {
            _full = text;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^.{20,}?[.!?](?=\s)");
            _short = m.Success && m.Length < text.Length - 1 ? m.Value + "  more…" : text;
            Dock = DockStyle.Top; AutoSize = false; ForeColor = Theme.Muted; Padding = new Padding(4, 4, 4, 6);
            if (_short != _full) { Cursor = Cursors.Hand; Click += (_, _) => { _open = !_open; Fit(); }; }
            Fit();
        }

        void Fit()
        {
            Text = _open ? _full : _short;
            int w = Math.Max(100, Width - Padding.Horizontal);
            Height = TextRenderer.MeasureText(Text, Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height + Padding.Vertical;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Width > 0) BeginInvokeFit(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }

        bool _pending;
        void BeginInvokeFit()
        {
            if (_pending || !IsHandleCreated) { if (!IsHandleCreated) Fit(); return; }
            _pending = true;
            BeginInvoke(() => { _pending = false; Fit(); });
        }
    }

    /// <summary>A toolbar of labelled button groups ("Rock: Tier 1 … | Dig and build: …"), wrapping by group.</summary>
    static FlowLayoutPanel Tools(params (string Label, Control[] Items)[] groups)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        foreach (var (label, items) in groups)
        {
            var g = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 18, 2) };
            g.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 9, 4, 0) });
            foreach (var c in items) { if (c is Button b) b.Padding = new Padding(3, 0, 3, 0); g.Controls.Add(c); }
            bar.Controls.Add(g);
        }
        return bar;
    }

    static FlowLayoutPanel Bar(params Control[] controls)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        p.Controls.AddRange(controls);
        return p;
    }

    static DataGridView ReadOnlyGrid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };

    static DataGridView EditGrid()
    {
        var g = new DataGridView { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        g.DataError += (_, e) =>
        {
            MessageBox.Show($"Invalid value: {e.Exception?.Message}", "Eg2 ModKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.ThrowException = false;
        };
        return g;
    }
}
