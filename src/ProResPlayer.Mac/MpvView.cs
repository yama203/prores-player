using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;

namespace ProResPlayer;

/// <summary>
/// Draws an <see cref="MpvPlayer"/> with mpv's OpenGL render API. Unlike the Windows build (mpv in a child
/// window via wid), the video is an ordinary control here, so other controls can be layered on top of it.
/// </summary>
public sealed unsafe class MpvView : OpenGlControlBase
{
	private MpvPlayer? _player;
	private IntPtr _render;
	private GlInterface? _gl;
	private GCHandle _self;

	private delegate void ReadPixelsFn(int x, int y, int w, int h, int format, int type, void* data);
	private delegate void BindFramebufferFn(int target, int fb);
	private ReadPixelsFn? _readPixels;
	private BindFramebufferFn? _bindFramebuffer;
	private TaskCompletionSource<Capture?>? _capture;

	/// <summary>Renders that produced a new video frame (for the playback smoothness check).</summary>
	public long NewFrames;

	/// <summary>Size of the last render in device pixels.</summary>
	public (int W, int H) PixelSize { get; private set; }

	/// <summary>True once mpv can draw here. With vo=libmpv, files loaded before this get no video output.</summary>
	public bool IsReady { get; private set; }

	/// <summary>Raised on the UI thread when <see cref="IsReady"/> turns true.</summary>
	public event Action? Ready;

	public sealed record Capture(int Width, int Height, byte[] Rgba);

	public MpvPlayer? Player
	{
		get => _player;
		set
		{
			if (_render != IntPtr.Zero)
				throw new InvalidOperationException("Set the player before the view is shown.");
			_player = value;
			RequestNextFrameRendering();
		}
	}

	protected override void OnOpenGlInit(GlInterface gl)
	{
		_gl = gl;
		_readPixels = Marshal.GetDelegateForFunctionPointer<ReadPixelsFn>(gl.GetProcAddress("glReadPixels"));
		_bindFramebuffer = Marshal.GetDelegateForFunctionPointer<BindFramebufferFn>(gl.GetProcAddress("glBindFramebuffer"));
		if (_player == null)
			return;

		if (!_self.IsAllocated)
			_self = GCHandle.Alloc(this);
		byte* api = (byte*)Marshal.StringToCoTaskMemUTF8("opengl");
		try
		{
			var init = new LibMpv.OpenGlInitParams { GetProcAddress = &GetProcAddress, GetProcAddressCtx = GCHandle.ToIntPtr(_self) };
			var p = stackalloc LibMpv.RenderParam[]
			{
				new(LibMpv.RenderParamApiType, api),
				new(LibMpv.RenderParamOpenGlInitParams, &init),
				new(LibMpv.RenderParamInvalid, null),
			};
			int err = LibMpv.RenderContextCreate(out _render, _player.Handle, p);
			if (err < 0)
				throw new InvalidOperationException("mpv_render_context_create failed: " + LibMpv.Error(err));
		}
		finally
		{
			Marshal.FreeCoTaskMem((IntPtr)api);
		}
		LibMpv.RenderContextSetUpdateCallback(_render, &OnMpvUpdate, GCHandle.ToIntPtr(_self));
		Dispatcher.UIThread.Post(() => { IsReady = true; Ready?.Invoke(); });
	}

	protected override void OnOpenGlRender(GlInterface gl, int fb)
	{
		if (_render == IntPtr.Zero)
			return;

		double scale = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
		int w = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
		int h = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
		PixelSize = (w, h);

		ulong flags = LibMpv.RenderContextUpdate(_render);
		if ((flags & LibMpv.RenderUpdateFrame) != 0)
			Interlocked.Increment(ref NewFrames);

		var fbo = new LibMpv.OpenGlFbo { Fbo = fb, W = w, H = h };
		int flip = 1;
		var p = stackalloc LibMpv.RenderParam[]
		{
			new(LibMpv.RenderParamOpenGlFbo, &fbo),
			new(LibMpv.RenderParamFlipY, &flip),
			new(LibMpv.RenderParamInvalid, null),
		};
		LibMpv.RenderContextRender(_render, p);
		_bindFramebuffer?.Invoke(0x8D40 /* GL_FRAMEBUFFER */, fb);	// mpv leaves framebuffer 0 bound; give Avalonia its own back

		if (_capture != null)
		{
			var tcs = _capture;
			_capture = null;
			tcs.TrySetResult(ReadBack(fb, w, h));
		}
	}

	private Capture? ReadBack(int fb, int w, int h)
	{
		if (_readPixels == null || _bindFramebuffer == null)
			return null;
		const int GL_FRAMEBUFFER = 0x8D40, GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401;
		_bindFramebuffer(GL_FRAMEBUFFER, fb);	// mpv leaves framebuffer 0 bound after rendering
		var buf = new byte[w * h * 4];
		fixed (byte* d = buf)
			_readPixels(0, 0, w, h, GL_RGBA, GL_UNSIGNED_BYTE, d);
		// GL rows go bottom-up; flip to top-down.
		var flipped = new byte[buf.Length];
		int stride = w * 4;
		for (int y = 0; y < h; y++)
			Buffer.BlockCopy(buf, (h - 1 - y) * stride, flipped, y * stride, stride);
		return new Capture(w, h, flipped);
	}

	/// <summary>Reads back what the view shows (debugging / self-test only).</summary>
	public Task<Capture?> CaptureAsync()
	{
		var tcs = new TaskCompletionSource<Capture?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_capture = tcs;
		RequestNextFrameRendering();
		return tcs.Task;
	}

	protected override void OnOpenGlDeinit(GlInterface gl) => FreeRenderContext();

	/// <summary>Must run before the player is disposed (mpv requires the render context to go first).</summary>
	public void FreeRenderContext()
	{
		if (_render != IntPtr.Zero)
		{
			LibMpv.RenderContextFree(_render);
			_render = IntPtr.Zero;
		}
		if (_self.IsAllocated)
			_self.Free();
	}

	[UnmanagedCallersOnly]
	private static IntPtr GetProcAddress(IntPtr ctx, IntPtr name)
	{
		var view = (MpvView)GCHandle.FromIntPtr(ctx).Target!;
		string? n = Marshal.PtrToStringUTF8(name);
		return n == null || view._gl == null ? IntPtr.Zero : view._gl.GetProcAddress(n);
	}

	// Called by mpv from its own threads: just schedule a redraw.
	[UnmanagedCallersOnly]
	private static void OnMpvUpdate(IntPtr ctx)
	{
		if (GCHandle.FromIntPtr(ctx).Target is MpvView view)
			Dispatcher.UIThread.Post(view.RequestNextFrameRendering, DispatcherPriority.Render);
	}
}
