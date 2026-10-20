using Eg2.Asura;

namespace Eg2.ModKit;

/// <summary>
/// Simple-mode pages for whole data types (traits, jobs, schemes, regions, objectives…) as <see cref="Tweak"/>s: one group
/// per object, its texts (text edits) and named numbers (field edits) from <see cref="ObjectInspector.FriendlyRows"/>.
/// Your copies (new objects) come first, marked ★; their rows edit the copy itself. Links and undecoded values stay on
/// Browse game data.
/// </summary>
public static class ObjectPages
{
    /// <param name="copies">Your new objects (from the mods list) as browsable objects; those of this tag are listed.</param>
    public static List<Tweak> Page(GameData game, string tag, IEnumerable<GameObject>? copies = null)
    {
        var list = new List<Tweak>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var objects = (copies ?? []).Where(c => c.Tag == tag && c.Draft is not null).Concat(game.Objects.Where(o => o.Tag == tag && !o.IsRecord));
        foreach (var o in objects)
        {
            var d = o.Draft;
            // a copy shows its source's rows (offsets match; values and texts it overrides come from the copy)
            var src = d is null ? o : game.Objects.FirstOrDefault(x => !x.IsRecord && x.Tag == d.Tag && x.Package.Equals(d.Package, StringComparison.OrdinalIgnoreCase)
                                                                       && x.Key.Equals(d.Source, StringComparison.OrdinalIgnoreCase));
            if (src is null) continue;
            var rows = ObjectInspector.FriendlyRows(src, game);
            var texts = rows.Where(r => r.Offset >= 0 && r.Offset <= src.Body.Length - 4 && r.Meaning.StartsWith('"'))
                .Select(r => (Row: r, Text: Characters.TextOf(game, Bytes.U32(src.Body, r.Offset)))).Where(x => x.Text is not null).ToList();
            var numbers = rows.Where(r => r.IsSetting && r.Editable && r.Offset >= ObjectHeader.Size && r.Offset <= src.Body.Length - r.Size).ToList();
            if (texts.Count == 0 && numbers.Count == 0) continue;

            string group = GroupName(src, texts.Select(x => x.Text!.Text).FirstOrDefault());
            if (d is not null) group = $"★ {d.Texts.OrderBy(t => t.Offset).FirstOrDefault()?.Text ?? group} (your copy @{d.Key})";
            if (!taken.Add(group))
            {
                string plain = group;
                group = $"{plain} ({o.Package})";
                for (int i = 2; !taken.Add(group); i++) group = $"{plain} ({o.Package} {i})";
            }
            foreach (var (r, t) in texts)
                list.Add(new Tweak
                {
                    Key = d is null ? $"text:{t!.Table}:{t.Key}" : $"draft:{d.Key}:{r.Offset}", Group = group, Label = $"Text: {r.Path}", Default = t!.Text,
                    Help = d is null ? "Texts change everywhere the game shows them." : "Your copy's own text; the game's stays as it is.",
                    Text = new TextEdit { Table = t.Table, Key = t.Key, Text = t.Text }, Object = o, Draft = d, DraftMod = o.DraftMod, DraftOffset = r.Offset,
                });
            if (Scheme.TryParse(src.Tag, src.Body) is { } scheme)
                foreach (var (field, label, game0) in new[]
                         {
                             ("Duration", "how long it runs (seconds; 7200 = 2 hours)", scheme.Duration),
                             ("Heat", "heat it adds to the region (negative lowers heat)", scheme.Heat),
                             ("Expiry", "how long the offer stays on the map (seconds; -1 = stays)", scheme.Expiry),
                         })
                    list.Add(new Tweak
                    {
                        Key = $"scheme:{(d is null ? Bytes.Hex(src.ObjectId) : "@" + d.Key)}:{field}", Group = group, Label = $"Scheme: {label}",
                        Default = game0.ToString(System.Globalization.CultureInfo.InvariantCulture), Object = o, Draft = d, DraftMod = o.DraftMod,
                        SchemeField = field, SchemeObject = d is null ? Bytes.Hex(src.ObjectId) : "@" + d.Key,
                        Help = "Minion groups and launch costs: Scheme… on Browse game data.",
                    });
            foreach (var r in numbers)
            {
                int eq = r.Meaning.LastIndexOf(" = ", StringComparison.Ordinal);
                string label = r.Path == "Script setting" && eq > 0 ? $"Script setting: {r.Meaning[..eq]}" : r.Path;
                string type = r.Size == 1 ? "u8" : r.Float.Length > 0 ? "f32" : r.Int.StartsWith('-') ? "i32" : "u32";
                list.Add(new Tweak
                {
                    Key = d is null ? $"{src.Package}:{src.Tag}:{src.Key}:{r.Offset}" : $"draft:{d.Key}:{r.Offset}", Group = group, Label = $"Number: {label}",
                    Default = type == "f32" ? r.Float : r.Int, Object = o, Draft = d, DraftMod = o.DraftMod, DraftOffset = r.Offset,
                    Help = r.Path == "Script setting" ? "Script settings are numbers in this thing's game script, named after the step they feed." : "",
                    Field = new FieldEdit
                    {
                        Package = src.Package, Tag = src.Tag, Object = src.Key, Offset = r.Offset, Type = type,
                        Expect = Convert.ToHexString(src.Body, r.Offset, r.Size), Note = $"{group}: {label}",
                    },
                });
            }
        }
        return list.DistinctBy(t => (t.Group, t.Key)).ToList();
    }

    /// <summary>The object's own name unless it's an internal one (Some_Name), then its first text.</summary>
    static string GroupName(GameObject o, string? firstText)
    {
        string name = o.Name.Trim('"').Trim();
        bool internalName = name.Length == 0 || name.StartsWith("0x") || !name.Contains(' ') && (name.Contains('_') || name.Length <= 4 && name.Any(char.IsDigit));
        if (internalName && firstText is not null)
        {
            string t = ObjectInspector.Clean(firstText).Trim().ReplaceLineEndings(" ");
            return t.Length > 60 ? t[..57] + "…" : t;
        }
        return name.Length > 0 ? name : Bytes.Hex(o.ObjectId);
    }
}
