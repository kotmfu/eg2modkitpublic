using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Generic list editor: remove, duplicate and reorder the entries of any list inside an object.</summary>
sealed partial class MainForm
{
    static int ListStart(GameObject o) => o.IsRecord ? 0 : ObjectHeader.Size;

    void ShowLists(GameObject o)
    {
        if (_game is null) return;
        if (o.Draft is not null) { Warn("Lists of new objects aren't editable yet; edit the game object you copied, or use the type's own editor."); return; }
        var top = ObjectLists.Tree(o.Body, ListStart(o));
        if (top is null) { Warn($"{o.Name} doesn't rebuild exactly, so its lists can't be edited."); return; }
        var lists = ObjectLists.Find(top);
        if (lists.Count == 0) { Warn($"{o.Name} has no lists."); return; }
        var mods = ModsInList().ToList();
        ListEdit? Existing(int at) => mods.SelectMany(m => m.ListEdits).FirstOrDefault(e => e.Package.Equals(o.Package, StringComparison.OrdinalIgnoreCase)
            && e.Tag == o.Tag && e.Object.Equals(o.Key, StringComparison.OrdinalIgnoreCase) && e.At == at);

        using var dlg = new ThemedForm { Text = $"Lists in {o.Name}", Width = 1000, Height = 620, StartPosition = FormStartPosition.CenterParent };
        var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520 };
        foreach (var l in lists)
            pick.Items.Add($"@{l.At}: {l.Count} x entry 0x{l.EntryKey & 0xffff:x}{(Existing(l.At) is { } e ? $"  (changed by a mod: {e.Order.Count})" : "")}");
        var entries = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        var order = new List<int>();
        int cur = -1;
        var labels = new List<string>();
        void Fill(int sel)
        {
            entries.Items.Clear();
            foreach (var i in order) entries.Items.Add($"{entries.Items.Count + 1}. [game #{i + 1}] {labels[i]}");
            entries.SelectedIndex = Math.Min(sel, entries.Items.Count - 1);
        }
        pick.SelectedIndexChanged += (_, _) =>
        {
            cur = pick.SelectedIndex;
            var l = lists[cur];
            labels = l.Node.Children.Skip(1).Cast<Prop>().Select(p => ObjectLists.EntryLabel(p, _game)).ToList();
            order = Existing(l.At)?.Order.ToList() ?? Enumerable.Range(0, l.Count).ToList();
            Fill(0);
        };
        void Move(int by) { int i = entries.SelectedIndex, j = i + by; if (i < 0 || j < 0 || j >= order.Count) return; (order[i], order[j]) = (order[j], order[i]); Fill(j); }
        var save = Btn("Save this list", (_, _) =>
        {
            if (cur < 0) return;
            var l = lists[cur];
            bool same = order.SequenceEqual(Enumerable.Range(0, l.Count));
            var owner = mods.FirstOrDefault(m => m.ListEdits.Contains(Existing(l.At)!)) ?? (same ? null : TargetMod());
            if (owner is null) return;
            if (ReferenceEquals(owner, _current)) CommitEditor();
            owner.ListEdits.RemoveAll(e => e.Package.Equals(o.Package, StringComparison.OrdinalIgnoreCase) && e.Tag == o.Tag
                                           && e.Object.Equals(o.Key, StringComparison.OrdinalIgnoreCase) && e.At == l.At);
            if (!same) owner.ListEdits.Add(new ListEdit { Package = o.Package, Tag = o.Tag, Object = o.Key, At = l.At, Count = l.Count, Order = order.ToList(),
                                                          Note = $"{o.Name}: list @{l.At} ({l.Count} -> {order.Count} entries)" });
            owner.Save();
            if (ReferenceEquals(owner, _current)) ShowEditor();
            Log($"{owner.Id}: {o.Name} list @{l.At} {(same ? "back to the game's" : $"now {order.Count} entries")}");
        });
        dlg.Controls.Add(entries);
        dlg.Controls.Add(Hint("Any list inside this object. Remove, duplicate (a copy of the same entry) or reorder entries, then \"Save this list\". " +
                              "What an entry means depends on the type; prefer the type's own editor (Tasks…, Requirements…, Scheme…) where there is one."));
        dlg.Controls.Add(Bar(pick,
            Btn("Up", (_, _) => Move(-1)), Btn("Down", (_, _) => Move(1)),
            Btn("Remove", (_, _) => { if (entries.SelectedIndex >= 0 && order.Count > 1) { order.RemoveAt(entries.SelectedIndex); Fill(entries.SelectedIndex); } else Warn("A list keeps at least one entry."); }),
            Btn("Duplicate", (_, _) => { if (entries.SelectedIndex >= 0) { order.Insert(entries.SelectedIndex + 1, order[entries.SelectedIndex]); Fill(entries.SelectedIndex + 1); } }),
            Btn("Game order", (_, _) => { if (cur >= 0) { order = Enumerable.Range(0, lists[cur].Count).ToList(); Fill(0); } }),
            save));
        pick.SelectedIndex = 0;
        dlg.ShowDialog(this);
    }
}
