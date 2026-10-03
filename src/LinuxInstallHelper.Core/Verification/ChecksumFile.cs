using System.Text.RegularExpressions;

namespace LinuxInstallHelper.Core.Verification;

public enum HashAlgorithmKind
{
    Sha256,
    Sha512,
}

/// <param name="FileName">File name (directories stripped), empty for a hash written alone.</param>
/// <param name="Algorithm">Algorithm deduced from the hash length.</param>
/// <param name="Hash">Lowercase hexadecimal hash.</param>
public sealed record ChecksumEntry(string FileName, HashAlgorithmKind Algorithm, string Hash);

/// <summary>
/// Parser for the checksum files published by distributions: GNU coreutils (<c>hash  file</c>,
/// <c>hash *file</c>) and BSD (<c>SHA256 (file) = hash</c>) formats, SHA-256 or SHA-512.
/// When a file is listed with both algorithms, SHA-256 is kept. Other lines are ignored.
/// </summary>
public sealed partial class ChecksumFile
{
    private readonly Dictionary<string, ChecksumEntry> _entries;

    private ChecksumFile(Dictionary<string, ChecksumEntry> entries, ChecksumEntry? unnamed)
    {
        _entries = entries;
        UnnamedEntry = unnamed;
    }

    /// <summary>Entries by file name.</summary>
    public IReadOnlyDictionary<string, ChecksumEntry> Entries => _entries;

    /// <summary>A hash written alone on a line (some <c>.sha256</c> files have no file name).</summary>
    public ChecksumEntry? UnnamedEntry { get; }

    public IEnumerable<string> FileNames => _entries.Keys;

    public static ChecksumFile Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var entries = new Dictionary<string, ChecksumEntry>(StringComparer.Ordinal);
        ChecksumEntry? unnamed = null;

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
                var algorithm = bsd.Groups["alg"].Value == "SHA512" ? HashAlgorithmKind.Sha512 : HashAlgorithmKind.Sha256;
                if (bsd.Groups["hash"].Length == ExpectedLength(algorithm))
                {
                    Add(entries, bsd.Groups["name"].Value, algorithm, bsd.Groups["hash"].Value);
                }

                continue;
            }

            var gnu = GnuLine().Match(line);
            if (gnu.Success)
            {
                Add(entries, gnu.Groups["name"].Value, AlgorithmOf(gnu.Groups["hash"].Value), gnu.Groups["hash"].Value);
                continue;
            }

            var alone = HashOnlyLine().Match(line);
            if (alone.Success)
            {
                var hash = alone.Groups["hash"].Value;
                unnamed ??= new ChecksumEntry(string.Empty, AlgorithmOf(hash), hash.ToLowerInvariant());
            }
        }

        return new ChecksumFile(entries, unnamed);
    }

    /// <summary>Returns the entry for <paramref name="fileName"/> (falls back to an unnamed hash when it is the only content).</summary>
    public ChecksumEntry? Find(string fileName)
    {
        if (_entries.TryGetValue(fileName, out var entry))
        {
            return entry;
        }

        return _entries.Count == 0 ? UnnamedEntry : null;
    }

    public static int ExpectedLength(HashAlgorithmKind algorithm) => algorithm == HashAlgorithmKind.Sha512 ? 128 : 64;

    private static HashAlgorithmKind AlgorithmOf(string hash) => hash.Length == 128 ? HashAlgorithmKind.Sha512 : HashAlgorithmKind.Sha256;

    private static void Add(Dictionary<string, ChecksumEntry> entries, string name, HashAlgorithmKind algorithm, string hash)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length == 0)
        {
            return;
        }

        var entry = new ChecksumEntry(normalized, algorithm, hash.ToLowerInvariant());
        if (!entries.TryGetValue(normalized, out var existing) || (existing.Algorithm == HashAlgorithmKind.Sha512 && algorithm == HashAlgorithmKind.Sha256))
        {
            entries[normalized] = entry;
        }
    }

    /// <summary>Strips leading <c>./</c>, directories and surrounding whitespace.</summary>
    internal static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        var slash = trimmed.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    [GeneratedRegex(@"^(?<alg>SHA256|SHA512)\s*\((?<name>.+)\)\s*=\s*(?<hash>[0-9a-fA-F]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BsdLine();

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{128}|[0-9a-fA-F]{64})\s+\*?(?<name>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GnuLine();

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{128}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex HashOnlyLine();
}
