using Eg2.ModKit;

namespace Eg2.ModManager;

/// <summary>Quick Tweaks tab: plain checkboxes and numbers, stored in the "quick-tweaks" mod or any mod picked in
/// "Save into" (e.g. a total conversion's own caps). The Research and Furniture Effects tabs save into the same mod.</summary>
sealed partial class MainForm
{
    readonly TweaksPanel _tweakHost = new();
    readonly ComboBox _tweakTarget = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    string _tweakTargetId = QuickTweaks.ModId;

    TabPage TweaksPage()
    {
        var apply = Theme.Primary(Btn("Install mods", async (_, _) => await Apply()));
        var page = new TabPage("Quick Tweaks");
        page.Controls.Add(_tweakHost);
        page.Controls.Add(Bar(apply, Btn("Remove mods from game", async (_, _) => await Uninstall()), LaunchButton()));
        _tweakTarget.SelectionChangeCommitted += (_, _) =>
        {
            SaveSettingsPage(_tabs.SelectedTab);   // into the mod picked before
            _tweakTargetId = _tweakTarget.SelectedItem is ModDefinition m ? m.Id : QuickTweaks.ModId;
            LoadTweaks();
            if (GridFor(_tabs.SelectedTab) is { } tg) LoadGrid(tg);
        };
        page.Controls.Add(Bar(new Label { Text = "Save into:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _tweakTarget));
        page.Controls.Add(Hint("Tick a setting to change it, set the number, then press \"Install mods\". " +
                               "Unticked settings stay as the game ships them. They're saved into the mod picked below " +
                               "(\"Quick tweaks\", or e.g. your total conversion's own mod); the Research and Furniture Effects tabs use it too."));
        return page;
    }

    ModDefinition? TweaksMod() => ModsInList().FirstOrDefault(m => m.Id == _tweakTargetId);

    void LoadTweaks()
    {
        var mod = TweaksMod();
        if (mod is not null && ReferenceEquals(mod, _current)) CommitEditor();
        _tweakTarget.BeginUpdate();
        _tweakTarget.Items.Clear();
        _tweakTarget.Items.AddRange(ModsInList().OrderBy(m => m.Id != QuickTweaks.ModId).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        if (TweaksMod() is null) _tweakTarget.Items.Insert(0, "Quick tweaks (new mod)");
        _tweakTarget.SelectedItem = (object?)TweaksMod() ?? _tweakTarget.Items[0];
        _tweakTarget.EndUpdate();
        _tweakHost.Show(QuickTweaks.All(_game), mod, _game is null ? "Research-based settings appear once the game data has loaded." : null);
    }

    /// <summary>Write the page into the picked mod (the quick-tweaks mod is created and ticked on first use).</summary>
    void SaveTweaks()
    {
        if (!_tweakHost.HasRows) return;
        var mod = TweaksMod();
        bool any = _tweakHost.AnyTicked;
        if (mod is null && !any) return;
        if (mod is null && (mod = NewTweaksMod()) is null) return;
        if (ReferenceEquals(mod, _current)) CommitEditor();
        _tweakHost.SaveInto(mod);
        FinishTweaksMod(mod, any);
    }

    ModDefinition? NewTweaksMod()
    {
        if (_tweakTargetId != QuickTweaks.ModId) return null;   // a picked mod that has gone away
        var mod = NewMod(QuickTweaks.ModId, "Quick tweaks", select: false);
        if (mod is not null) mod.Description = "Settings from the Quick Tweaks, Research and Furniture Effects tabs.";
        return mod;
    }

    /// <summary>Tick the mod when something is set, save it, refresh the editor if it's open.</summary>
    void FinishTweaksMod(ModDefinition mod, bool any)
    {
        int i = _modList.Items.IndexOf(mod);
        if (i >= 0 && any) _modList.SetItemChecked(i, true);
        mod.Save();
        if (ReferenceEquals(mod, _current)) ShowEditor();
    }
}
