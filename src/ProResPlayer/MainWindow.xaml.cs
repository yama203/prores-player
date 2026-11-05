using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ProResPlayer;

public partial class MainWindow : Window
{
	private enum CompareMode { Off, Side, Wipe }

	private const double Gap = 2;	// divider between A and B, in DIPs
	private const string FileFilter = "動画 (*.mov;*.mp4;*.m4v;*.mxf;*.mkv;*.avi;*.webm)|*.mov;*.mp4;*.m4v;*.mxf;*.mkv;*.avi;*.webm|すべてのファイル (*.*)|*.*";

	private MpvPlayer? _a;
	private MpvPlayer? _b;
	private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
	private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
	private string? _lastSaveDir;
	private readonly AppSettings _settings = AppSettings.Load();

	private CompareMode _compare = CompareMode.Off;
	private string _background = "black";
	private string? _infoPathA, _infoPathB;
	private string? _pathB;	// set as soon as B is requested; mpv loads asynchronously
	private bool _seekDragging, _updatingSeekBar, _syncingUi;
	private bool _fullscreen;
	private WindowState _prevState;
	private WindowStyle _prevStyle;

	public MainWindow()
	{
		InitializeComponent();
		_timer.Tick += OnTick;
		_statusTimer.Tick += (_, _) => { StatusText.Text = ""; StatusText.Visibility = Visibility.Collapsed; _statusTimer.Stop(); };
		SeekBar.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSeekBarDown), true);

		Rect work = SystemParameters.WorkArea;
		Width = Math.Min(Width, work.Width * 0.9);
		Height = Math.Min(Height, work.Height * 0.9);
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
	}

	// ------------------------------------------------------------------------------------------
	// Lifetime
	// ------------------------------------------------------------------------------------------

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		try
		{
			_a = new MpvPlayer(HostA.Handle);
		}
		catch (DllNotFoundException)
		{
			MessageBox.Show(this, "libmpv-2.dll が見つかりません。ProResPlayer.exe と同じフォルダに置いてください。", "ProRes Player",
				MessageBoxButton.OK, MessageBoxImage.Error);
			Close();
			return;
		}

		HostA.Clicked += (_, _) => { FocusSelf(); TogglePlay(); };
		HostA.DoubleClicked += (_, _) => { FocusSelf(); TogglePlay(); ToggleFullscreen(); };
		HostB.Clicked += (_, _) => { FocusSelf(); TogglePlay(); };
		HostB.DoubleClicked += (_, _) => { FocusSelf(); TogglePlay(); ToggleFullscreen(); };

		SetBackground(_settings.Background is "checker" or "black" or "white" or "alpha" ? _settings.Background : "black");
		SetLoopFile(LoopCheck.IsChecked == true);	// loop is on by default
		SetInfoVisible(_settings.InfoPanel);	// restore last open / closed state
		UpdateAssociationMenu();

		string[] files = App.StartupFiles.Where(File.Exists).ToArray();
		if (files.Length > 0)
			OpenA(files[0]);
		if (files.Length > 1)
			OpenB(files[1]);

		_timer.Start();
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		_timer.Stop();
		_b?.Dispose();
		_a?.Dispose();
	}

	// mpv's window lives on another thread; take keyboard focus back after clicks on the video.
	private void FocusSelf()
	{
		Activate();
		Keyboard.Focus(this);
	}

	// ------------------------------------------------------------------------------------------
	// Opening files
	// ------------------------------------------------------------------------------------------

	private void OpenA(string path)
	{
		if (_a == null)
			return;
		_a.Load(path);
		_a.Paused = false;	// start playing as soon as a file is opened
		_infoPathA = null;
		ClearLoop();
		Title = "ProRes Player - " + Path.GetFileName(path);
		UpdatePlaylist(path);
		if (_b != null && _b.HasFile)
			_b.SeekAbsolute(0);
	}

	// ------------------------------------------------------------------------------------------
	// Previous / next movie in the same folder
	// ------------------------------------------------------------------------------------------

	private readonly FolderPlaylist _playlist = new();

	private void UpdatePlaylist(string path)
	{
		_playlist.Update(path);
		string? prev = _playlist.Neighbor(-1), next = _playlist.Neighbor(1);
		PrevFileButton.IsEnabled = prev != null;
		NextFileButton.IsEnabled = next != null;
		PrevFileButton.ToolTip = (prev != null ? Path.GetFileName(prev) + "\n" : "") + "同じフォルダの前の動画 (Ctrl+← / PageUp)";
		NextFileButton.ToolTip = (next != null ? Path.GetFileName(next) + "\n" : "") + "同じフォルダの次の動画 (Ctrl+→ / PageDown)";
		FilePosText.Text = _playlist.Count > 1 ? $"{_playlist.Index + 1} / {_playlist.Count}" : "";
	}

	private void OpenNeighbor(int delta)
	{
		if (_a?.Path is not string current)
			return;
		_playlist.Update(current);	// pick up files added or removed since the last scan
		if (_playlist.Neighbor(delta) is string target)
			OpenA(target);
		else
			ShowStatus(delta < 0 ? "最初の動画です" : "最後の動画です");
	}

	private void OnPrevFile(object sender, RoutedEventArgs e) => OpenNeighbor(-1);
	private void OnNextFile(object sender, RoutedEventArgs e) => OpenNeighbor(1);

	private void OpenB(string path)
	{
		EnsureB();
		_b!.Load(path);
		_pathB = path;
		_infoPathB = null;
		if (_compare == CompareMode.Off)
			SetCompare(CompareMode.Side);
		Dispatcher.BeginInvoke(SyncBToA, DispatcherPriority.Background);
	}

	private void EnsureB()
	{
		if (_b != null)
			return;
		HostB.Visibility = Visibility.Visible;
		UpdateLayout();
		_b = new MpvPlayer(HostB.Handle);
		_b.Set("mute", "yes");
		_b.Set("loop-file", LoopCheck.IsChecked == true ? "inf" : "no");
		ApplyBackground(_b);
	}

	private void OnOpen(object sender, RoutedEventArgs e)
	{
		var dlg = new OpenFileDialog { Filter = FileFilter };
		if (dlg.ShowDialog(this) == true)
			OpenA(dlg.FileName);
	}

	private void OnOpenB(object sender, RoutedEventArgs e)
	{
		var dlg = new OpenFileDialog { Filter = FileFilter, Title = "比較する動画 (B) を開く" };
		if (dlg.ShowDialog(this) == true)
			OpenB(dlg.FileName);
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	// Drops on the WPF chrome. Drops directly on the video go to that mpv instance (drag-and-drop=replace)
	// and are picked up by the path polling in OnTick.
	private void OnDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
			return;
		OpenA(files[0]);
		if (files.Length > 1)
			OpenB(files[1]);
	}

	private void OnExit(object sender, RoutedEventArgs e) => Close();

	// ------------------------------------------------------------------------------------------
	// Transport (applied to A, mirrored to B while comparing)
	// ------------------------------------------------------------------------------------------

	private bool Comparing => _compare != CompareMode.Off && _b != null && _pathB != null;

	private void TogglePlay()
	{
		if (_a == null || !_a.HasFile)
			return;
		bool pause = !_a.Paused;
		// Pressing play on the last frame (loop off, or after stepping there) starts over from the top.
		if (!pause && AtEnd())
			SeekTo(0);
		_a.Paused = pause;
		if (Comparing)
		{
			_b!.Paused = pause;
			if (pause)
				SyncBToA();
		}
	}

	private bool AtEnd()
	{
		if (_a!.Get("eof-reached") == "yes")
			return true;
		double fps = _a.Fps, dur = _a.Duration;
		return fps > 0 && dur > 0 && _a.Time >= dur - 1.5 / fps;
	}

	private void Pause()
	{
		if (_a == null)
			return;
		_a.Paused = true;
		if (Comparing)
		{
			_b!.Paused = true;
			SyncBToA();
		}
	}

	private long? _stepTarget;
	private DateTime _stepTime;

	/// <summary>
	/// Frame step by seeking to an absolute frame. mpv's frame-step drops presses that arrive while a
	/// step is still in flight, so rapid key repeats are accumulated here instead.
	/// </summary>
	private void Step(int dir)
	{
		if (_a == null || !_a.HasFile)
			return;
		double fps = _a.Fps;
		Pause();
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
		SeekTo(FrameTime(target, fps));
	}

	// A hair past the frame's pts so rounding never lands on the previous frame.
	private static double FrameTime(long frame, double fps) => (frame + 0.01) / fps;

	private void SeekTo(double t)
	{
		if (_a == null || !_a.HasFile)
			return;
		_a.SeekAbsolute(Math.Max(0, t));
		if (Comparing)
			_b!.SeekAbsolute(Math.Max(0, t));
	}

	private void SeekBy(double dt) => SeekTo(_a!.Time + dt);

	private void SyncBToA()
	{
		if (_a != null && Comparing)
			_b!.SeekAbsolute(_a.Time);
	}

	private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlay();
	private void OnStepBack(object sender, RoutedEventArgs e) => Step(-1);
	private void OnStepForward(object sender, RoutedEventArgs e) => Step(1);
	private void OnGoStart(object sender, RoutedEventArgs e) { Pause(); SeekTo(0); }
	private void OnGoEnd(object sender, RoutedEventArgs e) => GoEnd();

	private void GoEnd()
	{
		if (_a == null || !_a.HasFile)
			return;
		Pause();
		double fps = _a.Fps;
		SeekTo(Math.Max(0, _a.Duration - (fps > 0 ? 1.0 / fps : 0.04)));
	}

	// Seek bar scrubbing. The slider's own dragging only works on the thumb, so the mouse is handled
	// here: press anywhere on the bar, drag to scrub (playback is held meanwhile), release to land
	// exactly on the frame and resume if it was playing.
	private bool _resumeAfterScrub;

	private void OnSeekBarDown(object sender, MouseButtonEventArgs e)
	{
		if (_a == null || !_a.HasFile)
			return;
		_seekDragging = true;
		_resumeAfterScrub = !_a.Paused;
		Pause();
		SeekBar.CaptureMouse();
		ScrubTo(e.GetPosition(SeekBar), exact: false);
		e.Handled = true;
	}

	private void OnSeekBarMove(object sender, MouseEventArgs e)
	{
		if (_seekDragging && e.LeftButton == MouseButtonState.Pressed)
			ScrubTo(e.GetPosition(SeekBar), exact: false);
	}

	private void OnSeekBarUp(object sender, MouseButtonEventArgs e)
	{
		if (!_seekDragging)
			return;
		ScrubTo(e.GetPosition(SeekBar), exact: true);
		_seekDragging = false;
		SeekBar.ReleaseMouseCapture();
		if (_resumeAfterScrub)
		{
			_a!.Paused = false;
			if (Comparing)
				_b!.Paused = false;
		}
		e.Handled = true;
	}

	private void ScrubTo(Point p, bool exact)
	{
		if (_a == null)
			return;
		double t;
		// Map the cursor to the value whose thumb centre sits under it. Track.ValueFromPoint is not used:
		// it works relative to the thumb's current position, so the first click landed off the cursor.
		if (SeekBar.Template.FindName("PART_Track", SeekBar) is System.Windows.Controls.Primitives.Track track
			&& track.ActualWidth > 0)
		{
			double thumbW = track.Thumb?.ActualWidth ?? 0;
			double x = SeekBar.TranslatePoint(p, track).X;
			double usable = Math.Max(1, track.ActualWidth - thumbW);
			t = (x - thumbW / 2) / usable * (SeekBar.Maximum - SeekBar.Minimum) + SeekBar.Minimum;
		}
		else
			t = p.X / Math.Max(1, SeekBar.ActualWidth) * SeekBar.Maximum;
		t = Math.Clamp(t, 0, SeekBar.Maximum);

		_updatingSeekBar = true;
		SeekBar.Value = t;
		_updatingSeekBar = false;

		if (exact)
			SeekTo(t);
		else
		{
			_a.SeekScrub(t);
			if (Comparing)
				_b!.SeekScrub(t);
		}
	}

	private void OnSeekBarChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		// Keyboard / programmatic changes only; mouse scrubbing is handled above.
	}

	// ------------------------------------------------------------------------------------------
	// Frame export
	// ------------------------------------------------------------------------------------------

	private long CurrentFrame => _a != null && _a.Fps > 0 ? (long)Math.Round(_a.Time * _a.Fps) : 0;

	private void ShowStatus(string text, bool sticky = false)
	{
		StatusText.Text = text;
		StatusText.Visibility = Visibility.Visible;
		_statusTimer.Stop();
		if (!sticky)
			_statusTimer.Start();
	}

	private void OnSaveFrame(object sender, RoutedEventArgs e) => SaveFrame();

	private void SaveFrame()
	{
		if (_a?.Path is not string video)
			return;
		Pause();
		var dlg = new SaveFileDialog
		{
			Filter = "PNG (*.png)|*.png",
			FileName = $"{Path.GetFileNameWithoutExtension(video)}_f{CurrentFrame:00000}.png",
			InitialDirectory = _lastSaveDir ?? Path.GetDirectoryName(video),
		};
		if (dlg.ShowDialog(this) != true)
			return;
		_lastSaveDir = Path.GetDirectoryName(dlg.FileName);
		if (File.Exists(dlg.FileName))
			File.Delete(dlg.FileName);	// mpv does not overwrite
		ShowStatus(_a.Screenshot(dlg.FileName) ? "保存しました: " + Path.GetFileName(dlg.FileName) : "保存に失敗しました");
	}

	private void OnCopyFrame(object sender, RoutedEventArgs e) => CopyFrame();

	/// <summary>
	/// Puts the frame on the clipboard as "PNG" (keeps alpha; Photoshop, Office, browsers) and as a
	/// regular bitmap for everything else.
	/// </summary>
	private void CopyFrame()
	{
		if (_a == null || !_a.HasFile)
			return;
		Pause();
		string tmp = Path.Combine(Path.GetTempPath(), "ProResPlayer_clipboard.png");
		if (File.Exists(tmp))
			File.Delete(tmp);
		// mpv's 8-bit screenshots drop alpha, so grab 16-bit RGBA and reduce to 8-bit BGRA here.
		if (!_a.Screenshot(tmp, highBitDepth: true))
		{
			ShowStatus("コピーに失敗しました");
			return;
		}
		BitmapSource src;
		using (var fs = File.OpenRead(tmp))
			src = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
		var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
		bgra.Freeze();

		var png = new MemoryStream();
		var enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(bgra));
		enc.Save(png);
		png.Position = 0;

		var data = new DataObject();
		data.SetData("PNG", png);
		data.SetImage(bgra);
		Clipboard.SetDataObject(data, true);
		ShowStatus($"コピーしました (フレーム {CurrentFrame})");
	}

	// ------------------------------------------------------------------------------------------
	// Loop (mpv A-B loop)
	// ------------------------------------------------------------------------------------------

	private void SetLoopPoint(string prop)
	{
		if (_a == null || !_a.HasFile)
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
		_a?.Set("loop-file", on ? "inf" : "no");
		_b?.Set("loop-file", on ? "inf" : "no");
		LoopCheck.IsChecked = on;
	}

	// ------------------------------------------------------------------------------------------
	// Cut: keep IN..OUT and save it without re-encoding
	// ------------------------------------------------------------------------------------------

	private bool _cutting;

	private void OnCut(object sender, RoutedEventArgs e) => _ = CutAsync();

	private async Task CutAsync()
	{
		if (_cutting || _a?.Path is not string source || !_a.HasFile)
			return;
		if (!File.Exists(source))
		{
			ShowStatus("ローカルのファイルだけカットできます");
			return;
		}
		double fps = _a.Fps, dur = _a.Duration;
		double? a = _a.GetDouble("ab-loop-a"), b = _a.GetDouble("ab-loop-b");
		if (fps <= 0 || dur <= 0)
		{
			ShowStatus("この動画はカットできません");
			return;
		}
		if (!a.HasValue && !b.HasValue)
		{
			ShowStatus("先に IN / OUT (I / O キー) で範囲を指定してください");
			return;
		}
		if (!Cutter.Available)
		{
			MessageBox.Show(this, "ffmpeg.exe が見つかりません:\n" + Cutter.FfmpegPath, "ProRes Player", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}
		Pause();

		long total = Math.Max(1, (long)Math.Round(dur * fps));
		long first = a.HasValue ? Math.Clamp((long)Math.Round(a.Value * fps), 0, total - 1) : 0;
		long last = b.HasValue ? Math.Clamp((long)Math.Round(b.Value * fps), 0, total - 1) : total - 1;	// OUT frame is kept
		if (last < first)
			(first, last) = (last, first);
		long count = last - first + 1;

		string codec = _a.Get("current-tracks/video/codec") ?? _a.Get("video-codec") ?? "";
		string? warning = Cutter.IsIntraOnly(codec) ? null :
			$"この動画 ({codec}) はキーフレームの位置でしか切れないため、IN より少し前から、OUT より少し後まで入ることがあります。";
		string range = $"{Timecode(first, fps)} 〜 {Timecode(last, fps)}   ({first} 〜 {last} f、{count} フレーム)";

		var dlg = new CutDialog(this, Path.GetFileName(source), range, warning);
		dlg.ShowDialog();
		if (dlg.Choice == CutChoice.Cancel)
			return;

		string target = source;
		if (dlg.Choice == CutChoice.SaveAs)
		{
			string ext = Path.GetExtension(source);
			var save = new SaveFileDialog
			{
				Filter = $"{ext.TrimStart('.').ToUpperInvariant()} (*{ext})|*{ext}",
				FileName = Path.GetFileNameWithoutExtension(source) + "_cut" + ext,
				InitialDirectory = Path.GetDirectoryName(source),
			};
			if (save.ShowDialog(this) != true)
				return;
			target = save.FileName;
		}
		bool overwrite = string.Equals(Path.GetFullPath(target), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase);

		// Write next to the target first, so a failed cut never destroys an existing file.
		string tmp = Path.Combine(Path.GetDirectoryName(target)!, "~" + Path.GetFileNameWithoutExtension(target) + ".cutting" + Path.GetExtension(target));
		_cutting = true;
		ShowStatus("カット中...", sticky: true);
		try
		{
			string? error = await Cutter.CutAsync(source, tmp, first, count, fps);
			if (error != null)
			{
				TryDelete(tmp);
				ShowStatus("カットに失敗しました");
				MessageBox.Show(this, error, "カットに失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			if (overwrite)
			{
				// mpv keeps the file open; unload it (and B, if it shows the same file) before replacing.
				_a.Command("stop");
				if (_b != null && string.Equals(_pathB, source, StringComparison.OrdinalIgnoreCase))
					_b.Command("stop");
			}
			if (!await MoveWithRetryAsync(tmp, target))
			{
				ShowStatus("保存に失敗しました");
				MessageBox.Show(this, $"{Path.GetFileName(target)} を置き換えられませんでした（ほかのアプリで開かれている可能性があります）。\n\nカット結果は次の名前で残してあります:\n{tmp}",
					"ProRes Player", MessageBoxButton.OK, MessageBoxImage.Warning);
				if (overwrite)
					OpenA(source);
				return;
			}

			if (overwrite)
			{
				OpenA(source);
				if (_b != null && string.Equals(_pathB, source, StringComparison.OrdinalIgnoreCase))
					_b.Load(source);
			}
			else
				UpdatePlaylist(source);	// the new file joins the folder list
			ShowStatus($"保存しました: {Path.GetFileName(target)} ({count} フレーム)");
		}
		finally
		{
			_cutting = false;
		}
	}

	private static async Task<bool> MoveWithRetryAsync(string from, string to)
	{
		for (int i = 0; i < 30; i++)
		{
			try
			{
				File.Move(from, to, overwrite: true);
				return true;
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
			await Task.Delay(100);
		}
		return false;
	}

	private static void TryDelete(string path)
	{
		try { File.Delete(path); } catch { }
	}

	private void OnSetIn(object sender, RoutedEventArgs e) => SetLoopPoint("ab-loop-a");
	private void OnSetOut(object sender, RoutedEventArgs e) => SetLoopPoint("ab-loop-b");
	private void OnClearLoop(object sender, RoutedEventArgs e) => ClearLoop();
	private void OnLoopCheck(object sender, RoutedEventArgs e) => SetLoopFile(LoopCheck.IsChecked == true);

	// ------------------------------------------------------------------------------------------
	// Background / alpha
	// ------------------------------------------------------------------------------------------

	private void SetBackground(string mode)
	{
		_background = mode;
		if (_settings.Background != mode)
		{
			_settings.Background = mode;
			_settings.Save();
		}
		ApplyBackground(_a);
		ApplyBackground(_b);
		_syncingUi = true;
		foreach (ComboBoxItem item in BackgroundBox.Items)
			item.IsSelected = (string)item.Tag == mode;
		_syncingUi = false;
	}

	private void ApplyBackground(MpvPlayer? p)
	{
		if (p == null)
			return;
		bool alphaOnly = _background == "alpha";
		p.Command("vf", "remove", "@alpha");
		if (alphaOnly)
			p.Command("vf", "add", "@alpha:lavfi=[alphaextract]");

		switch (_background)
		{
			case "checker":
				// mpv >= 0.38: background=tiles; older builds: alpha=blend-tiles.
				if (p.Set("background", "tiles") < 0)
					p.Set("alpha", "blend-tiles");
				break;
			case "white":
				SetSolid(p, "#FFFFFF");
				break;
			default:	// black, alpha
				SetSolid(p, "#000000");
				break;
		}
	}

	private static void SetSolid(MpvPlayer p, string color)
	{
		p.Set("background-color", color);
		if (p.Set("background", "color") < 0)
			p.Set("alpha", "blend");
	}

	private void OnBackgroundBox(object sender, SelectionChangedEventArgs e)
	{
		if (!_syncingUi && BackgroundBox.SelectedItem is ComboBoxItem item && _a != null)
			SetBackground((string)item.Tag);
	}

	private void OnBackgroundMenu(object sender, RoutedEventArgs e) => SetBackground((string)((FrameworkElement)sender).Tag);

	// ------------------------------------------------------------------------------------------
	// Compare layout
	// ------------------------------------------------------------------------------------------

	private void SetCompare(CompareMode mode)
	{
		if (mode != CompareMode.Off && _pathB == null)
		{
			var dlg = new OpenFileDialog { Filter = FileFilter, Title = "比較する動画 (B) を開く" };
			if (dlg.ShowDialog(this) != true)
				mode = CompareMode.Off;
			else
			{
				_compare = mode;	// set first so OpenB does not switch to Side
				OpenB(dlg.FileName);
			}
		}

		_compare = mode;
		_syncingUi = true;
		foreach (ComboBoxItem item in CompareBox.Items)
			item.IsSelected = (string)item.Tag == mode.ToString().ToLowerInvariant();
		_syncingUi = false;

		WipeSlider.Visibility = mode == CompareMode.Wipe ? Visibility.Visible : Visibility.Collapsed;
		InfoBPanel.Visibility = mode == CompareMode.Off ? Visibility.Collapsed : Visibility.Visible;
		if (mode == CompareMode.Off && _b != null)
			_b.Paused = true;
		else
			SyncBToA();
		LayoutVideo();
	}

	private void OnCompareBox(object sender, SelectionChangedEventArgs e)
	{
		if (_syncingUi || CompareBox.SelectedItem is not ComboBoxItem item || _a == null)
			return;
		SetCompare(ParseCompare((string)item.Tag));
	}

	private void OnCompareMenu(object sender, RoutedEventArgs e) => SetCompare(ParseCompare((string)((FrameworkElement)sender).Tag));

	private static CompareMode ParseCompare(string tag) => tag switch { "side" => CompareMode.Side, "wipe" => CompareMode.Wipe, _ => CompareMode.Off };

	private void OnWipeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => LayoutVideo();

	private void OnVideoAreaSizeChanged(object sender, SizeChangedEventArgs e) => LayoutVideo();

	/// <summary>
	/// Positions host A and B. In wipe mode each host only covers its part of the area, and
	/// video-zoom / video-pan-x make mpv draw the frame as if the host spanned the whole area,
	/// so the two halves line up exactly at the divider.
	/// </summary>
	private void LayoutVideo()
	{
		// Slider/ComboBox events fire while the XAML is still being loaded.
		if (!IsLoaded)
			return;
		double w = VideoArea.ActualWidth, h = VideoArea.ActualHeight;
		if (w <= 0 || h <= 0)
			return;

		switch (_compare)
		{
			case CompareMode.Off:
				HostA.HorizontalAlignment = HorizontalAlignment.Stretch;
				HostA.Width = double.NaN;
				if (_b != null)
					HostB.Visibility = Visibility.Collapsed;
				ResetView(_a);
				break;

			case CompareMode.Side:
			{
				double half = Math.Max(1, (w - Gap) / 2);
				HostA.HorizontalAlignment = HorizontalAlignment.Left;
				HostA.Width = half;
				HostB.HorizontalAlignment = HorizontalAlignment.Right;
				HostB.Width = half;
				HostB.Visibility = Visibility.Visible;
				ResetView(_a);
				ResetView(_b);
				break;
			}

			case CompareMode.Wipe:
			{
				double x = Math.Clamp(WipeSlider.Value, 0, 1) * w;
				double aRight = Math.Clamp(x - Gap / 2, 1, w - 1);
				double bLeft = Math.Clamp(x + Gap / 2, 1, w - 1);
				HostA.HorizontalAlignment = HorizontalAlignment.Left;
				HostA.Width = aRight;
				HostB.HorizontalAlignment = HorizontalAlignment.Right;
				HostB.Width = w - bLeft;
				HostB.Visibility = Visibility.Visible;
				AlignAsFull(_a, 0, aRight, w, h);
				AlignAsFull(_b, bLeft, w, w, h);
				break;
			}
		}
	}

	private static void ResetView(MpvPlayer? p)
	{
		if (p == null)
			return;
		p.Set("video-zoom", 0);
		p.Set("video-pan-x", 0);
	}

	// Host spans [left, right] of an area that is fullW wide.
	private static void AlignAsFull(MpvPlayer? p, double left, double right, double fullW, double h)
	{
		if (p == null)
			return;
		double dw = p.GetDouble("video-params/dw") ?? 0, dh = p.GetDouble("video-params/dh") ?? 0;
		if (dw <= 0 || dh <= 0)
			return;
		double hostW = Math.Max(1, right - left);
		double sFull = Math.Min(fullW / dw, h / dh);
		double sHost = Math.Min(hostW / dw, h / dh);
		double offsetPx = (fullW - left - right) / 2;
		p.Set("video-zoom", Math.Log2(sFull / sHost));
		p.Set("video-pan-x", offsetPx / (dw * sFull));
	}

	// ------------------------------------------------------------------------------------------
	// Polling: time display, seek bar, info, A/B drift
	// ------------------------------------------------------------------------------------------

	private int _layoutRetry;

	private void OnTick(object? sender, EventArgs e)
	{
		if (_a == null)
			return;

		string? pathA = _a.Path;
		if (pathA != null && pathA != _infoPathA && _a.GetDouble("video-params/dw") is > 0)
		{
			_infoPathA = pathA;
			InfoA.Text = BuildInfo(_a);
			Title = "ProRes Player - " + Path.GetFileName(pathA);
			UpdatePlaylist(pathA);	// also covers files dropped straight onto the video
			LayoutVideo();
		}
		if (_b != null)
		{
			string? pathB = _b.Path;
			if (pathB != null && pathB != _infoPathB && _b.GetDouble("video-params/dw") is > 0)
			{
				_infoPathB = pathB;
				InfoB.Text = BuildInfo(_b);
				LayoutVideo();
			}
		}
		// Video params can arrive after the first layout pass; redo the wipe alignment a few times.
		if (_compare == CompareMode.Wipe && _layoutRetry++ % 15 == 0)
			LayoutVideo();

		if (!_a.HasFile)
			return;

		double t = _a.Time, dur = _a.Duration, fps = _a.Fps;
		bool paused = _a.Paused;
		PlayButton.Content = paused ? "▶" : "❚❚";

		if (!_seekDragging)
		{
			_updatingSeekBar = true;
			SeekBar.Maximum = Math.Max(dur, 0.001);
			SeekBar.Value = Math.Clamp(t, 0, SeekBar.Maximum);
			_updatingSeekBar = false;
		}

		long frame = fps > 0 ? (long)Math.Round(t * fps) : 0;
		long total = fps > 0 ? (long)Math.Round(dur * fps) : 0;
		TimecodeText.Text = Timecode(frame, fps);
		FrameText.Text = $"{frame} / {Math.Max(0, total - 1)} f   {fps:0.###} fps";

		UpdateLoopRect(dur);

		// Keep B within a frame of A while playing.
		if (Comparing && !paused && fps > 0)
		{
			double tb = _b!.Time;
			if (Math.Abs(tb - t) > 1.5 / fps)
				_b.SeekAbsolute(t);
			if (_b.Paused)
				_b.Paused = false;
		}
	}

	private void UpdateLoopRect(double dur)
	{
		double? a = _a!.GetDouble("ab-loop-a"), b = _a.GetDouble("ab-loop-b");
		if (dur <= 0 || (!a.HasValue && !b.HasValue))
		{
			LoopRect.Visibility = Visibility.Collapsed;
			return;
		}
		double w = LoopCanvas.ActualWidth;
		double x0 = (a ?? 0) / dur * w, x1 = (b ?? dur) / dur * w;
		Canvas.SetLeft(LoopRect, x0);
		LoopRect.Width = Math.Max(2, x1 - x0);
		LoopRect.Visibility = Visibility.Visible;
	}

	private static string Timecode(long frame, double fps)
	{
		int nominal = Math.Max(1, (int)Math.Round(fps));
		long ff = frame % nominal, totalSec = frame / nominal;
		return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}:{3:00}",
			totalSec / 3600, totalSec / 60 % 60, totalSec % 60, ff);
	}

	private static string BuildInfo(MpvPlayer p)
	{
		var sb = new StringBuilder();
		void Line(string label, string? value)
		{
			if (!string.IsNullOrEmpty(value))
				sb.Append(label.PadRight(9)).Append(value).Append('\n');
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
		double? br = p.GetDouble("current-tracks/video/demux-bitrate") ?? p.GetDouble("video-bitrate");
		if (br is > 0)
			Line("ビットレート", $"{br / 1e6:0.0} Mbps");
		return sb.ToString();
	}

	// ------------------------------------------------------------------------------------------
	// View
	// ------------------------------------------------------------------------------------------

	private void OnToggleInfo(object sender, RoutedEventArgs e) => SetInfoVisible(InfoItem.IsChecked);

	private void OnInfoButton(object sender, RoutedEventArgs e) => SetInfoVisible(!InfoItem.IsChecked);

	private void SetInfoVisible(bool on)
	{
		InfoItem.IsChecked = on;
		if (_settings.InfoPanel != on)
		{
			_settings.InfoPanel = on;
			_settings.Save();
		}
		InfoPanel.Visibility = on && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
		// The arrow points the way the panel will move: ▶ hides it to the right, ◀ brings it back.
		InfoButton.Content = on ? "情報 ▶" : "◀ 情報";
	}

	private void OnControlGridSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (e.WidthChanged)
			ArrangeControlRows();
	}

	/// <summary>
	/// One row when everything fits; otherwise the right group (view / export / info) drops to a second
	/// row, and on very narrow windows the loop / cut group joins it, so no button is ever clipped.
	/// </summary>
	private void ArrangeControlRows()
	{
		static double Need(UIElement el)
		{
			el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			return el.DesiredSize.Width;
		}
		double w = ControlGrid.ActualWidth;
		double left = Need(LeftGroup), right = Need(RightGroup);
		double center = Need(((Panel)CenterGroup).Children[0]) + 16;	// loop box + margins; the status line may be trimmed

		static void Place(UIElement el, int row, int col, int span)
		{
			Grid.SetRow(el, row);
			Grid.SetColumn(el, col);
			Grid.SetColumnSpan(el, span);
		}
		if (left + center + right <= w)
		{
			Place(LeftGroup, 0, 0, 1);
			Place(CenterGroup, 0, 1, 1);
			Place(RightGroup, 0, 2, 1);
			RightGroup.Margin = new Thickness(0);
		}
		else if (left + center <= w)
		{
			Place(LeftGroup, 0, 0, 1);
			Place(CenterGroup, 0, 1, 1);
			Place(RightGroup, 1, 0, 3);
			RightGroup.Margin = new Thickness(0, 6, 0, 0);
		}
		else if (center + right <= w)
		{
			Place(LeftGroup, 0, 0, 3);
			Place(CenterGroup, 1, 0, 1);
			Place(RightGroup, 1, 2, 1);
			RightGroup.Margin = new Thickness(0, 6, 0, 0);
		}
		else
		{
			Place(LeftGroup, 0, 0, 3);
			Place(CenterGroup, 1, 0, 3);
			Place(RightGroup, 2, 0, 3);
			RightGroup.Margin = new Thickness(0, 6, 0, 0);
		}
		bool ownRow = Grid.GetRow(CenterGroup) == 1;
		CenterGroup.Margin = ownRow ? new Thickness(0, 6, 8, 0) : new Thickness(8, 0, 8, 0);
		CenterGroup.HorizontalAlignment = ownRow ? HorizontalAlignment.Left : HorizontalAlignment.Center;
	}

	private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

	private void ToggleFullscreen()
	{
		_fullscreen = !_fullscreen;
		if (_fullscreen)
		{
			_prevState = WindowState;
			_prevStyle = WindowStyle;
			WindowStyle = WindowStyle.None;
			if (WindowState == WindowState.Maximized)
				WindowState = WindowState.Normal;	// re-maximize so the taskbar is covered
			WindowState = WindowState.Maximized;
			MainMenu.Visibility = Visibility.Collapsed;
			ControlBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			WindowStyle = _prevStyle;
			WindowState = _prevState;
			MainMenu.Visibility = Visibility.Visible;
			ControlBar.Visibility = Visibility.Visible;
		}
		SetInfoVisible(InfoItem.IsChecked);
	}

	// ------------------------------------------------------------------------------------------
	// File association
	// ------------------------------------------------------------------------------------------

	private void UpdateAssociationMenu()
	{
		AssocItem.Header = FileAssociation.IsRegistered() ? "「プログラムから開く」から登録解除" : "「プログラムから開く」に登録";
	}

	private void OnToggleAssociation(object sender, RoutedEventArgs e)
	{
		if (FileAssociation.IsRegistered())
		{
			FileAssociation.Unregister();
			MessageBox.Show(this, "登録を解除しました。", "ProRes Player");
		}
		else
		{
			FileAssociation.Register();
			MessageBox.Show(this,
				"登録しました（" + string.Join(" ", FileAssociation.Extensions) + "）。\n\n" +
				"既定にするには、エクスプローラーで .mov を右クリック →「プログラムから開く」→「別のプログラムを選択」で " +
				"ProRes Player を選び、「常に使う」を押してください。",
				"ProRes Player");
		}
		UpdateAssociationMenu();
	}

	// ------------------------------------------------------------------------------------------
	// Keyboard
	// ------------------------------------------------------------------------------------------

	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
		bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		bool handled = true;

		switch (e.Key)
		{
			case Key.S when ctrl: SaveFrame(); break;
			case Key.C when ctrl: CopyFrame(); break;
			case Key.X when ctrl: _ = CutAsync(); break;
			case Key.O when ctrl && shift: OnOpenB(this, e); break;
			case Key.O when ctrl: OnOpen(this, e); break;
			case Key.Space: TogglePlay(); break;
			case Key.Left when ctrl: OpenNeighbor(-1); break;
			case Key.Right when ctrl: OpenNeighbor(1); break;
			case Key.PageUp: OpenNeighbor(-1); break;
			case Key.PageDown: OpenNeighbor(1); break;
			case Key.Left when shift: SeekBy(-1); break;
			case Key.Right when shift: SeekBy(1); break;
			case Key.Left: Step(-1); break;
			case Key.Right: Step(1); break;
			case Key.Up: SeekBy(10); break;
			case Key.Down: SeekBy(-10); break;
			case Key.Home: Pause(); SeekTo(0); break;
			case Key.End: GoEnd(); break;
			case Key.I: SetLoopPoint("ab-loop-a"); break;
			case Key.O: SetLoopPoint("ab-loop-b"); break;
			case Key.P: ClearLoop(); break;
			case Key.L: SetLoopFile(LoopCheck.IsChecked != true); break;
			case Key.D1: case Key.NumPad1: SetBackground("checker"); break;
			case Key.D2: case Key.NumPad2: SetBackground("black"); break;
			case Key.D3: case Key.NumPad3: SetBackground("white"); break;
			case Key.D4: case Key.NumPad4: SetBackground("alpha"); break;
			case Key.A: CycleBackground(); break;
			case Key.C: SetCompare(_compare switch { CompareMode.Off => CompareMode.Side, CompareMode.Side => CompareMode.Wipe, _ => CompareMode.Off }); break;
			case Key.OemOpenBrackets: WipeSlider.Value = Math.Max(0, WipeSlider.Value - 0.05); break;
			case Key.OemCloseBrackets: WipeSlider.Value = Math.Min(1, WipeSlider.Value + 0.05); break;
			case Key.Tab: SetInfoVisible(!InfoItem.IsChecked); break;
			case Key.F: case Key.F11: ToggleFullscreen(); break;
			case Key.Escape when _fullscreen: ToggleFullscreen(); break;
			default: handled = false; break;
		}
		e.Handled = handled;
	}

	private void CycleBackground()
	{
		string[] order = { "checker", "black", "white", "alpha" };
		SetBackground(order[(Array.IndexOf(order, _background) + 1) % order.Length]);
	}

	private void OnShortcuts(object sender, RoutedEventArgs e)
	{
		MessageBox.Show(this,
			"Space        再生 / 一時停止\n" +
			"← / →        1フレーム戻る / 進む\n" +
			"Shift+← / →  1秒戻る / 進む\n" +
			"↑ / ↓        10秒進む / 戻る\n" +
			"Home / End   先頭 / 末尾\n" +
			"Ctrl+← / →   同じフォルダの前 / 次の動画（PageUp / PageDown でも可）\n" +
			"I / O / P    ループ開始 / 終了 / クリア\n" +
			"Ctrl+X       IN〜OUT だけ残して保存（再エンコードなし）\n" +
			"L            ループ再生 オン/オフ\n" +
			"1 2 3 4 / A  背景: 市松 / 黒 / 白 / アルファのみ / 切り替え\n" +
			"C            比較: オフ → 左右 → ワイプ\n" +
			"[ / ]        ワイプ位置\n" +
			"Tab          情報パネル\n" +
			"F / F11      全画面 (Esc で戻る)\n" +
			"Ctrl+O       開く\n" +
			"Ctrl+Shift+O 比較用 (B) を開く\n" +
			"Ctrl+S       フレームを PNG で保存\n" +
			"Ctrl+C       フレームをコピー\n\n" +
			"動画の上でクリック: 再生/一時停止、ダブルクリック: 全画面\n" +
			"ファイルを2つ同時にドロップすると A と B に読み込みます。",
			"ショートカット");
	}
}
