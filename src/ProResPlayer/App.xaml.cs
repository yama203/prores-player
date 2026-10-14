using System.Windows;

namespace ProResPlayer;

public partial class App : Application
{
	/// <summary>Files passed on the command line (e.g. from "Open with"): A, and optionally B.</summary>
	public static string[] StartupFiles { get; private set; } = Array.Empty<string>();

	protected override void OnStartup(StartupEventArgs e)
	{
		// --register / --unregister: set up "Open with" without showing the window.
		if (e.Args.Length > 0 && (e.Args[0] == "--register" || e.Args[0] == "--unregister"))
		{
			if (e.Args[0] == "--register") FileAssociation.Register(); else FileAssociation.Unregister();
			Shutdown();
			return;
		}
		StartupFiles = e.Args;
		DispatcherUnhandledException += (_, ex) =>
		{
			string log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProResPlayer_crash.log");
			System.IO.File.AppendAllText(log, $"[{DateTime.Now:s}] {ex.Exception}\n\n");
			MessageBox.Show("エラーが発生しました:\n" + ex.Exception.Message + "\n\n詳細: " + log, "ProRes Player",
				MessageBoxButton.OK, MessageBoxImage.Error);
			ex.Handled = true;
		};
		base.OnStartup(e);
		MainWindow = new MainWindow();
		MainWindow.Show();
	}
}
