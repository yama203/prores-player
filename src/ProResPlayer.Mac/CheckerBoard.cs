using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ProResPlayer;

/// <summary>Grey checkerboard behind transparent areas (markup window).</summary>
public sealed class CheckerBoard : Control
{
	private static readonly IBrush Dark = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
	private static readonly IBrush Light = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

	public double Cell { get; set; } = 16;

	public override void Render(DrawingContext ctx)
	{
		double w = Bounds.Width, h = Bounds.Height, c = Math.Max(1, Cell);
		ctx.FillRectangle(Dark, new Rect(0, 0, w, h));
		for (int y = 0; y * c < h; y++)
			for (int x = y % 2; x * c < w; x += 2)
				ctx.FillRectangle(Light, new Rect(x * c, y * c, c, c));
	}
}
