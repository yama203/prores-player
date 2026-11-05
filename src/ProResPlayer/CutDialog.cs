using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProResPlayer;

public enum CutChoice { Cancel, Overwrite, SaveAs }

/// <summary>Asks how to save the cut: overwrite the source (default) or save under another name.</summary>
public sealed class CutDialog : Window
{
	public CutChoice Choice { get; private set; } = CutChoice.Cancel;

	public CutDialog(Window owner, string fileName, string range, string? warning)
	{
		Owner = owner;
		Title = "範囲をカットして保存";
		SizeToContent = SizeToContent.WidthAndHeight;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		Background = (Brush)Application.Current.Resources["Panel"];
		Foreground = (Brush)Application.Current.Resources["Fg"];

		var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16), MaxWidth = 460 };
		root.Children.Add(new TextBlock { Text = fileName, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
		root.Children.Add(new TextBlock { Text = range, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 6, 0, 0) });
		root.Children.Add(new TextBlock
		{
			Text = "IN〜OUT の範囲だけを残します。再エンコードしないので画質は変わりません。",
			Foreground = (Brush)Application.Current.Resources["Dim"],
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 10, 0, 0),
		});
		if (warning != null)
			root.Children.Add(new TextBlock
			{
				Text = warning,
				Foreground = (Brush)Application.Current.Resources["Accent"],
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 8, 0, 0),
			});

		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
		Button Add(string text, CutChoice choice, bool isDefault = false, bool isCancel = false)
		{
			var b = new Button { Content = text, MinWidth = 96, Margin = new Thickness(6, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
			b.Click += (_, _) => { Choice = choice; DialogResult = choice != CutChoice.Cancel; };
			buttons.Children.Add(b);
			return b;
		}
		Button overwrite = Add("上書き保存", CutChoice.Overwrite, isDefault: true);
		overwrite.BorderBrush = (Brush)Application.Current.Resources["Accent"];
		Add("別名で保存...", CutChoice.SaveAs);
		Add("キャンセル", CutChoice.Cancel, isCancel: true);
		root.Children.Add(buttons);
		Content = root;
	}
}
