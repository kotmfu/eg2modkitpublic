using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Own art: the textures of a new furniture item that has its own copy of the donor's model and material.</summary>
sealed partial class MainForm
{
    async Task EditItemTextures()
    {
        CommitEditor();
        if (_current is not { } mod || _newGrid.CurrentRow?.DataBoundItem is not NewFurniture item) { Warn("Select a new furniture item first."); return; }
        if (_game is not { } game) { Warn("Game data is still loading."); return; }
        FurnitureArt? art = null;
        if (!await Run($"Finding {item.Donor}'s textures", (log, ct) => art = FurnitureArt.ForItem(game, item.Donor, log, ct)) || art is null) return;
        var rows = art.Textures.Values.OrderBy(t => t.Name).ToList();
        if (art.Icon is { } icon) rows.Insert(0, icon);
        if (rows.Count == 0) { Warn($"{item.Donor} has no textures ModKit can find."); return; }
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        grid.Columns.Add("tex", "Texture");
        grid.Columns.Add("mine", "Your file (empty = the original, copied)");
        void Fill()
        {
            grid.Rows.Clear();
            foreach (var t in rows)
                grid.Rows.Add(t == art.Icon ? $"Build-menu icon ({Path.GetFileName(t.Name)})" : Path.GetFileName(t.Name), Mine(t)?.Source ?? "");
        }
        ArtTexture? Mine(FurnitureArt.Texture t) => item.Textures.FirstOrDefault(x => FurnitureArt.TextureHash(x.Texture) == t.Hash);
        FurnitureArt.Texture? Sel() => grid.CurrentRow is { Index: >= 0 } r ? rows[r.Index] : null;

        using var dlg = new ThemedForm { Text = $"Textures of {item.Id} (copied from {item.Donor})", Width = 900, Height = 420, StartPosition = FormStartPosition.CenterParent };
        var extract = Btn("Extract original…", async (_, _) =>
        {
            if (Sel() is not { } t) return;
            using var save = new SaveFileDialog { FileName = Path.ChangeExtension(Path.GetFileName(t.Name), ".dds"), Filter = "DDS texture|*.dds" };
            if (save.ShowDialog(dlg) != DialogResult.OK) return;
            byte[]? data = null;
            if (await Run("Reading texture", (log, ct) => data = FurnitureArt.Original(game, t, log, ct)) && data is not null)
            {
                File.WriteAllBytes(save.FileName, data);
                Log($"extracted {t.Name} to {save.FileName}");
            }
        });
        var replace = Btn("Replace with my file…", (_, _) =>
        {
            if (Sel() is not { } t) return;
            using var open = new OpenFileDialog { Filter = "DDS texture|*.dds" };
            if (open.ShowDialog(dlg) != DialogResult.OK) return;
            if (!File.ReadAllBytes(open.FileName).AsSpan().StartsWith("DDS "u8)) { Warn("That isn't a DDS file. Save it as DDS (same compression as the original works best)."); return; }
            string rel = Path.Combine("art", item.Id, Path.ChangeExtension(Path.GetFileName(t.Name), ".dds"));
            var dest = Path.Combine(ModPackage.AssetDir(mod), rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(open.FileName, dest, overwrite: true);
            item.Textures.RemoveAll(x => FurnitureArt.TextureHash(x.Texture) == t.Hash);
            item.Textures.Add(new ArtTexture { Texture = t.Name, Source = rel });
            if (t != art.Icon) item.OwnArt = true;   // the icon is per item name and needs no own model
            mod.Save();
            Fill();
        });
        var original = Btn("Use the original", (_, _) =>
        {
            if (Sel() is not { } t || Mine(t) is not { } m) return;
            var file = Path.Combine(ModPackage.AssetDir(mod), m.Source);
            if (File.Exists(file)) File.Delete(file);
            item.Textures.Remove(m);
            mod.Save();
            Fill();
        });
        dlg.Controls.Add(grid);
        dlg.Controls.Add(Bar(extract, replace, original));
        dlg.Controls.Add(Hint($"{item.Id} gets its own copy of {item.Donor}'s model ({string.Join(", ", art.Models.Select(m => m.Name))}) and material, " +
                              "so changing these textures leaves the original item alone. Untouched textures are copied as they are (high-res where the game has it). " +
                              "Colour = the painted look, normal = surface bumps, metal = shininess."));
        Fill();
        dlg.ShowDialog(this);
        ShowEditor();
    }
}
