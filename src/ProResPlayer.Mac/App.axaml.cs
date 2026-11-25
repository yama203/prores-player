using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace ProResPlayer;

public partial class App : Application
{
	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var main = new MainWindow(desktop.Args ?? Array.Empty<string>());
			desktop.MainWindow = main;

			// Files opened from Finder (double-click / Open With / drop on the Dock icon) arrive as an
			// activation event, not as command-line arguments.
			if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
				activatable.Activated += (_, e) =>
				{
					if (e is FileActivatedEventArgs f && f.Files.Select(i => i.TryGetLocalPath()).FirstOrDefault(p => p != null) is string path)
					{
						main.Open(path);
						main.Activate();
					}
				};
		}
		base.OnFrameworkInitializationCompleted();
	}
}
