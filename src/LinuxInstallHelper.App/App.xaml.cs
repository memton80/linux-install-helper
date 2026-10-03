using System.Globalization;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.App.ViewModels;
using LinuxInstallHelper.Core;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Disks.Windows;
using LinuxInstallHelper.Core.Download;
using LinuxInstallHelper.Core.Http;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Verification;
using LinuxInstallHelper.Core.Workflow;
using LinuxInstallHelper.Core.Writing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;

namespace LinuxInstallHelper.App;

public partial class App : Application
{
    private readonly StartupOptions _startup;
    private MainWindow? _window;

    public App()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(paths.Logs, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
        Log.Information("Linux Install Helper {Version} starting on {OS}", AppInfo.Version, Environment.OSVersion);

        _startup = StartupOptions.Parse(Environment.GetCommandLineArgs());
        var settings = new SettingsStore(paths);
        ApplyLanguage(_startup.Language ?? settings.Current.Language);

        // Must run after the language is chosen: resources are loaded by InitializeComponent.
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        Services = ConfigureServices(paths, settings);
    }

    /// <summary>Application-wide service provider.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>The single main window.</summary>
    public static MainWindow MainWindow => ((App)Current)._window
        ?? throw new InvalidOperationException("The main window is not created yet.");

    public static T GetService<T>()
        where T : class
        => Services.GetRequiredService<T>();

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = Services.GetRequiredService<MainWindow>();
        _window.Closed += (_, _) => Log.CloseAndFlush();

        var settings = Services.GetRequiredService<ISettingsStore>().Current;
        Services.GetRequiredService<IThemeService>().Apply(_startup.Theme ?? settings.Theme);
        _window.Start(_startup.Page ?? PageKeys.Distros);
        _window.Activate();
    }

    private static void ApplyLanguage(string? language)
    {
        if (language is null)
        {
            return;
        }

        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply the language {Language}", language);
        }
    }

    private static ServiceProvider ConfigureServices(AppPaths paths, SettingsStore settings)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        services.AddSingleton(paths);
        services.AddSingleton<ISettingsStore>(settings);
        services.AddSingleton(_ => HttpClientFactory.Create(AppInfo.Version));

        // Core
        services.AddSingleton<ICatalogService>(sp => new CatalogService(
            sp.GetRequiredService<HttpClient>(), paths, null, sp.GetRequiredService<ILogger<CatalogService>>()));
        services.AddSingleton<IPublicKeyStore>(sp => new PublicKeyStore(
            sp.GetRequiredService<HttpClient>(), paths, null, sp.GetRequiredService<ILogger<PublicKeyStore>>()));
        services.AddSingleton<IImageResolver, ImageResolver>();
        services.AddSingleton<IUrlProbe>(sp => new UrlProbe(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IDownloader>(sp => new ResumableDownloader(
            sp.GetRequiredService<HttpClient>(), null, sp.GetRequiredService<ILogger<ResumableDownloader>>()));
        services.AddSingleton<IImageVerifier, ImageVerifier>();
        services.AddSingleton<IDiskService, WindowsDiskService>();
        services.AddSingleton<IRawDiskAccess, WindowsRawDiskAccess>();
        services.AddSingleton<IDiskEjector, WindowsDiskEjector>();
        services.AddSingleton<IDiskFormatter, DiskpartFormatter>();
        services.AddSingleton<IUsbWriter>(sp => new RawDiskWriter(
            sp.GetRequiredService<IDiskService>(),
            sp.GetRequiredService<IRawDiskAccess>(),
            null,
            sp.GetRequiredService<ILogger<RawDiskWriter>>()));
        services.AddSingleton<ICreationPipeline, CreationPipeline>();

        // UI services
        services.AddSingleton<ILocalizer, ResourceLocalizer>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<AppBusyState>();
        services.AddSingleton<WizardState>();
        services.AddSingleton<DisplayFormatter>();
        services.AddSingleton<ErrorDescriber>();
        services.AddSingleton<DriveScanner>();
        services.AddSingleton<MainWindow>();

        // View models (the distributions list keeps its state while navigating)
        services.AddSingleton<DistrosViewModel>();
        services.AddTransient<LocalIsoViewModel>();
        services.AddTransient<DriveViewModel>();
        services.AddTransient<ProgressViewModel>();
        services.AddTransient<DoneViewModel>();
        services.AddTransient<RestoreViewModel>();
        services.AddTransient<GuideViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();

        return services.BuildServiceProvider();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception: {Message}", e.Message);
        Log.CloseAndFlush();
    }

    /// <summary>
    /// Developer options: <c>--page Settings</c>, <c>--theme dark</c>, <c>--lang fr-FR</c> (used by the CI to take screenshots).
    /// </summary>
    private sealed record StartupOptions(string? Page, AppTheme? Theme, string? Language)
    {
        public static StartupOptions Parse(string[] args)
        {
            string? Value(string name)
            {
                var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            AppTheme? theme = Enum.TryParse<AppTheme>(Value("--theme"), ignoreCase: true, out var t) ? t : null;
            var language = Value("--lang");
            return new StartupOptions(
                Value("--page"),
                theme,
                language is not null && UserSettings.SupportedLanguages.Contains(language) ? language : null);
        }
    }
}
