using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Eg2.ModManager;

// Startup check against the public releases page. Notify only: the player downloads the new build themselves.
partial class MainForm
{
    const string ReleasesApi = "https://api.github.com/repos/kotmfu/eg2modkitpublic/releases/latest";
    readonly ToolStripStatusLabel _update = new() { IsLink = true, Visible = false };

    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    async Task CheckForUpdate()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Eg2ModManager");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(ReleasesApi));
            var r = doc.RootElement;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return;
            var mine = Assembly.GetEntryAssembly()?.GetName().Version;
            if (mine is null || Norm(latest) <= Norm(mine)) return;

            var name = r.GetProperty("name").GetString() ?? tag;
            var notes = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var url = r.GetProperty("html_url").GetString()!;
            _update.Text = $"Update available: {tag}";
            _update.Visible = true;
            _update.Click += (_, _) => ShowUpdate(name, notes, url);
            Log($"Update available: {name} (you have {Norm(mine)}). {url}");
        }
        catch { }   // offline, rate-limited or API changed: no notice, nothing else to do
    }

    void ShowUpdate(string name, string notes, string url)
    {
        using var f = new Form { Text = name, Size = new Size(640, 480), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ShowInTaskbar = false };
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = notes.ReplaceLineEndings("\r\n") };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(Btn("Close", (_, _) => f.Close()));
        bar.Controls.Add(Btn("Open download page", (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })));
        f.Controls.Add(box);
        f.Controls.Add(bar);
        Theme.Apply(f);
        f.ShowDialog(this);
    }
}
