using System.Globalization;
using System.Text.RegularExpressions;
using NJsonSchema;

namespace LinuxInstallHelper.Core.Catalog;

/// <summary>
/// Validates a catalog against <c>distros.schema.json</c>, then applies the rules a JSON schema
/// cannot express (HTTPS only, unique ids, valid regular expressions, coherent signatures...).
/// </summary>
public sealed class CatalogValidator
{
    private static readonly Regex Sha256Regex = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex FingerprintRegex = new("^[0-9A-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex IdRegex = new("^[a-z0-9][a-z0-9-]{1,48}$", RegexOptions.CultureInvariant);
    private static readonly Regex ColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim DefaultLock = new(1, 1);
    private static CatalogValidator? _default;

    private readonly JsonSchema _schema;

    private CatalogValidator(JsonSchema schema)
    {
        _schema = schema;
    }

    /// <summary>Validator using the schema embedded in this assembly.</summary>
    public static async Task<CatalogValidator> GetDefaultAsync()
    {
        if (_default is not null)
        {
            return _default;
        }

        await DefaultLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return _default ??= await CreateAsync(EmbeddedResources.ReadSchemaJson()).ConfigureAwait(false);
        }
        finally
        {
            DefaultLock.Release();
        }
    }

    public static async Task<CatalogValidator> CreateAsync(string schemaJson)
        => new(await JsonSchema.FromJsonAsync(schemaJson).ConfigureAwait(false));

    /// <summary>Parses and fully validates a catalog. Throws <see cref="CatalogException"/> when it is not valid.</summary>
    public DistroCatalog ParseAndValidate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        IReadOnlyList<string> schemaErrors;
        try
        {
            schemaErrors = _schema.Validate(json).Select(e => e.ToString()).ToList();
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or FormatException)
        {
            throw new CatalogException($"The catalog is not valid JSON: {ex.Message}", [ex.Message], ex);
        }

        if (schemaErrors.Count > 0)
        {
            throw new CatalogException("The catalog does not match distros.schema.json.", schemaErrors);
        }

        var catalog = CatalogSerializer.Deserialize(json);
        var errors = ValidateSemantics(catalog);
        if (errors.Count > 0)
        {
            throw new CatalogException("The catalog contains invalid entries.", errors);
        }

