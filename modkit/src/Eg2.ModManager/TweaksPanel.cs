using Eg2.ModKit;

namespace Eg2.Ui;

/// <summary>
/// The Quick Tweaks rows (tick + number per <see cref="Tweak"/>), shared by ModManager's Quick Tweaks tab and the
/// player installer (linked into both), so the two always offer the same settings. Values live in a mod.
/// </summary>
sealed class TweaksPanel : FlowLayoutPanel
{
    readonly List<(Tweak Tweak, CheckBox On, NumericUpDown? Number)> _rows = new();

    public TweaksPanel()
    {
        Dock = DockStyle.Fill; FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; Padding = new Padding(12, 4, 12, 12);
    }

    public bool AnyTicked => _rows.Any(r => r.On.Checked);
    public bool HasRows => _rows.Count > 0;

    /// <summary>One row per tweak, ticked and filled in from <paramref name="mod"/> (null = nothing set yet).</summary>
    public void Show(IEnumerable<Tweak> tweaks, ModDefinition? mod, string? footer = null)
    {
        SuspendLayout();
        Controls.Clear();
        _rows.Clear();
        _hints.Clear();
        var bold = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        foreach (var group in tweaks.GroupBy(t => t.Group))
        {
            Controls.Add(new Label { Text = group.Key, Font = bold, AutoSize = true, Margin = new Padding(0, 14, 0, 4) });
            foreach (var t in group)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(12, 0, 0, 0) };
                // long names end in "…" and show in full on hover
                var on = new CheckBox { Text = t.Label, Width = 340, AutoEllipsis = true, Checked = mod is not null && t.IsIn(mod) };
                row.Controls.Add(on);
                NumericUpDown? number = null;
                if (!t.IsFlag)
                {
                    number = new NumericUpDown { Minimum = t.Min, Maximum = t.Max, Width = 120, ThousandsSeparator = true, Enabled = on.Checked,
                                                 DecimalPlaces = t.Decimals, Increment = t.Decimals > 0 ? 0.1m : 1 };
                    string start = (mod is null ? null : t.ValueIn(mod)) ?? t.Default ?? t.Runtime?.Value ?? "0";
                    number.Value = decimal.TryParse(start, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, t.Min, t.Max) : t.Min;
                    on.CheckedChanged += (_, _) => number.Enabled = on.Checked;
                    row.Controls.Add(number);
                }
                string hint = string.Join("  ·  ", new[] { t.Default is null ? null : $"game default: {t.Default}", t.Help }.Where(s => !string.IsNullOrEmpty(s)));
                var hintLabel = new Label { Text = hint, AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(8, 6, 0, 0) };
                row.Controls.Add(hintLabel);
                _hints.Add(hintLabel);
                Controls.Add(row);
                _rows.Add((t, on, number));
            }
        }
        if (footer is not null) Controls.Add(new Label { Text = footer, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 14, 0, 0) });
        ResumeLayout();
        WrapHints();
    }

    readonly List<Label> _hints = new();

    /// <summary>Hints wrap at the panel's right edge instead of running off it.</summary>
    void WrapHints()
    {
        int right = ClientSize.Width - Padding.Right - SystemInformation.VerticalScrollBarWidth;
        foreach (var h in _hints)
            if (h.Parent is { } row) h.MaximumSize = new Size(Math.Max(200, right - row.Left - h.Left), 0);
    }

    protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); WrapHints(); }

    /// <summary>Ticked rows set in <paramref name="mod"/>, unticked ones removed from it.</summary>
    public void SaveInto(ModDefinition mod)
    {
        foreach (var (t, on, number) in _rows)
        {
            if (on.Checked) t.Set(mod, t.IsFlag ? "1" : t.Decimals > 0 ? number!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ((long)number!.Value).ToString());
            else t.Remove(mod);
        }
    }
}
