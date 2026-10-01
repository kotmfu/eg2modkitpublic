using Eg2.Asura;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Scripts → "Nodes and links…": remove or copy steps, add or remove arrows (saved as GraphEdits).</summary>
sealed partial class MainForm
{
    void EditGraphStructure(GameObject o, FlowGraph original)
    {
        LearnPins();
        var names = FgNames();
        string graphName = original.Name;
        bool onlyGraph = FlowGraph.Find(o.Body).Count(g => g.Name == graphName) == 1;
        string? graphRef = onlyGraph ? graphName : null;
        if (!onlyGraph && FlowGraph.Find(o.Body).Count != 1) { Warn("Several scripts in this object share that name, so ModKit can't tell them apart."); return; }
        if (FlowGraphEdit.Problem(o.Body, graphRef) is { } why) { Warn($"Can't restructure \"{graphName}\": {why}."); return; }
        var owner = o.DraftMod ?? TargetMod();
        if (owner is null) return;
        string objRef = SwapRef(o);

        List<GraphEdit> Mine() => owner.GraphEdits.Where(e => e.Object.Equals(objRef, StringComparison.OrdinalIgnoreCase) && (e.Graph ?? "") == (graphRef ?? "")).ToList();
        byte[] Preview(IEnumerable<GraphEdit> ops)
        {
            var b = o.Body;
            foreach (var e in ops)
                b = e.Op switch
                {
                    "remove-node" => FlowGraphEdit.RemoveNode(b, graphRef, e.Node!.Value),
                    "copy-node" => FlowGraphEdit.CopyNode(b, graphRef, e.Node!.Value).Body,
                    "add-link" => FlowGraphEdit.AddLink(b, graphRef, e.From!.Value, e.FromPin!.Value, e.To!.Value, e.ToPin!.Value),
                    "remove-link" => FlowGraphEdit.RemoveLink(b, graphRef, e.Link!.Value),
                    _ => b,
                };
            return b;
        }

        using var dlg = new ThemedForm { Text = $"Nodes and links of \"{graphName}\" in {o.Name}", Width = 1100, Height = 640, StartPosition = FormStartPosition.CenterParent };
        var nodes = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var links = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        var status = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Muted };
        FlowGraph gr = original;
        byte[] body = o.Body;
        string Node(uint id) => id == 0 ? "#0 Start" : id == 1 ? "#1 End" : $"#{id} {names.Type(gr.Nodes.FirstOrDefault(n => n.Id == id)?.Type ?? 0)}";

        void Refresh()
        {
            try { body = Preview(Mine()); }
            catch (InvalidOperationException e) { Warn($"The saved changes no longer apply: {e.Message}"); body = o.Body; }
            gr = FlowGraph.Find(body).First(g => g.Name == graphName);
            nodes.Items.Clear();
            foreach (var n in gr.Nodes) nodes.Items.Add(new Choice(n.Id.ToString(), $"{Node(n.Id)}   ({gr.Links.Count(l => l.ToNode == n.Id)} in, {gr.Links.Count(l => l.FromNode == n.Id)} out)"));
            links.Items.Clear();
            foreach (var l in gr.Links)
                links.Items.Add(new Choice(l.Id.ToString(), $"{Node(l.FromNode)}.{names.Pin(l.FromPin, true)}  →  {Node(l.ToNode)}.{names.Pin(l.ToPin, false)}"));
            int count = Mine().Count;
            status.Text = count == 0 ? "No changes to this script yet." : $"{count} change{(count == 1 ? "" : "s")} to this script in \"{owner.Name}\".";
        }

        void Save(GraphEdit e)
        {
            try { Preview(Mine().Append(e)); }
            catch (InvalidOperationException ex) { Warn($"Can't do that: {ex.Message}"); return; }
            if (ReferenceEquals(owner, _current)) CommitEditor();
            owner.GraphEdits.Add(e);
            owner.Save();
            if (ReferenceEquals(owner, _current)) ShowEditor();
            Log($"{owner.Id}: {o.Name} script \"{graphName}\": {e.Note}");
            Refresh();
        }
        GraphEdit New(string op, string note, Action<GraphEdit> set) { var e = new GraphEdit { Object = objRef, Graph = graphRef, Op = op, Note = note }; set(e); return e; }
        uint? Sel(ListBox l) => l.SelectedItem is Choice c ? uint.Parse(c.Ref) : null;