        return catalog;
    }

    /// <summary>Rules that the JSON schema cannot express. Returns an empty list when everything is fine.</summary>
    public static IReadOnlyList<string> ValidateSemantics(DistroCatalog catalog)
    {
        var errors = new List<string>();

        if (catalog.SchemaVersion != DistroCatalog.SupportedSchemaVersion)
        {
            errors.Add($"Unsupported schemaVersion {catalog.SchemaVersion} (expected {DistroCatalog.SupportedSchemaVersion}).");
        }

        if (!DateOnly.TryParseExact(catalog.Updated, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add($"'updated' is not a valid date: '{catalog.Updated}'.");
        }

        if (catalog.Distros.Count == 0)
        {
            errors.Add("The catalog does not contain any distribution.");
        }

        foreach (var duplicate in catalog.Distros.GroupBy(d => d.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate id '{duplicate.Key}'.");
        }

        foreach (var distro in catalog.Distros)
        {
            ValidateDistro(distro, errors);
        }

        return errors;
    }

    private static void ValidateDistro(Distro distro, List<string> errors)
    {
        var prefix = $"[{distro.Id}]";
        void Error(string message) => errors.Add($"{prefix} {message}");

        if (!IdRegex.IsMatch(distro.Id))
        {
            Error("invalid id.");
        }

        if (!DistroFamilies.All.Contains(distro.Family, StringComparer.Ordinal))
        {
            Error($"unknown family '{distro.Family}'.");
        }

        foreach (var category in distro.Categories.Where(c => !DistroCategories.All.Contains(c, StringComparer.Ordinal)))
        {
            Error($"unknown category '{category}'.");
        }

        if (distro.Architecture is not (DistroArchitectures.X64 or DistroArchitectures.Arm64))
        {
            Error($"unknown architecture '{distro.Architecture}'.");
        }

        if (!ColorRegex.IsMatch(distro.Color))
        {
            Error($"invalid color '{distro.Color}'.");
        }

        if (!IsHttpsUrl(distro.Homepage))
        {
            Error("homepage must be an absolute https:// URL.");
        }

        ValidateImage(distro.Image, Error);
    }

    private static void ValidateImage(DistroImage image, Action<string> error)
    {
        if (image.Size <= 0)
        {
            error("image.size must be positive.");
        }

        if (image.Urls.Count == 0)
        {
            error("image.urls must contain at least one URL.");
        }

        foreach (var url in image.Urls.Where(u => !IsHttpsUrl(u)))
        {
            error($"image URL is not an absolute https:// URL: '{url}'.");
        }

        if (image.Urls.Any(u => u.Contains(DistroImage.FileNamePlaceholder, StringComparison.Ordinal)))
        {
            error("image.urls cannot use the {fileName} placeholder.");
        }

        if (image.Sha256 is not null && !Sha256Regex.IsMatch(image.Sha256))
        {
            error("image.sha256 must be 64 hexadecimal characters.");
        }

        var resolve = image.Resolve;
        var usesChecksumPattern = resolve?.Type == ResolveTypes.ChecksumPattern;
        var usesJson = resolve?.Type == ResolveTypes.Json;

        if (image.Sha256 is null && image.Checksum is null && !usesJson)
        {
            error("image needs 'sha256', 'checksum' or a 'json' resolver to be verifiable.");
        }

        if (image.Checksum is not null)
        {
            if (!IsHttpsUrl(image.Checksum.Url.Replace(DistroImage.FileNamePlaceholder, image.FileName, StringComparison.Ordinal)))
            {
                error("image.checksum.url must be an absolute https:// URL.");
            }

            if (usesChecksumPattern && image.Checksum.Url.Contains(DistroImage.FileNamePlaceholder, StringComparison.Ordinal))
            {
                error("image.checksum.url cannot use {fileName} together with a checksum-pattern resolver.");
            }
        }

        if (resolve is not null)
        {
            ValidateResolve(image, resolve, error);
        }

        if (image.Signature is not null)
        {
            ValidateSignature(image, image.Signature, error);
        }
    }

    private static void ValidateResolve(DistroImage image, ImageResolveRule resolve, Action<string> error)
    {
        switch (resolve.Type)
        {
            case ResolveTypes.ChecksumPattern:
                if (image.Checksum is null)
                {
                    error("a checksum-pattern resolver needs image.checksum.");
                }

                if (string.IsNullOrWhiteSpace(resolve.Pattern))
                {
                    error("a checksum-pattern resolver needs a pattern.");
                    break;
                }

                try
                {
                    var regex = new Regex(resolve.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    if (!regex.IsMatch(image.FileName))
                    {
                        error($"image.fileName '{image.FileName}' does not match the resolve pattern.");
                    }
                }
                catch (ArgumentException ex)
                {
                    error($"invalid resolve pattern: {ex.Message}");
                }

                break;

            case ResolveTypes.Json:
                if (!IsHttpsUrl(resolve.Url))
                {
                    error("a json resolver needs an absolute https:// url.");
                }

                if (string.IsNullOrWhiteSpace(resolve.UrlField) || string.IsNullOrWhiteSpace(resolve.Sha256Field))
                {
                    error("a json resolver needs urlField and sha256Field.");
                }

                break;

            default:
                error($"unknown resolver type '{resolve.Type}'.");
                break;
        }
    }

    private static void ValidateSignature(DistroImage image, SignatureSource signature, Action<string> error)
    {
        if (signature.Kind is not (SignatureKinds.Detached or SignatureKinds.ClearSigned))
        {
            error($"unknown signature kind '{signature.Kind}'.");
        }

        if (signature.Target is not (SignatureTargets.Checksum or SignatureTargets.Image))
        {
            error($"unknown signature target '{signature.Target}'.");
        }

        if (signature.Kind == SignatureKinds.Detached)
        {
            var url = signature.Url?.Replace(DistroImage.FileNamePlaceholder, image.FileName, StringComparison.Ordinal);
            if (!IsHttpsUrl(url))
            {
                error("a detached signature needs an absolute https:// url.");
            }
        }

        if (signature.Kind == SignatureKinds.ClearSigned && signature.Target != SignatureTargets.Checksum)
        {
            error("a clearsigned signature can only cover the checksum file.");
        }

        if (signature.Target == SignatureTargets.Checksum && image.Checksum is null)
        {
            error("a signature of the checksum file needs image.checksum.");
        }

        if (signature.Fingerprints.Count == 0)
        {
            error("signature.fingerprints must contain at least one fingerprint.");
        }

        foreach (var fingerprint in signature.Fingerprints.Where(f => !FingerprintRegex.IsMatch(f)))
        {
            error($"invalid fingerprint '{fingerprint}' (40 uppercase hexadecimal characters expected).");
        }
    }

    internal static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && !string.IsNullOrEmpty(uri.Host);
}
