using System.Runtime.InteropServices;

namespace ProResPlayer;

/// <summary>
/// Puts an image on the macOS pasteboard as PNG (keeps alpha; Photoshop, browsers, chat apps) and as TIFF
/// (what Preview's "New from Clipboard", Keynote and older apps read). Talks to NSPasteboard directly.
/// </summary>
public static class ClipboardImage
{
	private const string ObjC = "/usr/lib/libobjc.dylib";

	[DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
	[DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
	[DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
	[DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr sel);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr sel, IntPtr a);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr sel, IntPtr a, IntPtr b);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr sel, byte[] bytes, nuint length);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendStr(IntPtr obj, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
	[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nuint SendULong(IntPtr obj, IntPtr sel);

	private static IntPtr Sel(string s) => sel_registerName(s);
	private static IntPtr NSString(string s) => SendStr(objc_getClass("NSString"), Sel("stringWithUTF8String:"), s);
	private static IntPtr Pasteboard => Send(objc_getClass("NSPasteboard"), Sel("generalPasteboard"));

	/// <param name="png">An 8-bit RGBA PNG.</param>
	public static bool SetPng(byte[] png)
	{
		IntPtr pool = objc_autoreleasePoolPush();
		try
		{
			IntPtr data = Send(objc_getClass("NSData"), Sel("dataWithBytes:length:"), png, (nuint)png.Length);
			IntPtr rep = Send(objc_getClass("NSBitmapImageRep"), Sel("imageRepWithData:"), data);
			IntPtr tiff = rep == IntPtr.Zero ? IntPtr.Zero : Send(rep, Sel("TIFFRepresentation"));
			IntPtr pb = Pasteboard;
			Send(pb, Sel("clearContents"));
			bool ok = ((long)Send(pb, Sel("setData:forType:"), data, NSString("public.png")) & 0xFF) != 0;	// BOOL
			if (tiff != IntPtr.Zero)
				Send(pb, Sel("setData:forType:"), tiff, NSString("public.tiff"));
			return ok;
		}
		finally
		{
			objc_autoreleasePoolPop(pool);
		}
	}

	/// <summary>The pasteboard's data of a type such as "public.png", or null. For tests.</summary>
	public static byte[]? GetData(string type)
	{
		IntPtr pool = objc_autoreleasePoolPush();
		try
		{
			IntPtr data = Send(Pasteboard, Sel("dataForType:"), NSString(type));
			if (data == IntPtr.Zero)
				return null;
			var bytes = new byte[(int)SendULong(data, Sel("length"))];
			Marshal.Copy(Send(data, Sel("bytes")), bytes, 0, bytes.Length);
			return bytes;
		}
		finally
		{
			objc_autoreleasePoolPop(pool);
		}
	}

	/// <summary>Size in bytes of the pasteboard's data of a type such as "public.png" (0 if none). For tests.</summary>
	public static long DataLength(string type)
	{
		IntPtr pool = objc_autoreleasePoolPush();
		try
		{
			IntPtr data = Send(Pasteboard, Sel("dataForType:"), NSString(type));
			return data == IntPtr.Zero ? 0 : (long)SendULong(data, Sel("length"));
		}
		finally
		{
			objc_autoreleasePoolPop(pool);
		}
	}
}
