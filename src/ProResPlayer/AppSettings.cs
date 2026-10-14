using System.IO;
using System.Text.Json;

namespace ProResPlayer;

/// <summary>Per-user settings kept between runs in %APPDATA%\ProResPlayer\settings.json.</summary>
public sealed class AppSettings
{
	public bool InfoPanel { get; set; } = true;

	/// <summary>checker / black / white / alpha</summary>
	public string Background { get; set; } = "black";

	private static string FilePath =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProResPlayer", "settings.json");

	public static AppSettings Load()
	{
		try
		{
			if (File.Exists(FilePath))
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
		}
		catch (Exception)
		{
			// A broken or unreadable file just falls back to defaults.
		}
		return new AppSettings();
	}

	public void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception)
		{
			// Not being able to save settings must never break playback.
		}
	}
}
