using System.Diagnostics;
using System.Text;

namespace ProResPlayer;

/// <summary>
/// Development check for the render path: <c>ProResPlayer --selftest &lt;outDir&gt; &lt;alphaClip&gt; [clipToPlay...]</c>.
/// Reads back the view in each background mode, checks frame stepping, and measures playback smoothness.
/// Writes <c>selftest.txt</c> and raw RGBA captures (<c>*.rgba</c>, size in the log) to outDir, then quits.
/// </summary>
internal static class SelfTest
{
	public static async Task Run(MainWindow w, string[] args)
	{
		string outDir = args[0];
		Directory.CreateDirectory(outDir);
		var log = new StringBuilder();
		void Log(string s) { log.AppendLine(s); Console.WriteLine(s); }
		var p = w.PlayerA;

		try
		{
			for (int i = 0; i < 100 && !w.View.IsReady; i++)
				await Task.Delay(50);
			Log($"view ready={w.View.IsReady}");
			// --- alpha / background ---
			await Load(w, args[1]);
			Log($"[alpha] {Path.GetFileName(args[1])}  fps={p.Fps} dur={p.Duration}  hwdec={p.Get("hwdec-current")}  pixfmt={p.Get("video-params/pixelformat")} alpha={p.Get("video-params/alpha")}");
			p.SeekAbsolute(MainWindow.FrameTime(30, p.Fps));
			await Task.Delay(600);
			foreach (string bg in new[] { "checker", "black", "white", "alpha" })
			{
				w.SetBackground(bg);
				await Task.Delay(500);
				await Save(w, outDir, "bg_" + bg, Log);
			}
			w.SetBackground("checker");

			// --- frame stepping (rapid repeats) ---
			p.SeekAbsolute(MainWindow.FrameTime(30, p.Fps));
			await Task.Delay(500);
			long f0 = w.CurrentFrame;
			for (int i = 0; i < 5; i++) { w.Step(1); await Task.Delay(30); }
			await Task.Delay(700);
			long f1 = w.CurrentFrame;
			for (int i = 0; i < 3; i++) { w.Step(-1); await Task.Delay(30); }
			await Task.Delay(700);
			long f2 = w.CurrentFrame;
			Log($"[step] start={f0} +5 -> {f1} (expect {f0 + 5}) -3 -> {f2} (expect {f0 + 2})  {(f1 == f0 + 5 && f2 == f0 + 2 ? "OK" : "NG")}");

			// --- A-B loop: IN at 30, OUT at 40, play; must stay inside ---
			p.SeekAbsolute(MainWindow.FrameTime(30, p.Fps)); await Task.Delay(400);
			w.SetLoopPointForTest("ab-loop-a");
			p.SeekAbsolute(MainWindow.FrameTime(40, p.Fps)); await Task.Delay(400);
			w.SetLoopPointForTest("ab-loop-b");
			p.Paused = false;
			long minF = long.MaxValue, maxF = long.MinValue;
			for (int i = 0; i < 40; i++) { await Task.Delay(50); minF = Math.Min(minF, w.CurrentFrame); maxF = Math.Max(maxF, w.CurrentFrame); }
			p.Paused = true;
			Log($"[loop] IN=30 OUT=40 played frames {minF}..{maxF}  {(minF >= 30 && maxF <= 41 ? "OK" : "NG")}");
			Log($"[info] {w.InfoTextForTest.Replace("\n", " | ")}");

			// --- cut: args[2] if it has "pcm" in its name: IN 30, OUT 40 -> save as, and overwrite a copy ---
			string? pcm = args.Skip(2).FirstOrDefault(a => a.Contains("pcm"));
			if (pcm != null)
			{
				string copy = Path.Combine(outDir, "overwrite_" + Path.GetFileName(pcm));
				File.Copy(pcm, copy, true);
				foreach (var (src, dst) in new[] { (pcm, Path.Combine(outDir, "cut_saveas.mov")), (copy, copy) })
				{
					await Load(w, src);
					p.SeekAbsolute(MainWindow.FrameTime(30, p.Fps)); await Task.Delay(400);
					w.SetLoopPointForTest("ab-loop-a");
					p.SeekAbsolute(MainWindow.FrameTime(40, p.Fps)); await Task.Delay(400);
					w.SetLoopPointForTest("ab-loop-b");
					var (first, last) = w.CutFramesForTest!.Value;
					bool ok = await w.CutToFileAsync(src, dst, first, last - first + 1, p.Fps);
					await Task.Delay(800);
					Log($"[cut] {Path.GetFileName(dst)} frames {first}..{last} ok={ok} reopened={Path.GetFileName(p.Path ?? "")} dur={p.Duration:0.###}");
				}
			}

			// --- frame export at frame 30 of the alpha clip ---
			await Load(w, args[1]);
			w.SetBackground("checker");
			p.SeekAbsolute(MainWindow.FrameTime(30, p.Fps)); await Task.Delay(600);
			Log($"[png] saved={w.SaveFrameTo(Path.Combine(outDir, "frame_save.png"))}");
			w.CopyFrame();
			byte[]? clipPng = ClipboardImage.GetData("public.png");
			if (clipPng != null) File.WriteAllBytes(Path.Combine(outDir, "frame_clip.png"), clipPng);
			Log($"[copy] png={clipPng?.Length ?? 0} bytes tiff={ClipboardImage.DataLength("public.tiff")} bytes");
			var mk = w.OpenMarkup();
			await Task.Delay(1200);
			if (mk != null)
			{
				mk.DrawArrowForTest(new Avalonia.Point(300, 200), new Avalonia.Point(1300, 520));
				await Task.Delay(500);
				File.WriteAllBytes(Path.Combine(outDir, "markup.png"), mk.RenderPng());
				Shot(mk, outDir, "markup_window", Log);
				mk.Close();
				Log("[markup] markup.png");
			}

			// --- compare: args with "422_pcm" as B over the alpha clip as A ---
			string? bClip = args.Skip(2).FirstOrDefault(a => a.Contains("pcm"));
			if (bClip != null)
			{
				await Load(w, args[1]);
				w.OpenBForTest(bClip);
				for (int i = 0; i < 100 && !(w.PlayerB?.Fps > 0); i++) await Task.Delay(50);
				await Task.Delay(500);
				var b = w.PlayerB!;
				w.SetBackground("checker");
				p.Paused = false; b.Paused = false;
				await Task.Delay(2000);
				double da = Math.Abs(p.Time - b.Time) * p.Fps;
				Log($"[compare] playing: A={p.Time:0.000} B={b.Time:0.000} diff={da:0.00} f  {(da <= 1.5 ? "OK" : "NG")}");
				w.Step(1);	// pauses both and lines B up
				await Task.Delay(300);
				for (int i = 0; i < 3; i++) { w.Step(1); await Task.Delay(30); }
				await Task.Delay(800);
				long fa = (long)Math.Round(p.Time * p.Fps), fb = (long)Math.Round(b.Time * b.Fps);
				Log($"[compare] after steps: A frame {fa}, B frame {fb}  {(fa == fb ? "OK" : "NG")}");
				Shot(w, outDir, "compare_side", Log);
				await Task.Delay(1500);
				w.SetCompareForTest("wipe");
				w.SetWipeForTest(0.35);
				await Task.Delay(800);
				Shot(w, outDir, "compare_wipe", Log);
				await Task.Delay(1500);
				w.SetCompareForTest("off");
				await Task.Delay(500);
			}

			w.ShowCutPanelForTest();
			p.SeekAbsolute(MainWindow.FrameTime(30, p.Fps)); await Task.Delay(300);
			w.SetLoopPointForTest("ab-loop-a");
			p.SeekAbsolute(MainWindow.FrameTime(40, p.Fps)); await Task.Delay(300);
			w.SetLoopPointForTest("ab-loop-b");
			await Task.Delay(300);

			// --- whole window (controls only; the GL video comes out blank in this kind of snapshot) ---
			var size = new Avalonia.PixelSize((int)w.Bounds.Width * 2, (int)w.Bounds.Height * 2);
			using (var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(192, 192)))
			{
				bmp.Render(w);
				#pragma warning disable CS0618
				bmp.Save(Path.Combine(outDir, "window.png"));
#pragma warning restore CS0618
			}
			Log("[window] window.png");

			// --- playback smoothness ---
			foreach (string clip in args.Skip(2))
			{
				await Load(w, clip);
				w.SetBackground(clip.Contains("4444") ? "checker" : "black");
				p.Paused = false;
				await Task.Delay(1500);
				long n0 = w.View.NewFrames;
				long d0 = Long(p.Get("frame-drop-count")), dd0 = Long(p.Get("decoder-frame-drop-count")), v0 = Long(p.Get("vo-delayed-frame-count"));
				double t0 = p.Time;
				var sw = Stopwatch.StartNew();
				await Task.Delay(6000);
				await Save(w, outDir, "play_" + Path.GetFileNameWithoutExtension(clip), Log);
				double secs = sw.Elapsed.TotalSeconds;
				long n1 = w.View.NewFrames;
				long d1 = Long(p.Get("frame-drop-count")), dd1 = Long(p.Get("decoder-frame-drop-count")), v1 = Long(p.Get("vo-delayed-frame-count"));
				double t1 = p.Time;
				Log($"[play] {Path.GetFileName(clip)}  {p.Get("width")}x{p.Get("height")} fps={p.Fps}  hwdec={p.Get("hwdec-current")}");
				Log($"       new frames drawn {(n1 - n0) / secs:F2}/s, media advanced {(t1 - t0) / secs:F3}x realtime, " +
					$"dropped(vo)={d1 - d0} dropped(decoder)={dd1 - dd0} delayed={v1 - v0}, view={w.View.PixelSize.W}x{w.View.PixelSize.H}");
				p.Paused = true;
			}
		}
		catch (Exception ex)
		{
			Log("ERROR " + ex);
		}
		File.WriteAllText(Path.Combine(outDir, "selftest.txt"), log.ToString());
		w.Close();
	}

	[System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib")]
	private static extern IntPtr sel_registerName(string name);
	[System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
	private static extern long MsgSendLong(IntPtr obj, IntPtr sel);

	/// <summary>Real screenshot of the window (needs Screen Recording permission for the parent app).</summary>
	private static void Shot(Avalonia.Controls.Window w, string outDir, string name, Action<string> log)
	{
		var h = w.TryGetPlatformHandle();
		if (h == null) { log($"[shot] {name}: no handle ({h?.HandleDescriptor})"); return; }
		long num = MsgSendLong(h.Handle, sel_registerName("windowNumber"));
		string path = Path.Combine(outDir, name + ".png");
		var psi = new ProcessStartInfo("/usr/sbin/screencapture", $"-x -o -l {num} \"{path}\"");
		using var pr = Process.Start(psi)!;
		pr.WaitForExit(10000);
		log($"[shot] {name} ({h.HandleDescriptor} #{num}) exists={File.Exists(path)}");
	}

	private static long Long(string? s) => long.TryParse(s, out long v) ? v : 0;

	private static async Task Load(MainWindow w, string path)
	{
		w.Open(path);
		var p = w.PlayerA;
		p.Paused = true;	// files start playing when opened
		for (int i = 0; i < 100 && !(p.Fps > 0 && p.Duration > 0 && p.Get("video-params/w") != null); i++)
			await Task.Delay(50);
		await Task.Delay(300);
	}

	private static async Task Save(MainWindow w, string outDir, string name, Action<string> log)
	{
		var c = await w.View.CaptureAsync().WaitAsync(TimeSpan.FromSeconds(3));
		if (c == null) { log($"[capture] {name}: failed"); return; }
		File.WriteAllBytes(Path.Combine(outDir, name + ".rgba"), c.Rgba);
		log($"[capture] {name}.rgba {c.Width}x{c.Height}");
	}
}
