using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Tasks window: an objective's task list (remove, reorder, copy tasks from other objectives).</summary>
sealed partial class MainForm
{
    HashSet<uint>? _roomIds;
    Dictionary<string, (GameObject Obj, List<ObjectiveTasks.Task> Tasks)>? _objectiveTasks;

    /// <summary>"0x&lt;objective&gt;:&lt;index&gt;" → readable label (task text, its step, and where it's from).</summary>
    string TaskLabel(string r, uint self)
    {
        _roomIds ??= _game!.Objects.Where(x => x.Tag == "room" && !x.IsRecord).Select(x => x.ObjectId).ToHashSet();
        _objectiveTasks ??= _game!.Objects.Where(x => x.Tag == "robj" && !x.IsRecord).DistinctBy(x => x.ObjectId)
            .Select(x => (Obj: x, Tasks: ObjectiveTasks.Parse(x.Body, _roomIds))).Where(x => x.Tasks is not null)
            .ToDictionary(x => Bytes.Hex(x.Obj.ObjectId), x => (x.Obj, x.Tasks!), StringComparer.OrdinalIgnoreCase);
        var parts = r.Split(':');
        if (parts.Length != 2 || !_objectiveTasks.TryGetValue(parts[0], out var hit) || !int.TryParse(parts[1], out int i) || i < 0 || i >= hit.Tasks.Count) return $"{r} (not found)";
        var (from, l) = hit;
        var t = l[i];
        string text = _game.Text.TryGetValue(t.TextKey, out var tx) ? ObjectInspector.Clean(tx.Text) : "(no text)";
        string step = t.Room != 0 && _game.ObjectsById.TryGetValue(t.Room, out var rl) ? $"  [starts {rl[0].Name}]" : "";
        return $"{text}{step}{(from.ObjectId == self ? "" : $"  — from {from.Name}")}";
    }

    void ShowTasks(GameObject o)
    {
        if (_game is null) return;
        _roomIds ??= _game.Objects.Where(x => x.Tag == "room" && !x.IsRecord).Select(x => x.ObjectId).ToHashSet();
        var game = ObjectiveTasks.Parse(o.Body, _roomIds);
        if (game is null) { Warn($"{o.Name}: its task list can't be read safely."); return; }
        string objRef = SwapRef(o);
        string self = Bytes.Hex(o.Draft is { } d ? Convert.ToUInt32(d.Source, 16) : o.ObjectId);
        uint selfId = Convert.ToUInt32(self, 16);
        var gameRefs = game.Select(t => $"{self}:{t.Index}").ToList();
        var owners = o.DraftMod is { } dm ? new[] { dm } : ModsInList();
        var hit = owners.SelectMany(m => m.TaskEdits.Select(e => (m, e))).FirstOrDefault(x => x.e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));
        var cur = (hit.e?.Tasks ?? gameRefs).ToList();

        using var dlg = new ThemedForm { Text = $"Tasks of {o.Name}", Width = 1000, Height = 520, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        void Fill(int sel) { list.Items.Clear(); foreach (var t in cur) list.Items.Add($"{list.Items.Count + 1}. {TaskLabel(t, selfId)}"); list.SelectedIndex = Math.Min(sel, list.Items.Count - 1); }
        void Move(int by)
        {
            int i = list.SelectedIndex, j = i + by;
            if (i < 0 || j < 0 || j >= cur.Count) return;
            (cur[i], cur[j]) = (cur[j], cur[i]);
            Fill(j);
        }
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var reset = new Button { Text = "Back to the game's", DialogResult = DialogResult.Abort, AutoSize = true };
        dlg.Controls.Add(list);
        dlg.Controls.Add(Hint("The objective's tasks in the order the game runs them; each starts its step (script) when it begins. " +
                              "Copied tasks bring their own text, conditions and step. Nothing is checked in-game: test story changes on a new save."));
        dlg.Controls.Add(Bar(
            Btn("Up", (_, _) => Move(-1)), Btn("Down", (_, _) => Move(1)),
            Btn("Remove", (_, _) => { if (list.SelectedIndex >= 0 && cur.Count > 1) { cur.RemoveAt(list.SelectedIndex); Fill(list.SelectedIndex); } else Warn("An objective needs at least one task."); }),
            Btn("Add a task from another objective…", (_, _) => { if (PickTask() is { } t) { cur.Insert(list.SelectedIndex + 1, t); Fill(list.SelectedIndex + 1); } }),
            ok, reset));
        Fill(0);
        var res = dlg.ShowDialog(this);
        if (res is not (DialogResult.OK or DialogResult.Abort)) return;
        TaskEdit? edit = res == DialogResult.OK && !cur.SequenceEqual(gameRefs, StringComparer.OrdinalIgnoreCase)
            ? new TaskEdit { Object = objRef, Tasks = cur, Note = o.Name.Split("  (")[0] } : null;
        if (edit is null && hit.m is null) return;
        var mod = o.DraftMod ?? hit.m ?? TargetMod();
        if (mod is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.TaskEdits.RemoveAll(e => e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase));
        if (edit is not null) mod.TaskEdits.Add(edit);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: tasks of {o.Name} {(edit is null ? "back to the game's" : $"set ({cur.Count})")}");
    }

    /// <summary>Pick any task of any objective; returns "0x&lt;objective&gt;:&lt;index&gt;".</summary>
    string? PickTask()
    {
        TaskLabel("", 0);   // fills the cache
        var all = _objectiveTasks!.SelectMany(x => x.Value.Tasks.Select(t => $"{x.Key}:{t.Index}"))
            .Select(r => (Ref: r, Label: TaskLabel(r, 0))).OrderBy(x => x.Label).ToList();
        using var dlg = new ThemedForm { Text = "Add a task", Width = 1000, Height = 600, StartPosition = FormStartPosition.CenterParent };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search task text, step or objective (e.g. kidnap, interrogate)" };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        var shown = new List<string>();
        void Fill()
        {
            list.BeginUpdate(); list.Items.Clear(); shown.Clear();
            foreach (var (r, l) in all.Where(x => search.Text.Length == 0 || x.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Take(2000)) { shown.Add(r); list.Items.Add(l); }
            list.EndUpdate();
        }
        search.TextChanged += (_, _) => Fill();
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, AutoSize = true };
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(list);
        dlg.Controls.Add(search);
        dlg.Controls.Add(Bar(ok));
        dlg.AcceptButton = ok;
        Fill();
        return dlg.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0 ? shown[list.SelectedIndex] : null;
    }
}
