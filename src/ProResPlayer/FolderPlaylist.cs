using System.IO;
using System.Runtime.InteropServices;

namespace ProResPlayer;

/// <summary>
/// The movie files in the folder of the current file, in Explorer's name order (numbers compared as
/// numbers, so "shot2" comes before "shot10"), for stepping to the previous / next file.
/// </summary>
public sealed class FolderPlaylist
{
	private List<string> _files = new();

	public int Index { get; private set; } = -1;
	public int Count => _files.Count;

	public static bool IsMovie(string path) =>
		FileAssociation.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

	/// <summary>Rescans the folder of <paramref name="current"/> (picks up files added since the last scan).</summary>
	public void Update(string current)
	{
		string? dir = Path.GetDirectoryName(current);
		if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
		{
			_files = new List<string> { current };
			Index = 0;
			return;
		}
		try
		{
			_files = Directory.EnumerateFiles(dir).Where(IsMovie).ToList();
		}
		catch (Exception)
		{
			_files = new List<string>();
		}
		_files.Sort((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
		Index = _files.FindIndex(f => string.Equals(f, current, StringComparison.OrdinalIgnoreCase));
		if (Index < 0)
		{
			_files.Add(current);
			Index = _files.Count - 1;
		}
	}

	/// <summary>The file <paramref name="delta"/> positions away, or null at either end.</summary>
	public string? Neighbor(int delta)
	{
		int i = Index + delta;
		return i >= 0 && i < _files.Count ? _files[i] : null;
	}

	[DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
	private static extern int StrCmpLogicalW(string a, string b);
}
