namespace Eg2.ModManager;

/// <summary>
/// While <see cref="MainForm.Run"/> works: the window is covered by a dimmed snapshot of itself (in the theme's colours,
/// instead of Windows' grey disabled controls) with a card saying what is happening: the job, the current step, the
/// last few steps, how many so far and the elapsed time.
/// </summary>
sealed partial class MainForm
{
    Panel? _busy;
    Label _busyStep = null!, _busyRecent = null!, _busyTime = null!;
    Panel _busyBar = null!;
    double? _busyDone;      // 0..1 once a step says "n/total"; null = still unknown (a gold block sweeps instead)
    int _busySweep;
    readonly Queue<string> _busyLines = new();
    int _busySteps;
    System.Windows.Forms.Timer? _busyTimer;

    void ShowBusy(string what)
    {
        var host = _split.Panel1;
        var shot = new Bitmap(Math.Max(1, host.Width), Math.Max(1, host.Height));
        host.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
        using (var g = Graphics.FromImage(shot))
        using (var dim = new SolidBrush(Color.FromArgb(185, Theme.Back)))
            g.FillRectangle(dim, 0, 0, shot.Width, shot.Height);

        // not docked: docking would put it beside the sidebar, and the snapshot (sidebar included) would show a second one
        _busy = new Panel
        {
            Bounds = host.ClientRectangle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackgroundImage = shot, BackgroundImageLayout = ImageLayout.None,
        };
        var card = new Panel { Size = new Size(560, 250), BackColor = Theme.Panel, Padding = new Padding(18, 14, 18, 14) };
        card.Paint += (_, e) => { using var p = new Pen(Theme.Gold, 2); e.Graphics.DrawRectangle(p, 1, 1, card.Width - 3, card.Height - 3); };
        var title = new Label { Text = what, Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Gold, Font = new Font(Font.FontFamily, 14f, FontStyle.Bold) };
        _busyStep = new Label { Text = "Starting…", Dock = DockStyle.Top, Height = 40, ForeColor = Theme.Text, Font = new Font(Font.FontFamily, 10f), AutoEllipsis = true };
        var bar = _busyBar = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Input };
        bar.Paint += (_, e) =>
        {
            using var fill = new SolidBrush(Theme.Gold);
            if (_busyDone is { } d) e.Graphics.FillRectangle(fill, 0, 0, (int)(bar.Width * d), bar.Height);
            else { int w = bar.Width / 4, x = _busySweep % (bar.Width + w) - w; e.Graphics.FillRectangle(fill, x, 0, w, bar.Height); }
        };
        _busyRecent = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Consolas", 8.5f), AutoEllipsis = true, Padding = new Padding(0, 8, 0, 0) };
        _busyTime = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        var cancel = new LinkLabel { Text = "Cancel", AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, LinkColor = Theme.Link };
        cancel.LinkClicked += (_, _) => { _cts?.Cancel(); _busyStep.Text = "Cancelling…"; };
        card.Controls.Add(_busyRecent);
        card.Controls.Add(bar);
        card.Controls.Add(_busyStep);
        card.Controls.Add(title);
        card.Controls.Add(_busyTime);
        _busyTime.Controls.Add(cancel);
        _busyTime.Resize += (_, _) => cancel.Location = new Point(_busyTime.Width - cancel.Width, 4);
        _busy.Controls.Add(card);
        void Centre() => card.Location = new Point(Math.Max(0, (_busy.Width - card.Width) / 2), Math.Max(0, (_busy.Height - card.Height) / 2));
        _busy.Resize += (_, _) => Centre();
        host.Controls.Add(_busy);
        _busy.BringToFront();
        Centre();

        _busyLines.Clear();
        _busySteps = 0;
        var started = DateTime.Now;
        _busyDone = null;
        _busyTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _busyTimer.Tick += (_, _) =>
        {
            _busyTime.Text = $"{(DateTime.Now - started).TotalSeconds:0}s · {_busySteps:N0} step{(_busySteps == 1 ? "" : "s")}";
            _busySweep += 12;
            _busyBar.Invalidate();
        };
        _busyTimer.Start();
    }

    static readonly System.Text.RegularExpressions.Regex Count = new(@"\b(\d+)/(\d+)\b");

    /// <summary>A log line from the running job: becomes the current step and joins the recent list. A step saying
    /// "n/total" (e.g. "reading package 12/140") turns the bar into a real progress bar.</summary>
    void BusyStep(string line)
    {
        if (_busy is null || string.IsNullOrWhiteSpace(line)) return;
        _busySteps++;
        if (Count.Match(line) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int done) && int.TryParse(m.Groups[2].Value, out int total) && total > 0 && done <= total)
            _busyDone = (double)done / total;
        _busyStep.Text = line.Trim();
        _busyLines.Enqueue(line.Trim());
        while (_busyLines.Count > 6) _busyLines.Dequeue();
        _busyRecent.Text = string.Join(Environment.NewLine, _busyLines.Reverse().Skip(1));
    }

    void HideBusy()
    {
        _busyTimer?.Dispose();
        _busyTimer = null;
        if (_busy is null) return;
        var img = _busy.BackgroundImage;
        _split.Panel1.Controls.Remove(_busy);
        _busy.Dispose();
        img?.Dispose();
        _busy = null;
    }
}
