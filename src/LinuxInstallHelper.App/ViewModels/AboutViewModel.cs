using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;

namespace LinuxInstallHelper.App.ViewModels;

public sealed record ThirdPartyComponent(string Name, string License, Uri Url)
{
    public ThirdPartyComponent(string name, string license, string url)
        : this(name, license, new Uri(url))
    {
    }
}

public sealed partial class AboutViewModel
{
    public AboutViewModel(ILocalizer localizer)
    {
        Version = localizer.Format("About_Version", AppInfo.Version);
    }

    public string Version { get; }

    public string RepositoryUrl => AppInfo.RepositoryUrl;

    public IReadOnlyList<ThirdPartyComponent> Components { get; } =
    [
        new("Windows App SDK / WinUI 3", "MIT", "https://github.com/microsoft/WindowsAppSDK"),
        new(".NET", "MIT", "https://github.com/dotnet/runtime"),
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Microsoft.Extensions.*", "MIT", "https://github.com/dotnet/runtime"),
        new("BouncyCastle.Cryptography", "MIT", "https://github.com/bcgit/bc-csharp"),
        new("NJsonSchema", "MIT", "https://github.com/RicoSuter/NJsonSchema"),
        new("Newtonsoft.Json", "MIT", "https://github.com/JamesNK/Newtonsoft.Json"),
        new("Serilog", "Apache-2.0", "https://github.com/serilog/serilog"),
        new("System.Management", "MIT", "https://github.com/dotnet/runtime"),
    ];

    [RelayCommand]
    private void OpenRepository() => SystemActions.OpenUrl(AppInfo.RepositoryUrl);
}
