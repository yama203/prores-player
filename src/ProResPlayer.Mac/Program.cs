using Avalonia;

namespace ProResPlayer;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

	// mpv's render API draws with OpenGL, so ask Avalonia for OpenGL rather than Metal on macOS.
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UsePlatformDetect()
		.With(new AvaloniaNativePlatformOptions { RenderingMode = new[] { AvaloniaNativeRenderingMode.OpenGl } });
}
