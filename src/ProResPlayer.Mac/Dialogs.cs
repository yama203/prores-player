using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ProResPlayer;

public enum CutChoice { Cancel, Overwrite, SaveAs }

/// <summary>Small modal dialogs (Avalonia has no MessageBox).</summary>
public static class Dialogs
{
	private static IBrush Res(string key) => (IBrush)Application.Current!.FindResource(key)!;

	private static Window Make(string title, Control body, params Button[] buttons)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
		foreach (var b in buttons)
			row.Children.Add(b);
		var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16), MaxWidth = 480 };
		root.Children.Add(body);
		root.Children.Add(row);
		return new Window
		{
			Title = title,
			SizeToContent = SizeToContent.WidthAndHeight,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ShowInTaskbar = false,
			Background = Res("Panel"),
			Foreground = Res("Fg"),
			Content = root,
		};
	}

	private static Button Btn(string text, bool accent = false)
	{
		var b = new Button { Content = text, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
		if (accent)
			b.Classes.Add("accent");
		return b;
	}

	private static TextBlock Text(string s, string brush = "Fg", bool bold = false, string? font = null) => new()
	{
		Text = s,
		Foreground = Res(brush),
		FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
		FontFamily = font != null ? new FontFamily(font) : FontFamily.Default,
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0, 6, 0, 0),
	};

	public static async Task ShowMessage(Window owner, string title, string message)
	{
		var ok = Btn("OK", accent: true);
		ok.IsDefault = true;
		var body = new StackPanel();
		body.Children.Add(Text(title, bold: true));
		body.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MaxHeight = 300 });
		var w = Make(title, body, ok);
		ok.Click += (_, _) => w.Close();
		await w.ShowDialog(owner);
	}

	/// <summary>Asks how to save the cut: overwrite the source (default) or save under another name.</summary>
	public static async Task<CutChoice> AskCut(Window owner, string fileName, string range, string? warning)
	{
		var body = new StackPanel();
		body.Children.Add(Text(fileName, bold: true));
		body.Children.Add(Text(range, font: "Menlo"));
		body.Children.Add(Text("IN〜OUT の範囲だけを残します。再エンコードしないので画質は変わりません。", "Dim"));
		if (warning != null)
			body.Children.Add(Text(warning, "Accent"));

		var cancel = Btn("キャンセル");
		var saveAs = Btn("別名で保存...");
		var overwrite = Btn("上書き保存", accent: true);
		overwrite.IsDefault = true;
		cancel.IsCancel = true;
		var w = Make("範囲をカットして保存", body, cancel, saveAs, overwrite);	// macOS order: default button on the right
		var choice = CutChoice.Cancel;
		cancel.Click += (_, _) => w.Close();
		saveAs.Click += (_, _) => { choice = CutChoice.SaveAs; w.Close(); };
		overwrite.Click += (_, _) => { choice = CutChoice.Overwrite; w.Close(); };
		await w.ShowDialog(owner);
		return choice;
	}
}
