namespace LinuxInstallHelper.Core.Catalog;

/// <summary>The catalog could not be parsed or is not valid.</summary>
public sealed class CatalogException : Exception
{
    public CatalogException(string message, IReadOnlyList<string>? errors = null, Exception? inner = null)
        : base(message, inner)
    {
        Errors = errors ?? [];
    }

    public IReadOnlyList<string> Errors { get; }
}
