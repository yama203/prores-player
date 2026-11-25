using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ProResPlayer;

public static class ClipboardImage
{
	/// <summary>
	/// Puts an image on the clipboard as "PNG" (keeps alpha; Photoshop, Office, browsers, chat apps) and as a
	/// regular bitmap for everything else.
	/// </summary>
	public static void Set(BitmapSource image)
	{
		var png = new MemoryStream();
		var enc = new PngBitmapEncoder();
		enc.Frames.Add(BitmapFrame.Create(image));
		enc.Save(png);
		png.Position = 0;

		var data = new DataObject();
		data.SetData("PNG", png);
		data.SetImage(image);
		Clipboard.SetDataObject(data, true);
	}
}
