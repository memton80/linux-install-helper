using System.Text.RegularExpressions;

namespace LinuxInstallHelper.Core.Verification;

/// <summary>
/// Parser for the SHA-256 checksum files published by distributions:
/// GNU coreutils (<c>hash  file</c>, <c>hash *file</c>) and BSD (<c>SHA256 (file) = hash</c>) formats.
/// Lines for other algorithms and comments are ignored.
/// </summary>
public sealed partial class ChecksumFile
{
    private readonly Dictionary<string, string> _entries;

    private ChecksumFile(Dictionary<string, string> entries, string? unnamedHash)
    {
        _entries = entries;
        UnnamedHash = unnamedHash;
    }

    /// <summary>File name → lowercase SHA-256.</summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    /// <summary>A hash written alone on a line (some <c>.sha256</c> files have no file name).</summary>
    public string? UnnamedHash { get; }

    public IEnumerable<string> FileNames => _entries.Keys;

    public static ChecksumFile Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        string? unnamed = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var bsd = BsdLine().Match(line);
            if (bsd.Success)
            {
                Add(entries, bsd.Groups["name"].Value, bsd.Groups["hash"].Value);
                continue;
            }

            var gnu = GnuLine().Match(line);
            if (gnu.Success)
            {
                Add(entries, gnu.Groups["name"].Value, gnu.Groups["hash"].Value);
                continue;
            }

            var alone = HashOnlyLine().Match(line);
            if (alone.Success)
            {
                unnamed ??= alone.Groups["hash"].Value.ToLowerInvariant();
            }
        }

        return new ChecksumFile(entries, unnamed);
    }

    /// <summary>Returns the hash for <paramref name="fileName"/> (falls back to an unnamed hash when it is the only entry).</summary>
    public string? Find(string fileName)
    {
        if (_entries.TryGetValue(fileName, out var hash))
        {
            return hash;
        }

        return _entries.Count == 0 ? UnnamedHash : null;
    }

    private static void Add(Dictionary<string, string> entries, string name, string hash)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length > 0)
        {
            entries.TryAdd(normalized, hash.ToLowerInvariant());
        }
    }

    /// <summary>Strips leading <c>./</c>, directories and surrounding whitespace.</summary>
    internal static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        var slash = trimmed.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    [GeneratedRegex(@"^SHA256\s*\((?<name>.+)\)\s*=\s*(?<hash>[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex BsdLine();

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GnuLine();

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex HashOnlyLine();
}
