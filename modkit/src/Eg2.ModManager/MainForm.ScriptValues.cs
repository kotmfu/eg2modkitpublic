using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Scripts → "Values…": every number and object reference inside a script's steps, editable in place (saved as ordinary
/// value edits). "Find scripts…": search every script in the game by name, step, or what it points at (e.g. an
/// objective's id finds the reward that starts it).
/// </summary>
sealed partial class MainForm
{
    sealed record ScriptValue(uint Node, string Step, string What, string Type, int Offset, string Game);

    /// <summary>Numbers (named settings) and object references inside the steps of <paramref name="gr"/>.</summary>
    List<ScriptValue> ScriptValues(GameObject o, FlowGraph gr)
    {
        var names = FgNames();
        var pars = ObjectInspector.Params(o);
        var linkBytes = gr.Links.Select(l => (l.Offset, End: l.Offset + FlowGraphEdit.LinkSize)).ToList();
        var rows = new List<ScriptValue>();
        foreach (var n in gr.Nodes.Where(n => n.Id is not (0 or 1)))
        {
            string step = $"{names.Type(n.Type)} #{n.Id & 0xfff}"; // same label as the script preview
            foreach (var p in pars.Where(p => p.Offset >= n.Start && p.Offset < n.End))
                rows.Add(new ScriptValue(n.Id, step, p.Label.Split(" → ")[^1], p.Type, p.Offset, p.Value));
            for (int i = n.Start + 4; i + 4 <= n.End; i++)
            {
                uint v = Bytes.U32(o.Body, i);
                if (v == n.Id || linkBytes.Any(l => i >= l.Offset && i < l.End) || FgObjectName(v) is not { } what) continue;
                rows.Add(new ScriptValue(n.Id, step, what, "object", i, Bytes.Hex(v)));
                i += 3;
            }
        }
        return rows;
    }