        var removeNode = Btn("Remove node", (_, _) =>
        {
            if (Sel(nodes) is not { } id) return;
            if (id is 0 or 1) { Warn("The start and end of a script stay."); return; }
            Save(New("remove-node", $"remove {Node(id)} and its arrows", x => { x.Node = id; }));
        });
        var copyNode = Btn("Copy node", (_, _) =>
        {
            if (Sel(nodes) is not { } id) return;
            Save(New("copy-node", $"copy {Node(id)} (with its settings and outgoing arrows)", x => { x.Node = id; }));
        });
        var removeLink = Btn("Remove arrow", (_, _) =>
        {
            if (Sel(links) is not { } id || gr.Links.FirstOrDefault(l => l.Id == id) is not { } l) return;
            Save(New("remove-link", $"remove arrow {Node(l.FromNode)} → {Node(l.ToNode)}", x => { x.Link = id; }));
        });
        var addLink = Btn("Add arrow…", (_, _) => { if (PickLink(gr, names, Node) is { } e) Save(New("add-link", $"add arrow {Node(e.From)} → {Node(e.To)}", x => { x.From = e.From; x.FromPin = e.FromPin; x.To = e.To; x.ToPin = e.ToPin; })); });
        var undo = Btn("Undo last change", (_, _) =>
        {
            var last = Mine().LastOrDefault();
            if (last is null) return;
            if (ReferenceEquals(owner, _current)) CommitEditor();
            owner.GraphEdits.Remove(last);
            owner.Save();
            if (ReferenceEquals(owner, _current)) ShowEditor();
            Refresh();
        });

        var split = new SplitContainer { Dock = DockStyle.Fill };
        dlg.Shown += (_, _) => split.SplitterDistance = split.Width * 2 / 5;   // only valid once the dialog has its size
        split.Panel1.Controls.Add(nodes); split.Panel1.Controls.Add(new Label { Text = "Steps (nodes)", Dock = DockStyle.Top, Height = 20 });
        split.Panel2.Controls.Add(links); split.Panel2.Controls.Add(new Label { Text = "Arrows (links)", Dock = DockStyle.Top, Height = 20 });
        dlg.Controls.Add(split);
        dlg.Controls.Add(status);
        dlg.Controls.Add(Hint("Remove a step (its arrows go too), copy a step (same settings; give it an incoming arrow, then change its numbers in the Scripts " +
                              "window's settings), add or remove arrows. Changes are saved to \"" + owner.Name + "\" as you go. New arrows can only leave an output that already has one. Test on a new save."));
        dlg.Controls.Add(Bar(removeNode, copyNode, addLink, removeLink, undo));
        Refresh();
        dlg.ShowDialog(this);
    }

    /// <summary>Choose a new arrow: from an output that already has an arrow, to any node's input.</summary>
    (uint From, uint FromPin, uint To, uint ToPin)? PickLink(FlowGraph gr, FlowGraph.Names names, Func<uint, string> node)
    {
        var outs = gr.Links.Select(l => (l.FromNode, l.FromPin)).Distinct()
            .Select(x => new Choice($"{x.FromNode}:{x.FromPin}", $"{node(x.FromNode)}.{names.Pin(x.FromPin, true)}")).ToList();
        var targets = gr.Nodes.Where(n => n.Id != 0).Select(n => new Choice(n.Id.ToString(), node(n.Id))).ToList();
        using var dlg = new ThemedForm { Text = "Add arrow", Width = 560, Height = 240, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var from = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, DataSource = outs, DisplayMember = "Label", ValueMember = "Ref" };
        var to = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, DataSource = targets, DisplayMember = "Label", ValueMember = "Ref" };
        var pin = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, DisplayMember = "Label", ValueMember = "Ref" };
        void Pins()
        {
            if (to.SelectedValue is not string v) return;
            uint t = gr.Nodes.First(n => n.Id == uint.Parse(v)).Type;
            var set = _fgInPins!.TryGetValue(t, out var s) ? s : new HashSet<uint>();
            pin.DataSource = set.Select(p => new Choice(p.ToString(), $"{names.Pin(p, false)} ({Bytes.Hex(p)})")).OrderBy(c => c.Label).ToList();
        }
        to.SelectedIndexChanged += (_, _) => Pins();
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        table.Controls.Add(Lbl("From output"), 0, 0); table.Controls.Add(from, 1, 0);
        table.Controls.Add(Lbl("To step"), 0, 1); table.Controls.Add(to, 1, 1);
        table.Controls.Add(Lbl("Its input"), 0, 2); table.Controls.Add(pin, 1, 2);
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, AutoSize = true };
        dlg.Controls.Add(table);
        dlg.Controls.Add(Bar(ok));
        dlg.Shown += (_, _) => Pins();
        if (dlg.ShowDialog(this) != DialogResult.OK || from.SelectedValue is not string f || to.SelectedValue is not string tv || pin.SelectedValue is not string pv) return null;
        var fp = f.Split(':');
        return (uint.Parse(fp[0]), uint.Parse(fp[1]), uint.Parse(tv), uint.Parse(pv));
    }
}
