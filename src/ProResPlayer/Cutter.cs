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
	/// <list type="bullet">
	/// <item>The seek point is the first frame's time rounded up to the microsecond, so the demuxer lands
	/// on that frame (the last keyframe at or before it) and float rounding never picks the previous one.</item>
	/// <item>Video and audio are read as two inputs, each with its own input-side duration: video stops half
	/// a frame before the next frame, audio runs the exact range. Output-side limits (-t / -frames:v) would
	/// go through ffmpeg's sync queue, which cuts the audio at the last whole audio frame before the video
	/// ends.</item>
	/// <item>PCM audio is decoded and re-encoded to the same PCM format: bit-identical, but trimmed to the
	/// sample instead of to the packet. Compressed audio (AAC...) is copied.</item>
	/// <item>make_zero shifts the first kept packet to t=0 instead of hiding it behind an edit list.</item>
	/// </list>
	/// </summary>
	public static async Task<string?> CutAsync(string input, string output, long firstFrame, long frameCount, double fps, string? audioCodec)
	{
		string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
		string start = F(Math.Ceiling(firstFrame / fps * 1e6) / 1e6);
		var psi = new ProcessStartInfo(FfmpegPath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
		};
		var args = new List<string>
		{
			"-hide_banner", "-nostdin", "-loglevel", "error", "-y",
			"-ss", start, "-t", F((frameCount - 0.5) / fps), "-i", input,
			"-ss", start, "-t", F(frameCount / fps), "-i", input,
			"-map", "0:v", "-map", "1:a?", "-map_metadata", "0",
			"-c", "copy",
		};
		if (audioCodec != null && audioCodec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
			args.AddRange(new[] { "-c:a", audioCodec });
		args.AddRange(new[] { "-avoid_negative_ts", "make_zero", output });
		foreach (string a in args)
			psi.ArgumentList.Add(a);

		using var p = Process.Start(psi)!;
		string err = await p.StandardError.ReadToEndAsync();
		await p.WaitForExitAsync();
		if (p.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 0)
			return null;
		return string.IsNullOrWhiteSpace(err) ? $"ffmpeg が終了コード {p.ExitCode} で失敗しました" : err.Trim();
	}
}
