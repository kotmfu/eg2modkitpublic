using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Assets → "Recolour…": move one colour range of the selected textures to another, keeping their shading (the Cold War
/// uniforms were made this way). Previewed on the first texture; the results are encoded like the originals (format,
/// mips) and stored as replacements.
/// </summary>
sealed partial class MainForm
{
    async void RecolourAssets()
    {
        if (_game is null) return;
        var sel = Selected().Where(a => a.Kind == "Texture").ToList();
        if (sel.Count == 0) { Warn("Select one or more textures (tip: search for a name, then select all its copies and skin variants)."); return; }
        var first = AssetIndex.Read(_game.Install, new[] { sel[0] }).Single().Data;
        if (Dds.Decode(first, out int w, out int h) is not { } full) { Warn($"ModKit can't read {Path.GetFileName(sel[0].Name)} ({Dds.FormatOf(first)})."); return; }
        var small = Shrink(full, w, h, 400, out int sw, out int sh);
        for (int i = 3; i < small.Length; i += 4) small[i] = 255;   // preview only: model albedo alpha is a mask, not transparency

        using var dlg = new ThemedForm
        {
            Text = $"Recolour {(sel.Count == 1 ? Path.GetFileName(sel[0].Name) : $"{sel.Count} textures")}", Width = 940, Height = 640,
            StartPosition = FormStartPosition.CenterParent,
        };
        var before = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Image = RgbaBitmap(small, sw, sh), Cursor = Cursors.Cross };
        var after = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
        NumericUpDown Num(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 70 };
        var hue = Num(0, 359, 30); var range = Num(1, 180, 20); var minSat = Num(0, 100, 30);
        var toHue = Num(0, 359, 0); var keepHue = new CheckBox { Text = "keep hue", AutoSize = true };
        var sat = Num(0, 300, 100); var val = Num(0, 300, 100);
        var fromSwatch = new Panel { Width = 24, Height = 20 }; var toSwatch = new Panel { Width = 24, Height = 20 };
        Color Swatch(float hh) { var (r, g, b) = Recolour.FromHsv(hh, 0.8f, 0.9f); return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255)); }
        HueRule Rule() => new((float)hue.Value, (float)range.Value, (float)minSat.Value / 100, keepHue.Checked ? null : (float)toHue.Value, (float)sat.Value / 100, (float)val.Value / 100);
        void Update()
        {
            var copy = (byte[])small.Clone();
            Recolour.Apply(copy, new[] { Rule() });
            after.Image?.Dispose();
            after.Image = RgbaBitmap(copy, sw, sh);
            fromSwatch.BackColor = Swatch((float)hue.Value);
            toSwatch.BackColor = keepHue.Checked ? fromSwatch.BackColor : Swatch((float)toHue.Value);
            toHue.Enabled = !keepHue.Checked;
        }
        foreach (var n in new[] { hue, range, minSat, toHue, sat, val }) n.ValueChanged += (_, _) => Update();
        keepHue.CheckedChanged += (_, _) => Update();
        // click the picture to pick the colour to change
        before.MouseClick += (_, e) =>
        {
            float scale = Math.Min((float)before.Width / sw, (float)before.Height / sh);
            int x = (int)((e.X - (before.Width - sw * scale) / 2) / scale), y = (int)((e.Y - (before.Height - sh * scale) / 2) / scale);
            if (x < 0 || y < 0 || x >= sw || y >= sh) return;
            var (hh, ss, _) = Recolour.ToHsv(small[(y * sw + x) * 4], small[(y * sw + x) * 4 + 1], small[(y * sw + x) * 4 + 2]);
            hue.Value = (int)hh % 360;
            minSat.Value = Math.Clamp((int)(ss * 100) - 15, 0, 100);
        };

        var controls = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 6, Padding = new Padding(8) };
        void Row(string label, Control? a, Control? swatchA, string label2, Control b, Control? swatchB)
        {
            foreach (var c in new[] { Lbl(label), a ?? new Label(), swatchA ?? new Label(), Lbl(label2), b, swatchB ?? new Label() }) controls.Controls.Add(c);
        }
        Row("Change colours near hue", hue, fromSwatch, "to hue", toHue, toSwatch);
        Row("within ± degrees", range, null, "", keepHue, null);
        Row("with saturation at least %", minSat, null, "saturation %", sat, null);
        Row("", null, null, "brightness %", val, null);
        var split = new SplitContainer { Dock = DockStyle.Fill };
        dlg.Shown += (_, _) => split.SplitterDistance = split.Width / 2;
        split.Panel1.Controls.Add(before); split.Panel1.Controls.Add(new Label { Text = "Before (click to pick a colour)", Dock = DockStyle.Top, Height = 20 });
        split.Panel2.Controls.Add(after); split.Panel2.Controls.Add(new Label { Text = "After", Dock = DockStyle.Top, Height = 20 });
        var ok = new Button { Text = sel.Count == 1 ? "Recolour" : $"Recolour all {sel.Count}", DialogResult = DialogResult.OK, AutoSize = true };
        dlg.Controls.Add(split);
        dlg.Controls.Add(controls);
        dlg.Controls.Add(Bar(ok));
        dlg.Controls.Add(Hint("Colours within the hue range move to the new hue; brightness and shading stay, and the edges blend. " +
                              "Saturation/brightness scale the matched colours (e.g. saturation 10% turns them grey). The result is written in the game's own texture format."));
        Update();
        if (dlg.ShowDialog(this) != DialogResult.OK || TargetMod() is not { } mod) return;

        var rule = Rule();
        var done = new List<(AssetEntry A, byte[] Dds)>();
        var game = _game;
        if (!await Run($"Recolouring {sel.Count} texture{(sel.Count == 1 ? "" : "s")}", (log, ct) =>
            {
                foreach (var (a, orig) in AssetIndex.Read(game.Install, sel, ct))
                {
                    if (Dds.Decode(orig, out int tw, out int th) is not { } px) { log($"skipped {a.Name}: {Dds.FormatOf(orig)} can't be read"); continue; }
                    Recolour.Apply(px, new[] { rule });
                    try { done.Add((a, Dds.EncodeLike(orig, px, tw, th))); }
                    catch (NotSupportedException e) { log($"skipped {a.Name}: {e.Message}"); }
                    log($"recoloured {a.Name}");
                }
            })) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        string how = $"hue {rule.Hue}±{rule.Half} -> {(rule.ToHue is { } t ? $"hue {t}" : "same hue")}, saturation x{rule.SatMul:0.##}, brightness x{rule.ValMul:0.##}";
        foreach (var (a, dds) in done) StoreReplacement(mod, a, dds, $"Texture: {Path.GetFileName(a.Name)} recoloured ({how})");
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        _status.Text = $"{done.Count} texture{(done.Count == 1 ? "" : "s")} recoloured into \"{mod.Name}\".";
        FilterAssets();
    }

    /// <summary>Nearest-neighbour copy no larger than <paramref name="max"/> on either side (for previews).</summary>
    static byte[] Shrink(byte[] rgba, int w, int h, int max, out int nw, out int nh)
    {
        float k = Math.Min(1, (float)max / Math.Max(w, h));
        nw = Math.Max(1, (int)(w * k)); nh = Math.Max(1, (int)(h * k));
        var d = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++) Array.Copy(rgba, ((y * h / nh) * w + x * w / nw) * 4, d, (y * nw + x) * 4, 4);
        return d;
    }

    static Bitmap RgbaBitmap(byte[] rgba, int w, int h)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bgra = (byte[])rgba.Clone();
        for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, d.Scan0, bgra.Length);
        bmp.UnlockBits(d);
        return bmp;
    }
}
