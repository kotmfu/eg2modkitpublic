using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Script nodes window: every node type the game's scripts use, what it connects to and where it's used.</summary>
sealed partial class MainForm
{
    NodeCatalog? _nodeCatalog;
    List<BehaviourTrees.Tree>? _trees;
    Dictionary<uint, List<GameObject>>? _treeUsers;

    async Task ShowBehaviourTrees()
    {
        if (_game is null) { Warn("Game data hasn't loaded yet."); return; }
        if (_trees is null)
        {
            var game = _game;
            if (!await Run("Reading behaviour trees", (log, ct) =>
                {
                    log(@"reading misc\common.asr");
                    var axbt = Eg2.Asura.AsuraArchive.Load(game.Install.Full(@"misc\common.asr")).First("AXBT")
                               ?? throw new InvalidOperationException("misc\\common.asr has no AXBT chunk");
                    _trees = BehaviourTrees.Parse(axbt.Body);
                    _treeUsers = BehaviourTrees.Users(_trees, game.Objects);
                })) return;
        }
        var trees = _trees!;
        string Users(BehaviourTrees.Tree t) => _treeUsers!.TryGetValue(t.Hash, out var l) ? string.Join(", ", l.Select(o => o.Name.Trim('"')).Distinct().Take(4)) : "";
        string Label(BehaviourTrees.Tree t) =>
            $"{(BehaviourTrees.TreeNames.TryGetValue(t.Hash, out var name) ? name + ": " : "")}{(Users(t) is { Length: > 0 } u ? u :string.Join(" / ", BehaviourTrees.All(t.Root).Where(n => n.Name.StartsWith("EG ")).Select(n => n.Name[3..]).Distinct().Take(2)))}  ({t.NodeCount} steps)";
        using var dlg = new ThemedForm { Text = $"Behaviour trees ({trees.Count})", Width = 1100, Height = 720, StartPosition = FormStartPosition.CenterParent };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Words (all must match): job names or node names" };
        var list = new ListBox { Dock = DockStyle.Left, Width = 460, IntegralHeight = false, HorizontalScrollbar = true };
        var info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9.5f) };
        var shown = new List<BehaviourTrees.Tree>();
        void Fill()
        {
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = trees.Where(t => words.All(w => (Users(t) + " " + string.Join(" ", BehaviourTrees.All(t.Root).Select(n => n.Name))).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var t in shown) list.Items.Add(Label(t));
            list.EndUpdate();
        }
        // the selected tree's editable values: typed settings and plain fields (mode, count, comparison...)
        var settings = new ListBox { Dock = DockStyle.Bottom, Height = 220, IntegralHeight = false, HorizontalScrollbar = true };
        var rows = new List<(BehaviourTrees.Node Step, BehaviourTrees.Value Value)>();
        string Hex(BehaviourTrees.Tree t) => Eg2.Asura.Bytes.Hex(t.Hash);
        bool Same(TreeEdit e, BehaviourTrees.Tree t, BehaviourTrees.Node step, BehaviourTrees.Value v) =>
            e.Tree.Equals(Hex(t), StringComparison.OrdinalIgnoreCase) && e.Step == step.Index
            && (v.Field is null ? string.IsNullOrEmpty(e.Field) && e.Setting == v.Setting : e.Field == v.Field);
        (ModDefinition Mod, TreeEdit Edit)? EditOf(BehaviourTrees.Tree t, BehaviourTrees.Node step, BehaviourTrees.Value v) =>
            ModsInList().SelectMany(m => m.TreeEdits.Select(e => (m, e))).FirstOrDefault(x => Same(x.e, t, step, v)) is { m: not null } hit ? hit : null;
        void ShowTree()
        {
            if (list.SelectedIndex < 0) return;
            var t = shown[list.SelectedIndex];
            var users = _treeUsers!.TryGetValue(t.Hash, out var l) ? l : new();
            info.Text = ($"Tree {Hex(t)}{(BehaviourTrees.TreeNames.TryGetValue(t.Hash, out var tn) ? $" ({tn})" : "")}: {t.NodeCount} steps\n" +
                         $"Used by: {(users.Count > 0 ? string.Join(", ", users.Select(o => $"{o.Tag} {o.Name}")) : "nothing found")}\n\n" +
                         BehaviourTrees.Outline(t)).ReplaceLineEndings(Environment.NewLine);
            rows = BehaviourTrees.All(t.Root).SelectMany(n => BehaviourTrees.Values(n).Select(v => (n, v))).ToList();
            settings.BeginUpdate(); settings.Items.Clear();
            foreach (var (step, v) in rows)
                settings.Items.Add($"{step.Name}  #{step.Index}, {v.Label}: {v.Game}" + (EditOf(t, step, v) is var (m, e) ? $"   →  {e.Value}  (mod {m.Id})" : ""));
            if (rows.Count == 0) settings.Items.Add("No editable values in this tree (only variable references).");
            settings.EndUpdate();
        }
        void EditSetting()
        {
            if (list.SelectedIndex < 0 || settings.SelectedIndex < 0 || settings.SelectedIndex >= rows.Count) return;
            var t = shown[list.SelectedIndex];
            var (step, v) = rows[settings.SelectedIndex];
            var existing = EditOf(t, step, v);
            string current = existing?.Edit.Value ?? v.Game;
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                $"{step.Name} (step #{step.Index}), {v.Label}\nGame value: {v.Game}\n\nNew value: {v.Help}.\nLeave empty to go back to the game's value.",
                Text, current).Trim();
            if (input == current) return;
            if (input.Length > 0)
            {
                try { BehaviourTrees.Encode(v, input); }
                catch (Exception x) when (x is FormatException or OverflowException) { Warn($"'{input}' isn't valid here: {v.Help}."); return; }
            }
            var mod = existing?.Mod ?? (input.Length > 0 ? TargetMod() : null);
            if (mod is null) return;
            if (ReferenceEquals(mod, _current)) CommitEditor();
            mod.TreeEdits.RemoveAll(e => Same(e, t, step, v));
            if (input.Length > 0)
                mod.TreeEdits.Add(new TreeEdit
                {
                    Tree = Hex(t), Step = step.Index, Setting = v.Setting, Field = v.Field, Value = input, Expect = v.Game,
                    Note = $"{Users(t)} / {step.Name}: {v.Label}".TrimStart(' ', '/'),
                });
            mod.Save();
            if (ReferenceEquals(mod, _current)) ShowEditor();
            Log($"{mod.Id}: behaviour tree {Hex(t)} {step.Name} {v.Label} = {(input.Length > 0 ? input : "game value")}");
            int keep = settings.SelectedIndex;
            ShowTree();
            settings.SelectedIndex = keep;
        }
        list.SelectedIndexChanged += (_, _) => ShowTree();
        settings.DoubleClick += (_, _) => EditSetting();
        settings.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) EditSetting(); };
        Debounce(search, Fill);
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(info);
        right.Controls.Add(new Splitter { Dock = DockStyle.Bottom });
        right.Controls.Add(settings);
        dlg.Controls.Add(right);
        dlg.Controls.Add(new Splitter { Dock = DockStyle.Left });
        dlg.Controls.Add(list);
        dlg.Controls.Add(search);
        dlg.Controls.Add(Hint("How minions, agents and geniuses carry out each job: \"Serial\" runs its steps in order, \"Parallel\" runs them together, " +
                              "\"Condition\" checks something; \"Always Succeed\", \"Loop\" and \"Timer\" wrap the step under them. " +
                              "Double-click a value in the bottom list to change it; it's saved in the selected mod and applied while the game runs."));
        Fill();
        dlg.ShowDialog(this);
    }

    void ShowNodeCatalog()
    {
        if (_game is null) { Warn("Game data hasn't loaded yet."); return; }
        if (!RunSync("Cataloguing script nodes", () => _nodeCatalog ??= NodeCatalog.Build(_game.Objects, FgNames()))) return;
        var cat = _nodeCatalog!;
        using var dlg = new ThemedForm { Text = $"Script nodes ({cat.Entries.Count} types, {cat.Entries.Sum(e => e.Count):N0} uses)", Width = 1250, Height = 760, StartPosition = FormStartPosition.CenterParent };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Words (all must match): node names, pin names, stored strings, or a type like 0xe2767556" };
        var list = new ListBox { Dock = DockStyle.Left, Width = 440, IntegralHeight = false, HorizontalScrollbar = true };
        var info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
        var uses = new ListBox { Dock = DockStyle.Bottom, Height = 260, IntegralHeight = false, HorizontalScrollbar = true };
        var count = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Theme.Muted };
        var shown = new List<NodeCatalog.Entry>();
        string Searchable(NodeCatalog.Entry e) => string.Join(" ", new[] { e.Name, Eg2.Asura.Bytes.Hex(e.Type) }
            .Concat(e.Inputs.Keys).Concat(e.Outputs.Keys).Concat(e.Strings.Keys).Concat(e.Owners.Keys));
        void Fill()
        {
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = cat.Entries.Where(e => words.All(w => Searchable(e).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var e in shown) list.Items.Add($"{e.Count,6:N0}  {e.Name}");
            list.EndUpdate();
            count.Text = $"{shown.Count} node types";
        }
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var e = shown[list.SelectedIndex];
            info.Text = NodeCatalog.Describe(e).ReplaceLineEndings(Environment.NewLine);
            uses.BeginUpdate(); uses.Items.Clear();
            foreach (var u in e.Uses) uses.Items.Add($"{u.Object.Tag} {u.Object.Name}  /  {u.Script}  (node #{u.Node & 0xfff})");
            uses.EndUpdate();
        };
        Debounce(search, Fill);
        var open = new Button { Text = "Open the selected use's object", DialogResult = DialogResult.OK, AutoSize = true };
        uses.DoubleClick += (_, _) => { if (uses.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(info);
        right.Controls.Add(new Splitter { Dock = DockStyle.Bottom });
        right.Controls.Add(uses);
        dlg.Controls.Add(right);
        dlg.Controls.Add(new Splitter { Dock = DockStyle.Left });
        dlg.Controls.Add(list);
        dlg.Controls.Add(count);
        dlg.Controls.Add(search);
        dlg.Controls.Add(Hint("Every kind of step the game's scripts use. \"Do:\" steps run when the arrow before them fires; \"Get:\" steps feed values in. " +
                              "Names come from the values and pins stored with each step (internal pin names in [brackets]). To use a step in another script, open an object that has it, " +
                              "then Scripts… → Nodes and links… → copy it."));
        dlg.Controls.Add(Bar(open));
        Fill();
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0 || uses.SelectedIndex < 0) return;
        _tabs.SelectedTab = _browsePage;
        Show(shown[list.SelectedIndex].Uses[uses.SelectedIndex].Object);
    }
}
