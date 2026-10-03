using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Script nodes window: every node type the game's scripts use, what it connects to and where it's used.</summary>
sealed partial class MainForm
{
    NodeCatalog? _nodeCatalog;

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
