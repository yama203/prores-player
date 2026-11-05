using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace ProResPlayer;

/// <summary>
/// Lossless trimming with the bundled ffmpeg (stream copy, no re-encode).
/// ProRes and other intra-only codecs cut on the exact frame; long-GOP codecs (H.264 / HEVC...)
/// can only start on a keyframe, so the result may begin a little before the IN point.
/// </summary>
public static class Cutter
{
	public static string FfmpegPath => Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");

	public static bool Available => File.Exists(FfmpegPath);

	/// <summary>Codecs where every frame is a keyframe, so a stream copy cuts on the exact frame.</summary>
	public static bool IsIntraOnly(string? codec) =>
		codec != null && new[] { "prores", "dnxhd", "dnxhr", "mjpeg", "png", "qtrle", "rawvideo", "v210", "cineform", "cfhd", "ffv1", "utvideo", "hap" }
			.Any(c => codec.Contains(c, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Copies frames [firstFrame, firstFrame + frameCount) of <paramref name="input"/> to <paramref name="output"/>.
	/// The seek point is a quarter frame past the first frame (the demuxer picks the last keyframe at or
	/// before it, i.e. that frame) and the duration stops half a frame before the next one, so float rounding
	/// never adds or drops a frame. make_zero shifts the first kept packet to t=0 instead of hiding it
	/// behind an edit list.
	/// </summary>
	public static async Task<string?> CutAsync(string input, string output, long firstFrame, long frameCount, double fps)
	{
		string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
		var psi = new ProcessStartInfo(FfmpegPath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
		};
		foreach (string a in new[]
		{
			"-hide_banner", "-nostdin", "-loglevel", "error", "-y",
			"-ss", F((firstFrame + 0.25) / fps),
			"-i", input,
			"-t", F((frameCount - 0.5) / fps),
			"-map", "0:v", "-map", "0:a?", "-map_metadata", "0",
			"-c", "copy", "-avoid_negative_ts", "make_zero",
			output,
		})
			psi.ArgumentList.Add(a);

		using var p = Process.Start(psi)!;
		string err = await p.StandardError.ReadToEndAsync();
		await p.WaitForExitAsync();
		if (p.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 0)
			return null;
		return string.IsNullOrWhiteSpace(err) ? $"ffmpeg が終了コード {p.ExitCode} で失敗しました" : err.Trim();
	}
}
