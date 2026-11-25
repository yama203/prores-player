using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ProResPlayer;

public partial class MainWindow : Window
{
	private enum CompareMode { Off, Side, Wipe }

	private const double Gap = 2;	// divider between A and B side by side, in DIPs

	private readonly MpvPlayer _a;
	private MpvPlayer? _b;
	private string? _pathB, _pendingB, _infoPathB;
	private CompareMode _compare = CompareMode.Off;
	private readonly DispatcherTimer _timer;
	private readonly DispatcherTimer _statusTimer;
	private readonly AppSettings _settings = AppSettings.Load();
	private readonly FolderPlaylist _playlist = new();
	private string _background = "black";
	private string? _infoPath;
	private string? _pendingOpen;
	private long? _stepTarget;
	private DateTime _stepTime;
	private bool _resumeAfterScrub;
	private bool _muted;
	private bool _initialized;
	private string? _lastSaveDir;

	// macOS menu bar items whose check marks follow the state.
	private readonly NativeMenuItem MenuChecker = new("背景: 市松模様") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuBlack = new("背景: 黒") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuWhite = new("背景: 白") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuAlpha = new("アルファのみ") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuInfo = new("情報パネル") { ToggleType = MenuItemToggleType.CheckBox };
	private readonly NativeMenuItem MenuLoop = new("ループ再生") { ToggleType = MenuItemToggleType.CheckBox };
	private readonly NativeMenuItem MenuCmpOff = new("オフ") { ToggleType = MenuItemToggleType.Radio, IsChecked = true };
	private readonly NativeMenuItem MenuCmpSide = new("左右に並べる") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuCmpWipe = new("ワイプ") { ToggleType = MenuItemToggleType.Radio };

	private IEnumerable<MenuItem> ViewMenuItems => ((MenuFlyout)ViewButton.Flyout!).Items.OfType<MenuItem>();

	public MainWindow() : this(Array.Empty<string>()) { }

