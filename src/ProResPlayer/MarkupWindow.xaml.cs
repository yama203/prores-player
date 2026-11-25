using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace ProResPlayer;

/// <summary>
/// Draw on a grabbed frame (pen, arrow, line, rectangle, ellipse, text) and copy / save the result,
/// optionally with a strip below that names the file, frame, timecode and time.
/// Everything is laid out in image pixels inside a Viewbox, so the export is at the frame's resolution.
/// </summary>
public partial class MarkupWindow : Window
{
	private enum Tool { Pen, Arrow, Line, Rect, Ellipse, Text }

	private static readonly (string Name, Color Color)[] Colors =
	{
		("赤", Color.FromRgb(0xFF, 0x3B, 0x30)),
		("黄", Color.FromRgb(0xFF, 0xD6, 0x0A)),
		("緑", Color.FromRgb(0x34, 0xC7, 0x59)),
		("青", Color.FromRgb(0x0A, 0x84, 0xFF)),
		("白", Color.FromRgb(0xFF, 0xFF, 0xFF)),
		("黒", Color.FromRgb(0x10, 0x10, 0x10)),
	};

	private readonly string _baseName;
	private readonly double _unit;	// 1 at 1280 px on the long side; scales strokes and text with the frame
	private Tool _tool = Tool.Arrow;
	private Color _color = Colors[0].Color;
	private readonly List<UIElement> _items = new();
	private readonly Dictionary<Tool, ToggleButton> _toolButtons;
	private readonly List<ToggleButton> _colorButtons = new();

	private Point _start;
	private FrameworkElement? _current;	// shape being dragged
	private Polyline? _pen;
	private TextBox? _editing;

