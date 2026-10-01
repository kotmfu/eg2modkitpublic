using System.Globalization;
using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Scheme window: minion alternatives, heat gain, offer expiry.</summary>
sealed partial class MainForm
{
    void ShowScheme(GameObject o)
    {
        if (_game is null) return;
        var game = Scheme.TryParse(o.Tag, o.Body);
        if (game is null) { Warn($"{o.Name}: this scheme's layout can't be edited safely yet."); return; }
        _minionTypes ??= Requirements.MinionTypes(_game);
        string objRef = SwapRef(o);
        var owners = o.DraftMod is { } dm ? new[] { dm } : ModsInList();
        var hit = owners.SelectMany(m => m.SchemeEdits.Select(e => (m, e))).FirstOrDefault(x => x.e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));
        var gameGroups = game.Minions.Select(gr => gr.Select(m => new CountedRef { Ref = Bytes.Hex(m.Type), Count = m.Count }).ToList()).ToList();
        var minions = _minionTypes.Select(t => new Choice(Bytes.Hex(t.Key), t.Value)).OrderBy(c => c.Label).ToList();
        var inv = CultureInfo.InvariantCulture;

        using var dlg = new ThemedForm { Text = $"Scheme: {o.Name}", Width = 780, Height = 640, StartPosition = FormStartPosition.CenterParent };
        var grid = EditGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Group", ValueType = typeof(int), FillWeight = 40 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Minion type", DataSource = minions, DisplayMember = nameof(Choice.Label), ValueMember = nameof(Choice.Ref), FlatStyle = FlatStyle.Flat, FillWeight = 200 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "How many", ValueType = typeof(uint), FillWeight = 60 });
        var groups = hit.e?.Minions ?? gameGroups;
        for (int gi = 0; gi < groups.Count; gi++)
            foreach (var m in groups[gi])
            {
                if (!minions.Any(c => c.Ref.Equals(m.Ref, StringComparison.OrdinalIgnoreCase))) minions.Add(new Choice(m.Ref, m.Ref));
                grid.Rows.Add(gi + 1, minions.First(c => c.Ref.Equals(m.Ref, StringComparison.OrdinalIgnoreCase)).Ref, m.Count);
            }
        var resources = _game.Objects.Where(x => x.Tag == "rcns" && !x.IsRecord).DistinctBy(x => x.ObjectId)
            .Select(x => new Choice(Bytes.Hex(x.ObjectId), x.Name.Trim('"'))).OrderBy(c => c.Label).ToList();
        var gameCosts = game.Costs.Select(c => new CountedRef { Ref = Bytes.Hex(c.Resource), Count = c.Amount }).ToList();
        var costGrid = EditGrid();
        costGrid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Launch cost: resource", DataSource = resources, DisplayMember = nameof(Choice.Label), ValueMember = nameof(Choice.Ref), FlatStyle = FlatStyle.Flat, FillWeight = 200 });
        costGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Amount", ValueType = typeof(uint), FillWeight = 60 });
        foreach (var c in hit.e?.Costs ?? gameCosts)
        {
            if (!resources.Any(x => x.Ref.Equals(c.Ref, StringComparison.OrdinalIgnoreCase))) resources.Add(new Choice(c.Ref, c.Ref));
            costGrid.Rows.Add(resources.First(x => x.Ref.Equals(c.Ref, StringComparison.OrdinalIgnoreCase)).Ref, c.Count);
        }
        var duration = new TextBox { Width = 90, Text = (hit.e?.Duration ?? game.Duration).ToString(inv) };
        var heat = new TextBox { Width = 90, Text = (hit.e?.Heat ?? game.Heat).ToString(inv) };
        var expiry = new TextBox { Width = 90, Text = (hit.e?.Expiry ?? game.Expiry).ToString(inv) };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var reset = new Button { Text = "Back to the game's", DialogResult = DialogResult.Abort, AutoSize = true };
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        split.Controls.Add(grid, 0, 0); split.Controls.Add(costGrid, 0, 1);
        dlg.Controls.Add(split);
        if (game.Heat < 0)
            dlg.Controls.Add(new Label { Dock = DockStyle.Top, Height = 36, ForeColor = Theme.Warn, Padding = new Padding(4),
                Text = "Heat-lowering scheme (put on the map by a region upgrade). A price change didn't show on an offer already on the map (its duration did); check a newly offered one." });
        dlg.Controls.Add(Hint("Minions: rows with the same Group number are needed together; with several groups each offer uses one of them (the game picks). " +
                              "No rows = no minions needed. Launch cost: the intel versions of schemes cost Intel, some cost Gold. Duration in seconds (7200 = 2h). Heat gain goes to the region (negative lowers heat). Expiry = seconds the offer stays on the map (-1 = stays)."));
        dlg.Controls.Add(Bar(Lbl("Duration (s)"), duration, Lbl("Heat gain"), heat, Lbl("Offer expires after (s)"), expiry, ok, reset));

        List<List<CountedRef>> Read() => grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && r.Cells[1].Value is string)
            .GroupBy(r => r.Cells[0].Value is int g ? g : 1).OrderBy(x => x.Key)
            .Select(x => x.Select(r => new CountedRef { Ref = (string)r.Cells[1].Value, Count = r.Cells[2].Value is uint c ? c : 1 }).ToList()).ToList();
        SchemeEdit? edit = null;
        dlg.FormClosing += (_, e) =>
        {
            if (dlg.DialogResult != DialogResult.OK) return;
            if (!float.TryParse(heat.Text, NumberStyles.Float, inv, out var h) || !float.TryParse(expiry.Text, NumberStyles.Float, inv, out var x)
                || !float.TryParse(duration.Text, NumberStyles.Float, inv, out var du))
            { Warn("Duration, heat gain and expiry must be numbers (e.g. 15 or -1)."); e.Cancel = true; return; }
            var ms = Read();
            var cs = costGrid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && r.Cells[0].Value is string)
                .Select(r => new CountedRef { Ref = (string)r.Cells[0].Value, Count = r.Cells[1].Value is uint a ? a : 1 }).ToList();
            bool sameCosts = cs.Count == gameCosts.Count && cs.Zip(gameCosts).All(q => q.First.Ref.Equals(q.Second.Ref, StringComparison.OrdinalIgnoreCase) && q.First.Count == q.Second.Count);
            var check = new Scheme { Heat = h, Expiry = x, Duration = du };
            check.Minions.AddRange(ms.Select(g => g.Select(m => (1u, m.Count)).ToList()));
            if (check.Problems() is { Count: > 0 } bad) { Warn("Can't save: " + string.Join("; ", bad) + "."); e.Cancel = true; return; }
            bool same = ms.Count == gameGroups.Count && ms.Zip(gameGroups).All(p => p.First.Count == p.Second.Count &&
                        p.First.Zip(p.Second).All(q => q.First.Ref.Equals(q.Second.Ref, StringComparison.OrdinalIgnoreCase) && q.First.Count == q.Second.Count));
            edit = new SchemeEdit { Object = objRef, Minions = same ? null : ms, Heat = h == game.Heat ? null : h, Expiry = x == game.Expiry ? null : x, Duration = du == game.Duration ? null : du, Costs = sameCosts ? null : cs, Note = o.Name };
            if (edit.Minions is null && edit.Heat is null && edit.Expiry is null && edit.Duration is null && edit.Costs is null) edit = null;
        };
        var res = dlg.ShowDialog(this);
        if (res is not (DialogResult.OK or DialogResult.Abort)) return;
        if (edit is null && hit.m is null) return;
        var mod = o.DraftMod ?? hit.m ?? TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.SchemeEdits.RemoveAll(e => e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));
        if (edit is not null) mod.SchemeEdits.Add(edit);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: scheme {o.Name} {(edit is null ? "back to the game's" : "changed")}");
    }
}
