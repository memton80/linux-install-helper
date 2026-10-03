using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Download;
using LinuxInstallHelper.Core.Verification;
using LinuxInstallHelper.Core.Writing;

namespace LinuxInstallHelper.App.Services;

public sealed record ErrorDescription(string Title, string Message, string Details);

/// <summary>Turns exceptions into clear, localized messages (technical details kept apart).</summary>
public sealed class ErrorDescriber
{
    private readonly ILocalizer _localizer;

    public ErrorDescriber(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public ErrorDescription Describe(Exception exception)
    {
        var key = exception switch
        {
            VerificationException v => "Error_" + v.Failure,
            DownloadException d => "Error_" + d.Failure,
            UsbWriteException u => "Error_" + u.Failure,
            CatalogException => "Error_Catalog",
            UnauthorizedAccessException => "Error_AccessDenied",
            IOException => "Error_IO",
            HttpRequestException or TimeoutException => "Error_Network",
            _ => "Error_Unexpected",
        };

        return new ErrorDescription(
            _localizer.Get(key + "_Title"),
            _localizer.Get(key),
            $"{exception.GetType().Name}: {exception.Message}");
    }
}
