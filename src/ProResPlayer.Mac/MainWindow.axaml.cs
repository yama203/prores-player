using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ProResPlayer;

public partial class MainWindow : Window
{
	private readonly MpvPlayer _a;
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

	// macOS menu bar items whose check marks follow the state.
	private readonly NativeMenuItem MenuChecker = new("背景: 市松模様") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuBlack = new("背景: 黒") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuWhite = new("背景: 白") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuAlpha = new("アルファのみ") { ToggleType = MenuItemToggleType.Radio };
	private readonly NativeMenuItem MenuInfo = new("情報パネル") { ToggleType = MenuItemToggleType.CheckBox };
	private readonly NativeMenuItem MenuLoop = new("ループ再生") { ToggleType = MenuItemToggleType.CheckBox };

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
		Seek.Scrubbing += t => _a.SeekScrub(t);
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
		VideoArea.Tapped += (_, _) => TogglePlay();
		VideoArea.DoubleTapped += (_, _) => { TogglePlay(); ToggleFullscreen(); };	// undo the first tap's toggle
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
			_a.Dispose();
		};

		string[] files = args.Where(a => !a.StartsWith("--")).ToArray();
		int st = Array.IndexOf(args, "--selftest");
		if (st >= 0)
			Opened += async (_, _) => await SelfTest.Run(this, args.Skip(st + 1).ToArray());
		else if (files.Length > 0)
			Open(files[0]);
	}

	internal MpvPlayer PlayerA => _a;
	internal MpvView View => Video;
	internal void SetLoopPointForTest(string prop) => SetLoopPoint(prop);
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
	}

	private async Task OpenDialog()
	{
		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "動画を開く",
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("動画") { Patterns = FolderPlaylist.Extensions.Select(e => "*" + e).ToArray() },
				FilePickerFileTypes.All,
			},
		});
		string? path = files.FirstOrDefault()?.TryGetLocalPath();
		if (path != null)
			Open(path);
	}

	private void OnDrop(object? sender, DragEventArgs e)
	{
		string? path = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null);
		if (path != null)
			Open(path);
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

	private void ShowStatus(string text)
	{
		StatusText.Text = text;
		StatusBox.IsVisible = true;
		_statusTimer.Stop();
		_statusTimer.Start();
	}

	// ------------------------------------------------------------------------------------------
	// Transport
	// ------------------------------------------------------------------------------------------

	private void TogglePlay()
	{
		if (!_a.HasFile)
			return;
		bool pause = !_a.Paused;
		// Pressing play on the last frame (loop off, or after stepping there) starts over from the top.
		if (!pause && AtEnd())
			_a.SeekAbsolute(0);
		_a.Paused = pause;
		_stepTarget = null;
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
		_a.Paused = true;
		if (fps <= 0)
		{
			if (dir > 0) _a.FrameStep(); else _a.FrameBackStep();
			return;
		}
		long last = Math.Max(0, (long)Math.Round(_a.Duration * fps) - 1);
		bool recent = _stepTarget.HasValue && DateTime.UtcNow - _stepTime < TimeSpan.FromMilliseconds(400);
		long current = recent ? _stepTarget!.Value : (long)Math.Round(_a.Time * fps);
		long target = Math.Clamp(current + dir, 0, last);
		_stepTarget = target;
		_stepTime = DateTime.UtcNow;
		_a.SeekAbsolute(FrameTime(target, fps));
	}

	// A hair past the frame's pts so rounding never lands on the previous frame.
	internal static double FrameTime(long frame, double fps) => (frame + 0.01) / fps;

	private void SeekBy(double dt)
	{
		if (_a.HasFile)
			_a.SeekAbsolute(Math.Max(0, _a.Time + dt));
	}

	private void GoStart()
	{
		if (!_a.HasFile)
			return;
		_a.Paused = true;
		_a.SeekAbsolute(0);
	}

	private void GoEnd()
	{
		if (!_a.HasFile)
			return;
		_a.Paused = true;
		double fps = _a.Fps;
		_a.SeekAbsolute(Math.Max(0, _a.Duration - (fps > 0 ? 1.0 / fps : 0.04)));
	}

	internal long CurrentFrame => _a.Fps > 0 ? (long)Math.Round(_a.Time * _a.Fps) : 0;

	// Scrubbing holds playback; release lands exactly on the frame and resumes if it was playing.
	private void OnScrubStarted(double t)
	{
		_resumeAfterScrub = !_a.Paused;
		_a.Paused = true;
		_stepTarget = null;
		_a.SeekScrub(t);
	}

	private void OnScrubEnded(double t)
	{
		_a.SeekAbsolute(t);
		if (_resumeAfterScrub)
			_a.Paused = false;
	}

	private void OnPlayPause(object? sender, RoutedEventArgs e) => TogglePlay();
	private void OnStepBack(object? sender, RoutedEventArgs e) => Step(-1);
	private void OnStepForward(object? sender, RoutedEventArgs e) => Step(1);
	private void OnGoStart(object? sender, RoutedEventArgs e) => GoStart();
	private void OnGoEnd(object? sender, RoutedEventArgs e) => GoEnd();
	private void OnPrevFile(object? sender, RoutedEventArgs e) => OpenNeighbor(-1);
	private void OnNextFile(object? sender, RoutedEventArgs e) => OpenNeighbor(1);

	// ------------------------------------------------------------------------------------------
	// Loop (mpv A-B loop)
	// ------------------------------------------------------------------------------------------

	private void SetLoopPoint(string prop)
	{
		if (!_a.HasFile)
			return;
		_a.Set(prop, _a.Time);
		// Keep A < B.
		double? a = _a.GetDouble("ab-loop-a"), b = _a.GetDouble("ab-loop-b");
		if (a.HasValue && b.HasValue && a > b)
		{
			_a.Set("ab-loop-a", b.Value);
			_a.Set("ab-loop-b", a.Value);
		}
	}

	private void ClearLoop()
	{
		_a.Set("ab-loop-a", "no");
		_a.Set("ab-loop-b", "no");
	}

	private void SetLoopFile(bool on)
	{
		_a.Set("loop-file", on ? "inf" : "no");
		LoopButton.IsChecked = on;
		MenuLoop.IsChecked = on;
	}

	private void OnLoopButton(object? sender, RoutedEventArgs e) => SetLoopFile(LoopButton.IsChecked == true);

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
		ApplyBackground();
		foreach (var item in ViewMenuItems)
			item.IsChecked = (string?)item.Tag == mode;
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

	private void ApplyBackground()
	{
		_a.Command("vf", "remove", "@alpha");
		// VideoToolbox decodes ProRes on the GPU, where lavfi filters can't reach the frames.
		// For the alpha matte, copy decoded frames back to memory (still hardware-decoded).
		_a.Set("hwdec", _background == "alpha" ? "videotoolbox-copy" : "auto-safe");
		if (_background == "alpha")
			_a.Command("vf", "add", "@alpha:lavfi=[alphaextract]");
		if (_background == "checker")
		{
			if (_a.Set("background", "tiles") < 0)
				_a.Set("alpha", "blend-tiles");
			return;
		}
		_a.Set("background-color", _background == "white" ? "#FFFFFF" : "#000000");
		if (_a.Set("background", "color") < 0)
			_a.Set("alpha", "blend");
	}

	private void OnViewMenuItem(object? sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: string tag })
			SetBackground(tag);
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

	private void ShowInfo(List<(string Label, string Value)> rows)
	{
		InfoHint.IsVisible = false;
		InfoGrid.Children.Clear();
		InfoGrid.RowDefinitions.Clear();
		for (int i = 0; i < rows.Count; i++)
		{
			InfoGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			var label = new TextBlock { Text = rows[i].Label, FontSize = 12, Foreground = (IBrush)this.FindResource("Dim")!, Margin = new Avalonia.Thickness(0, 2, 12, 2) };
			var value = new SelectableTextBlock { Text = rows[i].Value, FontFamily = new FontFamily("Menlo"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 2) };
			Grid.SetRow(label, i);
			Grid.SetRow(value, i);
			Grid.SetColumn(value, 1);
			InfoGrid.Children.Add(label);
			InfoGrid.Children.Add(value);
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
			ShowInfo(BuildInfo(_a));
		}
		if (!_a.HasFile)
			return;

		double t = _a.Time, dur = _a.Duration, fps = _a.Fps;
		if (this.TryFindResource(_a.Paused ? "IconPlay" : "IconPause", out object? g) && g is Geometry geo && PlayIcon.Data != geo)
			PlayIcon.Data = geo;

		Seek.Update(dur, t, fps, _a.GetDouble("ab-loop-a"), _a.GetDouble("ab-loop-b"));

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
			Item("同じフォルダの前の動画", () => OpenNeighbor(-1)),
			Item("同じフォルダの次の動画", () => OpenNeighbor(1))));
		bar.Items.Add(Sub("表示",
			MenuChecker, MenuBlack, MenuWhite, MenuAlpha,
			new NativeMenuItemSeparator(),
			MenuInfo, MenuLoop,
			Item("全画面", ToggleFullscreen, "Cmd+Ctrl+F")));
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
			"I / O / P    ループ開始 / 終了 / クリア\n" +
			"L            ループ再生 オン / オフ\n" +
			"M            ミュート オン / オフ\n" +
			"⌘↑ / ⌘↓      音量を上げる / 下げる\n" +
			"1 2 3 4 / A  背景: 市松 / 黒 / 白 / アルファのみ / 切り替え\n" +
			"Tab          情報パネル\n" +
			"F / ⌘⌃F      全画面 (Esc で戻る)\n" +
			"⌘O           開く\n\n" +
			"動画の上でクリック: 再生 / 一時停止、ダブルクリック: 全画面";
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
