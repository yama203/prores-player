using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ProResPlayer;

/// <summary>
/// Seek bar with the IN / OUT range: a band over the track, [ ] marks at its ends and their timecodes in a
/// row underneath (kept even without a range so the bar never changes height). Press anywhere to scrub.
/// </summary>
public sealed class SeekBar : Control
{
	private const double Pad = 9;	// thumb radius + a little, so the thumb never clips at the ends
	private const double TrackY = 11;
	private const double LabelRow = 14;

	private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#4A4A4A"));
	private static readonly IBrush PlayedBrush = new SolidColorBrush(Color.Parse("#E8A33D"));
	private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#E8A33D"));
	private static readonly IBrush BandBrush = new SolidColorBrush(Color.Parse("#E8A33D"), 0.3);
	private static readonly IBrush ThumbBrush = new SolidColorBrush(Color.Parse("#F2F2F2"));
	private static readonly IPen MarkPen = new Pen(AccentBrush, 2.5, lineJoin: PenLineJoin.Round);
	private static readonly Typeface Mono = new("Menlo");

	private double _duration, _value, _fps;
	private double? _in, _out;
	private bool _dragging;

	/// <summary>Raised with the time under the pointer: on press, while dragging, and on release.</summary>
	public event Action<double>? ScrubStarted, Scrubbing, ScrubEnded;

	public bool IsDragging => _dragging;

	public SeekBar()
	{
		Height = TrackY * 2 + LabelRow;
		Cursor = new Cursor(StandardCursorType.Hand);
	}

	/// <summary>Updates what is drawn; ignored for the value while the user is dragging.</summary>
	public void Update(double duration, double value, double fps, double? loopIn, double? loopOut)
	{
		bool changed = duration != _duration || fps != _fps || loopIn != _in || loopOut != _out || (!_dragging && value != _value);
		_duration = duration;
		_fps = fps;
		_in = loopIn;
		_out = loopOut;
		if (!_dragging)
			_value = value;
		if (changed)
			InvalidateVisual();
	}

	private double X(double t)
	{
		double frac = _duration > 0 ? Math.Clamp(t / _duration, 0, 1) : 0;
		return Pad + frac * Math.Max(1, Bounds.Width - 2 * Pad);
	}

	private double TimeAt(double x) =>
		_duration <= 0 ? 0 : Math.Clamp((x - Pad) / Math.Max(1, Bounds.Width - 2 * Pad), 0, 1) * _duration;

	public override void Render(DrawingContext ctx)
	{
		double w = Bounds.Width;
		ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, Bounds.Height));	// whole area is hit-testable
		ctx.DrawRectangle(TrackBrush, null, new RoundedRect(new Rect(Pad, TrackY - 2, Math.Max(0, w - 2 * Pad), 4), 2));
		if (_duration <= 0)
			return;

		bool any = _in.HasValue || _out.HasValue;
		double x0 = X(_in ?? 0), x1 = X(_out ?? _duration);
		if (any)
			ctx.DrawRectangle(BandBrush, null, new RoundedRect(new Rect(x0, TrackY - 6, Math.Max(3, x1 - x0), 12), 3));

		double xv = X(_value);
		ctx.DrawRectangle(PlayedBrush, null, new RoundedRect(new Rect(Pad, TrackY - 2, Math.Max(0, xv - Pad), 4), 2));

		double labelTop = TrackY * 2 - 1;
		double inRight = double.NegativeInfinity;
		if (_in.HasValue)
		{
			ctx.DrawGeometry(null, MarkPen, Bracket(x0 - 1, +1));
			var ft = Label("IN " + MainWindow.Timecode(Frame(_in.Value), _fps));
			double l = Math.Clamp(x0, 0, Math.Max(0, w - ft.Width));
			ctx.DrawText(ft, new Point(l, labelTop));
			inRight = l + ft.Width;
		}
		if (_out.HasValue)
		{
			ctx.DrawGeometry(null, MarkPen, Bracket(x1 + 1, -1));
			var ft = Label(MainWindow.Timecode(Frame(_out.Value), _fps) + " OUT");
			// Right-aligned to the OUT mark, but never on top of the IN label.
			double l = Math.Clamp(Math.Max(x1 - ft.Width, inRight + 8), 0, Math.Max(0, w - ft.Width));
			ctx.DrawText(ft, new Point(l, labelTop));
		}

		ctx.DrawEllipse(ThumbBrush, null, new Point(xv, TrackY), 7, 7);
	}

	private long Frame(double t) => _fps > 0 ? (long)Math.Round(t * _fps) : 0;

	private static FormattedText Label(string s) =>
		new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 11, AccentBrush);

	/// <summary>[ (dir = +1) or ] (dir = -1) with its spine at x.</summary>
	private static Geometry Bracket(double x, int dir)
	{
		var g = new StreamGeometry();
		using (var c = g.Open())
		{
			c.BeginFigure(new Point(x + 7 * dir, TrackY - 11), false);
			c.LineTo(new Point(x, TrackY - 11));
			c.LineTo(new Point(x, TrackY + 11));
			c.LineTo(new Point(x + 7 * dir, TrackY + 11));
			c.EndFigure(false);
		}
		return g;
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		if (_duration <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;
		_dragging = true;
		e.Pointer.Capture(this);
		_value = TimeAt(e.GetPosition(this).X);
		InvalidateVisual();
		ScrubStarted?.Invoke(_value);
		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		if (!_dragging)
			return;
		_value = TimeAt(e.GetPosition(this).X);
		InvalidateVisual();
		Scrubbing?.Invoke(_value);
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		if (!_dragging)
			return;
		_value = TimeAt(e.GetPosition(this).X);
		_dragging = false;
		e.Pointer.Capture(null);
		InvalidateVisual();
		ScrubEnded?.Invoke(_value);
		e.Handled = true;
	}

	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		if (_dragging)
		{
			_dragging = false;
			ScrubEnded?.Invoke(_value);
		}
	}
}
