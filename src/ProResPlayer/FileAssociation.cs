using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ProResPlayer;

/// <summary>
/// Adds ProRes Player to "Open with" for movie files (per user, HKCU, no admin needed).
/// Windows does not let apps make themselves the default; the user picks it once in the Open with dialog.
/// </summary>
public static class FileAssociation
{
	private const string AppKey = @"Software\Classes\Applications\ProResPlayer.exe";
	private const string ProgId = "ProResPlayer.Movie";
	public static readonly string[] Extensions = { ".mov", ".mp4", ".m4v", ".mxf", ".mkv", ".avi", ".webm" };

	public static void Register()
	{
		string exe = Environment.ProcessPath ?? throw new InvalidOperationException("process path unknown");
		string command = $"\"{exe}\" \"%1\"";

		using (RegistryKey app = Registry.CurrentUser.CreateSubKey(AppKey))
		{
			app.SetValue("FriendlyAppName", "ProRes Player");
			using (RegistryKey cmd = app.CreateSubKey(@"shell\open\command"))
				cmd.SetValue("", command);
			using RegistryKey types = app.CreateSubKey("SupportedTypes");
			foreach (string ext in Extensions)
				types.SetValue(ext, "");
		}

		using (RegistryKey prog = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
		{
			prog.SetValue("", "Movie (ProRes Player)");
			using (RegistryKey icon = prog.CreateSubKey("DefaultIcon"))
				icon.SetValue("", $"\"{exe}\",0");
			using RegistryKey cmd = prog.CreateSubKey(@"shell\open\command");
			cmd.SetValue("", command);
		}

		foreach (string ext in Extensions)
		{
			using RegistryKey owp = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
			owp.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
		}

		NotifyShell();
	}

	public static void Unregister()
	{
		Registry.CurrentUser.DeleteSubKeyTree(AppKey, false);
		Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false);
		foreach (string ext in Extensions)
		{
			using RegistryKey? owp = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
			owp?.DeleteValue(ProgId, false);
		}
		NotifyShell();
	}

	public static bool IsRegistered()
	{
		using RegistryKey? k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId);
		return k != null;
	}

	private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

	[DllImport("shell32.dll")]
	private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
