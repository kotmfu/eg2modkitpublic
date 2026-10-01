using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Mods editor tab listing a mod's tree layouts, script swaps and requirement edits (edited on the Trees tab / dialogs).</summary>
sealed partial class MainForm
{
    readonly DataGridView _changesGrid = ReadOnlyGrid();
    List<(Action Open, Action Remove)> _changeActions = new();

    sealed record ChangeRow(string Kind, string What, string Details);

    TabPage ChangesPage()
    {
        var page = new TabPage("Trees, scripts, tasks…");
        page.Controls.Add(_changesGrid);
        page.Controls.Add(Bar(Btn("Open selected", (_, _) => ChangeAction(true)), Btn("Remove selected", (_, _) => ChangeAction(false))));
        page.Controls.Add(Hint("Research / engineering tree layouts (Trees tab), replaced scripts (Browse game data → Scripts…), objective task lists (Browse game data → Tasks…), schemes (Browse game data → Scheme…), scheme pools (Browse game data → Schemes offered…) and requirement changes " +
                               "(Requirements… buttons). Double-click to open one where it's edited."));
        _changesGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ChangeAction(true); };
        return page;
    }

    void ChangeAction(bool open)
    {
        if (_current is null || _changesGrid.CurrentRow is not { Index: var i } || i >= _changeActions.Count) return;
        if (open) { _changeActions[i].Open(); return; }
        if (MessageBox.Show(this, $"Remove \"{_changesGrid.Rows[i].Cells[1].Value}\" from {_current.Name}?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        CommitEditor();
        _changeActions[i].Remove();
        _current.Save();
        ShowEditor();
    }

    /// <summary>Display name of a "0x..." object or "@Key" new object.</summary>
    string RefName(string r)
    {
        if (r.StartsWith('@') || _game is null) return r;
        try
        {
            return _game.ObjectsById.TryGetValue(Convert.ToUInt32(r, 16), out var l)
                ? (l[0].Tag is "rttr" or "rctt" ? TreeName(l[0]) : l[0].Name.Split("  (")[0].Trim('"')) : r;
        }
        catch (FormatException) { return r; }
    }

    void FillChanges()
    {
        var rows = new List<ChangeRow>();
        _changeActions = new();
        var m = _current;
        if (m is not null)
        {
            void Tree(TreeLayout t, bool eng, List<TreeLayout> owner)
            {
                int added = t.Nodes.Count(n => n.Research.StartsWith('@'));
                rows.Add(new ChangeRow(eng ? "Engineering tree" : "Research tree", RefName(t.Tree),
                    $"{t.Nodes.Count} items{(added > 0 ? $" ({added} new)" : "")}, {t.Links.Count} prerequisite links{(t.Background is not null ? ", own background" : "")}"));
                _changeActions.Add((() => OpenTreeById(t.Tree), () => owner.Remove(t)));
            }
            foreach (var t in m.ResearchTrees) Tree(t, false, m.ResearchTrees);
            foreach (var t in m.EngineeringTrees) Tree(t, true, m.EngineeringTrees);
            foreach (var s in m.ScriptSwaps)
            {
                rows.Add(new ChangeRow("Script swap", RefName(s.Object),
                    (s.Note ?? $"script {s.Script + 1} -> copy of script {s.FromScript + 1} of {RefName(s.From)}") +
                    (s.Values.Count > 0 ? $"; {string.Join(", ", s.Values.Select(v => $"{v.Note ?? "value"} = {v.Value}"))}" : "")));
                _changeActions.Add((() => OpenInBrowse(s.Object), () => m.ScriptSwaps.Remove(s)));
            }
            foreach (var e in m.RequirementEdits)
            {
                if (_game is not null) _minionTypes ??= Requirements.MinionTypes(_game);
                string Minion(string r) => _minionTypes is not null && uint.TryParse(r.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var h) && _minionTypes.TryGetValue(h, out var n) ? n : r;
                var parts = new List<string>();
                if (e.Minions is { } ms) parts.Add("minions: " + string.Join(", ", ms.Select(x => $"{x.Count} {Minion(x.Ref)}")));
                if (e.Furniture is { } fs) parts.Add("furniture: " + string.Join(", ", fs.Select(x => $"{x.Count} {x.Ref}")));
                if (e.Costs is { } cs) parts.Add("cost: " + string.Join(", ", cs.Select(x => $"{x.Count} {RefName(x.Ref)}")));
                if (e.Unlocks is { } us) parts.Add("unlocks: " + (us.Count == 0 ? "nothing" : string.Join(", ", us)));
                rows.Add(new ChangeRow("Requirements", RefName(e.Object), string.Join("; ", parts)));
                _changeActions.Add((() => OpenRequirements(m, e.Object), () => m.RequirementEdits.Remove(e)));
            }
            foreach (var e in m.TaskEdits)
            {
                rows.Add(new ChangeRow("Tasks", RefName(e.Object), $"{e.Tasks.Count} tasks: {string.Join(", ", e.Tasks)}"));
                _changeActions.Add((() => OpenInBrowse(e.Object), () => m.TaskEdits.Remove(e)));
            }
            foreach (var e in m.SchemeEdits)
            {
                if (_game is not null) _minionTypes ??= Requirements.MinionTypes(_game);
                string Minion(string r) => _minionTypes is not null && uint.TryParse(r.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var h) && _minionTypes.TryGetValue(h, out var n) ? n : r;
                var parts = new List<string>();
                if (e.Minions is { } ms) parts.Add("minions: " + (ms.Count == 0 ? "none" : string.Join(" or ", ms.Select(g => string.Join(" + ", g.Select(x => $"{x.Count} {Minion(x.Ref)}"))))));
                if (e.Duration is { } du) parts.Add($"duration {du} s");
                if (e.Heat is { } hh) parts.Add($"heat {hh}");
                if (e.Costs is { } cc) parts.Add("cost: " + (cc.Count == 0 ? "free" : string.Join(", ", cc.Select(x => $"{x.Count} {RefName(x.Ref)}"))));
                if (e.Expiry is { } xx) parts.Add($"expires after {xx}s");
                rows.Add(new ChangeRow("Scheme", RefName(e.Object), string.Join("; ", parts)));
                _changeActions.Add((() => OpenInBrowse(e.Object), () => m.SchemeEdits.Remove(e)));
            }
            foreach (var e in m.PoolEdits)
            {
                var parts = new List<string>();
                if (e.Add.Count > 0) parts.Add("adds " + string.Join(", ", e.Add.Select(RefName)));
                if (e.Remove.Count > 0) parts.Add("removes " + string.Join(", ", e.Remove.Select(RefName)));
                rows.Add(new ChangeRow("Scheme pool", RefName(e.Pool), string.Join("; ", parts)));
                _changeActions.Add((() => OpenInBrowse(e.Pool), () => m.PoolEdits.Remove(e)));
            }
            foreach (var e in m.MapEdits)
            {
                rows.Add(new ChangeRow("Lair map", $"{Path.GetFileNameWithoutExtension(e.File)} floor {e.Floor}",
                    (e.Action ?? "tier").ToLowerInvariant() switch
                    {
                        "dig" => $"dig out {e.X0},{e.Y0} .. {e.X1},{e.Y1}",
                        "room" => $"build {e.Room} in {e.X0},{e.Y0} .. {e.X1},{e.Y1}",
                        "gold" => $"gold seam in {e.X0},{e.Y0} .. {e.X1},{e.Y1}",
                        "wall" => $"edge rock in {e.X0},{e.Y0} .. {e.X1},{e.Y1}",
                        "rock" => $"fill with tier {e.Tier} rock {e.X0},{e.Y0} .. {e.X1},{e.Y1}",
                        "remove" => $"remove objects in {e.X0},{e.Y0} .. {e.X1},{e.Y1}",
                        "place" => $"place {e.Item} at {e.X0},{e.Y0} facing {e.Facing}",
                        _ => $"rock in {e.X0},{e.Y0} .. {e.X1},{e.Y1} -> tier {e.Tier}",
                    }));
                _changeActions.Add((() => Warn("Lair map changes are edited in the mod's file for now."), () => m.MapEdits.Remove(e)));
            }
            foreach (var e in m.SceneryEdits)
            {
                rows.Add(new ChangeRow("Island scenery", $"{Path.GetFileNameWithoutExtension(e.File)}: {e.GroupName ?? $"group {e.Group}"}",
                    $"{e.Action}{(e.Action == "move" ? $" by {e.DX},{e.DY},{e.DZ}" : "")}{(e.X0 is null ? " (whole group)" : $" in {e.X0:0},{e.Z0:0} .. {e.X1:0},{e.Z1:0}")}"));
                _changeActions.Add((() => Warn("Island scenery is edited on the Island tab."), () => m.SceneryEdits.Remove(e)));
            }
            foreach (var e in m.ShapeEdits)
            {
                var parts = new List<string>();
                if (e.Width is not null || e.Height is not null) parts.Add($"footprint {e.Width?.ToString() ?? "same"} x {e.Height?.ToString() ?? "same"}");
                if (e.Slots is { } sl) parts.Add($"{sl} slots");
                rows.Add(new ChangeRow("Furniture size", e.Note ?? e.Name, string.Join(", ", parts)));
                _changeActions.Add((() => { if (_game?.Objects.FirstOrDefault(x => x.IsRecord && x.RecordName!.Equals(e.Name, StringComparison.OrdinalIgnoreCase)) is { } f) { _tabs.SelectedTab = _browsePage; Show(f); } }, () => m.ShapeEdits.Remove(e)));
            }
            foreach (var e in m.JobEdits)
            {
                rows.Add(new ChangeRow("Who can do a job", RefName(e.Object), e.Note ?? string.Join(", ", e.Types)));
                _changeActions.Add((() => OpenInBrowse(e.Object), () => m.JobEdits.Remove(e)));
            }
            foreach (var e in m.ListEdits)
            {
                rows.Add(new ChangeRow("List", e.Tag == "fntr" ? e.Object : RefName(e.Object), e.Note ?? $"list @{e.At}: {e.Count} -> {e.Order.Count} entries"));
                _changeActions.Add((() =>
                {
                    var o = _game?.Objects.FirstOrDefault(x => x.Tag == e.Tag && x.Package.Equals(e.Package, StringComparison.OrdinalIgnoreCase) && x.Key.Equals(e.Object, StringComparison.OrdinalIgnoreCase));
                    if (o is not null) { _tabs.SelectedTab = _browsePage; Show(o); }
                }, () => m.ListEdits.Remove(e)));
            }
            foreach (var e in m.GraphEdits)
            {
                // removing one from the middle can break later ones (they may use the ids it made); Apply reports that
                rows.Add(new ChangeRow("Script structure", RefName(e.Object), $"{(string.IsNullOrEmpty(e.Graph) ? "" : $"\"{e.Graph}\": ")}{e.Note ?? e.Op}"));
                _changeActions.Add((() => OpenInBrowse(e.Object), () => m.GraphEdits.Remove(e)));
            }
        }
        _changesGrid.DataSource = rows;
        if (_changesGrid.Columns["Details"] is { } d) d.FillWeight = 300;
    }

    void OpenTreeById(string id)
    {
        if (_game is null) return;
        _tabs.SelectedTab = _treesPage;
        var t = _treeList.Items.Cast<GameObject>().FirstOrDefault(o => Bytes.Hex(o.ObjectId).Equals(id, StringComparison.OrdinalIgnoreCase));
        if (t is not null) _treeList.SelectedItem = t;
    }

    void OpenRequirements(ModDefinition m, string objRef)
    {
        if (_game is null) return;
        GameObject? src;
        if (objRef.StartsWith('@'))
        {
            var d = m.NewObjects.FirstOrDefault(o => o.Key.Equals(objRef[1..], StringComparison.OrdinalIgnoreCase));
            src = d is null ? null : _game.Objects.FirstOrDefault(x => x.Tag == d.Tag && !x.IsRecord && Bytes.Hex(x.ObjectId).Equals(d.Source, StringComparison.OrdinalIgnoreCase));
        }
        else src = _game.Objects.FirstOrDefault(x => Requirements.Supports(x.Tag) && !x.IsRecord && Bytes.Hex(x.ObjectId).Equals(objRef, StringComparison.OrdinalIgnoreCase));
        if (src is null) { Warn($"{objRef} isn't in the game data (or this mod) any more."); return; }
        ShowRequirements(objRef, src, objRef.StartsWith('@') ? m : null);
        FillChanges();
    }

    void OpenInBrowse(string objRef)
    {
        if (objRef.StartsWith('@')) { Warn($"{objRef} is one of this mod's new objects: open it from Browse game data → New objects."); return; }
        if (_game?.Objects.FirstOrDefault(x => !x.IsRecord && Bytes.Hex(x.ObjectId).Equals(objRef, StringComparison.OrdinalIgnoreCase)) is { } o) { _tabs.SelectedTab = _browsePage; Show(o); }
    }
}
