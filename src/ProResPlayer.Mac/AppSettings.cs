using System.Text.Json;

namespace ProResPlayer;

/// <summary>Per-user settings kept between runs in ~/Library/Application Support/ProResPlayer/settings.json.</summary>
public sealed class AppSettings
{
	public bool InfoPanel { get; set; } = true;

	/// <summary>checker / black / white / alpha</summary>
	public string Background { get; set; } = "black";

	/// <summary>0-100</summary>
	public double Volume { get; set; } = 100;

	public bool Mute { get; set; }

	// PRORESPLAYER_SETTINGS overrides the location (used by --selftest so it doesn't touch the real settings).
	private static string FilePath => Environment.GetEnvironmentVariable("PRORESPLAYER_SETTINGS") ?? Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "ProResPlayer", "settings.json");

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
