using System.Text.Json;

namespace Eg2.ModKit;

/// <summary>A piece of a lair floor (rooms, gold seams, edge rock, furniture) saved as map changes relative to its top-left
/// cell, to paste elsewhere or into another lair: clear the objects there, fill it with rock, dig the rooms back out
/// row by row, then place the furniture (carried as records, so it needs a lair with the same object format).</summary>
public sealed class MapStamp
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public List<MapEdit> Edits { get; set; } = new();

    // ponytail: outside/beach cells come back as rock (a stamp is for the underground).
    public static MapStamp Capture(LairMap map, int floor, int x0, int y0, int x1, int y1, string name, Func<uint, string?> itemName)
    {
        var f = map.Floors.FirstOrDefault(x => x.Index == floor) ?? throw new ArgumentException($"no floor {floor}");
        (x0, x1) = (Math.Clamp(Math.Min(x0, x1), 0, f.Width - 1), Math.Clamp(Math.Max(x0, x1), 0, f.Width - 1));
        (y0, y1) = (Math.Clamp(Math.Min(y0, y1), 0, f.Height - 1), Math.Clamp(Math.Max(y0, y1), 0, f.Height - 1));
        var s = new MapStamp { Name = name, Width = x1 - x0 + 1, Height = y1 - y0 + 1 };
        MapEdit Area(string action, int ax0, int ay0, int ax1, int ay1) => new() { Action = action, X0 = ax0, Y0 = ay0, X1 = ax1, Y1 = ay1, Note = $"stamp {name}" };
        s.Edits.Add(Area("remove", 0, 0, s.Width - 1, s.Height - 1));
        s.Edits.Add(Area("rock", 0, 0, s.Width - 1, s.Height - 1));
        static string? Kind(Eg2.Asura.Prop c) => LairMap.TypeOf(c) is var t && t == LairMap.Spot ? "gold" : t == LairMap.Edge ? "wall"
            : LairMap.RoomName(t) is { } r ? "room:" + r : LairMap.TierOf(c) is > 0 and var tier ? $"tier:{tier}" : null;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1;)
            {
                string? k = Kind(f.Cells[y * f.Width + x]);
                int end = x;
                while (end + 1 <= x1 && Kind(f.Cells[y * f.Width + end + 1]) == k) end++;
                if (k is not null)
                {
                    var e = Area(k.Split(':')[0], x - x0, y - y0, end - x0, y - y0);
                    if (k.StartsWith("room:")) e.Room = k[5..];
                    if (k.StartsWith("tier:")) e.Tier = int.Parse(k[5..]);
                    s.Edits.Add(e);
                }
                x = end + 1;
            }
        foreach (var o in map.Objects.Where(o => o.Floor == floor && o.Column >= x0 && o.Column <= x1 && o.Row >= y0 && o.Row <= y1))
        {
            var e = Area("place", o.Column - x0, o.Row - y0, o.Column - x0, o.Row - y0);
            e.Item = itemName(o.Fnas) ?? o.Fnas.ToString("x8");
            e.Facing = $"{o.FaceX},{o.FaceY}";
            e.Template = Convert.ToHexString(o.Node.Payload());
            e.TemplateKey = o.Node.Key.ToString("x8");
            s.Edits.Add(e);
        }
        return s;
    }

    /// <summary>The stamp's changes for <paramref name="file"/>'s floor with its top-left cell at column x, row y.</summary>
    public List<MapEdit> At(string file, int floor, int x, int y) => Edits.Select(e => new MapEdit
    {
        File = file, Floor = floor, Action = e.Action, X0 = e.X0 + x, Y0 = e.Y0 + y, X1 = e.X1 + x, Y1 = e.Y1 + y, Tier = e.Tier,
        Room = e.Room, Item = e.Item, Facing = e.Facing, Template = e.Template, TemplateKey = e.TemplateKey, Note = e.Note,
    }).ToList();

    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, string.Concat(Name.Split(Path.GetInvalidFileNameChars())) + ".json"), JsonSerializer.Serialize(this, ModDefinition.Json));
    }

    public static List<MapStamp> LoadAll(string dir) => !Directory.Exists(dir) ? new()
        : Directory.GetFiles(dir, "*.json").Select(f => { try { return JsonSerializer.Deserialize<MapStamp>(File.ReadAllText(f), ModDefinition.Json); } catch (JsonException) { return null; } })
                   .OfType<MapStamp>().OrderBy(s => s.Name).ToList();
}
