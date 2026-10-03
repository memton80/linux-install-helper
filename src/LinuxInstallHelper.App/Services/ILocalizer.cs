namespace LinuxInstallHelper.App.Services;

/// <summary>Access to the localized strings (Strings/*/Resources.resw).</summary>
public interface ILocalizer
{
    /// <summary>Returns the string for <paramref name="key"/>, or the key itself when it is missing.</summary>
    string Get(string key);

    /// <summary>Returns the formatted string for <paramref name="key"/>.</summary>
    string Format(string key, params object?[] args);
}
