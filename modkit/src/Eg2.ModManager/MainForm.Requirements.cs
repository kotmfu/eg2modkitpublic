using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Requirements window: which minions, furniture and resources a research / engineering item needs, and what it unlocks.</summary>
sealed partial class MainForm
{
    Dictionary<uint, string>? _minionTypes;
    Dictionary<string, uint>? _furnitureFnas;

    sealed record Choice(string Ref, string Label) { public override string ToString() => Label; }

    /// <param name="objRef">"0x..." for a game item, "@Key" for a new one.</param>
    /// <param name="owner">The mod that owns a new item (null for game items: saved in the target mod).</param>
    void ShowRequirements(string objRef, GameObject src, ModDefinition? owner)
    {
        if (_game is null) return;
        var game = Requirements.TryParse(src.Tag, src.Body);
        if (game is null) { Warn($"{src.Name}: its requirement lists can't be read safely yet, so they can't be changed."); return; }
        _minionTypes ??= Requirements.MinionTypes(_game);
        _furnitureFnas ??= Requirements.FurnitureFnas(_game);
        var owners = owner is not null ? new[] { owner } : ModsInList();
        var hit = owners.SelectMany(m => m.RequirementEdits.Select(e => (m, e))).FirstOrDefault(x => x.e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));

        // choices; refs as the mod file stores them
        var minions = _minionTypes.Select(t => new Choice(Bytes.Hex(t.Key), t.Value)).OrderBy(c => c.Label).ToList();
        var resources = _game.Objects.Where(x => x.Tag == "rcns" && !x.IsRecord).DistinctBy(x => x.ObjectId)
            .Select(x => new Choice(Bytes.Hex(x.ObjectId), x.Name.Trim('"'))).OrderBy(c => c.Label).ToList();
        var recNames = _game.Objects.Where(x => x.IsRecord).GroupBy(x => x.RecordName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(o => o.Name).FirstOrDefault(n => n.Contains("  (")) ?? x.Key, StringComparer.OrdinalIgnoreCase);
        var furniture = _furnitureFnas.Keys.Select(k => new Choice(k, recNames.TryGetValue(k, out var n) && n.IndexOf("  (") is > 0 and var i ? $"{n[(i + 3)..^1]}  [{k}]" : k))
            .Concat(ModsInList().SelectMany(m => m.NewFurniture).Select(f => new Choice(f.Id, $"{f.DisplayName}  [new: {f.Id}]")))
            .DistinctBy(c => c.Ref, StringComparer.OrdinalIgnoreCase).OrderBy(c => c.Label).ToList();
        var fnasName = _furnitureFnas.GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.First().Key);

        // current = the mod's lists where set, else the game's
        List<CountedRef> Counted(IEnumerable<(uint, uint)> l, Func<uint, string> name) => l.Select(x => new CountedRef { Ref = name(x.Item1), Count = x.Item2 }).ToList();
        string Fn(uint id) => fnasName.TryGetValue(id, out var n) ? n : Bytes.Hex(id);
        var gMin = Counted(game.Minions, Bytes.Hex);
        var gFur = Counted(game.Furniture, Fn);
        var gCost = Counted(game.Costs, Bytes.Hex);
        var gUn = game.Unlocks.Select(Fn).ToList();

        using var dlg = new ThemedForm { Text = $"Requirements of {src.Name.Split("  (")[0]}{(objRef.StartsWith('@') ? $" ({objRef})" : "")}", Width = 900, Height = 640, StartPosition = FormStartPosition.CenterParent };
        DataGridView Grid(List<Choice> choices, bool counted, string what)
        {
            var g = EditGrid();
            g.Dock = DockStyle.Fill;
            g.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = what, DataSource = choices, DisplayMember = nameof(Choice.Label), ValueMember = nameof(Choice.Ref), FillWeight = 300, FlatStyle = FlatStyle.Flat });
            if (counted) g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = what == "Resource" ? "Amount" : "How many", ValueType = typeof(uint) });
            return g;
        }
        void Fill(DataGridView g, IEnumerable<(string Ref, uint Count)> rows, List<Choice> choices)
        {
            foreach (var (r, c) in rows)
            {
                if (!choices.Any(x => x.Ref.Equals(r, StringComparison.OrdinalIgnoreCase))) choices.Add(new Choice(r, r));   // keep unknowns visible
                int i = g.Rows.Add();
                g.Rows[i].Cells[0].Value = choices.First(x => x.Ref.Equals(r, StringComparison.OrdinalIgnoreCase)).Ref;
                if (g.ColumnCount > 1) g.Rows[i].Cells[1].Value = c;
            }
        }
        var gm = Grid(minions, true, "Minion type");
        var gf = Grid(furniture, true, "Furniture needed in the lair");
        var gc = Grid(resources, true, "Resource");
        var gu = Grid(furniture, false, "Furniture it unlocks");
        Fill(gm, (hit.e?.Minions ?? gMin).Select(x => (x.Ref, x.Count)), minions);
        Fill(gf, (hit.e?.Furniture ?? gFur).Select(x => (x.Ref, x.Count)), furniture);
        Fill(gc, (hit.e?.Costs ?? gCost).Select(x => (x.Ref, x.Count)), resources);
        Fill(gu, (hit.e?.Unlocks ?? gUn).Select(x => (x, 0u)), furniture);

        List<CountedRef> Read(DataGridView g) => g.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && r.Cells[0].Value is string)
            .Select(r => new CountedRef { Ref = (string)r.Cells[0].Value, Count = g.ColumnCount > 1 && r.Cells[1].Value is uint c ? c : 1 }).ToList();
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        table.Controls.Add(gm, 0, 0); table.Controls.Add(gc, 1, 0); table.Controls.Add(gf, 0, 1); table.Controls.Add(gu, 1, 1);
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var reset = new Button { Text = "Back to the game's", DialogResult = DialogResult.Abort, AutoSize = true };
        dlg.Controls.Add(table);
        dlg.Controls.Add(Hint("Add a row at the bottom of a list, select a row and press Delete to remove it. " +
                              "Unlocks = furniture that becomes buildable when it's done (your new furniture is in the list too)."));
        dlg.Controls.Add(Bar(ok, reset));
        dlg.FormClosing += (_, e) =>
        {
            if (dlg.DialogResult != DialogResult.OK) return;
            var m = Read(gm); var f = Read(gf);
            var bad = Requirements.Problems(m.Count, f.Count, m.Concat(f).Any(x => x.Count == 0));
            if (bad.Count == 0) return;
            Warn("Can't save these requirements: it " + string.Join("; it ", bad) + ".");
            e.Cancel = true;
        };
        var res = dlg.ShowDialog(this);
        if (res is not (DialogResult.OK or DialogResult.Abort)) return;

        static bool Same(List<CountedRef> a, List<CountedRef> b) => a.Count == b.Count && a.Zip(b).All(x => x.First.Ref.Equals(x.Second.Ref, StringComparison.OrdinalIgnoreCase) && x.First.Count == x.Second.Count);
        RequirementEdit? edit = null;
        if (res == DialogResult.OK)
        {
            var m = Read(gm); var f = Read(gf); var c = Read(gc); var u = Read(gu).Select(x => x.Ref).ToList();
            edit = new RequirementEdit
            {
                Object = objRef,
                Minions = Same(m, gMin) ? null : m,
                Furniture = Same(f, gFur) ? null : f,
                Costs = Same(c, gCost) ? null : c,
                Unlocks = u.SequenceEqual(gUn, StringComparer.OrdinalIgnoreCase) ? null : u,
                Note = src.Name.Split("  (")[0],
            };
            if (edit.Minions is null && edit.Furniture is null && edit.Costs is null && edit.Unlocks is null) edit = null;
        }
        if (edit is null && hit.m is null) return;
        var mod = owner ?? hit.m ?? TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.RequirementEdits.RemoveAll(e => e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));
        if (edit is not null) mod.RequirementEdits.Add(edit);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: requirements of {src.Name.Split("  (")[0]} {(edit is null ? "back to the game's" : "changed")}");
    }
}
