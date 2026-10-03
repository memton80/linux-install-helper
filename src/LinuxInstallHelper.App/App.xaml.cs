using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.App.Views;
using LinuxInstallHelper.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;

namespace LinuxInstallHelper.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

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

        Services = ConfigureServices(paths);
        UnhandledException += OnUnhandledException;
        Log.Information("Linux Install Helper {Version} starting", AppInfo.Version);
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
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices(AppPaths paths)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        services.AddSingleton(paths);

        // UI services
        services.AddSingleton<ILocalizer, ResourceLocalizer>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception: {Message}", e.Message);
        Log.CloseAndFlush();
    }
}
