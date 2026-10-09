using Avalonia;

namespace ContextGuard.App;

internal static class Program
{
    internal static bool SmokeStartup { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        SmokeStartup = args.Contains("--smoke-startup", StringComparer.Ordinal);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new X11PlatformOptions { WmClass = "WithoutContext" }).WithInterFont();
}
