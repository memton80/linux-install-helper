using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace LinuxInstallHelper.App.Services;

public sealed class ResourceLocalizer : ILocalizer
{
    private readonly ResourceLoader _loader = AppResources.CreateLoader();

    public string Get(string key)
    {
        try
        {
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
