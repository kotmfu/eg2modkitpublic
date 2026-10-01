global using Eg2.Ui;
using System.Runtime.InteropServices;

namespace Eg2.Ui;

/// <summary>
/// Evil Genius 2 look: dark navy lair panels, gold trim, cyan highlights (on by default; Settings → "Classic Windows
/// look" turns it off). Colours used in code go through the properties below so both looks stay readable.
/// Shared with the player installer (linked source file).
/// </summary>
static class Theme
{
    public static bool Eg2 { get; set; } = true;

    static Color Pick(Color eg2, Color classic) => Eg2 ? eg2 : classic;
    public static Color Back => Pick(Color.FromArgb(14, 22, 36), SystemColors.Control);
    public static Color Panel => Pick(Color.FromArgb(22, 34, 52), SystemColors.Control);
    public static Color Input => Pick(Color.FromArgb(9, 15, 26), SystemColors.Window);
    public static Color Text => Pick(Color.FromArgb(232, 228, 214), SystemColors.ControlText);
    public static Color Muted => Pick(Color.FromArgb(138, 152, 170), SystemColors.GrayText);
    public static Color Gold => Pick(Color.FromArgb(242, 178, 52), SystemColors.ControlDark);
    public static Color Cyan => Pick(Color.FromArgb(64, 208, 214), SystemColors.Highlight);
    public static Color Selection => Pick(Color.FromArgb(28, 92, 110), SystemColors.Highlight);
    public static Color SelectionText => Pick(Color.White, SystemColors.HighlightText);
    public static Color Lines => Pick(Color.FromArgb(44, 60, 82), SystemColors.ControlLight);
    public static Color Good => Pick(Color.FromArgb(110, 214, 120), Color.DarkGreen);
    public static Color Bad => Pick(Color.FromArgb(255, 112, 96), Color.DarkRed);
    public static Color Warn => Pick(Color.FromArgb(255, 170, 60), Color.DarkOrange);
    public static Color Link => Pick(Color.FromArgb(110, 190, 255), Color.RoyalBlue);
    public static Color Highlight => Pick(Color.FromArgb(56, 76, 44), Color.FromArgb(232, 244, 232));   // e.g. tree roots

    /// <summary>The exe's own icon (app.ico), for every window.</summary>
    static readonly Icon? AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

    static readonly Font? Heading = FontOrNull("Bahnschrift SemiBold", 9.75f);

    static Font? FontOrNull(string family, float size)
    {
        using var f = new Font(family, size);
        return f.Name == family ? new Font(family, size) : null;
    }

