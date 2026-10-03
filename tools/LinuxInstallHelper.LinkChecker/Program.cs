using System.Globalization;
using System.Text;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Http;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Verification;
using LinuxInstallHelper.LinkChecker;

// Checks every download of the catalog: HTTP status, size, checksum file, signature.
// Usage: LinuxInstallHelper.LinkChecker [--catalog catalog/distros.json] [--report report.md] [--only id1,id2]
var options = CheckerOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: LinuxInstallHelper.LinkChecker [--catalog <path>] [--report <markdown file>] [--only <id,...>]");
    return 64;
}

var json = await File.ReadAllTextAsync(options.CatalogPath);
DistroCatalog catalog;
try
{
    catalog = (await CatalogValidator.GetDefaultAsync()).ParseAndValidate(json);
}
catch (CatalogException ex)
{
    var message = new StringBuilder($"## ❌ Invalid catalog\n\n{ex.Message}\n\n");
    foreach (var error in ex.Errors)
    {
        message.AppendLine(CultureInfo.InvariantCulture, $"- {error}");
    }

    Console.Error.WriteLine(message);
    await Report.WriteAsync(options.ReportPath, message.ToString());
    return 2;
}

using var http = HttpClientFactory.Create("link-checker");
var checker = new DistroChecker(new ImageResolver(http, new PublicKeyStore(http)), new UrlProbe(http));

var distros = catalog.Distros
    .Where(d => options.Only.Count == 0 || options.Only.Contains(d.Id))
    .ToList();

using var throttle = new SemaphoreSlim(4);
var results = await Task.WhenAll(distros.Select(async distro =>
{
    await throttle.WaitAsync();
    try
    {
        var result = await checker.CheckAsync(distro, CancellationToken.None);
        Console.WriteLine($"{result.Icon} {distro.Id}: {result.Summary}");
        foreach (var note in result.Notes)
        {
            Console.WriteLine($"     {note}");
        }

        return result;
    }
    finally
    {
        throttle.Release();
    }
}));

var markdown = Report.Build(catalog, results);
await Report.WriteAsync(options.ReportPath, markdown);

var errors = results.Count(r => r.Status == CheckStatus.Error);
var warnings = results.Count(r => r.Status == CheckStatus.Warning);
Console.WriteLine();
Console.WriteLine($"{results.Length} distributions checked: {errors} error(s), {warnings} warning(s).");
return errors > 0 ? 1 : 0;
