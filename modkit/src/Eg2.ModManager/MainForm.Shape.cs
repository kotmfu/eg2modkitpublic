using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Furniture size / slot count, and who may do a job.</summary>
sealed partial class MainForm
{
    void ShowShape(GameObject o)
    {
        if (_game is null || !o.IsRecord) return;
        string name = o.RecordName!;
        var fp = FurnitureShape.Footprint(o.Body);
        int slots = FurnitureShape.SlotCount(o.Body);
        var hit = ModsInList().SelectMany(m => m.ShapeEdits.Select(e => (m, e))).FirstOrDefault(x => x.e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        using var dlg = new ThemedForm { Text = $"Size & slots: {o.Name}", Width = 620, Height = 260, StartPosition = FormStartPosition.CenterParent };
        NumericUpDown Num(int v, int max) => new() { Minimum = 1, Maximum = max, Value = Math.Clamp(v, 1, max), Width = 70 };
        var w = Num(hit.e?.Width ?? fp?.W ?? 1, 32);
        var h = Num(hit.e?.Height ?? fp?.H ?? 1, 32);
        var s = Num(hit.e?.Slots ?? Math.Max(slots, 1), 64);
        w.Enabled = h.Enabled = fp is not null;
        s.Enabled = slots > 0;
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var reset = new Button { Text = "Back to the game's", DialogResult = DialogResult.Abort, AutoSize = true };
        var hint = Hint("");
        dlg.Controls.Add(hint);
        hint.Text = ($"Footprint in tiles (game: {(fp is { } f ? $"{f.W} x {f.H}" : "none")}); new tiles copy the nearest edge tile. The model isn't resized, " +
                              $"so a bigger footprint just reserves more floor. Job slots (game: {(slots > 0 ? slots.ToString() : "none")}): how many minions can use it at once " +
                              "(lockers: each slot adds 1 to the minion cap); extra slots copy the last one, so their minions stand in the same spot.");
        dlg.Controls.Add(Bar(Lbl("Width"), w, Lbl("Depth"), h, Lbl("Slots"), s, ok, reset));
        var res = dlg.ShowDialog(this);
        if (res is not (DialogResult.OK or DialogResult.Abort)) return;
        FurnitureShapeEdit? edit = null;
        if (res == DialogResult.OK)
        {
            edit = new FurnitureShapeEdit
            {
                Name = name, Note = o.Name,
                Width = fp is { } a && (int)w.Value != a.W ? (int)w.Value : null,
                Height = fp is { } b && (int)h.Value != b.H ? (int)h.Value : null,
                Slots = slots > 0 && (int)s.Value != slots ? (int)s.Value : null,
            };
            if (edit.Width is null && edit.Height is null && edit.Slots is null) edit = null;
        }
        if (edit is null && hit.m is null) return;
        var mod = hit.m ?? TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.ShapeEdits.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (edit is not null) mod.ShapeEdits.Add(edit);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: size/slots of {name} {(edit is null ? "back to the game's" : "changed")}");
    }

    void ShowJobTypes(GameObject o)
    {
        if (_game is null) return;
        var game = JobTypes.Parse(o.Body);
        if (game is null) { Warn($"{o.Name}: this job has no list of who may do it (anyone can), or it can't be read safely."); return; }
        _minionTypes ??= Requirements.MinionTypes(_game);
        string objRef = Bytes.Hex(o.ObjectId);
        var hit = ModsInList().SelectMany(m => m.JobEdits.Select(e => (m, e))).FirstOrDefault(x => x.e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));
        var gameKeys = game.Select(e => Bytes.Hex(e.Key)).ToList();
        var current = (hit.e?.Types ?? gameKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // minion types first, then the other characters this job lists (geniuses, henchmen, agents: unnamed in the files)
        var rows = _minionTypes.OrderBy(t => t.Value).Select(t => (Key: Bytes.Hex(t.Key), Label: t.Value))
            .Concat(gameKeys.Concat(current).Distinct(StringComparer.OrdinalIgnoreCase).Where(k => !_minionTypes.ContainsKey(Convert.ToUInt32(k, 16)))
                .Select(k => (Key: k, Label: $"Other character {k}")))
            .ToList();

        using var dlg = new ThemedForm { Text = $"Who can do {o.Name}", Width = 460, Height = 560, StartPosition = FormStartPosition.CenterParent };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        foreach (var r in rows) list.Items.Add(r.Label, current.Contains(r.Key));
        var users = _game.Objects.Where(x => x.IsRecord && x.Body.AsSpan().IndexOf(BitConverter.GetBytes(o.ObjectId)) >= 0).Select(x => x.Name.Split("  (")[^1].TrimEnd(')')).Distinct().ToList();
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var reset = new Button { Text = "Back to the game's", DialogResult = DialogResult.Abort, AutoSize = true };
        dlg.Controls.Add(list);
        dlg.Controls.Add(Hint($"Ticked characters may do this job. It's shared by {users.Count} furniture item(s): {string.Join(", ", users.Take(6))}{(users.Count > 6 ? ", …" : "")}. " +
                              "A newly ticked type copies the job settings of the first listed one."));
        dlg.Controls.Add(Bar(ok, reset));
        dlg.FormClosing += (_, e) => { if (dlg.DialogResult == DialogResult.OK && list.CheckedIndices.Count == 0) { Warn("Tick at least one."); e.Cancel = true; } };
        var res = dlg.ShowDialog(this);
        if (res is not (DialogResult.OK or DialogResult.Abort)) return;
        // keep the game's order for kept keys, new ones after
        var picked = list.CheckedIndices.Cast<int>().Select(i => rows[i].Key).ToList();
        var ordered = gameKeys.Where(k => picked.Contains(k, StringComparer.OrdinalIgnoreCase)).Concat(picked.Where(k => !gameKeys.Contains(k, StringComparer.OrdinalIgnoreCase))).ToList();
        JobEdit? edit = res == DialogResult.OK && !ordered.SequenceEqual(gameKeys, StringComparer.OrdinalIgnoreCase)
            ? new JobEdit { Object = objRef, Types = ordered, Note = $"{o.Name}: {string.Join(", ", ordered.Select(k => _minionTypes.TryGetValue(Convert.ToUInt32(k, 16), out var n) ? n : k))}" } : null;
        if (edit is null && hit.m is null) return;
        var mod = hit.m ?? TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.JobEdits.RemoveAll(e => e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));
        if (edit is not null) mod.JobEdits.Add(edit);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: who can do {o.Name} {(edit is null ? "back to the game's" : "changed")}");
    }
}