    /// <summary>Style a form and everything in it, now and whatever gets added later.</summary>
    public static void Apply(Control root)
    {
        if (root is Form { ShowIcon: true } form) form.Icon = AppIcon;
        if (!Eg2) return;
        Style(root);
        if (root is Form f) DarkTitleBar(f);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, object> Primaries = new();

    /// <summary>The page's main action (Apply / Install mods): bigger and bold, filled gold in the EG2 look.</summary>
    public static Button Primary(Button b)
    {
        b.Font = new Font(b.Font.FontFamily, 11f, FontStyle.Bold);
        Primaries.AddOrUpdate(b, b);
        return b;
    }

    static void Style(Control c)
    {
        switch (c)
        {
            case Form f: f.BackColor = Back; f.ForeColor = Text; break;
            case Button b when Primaries.TryGetValue(b, out _):
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Gold; b.ForeColor = Back;
                b.FlatAppearance.BorderColor = Gold;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 204, 96);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(214, 150, 30);
                break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Panel; b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Gold;
                b.FlatAppearance.MouseOverBackColor = Selection;
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 120, 140);
                break;
            case CheckBox or RadioButton: c.ForeColor = Text; c.BackColor = Color.Transparent; break;
            case TextBoxBase or ListBox: c.BackColor = Input; c.ForeColor = Text; DarkScrollbars(c); Border(c); break;
            case UpDownBase ud: ud.BackColor = Input; ud.ForeColor = Text; DarkScrollbars(ud); UpDownBorder(ud); break;
            case ComboBox cb: cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Input; cb.ForeColor = Text; break;
            case ListView lv: List(lv); Border(lv); break;
            case TreeView tv: tv.BackColor = Input; tv.ForeColor = Text; tv.LineColor = Muted; DarkScrollbars(tv); Border(tv); break;
            case DataGridView g: Grid(g); break;
            case PropertyGrid pg:
                pg.BackColor = Back; pg.ViewBackColor = Input; pg.ViewForeColor = Text; pg.LineColor = Panel;
                pg.CategoryForeColor = Gold; pg.HelpBackColor = Panel; pg.HelpForeColor = Text; pg.ViewBorderColor = Lines;
                pg.CategorySplitterColor = Lines; pg.CommandsBackColor = Panel; pg.CommandsForeColor = Text;
                break;
            case TabControl t: Tabs(t); break;
            case TabPage p: p.BackColor = Back; p.ForeColor = Text; break;
            case GroupBox gb: gb.ForeColor = Gold; gb.BackColor = Back; break;
            case StatusStrip or ToolStrip or MenuStrip:
                var s = (ToolStrip)c; s.BackColor = Panel; s.ForeColor = Text; s.RenderMode = ToolStripRenderMode.Professional;
                foreach (var item in s.Items.OfType<ToolStripLabel>()) { item.LinkColor = Link; item.ActiveLinkColor = Cyan; item.VisitedLinkColor = Link; }
                break;
            case LinkLabel ll: ll.LinkColor = Link; ll.ActiveLinkColor = Cyan; ll.VisitedLinkColor = Link; ll.ForeColor = Text; break;
            case Label l:
                // keep deliberate colours (status, hints) mapped; plain labels get the text colour
                if (l.ForeColor == SystemColors.GrayText) l.ForeColor = Muted;
                else if (l.ForeColor == SystemColors.ControlText) l.ForeColor = Text;
                if (l.Font.Bold && Heading is not null) l.Font = new Font(Heading.FontFamily, l.Font.Size, FontStyle.Regular);
                break;
            case SplitContainer sc: sc.BackColor = Lines; break;
            case System.Windows.Forms.Panel or UserControl:
                if (c.BackColor == SystemColors.Control || c.BackColor == SystemColors.Window || c.BackColor == Color.Empty) c.BackColor = Back;
                c.ForeColor = Text;
                if (c is ScrollableControl { AutoScroll: true }) DarkScrollbars(c);
                break;
        }
        foreach (Control child in c.Controls) Style(child);
        c.ControlAdded -= OnAdded;
        c.ControlAdded += OnAdded;
    }

    static void OnAdded(object? sender, ControlEventArgs e) { if (Eg2 && e.Control is not null) Style(e.Control); }

    static void Grid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BackgroundColor = Input; g.GridColor = Lines; g.BorderStyle = BorderStyle.None;
        g.DefaultCellStyle.BackColor = Input; g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = Selection; g.DefaultCellStyle.SelectionForeColor = SelectionText;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(14, 22, 36);
        g.ColumnHeadersDefaultCellStyle.BackColor = Panel; g.ColumnHeadersDefaultCellStyle.ForeColor = Gold;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.RowHeadersDefaultCellStyle.BackColor = Panel; g.RowHeadersDefaultCellStyle.ForeColor = Text;
        if (Heading is not null) g.ColumnHeadersDefaultCellStyle.Font = Heading;
        DarkScrollbars(g);
    }

    static void List(ListView lv)
    {
        lv.BackColor = Input; lv.ForeColor = Text;
        DarkScrollbars(lv);
        if (lv.View != View.Details || lv.OwnerDraw) return;
        // the last column takes the spare width: the header past the last column is native and would stay light
        void FillLast()
        {
            if (lv.Columns.Count == 0) return;
            int rest = lv.ClientSize.Width - lv.Columns.Cast<ColumnHeader>().SkipLast(1).Sum(h => h.Width);
            var last = lv.Columns[^1];
            if (rest > 40 && last.Width != rest) last.Width = rest;
        }
        lv.ClientSizeChanged += (_, _) => FillLast();
        lv.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex < lv.Columns.Count - 1) FillLast(); };
        lv.OwnerDraw = true;   // only to paint the column headers dark; rows draw as usual
        lv.DrawItem += (_, e) => e.DrawDefault = true;
        lv.DrawSubItem += (_, e) => e.DrawDefault = true;
        lv.DrawColumnHeader += (_, e) =>
        {
            // a scrollbar appearing doesn't resize the list, but the header repaints: refit from here
            if (e.ColumnIndex == lv.Columns.Count - 1 && lv.IsHandleCreated) lv.BeginInvoke(FillLast);
            using (var fill = new SolidBrush(Panel)) e.Graphics.FillRectangle(fill, e.Bounds);
            using (var line = new Pen(Lines)) e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
            var align = e.Header?.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, Heading ?? lv.Font, Rectangle.Inflate(e.Bounds, -4, 0), Gold,
                                  align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    static readonly HashSet<TabControl> Painted = new();

    static void Tabs(TabControl t)
    {
        if (!Painted.Add(t)) return;
        t.Padding = new Point(14, 5);
        t.BackColor = Back;
        var painter = new TabPainter(t);
        if (t.IsHandleCreated) painter.AssignHandle(t.Handle);
        t.HandleCreated += (_, _) => painter.AssignHandle(t.Handle);
        t.HandleDestroyed += (_, _) => painter.ReleaseHandle();
        t.SelectedIndexChanged += (_, _) => t.Invalidate();
    }

    /// <summary>
    /// Paints a tab control's header strip and page frame after Windows draws them (the native ones are light and ignore
    /// BackColor). Tabs stay native, so keyboard/UI Automation behave as before.
    /// </summary>
    sealed class TabPainter : NativeWindow
    {
        readonly TabControl _t;
        public TabPainter(TabControl t) => _t = t;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F || _t.TabCount == 0 || _t.Alignment != TabAlignment.Top) return;   // WM_PAINT
            using var g = Graphics.FromHwnd(_t.Handle);
            int stripBottom = Enumerable.Range(0, _t.TabCount).Max(i => _t.GetTabRect(i).Bottom) + 2;
            using (var back = new SolidBrush(Back)) g.FillRectangle(back, 0, 0, _t.Width, stripBottom);
            // page frame: cover the native light border around the display area
            var page = _t.DisplayRectangle;
            using (var frame = new Pen(Back, 8)) g.DrawRectangle(frame, Rectangle.Inflate(page, 4, 4));
            if (_t.ItemSize.Height <= 2) return;   // strip hidden (sidebar navigation): nothing more to draw
            using (var line = new Pen(Lines)) g.DrawLine(line, 0, stripBottom - 1, _t.Width, stripBottom - 1);
            for (int i = 0; i < _t.TabCount; i++)
            {
                var r = _t.GetTabRect(i);
                bool sel = i == _t.SelectedIndex;
                if (sel) { r.Inflate(0, 2); r.Offset(0, -1); }
                using (var fill = new SolidBrush(sel ? Panel : Back)) g.FillRectangle(fill, r);
                if (sel) using (var gold = new Pen(Gold, 3)) g.DrawLine(gold, r.Left + 2, r.Bottom - 2, r.Right - 3, r.Bottom - 2);
                TextRenderer.DrawText(g, _t.TabPages[i].Text, Heading ?? _t.Font, r, sel ? Gold : Text,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    static readonly HashSet<Control> Bordered = new();

    /// <summary>Sunken (3D) borders are drawn light by Windows; repainted as a flat line, cyan while focused.</summary>
    static void Border(Control c)
    {
        bool sunken = c switch
        {
            TextBoxBase t => t.BorderStyle == BorderStyle.Fixed3D, ListBox l => l.BorderStyle == BorderStyle.Fixed3D,
            ListView l => l.BorderStyle == BorderStyle.Fixed3D, TreeView t => t.BorderStyle == BorderStyle.Fixed3D, _ => false,
        };
        if (!sunken || !Bordered.Add(c)) return;
        var painter = new BorderPainter(c);
        if (c.IsHandleCreated) painter.AssignHandle(c.Handle);
        c.HandleCreated += (_, _) => painter.AssignHandle(c.Handle);
        c.HandleDestroyed += (_, _) => painter.ReleaseHandle();
        c.Disposed += (_, _) => Bordered.Remove(c);
    }

    static void DrawEdge(Graphics g, Control c, Rectangle r)
    {
        using (var outer = new Pen(c.ContainsFocus ? Cyan : Lines)) g.DrawRectangle(outer, r.X, r.Y, r.Width - 1, r.Height - 1);
        using (var inner = new Pen(c.BackColor)) g.DrawRectangle(inner, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
    }

    sealed class BorderPainter : NativeWindow
    {
        readonly Control _c;
        public BorderPainter(Control c) => _c = c;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WM_NCPAINT, WM_SETFOCUS, WM_KILLFOCUS, WM_ENABLE, WM_MOUSEMOVE, WM_MOUSELEAVE (themed borders hot-track)
            if (m.Msg is not (0x0085 or 0x0007 or 0x0008 or 0x000A or 0x0200 or 0x02A3)) return;
            IntPtr dc = GetWindowDC(Handle);
            if (dc == IntPtr.Zero) return;
            try { using var g = Graphics.FromHdc(dc); DrawEdge(g, _c, new Rectangle(Point.Empty, _c.Size)); }
            finally { ReleaseDC(Handle, dc); }
        }
    }

    /// <summary>Number boxes paint their own (light) border: drawn over, and redrawn as focus comes and goes.</summary>
    static void UpDownBorder(UpDownBase ud)
    {
        if (ud.BorderStyle != BorderStyle.Fixed3D || !Bordered.Add(ud)) return;
        ud.Paint += (_, e) => DrawEdge(e.Graphics, ud, ud.ClientRectangle);
        ud.Enter += (_, _) => ud.Invalidate();
        ud.Leave += (_, _) => ud.Invalidate();
        ud.Disposed += (_, _) => Bordered.Remove(ud);
    }

    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string? idList);

    static void DarkTitleBar(Form f)
    {
        void Set() { int on = 1; try { DwmSetWindowAttribute(f.Handle, 20, ref on, 4); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } }
        if (f.IsHandleCreated) Set(); else f.HandleCreated += (_, _) => Set();
    }

    static void DarkScrollbars(Control c)
    {
        void Set() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } }
        if (c.IsHandleCreated) Set(); else c.HandleCreated += (_, _) => Set();
    }
}

/// <summary>A dialog that picks up the current look when it opens.</summary>
class ThemedForm : Form
{
    protected override void OnLoad(EventArgs e)
    {
        Theme.Apply(this);
        base.OnLoad(e);
    }
}
