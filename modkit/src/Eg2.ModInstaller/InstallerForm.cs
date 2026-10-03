using System.Diagnostics;
using System.Text.Json;
using Eg2.ModKit;

namespace Eg2.ModInstaller;

/// <summary>
/// Player-facing installer: mods live in a "Mods" folder next to the exe. Add them with the button,
/// by dropping files on the window, or by opening a mod file with this exe; tick the ones you want and
/// press Apply. Uses the same builder/installer as ModKit, so its installs and removals are interchangeable.
/// </summary>
sealed partial class InstallerForm : Form
{
    sealed class Settings
    {
        public string GamePath { get; set; } = "";
        public string Language { get; set; } = "en";
        public List<string> Enabled { get; set; } = new();
    }

    static readonly string Here = AppContext.BaseDirectory;
    static readonly string ModsDir = Path.Combine(Here, "Mods");
    static readonly string SettingsFile = Path.Combine(Here, "Eg2ModInstaller.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly Settings _settings;
    readonly TextBox _gamePath = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    readonly ComboBox _lang = new() { Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false,
        HideSelection = false, AllowDrop = true,
    };
    readonly Label _aboutTitle = new() { Dock = DockStyle.Top, Height = 24, Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold), AutoEllipsis = true };
    readonly Label _about = new() { Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, AutoEllipsis = true };
    readonly Label _installed = new() { Dock = DockStyle.Top, Height = 26, Padding = new Padding(4, 4, 4, 0), ForeColor = SystemColors.GrayText, AutoEllipsis = true };
    readonly TextBox _log = new() { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false };
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
    GameData? _game;
    bool _busy, _loading;

