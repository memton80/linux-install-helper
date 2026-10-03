namespace LinuxInstallHelper.LinkChecker;

internal sealed record CheckerOptions(string CatalogPath, string? ReportPath, IReadOnlySet<string> Only)
{
    public static CheckerOptions? Parse(string[] args)
    {
        var catalog = Path.Combine("catalog", "distros.json");
        string? report = null;
        var only = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--catalog" when i + 1 < args.Length:
                    catalog = args[++i];
                    break;
                case "--report" when i + 1 < args.Length:
                    report = args[++i];
                    break;
                case "--only" when i + 1 < args.Length:
                    only.UnionWith(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                default:
                    return null;
            }
        }

        return File.Exists(catalog) ? new CheckerOptions(catalog, report, only) : null;
    }
}
