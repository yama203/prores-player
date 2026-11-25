using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;

namespace ProResPlayer;

/// <summary>
/// Draw on a grabbed frame (pen, arrow, line, rectangle, ellipse, text) and copy / save the result,
/// optionally with a strip below that names the file, frame, timecode and time.
/// Everything is laid out in image pixels inside a Viewbox, so the export is at the frame's resolution.
/// </summary>
public partial class MarkupWindow : Window
{
	private enum Tool { Pen, Arrow, Line, Rect, Ellipse, Text }

	private static readonly (string Name, Color Color)[] Palette =
	{
		("赤", Color.FromRgb(0xFF, 0x3B, 0x30)),
		("黄", Color.FromRgb(0xFF, 0xD6, 0x0A)),
		("緑", Color.FromRgb(0x34, 0xC7, 0x59)),
		("青", Color.FromRgb(0x0A, 0x84, 0xFF)),
		("白", Color.FromRgb(0xFF, 0xFF, 0xFF)),
		("黒", Color.FromRgb(0x10, 0x10, 0x10)),
	};

	private readonly string _baseName;
	private readonly string? _saveDir;
	private readonly double _unit;	// 1 at 1280 px on the long side; scales strokes and text with the frame
	private Tool _tool = Tool.Arrow;
	private Color _color = Palette[0].Color;
	private readonly List<Control> _items = new();
	private readonly Dictionary<Tool, ToggleButton> _toolButtons;
	private readonly List<ToggleButton> _colorButtons = new();

	private Point _start;
	private Control? _current;	// shape being dragged
	private List<Point>? _penPoints;
	private TextBox? _editing;