    public InstallerForm(string[] args)
    {
        Text = "Evil Genius 2 Mod Installer";
        Size = new Size(760, 600);
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;

        _settings = LoadSettings();
        Directory.CreateDirectory(ModsDir);

        _list.Columns.Add("Mod", 250);
        _list.Columns.Add("Version", 70);
        _list.Columns.Add("Author", 120);
        _list.Columns.Add("In the game", 260);
        _list.SmallImageList = new ImageList { ImageSize = new Size(1, 24 * DeviceDpi / 96) };   // roomier rows
        _list.SelectedIndexChanged += (_, _) => ShowAbout();
        _list.ItemCheck += (_, e) => { if (_busy) e.NewValue = e.CurrentValue; };   // the list stays enabled while busy (disabled it turns light grey)
        _list.ItemChecked += (_, _) => { if (!_loading) { SaveEnabled(); RefreshInstalled(); } };
        foreach (Control c in new Control[] { this, _list }) { c.DragEnter += OnDragEnter; c.DragDrop += OnDragDrop; }

        foreach (var l in new[] { "en", "fr", "ge", "it", "sp", "ru", "pb", "cs", "ct", "am" }) _lang.Items.Add(l);
        _lang.SelectedItem = _lang.Items.Contains(_settings.Language) ? _settings.Language : "en";
        _gamePath.Text = GameInstall.Validate(_settings.GamePath) is null ? _settings.GamePath : GameInstall.FindDefault() ?? _settings.GamePath;

        var game = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new Padding(0, 0, 0, 4) };
        game.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        game.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) game.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        game.Controls.Add(Lbl("Game folder:"), 0, 0);
        game.Controls.Add(_gamePath, 1, 0);
        game.Controls.Add(Btn("Browse…", (_, _) => PickGame()), 2, 0);
        game.Controls.Add(Lbl("Language:"), 3, 0);
        game.Controls.Add(_lang, 4, 0);

        // what you do to the list, above it; what you do to the game, below it (Apply last, where the eye ends)
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 4) };
        tools.Controls.AddRange(new Control[]
        {
            Btn("Add mod…", (_, _) => AddDialog()),
            Btn("Delete mod", (_, _) => DeleteSelected()),
            Btn("Quick tweaks…", async (_, _) => await TweaksDialog()),
            Btn("Open mods folder", (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ModsDir}\"") { UseShellExecute = true })),
            Btn("Refresh list", (_, _) => LoadMods()),
        });
        var go = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty };
        go.Controls.AddRange(new Control[]
        {
            Btn("Launch game", (_, _) =>
            {
                string? err = GameInstall.Validate(_gamePath.Text) ?? GameInstall.Launch(_gamePath.Text);
                if (err is not null) MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }),
            Theme.Primary(Btn("Apply", async (_, _) => await Apply())),
        });
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var remove = Btn("Remove all mods from game", async (_, _) => await RemoveAll());
        remove.Anchor = AnchorStyles.Left;
        bar.Controls.Add(remove, 0, 0);
        bar.Controls.Add(go, 1, 0);

        var about = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(10, 8, 10, 8), BackColor = Theme.Panel };
        about.Controls.Add(_about);
        about.Controls.Add(_aboutTitle);

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 40, ForeColor = SystemColors.GrayText, Padding = new Padding(4),
            Text = "Tick the mods you want and press Apply. Add mods with \"Add mod…\" or by dropping mod files (.eg2mod, .zip or .json) onto this window. " +
                   "Close the game before applying.",
        };
        _body.Controls.Add(_list);
        _body.Controls.Add(tools);
        _body.Controls.Add(about);
        _body.Controls.Add(bar);
        _body.Controls.Add(_installed);
        _body.Controls.Add(hint);
        _body.Controls.Add(game);

        var strip = new StatusStrip();
        strip.Items.AddRange(new ToolStripItem[] { _status });
        Controls.Add(_body);
        Controls.Add(_log);
        Controls.Add(strip);

        _status.Text = GameInstall.Validate(_gamePath.Text) is null ? "Ready." : "Couldn't find Evil Genius 2; pick its folder with Browse.";
        LoadMods();
        AddFiles(args.Where(File.Exists));   // "open with" / dropped onto the exe
        RefreshInstalled();
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; else SaveSettings(); };
    }

    // ------------------------------------------------------------------ mods folder

    void LoadMods()
    {
        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var f in Directory.EnumerateFiles(ModsDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            ModDefinition m;
            try { m = ModDefinition.Load(f); }
            catch (Exception e) { Log($"skipped {Path.GetFileName(f)}: {e.Message}"); continue; }
            _list.Items.Add(new ListViewItem(new[] { Title(m), m.Version, m.Author, "" }) { Tag = m, Checked = _settings.Enabled.Contains(m.Id) });
        }
        _list.EndUpdate();
        _loading = false;
        ShowAbout();
        RefreshInstalled();
    }

    static string Title(ModDefinition m) => string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name;

    IEnumerable<ModDefinition> Mods() => _list.Items.Cast<ListViewItem>().Select(i => (ModDefinition)i.Tag!);

    /// <summary>Copy mods (.json, or .eg2mod with assets) into the Mods folder and tick them.</summary>
    void AddFiles(IEnumerable<string> files)
    {
        int added = 0;
        foreach (var src in files)
        {
            ModDefinition m;
            try { m = ModPackage.Peek(src); }
            catch (Exception e) { Warn($"{Path.GetFileName(src)} isn't a mod file this installer understands: {e.Message}"); continue; }

            var existing = Mods().FirstOrDefault(x => x.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
            if (existing?.FilePath is { } p && Path.GetFullPath(p).Equals(Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase)) continue;
            if (existing is not null && MessageBox.Show(this,
                    $"\"{Title(existing)}\" {existing.Version} is already in your mods. Replace it with {m.Version}?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
            try
            {
                if (existing is not null) ModPackage.Delete(existing);
                m = ModPackage.Install(src, ModsDir);
            }
            catch (Exception e) { Warn($"Couldn't add {Path.GetFileName(src)}: {e.Message}"); continue; }
            if (!_settings.Enabled.Contains(m.Id)) _settings.Enabled.Add(m.Id);
            Log($"added {Title(m)} ({Path.GetFileName(src)})");
            added++;
        }
        if (added == 0) return;
        SaveSettings();
        LoadMods();
        _status.Text = $"Added {added} mod{(added == 1 ? "" : "s")}. Press Apply to put {(added == 1 ? "it" : "them")} in the game.";
    }

    void AddDialog()
    {
        using var dlg = new OpenFileDialog { Filter = $"Evil Genius 2 mods ({ModPackage.Patterns})|{ModPackage.Patterns}", Multiselect = true, Title = "Add mods" };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
    }

    void DeleteSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not ModDefinition m) { Warn("Select a mod first."); return; }
        if (MessageBox.Show(this, $"Delete \"{Title(m)}\" from your mods folder?\n\nIf it's in the game now, press Apply afterwards to take it out.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        ModPackage.Delete(m);
        _settings.Enabled.Remove(m.Id);
        SaveSettings();
        LoadMods();
    }

    void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
    }

    void ShowAbout()
    {
        var m = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModDefinition : null;
        _aboutTitle.Text = m is null ? (_list.Items.Count == 0 ? "No mods yet" : "") : $"{Title(m)}  v{m.Version}{(m.Author.Length > 0 ? "  ·  by " + m.Author : "")}";
        _aboutTitle.Visible = _aboutTitle.Text.Length > 0;
        _about.Text = m is null
            ? (_list.Items.Count == 0 ? "Drop mod files (.eg2mod, .zip or .json) onto this window, or press \"Add mod…\"." : "Select a mod to see what it does.")
            : m.Description.Length > 0 ? m.Description : "No description.";
    }

    // ------------------------------------------------------------------ quick tweaks

    /// <summary>ModKit's Quick Tweaks page (same rows, <see cref="Eg2.Ui.TweaksPanel"/>), saved as the quick-tweaks mod
    /// in the Mods folder and ticked, so it applies like any mod.</summary>
    async Task TweaksDialog()
    {
        if (GameRoot() is not { } root) return;
        string lang = (string)_lang.SelectedItem!;
        if ((_game is null || _game.Install.Root != root || _game.Language != lang)
            && !await Run("Reading game data", log => { _game = null; _game = GameData.Load(new GameInstall(root), lang, log); }))
            return;
        var mod = Mods().FirstOrDefault(m => m.Id == QuickTweaks.ModId);
        using var dlg = new ThemedForm { Text = "Quick tweaks", Size = new Size(900, 640), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false };
        var panel = new Eg2.Ui.TweaksPanel();
        panel.Show(QuickTweaks.All(_game), mod);
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.AddRange(new Control[] { cancel, ok });
        dlg.Controls.Add(panel);
        dlg.Controls.Add(bar);
        dlg.Controls.Add(new Label { Dock = DockStyle.Top, Height = 40, ForeColor = SystemColors.GrayText, Padding = new Padding(8),
                                     Text = "Tick a setting to change it and set the number. Unticked settings stay as the game ships them. Press Apply afterwards." });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (mod is null && !panel.AnyTicked) return;
        mod ??= new ModDefinition { Id = QuickTweaks.ModId, Name = "Quick tweaks", Description = "Settings from the Quick tweaks page." };
        panel.SaveInto(mod);
        mod.Save(mod.FilePath ?? Path.Combine(ModsDir, QuickTweaks.ModId + ".json"));
        if (panel.AnyTicked && !_settings.Enabled.Contains(mod.Id)) _settings.Enabled.Add(mod.Id);
        SaveSettings();
        LoadMods();
        _status.Text = "Quick tweaks saved. Press Apply to put them in the game.";
    }

    // ------------------------------------------------------------------ apply / remove

    /// <summary>Apply, then give the build's memory (gigabytes with big texture mods) back to Windows.</summary>
    async Task Apply()
    {
        try { await ApplyBuilt(); }
        finally { ModBuilder.ReleaseMemory(); }
    }

    async Task ApplyBuilt()
    {
        if (GameRoot() is not { } root) return;
        var mods = Mods().Where(m => _settings.Enabled.Contains(m.Id)).ToList();
        if (mods.Count == 0)
        {
            if (Installer.Current(root) is not null) await RemoveAll();
            else Warn("Tick at least one mod first.");
            return;
        }
        string lang = (string)_lang.SelectedItem!;
        BuildResult? r = null;
        SortedDictionary<string, byte[]>? files = null;
        // r and files are captured by the lambdas, so the finished task would keep gigabytes alive: cleared on the way out
        try
        {
            bool ok = await Run("Applying mods", log =>
            {
                if (_game is null || _game.Install.Root != root || _game.Language != lang)
                {
                    _game = null;
                    log("reading game data (first time only)…");
                    _game = GameData.Load(new GameInstall(root), lang, log);
                }
                r = ModBuilder.Build(_game, mods, log);
            });
            if (!ok || r is null) return;
            foreach (var e in r.Errors) Log("ERROR " + e);
            if (!r.Ok)
            {
                ShowLog();
                Warn("These mods can't be applied together:\n\n" + string.Join("\n", r.Errors.Take(8)) +
                     (r.Errors.Count > 8 ? $"\n… and {r.Errors.Count - 8} more" : "") + "\n\nUntick one of them and try again.");
                return;
            }
            var foreign = Installer.Foreign(root, r.Files.Keys);
            if (foreign.Count > 0 && MessageBox.Show(this,
                    "Some game files were changed by something else (another mod tool?):\n\n" + string.Join("\n", foreign) +
                    "\n\nReplace them? \"Remove all mods from game\" will remove them later.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            files = r.Files;
            r = null;
            var wine = GameInstall.WineNote(root, files.Keys);
            if (await Run("Installing", log => Installer.Install(root, files, mods, foreign.Count > 0, log)))
            {
                _status.Text = $"{mods.Count} mod{(mods.Count == 1 ? " is" : "s are")} in the game. Start it from Steam as usual.";
                if (wine is not null) { try { Clipboard.SetText(GameInstall.WineLaunchOption); } catch (System.Runtime.InteropServices.ExternalException) { } Warn(wine); }
            }
            RefreshInstalled();
        }
        finally { r = null; files = null; }
    }

    async Task RemoveAll()
    {
        if (GameRoot() is not { } root) return;
        if (Installer.Current(root) is null) { _status.Text = "No mods are in the game."; return; }
        if (await Run("Removing mods", log => Installer.Uninstall(root, log))) _status.Text = "Mods removed; the game is back to normal.";
        RefreshInstalled();
    }

    void RefreshInstalled()
    {
        InstallRecord? rec = null;
        try { if (GameInstall.Validate(_gamePath.Text) is null) rec = Installer.Current(_gamePath.Text); }
        catch (Exception e) { Log($"install record unreadable: {e.Message}"); }
        // per mod: installed version vs this one ("In the game" column); mods applied but no longer here get a line of their own
        foreach (ListViewItem item in _list.Items)
        {
            string status = Installer.Status(rec, (ModDefinition)item.Tag!, item.Checked);
            item.SubItems[3].Text = status;
            item.ForeColor = status.Length == 0 || status == "up to date" ? Theme.Text : Theme.Warn;   // as ModKit's Install page: orange = Apply needed
        }
        var gone = rec?.Installed.Where(e => !Mods().Any(m => m.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase))).Select(e => $"{e.Name} v{e.Version}").ToList() ?? new();
        _installed.Text = rec is null ? "No mods are in the game right now."
            : $"Last applied {rec.InstalledAt:g}." + (gone.Count > 0 ? $" Also in the game but no longer in the Mods folder: {string.Join(", ", gone)}." : "");
    }

    async Task<bool> Run(string what, Action<Action<string>> work)
    {
        if (_busy) return false;
        _busy = true;
        Lock(true);
        ShowBusy(what);
        _status.Text = what + "…";
        IProgress<string> progress = new Progress<string>(line => { BusyStep(line); Log(line); });
        try
        {
            await Task.Run(() => work(progress.Report));
            _status.Text = what + ": done.";
            return true;
        }
        catch (Exception e)
        {
            Log($"ERROR {what}: {e}");
            ShowLog();
            _status.Text = what + ": failed.";
            Warn($"{what} failed: {e.Message}");
            return false;
        }
        finally
        {
            _busy = false;
            Lock(false);
            HideBusy();
            // building reads GBs of package data; hand the memory back to Windows
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
    }

    void Lock(bool busy)
    {
        foreach (Control c in _body.Controls) if (c != _list) c.Enabled = !busy;
    }

    // ------------------------------------------------------------------ settings / plumbing

    string? GameRoot()
    {
        if (GameInstall.Validate(_gamePath.Text) is { } err)
        {
            Warn($"That isn't the Evil Genius 2 folder ({err}). Press Browse and pick the folder that contains \"bin\" and \"misc\".");
            return null;
        }
        SaveSettings();
        return Path.GetFullPath(_gamePath.Text);
    }

    void PickGame()
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = _gamePath.Text, UseDescriptionForTitle = true, Description = "Evil Genius 2 folder" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _gamePath.Text = dlg.SelectedPath;
        _status.Text = GameInstall.Validate(dlg.SelectedPath) is { } err ? $"Not the game folder: {err}" : "Ready.";
        RefreshInstalled();
    }

    void SaveEnabled()
    {
        _settings.Enabled = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => ((ModDefinition)i.Tag!).Id).ToList();
        SaveSettings();
    }

    static Settings LoadSettings()
    {
        try { if (File.Exists(SettingsFile)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new(); }
        catch { /* unreadable: start fresh */ }
        return new();
    }

    void SaveSettings()
    {
        _settings.GamePath = _gamePath.Text;
        _settings.Language = (string?)_lang.SelectedItem ?? "en";
        try { File.WriteAllText(SettingsFile, JsonSerializer.Serialize(_settings, Json)); }
        catch (Exception e) { Log($"couldn't save settings: {e.Message}"); }
    }

    void Log(string line) => _log.AppendText(line + Environment.NewLine);
    void ShowLog() => _log.Visible = true;
    void Warn(string msg) => MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    static Button Btn(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2) };
        b.Click += click;
        return b;
    }

    static Label Lbl(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 4, 0) };
}
