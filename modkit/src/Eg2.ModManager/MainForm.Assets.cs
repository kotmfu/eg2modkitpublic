using System.Diagnostics;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// Assets tab: every sound, texture, animation and model the game ships. Extract them (WAV/DDS or raw
/// chunks), open one to hear/see it, or replace it with your own file in the selected mod.
/// </summary>
sealed partial class MainForm
{
    const int MaxAssetRows = 5000;
    readonly DataGridView _assetList = ReadOnlyGrid();
    readonly ComboBox _assetKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly TextBox _assetSearch = new() { Width = 300, PlaceholderText = "Search names, e.g. laugh or bunk" };
    readonly Label _assetTitle = new() { Dock = DockStyle.Top, Height = 48, Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f, FontStyle.Bold) };
    readonly Label _assetInfo = new() { Dock = DockStyle.Top, Height = 230 };
    readonly CheckBox _texturesAsPng = new() { Text = "Extract textures as PNG", Checked = true, AutoSize = true };
    readonly Label _assetCount = new() { Dock = DockStyle.Bottom, Height = 22, ForeColor = Theme.Muted };
    TabPage _assetsPage = null!;
    List<AssetEntry>? _assets;
    List<Skeleton>? _skeletons;
    HashSet<string> _replaced = new();
    string? _lastExtractDir;

    TabPage AssetsPage()
    {
        _assetKind.Items.AddRange(new object[] { "All kinds", "Sound", "Streamed sound", "Texture", "Mesh", "Animation", "Animation (FAAN)", "Model", "Skeleton", "File" });
        _assetKind.SelectedIndex = 0;
        _assetKind.SelectedIndexChanged += (_, _) => FilterAssets();
        Debounce(_assetSearch, FilterAssets);
        _assetList.MultiSelect = true;
        _assetList.SelectionChanged += (_, _) => ShowAsset();
        _assetList.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenAsset(); };
        _assetList.DataBindingComplete += (_, _) =>
        {
            foreach (var (col, w) in new[] { ("Kind", 12), ("Name", 60), ("File", 30), ("SizeKb", 8) })
                if (_assetList.Columns[col] is { } c) c.FillWeight = w;
        };
        _assetList.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _assetList.Rows[e.RowIndex].DataBoundItem is AssetEntry a && e.CellStyle is { } st)
            {
                if (_replaced.Contains(a.Key)) st.Font = new Font(_assetList.Font, FontStyle.Bold);
            }
        };

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        left.Controls.Add(_assetList);
        left.Controls.Add(_assetCount);
        left.Controls.Add(Bar(_assetKind, _assetSearch, _texturesAsPng));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var b in new[]
                 {
                     Btn("Open (listen / view)", (_, _) => OpenAsset()),
                     Btn("Extract selected…", async (_, _) => await ExtractAssets(Selected())),
                     Btn("Extract everything shown…", async (_, _) => await ExtractAssets(ShownAssets())),
                     Btn("Dump all game assets…", async (_, _) => await ExtractAssets(_assets ?? new())),
                     Btn("Replace with my file…", (_, _) => ReplaceAsset()),
                     Btn("Add as a new animation…", (_, _) => AddNewClip()),
                     Btn("Play another animation here…", (_, _) => SwapClip()),
                     Btn("Recolour textures…", (_, _) => RecolourAssets()),
                     Btn("Undo replacement", (_, _) => UndoReplacement()),
                 })
        {
            b.MinimumSize = new Size(220, 0);
            buttons.Controls.Add(b);
        }
        var right = new Panel { Dock = DockStyle.Right, Width = 380, Padding = new Padding(8, 4, 4, 4) };
        right.Controls.Add(buttons);
        right.Controls.Add(_assetInfo);
        right.Controls.Add(_assetTitle);

        var page = new TabPage("Assets");
        page.Controls.Add(left);
        page.Controls.Add(right);
        page.Controls.Add(Hint("Every sound, texture, mesh, animation and model in the game. Extract gives you WAV (sounds), PNG or DDS (textures) and OBJ (meshes: edit in Blender, " +
                               "replace with your OBJ) and glTF (animations: edit in Blender, replace or add as a new one); skeletons are the engine's own format. Bold = replaced by a mod. " +
                               "Textures in the textures folder's blobs are the high-res versions; package copies are low-res fallbacks and GUI icons."));
        return page;
    }

    async Task LoadAssets()
    {
        if (_assets is not null || _game is null) { FilterAssets(); return; }
        var game = _game;
        List<AssetEntry>? list = null;
        if (await Run("Indexing assets", (log, ct) => list = game.AllAssets(log, ct))) _assets = list;
        FilterAssets();
    }

    void FilterAssets()
    {
        if (_assets is null) { _assetList.DataSource = null; _assetCount.Text = _game is null ? "Game data is still loading." : ""; return; }
        _replaced = ModsInList().SelectMany(m => m.Assets).Select(a => AssetIndex.Key(a.File, a.Tag, a.Name, a.Occurrence)).ToHashSet();
        string kind = _assetKind.SelectedIndex > 0 ? (string)_assetKind.SelectedItem! : "";
        var words = _assetSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = _assets.Where(a => (kind.Length == 0 || a.Kind == kind)
                                      && words.All(w => a.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || a.File.Contains(w, StringComparison.OrdinalIgnoreCase)))
                          .ToList();
        _assetList.DataSource = hits.Take(MaxAssetRows).ToList();
        _assetCount.Text = hits.Count > MaxAssetRows ? $"{hits.Count:N0} matches; showing the first {MaxAssetRows:N0}. Search to narrow it down." : $"{hits.Count:N0} matches.";
    }

    List<AssetEntry> Selected() => _assetList.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<AssetEntry>().ToList();
    List<AssetEntry> ShownAssets() => (_assetList.DataSource as List<AssetEntry>) ?? new();

    void ShowAsset()
    {
        var sel = Selected();
        if (sel.Count != 1) { _assetTitle.Text = sel.Count == 0 ? "" : $"{sel.Count} assets selected"; _assetInfo.Text = ""; return; }
        var a = sel[0];
        var by = ModsInList().Where(m => m.Assets.Any(r => r.Targets(a))).Select(m => m.Name).ToList();
        _assetTitle.Text = Path.GetFileName(a.Name);
        _assetInfo.Text = $"{a.Kind}, {a.SizeKb:N0} KB\n{a.Name}\nin {a.File}" +
                          (a.Occurrence > 0 ? $"\n(copy #{a.Occurrence + 1} of this name in that file)" : "") +
                          (by.Count > 0 ? $"\n\nReplaced by: {string.Join(", ", by)}" : "") +
                          (a.Streamed ? "\n\nHigh-res streamed texture: a replacement goes into a new blob and the .ts tables are repointed." : "") +
                          (a.Kind == "Sound" ? "\n\nGame sounds are MS-ADPCM WAV, 48 kHz; plain PCM WAV works too." : "") +
                          (a.Tag == "ASTS" && _game?.Text.GetValueOrDefault(Eg2.Asura.Chunks.TextTable.KeyHash(Path.GetFileNameWithoutExtension(a.Name))) is { } sub
                              ? $"\n\nSubtitle ({sub.Table} / {sub.Key}): \"{sub.Text}\"\nChange it with a text edit (Mods → Text)." : "") +
                          (a.Tag == "ASTS" ? "\n\nStreamed dialogue/music: a replacement rewrites the whole store file (can be hundreds of MB). Not yet confirmed in-game." : "") +
                          "\n\nReplacing changes it everywhere the game uses it, including copies made with \"Copy as a new object\".";
    }

    async Task ExtractAssets(List<AssetEntry> list)
    {
        if (_game is null || list.Count == 0) { Warn("Select one or more assets first."); return; }
        long mb = list.Sum(a => a.Size) / 1048576;
        if (list.Count > 200 && MessageBox.Show(this, $"Extract {list.Count:N0} assets ({mb:N0} MB)?", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        using var dlg = new FolderBrowserDialog { Description = "Extract to folder", UseDescriptionForTitle = true, SelectedPath = _lastExtractDir ?? "" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var outDir = _lastExtractDir = dlg.SelectedPath;
        var install = _game.Install;
        bool png = _texturesAsPng.Checked;
        int n = 0;
        if (await Run("Extracting", (log, ct) =>
            {
                foreach (var (e, data) in AssetIndex.Read(install, list, ct))
                {
                    var dest = Path.Combine(outDir, AssetIndex.OutputPath(e));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    // PNG: most tools can't open the game's BC7 DDS; formats Dds can't decode stay DDS
                    if (png && e.Kind == "Texture" && Dds.Decode(data, out int w, out int h) is { } px) SavePng(px, w, h, Path.ChangeExtension(dest, ".png"));
                    else if (e.Kind == "Animation" && AnimationAsGltf(data, log, ct) is { } gltf) File.WriteAllText(Path.ChangeExtension(dest, ".gltf"), gltf);
                    else File.WriteAllBytes(dest, AssetIndex.ExportBytes(e, data));
                    if (++n % 250 == 0) log($"extracted {n:N0} of {list.Count:N0}");
                }
            }))
        {
            _status.Text = $"Extracted {n:N0} assets to {outDir}.";
            OpenFolder(outDir);
        }
    }

    void OpenAsset()
    {
        if (_game is null || Selected() is not [var a]) return;
        if (a.Kind == "Animation") { _ = OpenAnimation(a); return; }
        if (a.Tag is not ("RSCF" or "ASTS")) { Warn("Facial animations and model skeletons are in the engine's own format; there's nothing to open them with. Extract them to swap with other game files. (Meshes open as OBJ, animations as glTF.)"); return; }
        try
        {
            var (_, data) = AssetIndex.Read(_game.Install, new[] { a }).Single();
            var dest = Path.Combine(Path.GetTempPath(), "Eg2ModKit", "preview", AssetIndex.OutputPath(a));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // textures open as PNG (Windows can't show BC7 DDS): also the easiest start for repainting one
            if (a.Kind == "Texture" && Dds.Decode(data, out int w, out int h) is { } px)
            {
                dest = Path.ChangeExtension(dest, ".png");
                SavePng(px, w, h, dest);
            }
            else File.WriteAllBytes(dest, AssetIndex.ExportBytes(a, data));
            Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
        }
        catch (Exception e) { Warn($"Couldn't open it: {e.Message}\n\nExtract it instead and open it with a WAV player or a DDS viewer (e.g. paint.net, GIMP)."); }
    }

    /// <summary>Put <paramref name="data"/> in the mod's assets folder as the replacement for <paramref name="a"/> (the caller saves the mod).</summary>
    static void StoreReplacement(ModDefinition mod, AssetEntry a, byte[] data, string note)
    {
        var rel = AssetIndex.ReplacementPath(a);
        var dest = Path.Combine(ModPackage.AssetDir(mod), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, data);
        mod.Assets.RemoveAll(r => r.Targets(a));
        mod.Assets.Add(new AssetReplacement { File = a.File, Tag = a.Tag, Name = a.Name, Occurrence = a.Occurrence, Source = rel, Note = note });
    }

    void ReplaceAsset()
    {
        if (Selected() is not [var a]) { Warn("Select one asset to replace."); return; }
        if (a.Kind == "Mesh") { ReplaceMesh(a); return; }
        if (a.Kind == "Animation") { ReplaceAnimation(a); return; }
        string filter = a.Kind switch { "Sound" or "Streamed sound" => "WAV sound (*.wav)|*.wav", "Texture" => "Image (*.png;*.jpg;*.dds)|*.png;*.jpg;*.jpeg;*.dds", _ => $"Extracted {a.Tag} chunk (*.{a.Tag.ToLowerInvariant()})|*.{a.Tag.ToLowerInvariant()}|All files|*.*" };
        using var dlg = new OpenFileDialog { Filter = filter, Title = $"Replace {Path.GetFileName(a.Name)}" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var data = File.ReadAllBytes(dlg.FileName);
        if (a.Kind is "Sound" or "Streamed sound" && AssetIndex.WavFormat(data) is null) { Warn("That isn't a WAV file."); return; }
        if (a.Kind == "Texture" && !data.AsSpan().StartsWith("DDS "u8))
        {
            if (_game is null) return;
            try { data = ImageToDds(a, dlg.FileName); }
            catch (Exception e) { Warn($"Couldn't use that picture: {e.Message}"); return; }
        }
        if (a.Kind is "Sound" or "Streamed sound" && AssetIndex.WavFormat(data) != 2
            && MessageBox.Show(this, "The game's own sounds are MS-ADPCM WAV. This file is a different WAV format, which may not play in the game. Use it anyway?",
                               Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (TargetMod() is not { } mod) return;

        if (ReferenceEquals(mod, _current)) CommitEditor();
        StoreReplacement(mod, a, data, $"{a.Kind}: {Path.GetFileName(a.Name)} <- {Path.GetFileName(dlg.FileName)}");
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {a.Name} replaced by {dlg.FileName}");
        _status.Text = $"\"{Path.GetFileName(a.Name)}\" will be replaced by \"{mod.Name}\" when you apply.";
        FilterAssets();
    }

    static void SavePng(byte[] rgba, int w, int h, string path)
    {
        using var bmp = RgbaBitmap(rgba, w, h);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>A picture encoded like the game's texture: its size (stretched to fit), format and mip count.</summary>
    byte[] ImageToDds(AssetEntry a, string path)
    {
        var (_, orig) = AssetIndex.Read(_game!.Install, new[] { a }).Single();
        int h = BitConverter.ToInt32(orig, 12), w = BitConverter.ToInt32(orig, 16);
        using var src = new Bitmap(path);
        using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, w, h);
        }
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new byte[w * h * 4];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
        bmp.UnlockBits(d);
        for (int i = 0; i < px.Length; i += 4) (px[i], px[i + 2]) = (px[i + 2], px[i]);   // BGRA -> RGBA
        return Dds.EncodeLike(orig, px, w, h);
    }

    /// <summary>Every model skeleton in the game (HSKN), read once: an exported animation needs its rig's bones.</summary>
    List<Skeleton> Skeletons(Action<string> log, CancellationToken ct)
    {
        if (_skeletons is not null) return _skeletons;
        log("reading model skeletons (once)");
        var list = new List<Skeleton>();
        foreach (var (_, body) in AssetIndex.Read(_game!.Install, (_assets ?? new()).Where(x => x.Tag == "HSKN"), ct))
            if (Skeleton.Parse(body) is { } s) list.Add(s);
        return _skeletons = list;
    }

    /// <summary>glTF of a clip on its rig's skeleton, or null when no skeleton shares its bones.</summary>
    string? AnimationAsGltf(byte[] hcan, Action<string> log, CancellationToken ct)
    {
        var clip = Eg2.Asura.Chunks.AnimClip.Parse(hcan);
        return AnimationGltf.SkeletonFor(clip, Skeletons(log, ct)) is { } sk ? AnimationGltf.Export(hcan, sk) : null;
    }

    async Task OpenAnimation(AssetEntry a)
    {
        string? dest = null;
        if (!await Run("Exporting animation", (log, ct) =>
            {
                var (_, data) = AssetIndex.Read(_game!.Install, new[] { a }, ct).Single();
                var gltf = AnimationAsGltf(data, log, ct) ?? throw new InvalidDataException("no model skeleton in the game has this animation's bones");
                dest = Path.Combine(Path.GetTempPath(), "Eg2ModKit", "preview", Path.ChangeExtension(AssetIndex.OutputPath(a), ".gltf"));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllText(dest, gltf);
            })) return;
        try { Process.Start(new ProcessStartInfo(dest!) { UseShellExecute = true }); }
        catch (Exception e) { Warn($"Saved {dest}, but Windows has no program for .gltf files ({e.Message}). Open it in Blender: File → Import → glTF 2.0."); }
    }

    /// <summary>
    /// Replace an animation with a glTF (.gltf/.glb, e.g. from Blender: export the game's clip first so the bone names
    /// match), or with an extracted .hcan. The game's clip stays the template: its rig, events and effects are kept,
    /// and bones the file lacks keep their old motion.
    /// </summary>
    void ReplaceAnimation(AssetEntry a)
    {
        if (_game is null) return;
        using var dlg = new OpenFileDialog { Filter = "glTF animation (*.glb;*.gltf)|*.glb;*.gltf|Extracted HCAN chunk (*.hcan)|*.hcan", Title = $"Replace {a.Name}" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (TargetMod() is not { } mod) return;
        byte[] data;
        string how;
        try
        {
            var (_, orig) = AssetIndex.Read(_game.Install, new[] { a }).Single();
            if (dlg.FileName.EndsWith(".hcan", StringComparison.OrdinalIgnoreCase)) { data = File.ReadAllBytes(dlg.FileName); Eg2.Asura.Chunks.AnimClip.Parse(data); how = "raw clip"; }
            else data = AnimationGltf.Import(orig, dlg.FileName, out how);
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or IOException or System.Text.Json.JsonException
                                    or IndexOutOfRangeException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { Warn($"Couldn't use that file: {e.Message}"); return; }

        if (ReferenceEquals(mod, _current)) CommitEditor();
        StoreReplacement(mod, a, data, $"Animation: {a.Name} <- {Path.GetFileName(dlg.FileName)}");
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {a.Name} replaced by {dlg.FileName} ({how})");
        _status.Text = $"\"{a.Name}\" will be replaced by \"{mod.Name}\" when you apply ({how}).";
        FilterAssets();
    }

    /// <summary>
    /// A new clip under its own name, made from a glTF (or .hcan) with the selected clip as the template (its rig,
    /// events and effects). Optionally every reference to the selected clip then plays the new one.
    /// </summary>
    void AddNewClip()
    {
        if (_game is null || Selected() is not [var a] || a.Kind != "Animation") { Warn("Select one animation: the new one copies its skeleton and events."); return; }
        using var dlg = new OpenFileDialog { Filter = "glTF animation (*.glb;*.gltf)|*.glb;*.gltf|Extracted HCAN chunk (*.hcan)|*.hcan", Title = $"New animation based on {a.Name}" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (Prompt("Name for the new animation (letters, digits and _; it must not be a game animation's name):", a.Name + "_new") is not { Length: > 0 } name) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]+$")) { Warn("Use letters, digits and _ only."); return; }
        if (_assets?.Any(x => x.Kind == "Animation" && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true) { Warn($"The game already has an animation called {name}."); return; }
        if (TargetMod() is not { } mod) return;
        if (mod.NewClips.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Warn($"{mod.Name} already adds an animation called {name}."); return; }
        byte[] data;
        string how;
        try
        {
            var (_, orig) = AssetIndex.Read(_game.Install, new[] { a }).Single();
            if (dlg.FileName.EndsWith(".hcan", StringComparison.OrdinalIgnoreCase)) { data = File.ReadAllBytes(dlg.FileName); Eg2.Asura.Chunks.AnimClip.Parse(data); how = "raw clip"; }
            else data = AnimationGltf.Import(orig, dlg.FileName, out how);
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or IOException or System.Text.Json.JsonException
                                    or IndexOutOfRangeException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { Warn($"Couldn't use that file: {e.Message}"); return; }

        if (ReferenceEquals(mod, _current)) CommitEditor();
        var rel = Path.Combine("clips", name + ".hcan");
        var dest = Path.Combine(ModPackage.AssetDir(mod), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, data);
        mod.NewClips.Add(new NewClip { Name = name, Source = rel, Note = $"from {Path.GetFileName(dlg.FileName)}, based on {a.Name}" });
        bool swap = MessageBox.Show(this, $"Play {name} wherever the game plays {a.Name}? (Characters' animation sets, random picks and the animation logic.)\n\n" +
                                          "No: it's added but nothing plays it until something names it.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        if (swap) { mod.ClipSwaps.RemoveAll(s => s.From.Equals(a.Name, StringComparison.OrdinalIgnoreCase)); mod.ClipSwaps.Add(new ClipSwap { From = a.Name, To = name }); }
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: new animation {name} from {dlg.FileName} ({how}){(swap ? $", playing in place of {a.Name}" : "")}");
        _status.Text = $"\"{name}\" will be added by \"{mod.Name}\" when you apply{(swap ? $", in place of {a.Name}" : "")}.";
    }

    /// <summary>Every reference to the selected clip (anim sets, random picks, reflexes, animation logic) points at another one.</summary>
    void SwapClip()
    {
        if (_game is null || Selected() is not [var a] || a.Kind != "Animation") { Warn("Select the animation to replace everywhere it plays."); return; }
        if (Prompt($"Play which animation wherever {a.Name} plays? (A game animation's name, or a new one a mod adds.)", "") is not { Length: > 0 } to) return;
        bool known = _assets?.Any(x => x.Kind == "Animation" && x.Name.Equals(to, StringComparison.OrdinalIgnoreCase)) == true
                     || ModsInList().Any(m => m.NewClips.Any(c => c.Name.Equals(to, StringComparison.OrdinalIgnoreCase)));
        if (!known) { Warn($"No game animation or mod's new animation is called {to}."); return; }
        if (TargetMod() is not { } mod) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        mod.ClipSwaps.RemoveAll(s => s.From.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
        mod.ClipSwaps.Add(new ClipSwap { From = a.Name, To = to });
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        Log($"{mod.Id}: {a.Name} -> {to}");
        _status.Text = $"\"{to}\" will play in place of \"{a.Name}\" when you apply \"{mod.Name}\".";
    }

    /// <summary>
    /// Replace a mesh with an OBJ (Blender etc.; export the game's OBJ first to keep its scale, axes and material groups).
    /// Optionally the same OBJ replaces every detail level ("l1#name".."l5#name"), each keeping its own materials.
    /// </summary>
    void ReplaceMesh(AssetEntry a)
    {
        if (_game is null || _assets is null) return;
        using var dlg = new OpenFileDialog { Filter = "Wavefront OBJ (*.obj)|*.obj", Title = $"Replace {a.Name}" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string baseName = System.Text.RegularExpressions.Regex.Replace(a.Name, @"^l\d#", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var lods = _assets.Where(x => x.Kind == "Mesh" && x.File.Equals(a.File, StringComparison.OrdinalIgnoreCase) && x.Occurrence == a.Occurrence
                                      && System.Text.RegularExpressions.Regex.Replace(x.Name, @"^l\d#", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                                             .Equals(baseName, StringComparison.OrdinalIgnoreCase)).ToList();
        var targets = new List<AssetEntry> { a };
        if (lods.Count > 1 && MessageBox.Show(this, $"{baseName} has {lods.Count} detail levels. Use this OBJ for all of them? (Recommended: otherwise the old model shows when zoomed out.)",
                                              Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            targets = lods;
        if (TargetMod() is not { } mod) return;
        var obj = File.ReadAllText(dlg.FileName);
        var built = new List<(AssetEntry Entry, string Rel, byte[] Mesh)>();
        try
        {
            foreach (var (e, payload) in AssetIndex.Read(_game.Install, targets))
            {
                var mesh = MeshGeometry.Parse(payload).WithObj(obj);
                built.Add((e, Path.ChangeExtension(AssetIndex.OutputPath(e), ".mesh"), mesh.ToBytes()));
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        { Warn($"Couldn't use that OBJ: {ex.Message}"); return; }

        if (ReferenceEquals(mod, _current)) CommitEditor();
        foreach (var (e, rel, mesh) in built)
        {
            var dest = Path.Combine(ModPackage.AssetDir(mod), rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, mesh);
            mod.Assets.RemoveAll(r => r.Targets(e));
            mod.Assets.Add(new AssetReplacement
            {
                File = e.File, Tag = e.Tag, Name = e.Name, Occurrence = e.Occurrence, Source = rel,
                Note = $"Mesh: {e.Name} <- {Path.GetFileName(dlg.FileName)}",
            });
        }
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
        var m0 = MeshGeometry.Parse(built[0].Mesh);
        Log($"{mod.Id}: {string.Join(", ", built.Select(b => b.Entry.Name))} replaced by {dlg.FileName} ({m0.VertexCount:N0} vertices, {m0.Indices.Length / 3:N0} triangles)");
        _status.Text = $"\"{baseName}\" ({built.Count} detail level{(built.Count == 1 ? "" : "s")}) will be replaced by \"{mod.Name}\" when you apply.";
        FilterAssets();
    }

    void UndoReplacement()
    {
        if (Selected() is not [var a]) return;
        foreach (var mod in ModsInList().Where(m => m.Assets.Any(r => r.Targets(a))).ToList())
        {
            if (ReferenceEquals(mod, _current)) CommitEditor();
            foreach (var r in mod.Assets.Where(r => r.Targets(a)).ToList())
            {
                var file = Path.Combine(ModPackage.AssetDir(mod), r.Source);
                if (File.Exists(file)) File.Delete(file);
                mod.Assets.Remove(r);
            }
            mod.Save();
            if (ReferenceEquals(mod, _current)) ShowEditor();
            Log($"{mod.Id}: removed replacement of {a.Name}");
        }
        FilterAssets();
    }
}
