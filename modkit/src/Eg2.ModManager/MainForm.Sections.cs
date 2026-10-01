using System.Collections;
using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>
/// The mod editor's parts as a list with counts ("Text (4,812)") instead of a tab strip that overflows; the pages stay
/// in <see cref="_modTabs"/> (header hidden), so code that picks a page by index keeps working.
/// </summary>
sealed partial class MainForm
{
    readonly ListBox _sections = new()
    {
        Dock = DockStyle.Left, Width = 190, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, BorderStyle = BorderStyle.None,
    };

    // page index -> label; the last page ("Other changes") counts every list the others don't show
    static readonly string[] SectionNames = { "New furniture", "Prices", "Text", "Values", "While the game runs", "Art and sounds", "New objects", "Other changes" };

    void BuildSections()
    {
        _modTabs.Appearance = TabAppearance.FlatButtons;
        _modTabs.SizeMode = TabSizeMode.Fixed;
        _modTabs.ItemSize = new Size(0, 1);
        _sections.Items.AddRange(SectionNames.Cast<object>().ToArray());
        _sections.SelectedIndexChanged += (_, _) => { if (_sections.SelectedIndex >= 0) _modTabs.SelectedIndex = _sections.SelectedIndex; };
        _modTabs.SelectedIndexChanged += (_, _) => { if (_sections.SelectedIndex != _modTabs.SelectedIndex) _sections.SelectedIndex = _modTabs.SelectedIndex; };
        _sections.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            int n = SectionCount(e.Index);
            using (var back = new SolidBrush(sel ? Theme.Selection : Theme.Panel)) e.Graphics.FillRectangle(back, e.Bounds);
            var color = sel ? Theme.SelectionText : n == 0 ? Theme.Muted : Theme.Text;
            string text = n == 0 ? SectionNames[e.Index] : $"{SectionNames[e.Index]}  ({n:N0})";
            TextRenderer.DrawText(e.Graphics, text, _sections.Font, Rectangle.Inflate(e.Bounds, -8, 0), color,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        _sections.SelectedIndex = 0;
    }

    int SectionCount(int page)
    {
        if (_current is not { } m) return 0;
        return page switch
        {
            0 => m.NewFurniture.Count,
            1 => m.FurnitureEdits.Count,
            2 => m.TextEdits.Count,
            3 => m.FieldEdits.Count,
            4 => m.Runtime.Count,
            5 => m.Assets.Count + m.NewAssets.Count,
            6 => m.NewObjects.Count,
            _ => typeof(ModDefinition).GetProperties()
                .Where(p => typeof(IList).IsAssignableFrom(p.PropertyType) && p.Name is not (nameof(m.NewFurniture) or nameof(m.FurnitureEdits)
                    or nameof(m.TextEdits) or nameof(m.FieldEdits) or nameof(m.Runtime) or nameof(m.Assets) or nameof(m.NewAssets) or nameof(m.NewObjects)))
                .Sum(p => (p.GetValue(m) as IList)?.Count ?? 0),
        };
    }

    /// <summary>Recount after the mod changed; opens the first part that has something in it when a mod is picked.</summary>
    void RefreshSections(bool picked)
    {
        if (picked && _current is not null && SectionCount(_modTabs.SelectedIndex) == 0)
            for (int i = 0; i < SectionNames.Length; i++)
                if (SectionCount(i) > 0) { _modTabs.SelectedIndex = i; break; }
        _sections.Invalidate();
    }
}
