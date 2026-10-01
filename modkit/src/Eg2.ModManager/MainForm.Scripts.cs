using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Scripts window: read an object's scripts and swap one for a copy of another object's script.</summary>
sealed partial class MainForm
{
    FlowGraph.Names FgNames() =>
        _fgNames ??= FlowGraph.Names.Learn(_game!.Objects.Where(x => x.Body.AsSpan().IndexOf("FlowGraph/"u8) >= 0).Select(x => x.Body));

    string? FgObjectName(uint id) => id > 0x1000000 && _game!.ObjectsById.TryGetValue(id, out var l) ? $"{l[0].Tag} {l[0].Name}" : null;

    /// <summary>How mods refer to this object in a ScriptSwap: "@Key" for a new object, "0x..." for a game object.</summary>
    static string SwapRef(GameObject o) => o.Draft is { } d ? "@" + d.Key : Bytes.Hex(o.ObjectId);

    IEnumerable<ModDefinition> SwapOwners(GameObject o) => o.DraftMod is { } m ? new[] { m } : ModsInList();

    (ModDefinition Mod, ScriptSwap Swap)? SwapOf(GameObject o, int script) =>
        SwapOwners(o).SelectMany(m => m.ScriptSwaps.Select(s => (m, s)))
            .FirstOrDefault(x => x.s.Object.Equals(SwapRef(o), StringComparison.OrdinalIgnoreCase) && x.s.Script == script) is { m: not null } hit ? hit : null;

