using System.Globalization;

namespace ProResPlayer;

/// <summary>
/// The movie files in the folder of the current file, in Finder's name order (numbers compared as
/// numbers, so "shot2" comes before "shot10"), for stepping to the previous / next file.
/// </summary>
public sealed class FolderPlaylist
{
	public static readonly string[] Extensions = { ".mov", ".mp4", ".m4v", ".mxf", ".mkv", ".avi", ".webm" };

	private List<string> _files = new();

	public int Index { get; private set; } = -1;
	public int Count => _files.Count;

	public static bool IsMovie(string path) =>
		Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

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
			// Skip AppleDouble files ("._name.mov") that macOS leaves on non-APFS volumes.
			_files = Directory.EnumerateFiles(dir).Where(f => IsMovie(f) && !Path.GetFileName(f).StartsWith("._")).ToList();
		}
		catch (Exception)
		{
			_files = new List<string>();
		}
		_files.Sort((a, b) => CompareNatural(Path.GetFileName(a), Path.GetFileName(b)));
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

	/// <summary>Name order with digit runs compared as numbers (Finder / Explorer style).</summary>
	public static int CompareNatural(string a, string b)
	{
		var cmp = CultureInfo.CurrentCulture.CompareInfo;
		int i = 0, j = 0;
		while (i < a.Length && j < b.Length)
		{
			if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
			{
				int si = i, sj = j;
				while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
				while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
				string na = a[si..i].TrimStart('0'), nb = b[sj..j].TrimStart('0');
				if (na.Length != nb.Length)
					return na.Length.CompareTo(nb.Length);
				int c = string.CompareOrdinal(na, nb);
				if (c != 0)
					return c;
				continue;
			}
			int ei = i, ej = j;
			while (ei < a.Length && !char.IsAsciiDigit(a[ei])) ei++;
			while (ej < b.Length && !char.IsAsciiDigit(b[ej])) ej++;
			int t = cmp.Compare(a, i, ei - i, b, j, ej - j, CompareOptions.IgnoreCase);
			if (t != 0)
				return t;
			i = ei;
			j = ej;
		}
		return (a.Length - i).CompareTo(b.Length - j);
	}
}
