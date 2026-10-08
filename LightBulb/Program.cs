using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Threading;
using LightBulb.PlatformInterop;

namespace LightBulb;

public static class Program
{
    private static Assembly Assembly { get; } = Assembly.GetExecutingAssembly();

    public static string Name { get; } = Assembly.GetName().Name ?? "LightBulb";

    public static Version Version { get; } = Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string VersionString { get; } =
        Version.ToString(4)
        + " (upstream "
        + Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpstreamRef")
            ?.Value
        + ")";

    public static bool IsDevelopmentBuild { get; } = Version.Major is <= 0 or >= 999;

    public static string ExecutableDirPath { get; } = AppContext.BaseDirectory;

    public static string ExecutableFilePath { get; } =
        Path.ChangeExtension(Assembly.Location, "exe");

    public static string ProjectUrl { get; } = "https://github.com/Tyrrrz/LightBulb";

    public static string ProjectReleasesUrl { get; } = $"{ProjectUrl}/releases";

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(
                new Win32PlatformOptions
                {
                    // Use redirection surface composition to avoid Avalonia's WinUI Composition
                    // renderer (WinUiCompositorConnection) from ticking endlessly via dcomp.dll
                    // when the application is idle, which would otherwise wake up dwm.exe
                    // continuously even when monitors are powered off.
                    CompositionMode = [Win32CompositionMode.RedirectionSurface],
                    RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
                }
            )
            .LogToTrace();

    [STAThread]
    public static int Main(string[] args)
    {
        var exitEventName = $"Local\\{Name}_Exit";
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            if (!EventWaitHandle.TryOpenExisting(exitEventName, out var existingExitEvent))
                return 1;
            using (existingExitEvent)
                existingExitEvent.Set();
            return 0;
        }
        // Ensure only one instance of the app is running at a time
        using var identityMutex = new Mutex(
            true,
            $"{Name}_Identity",
            out var isOnlyRunningInstance
        );

        if (!isOnlyRunningInstance)
            return 1;

        using var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, exitEventName);
        var exitRegistration = ThreadPool.RegisterWaitForSingleObject(
            exitEvent,
            (_, _) => Dispatcher.UIThread.Post(() => App.Shutdown()),
            null,
            Timeout.Infinite,
            true
        );

        // Build and run the app
        var builder = BuildAvaloniaApp();

        try
        {
            return builder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            MessageBox.ShowError("Fatal Error", ex.ToString());
            throw;
        }
        finally
        {
            exitRegistration.Unregister(null);
            // Clean up after application shutdown
            if (builder.Instance is IDisposable disposableApp)
                disposableApp.Dispose();
        }
    }
}
