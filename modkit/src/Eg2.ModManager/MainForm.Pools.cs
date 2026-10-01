using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Scheme pool window: which schemes a pool (rspl) offers; add game schemes or the mod's own new ones, remove others.</summary>
sealed partial class MainForm
{
    string SchemeLabel(string r, ModDefinition? mod)
    {
        if (r.StartsWith('@'))
        {
            var n = mod?.NewObjects.FirstOrDefault(x => x.Tag == "rscm" && x.Key.Equals(r[1..], StringComparison.OrdinalIgnoreCase));
            return n is null ? $"{r} (not a new scheme of {mod?.Name ?? "this mod"})" : $"{n.Texts.FirstOrDefault()?.Text ?? n.Key}  — new in {mod!.Name} ({r})";
        }
        return RefName(r) + $"  ({r})";
    }

    void ShowPool(GameObject o)
    {
        if (_game is null) return;
        if (o.Draft is not null) { Warn("This pool is one of a mod's new objects; edit the pools the game uses instead."); return; }
        if (SchemePool.TryParse(o.Tag, o.Body) is not { } pool) { Warn($"{o.Name}: this scheme pool can't be read safely."); return; }
        string self = Bytes.Hex(o.ObjectId);
        var gameRefs = pool.Entries.Select(e => Bytes.Hex(e.Scheme)).ToList();
        var hit = ModsInList().SelectMany(m => m.PoolEdits.Select(e => (m, e))).FirstOrDefault(x => x.e.Pool.Equals(self, StringComparison.OrdinalIgnoreCase));
        var mod = hit.m ?? TargetMod();
        if (mod is null) return;
        // the pool as the mod leaves it: game entries minus removed, plus added
        var cur = gameRefs.Where(r => hit.e is null || !hit.e.Remove.Contains(r, StringComparer.OrdinalIgnoreCase)).Concat(hit.e?.Add ?? new()).ToList();

        using var dlg = new ThemedForm { Text = $"Schemes offered by pool {o.Name} — saved in {mod.Name}", Width = 900, Height = 480, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        void Fill(int sel) { list.Items.Clear(); foreach (var r in cur) list.Items.Add(SchemeLabel(r, mod)); list.SelectedIndex = Math.Min(sel, list.Items.Count - 1); }
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var reset = new Button { Text = "Back to the game's", DialogResult = DialogResult.Abort, AutoSize = true };
        dlg.Controls.Add(list);
        dlg.Controls.Add(Hint("The game picks one of these (each entry equally likely; a scheme listed twice is twice as likely) when it offers a " +
                              "new scheme from this pool. Already-offered schemes keep their old version."));
        dlg.Controls.Add(Bar(
            Btn("Add a scheme…", (_, _) => { if (PickScheme(mod) is { } r) { cur.Add(r); Fill(cur.Count - 1); } }),
            Btn("Remove", (_, _) => { if (list.SelectedIndex >= 0 && cur.Count > 1) { cur.RemoveAt(list.SelectedIndex); Fill(list.SelectedIndex); } else Warn("A pool needs at least one scheme."); }),
            ok, reset));
        Fill(0);
        var res = dlg.ShowDialog(this);
        if (res is not (DialogResult.OK or DialogResult.Abort)) return;
        var (left, remove) = PoolEdit.Diff(gameRefs, cur);
        PoolEdit? edit = res == DialogResult.OK && (left.Count > 0 || remove.Count > 0)
            ? new PoolEdit { Pool = self, Add = left, Remove = remove, Note = o.Name.Split("  (")[0] } : null;
        if (edit is null && hit.m is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.PoolEdits.RemoveAll(e => e.Pool.Equals(self, StringComparison.OrdinalIgnoreCase));
        if (edit is not null) mod.PoolEdits.Add(edit);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: scheme pool {o.Name} {(edit is null ? "back to the game's" : $"set ({cur.Count} schemes)")}");
    }

    /// <summary>Pick a game scheme ("0x...") or one of <paramref name="mod"/>'s new schemes ("@Key").</summary>
    string? PickScheme(ModDefinition mod)
    {
        var all = mod.NewObjects.Where(x => x.Tag == "rscm").Select(x => (Ref: "@" + x.Key, Label: SchemeLabel("@" + x.Key, mod)))
            .Concat(_game!.Objects.Where(x => x.Tag == "rscm" && !x.IsRecord).DistinctBy(x => x.ObjectId)
                .Select(x => (Ref: Bytes.Hex(x.ObjectId), Label: $"{x.Name}  [{x.Package}]")).OrderBy(x => x.Label))
            .ToList();
        using var dlg = new ThemedForm { Text = "Add a scheme", Width = 900, Height = 600, StartPosition = FormStartPosition.CenterParent };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search scheme name or package (this mod's new schemes are listed first)" };
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
