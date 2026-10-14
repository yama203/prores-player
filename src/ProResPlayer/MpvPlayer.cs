using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace ProResPlayer;

/// <summary>One libmpv instance rendering into a child window.</summary>
public sealed class MpvPlayer : IDisposable
{
	private IntPtr _ctx;
	private readonly Thread _eventThread;
	private volatile bool _stopping;

	public MpvPlayer(IntPtr wid)
	{
		_ctx = LibMpv.Create();
		if (_ctx == IntPtr.Zero)
			throw new InvalidOperationException("mpv_create failed");

		SetOption("wid", wid.ToInt64().ToString(CultureInfo.InvariantCulture));
		SetOption("vo", "gpu-next");
		SetOption("hwdec", "auto-safe");
		SetOption("vd-lavc-threads", "0");
		SetOption("keep-open", "always");
		SetOption("idle", "yes");
		SetOption("force-window", "yes");
		SetOption("hr-seek", "yes");
		SetOption("hr-seek-framedrop", "no");
		SetOption("pause", "yes");
		SetOption("osc", "no");
		SetOption("osd-level", "0");
		SetOption("input-default-bindings", "no");
		SetOption("input-vo-keyboard", "no");
		SetOption("input-cursor", "no");
		SetOption("cursor-autohide", "no");
		SetOption("drag-and-drop", "replace");
		SetOption("terminal", "no");
		SetOption("audio-display", "no");
		SetOption("background-color", "#000000");
		SetOption("screenshot-format", "png");
		SetOption("screenshot-png-compression", "3");
		SetOption("screenshot-high-bit-depth", "yes");
		// GPU screenshots are blended onto the background (checkerboard); software ones keep alpha.
		SetOption("screenshot-sw", "yes");

		int err = LibMpv.Initialize(_ctx);
		if (err < 0)
			throw new InvalidOperationException("mpv_initialize failed: " + LibMpv.PtrToUtf8(LibMpv.ErrorString(err)));

		// The event queue must be drained or it fills up.
		_eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
		_eventThread.Start();
	}

	private void EventLoop()
	{
		while (!_stopping)
		{
			IntPtr ev = LibMpv.WaitEvent(_ctx, -1);
			if (ev == IntPtr.Zero)
				continue;
			int id = Marshal.ReadInt32(ev);
			if (id == LibMpv.EventShutdown)
				break;
		}
	}

	public int SetOption(string name, string value) => LibMpv.SetOptionString(_ctx, name, value);

	public int Set(string name, string value) => LibMpv.SetPropertyString(_ctx, name, value);

	public int Set(string name, double value) => Set(name, value.ToString("R", CultureInfo.InvariantCulture));

	public string? Get(string name)
	{
		if (_ctx == IntPtr.Zero)
			return null;
		IntPtr p = LibMpv.GetPropertyString(_ctx, name);
		if (p == IntPtr.Zero)
			return null;
		try { return LibMpv.PtrToUtf8(p); }
		finally { LibMpv.Free(p); }
	}

	public double? GetDouble(string name)
	{
		string? s = Get(name);
		return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
	}

	public int Command(params string[] args)
	{
		var ptrs = new IntPtr[args.Length + 1];
		try
		{
			for (int i = 0; i < args.Length; i++)
				ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
			return LibMpv.Command(_ctx, ptrs);
		}
		finally
		{
			for (int i = 0; i < args.Length; i++)
				Marshal.FreeCoTaskMem(ptrs[i]);
		}
	}

	// ---- convenience -------------------------------------------------------------------------

	public void Load(string path) => Command("loadfile", path, "replace");

	public bool HasFile => !string.IsNullOrEmpty(Get("path"));

	public string? Path => Get("path");

	public bool Paused
	{
		get => Get("pause") == "yes";
		set => Set("pause", value ? "yes" : "no");
	}

	public double Time => GetDouble("time-pos") ?? 0;

	public double Duration => GetDouble("duration") ?? 0;

	public double Fps => GetDouble("container-fps") ?? GetDouble("estimated-vf-fps") ?? 0;

	public void SeekAbsolute(double seconds) => Command("seek", seconds.ToString("R", CultureInfo.InvariantCulture), "absolute+exact");

	public void SeekRelative(double seconds) => Command("seek", seconds.ToString("R", CultureInfo.InvariantCulture), "relative+exact");

	/// <summary>Writes the current frame at source resolution (no OSD) to a PNG. Synchronous.</summary>
	public bool Screenshot(string path, bool highBitDepth = true)
	{
		Set("screenshot-high-bit-depth", highBitDepth ? "yes" : "no");
		return Command("screenshot-to-file", path, "video") >= 0 && File.Exists(path);
	}

	public void FrameStep() => Command("frame-step");

	public void FrameBackStep() => Command("frame-back-step");

	public void Dispose()
	{
		if (_ctx == IntPtr.Zero)
			return;
		_stopping = true;
		LibMpv.Wakeup(_ctx);
		_eventThread.Join(2000);
		LibMpv.TerminateDestroy(_ctx);
		_ctx = IntPtr.Zero;
	}
}
