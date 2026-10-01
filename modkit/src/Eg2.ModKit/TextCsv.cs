using System.Text;

namespace Eg2.ModKit;

/// <summary>
/// All game text as one spreadsheet (UTF-8 CSV with BOM so Excel keeps accents): Table, Key, Game text, New text.
/// Fill in "New text" for the lines to change and import it back into a mod as TextEdits. Blank "New text" (or the
/// same as the game's) means "leave as the game ships it" and removes that mod's edit.
/// </summary>
public static class TextCsv
{
    static readonly string[] Header = { "Table", "Key", "Game text", "New text" };

    public static void Export(GameData game, ModDefinition? mod, string path)
    {
        var mine = mod?.TextEdits.ToDictionary(e => (e.Table.ToLowerInvariant(), e.Key), e => e.Text) ?? new();
        var sb = new StringBuilder();
        Row(sb, Header);
        foreach (var t in game.Text.Values.OrderBy(t => t.Table, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Key, StringComparer.Ordinal))
            Row(sb, t.Table, t.Key, t.Text, mine.GetValueOrDefault((t.Table.ToLowerInvariant(), t.Key), ""));
        // edits of keys the game doesn't have (new lines) stay in the sheet too
        var known = game.Text.Values.Select(t => (t.Table.ToLowerInvariant(), t.Key)).ToHashSet();
        foreach (var e in mod?.TextEdits.Where(e => !known.Contains((e.Table.ToLowerInvariant(), e.Key))) ?? Enumerable.Empty<TextEdit>())
            Row(sb, e.Table, e.Key, "", e.Text);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public sealed record ImportResult(int Changed, int Removed, int Unchanged, List<string> Problems);

    /// <summary>Apply a filled-in sheet to <paramref name="mod"/>'s TextEdits (the caller saves the mod).</summary>
    public static ImportResult Import(GameData game, ModDefinition mod, string path)
    {
        var rows = Parse(File.ReadAllText(path));
        var problems = new List<string>();
        if (rows.Count == 0 || !rows[0].Take(4).Select(h => h.Trim()).SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"the first row must be: {string.Join(", ", Header)} (export a sheet first)");
        var game_ = game.Text.Values.ToDictionary(t => (t.Table.ToLowerInvariant(), t.Key), t => t.Text);
        int changed = 0, removed = 0, same = 0;
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 1 && r[0].Length == 0) continue;   // blank line
            if (r.Count < 4) { problems.Add($"row {i + 1}: expected 4 columns, found {r.Count}"); continue; }
            string table = r[0].Trim(), key = r[1].Trim(), text = r[3];
            if (!game.TextTables.Contains(table)) { problems.Add($"row {i + 1}: no text table '{table}'"); continue; }
            if (key.Length == 0 || key.Any(c => c > 127)) { problems.Add($"row {i + 1}: bad key '{key}'"); continue; }
            game_.TryGetValue((table.ToLowerInvariant(), key), out var original);
            int at = mod.TextEdits.FindIndex(e => e.Table.Equals(table, StringComparison.OrdinalIgnoreCase) && e.Key == key);
            if (text.Length == 0 || text == original)
            {
                if (at >= 0) { mod.TextEdits.RemoveAt(at); removed++; } else same++;
                continue;
            }
            if (at >= 0) { if (mod.TextEdits[at].Text == text) { same++; continue; } mod.TextEdits[at].Text = text; }
            else mod.TextEdits.Add(new TextEdit { Table = table, Key = key, Text = text });
            changed++;
        }
        return new ImportResult(changed, removed, same, problems);
    }

    static void Row(StringBuilder sb, params string[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var c = cells[i];
            if (c.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || c.StartsWith(' ') || c.EndsWith(' ')) sb.Append('"').Append(c.Replace("\"", "\"\"")).Append('"');
            else sb.Append(c);
        }
        sb.Append("\r\n");
    }

    /// <summary>RFC 4180 CSV (quoted fields may hold commas, quotes and line breaks). Also accepts Excel's ";" sheets.</summary>
    public static List<List<string>> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        int firstLineEnd = text.IndexOfAny(new[] { '\r', '\n' });
        var head = firstLineEnd < 0 ? text : text[..firstLineEnd];
        char sep = head.Count(c => c == ';') > head.Count(c => c == ',') ? ';' : ',';
        var rows = new List<List<string>>();
        var row = new List<string>(); var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>();
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