	/// <param name="frame">The frame, Bgra32 (may have alpha).</param>
	/// <param name="background">checker / white / anything else = black: what transparent areas are flattened onto.</param>
	/// <param name="info">One-line description for the strip under the image.</param>
	public MarkupWindow(Window owner, BitmapSource frame, string background, string info, string baseName)
	{
		InitializeComponent();
		Owner = owner;
		_baseName = baseName;
		Width = Math.Max(MinWidth, owner.ActualWidth * 0.85);
		Height = Math.Max(MinHeight, owner.ActualHeight * 0.85);

		double w = frame.PixelWidth, h = frame.PixelHeight;
		_unit = Math.Max(1, Math.Max(w, h) / 1280.0);
		Stage.Width = w;
		Stage.Height = h;
		FrameImage.Source = frame;
		Backdrop.Fill = background switch
		{
			"checker" => CheckerBrush(16 * _unit),
			"white" => Brushes.White,
			_ => Brushes.Black,
		};
		// A soft dark halo keeps every colour readable on bright and dark footage alike.
		Ink.Effect = new DropShadowEffect { Color = Colors[5].Color, ShadowDepth = 0, BlurRadius = 4 * _unit, Opacity = 0.7 };

		InfoText.Text = info;
		InfoText.FontSize = 20 * _unit;
		InfoStrip.Padding = new Thickness(14 * _unit, 9 * _unit, 14 * _unit, 9 * _unit);
		InfoStrip.Width = w;

		_toolButtons = new()
		{
			[Tool.Pen] = PenTool, [Tool.Arrow] = ArrowTool, [Tool.Line] = LineTool,
			[Tool.Rect] = RectTool, [Tool.Ellipse] = EllipseTool, [Tool.Text] = TextTool,
		};
		foreach (var (name, color) in Colors)
		{
			var b = new ToggleButton
			{
				MinWidth = 30, Width = 30, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0),
				Content = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)) },
				ToolTip = name, Tag = color,
			};
			System.Windows.Automation.AutomationProperties.SetName(b, name);
			b.Click += (_, _) => SetColor((Color)b.Tag);
			_colorButtons.Add(b);
			ColorPanel.Children.Add(b);
		}
		SetTool(Tool.Arrow);
		SetColor(_color);
		Closing += (_, _) => CommitText();
	}

	private static Brush CheckerBrush(double cell)
	{
		var g = new DrawingGroup();
		g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
		g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), null,
			new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 1, 1)), new RectangleGeometry(new Rect(1, 1, 1, 1)) } }));
		return new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, cell * 2, cell * 2), ViewportUnits = BrushMappingMode.Absolute };
	}

	// ------------------------------------------------------------------------------------------
	// Tool / colour / width
	// ------------------------------------------------------------------------------------------

	private void OnTool(object sender, RoutedEventArgs e) => SetTool(Enum.Parse<Tool>((string)((FrameworkElement)sender).Tag));

	private void SetTool(Tool tool)
	{
		CommitText();
		_tool = tool;
		foreach (var (t, b) in _toolButtons)
			b.IsChecked = t == tool;
		Ink.Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
	}

	private void SetColor(Color c)
	{
		_color = c;
		foreach (var b in _colorButtons)
			b.IsChecked = (Color)b.Tag == c;
		if (_editing != null)
			_editing.Foreground = new SolidColorBrush(c);
	}

	private double StrokeWidth => WidthBox.SelectedIndex switch { 0 => 3, 2 => 10, _ => 6 } * _unit;

	private double TextSize => WidthBox.SelectedIndex switch { 0 => 22, 2 => 48, _ => 32 } * _unit;

	private Brush Stroke => new SolidColorBrush(_color);

	// ------------------------------------------------------------------------------------------
	// Drawing
	// ------------------------------------------------------------------------------------------

	private void OnInkDown(object sender, MouseButtonEventArgs e)
	{
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
		Ink.CaptureMouse();
		switch (_tool)
		{
			case Tool.Pen:
				_pen = new Polyline
				{
					Stroke = Stroke, StrokeThickness = StrokeWidth, StrokeLineJoin = PenLineJoin.Round,
					StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
				};
				_pen.Points.Add(p);
				_pen.Points.Add(p);
				_current = _pen;
				break;
			case Tool.Line:
				_current = new Line { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, Stroke = Stroke, StrokeThickness = StrokeWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
				break;
			case Tool.Arrow:
				_current = new Path { Fill = Stroke, Stroke = Stroke, StrokeThickness = StrokeWidth, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round };
				break;
			case Tool.Rect:
				_current = new Rectangle { Stroke = Stroke, StrokeThickness = StrokeWidth, RadiusX = 2 * _unit, RadiusY = 2 * _unit };
				break;
			case Tool.Ellipse:
				_current = new Ellipse { Stroke = Stroke, StrokeThickness = StrokeWidth };
				break;
		}
		if (_current != null)
		{
			Ink.Children.Add(_current);
			Update(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
		}
		e.Handled = true;
	}

	private void OnInkMove(object sender, MouseEventArgs e)
	{
		if (_current != null && e.LeftButton == MouseButtonState.Pressed)
			Update(e.GetPosition(Ink), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
	}

	private void OnInkUp(object sender, MouseButtonEventArgs e)
	{
		if (_current == null)
			return;
		Ink.ReleaseMouseCapture();
		Point p = e.GetPosition(Ink);
		// A click without a drag leaves nothing behind (except a dot for the pen).
		if (_tool != Tool.Pen && (p - _start).Length < 3 * _unit)
			Ink.Children.Remove(_current);
		else
			_items.Add(_current);
		_current = null;
		_pen = null;
	}

	private void Update(Point p, bool shift)
	{
		switch (_current)
		{
			case Polyline pl:
				pl.Points.Add(p);
				break;
			case Line l:
				p = SnapAngle(_start, p, shift);
				l.X2 = p.X;
				l.Y2 = p.Y;
				break;
			case Path path:
				path.Data = ArrowGeometry(_start, SnapAngle(_start, p, shift), StrokeWidth);
				break;
			case Shape s:	// Rectangle / Ellipse
			{
				double dx = p.X - _start.X, dy = p.Y - _start.Y;
				if (shift)
				{
					double d = Math.Max(Math.Abs(dx), Math.Abs(dy));
					dx = Math.Sign(dx) * d;
					dy = Math.Sign(dy) * d;
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
		var g = new GeometryGroup { FillRule = FillRule.Nonzero };
		if (len < 0.5)
			return g;
		v /= len;
		double head = Math.Min(len, width * 4.5);
		Vector n = new(-v.Y, v.X);
		Point baseCenter = b - v * head;
		g.Children.Add(new LineGeometry(a, b - v * head * 0.6));
		var tri = new PathFigure { StartPoint = b, IsClosed = true, IsFilled = true };
		tri.Segments.Add(new LineSegment(baseCenter + n * head * 0.55, true));
		tri.Segments.Add(new LineSegment(baseCenter - n * head * 0.55, true));
		g.Children.Add(new PathGeometry(new[] { tri }));
		return g;
	}

	// ------------------------------------------------------------------------------------------
	// Text
	// ------------------------------------------------------------------------------------------

	private void BeginText(Point p)
	{
		_editing = new TextBox
		{
			MinWidth = 60 * _unit,
			FontSize = TextSize,
			FontWeight = FontWeights.SemiBold,
			Foreground = Stroke,
			Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
			BorderBrush = Brushes.White,
			BorderThickness = new Thickness(1),
			Padding = new Thickness(4 * _unit, 1 * _unit, 4 * _unit, 1 * _unit),
			AcceptsReturn = false,
			CaretBrush = Brushes.White,
		};
		_editing.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape)
			{
				_editing!.Text = "";
				CommitText();
				e.Handled = true;
			}
		};
		_editing.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == Key.Return)
			{
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
				{
					int i = _editing!.CaretIndex;
					_editing.Text = _editing.Text.Insert(i, "\n");
					_editing.CaretIndex = i + 1;
				}
				else
					CommitText();
				e.Handled = true;
			}
		};
		Canvas.SetLeft(_editing, p.X);
		Canvas.SetTop(_editing, p.Y - TextSize * 0.7);
		Ink.Children.Add(_editing);
		Dispatcher.BeginInvoke(() => _editing?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
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
		Keyboard.Focus(this);
	}

	// ------------------------------------------------------------------------------------------
	// Undo / clear / info strip
	// ------------------------------------------------------------------------------------------

	private void OnUndo(object sender, RoutedEventArgs e) => Undo();

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

	private void OnClear(object sender, RoutedEventArgs e)
	{
		CommitText();
		foreach (var item in _items)
			Ink.Children.Remove(item);
		_items.Clear();
	}

	private void OnInfoCheck(object sender, RoutedEventArgs e) =>
		InfoStrip.Visibility = InfoCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

	// ------------------------------------------------------------------------------------------
	// Export
	// ------------------------------------------------------------------------------------------

	/// <summary>The sheet at the frame's pixel size (plus the info strip), 96 dpi.</summary>
	private BitmapSource Render()
	{
		CommitText();
		Sheet.UpdateLayout();
		double w = Sheet.ActualWidth, h = Sheet.ActualHeight;
		var dv = new DrawingVisual();
		using (var dc = dv.RenderOpen())
			dc.DrawRectangle(new VisualBrush(Sheet) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
		var rtb = new RenderTargetBitmap((int)Math.Round(w), (int)Math.Round(h), 96, 96, PixelFormats.Pbgra32);
		rtb.Render(dv);
		rtb.Freeze();
		return rtb;
	}

	private void OnCopy(object sender, RoutedEventArgs e) => Copy();

	private void Copy()
	{
		ClipboardImage.Set(Render());
		HintText.Text = "コピーしました。AI のチャットなどに Ctrl+V で貼り付けられます。";
	}

	private void OnSave(object sender, RoutedEventArgs e) => Save();

	private void Save()
	{
		var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = _baseName + "_markup.png" };
		if (dlg.ShowDialog(this) != true)
			return;
		var enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(Render()));
		using (var fs = File.Create(dlg.FileName))
			enc.Save(fs);
		HintText.Text = "保存しました: " + dlg.FileName;
	}

	// ------------------------------------------------------------------------------------------
	// Keyboard
	// ------------------------------------------------------------------------------------------

	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		if (_editing != null && _editing.IsKeyboardFocusWithin)
			return;	// typing text
		bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
		bool handled = true;
		switch (e.Key)
		{
			case Key.C when ctrl: Copy(); break;
			case Key.S when ctrl: Save(); break;
			case Key.Z when ctrl: Undo(); break;
			case Key.P: SetTool(Tool.Pen); break;
			case Key.A: SetTool(Tool.Arrow); break;
			case Key.L: SetTool(Tool.Line); break;
			case Key.R: SetTool(Tool.Rect); break;
			case Key.O: SetTool(Tool.Ellipse); break;
			case Key.T: SetTool(Tool.Text); break;
			case Key.Escape: Close(); break;
			default: handled = false; break;
		}
		e.Handled = handled;
	}
}