    void ShowScripts()
    {
        if (_shown is not GameObject o || _game is null) return;
        var names = FgNames();
        var all = FlowGraph.Find(o.Body);
        var swappable = FlowGraph.Swappable(o.Body);

        using var dlg = new ThemedForm { Text = $"Scripts of {o.Name}", Width = 960, Height = 680, StartPosition = FormStartPosition.CenterParent };
        var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        var view = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9.5f) };
        var replace = Btn("Replace with a copy from…", (_, _) => { });
        var undo = Btn("Undo replacement", (_, _) => { });
        var rewire = Btn("Rewire links…", (_, _) => { if (pick.SelectedIndex >= 0) RewireLinks(o, all[pick.SelectedIndex]); });
        var structure = Btn("Nodes and links…", (_, _) => { if (pick.SelectedIndex >= 0) EditGraphStructure(o, all[pick.SelectedIndex]); });
        var values = Btn("Values…", (_, _) => { if (pick.SelectedIndex >= 0) EditScriptValues(o, all[pick.SelectedIndex]); });
        var find = Btn("Find scripts…", (_, _) => { dlg.Close(); FindScripts(); });
        for (int i = 0; i < all.Count; i++) pick.Items.Add($"{i + 1}. {all[i].Name}  ({all[i].Template.Replace("FlowGraph/", "")})");

        int SwapIndex() => pick.SelectedIndex < 0 ? -1 : swappable.FindIndex(s => s.Graph.Start == all[pick.SelectedIndex].Start);
        void Refresh()
        {
            if (pick.SelectedIndex < 0) return;
            int si = SwapIndex();
            var gr = all[pick.SelectedIndex];
            var text = gr.Describe(o.Body, names, FgObjectName, ObjectInspector.Params(o));
            if (si >= 0 && SwapOf(o, si) is var (m, s) && _game.Objects.FirstOrDefault(x => !x.IsRecord && Bytes.Hex(x.ObjectId).Equals(s.From, StringComparison.OrdinalIgnoreCase)) is { } donor)
            {
                var dg = FlowGraph.Swappable(donor.Body);
                text = $"REPLACED by mod \"{m.Name}\" with a copy of script {s.FromScript + 1} of {donor.Name}" +
                       (s.Values.Count > 0 ? $", changed: {string.Join(", ", s.Values.Select(v => $"{v.Note ?? "value"} = {v.Value}"))}" : "") +
                       Environment.NewLine + Environment.NewLine +
                       (s.FromScript < dg.Count ? dg[s.FromScript].Graph.Describe(donor.Body, names, FgObjectName, ObjectInspector.Params(donor)) : "") +
                       Environment.NewLine + "---- the game's script (not used while replaced) ----" + Environment.NewLine + text;
            }
            view.Text = text.ReplaceLineEndings(Environment.NewLine);
            replace.Enabled = si >= 0;
            undo.Enabled = si >= 0 && SwapOf(o, si) is not null;
        }
        pick.SelectedIndexChanged += (_, _) => Refresh();
        replace.Click += (_, _) => { if (SwapIndex() is var si and >= 0 && PickScriptSwap(o, si)) Refresh(); };
        undo.Click += (_, _) =>
        {
            if (SwapIndex() is not (var si and >= 0) || SwapOf(o, si) is not var (m, s)) return;
            if (ReferenceEquals(m, _current)) CommitEditor();
            m.ScriptSwaps.Remove(s);
            m.Save();
            if (ReferenceEquals(m, _current)) ShowEditor();
            Refresh();
        };
        dlg.Controls.Add(view);
        dlg.Controls.Add(Bar(pick, values, structure, rewire, replace, undo, find));
        dlg.Controls.Add(Hint("What this thing's game scripts do. Each node is a step (#n = its number); the arrows say which output feeds which input. " +
                              "Names are learned from the game data, so a few are guesses. \"Replace with a copy from…\" swaps the whole script, e.g. to change what a research rewards."));
        if (pick.Items.Count > 0) pick.SelectedIndex = 0;
        dlg.Shown += (_, _) => view.Select(0, 0);
        dlg.ShowDialog(this);
    }

    Dictionary<uint, HashSet<uint>>? _fgOutPins, _fgInPins;

    /// <summary>Per node type, the pins game graphs use as outputs / inputs (learned from every link in the game).</summary>
    void LearnPins()
    {
        if (_fgOutPins is not null) return;
        _fgOutPins = new(); _fgInPins = new();
        foreach (var x in _game!.Objects.Where(x => x.Body.AsSpan().IndexOf("FlowGraph/"u8) >= 0).DistinctBy(x => x.ObjectId))
            foreach (var gr in FlowGraph.Find(x.Body))
            {
                var type = gr.Nodes.ToDictionary(n => n.Id, n => n.Type);
                foreach (var l in gr.Links)
                {
                    if (type.TryGetValue(l.FromNode, out var ft)) (_fgOutPins.TryGetValue(ft, out var s) ? s : _fgOutPins[ft] = new()).Add(l.FromPin);
                    if (type.TryGetValue(l.ToNode, out var tt)) (_fgInPins.TryGetValue(tt, out var s2) ? s2 : _fgInPins[tt] = new()).Add(l.ToPin);
                }
            }
    }

    /// <summary>
    /// Change where a script's links go: destination node and pin, and the source pin. Links live inside their source
    /// node, so the source node stays. Saved as ordinary value edits (u32 at link +8 from pin, +12 to node, +16 to pin).
    /// </summary>
    void RewireLinks(GameObject o, FlowGraph gr)
    {
        LearnPins();
        var names = FgNames();
        var type = gr.Nodes.ToDictionary(n => n.Id, n => n.Type);
        string NodeLabel(uint id) => id == 0 ? "#0 Start" : id == 1 ? "#1 End" : $"#{id} {names.Type(type.GetValueOrDefault(id))}";
        var nodeChoices = gr.Nodes.Select(n => new Choice(n.Id.ToString(), NodeLabel(n.Id))).ToList();
        List<Choice> Pins(uint node, bool output)
        {
            var set = (output ? _fgOutPins! : _fgInPins!).TryGetValue(type.GetValueOrDefault(node), out var s) ? s : new HashSet<uint>();
            return set.Select(p => new Choice(p.ToString(), $"{names.Pin(p, output)} ({Bytes.Hex(p)})")).OrderBy(c => c.Label).ToList();
        }
        using var dlg = new ThemedForm { Text = $"Links of \"{gr.Name}\" in {o.Name}", Width = 1000, Height = 560, StartPosition = FormStartPosition.CenterParent };
        var grid = EditGrid();
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "From node", ReadOnly = true, FillWeight = 120 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "From pin", FlatStyle = FlatStyle.Flat, DisplayMember = "Label", ValueMember = "Ref", FillWeight = 90 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "To node", FlatStyle = FlatStyle.Flat, DataSource = nodeChoices, DisplayMember = "Label", ValueMember = "Ref", FillWeight = 120 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "To pin", FlatStyle = FlatStyle.Flat, DisplayMember = "Label", ValueMember = "Ref", FillWeight = 90 });
        void SetPins(int row, int col, uint node, bool output, uint current)
        {
            var list = Pins(node, output);
            if (!list.Any(c => c.Ref == current.ToString())) list.Insert(0, new Choice(current.ToString(), $"{names.Pin(current, output)} ({Bytes.Hex(current)})"));
            var cell = (DataGridViewComboBoxCell)grid.Rows[row].Cells[col];
            cell.DataSource = list; cell.DisplayMember = "Label"; cell.ValueMember = "Ref"; cell.Value = current.ToString();
        }
        foreach (var l in gr.Links)
        {
            int i = grid.Rows.Add(NodeLabel(l.FromNode), null, l.ToNode.ToString(), null);
            SetPins(i, 1, l.FromNode, true, l.FromPin);
            SetPins(i, 3, l.ToNode, false, l.ToPin);
        }
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2 || grid.Rows[e.RowIndex].Cells[2].Value is not string v) return;
            uint node = uint.Parse(v);
            var first = Pins(node, false).FirstOrDefault();
            SetPins(e.RowIndex, 3, node, false, first is null ? gr.Links[e.RowIndex].ToPin : uint.Parse(first.Ref));
        };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        dlg.Controls.Add(grid);
        dlg.Controls.Add(Hint("Each row is one arrow in the script. Change where it goes (node and its input pin) or which output of its node it leaves from. " +
                              "Pin lists come from how the game's own scripts use each node type. Test script changes on a new save."));
        dlg.Controls.Add(Bar(ok));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var edits = new List<FieldEdit>();
        for (int i = 0; i < gr.Links.Count; i++)
        {
            var l = gr.Links[i];
            void Add(int rel, uint was, object? now, string what)
            {
                if (now is not string sv || uint.Parse(sv) == was) return;
                edits.Add(new FieldEdit { Package = o.Package, Tag = o.Tag, Object = o.Key, Offset = l.Offset + rel, Type = "u32", Value = Bytes.Hex(uint.Parse(sv)),
                                          Expect = Convert.ToHexString(o.Body, l.Offset + rel, 4), Note = $"{o.Name}: script \"{gr.Name}\" link {NodeLabel(l.FromNode)} -> {what}" });
            }
            Add(8, l.FromPin, grid.Rows[i].Cells[1].Value, "from pin");
            Add(12, l.ToNode, grid.Rows[i].Cells[2].Value, "to node");
            Add(16, l.ToPin, grid.Rows[i].Cells[3].Value, "to pin");
        }
        if (edits.Count == 0) return;
        if (o.Draft is { } d)
        {
            foreach (var e in edits) { d.Edits.RemoveAll(x => x.Offset == e.Offset); d.Edits.Add(e); }
            SaveDraft(o);
        }
        else if (TargetMod() is { } mod)
            EditMod(mod, 3, () =>
            {
                foreach (var e in edits) { mod.FieldEdits.RemoveAll(x => x.Package == e.Package && x.Tag == e.Tag && x.Object == e.Object && x.Offset == e.Offset); mod.FieldEdits.Add(e); }
            });
        Log($"{o.Name}: script \"{gr.Name}\": {edits.Count} link values changed");
    }

    /// <summary>Pick a donor script, set its numbers, and save the swap in the right mod. True if saved.</summary>
    bool PickScriptSwap(GameObject o, int script)
    {
        var names = FgNames();
        var target = FlowGraph.Swappable(o.Body)[script].Graph;
        string root = target.Template.Replace("FlowGraph/", "").Split('\\', '/')[0];   // e.g. "Rewards"
        var choices = _game!.Objects.Where(x => !x.IsRecord && x.Body.AsSpan().IndexOf("FlowGraph/"u8) >= 0)
            .SelectMany(x => FlowGraph.Swappable(x.Body).Select((s, i) => (Obj: x, Index: i, s.Graph)))
            .Where(c => c.Graph.Template.Replace("FlowGraph/", "").StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => (c.Graph.Name, c.Graph.Template, c.Obj.Body.Length))   // the same script is copied into many objects
            .OrderBy(c => c.Graph.Name).ToList();

        using var dlg = new ThemedForm { Text = $"Replace \"{target.Name}\" with…", Width = 980, Height = 640, StartPosition = FormStartPosition.CenterParent };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search script, object or step names (e.g. gold, heat, minion)" };
        var list = new ListBox { Dock = DockStyle.Left, Width = 440, IntegralHeight = false };
        var preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
        var shown = new List<(GameObject Obj, int Index, FlowGraph Graph)>();
        void Fill()
        {
            list.Items.Clear(); shown.Clear();
            foreach (var c in choices)
            {
                string label = $"{c.Graph.Name}  —  {c.Obj.Name}";
                string steps = string.Join(" ", c.Graph.Nodes.Select(n => names.Type(n.Type)));
                if (search.Text.Length > 0 && !$"{label} {c.Graph.Template} {steps}".Contains(search.Text, StringComparison.OrdinalIgnoreCase)) continue;
                shown.Add(c); list.Items.Add(label);
            }
        }
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var c = shown[list.SelectedIndex];
            preview.Text = c.Graph.Describe(c.Obj.Body, names, FgObjectName, ObjectInspector.Params(c.Obj)).ReplaceLineEndings(Environment.NewLine);
        };
        search.TextChanged += (_, _) => Fill();
        Fill();
        var ok = new Button { Text = "Use this script", DialogResult = DialogResult.OK, AutoSize = true };
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(preview);
        dlg.Controls.Add(new Splitter { Dock = DockStyle.Left });
        dlg.Controls.Add(list);
        dlg.Controls.Add(search);
        dlg.Controls.Add(Hint($"Scripts of the same kind ({root}) from anywhere in the game. The copy replaces \"{target.Name}\"; you can change its numbers next."));
        dlg.Controls.Add(Bar(ok));
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return false;
        var pickd = shown[list.SelectedIndex];

        // its numbers: offsets relative to the copied block
        var block = FlowGraph.Swappable(pickd.Obj.Body)[pickd.Index].Block;
        int payloadAt = block.Offset + 9, payloadEnd = payloadAt + block.Payload().Length;
        var pars = ObjectInspector.Params(pickd.Obj).Where(p => p.Offset >= payloadAt && p.Offset < payloadEnd).ToList();
        var edits = new List<FieldEdit>();
        if (pars.Count > 0)
        {
            using var vd = new ThemedForm { Text = "Numbers in the copied script", Width = 620, Height = 420, StartPosition = FormStartPosition.CenterParent };
            var grid = EditGrid();
            grid.AllowUserToAddRows = false;
            grid.Columns.Add("What", "What"); grid.Columns.Add("Game", "Game value"); grid.Columns.Add("Yours", "Your value");
            grid.Columns[0].ReadOnly = grid.Columns[1].ReadOnly = true;
            foreach (var p in pars) grid.Rows.Add(p.Label, p.Value, "");
            var done = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            vd.Controls.Add(grid);
            vd.Controls.Add(Hint("Leave \"Your value\" empty to keep the copied script's number."));
            vd.Controls.Add(Bar(done));
            vd.AcceptButton = done;
            if (vd.ShowDialog(this) != DialogResult.OK) return false;
            for (int i = 0; i < pars.Count; i++)
            {
                var yours = grid.Rows[i].Cells[2].Value?.ToString()?.Trim();
                if (string.IsNullOrEmpty(yours) || yours == pars[i].Value) continue;
                var p = pars[i];
                var e = new FieldEdit { Offset = p.Offset - payloadAt, Type = p.Type == "u8" ? "u8" : p.Type, Value = yours, Note = p.Label,
                                        Expect = Convert.ToHexString(pickd.Obj.Body, p.Offset, FieldEdit.SizeOf(p.Type)) };
                try { e.Encode(); }
                catch (Exception ex) when (ex is FormatException or OverflowException) { Warn($"{p.Label}: \"{yours}\" isn't a valid {p.Type} ({ex.Message})."); return false; }
                edits.Add(e);
            }
        }

        var mod = o.DraftMod ?? SwapOf(o, script)?.Mod ?? TargetMod();
        if (mod is null) return false;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.ScriptSwaps.RemoveAll(s => s.Object.Equals(SwapRef(o), StringComparison.OrdinalIgnoreCase) && s.Script == script);
        mod.ScriptSwaps.Add(new ScriptSwap
        {
            Object = SwapRef(o), Script = script, From = Bytes.Hex(pickd.Obj.ObjectId), FromScript = pickd.Index, Values = edits,
            Note = $"{o.Name.Split("  (")[0]}: \"{target.Name}\" -> copy of \"{pickd.Graph.Name}\" ({pickd.Obj.Name})",
        });
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {o.Name} script \"{target.Name}\" replaced by a copy of \"{pickd.Graph.Name}\" ({edits.Count} numbers changed)");
        return true;
    }
}