	public MarkupWindow() : this(new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96)), "black", "", "frame", null) { }

	/// <param name="frame">The frame (may have alpha).</param>
	/// <param name="background">checker / white / anything else = black: what transparent areas are flattened onto.</param>
	/// <param name="info">One-line description for the strip under the image.</param>
	public MarkupWindow(Bitmap frame, string background, string info, string baseName, string? saveDir)
	{
		InitializeComponent();
		_baseName = baseName;
		_saveDir = saveDir;

		double w = frame.PixelSize.Width, h = frame.PixelSize.Height;
		_unit = Math.Max(1, Math.Max(w, h) / 1280.0);
		Stage.Width = w;
		Stage.Height = h;
		FrameImage.Source = frame;
		Checker.Cell = 16 * _unit;
		Checker.IsVisible = background == "checker";
		Backdrop.Background = background switch
		{
			"checker" => Brushes.Transparent,
			"white" => Brushes.White,
			_ => Brushes.Black,
		};
		// A soft dark halo keeps every colour readable on bright and dark footage alike.
		Ink.Effect = new DropShadowEffect { Color = Palette[5].Color, OffsetX = 0, OffsetY = 0, BlurRadius = 4 * _unit, Opacity = 0.7 };

		InfoText.Text = info;
		InfoText.FontSize = 20 * _unit;
		InfoStrip.Padding = new Thickness(14 * _unit, 9 * _unit, 14 * _unit, 9 * _unit);
		InfoStrip.Width = w;

		_toolButtons = new()
		{
			[Tool.Pen] = PenTool, [Tool.Arrow] = ArrowTool, [Tool.Line] = LineTool,
			[Tool.Rect] = RectTool, [Tool.Ellipse] = EllipseTool, [Tool.Text] = TextTool,
		};
		foreach (var (name, color) in Palette)
		{
			var b = new ToggleButton
			{
				Classes = { "icon" },
				MinWidth = 30, Width = 30, Padding = new Thickness(0), Tag = color,
				Content = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)) },
			};
			ToolTip.SetTip(b, name);
			b.Click += (_, _) => SetColor((Color)b.Tag!);
			_colorButtons.Add(b);
			ColorPanel.Children.Add(b);
		}

		Ink.PointerPressed += OnInkDown;
		Ink.PointerMoved += OnInkMove;
		Ink.PointerReleased += OnInkUp;
		AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
		SetTool(Tool.Arrow);
		SetColor(_color);
		Closing += (_, _) => CommitText();
	}

	// ------------------------------------------------------------------------------------------
	// Tool / colour / width
	// ------------------------------------------------------------------------------------------

	private void OnTool(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { Tag: string tag })
			SetTool(Enum.Parse<Tool>(tag));
	}

	private void SetTool(Tool tool)
	{
		CommitText();
		_tool = tool;
		foreach (var (t, b) in _toolButtons)
			b.IsChecked = t == tool;
		Ink.Cursor = new Cursor(tool == Tool.Text ? StandardCursorType.Ibeam : StandardCursorType.Cross);
	}

	private void SetColor(Color c)
	{
		_color = c;
		foreach (var b in _colorButtons)
			b.IsChecked = (Color)b.Tag! == c;
		if (_editing != null)
			_editing.Foreground = new SolidColorBrush(c);
	}

	private double StrokeWidth => WidthBox.SelectedIndex switch { 0 => 3, 2 => 10, _ => 6 } * _unit;

	private double TextSize => WidthBox.SelectedIndex switch { 0 => 22, 2 => 48, _ => 32 } * _unit;

	private IBrush Stroke => new SolidColorBrush(_color);

	// ------------------------------------------------------------------------------------------
	// Drawing
	// ------------------------------------------------------------------------------------------

	private void OnInkDown(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(Ink).Properties.IsLeftButtonPressed)
			return;
		Point p = e.GetPosition(Ink);
		if (_tool == Tool.Text)
		{
			// Clicking elsewhere while typing just finishes that text.
			if (_editing != null)
				CommitText();
			else
				BeginText(p);
			e.Handled = true;
			return;
		}
		CommitText();
		_start = p;
		e.Pointer.Capture(Ink);
		_current = _tool switch
		{
			Tool.Pen => new Polyline { Stroke = Stroke, StrokeThickness = StrokeWidth, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round },
			Tool.Line => new Line { StartPoint = p, EndPoint = p, Stroke = Stroke, StrokeThickness = StrokeWidth, StrokeLineCap = PenLineCap.Round },
			Tool.Arrow => new Path { Fill = Stroke, Stroke = Stroke, StrokeThickness = StrokeWidth, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round },
			Tool.Rect => new Rectangle { Stroke = Stroke, StrokeThickness = StrokeWidth, RadiusX = 2 * _unit, RadiusY = 2 * _unit },
			Tool.Ellipse => new Ellipse { Stroke = Stroke, StrokeThickness = StrokeWidth },
			_ => null,
		};
		if (_tool == Tool.Pen)
			_penPoints = new List<Point> { p, p };
		if (_current != null)
		{
			Ink.Children.Add(_current);
			Update(p, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
		}
		e.Handled = true;
	}

	private void OnInkMove(object? sender, PointerEventArgs e)
	{
		if (_current != null && e.Pointer.Captured == Ink)
			Update(e.GetPosition(Ink), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
	}

	private void OnInkUp(object? sender, PointerReleasedEventArgs e)
	{
		if (_current == null)
			return;
		e.Pointer.Capture(null);
		Point p = e.GetPosition(Ink);
		// A click without a drag leaves nothing behind (except a dot for the pen).
		var d = p - _start;
		if (_tool != Tool.Pen && Math.Sqrt(d.X * d.X + d.Y * d.Y) < 3 * _unit)
			Ink.Children.Remove(_current);
		else
			_items.Add(_current);
		_current = null;
		_penPoints = null;
	}

	private void Update(Point p, bool shift)
	{
		switch (_current)
		{
			case Polyline pl:
				_penPoints!.Add(p);
				pl.Points = new List<Point>(_penPoints);
				break;
			case Line l:
				l.EndPoint = SnapAngle(_start, p, shift);
				break;
			case Path path:
				path.Data = ArrowGeometry(_start, SnapAngle(_start, p, shift), StrokeWidth);
				break;
			case Shape s:	// Rectangle / Ellipse
			{
				double dx = p.X - _start.X, dy = p.Y - _start.Y;
				if (shift)
				{
					double m = Math.Max(Math.Abs(dx), Math.Abs(dy));
					dx = Math.Sign(dx) * m;
					dy = Math.Sign(dy) * m;
				}
				Canvas.SetLeft(s, Math.Min(_start.X, _start.X + dx));
				Canvas.SetTop(s, Math.Min(_start.Y, _start.Y + dy));
				s.Width = Math.Abs(dx);
				s.Height = Math.Abs(dy);
				break;
			}
		}
	}

	/// <summary>With Shift, lines and arrows snap to 0 / 45 / 90 degrees.</summary>
	private static Point SnapAngle(Point a, Point b, bool shift)
	{
		if (!shift)
			return b;
		Vector v = b - a;
		double angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
		return a + new Vector(Math.Cos(angle), Math.Sin(angle)) * v.Length;
	}

	/// <summary>Shaft plus a filled head; the shaft stops inside the head so the tip stays sharp.</summary>
	private static Geometry ArrowGeometry(Point a, Point b, double width)
	{
		Vector v = b - a;
		double len = v.Length;
		var g = new GeometryGroup { FillRule = FillRule.NonZero };
		if (len < 0.5)
			return g;
		v /= len;
		double head = Math.Min(len, width * 4.5);
		var n = new Vector(-v.Y, v.X);
		Point baseCenter = b - v * head;
		g.Children.Add(new LineGeometry(a, b - v * head * 0.6));
		g.Children.Add(new PolylineGeometry(new[] { b, baseCenter + n * head * 0.55, baseCenter - n * head * 0.55 }, true));
		return g;
	}

	// ------------------------------------------------------------------------------------------
	// Text
	// ------------------------------------------------------------------------------------------

	private void BeginText(Point p)
	{
		var box = new TextBox
		{
			MinWidth = 60 * _unit,
			FontSize = TextSize,
			FontWeight = FontWeight.SemiBold,
			Foreground = Stroke,
			Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
			BorderBrush = Brushes.White,
			BorderThickness = new Thickness(1),
			Padding = new Thickness(4 * _unit, 1 * _unit, 4 * _unit, 1 * _unit),
			AcceptsReturn = true,
			CaretBrush = Brushes.White,
		};
		box.AddHandler(KeyDownEvent, (_, e) =>
		{
			if (e.Key == Key.Escape)
			{
				box.Text = "";
				CommitText();
				e.Handled = true;
			}
			else if (e.Key == Key.Return && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
			{
				CommitText();	// ⇧Return falls through and inserts a line break
				e.Handled = true;
			}
		}, RoutingStrategies.Tunnel);
		Canvas.SetLeft(box, p.X);
		Canvas.SetTop(box, p.Y - TextSize * 0.7);
		Ink.Children.Add(box);
		_editing = box;
		Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Input);
	}

	/// <summary>Turns the text box being edited into a label (or drops it when empty).</summary>
	private void CommitText()
	{
		if (_editing is not TextBox box)
			return;
		_editing = null;
		double x = Canvas.GetLeft(box), y = Canvas.GetTop(box);
		Ink.Children.Remove(box);
		if (string.IsNullOrWhiteSpace(box.Text))
			return;
		var label = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
			CornerRadius = new CornerRadius(3 * _unit),
			Padding = box.Padding,
			Child = new TextBlock { Text = box.Text, FontSize = box.FontSize, FontWeight = box.FontWeight, Foreground = box.Foreground },
		};
		Canvas.SetLeft(label, x);
		Canvas.SetTop(label, y);
		Ink.Children.Add(label);
		_items.Add(label);
		Focus();
	}

	// ------------------------------------------------------------------------------------------
	// Undo / clear / info strip
	// ------------------------------------------------------------------------------------------

	private void OnUndo(object? sender, RoutedEventArgs e) => Undo();

	private void Undo()
	{
		if (_editing != null)
		{
			_editing.Text = "";
			CommitText();
			return;
		}
		if (_items.Count == 0)
			return;
		Ink.Children.Remove(_items[^1]);
		_items.RemoveAt(_items.Count - 1);
	}

	private void OnClear(object? sender, RoutedEventArgs e)
	{
		CommitText();
		foreach (var item in _items)
			Ink.Children.Remove(item);
		_items.Clear();
	}

	private void OnInfoCheck(object? sender, RoutedEventArgs e) => InfoStrip.IsVisible = InfoCheck.IsChecked == true;

	// ------------------------------------------------------------------------------------------
	// Export
	// ------------------------------------------------------------------------------------------

	/// <summary>The sheet at the frame's pixel size (plus the info strip), as PNG.</summary>
	internal byte[] RenderPng()
	{
		CommitText();
		Sheet.UpdateLayout();
		var size = new PixelSize((int)Math.Round(Sheet.Bounds.Width), (int)Math.Round(Sheet.Bounds.Height));
		using var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
		rtb.Render(Sheet);
		using var ms = new MemoryStream();
#pragma warning disable CS0618
		rtb.Save(ms);
#pragma warning restore CS0618
		return ms.ToArray();
	}

	private void OnCopy(object? sender, RoutedEventArgs e) => Copy();

	private void Copy()
	{
		HintText.Text = ClipboardImage.SetPng(RenderPng())
			? "コピーしました。AI のチャットなどに ⌘V で貼り付けられます。"
			: "コピーに失敗しました";
	}

	private void OnSave(object? sender, RoutedEventArgs e) => _ = Save();

	private async Task Save()
	{
		byte[] png = RenderPng();
		var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "マークアップを PNG で保存",
			SuggestedFileName = _baseName + "_markup.png",
			SuggestedStartLocation = _saveDir != null ? await StorageProvider.TryGetFolderFromPathAsync(_saveDir) : null,
			DefaultExtension = "png",
			FileTypeChoices = new[] { FilePickerFileTypes.ImagePng },
		});
		if (file?.TryGetLocalPath() is not string path)
			return;
		await File.WriteAllBytesAsync(path, png);
		HintText.Text = "保存しました: " + path;
	}

	// ------------------------------------------------------------------------------------------
	// Keyboard
	// ------------------------------------------------------------------------------------------

	private void OnKey(object? sender, KeyEventArgs e)
	{
		if (_editing != null && _editing.IsKeyboardFocusWithin)
			return;	// typing text
		bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
		switch (e.Key)
		{
			case Key.C when cmd: Copy(); break;
			case Key.S when cmd: _ = Save(); break;
			case Key.Z when cmd: Undo(); break;
			case Key.W when cmd: Close(); break;
			case Key.P when !cmd: SetTool(Tool.Pen); break;
			case Key.A when !cmd: SetTool(Tool.Arrow); break;
			case Key.L when !cmd: SetTool(Tool.Line); break;
			case Key.R when !cmd: SetTool(Tool.Rect); break;
			case Key.O when !cmd: SetTool(Tool.Ellipse); break;
			case Key.T when !cmd: SetTool(Tool.Text); break;
			case Key.Escape: Close(); break;
			default: return;
		}
		e.Handled = true;
	}

	// ---- self-test helpers ----------------------------------------------------------------------

	internal void DrawArrowForTest(Point a, Point b)
	{
		var path = new Path { Fill = Stroke, Stroke = Stroke, StrokeThickness = StrokeWidth, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round, Data = ArrowGeometry(a, b, StrokeWidth) };
		Ink.Children.Add(path);
		_items.Add(path);
		var label = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), CornerRadius = new CornerRadius(3 * _unit), Padding = new Thickness(4 * _unit, 1 * _unit),
			Child = new TextBlock { Text = "ここ", FontSize = TextSize, FontWeight = FontWeight.SemiBold, Foreground = Stroke },
		};
		Canvas.SetLeft(label, a.X);
		Canvas.SetTop(label, a.Y);
		Ink.Children.Add(label);
		_items.Add(label);
	}
}