    void EditScriptValues(GameObject o, FlowGraph gr)
    {
        var rows = ScriptValues(o, gr);
        if (rows.Count == 0) { Warn($"\"{gr.Name}\" has no numbers or object references ModKit can change."); return; }
        var owner = o.DraftMod ?? TargetMod();
        if (owner is null) return;
        // what's already changed: the new object's own edits, or value edits in the target mod
        List<FieldEdit> Existing() => o.Draft is { } d ? d.Edits
            : owner.FieldEdits.Where(x => x.Package == o.Package && x.Tag == o.Tag && x.Object == o.Key).ToList();
        string? Current(int offset) => Existing().FirstOrDefault(x => x.Offset == offset)?.Value;

        using var dlg = new ThemedForm { Text = $"Values in \"{gr.Name}\" of {o.Name}", Width = 1000, Height = 600, StartPosition = FormStartPosition.CenterParent };
        var grid = EditGrid();
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.Columns.Add("Step", "Step"); grid.Columns.Add("What", "What"); grid.Columns.Add("Game", "Game value"); grid.Columns.Add("Yours", "Your value");
        foreach (DataGridViewColumn c in grid.Columns) c.ReadOnly = c.Name != "Yours";
        grid.Columns["Step"]!.FillWeight = 70; grid.Columns["What"]!.FillWeight = 120; grid.Columns["Game"]!.FillWeight = 50; grid.Columns["Yours"]!.FillWeight = 50;
        foreach (var r in rows) grid.Rows.Add(r.Step, r.Type == "object" ? r.What : $"{r.What} ({r.Type})", r.Game, Current(r.Offset) ?? "");
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        dlg.Controls.Add(grid);
        dlg.Controls.Add(Hint("Numbers are the step's settings (amounts, durations, chances). Object rows are what a step points at: type another object's id " +
                              "(0x…, from Browse game data) to point it somewhere else, e.g. which objective a reward starts. Clear \"Your value\" to go back to the game's. " +
                              $"Saved to \"{owner.Name}\". Test script changes on a new save."));
        dlg.Controls.Add(Bar(ok));
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var set = new List<FieldEdit>(); var cleared = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string yours = grid.Rows[i].Cells["Yours"].Value?.ToString()?.Trim() ?? "";
            if (yours.Length == 0 || yours.Equals(r.Game, StringComparison.OrdinalIgnoreCase)) { cleared.Add(r.Offset); continue; }
            var e = new FieldEdit
            {
                Package = o.Package, Tag = o.Tag, Object = o.Key, Offset = r.Offset, Type = r.Type == "object" ? "u32" : r.Type, Value = yours,
                Expect = Convert.ToHexString(o.Body, r.Offset, FieldEdit.SizeOf(r.Type == "object" ? "u32" : r.Type)),
                Note = $"{o.Name}: script \"{gr.Name}\" {r.Step}: {r.What}",
            };
            try { e.Encode(); }
            catch (Exception ex) when (ex is FormatException or OverflowException) { Warn($"{r.Step} {r.What}: \"{yours}\" isn't a valid {e.Type} ({ex.Message})."); return; }
            if (r.Type == "object" && FgObjectName(BitConverter.ToUInt32(e.Encode())) is null
                && MessageBox.Show(this, $"{yours} isn't an object ModKit knows. Use it anyway?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            set.Add(e);
        }
        void Apply(List<FieldEdit> list)
        {
            list.RemoveAll(x => (o.Draft is not null || x.Package == o.Package && x.Tag == o.Tag && x.Object == o.Key)
                                && (cleared.Contains(x.Offset) || set.Any(s => s.Offset == x.Offset)));
            list.AddRange(set);
        }
        if (o.Draft is { } draft) { Apply(draft.Edits); SaveDraft(o); }
        else EditMod(owner, 3, () => Apply(owner.FieldEdits));
        Log($"{o.Name}: script \"{gr.Name}\": {set.Count} values set, {cleared.Count(c => Existing().All(x => x.Offset != c))} at the game's");
    }

    sealed record ScriptHit(GameObject Obj, FlowGraph Graph, string Label, string Search);
    List<ScriptHit>? _scriptIndex;

    /// <summary>Every script in the game, with the words a search can match: names, steps, and what the steps point at.</summary>
    List<ScriptHit> ScriptIndex()
    {
        if (_scriptIndex is not null) return _scriptIndex;
        var names = FgNames();
        var list = new List<ScriptHit>();
        foreach (var o in _game!.Objects.Where(x => !x.IsRecord && x.Body.AsSpan().IndexOf("FlowGraph/"u8) >= 0).DistinctBy(x => (x.Package, x.ObjectId)))
            foreach (var g in FlowGraph.Find(o.Body))
            {
                var words = new System.Text.StringBuilder($"{o.Name} {o.Tag} {Bytes.Hex(o.ObjectId)} {g.Name} {g.Template} ");
                foreach (var n in g.Nodes) words.Append(names.Type(n.Type)).Append(' ');
                for (int i = g.Start; i + 4 <= g.End; i++)
                    if (FgObjectName(Bytes.U32(o.Body, i)) is { } on) { words.Append(Bytes.Hex(Bytes.U32(o.Body, i))).Append(' ').Append(on).Append(' '); i += 3; }
                list.Add(new ScriptHit(o, g, $"{g.Name}  —  {o.Name}  ({o.Package})", words.ToString()));
            }
        return _scriptIndex = list;
    }

    void FindScripts()
    {
        if (_game is null) { Warn("Game data hasn't loaded yet."); return; }
        List<ScriptHit> all = new();
        if (!RunSync("Indexing scripts", () => all = ScriptIndex())) return;
        var names = FgNames();
        using var dlg = new ThemedForm { Text = $"Find scripts ({all.Count:N0} in the game)", Width = 1150, Height = 700, StartPosition = FormStartPosition.CenterParent };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Words (all must match): script, object or step names, or an object id like 0xa86059c2 to find what points at it" };
        var list = new ListBox { Dock = DockStyle.Left, Width = 520, IntegralHeight = false, HorizontalScrollbar = true };
        var preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
        var count = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Theme.Muted };
        var shown = new List<ScriptHit>();
        void Fill()
        {
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            shown = all.Where(h => words.All(w => h.Search.Contains(w, StringComparison.OrdinalIgnoreCase))).Take(2000).ToList();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var h in shown) list.Items.Add(h.Label);
            list.EndUpdate();
            count.Text = shown.Count == 2000 ? "First 2,000 matches; add words to narrow it down." : $"{shown.Count:N0} matches";
        }
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var h = shown[list.SelectedIndex];
            preview.Text = h.Graph.Describe(h.Obj.Body, names, FgObjectName, ObjectInspector.Params(h.Obj)).ReplaceLineEndings(Environment.NewLine);
        };
        Debounce(search, Fill);
        var open = new Button { Text = "Open its object", DialogResult = DialogResult.OK, AutoSize = true };
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(preview);
        dlg.Controls.Add(new Splitter { Dock = DockStyle.Left });
        dlg.Controls.Add(list);
        dlg.Controls.Add(count);
        dlg.Controls.Add(search);
        dlg.Controls.Add(Hint("Every script in the game. Open one to see it on the Browse tab; its Scripts… button then lets you change values, arrows and steps."));
        dlg.Controls.Add(Bar(open));
        Fill();
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        _tabs.SelectedTab = _browsePage;
        Show(shown[list.SelectedIndex].Obj);
    }

    /// <summary>Short work on the UI thread behind a wait cursor (the index is built once and cached).</summary>
    bool RunSync(string what, Action work)
    {
        var cur = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        _status.Text = what + "…";
        try { work(); _status.Text = "Ready."; return true; }
        catch (Exception e) { Log($"ERROR {what}: {e}"); Warn($"{what} failed: {e.Message}"); return false; }
        finally { Cursor.Current = cur; }
    }
}
