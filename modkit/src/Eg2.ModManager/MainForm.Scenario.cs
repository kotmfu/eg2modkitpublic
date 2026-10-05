using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Scenario page: one place for a scenario mod's pieces. Pick an island; choose the attack every genius's first objective
/// opens with (a task copied in front of "A Place To Call Your Own", TaskEdits); see, guard or remove the agents placed on
/// that island (agent MapEdits, placed on the Lair maps page).
/// </summary>
sealed partial class MainForm
{
    TabPage? _scenarioPage;
    readonly ListBox _scenIslands = new() { Dock = DockStyle.Fill, IntegralHeight = false, FormattingEnabled = true };
    readonly ComboBox _scenOpening = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
    readonly ListBox _scenAgents = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly Label _scenTitle = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 12f, FontStyle.Bold), Padding = new Padding(4, 8, 0, 4) };
    readonly List<MapEdit> _scenShown = new();
    bool _scenFilling;

    /// <summary>Every genius's first objective ("A Place To Call Your Own", one copy per genius).</summary>
    static readonly string[] FirstObjectives = { "0xbdfda192", "0x5ea4fa33", "0x7736f618", "0x610f5351", "0xa732b6cf" };

    /// <summary>Tasks that start a raid at once and finish when it's beaten (label, "objective:task").</summary>
    static readonly (string Label, string? Task)[] Openings =
    {
        ("None: only the agents you place", null),
        ("A.N.V.I.L. agents attack (Roll Out the Red Carpet, last task)", "0x7d553679:5"),
        ("Investigators attack (the tutorial raid: Amass Muscle, last task)", "0x88f80e29:5"),
        ("Resilient A.N.V.I.L. agents attack (Reign of Terra, first task; untested)", "0xfe01135c:0"),
    };

    TabPage ScenarioPage()
    {
        var page = new TabPage("Scenario");
        var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(4) };
        left.Controls.Add(_scenIslands);
        left.Controls.Add(new Label { Text = "Island", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Padding = new Padding(0, 4, 0, 4) });

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 4, 4) };
        foreach (var o in Openings) _scenOpening.Items.Add(o.Label);
        right.Controls.Add(_scenAgents);
        right.Controls.Add(Bar(Btn("Place agents on the map…", (_, _) => ScenarioPlace()),
                               Btn("Guard / roam", (_, _) => ScenarioToggleGuard()),
                               Btn("Remove", (_, _) => ScenarioRemoveAgent())));
        right.Controls.Add(new Label { Text = "Agents on this island", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Padding = new Padding(0, 12, 0, 2) });
        right.Controls.Add(Bar(_scenOpening));
        right.Controls.Add(new Label { Text = "Opening attack (every genius, any island)", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Padding = new Padding(0, 8, 0, 2) });
        _scenTitle.Dock = DockStyle.Top;
        right.Controls.Add(_scenTitle);
        right.Controls.Add(Hint("Make a scenario for new games: pick an island, place enemy agents in its lair and choose how the game opens. " +
                                "Agents are placed on the Lair maps page (Place agent…); a guard stays where it's placed and fights what comes to it, " +
                                "the others act like a normal raid. The opening attack is a task put in front of every genius's first objective: " +
                                "the game sends that raid at once and the objective goes on when it's beaten. Changes go to the mod selected under My mods."));

        page.Controls.Add(right);
        page.Controls.Add(new Splitter { Dock = DockStyle.Left });
        page.Controls.Add(left);
        _scenIslands.Format += (_, e) => { if (e.ListItem is string f) e.Value = LairName(f); };
        _scenIslands.SelectedIndexChanged += (_, _) => FillScenario();
        _scenOpening.SelectedIndexChanged += (_, _) => { if (!_scenFilling) SetOpening(Openings[_scenOpening.SelectedIndex].Task); };
        _scenAgents.DoubleClick += (_, _) => ScenarioToggleGuard();
        return page;
    }

    void LoadScenario()
    {
        if (_game is null) return;
        if (_scenIslands.Items.Count == 0)
        {
            foreach (var f in LairFiles()) _scenIslands.Items.Add(f);
            if (_scenIslands.Items.Count > 0) _scenIslands.SelectedIndex = 0;
        }
        FillScenario();
    }

    void FillScenario()
    {
        if (_game is null || _scenIslands.SelectedItem is not string file) return;
        _scenFilling = true;
        _scenTitle.Text = $"{LairName(file)}{(_current is null ? "   (select or create a mod under My mods)" : $"   in {_current.Name}")}";
        int open = Array.FindIndex(Openings, o => o.Task is not null && OpeningOf(_current) == o.Task);
        _scenOpening.SelectedIndex = open < 0 ? 0 : open;
        if (open < 0 && OpeningOf(_current) is { } other) _scenTitle.Text += $"   (opening task {other} set elsewhere)";
        int sel = _scenAgents.SelectedIndex;
        _scenAgents.Items.Clear(); _scenShown.Clear();
        foreach (var e in EditsFor(_current, file).Where(e => e.Action == "agent"))
        {
            _scenShown.Add(e);
            _scenAgents.Items.Add($"{(e.Tree == Agents.GuardTree ? "Guard" : "Roams")}   {e.Note?.Replace("guard: ", "")}   floor {e.Floor}, column {e.X0}, row {e.Y0}");
        }
        if (_scenAgents.Items.Count > 0) _scenAgents.SelectedIndex = Math.Clamp(sel, 0, _scenAgents.Items.Count - 1);
        _scenFilling = false;
    }

    /// <summary>The task the mod puts in front of the first objectives (null when it doesn't).</summary>
    static string? OpeningOf(ModDefinition? m)
    {
        var e = m?.TaskEdits.FirstOrDefault(t => t.Object.Equals(FirstObjectives[0], StringComparison.OrdinalIgnoreCase));
        return e is { Tasks.Count: > 1 } && !e.Tasks[0].StartsWith(FirstObjectives[0] + ":", StringComparison.OrdinalIgnoreCase) ? e.Tasks[0] : null;
    }

    void SetOpening(string? task)
    {
        if (_game is null) return;
        if (task is null && _current is null) return;
        if (TargetMod() is not { } mod) { FillScenario(); return; }
        TaskLabel("", 0);   // fills _objectiveTasks
        if (ReferenceEquals(mod, _current)) CommitEditor();
        foreach (var obj in FirstObjectives)
        {
            mod.TaskEdits.RemoveAll(e => e.Object.Equals(obj, StringComparison.OrdinalIgnoreCase));
            if (task is null || !_objectiveTasks!.TryGetValue(obj, out var own)) continue;
            mod.TaskEdits.Add(new TaskEdit
            {
                Object = obj, Note = $"opens with: {Openings.First(o => o.Task == task).Label}",
                Tasks = own.Tasks.Select(t => $"{obj}:{t.Index}").Prepend(task).ToList(),
            });
        }
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: opening attack {(task is null ? "none" : task)}");
        FillScenario();
    }

    MapEdit? ScenarioAgent()
    {
        if (_scenAgents.SelectedIndex is var i && i >= 0 && i < _scenShown.Count) return _scenShown[i];
        Warn("Select an agent first.");
        return null;
    }

    void ScenarioToggleGuard()
    {
        if (_current is not { } mod || ScenarioAgent() is not { } e) return;
        CommitEditor();
        bool guard = e.Tree != Agents.GuardTree;
        e.Tree = guard ? Agents.GuardTree : null;
        e.Note = (guard ? "guard: " : "") + (e.Note ?? "").Replace("guard: ", "");
        mod.Save();
        ShowEditor();
        FillScenario();
    }

    void ScenarioRemoveAgent()
    {
        if (_current is not { } mod || ScenarioAgent() is not { } e) return;
        CommitEditor();
        mod.MapEdits.Remove(e);
        mod.Save();
        ShowEditor();
        FillScenario();
    }

    /// <summary>Lair maps with this island open.</summary>
    void ScenarioPlace()
    {
        if (_scenIslands.SelectedItem is not string file) return;
        _tabs.SelectedTab = _mapsPage;
        int i = _lairList.Items.IndexOf(file);
        if (i >= 0) _lairList.SelectedIndex = i;
    }
}
