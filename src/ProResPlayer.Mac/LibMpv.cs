using System.Reflection;
using System.Runtime.InteropServices;

namespace ProResPlayer;

/// <summary>Minimal P/Invoke surface of libmpv's client API (client.h) and render API (render.h, render_gl.h).</summary>
internal static unsafe partial class LibMpv
{
	private const string Lib = "libmpv";

	public const int EventNone = 0;
	public const int EventShutdown = 1;

	// mpv_render_param_type
	public const int RenderParamInvalid = 0;
	public const int RenderParamApiType = 1;
	public const int RenderParamOpenGlInitParams = 2;
	public const int RenderParamOpenGlFbo = 3;
	public const int RenderParamFlipY = 4;

	public const ulong RenderUpdateFrame = 1;

	[StructLayout(LayoutKind.Sequential)]
	public struct RenderParam
	{
		public int Type;
		public void* Data;
		public RenderParam(int type, void* data) { Type = type; Data = data; }
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct OpenGlInitParams
	{
		public delegate* unmanaged<IntPtr, IntPtr, IntPtr> GetProcAddress;
		public IntPtr GetProcAddressCtx;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct OpenGlFbo
	{
		public int Fbo;
		public int W;
		public int H;
		public int InternalFormat;
	}

	static LibMpv() => NativeLibrary.SetDllImportResolver(typeof(LibMpv).Assembly, Resolve);

	/// <summary>Looks for libmpv inside the .app first (Contents/Frameworks), then next to the binary, then Homebrew.</summary>
	private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
	{
		if (name != Lib)
			return IntPtr.Zero;
		string baseDir = AppContext.BaseDirectory;
		string[] candidates =
		{
			Path.Combine(baseDir, "..", "Frameworks", "libmpv.2.dylib"),
			Path.Combine(baseDir, "libmpv.2.dylib"),
			"/opt/homebrew/lib/libmpv.2.dylib",
			"/usr/local/lib/libmpv.2.dylib",
		};
		foreach (string c in candidates)
			if (File.Exists(c) && NativeLibrary.TryLoad(c, out IntPtr h))
				return h;
		throw new DllNotFoundException("libmpv が見つかりません。`brew install mpv` で入れてください。");
	}

	[LibraryImport(Lib, EntryPoint = "mpv_create")]
	public static partial IntPtr Create();

	[LibraryImport(Lib, EntryPoint = "mpv_initialize")]
	public static partial int Initialize(IntPtr ctx);

	[LibraryImport(Lib, EntryPoint = "mpv_terminate_destroy")]
	public static partial void TerminateDestroy(IntPtr ctx);

	[LibraryImport(Lib, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
	public static partial int SetOptionString(IntPtr ctx, string name, string value);

	[LibraryImport(Lib, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
	public static partial int SetPropertyString(IntPtr ctx, string name, string value);

	[LibraryImport(Lib, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
	public static partial IntPtr GetPropertyString(IntPtr ctx, string name);

	[LibraryImport(Lib, EntryPoint = "mpv_command")]
	public static partial int Command(IntPtr ctx, IntPtr[] args);

	[LibraryImport(Lib, EntryPoint = "mpv_free")]
	public static partial void Free(IntPtr data);

	[LibraryImport(Lib, EntryPoint = "mpv_wait_event")]
	public static partial IntPtr WaitEvent(IntPtr ctx, double timeout);

	[LibraryImport(Lib, EntryPoint = "mpv_wakeup")]
	public static partial void Wakeup(IntPtr ctx);

	[LibraryImport(Lib, EntryPoint = "mpv_error_string")]
	public static partial IntPtr ErrorString(int error);

	[LibraryImport(Lib, EntryPoint = "mpv_render_context_create")]
	public static partial int RenderContextCreate(out IntPtr res, IntPtr mpv, RenderParam* parameters);

	[LibraryImport(Lib, EntryPoint = "mpv_render_context_set_update_callback")]
	public static partial void RenderContextSetUpdateCallback(IntPtr ctx, delegate* unmanaged<IntPtr, void> callback, IntPtr callbackCtx);

	[LibraryImport(Lib, EntryPoint = "mpv_render_context_update")]
	public static partial ulong RenderContextUpdate(IntPtr ctx);

	[LibraryImport(Lib, EntryPoint = "mpv_render_context_render")]
	public static partial int RenderContextRender(IntPtr ctx, RenderParam* parameters);

	[LibraryImport(Lib, EntryPoint = "mpv_render_context_report_swap")]
	public static partial void RenderContextReportSwap(IntPtr ctx);

	[LibraryImport(Lib, EntryPoint = "mpv_render_context_free")]
	public static partial void RenderContextFree(IntPtr ctx);

	public static string? PtrToUtf8(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);

	public static string Error(int err) => PtrToUtf8(ErrorString(err)) ?? err.ToString();
}
