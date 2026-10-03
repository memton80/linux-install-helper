using System.Globalization;
using System.Text;
using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.LinkChecker;

internal static class Report
{
    public static string Build(DistroCatalog catalog, IReadOnlyList<DistroCheckResult> results)
    {
        var errors = results.Count(r => r.Status == CheckStatus.Error);
        var warnings = results.Count(r => r.Status == CheckStatus.Warning);
        var md = new StringBuilder();

        md.AppendLine(errors > 0 ? "## ❌ Catalog link check failed" : "## ✅ Catalog link check passed");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Catalog updated {catalog.Updated} · {results.Count} distributions · {errors} error(s) · {warnings} warning(s) · checked {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        md.AppendLine();
        md.AppendLine("| | Distribution | Image | Size on server | Checksum |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var result in results)
        {
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Icon} | `{result.Distro.Id}` | {result.Resolved?.FileName ?? result.Distro.Image.FileName} | {DistroCheckResult.FormatSize(result.ServerSize)} | {result.SignatureText} |");
        }

        var withNotes = results.Where(r => r.Notes.Count > 0).ToList();
        if (withNotes.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("### Details");
            foreach (var result in withNotes)
            {
                md.AppendLine();
                md.AppendLine(CultureInfo.InvariantCulture, $"**{result.Distro.DisplayName}** (`{result.Distro.Id}`)");
                md.AppendLine();
                foreach (var note in result.Notes)
                {
                    md.AppendLine(CultureInfo.InvariantCulture, $"- {note}");
                }
            }
        }

        return md.ToString();
    }

    public static async Task WriteAsync(string? path, string markdown)
    {
        if (!string.IsNullOrEmpty(path))
        {
            await File.WriteAllTextAsync(path, markdown, new UTF8Encoding(false));
        }

        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(summary))
        {
            await File.AppendAllTextAsync(summary, markdown, new UTF8Encoding(false));
        }
    }
}
