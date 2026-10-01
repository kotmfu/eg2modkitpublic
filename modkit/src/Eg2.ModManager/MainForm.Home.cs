using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Home: where things stand (game found, mods ticked, what's installed) and one line on what each page is for.</summary>
sealed partial class MainForm
{
    TabPage _homePage = null!;
    readonly Label _homeGame = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f) };
    readonly Label _homeMods = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f) };

    TabPage HomePage()
    {
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 16, 24, 16) };
        void Add(Control c) => body.Controls.Add(c);
        Label Heading(string text, float size = 13f) => new() { Text = text, AutoSize = true, ForeColor = Theme.Gold, Font = new Font(Font.FontFamily, size, FontStyle.Bold), Margin = new Padding(0, 18, 0, 6) };
        Label Line(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 6) };

        var title = Heading("Evil Genius 2 ModKit", 18f);
        title.Margin = new Padding(0, 0, 0, 4);
        Add(title);
        Add(Line("Change the game with mods: yours or other people's. Nothing in the game is overwritten; \"Remove mods\" puts it back exactly as it was."));

        Add(Heading("Play with mods"));
        Add(_homeGame);
        Add(_homeMods);
        Add(Step("1", "Pick the mods you want", "Tick them on My mods (or drop a mod file someone sent you onto the list).", "Choose mods", () => _tabs.SelectedTab = _modsPage));
        Add(Step("2", "Install them into the game", "Builds every ticked mod and installs it. Do this again whenever you change a mod.", "Install mods",
                 async () => { await Apply(); RefreshHome(); }));
        Add(Step("3", "Play", "Start a new game to see map and island changes; most other changes show in existing saves too.", "Launch game",
                 () => { if (GameRoot() is { } root && GameInstall.Launch(root) is { } err) Warn(err); }));

        Add(Heading("Make your own changes"));
        Add(Line("Every change is saved in a mod, so you can switch it off, share it or remove it later."));
        Add(Card("Quick tweaks", "Tick-box settings: minion limits, starting gold, skip the intro video…", _tweaksPage));
        Add(Card("Research numbers", "How long each research takes, what it costs and what it gives.", GridPage("Research")));
        Add(Card("Furniture", "Floor plan, job slots and which minions can use each item; price, size and slot count.", _furniturePage));
        Add(Card("Furniture effects", "What using a piece of furniture does to a minion's stats.", GridPage("Furniture Effects")));
        Add(Card("Research trees", "Move research around, change what unlocks what, add new research.", _treesPage!));
        Add(Card("Lair maps", "Dig out, fill in and furnish the underground lair of any island (new games).", _mapsPage!));
        Add(Card("Islands", "Hide, move or copy the scenery around an island, or make a new island.", _islandPage!));
        Add(Card("My mods", "Your mods and everything each one changes: names, texts, prices, art, sounds.", _modsPage));
        Add(Line("Browse game data and Game files (every object, texture and sound in the game) are under Settings → \"Show advanced tools\"."));

        var page = new TabPage("Home");
        page.Controls.Add(body);
        return page;
    }

    Control Step(string number, string title, string text, string button, Action click)
    {
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 6, 0, 6) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = number, AutoSize = true, ForeColor = Theme.Gold, Font = new Font(Font.FontFamily, 16f, FontStyle.Bold) }, 0, 0);
        var words = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        words.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold) });
        words.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(550, 0), ForeColor = Theme.Muted });
        row.Controls.Add(words, 1, 0);
        var b = Btn(button, (_, _) => click());
        b.MinimumSize = new Size(140, 34);
        row.Controls.Add(b, 2, 0);
        return row;
    }

    Control Card(string title, string text, TabPage page)
    {
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 2, 0, 2) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var link = new LinkLabel { Text = title, AutoSize = true, Font = new Font(Font.FontFamily, 10f), Margin = new Padding(3, 4, 3, 3) };
        link.LinkClicked += (_, _) => _tabs.SelectedTab = page;
        row.Controls.Add(link, 0, 0);
        row.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Theme.Muted, Margin = new Padding(3, 5, 3, 3) }, 1, 0);
        return row;
    }

    void RefreshHome()
    {
        var err = GameInstall.Validate(_gamePath.Text);
        _homeGame.Text = err is null ? "✓ Evil Genius 2 found." : "✗ Evil Genius 2 not found: pick its folder in Settings.";
        _homeGame.ForeColor = err is null ? Theme.Good : Theme.Bad;
        int ticked = _modList.CheckedItems.Count;
        string installed = err is null && Installer.Current(Path.GetFullPath(_gamePath.Text)) is { } rec
            ? $"{rec.Mods.Count} installed in the game (last installed {rec.InstalledAt:g})."
            : "None installed in the game yet.";
        _homeMods.Text = $"{ticked} mod{(ticked == 1 ? "" : "s")} ticked. {installed}";
    }
}
