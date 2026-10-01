using Eg2.Asura;
using Eg2.Asura.Chunks;

namespace Eg2.ModKit;

/// <summary>
/// Lossless round-trip over every Asura archive in a game install (port of the
/// Python `asura verify`): payload, data objects, RSCF, HTXT, RSFL and fntr must
/// all rebuild byte-for-byte. The Python library passes on 264/264 files.
/// </summary>
public static class InstallVerifier
{
    public sealed record Result(int Ok, int Failed, List<string> Problems);

    public static Result Run(string root, Action<string> log, CancellationToken ct = default,
                             long maxSize = 256L * 1024 * 1024)
    {
        int ok = 0, failed = 0;
        var problems = new List<string>();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".asrpatch", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var info = new System.IO.FileInfo(f);
            if (info.Length < 8 || info.Length > maxSize) continue;
            bool sniff;
            try { sniff = AsuraArchive.Sniff(f); } catch (IOException) { continue; }
            if (!sniff) continue;

            string rel = System.IO.Path.GetRelativePath(root, f);
            try
            {
                var data = File.ReadAllBytes(f);
                var original = AsuraArchive.Decompress(data, out _);
                var a = AsuraArchive.FromBytes(data);
                if (!a.Payload().AsSpan().SequenceEqual(original)) throw new Exception("payload mismatch");
                foreach (var c in a.Chunks)
                {
                    if (c.IsDataObjectTag && c.Body.Length >= ObjectHeader.Size)
                        Same(DataObject.Parse(c.Tag, c.Body).ToBytes(), c.Body, $"object {c.Tag}");
                    switch (c.Tag)
                    {
                        case "RSCF": Same(EmbeddedFile.Parse(c.Body).ToBytes(), c.Body, "RSCF"); break;
                        case "HTXT": Same(TextTable.Parse(c.Body).ToBytes(), c.Body, "HTXT"); break;
                        case "RSFL": Same(ResourceList.Parse(c.Body).ToBytes(), c.Body, "RSFL"); break;
                        case "fntr": Same(FurnitureTable.Parse(c.Body).ToBytes(), c.Body, "fntr"); break;
                    }
                }
                ok++;
                log($"ok    {rel}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                problems.Add($"{rel}: {e.Message}");
                log($"FAIL  {rel}: {e.Message}");
            }
        }
        log($"round-trip: {ok} ok, {failed} failed");
        return new Result(ok, failed, problems);
    }

    static void Same(byte[] rebuilt, byte[] original, string what)
    {
        if (!rebuilt.AsSpan().SequenceEqual(original)) throw new Exception($"{what} mismatch");
    }
}