	public MainWindow(string[] args)
	{
		InitializeComponent();

		BuildMenuBar();
		_a = new MpvPlayer();
		Video.Player = _a;
		Video.Ready += () =>
		{
			if (_pendingOpen != null)
				Open(_pendingOpen);
			_pendingOpen = null;
		};

		Seek.ScrubStarted += OnScrubStarted;
		Seek.Scrubbing += t => { _a.SeekScrub(t); if (Comparing) _b!.SeekScrub(t); };
		Seek.ScrubEnded += OnScrubEnded;
		VolumeSlider.PropertyChanged += (_, e) =>
		{
			if (e.Property == Slider.ValueProperty && _initialized)
			{
				if (_muted && VolumeSlider.Value > 0)
					_muted = false;	// moving the slider up unmutes
				ApplyVolume();
			}
		};
		VolumeSlider.AddHandler(PointerWheelChangedEvent, (_, e) =>
		{
			SetVolume(VolumeSlider.Value + (e.Delta.Y > 0 ? 5 : -5));
			e.Handled = true;
		}, RoutingStrategies.Tunnel);
		VideoArea.Tapped += (_, e) => { if (!OnWipeHandle(e.Source)) TogglePlay(); };
		VideoArea.DoubleTapped += (_, e) => { if (!OnWipeHandle(e.Source)) { TogglePlay(); ToggleFullscreen(); } };	// undo the first tap's toggle
		VideoArea.SizeChanged += (_, _) => LayoutVideo();
		WipeSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) LayoutVideo(); };
		WipeHandle.PointerPressed += (_, e) => { e.Pointer.Capture(WipeHandle); e.Handled = true; };
		WipeHandle.PointerMoved += (_, e) =>
		{
			if (e.Pointer.Captured == WipeHandle && VideoArea.Bounds.Width > 0)
				WipeSlider.Value = Math.Clamp(e.GetPosition(VideoArea).X / VideoArea.Bounds.Width, 0, 1);
		};
		WipeHandle.PointerReleased += (_, e) => { e.Pointer.Capture(null); e.Handled = true; };
		AddHandler(DragDrop.DropEvent, OnDrop);
		AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

		_statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
		_statusTimer.Tick += (_, _) => { StatusBox.IsVisible = false; _statusTimer.Stop(); };
		_timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => OnTick());

		SetBackground(_settings.Background is "checker" or "black" or "white" or "alpha" ? _settings.Background : "black");
		SetLoopFile(true);	// loop is on by default
		SetInfoVisible(_settings.InfoPanel);
		_muted = _settings.Mute;
		VolumeSlider.Value = Math.Clamp(Math.Round(_settings.Volume), 0, 100);
		_initialized = true;
		ApplyVolume();
		_timer.Start();

		Closed += (_, _) =>
		{
			_timer.Stop();
			Video.FreeRenderContext();
			VideoB.FreeRenderContext();
			_b?.Dispose();
			_a.Dispose();
		};

		string[] files = args.Where(a => !a.StartsWith("--")).ToArray();
		int st = Array.IndexOf(args, "--selftest");
		if (st >= 0)
			Opened += async (_, _) => await SelfTest.Run(this, args.Skip(st + 1).ToArray());
		else
		{
			if (files.Length > 0)
				Open(files[0]);
			if (files.Length > 1)
				OpenB(files[1]);
		}
	}

	internal MpvPlayer PlayerA => _a;
	internal MpvView View => Video;
	internal MpvPlayer? PlayerB => _b;
	internal void OpenBForTest(string path) => OpenB(path);
	internal void SetCompareForTest(string mode) => _ = SetCompare(ParseCompare(mode));
	internal void SetWipeForTest(double v) => WipeSlider.Value = v;
	internal void SetLoopPointForTest(string prop) => SetLoopPoint(prop);
	internal (long First, long Last)? CutFramesForTest => CutFrames();
	internal void ShowCutPanelForTest() => SetCutPanel(true);
	internal string InfoTextForTest => string.Join(" | ", InfoGrid.Children.OfType<TextBlock>().Select(t => t.Text));

	// ------------------------------------------------------------------------------------------
	// Opening files
	// ------------------------------------------------------------------------------------------

	public void Open(string path)
	{
		Title = Path.GetFileName(path) + " — ProRes Player";
		Hint.IsVisible = false;
		if (!Video.IsReady)
		{
			_pendingOpen = path;	// with vo=libmpv, files loaded before the view can draw get no video
			return;
		}
		_a.Load(path);
		_a.Paused = false;	// start playing as soon as a file is opened
		_infoPath = null;
		_stepTarget = null;
		ClearLoop();
		UpdatePlaylist(path);
		if (_b != null && _b.HasFile)
		{
			_b.SeekAbsolute(0);
			_b.Paused = !Comparing;
		}
	}

	private void OpenB(string path)
	{
		EnsureB();
		_pathB = path;
		if (_compare == CompareMode.Off)
			_ = SetCompare(CompareMode.Side);
		if (!VideoB.IsReady)
		{
			_pendingB = path;	// loaded once the B view can draw (see Open)
			return;
		}
		_b!.Load(path);
		_infoPathB = null;
		_b.Paused = _a.Paused;
		Dispatcher.UIThread.Post(SyncBToA, DispatcherPriority.Background);
	}

	private void EnsureB()
	{
		if (_b != null)
			return;
		_b = new MpvPlayer();
		_b.Set("mute", "yes");	// B is always silent
		_b.Set("loop-file", LoopButton.IsChecked == true ? "inf" : "no");
		VideoB.Player = _b;
		VideoB.Ready += () =>
		{
			if (_pendingB != null)
			{
				string p = _pendingB;
				_pendingB = null;
				OpenB(p);
			}
		};
		ApplyBackground(_b);
		LayoutVideo();
	}

	private async Task<string?> PickMovie(string title)
	{
		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = title,
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("動画") { Patterns = FolderPlaylist.Extensions.Select(e => "*" + e).ToArray() },
				FilePickerFileTypes.All,
			},
		});
		return files.FirstOrDefault()?.TryGetLocalPath();
	}

	private async Task OpenBDialog()
	{
		if (await PickMovie("比較する動画 (B) を開く") is string path)
			OpenB(path);
	}

	private async Task OpenDialog()
	{
		if (await PickMovie("動画を開く") is string path)
			Open(path);
	}

	private void OnDrop(object? sender, DragEventArgs e)
	{
		var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? Array.Empty<string>();
		if (paths.Length > 0)
			Open(paths[0]);
		if (paths.Length > 1)
			OpenB(paths[1]);	// two files at once: A and B
	}

	private void UpdatePlaylist(string path)
	{
		_playlist.Update(path);
		string? prev = _playlist.Neighbor(-1), next = _playlist.Neighbor(1);
		PrevFileButton.IsEnabled = prev != null;
		NextFileButton.IsEnabled = next != null;
		FilePosText.Text = _playlist.Count > 1 ? $"{_playlist.Index + 1} / {_playlist.Count}" : "";
		ToolTip.SetTip(PrevFileButton, (prev != null ? Path.GetFileName(prev) + "\n" : "") + "同じフォルダの前の動画 (⌘← / PageUp)");
		ToolTip.SetTip(NextFileButton, (next != null ? Path.GetFileName(next) + "\n" : "") + "同じフォルダの次の動画 (⌘→ / PageDown)");
	}

	private void OpenNeighbor(int delta)
	{
		if (_a.Path is not string current)
			return;
		_playlist.Update(current);	// pick up files added or removed since the last scan
		if (_playlist.Neighbor(delta) is string target)
			Open(target);
		else
			ShowStatus(delta < 0 ? "最初の動画です" : "最後の動画です");
	}

	private void ShowStatus(string text, bool sticky = false)
	{
		StatusText.Text = text;
		StatusBox.IsVisible = true;
		_statusTimer.Stop();
		if (!sticky)
			_statusTimer.Start();
	}

	// ------------------------------------------------------------------------------------------
	// Transport
	// ------------------------------------------------------------------------------------------

	private bool Comparing => _compare != CompareMode.Off && _b != null && _pathB != null;

	private void TogglePlay()
	{
		if (!_a.HasFile)
			return;
		bool pause = !_a.Paused;
		// Pressing play on the last frame (loop off, or after stepping there) starts over from the top.
		if (!pause && AtEnd())
			SeekTo(0);
		SetPaused(pause);
		_stepTarget = null;
	}

	/// <summary>Pauses / plays A, and B with it while comparing (B is lined up with A on pause).</summary>
	private void SetPaused(bool pause)
	{
		_a.Paused = pause;
		if (Comparing)
		{
			_b!.Paused = pause;
			if (pause)
				SyncBToA();
		}
	}

	private void SeekTo(double t)
	{
		if (!_a.HasFile)
			return;
		_a.SeekAbsolute(Math.Max(0, t));
		if (Comparing)
			_b!.SeekAbsolute(Math.Max(0, t));
	}

	private void SyncBToA()
	{
		if (Comparing)
			_b!.SeekAbsolute(_a.Time);
	}

	private bool AtEnd()
	{
		if (_a.Get("eof-reached") == "yes")
			return true;
		double fps = _a.Fps, dur = _a.Duration;
		return fps > 0 && dur > 0 && _a.Time >= dur - 1.5 / fps;
	}

	/// <summary>
	/// Frame step by seeking to an absolute frame. mpv's frame-step drops presses that arrive while a
	/// step is still in flight, so rapid key repeats are accumulated here instead.
	/// </summary>
	internal void Step(int dir)
	{
		if (!_a.HasFile)
			return;
		double fps = _a.Fps;
		SetPaused(true);
		if (fps <= 0)
		{
			if (dir > 0) _a.FrameStep(); else _a.FrameBackStep();
			SyncBToA();
			return;
		}
		long last = Math.Max(0, (long)Math.Round(_a.Duration * fps) - 1);
		bool recent = _stepTarget.HasValue && DateTime.UtcNow - _stepTime < TimeSpan.FromMilliseconds(400);
		long current = recent ? _stepTarget!.Value : (long)Math.Round(_a.Time * fps);
		long target = Math.Clamp(current + dir, 0, last);
		_stepTarget = target;
		_stepTime = DateTime.UtcNow;
		SeekTo(FrameTime(target, fps));
	}

	// A hair past the frame's pts so rounding never lands on the previous frame.
	internal static double FrameTime(long frame, double fps) => (frame + 0.01) / fps;

	private void SeekBy(double dt)
	{
		if (_a.HasFile)
			SeekTo(_a.Time + dt);
	}

	private void GoStart()
	{
		if (!_a.HasFile)
			return;
		SetPaused(true);
		SeekTo(0);
	}

	private void GoEnd()
	{
		if (!_a.HasFile)
			return;
		SetPaused(true);
		double fps = _a.Fps;
		SeekTo(_a.Duration - (fps > 0 ? 1.0 / fps : 0.04));
	}

	internal long CurrentFrame => _a.Fps > 0 ? (long)Math.Round(_a.Time * _a.Fps) : 0;

	// Scrubbing holds playback; release lands exactly on the frame and resumes if it was playing.
	private void OnScrubStarted(double t)
	{
		_resumeAfterScrub = !_a.Paused;
		SetPaused(true);
		_stepTarget = null;
		_a.SeekScrub(t);
		if (Comparing)
			_b!.SeekScrub(t);
	}

	private void OnScrubEnded(double t)
	{
		SeekTo(t);
		if (_resumeAfterScrub)
			SetPaused(false);
	}

	private void OnPlayPause(object? sender, RoutedEventArgs e) => TogglePlay();
	private void OnStepBack(object? sender, RoutedEventArgs e) => Step(-1);
	private void OnStepForward(object? sender, RoutedEventArgs e) => Step(1);
	private void OnGoStart(object? sender, RoutedEventArgs e) => GoStart();
	private void OnGoEnd(object? sender, RoutedEventArgs e) => GoEnd();
	private void OnPrevFile(object? sender, RoutedEventArgs e) => OpenNeighbor(-1);
	private void OnNextFile(object? sender, RoutedEventArgs e) => OpenNeighbor(1);

	// ------------------------------------------------------------------------------------------
	// Frame export: PNG, clipboard, markup (always A)
	// ------------------------------------------------------------------------------------------

	private void OnSaveFrame(object? sender, RoutedEventArgs e) => _ = SaveFrame();
	private void OnCopyFrame(object? sender, RoutedEventArgs e) => CopyFrame();
	private void OnMarkup(object? sender, RoutedEventArgs e) => OpenMarkup();

	private string FrameBaseName(string video) => $"{Path.GetFileNameWithoutExtension(video)}_f{CurrentFrame:00000}";

	/// <summary>Saves the current frame at source resolution, 16-bit RGBA (keeps alpha).</summary>
	private async Task SaveFrame()
	{
		if (_a.Path is not string video || !_a.HasFile)
			return;
		SetPaused(true);
		string dir = _lastSaveDir ?? Path.GetDirectoryName(video) ?? "";
		var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "フレームを PNG で保存",
			SuggestedFileName = FrameBaseName(video) + ".png",
			SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(dir),
			DefaultExtension = "png",
			FileTypeChoices = new[] { FilePickerFileTypes.ImagePng },
		});
		if (file?.TryGetLocalPath() is not string path)
			return;
		ShowStatus(SaveFrameTo(path) ? "保存しました: " + Path.GetFileName(path) : "保存に失敗しました");
	}

	internal bool SaveFrameTo(string path)
	{
		_lastSaveDir = Path.GetDirectoryName(path);
		if (File.Exists(path))
			File.Delete(path);	// mpv does not overwrite
		return _a.Screenshot(path);
	}

	/// <summary>The current frame of A at source resolution (keeps alpha), or null.</summary>
	private Avalonia.Media.Imaging.Bitmap? GrabFrame()
	{
		if (!_a.HasFile)
			return null;
		SetPaused(true);
		string tmp = Path.Combine(Path.GetTempPath(), "ProResPlayer_frame.png");
		if (File.Exists(tmp))
			File.Delete(tmp);
		// mpv's 8-bit screenshots drop alpha, so grab 16-bit RGBA; the bitmap is 8-bit with alpha.
		if (!_a.Screenshot(tmp, highBitDepth: true))
			return null;
		using var fs = File.OpenRead(tmp);
		return new Avalonia.Media.Imaging.Bitmap(fs);
	}

	/// <summary>Copies the frame as PNG (keeps alpha) and TIFF.</summary>
	internal void CopyFrame()
	{
		if (!_a.HasFile)
			return;
		using var frame = GrabFrame();
		if (frame == null)
		{
			ShowStatus("コピーに失敗しました");
			return;
		}
		using var ms = new MemoryStream();
#pragma warning disable CS0618
		frame.Save(ms);
#pragma warning restore CS0618
		ShowStatus(ClipboardImage.SetPng(ms.ToArray()) ? $"コピーしました (フレーム {CurrentFrame})" : "コピーに失敗しました");
	}

	/// <summary>Opens the current frame in the markup window (draw, then copy / save).</summary>
	internal MarkupWindow? OpenMarkup()
	{
		if (_a.Path is not string video || !_a.HasFile)
			return null;
		if (GrabFrame() is not { } frame)
		{
			ShowStatus("フレームを取り込めませんでした");
			return null;
		}
		double fps = _a.Fps, t = _a.Time;
		long frameNo = CurrentFrame, last = fps > 0 ? Math.Max(0, (long)Math.Round(_a.Duration * fps) - 1) : 0;
		string info = string.Join("   ", new[]
		{
			Path.GetFileName(video),
			fps > 0 ? $"フレーム {frameNo} / {last}" : null,
			fps > 0 ? Timecode(frameNo, fps) : null,
			$"{t:0.000} 秒",
			$"{frame.PixelSize.Width}×{frame.PixelSize.Height}",
			fps > 0 ? $"{fps:0.###} fps" : null,
		}.Where(s => s != null));
		var w = new MarkupWindow(frame, _background, info, FrameBaseName(video), _lastSaveDir ?? Path.GetDirectoryName(video))
		{
			Width = Math.Max(760, Bounds.Width * 0.85),
			Height = Math.Max(420, Bounds.Height * 0.85),
		};
		w.Show(this);
		return w;
	}

	// ------------------------------------------------------------------------------------------
	// Loop (mpv A-B loop)
	// ------------------------------------------------------------------------------------------

	private void SetLoopPoint(string prop)
	{
		if (!_a.HasFile)
			return;
		double t = _a.Time;
		_a.Set(prop, t);
		_b?.Set(prop, t);
		// Keep A < B.
		double? a = _a.GetDouble("ab-loop-a"), b = _a.GetDouble("ab-loop-b");
		if (a.HasValue && b.HasValue && a > b)
		{
			_a.Set("ab-loop-a", b.Value); _a.Set("ab-loop-b", a.Value);
			_b?.Set("ab-loop-a", b.Value); _b?.Set("ab-loop-b", a.Value);
		}
	}

	private void ClearLoop()
	{
		foreach (var p in new[] { _a, _b })
		{
			p?.Set("ab-loop-a", "no");
			p?.Set("ab-loop-b", "no");
		}
	}

	private void SetLoopFile(bool on)
	{
		_a.Set("loop-file", on ? "inf" : "no");
		_b?.Set("loop-file", on ? "inf" : "no");
		LoopButton.IsChecked = on;
		MenuLoop.IsChecked = on;
	}

	private void OnLoopButton(object? sender, RoutedEventArgs e) => SetLoopFile(LoopButton.IsChecked == true);

	// ------------------------------------------------------------------------------------------
	// Cut: keep IN..OUT and save it without re-encoding
	// ------------------------------------------------------------------------------------------

	private bool _cutting;

	private void OnCutButton(object? sender, RoutedEventArgs e) => SetCutPanel(CutButton.IsChecked == true);
	private void OnSetIn(object? sender, RoutedEventArgs e) => SetLoopPoint("ab-loop-a");
	private void OnSetOut(object? sender, RoutedEventArgs e) => SetLoopPoint("ab-loop-b");
	private void OnClearLoop(object? sender, RoutedEventArgs e) => ClearLoop();
	private void OnCutSave(object? sender, RoutedEventArgs e) => _ = CutAsync();

	private void SetCutPanel(bool on)
	{
		CutButton.IsChecked = on;
		CutPanel.IsVisible = on && WindowState != WindowState.FullScreen;
	}

	/// <summary>First and last frame (both kept) of the IN / OUT range, or null without a range.</summary>
	private (long First, long Last)? CutFrames()
	{
		double fps = _a.Fps, dur = _a.Duration;
		double? a = _a.GetDouble("ab-loop-a"), b = _a.GetDouble("ab-loop-b");
		if (fps <= 0 || dur <= 0 || (!a.HasValue && !b.HasValue))
			return null;
		long total = Math.Max(1, (long)Math.Round(dur * fps));
		long first = a.HasValue ? Math.Clamp((long)Math.Round(a.Value * fps), 0, total - 1) : 0;
		long last = b.HasValue ? Math.Clamp((long)Math.Round(b.Value * fps), 0, total - 1) : total - 1;	// OUT frame is kept
		return last < first ? (last, first) : (first, last);
	}

	private void UpdateCutRange(double dur, double fps)
	{
		if (!CutPanel.IsVisible)
			return;
		if (CutFrames() is not var (first, last))
		{
			CutRangeText.Text = "I / O で範囲を決めてください";
			CutSaveButton.IsEnabled = false;
			return;
		}
		CutRangeText.Text = $"{Timecode(first, fps)} 〜 {Timecode(last, fps)}  ({last - first + 1} f)";
		CutSaveButton.IsEnabled = !_cutting;
	}

	private async Task CutAsync()
	{
		if (_cutting || _a.Path is not string source || !_a.HasFile)
			return;
		if (!File.Exists(source))
		{
			ShowStatus("ローカルのファイルだけカットできます");
			return;
		}
		if (_a.Fps <= 0 || _a.Duration <= 0)
		{
			ShowStatus("この動画はカットできません");
			return;
		}
		if (CutFrames() is not var (first, last))
		{
			ShowStatus("先に IN / OUT (I / O キー) で範囲を指定してください");
			return;
		}
		if (!Cutter.Available)
		{
			await Dialogs.ShowMessage(this, "ffmpeg が見つかりません", "カットには ffmpeg が必要です。開発中は `brew install ffmpeg` で入れてください。\n\n探した場所: " + Cutter.FfmpegPath);
			return;
		}
		SetPaused(true);

		double fps = _a.Fps;
		long count = last - first + 1;
		string codec = _a.Get("current-tracks/video/codec") ?? _a.Get("video-codec") ?? "";
		string? warning = Cutter.IsIntraOnly(codec) ? null :
			$"この動画 ({codec}) はキーフレームの位置でしか切れないため、IN より少し前から、OUT より少し後まで入ることがあります。";
		string range = $"{Timecode(first, fps)} 〜 {Timecode(last, fps)}   ({first} 〜 {last} f、{count} フレーム)";

		var choice = await Dialogs.AskCut(this, Path.GetFileName(source), range, warning);
		if (choice == CutChoice.Cancel)
			return;

		string target = source;
		if (choice == CutChoice.SaveAs)
		{
			string ext = Path.GetExtension(source);
			var folder = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(source)!);
			var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
			{
				Title = "カットした動画を保存",
				SuggestedFileName = Path.GetFileNameWithoutExtension(source) + "_cut" + ext,
				SuggestedStartLocation = folder,
				DefaultExtension = ext.TrimStart('.'),
				FileTypeChoices = new[] { new FilePickerFileType(ext.TrimStart('.').ToUpperInvariant()) { Patterns = new[] { "*" + ext } } },
			});
			if (file?.TryGetLocalPath() is not string chosen)
				return;
			target = chosen;
		}
		await CutToFileAsync(source, target, first, count, fps);
	}

	/// <summary>Writes the cut next to the target first, so a failed cut never destroys an existing file.</summary>
	internal async Task<bool> CutToFileAsync(string source, string target, long first, long count, double fps)
	{
		bool overwrite = string.Equals(Path.GetFullPath(target), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase);
		string tmp = Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileNameWithoutExtension(target) + ".cutting" + Path.GetExtension(target));
		_cutting = true;
		ShowStatus("カット中...", sticky: true);
		try
		{
			string? error = await Cutter.CutAsync(source, tmp, first, count, fps, _a.Get("current-tracks/audio/codec"));
			if (error != null)
			{
				TryDelete(tmp);
				ShowStatus("カットに失敗しました");
				await Dialogs.ShowMessage(this, "カットに失敗しました", error);
				return false;
			}
			bool bSame = _b != null && string.Equals(_pathB, source, StringComparison.OrdinalIgnoreCase);
			if (overwrite)
			{
				_a.Command("stop");	// let go of the old file before it is replaced
				if (bSame)
					_b!.Command("stop");
			}
			try
			{
				File.Move(tmp, target, overwrite: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				ShowStatus("保存に失敗しました");
				await Dialogs.ShowMessage(this, "保存に失敗しました",
					$"{Path.GetFileName(target)} を置き換えられませんでした。\n{ex.Message}\n\nカット結果は次の名前で残してあります:\n{tmp}");
				if (overwrite)
					Open(source);
				return false;
			}
			if (overwrite)
			{
				Open(source);
				if (bSame)
					OpenB(source);
			}
			else
				UpdatePlaylist(source);	// the new file joins the folder list
			ShowStatus($"保存しました: {Path.GetFileName(target)} ({count} フレーム)");
			return true;
		}
		finally
		{
			_cutting = false;
		}
	}

	private static void TryDelete(string path)
	{
		try { File.Delete(path); } catch { }
	}

	// ------------------------------------------------------------------------------------------
	// Background / alpha (same mpv settings as the Windows build)
	// ------------------------------------------------------------------------------------------

	internal void SetBackground(string mode)
	{
		_background = mode;
		if (_settings.Background != mode)
		{
			_settings.Background = mode;
			_settings.Save();
		}
		ApplyBackground(_a);
		ApplyBackground(_b);
		UpdateViewMenu();
		MenuChecker.IsChecked = mode == "checker";
		MenuBlack.IsChecked = mode == "black";
		MenuWhite.IsChecked = mode == "white";
		MenuAlpha.IsChecked = mode == "alpha";
	}

	private void CycleBackground()
	{
		string[] order = { "checker", "black", "white", "alpha" };
		SetBackground(order[(Array.IndexOf(order, _background) + 1) % order.Length]);
	}

	private void ApplyBackground(MpvPlayer? p)
	{
		if (p == null)
			return;
		p.Command("vf", "remove", "@alpha");
		// VideoToolbox decodes ProRes on the GPU, where lavfi filters can't reach the frames.
		// For the alpha matte, copy decoded frames back to memory (still hardware-decoded).
		p.Set("hwdec", _background == "alpha" ? "videotoolbox-copy" : "auto-safe");
		if (_background == "alpha")
			p.Command("vf", "add", "@alpha:lavfi=[alphaextract]");
		if (_background == "checker")
		{
			if (p.Set("background", "tiles") < 0)
				p.Set("alpha", "blend-tiles");
			return;
		}
		p.Set("background-color", _background == "white" ? "#FFFFFF" : "#000000");
		if (p.Set("background", "color") < 0)
			p.Set("alpha", "blend");
	}

	private void UpdateViewMenu()
	{
		string cmp = "cmp:" + _compare.ToString().ToLowerInvariant();
		foreach (var item in ViewMenuItems)
			if (item.Tag is string tag && tag != "openb")
				item.IsChecked = tag == "bg:" + _background || tag == cmp;
		MenuCmpOff.IsChecked = _compare == CompareMode.Off;
		MenuCmpSide.IsChecked = _compare == CompareMode.Side;
		MenuCmpWipe.IsChecked = _compare == CompareMode.Wipe;
	}

	private void OnViewMenuItem(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem { Tag: string tag })
			return;
		if (tag == "openb")
			_ = OpenBDialog();
		else if (tag.StartsWith("bg:"))
			SetBackground(tag[3..]);
		else
			_ = SetCompare(ParseCompare(tag[4..]));
	}

	// ------------------------------------------------------------------------------------------
	// Compare (A / B)
	// ------------------------------------------------------------------------------------------

	private static CompareMode ParseCompare(string tag) => tag switch { "side" => CompareMode.Side, "wipe" => CompareMode.Wipe, _ => CompareMode.Off };

	private void CycleCompare() =>
		_ = SetCompare(_compare switch { CompareMode.Off => CompareMode.Side, CompareMode.Side => CompareMode.Wipe, _ => CompareMode.Off });

	private async Task SetCompare(CompareMode mode)
	{
		if (mode != CompareMode.Off && _pathB == null)
		{
			string? path = await PickMovie("比較する動画 (B) を開く");
			if (path == null)
				mode = CompareMode.Off;
			else
			{
				_compare = mode;	// set first so OpenB does not switch to Side
				OpenB(path);
			}
		}
		_compare = mode;
		WipeSlider.IsVisible = mode == CompareMode.Wipe;
		InfoBPanel.IsVisible = mode != CompareMode.Off;
		if (_b != null)
		{
			if (mode == CompareMode.Off)
				_b.Paused = true;
			else
			{
				_b.Paused = _a.Paused;
				SyncBToA();
			}
		}
		UpdateViewMenu();
		LayoutVideo();
	}

	private bool OnWipeHandle(object? source) => source is Avalonia.Visual v && (v == WipeHandle || WipeHandle.IsVisualAncestorOf(v));

	/// <summary>
	/// Side by side: A on the left half, B on the right. Wipe: A and B both cover the whole area (so they
	/// line up exactly) and B is clipped to the right of the divider.
	/// </summary>
	private void LayoutVideo()
	{
		double w = VideoArea.Bounds.Width, h = VideoArea.Bounds.Height;
		bool on = _compare != CompareMode.Off && _b != null;
		VideoB.IsVisible = on;
		TagA.IsVisible = TagB.IsVisible = on;
		WipeHandle.IsVisible = on;
		if (!on || w <= 0)
		{
			Video.Margin = default;
			VideoB.Clip = null;
			return;
		}
		if (_compare == CompareMode.Side)
		{
			double half = (w - Gap) / 2;
			Video.Margin = new Avalonia.Thickness(0, 0, w - half, 0);
			VideoB.Margin = new Avalonia.Thickness(w - half, 0, 0, 0);
			VideoB.Clip = null;
			WipeHandle.IsHitTestVisible = false;
			WipeHandle.Margin = new Avalonia.Thickness(half + Gap / 2 - WipeHandle.Width / 2, 0, 0, 0);
			TagB.Margin = new Avalonia.Thickness(w - half + 10, 10, 0, 0);
		}
		else
		{
			double x = Math.Round(WipeSlider.Value * w);
			Video.Margin = default;
			VideoB.Margin = default;
			VideoB.Clip = new RectangleGeometry(new Avalonia.Rect(x, 0, Math.Max(0, w - x), h));
			WipeHandle.IsHitTestVisible = true;
			WipeHandle.Margin = new Avalonia.Thickness(x - WipeHandle.Width / 2, 0, 0, 0);
			TagB.Margin = new Avalonia.Thickness(Math.Min(x + 10, w - 40), 10, 0, 0);
		}
		TagA.Margin = new Avalonia.Thickness(10);
	}

	// ------------------------------------------------------------------------------------------
	// Info panel, fullscreen
	// ------------------------------------------------------------------------------------------

	private void SetInfoVisible(bool on)
	{
		if (_settings.InfoPanel != on)
		{
			_settings.InfoPanel = on;
			_settings.Save();
		}
		InfoPanel.IsVisible = on && WindowState != WindowState.FullScreen;
		InfoButton.IsChecked = on;
		MenuInfo.IsChecked = on;
	}

	private void OnInfoButton(object? sender, RoutedEventArgs e) => SetInfoVisible(!_settings.InfoPanel);

	private void ToggleFullscreen()
	{
		bool full = WindowState != WindowState.FullScreen;
		WindowState = full ? WindowState.FullScreen : WindowState.Normal;
		ControlBar.IsVisible = !full;
		SetInfoVisible(_settings.InfoPanel);
		SetCutPanel(CutButton.IsChecked == true);
	}

	private static List<(string Label, string Value)> BuildInfo(MpvPlayer p)
	{
		var rows = new List<(string, string)>();
		void Line(string label, string? value)
		{
			if (!string.IsNullOrEmpty(value))
				rows.Add((label, value));
		}

		Line("ファイル", p.Get("filename"));
		if (long.TryParse(p.Get("file-size"), out long size))
			Line("サイズ", $"{size / 1048576.0:0.0} MB");
		Line("コーデック", p.Get("current-tracks/video/codec-desc") ?? p.Get("video-codec"));
		Line("プロファイル", p.Get("current-tracks/video/codec-profile"));
		Line("解像度", $"{p.Get("video-params/w")} x {p.Get("video-params/h")}");
		double fps = p.Fps;
		Line("fps", fps.ToString("0.###", CultureInfo.InvariantCulture));
		double dur = p.Duration;
		Line("長さ", $"{dur:0.00} 秒 ({Math.Round(dur * fps)} f)");
		Line("画素", p.Get("video-params/hw-pixelformat") ?? p.Get("video-params/pixelformat"));
		string? alpha = p.Get("video-params/alpha");
		Line("アルファ", alpha is null or "auto" or "none" ? "なし" : alpha);
		Line("マトリクス", p.Get("video-params/colormatrix"));
		Line("原色", p.Get("video-params/primaries"));
		Line("ガンマ", p.Get("video-params/gamma"));
		Line("レンジ", p.Get("video-params/colorlevels") ?? p.Get("video-params/levels"));
		Line("デコード", p.Get("hwdec-current") is string hw and not "no" ? $"ハードウェア ({hw})" : "ソフトウェア");
		double? br = p.GetDouble("current-tracks/video/demux-bitrate") ?? p.GetDouble("video-bitrate");
		if (br is > 0)
			Line("ビットレート", $"{br / 1e6:0.0} Mbps");
		return rows;
	}

	private void ShowInfo(Grid grid, List<(string Label, string Value)> rows)
	{
		if (grid == InfoGrid)
			InfoHint.IsVisible = false;
		grid.Children.Clear();
		grid.RowDefinitions.Clear();
		for (int i = 0; i < rows.Count; i++)
		{
			grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			var label = new TextBlock { Text = rows[i].Label, FontSize = 12, Foreground = (IBrush)this.FindResource("Dim")!, Margin = new Avalonia.Thickness(0, 2, 12, 2) };
			var value = new SelectableTextBlock { Text = rows[i].Value, FontFamily = new FontFamily("Menlo"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 2) };
			Grid.SetRow(label, i);
			Grid.SetRow(value, i);
			Grid.SetColumn(value, 1);
			grid.Children.Add(label);
			grid.Children.Add(value);
		}
	}

	// ------------------------------------------------------------------------------------------
	// Volume
	// ------------------------------------------------------------------------------------------

	private void SetVolume(double v)
	{
		v = Math.Clamp(Math.Round(v), 0, 100);
		if (VolumeSlider.Value != v)
			VolumeSlider.Value = v;	// the PropertyChanged handler applies and saves it
		else
			ApplyVolume();
	}

	private void SetMute(bool on)
	{
		_muted = on;
		ApplyVolume();
	}

	private void OnMuteButton(object? sender, RoutedEventArgs e) => SetMute(!_muted);

	private void ApplyVolume()
	{
		double v = VolumeSlider.Value;
		_a.Set("volume", v);
		_a.Set("mute", _muted ? "yes" : "no");
		string icon = _muted || v == 0 ? "IconMute" : v < 34 ? "IconVolume1" : v < 67 ? "IconVolume2" : "IconVolume3";
		if (this.TryFindResource(icon, out object? g) && g is Geometry geo)
			MuteIcon.Data = geo;
		ToolTip.SetTip(MuteButton, (_muted ? "ミュート中" : $"音量 {v:0}%") + "\nクリックでミュート オン / オフ (M)");
		ToolTip.SetTip(VolumeSlider, $"音量 {v:0}% (⌘↑ / ⌘↓、ホイールでも変えられます)");
		if (_settings.Volume != v || _settings.Mute != _muted)
		{
			_settings.Volume = v;
			_settings.Mute = _muted;
			_settings.Save();
		}
	}

	// ------------------------------------------------------------------------------------------
	// Periodic UI update
	// ------------------------------------------------------------------------------------------

	private void OnTick()
	{
		string? path = _a.Path;
		if (path != null && path != _infoPath && _a.GetDouble("video-params/dw") is > 0)
		{
			_infoPath = path;
			ShowInfo(InfoGrid, BuildInfo(_a));
		}
		if (_b != null && _b.Path is string pathB && pathB != _infoPathB && _b.GetDouble("video-params/dw") is > 0)
		{
			_infoPathB = pathB;
			ShowInfo(InfoGridB, BuildInfo(_b));
		}
		if (!_a.HasFile)
			return;

		double t = _a.Time, dur = _a.Duration, fps = _a.Fps;
		// Keep B within a frame of A while playing.
		if (Comparing && !_a.Paused && fps > 0)
		{
			if (Math.Abs(_b!.Time - t) > 1.5 / fps)
				_b.SeekAbsolute(t);
			if (_b.Paused)
				_b.Paused = false;
		}
		if (this.TryFindResource(_a.Paused ? "IconPlay" : "IconPause", out object? g) && g is Geometry geo && PlayIcon.Data != geo)
			PlayIcon.Data = geo;

		Seek.Update(dur, t, fps, _a.GetDouble("ab-loop-a"), _a.GetDouble("ab-loop-b"));
		UpdateCutRange(dur, fps);

		long frame = fps > 0 ? (long)Math.Round(t * fps) : 0;
		long total = fps > 0 ? (long)Math.Round(dur * fps) : 0;
		TimecodeText.Text = Timecode(frame, fps);
		FrameText.Text = $"{frame} / {Math.Max(0, total - 1)} f   {fps:0.###} fps";
	}

	internal static string Timecode(long frame, double fps)
	{
		int nominal = Math.Max(1, (int)Math.Round(fps));
		long ff = frame % nominal, totalSec = frame / nominal;
		return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}:{3:00}",
			totalSec / 3600, totalSec / 60 % 60, totalSec % 60, ff);
	}

	// ------------------------------------------------------------------------------------------
	// Menu bar
	// ------------------------------------------------------------------------------------------

	private void BuildMenuBar()
	{
		static NativeMenuItem Item(string header, Action click, string? gesture = null)
		{
			var item = new NativeMenuItem(header);
			if (gesture != null)
				item.Gesture = KeyGesture.Parse(gesture);
			item.Click += (_, _) => click();
			return item;
		}
		static NativeMenuItem Sub(string header, params NativeMenuItemBase[] items)
		{
			var menu = new NativeMenu();
			foreach (var i in items)
				menu.Items.Add(i);
			return new NativeMenuItem(header) { Menu = menu };
		}

		MenuChecker.Click += (_, _) => SetBackground("checker");
		MenuBlack.Click += (_, _) => SetBackground("black");
		MenuWhite.Click += (_, _) => SetBackground("white");
		MenuAlpha.Click += (_, _) => SetBackground("alpha");
		MenuInfo.Click += (_, _) => SetInfoVisible(!_settings.InfoPanel);
		MenuLoop.Click += (_, _) => SetLoopFile(LoopButton.IsChecked != true);

		var bar = new NativeMenu();
		bar.Items.Add(Sub("ファイル",
			Item("開く...", () => _ = OpenDialog(), "Cmd+O"),
			new NativeMenuItemSeparator(),
			Item("フレームを PNG で保存...  ⌘S", () => _ = SaveFrame()),
			Item("フレームをコピー  ⌘C", CopyFrame),
			Item("フレームにマークアップ...  ⌘E", () => OpenMarkup()),
			new NativeMenuItemSeparator(),
			Item("IN〜OUT をカットして保存...  ⌘X", () => _ = CutAsync()),
			new NativeMenuItemSeparator(),
			Item("同じフォルダの前の動画", () => OpenNeighbor(-1)),
			Item("同じフォルダの次の動画", () => OpenNeighbor(1))));
		bar.Items.Add(Sub("表示",
			MenuChecker, MenuBlack, MenuWhite, MenuAlpha,
			new NativeMenuItemSeparator(),
			MenuInfo, MenuLoop,
			Item("全画面", ToggleFullscreen, "Cmd+Ctrl+F")));
		MenuCmpOff.Click += (_, _) => _ = SetCompare(CompareMode.Off);
		MenuCmpSide.Click += (_, _) => _ = SetCompare(CompareMode.Side);
		MenuCmpWipe.Click += (_, _) => _ = SetCompare(CompareMode.Wipe);
		bar.Items.Add(Sub("比較",
			Item("比較用に開く (B)...", () => _ = OpenBDialog(), "Cmd+Shift+O"),
			new NativeMenuItemSeparator(),
			MenuCmpOff, MenuCmpSide, MenuCmpWipe));
		bar.Items.Add(Sub("ヘルプ", Item("ショートカット一覧", ShowShortcuts)));
		NativeMenu.SetMenu(this, bar);
	}

	// ------------------------------------------------------------------------------------------
	// Keyboard (⌘ in place of Ctrl). ⌘O is handled by the menu bar.
	// ------------------------------------------------------------------------------------------

	private void OnKey(object? sender, KeyEventArgs e)
	{
		bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
		bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
		bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
		if (ctrl)
			return;	// ⌘⌃F etc. belong to the menu bar
		switch (e.Key)
		{
			case Key.X when cmd: _ = CutAsync(); break;
			case Key.S when cmd: _ = SaveFrame(); break;
			case Key.C when cmd: CopyFrame(); break;
			case Key.E when cmd: OpenMarkup(); break;
			case Key.X: SetCutPanel(CutButton.IsChecked != true); break;
			case Key.Space: TogglePlay(); break;
			case Key.Left when cmd: OpenNeighbor(-1); break;
			case Key.Right when cmd: OpenNeighbor(1); break;
			case Key.PageUp: OpenNeighbor(-1); break;
			case Key.PageDown: OpenNeighbor(1); break;
			case Key.Left when shift: SeekBy(-1); break;
			case Key.Right when shift: SeekBy(1); break;
			case Key.Left: Step(-1); break;
			case Key.Right: Step(1); break;
			case Key.Up when cmd: SetVolume(VolumeSlider.Value + 5); break;
			case Key.Down when cmd: SetVolume(VolumeSlider.Value - 5); break;
			case Key.Up: SeekBy(10); break;
			case Key.Down: SeekBy(-10); break;
			case Key.Home: GoStart(); break;
			case Key.End: GoEnd(); break;
			case Key.M when !cmd: SetMute(!_muted); break;
			case Key.I when !cmd: SetLoopPoint("ab-loop-a"); break;
			case Key.O when !cmd: SetLoopPoint("ab-loop-b"); break;
			case Key.P when !cmd: ClearLoop(); break;
			case Key.L when !cmd: SetLoopFile(LoopButton.IsChecked != true); break;
			case Key.D1 when !cmd: SetBackground("checker"); break;
			case Key.D2 when !cmd: SetBackground("black"); break;
			case Key.D3 when !cmd: SetBackground("white"); break;
			case Key.D4 when !cmd: SetBackground("alpha"); break;
			case Key.A when !cmd: CycleBackground(); break;
			case Key.C when !cmd: CycleCompare(); break;
			case Key.OemOpenBrackets: WipeSlider.Value = Math.Max(0, WipeSlider.Value - 0.05); break;
			case Key.OemCloseBrackets: WipeSlider.Value = Math.Min(1, WipeSlider.Value + 0.05); break;
			case Key.Tab: SetInfoVisible(!_settings.InfoPanel); break;
			case Key.F when !cmd: ToggleFullscreen(); break;
			case Key.Escape when WindowState == WindowState.FullScreen: ToggleFullscreen(); break;
			default: return;
		}
		e.Handled = true;
	}

	private void ShowShortcuts()
	{
		const string text =
			"Space        再生 / 一時停止\n" +
			"← / →        1フレーム戻る / 進む\n" +
			"⇧← / ⇧→      1秒戻る / 進む\n" +
			"↑ / ↓        10秒進む / 戻る\n" +
			"Home / End   先頭 / 末尾\n" +
			"⌘← / ⌘→      同じフォルダの前 / 次の動画（PageUp / PageDown でも可）\n" +
			"I / O / P    ループ開始 / 終了 / クリア（カットの範囲も同じ）\n" +
			"X            カットのパネル（IN / OUT / 保存）を出す / 閉じる\n" +
			"⌘X           IN〜OUT だけ残して保存（再エンコードなし）\n" +
			"L            ループ再生 オン / オフ\n" +
			"M            ミュート オン / オフ\n" +
			"⌘↑ / ⌘↓      音量を上げる / 下げる\n" +
			"1 2 3 4 / A  背景: 市松 / 黒 / 白 / アルファのみ / 切り替え\n" +
			"C            比較: オフ → 左右 → ワイプ\n" +
			"[ / ]        ワイプ位置（境目の線をドラッグしても動かせます）\n" +
			"Tab          情報パネル\n" +
			"F / ⌘⌃F      全画面 (Esc で戻る)\n" +
			"⌘O           開く\n" +
			"⌘S           フレームを PNG で保存（元の解像度、アルファ付き）\n" +
			"⌘C           フレームをコピー\n" +
			"⌘E           フレームにマークアップ（線・矢印・丸・文字を描いてコピー）\n" +
			"⌘⇧O          比較用 (B) を開く\n\n" +
			"動画の上でクリック: 再生 / 一時停止、ダブルクリック: 全画面\n" +
			"ファイルを2つ同時にドロップすると A と B に読み込みます。";
		var dlg = new Window
		{
			Title = "ショートカット",
			Width = 560,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Background,
			Content = new SelectableTextBlock { Text = text, FontFamily = new FontFamily("Menlo"), FontSize = 12, Margin = new Avalonia.Thickness(20) },
		};
		dlg.ShowDialog(this);
	}
}
