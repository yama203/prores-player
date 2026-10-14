using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ProResPlayer;

/// <summary>
/// Hosts a plain Win32 child window that mpv renders into (via the "wid" option).
/// mpv creates its own window inside it on its own thread, so mouse clicks never reach WPF;
/// they are picked up here through WM_PARENTNOTIFY instead.
/// </summary>
public sealed class VideoHost : HwndHost
{
	private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
	private const int WM_PARENTNOTIFY = 0x0210, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;

	private IntPtr _hwnd;
	private uint _lastClick;

	public event EventHandler? Clicked;
	public event EventHandler? DoubleClicked;

	protected override HandleRef BuildWindowCore(HandleRef hwndParent)
	{
		_hwnd = CreateWindowEx(0, "static", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
			0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		return new HandleRef(this, _hwnd);
	}

	protected override void DestroyWindowCore(HandleRef hwnd) => DestroyWindow(hwnd.Handle);

	protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WM_PARENTNOTIFY)
		{
			int evt = (int)(wParam.ToInt64() & 0xFFFF);
			if (evt == WM_LBUTTONDOWN)
			{
				uint now = GetMessageTime();
				bool dbl = now - _lastClick <= GetDoubleClickTime();
				_lastClick = dbl ? 0 : now;
				Dispatcher.BeginInvoke(() => (dbl ? DoubleClicked : Clicked)?.Invoke(this, EventArgs.Empty));
			}
			else if (evt == WM_RBUTTONDOWN)
			{
				Dispatcher.BeginInvoke(() => Clicked?.Invoke(this, EventArgs.Empty));
			}
		}
		return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
		int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

	[DllImport("user32.dll")]
	private static extern bool DestroyWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	private static extern uint GetMessageTime();

	[DllImport("user32.dll")]
	private static extern uint GetDoubleClickTime();
}
