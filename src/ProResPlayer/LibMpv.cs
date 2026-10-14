using System.Runtime.InteropServices;

namespace ProResPlayer;

/// <summary>Minimal P/Invoke surface of libmpv's client API (client.h).</summary>
internal static partial class LibMpv
{
	private const string Dll = "libmpv-2.dll";

	public const int EventNone = 0;
	public const int EventShutdown = 1;

	[LibraryImport(Dll, EntryPoint = "mpv_create")]
	public static partial IntPtr Create();

	[LibraryImport(Dll, EntryPoint = "mpv_initialize")]
	public static partial int Initialize(IntPtr ctx);

	[LibraryImport(Dll, EntryPoint = "mpv_terminate_destroy")]
	public static partial void TerminateDestroy(IntPtr ctx);

	[LibraryImport(Dll, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
	public static partial int SetOptionString(IntPtr ctx, string name, string value);

	[LibraryImport(Dll, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
	public static partial int SetPropertyString(IntPtr ctx, string name, string value);

	[LibraryImport(Dll, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
	public static partial IntPtr GetPropertyString(IntPtr ctx, string name);

	[LibraryImport(Dll, EntryPoint = "mpv_command")]
	public static partial int Command(IntPtr ctx, IntPtr[] args);

	[LibraryImport(Dll, EntryPoint = "mpv_free")]
	public static partial void Free(IntPtr data);

	[LibraryImport(Dll, EntryPoint = "mpv_wait_event")]
	public static partial IntPtr WaitEvent(IntPtr ctx, double timeout);

	[LibraryImport(Dll, EntryPoint = "mpv_wakeup")]
	public static partial void Wakeup(IntPtr ctx);

	[LibraryImport(Dll, EntryPoint = "mpv_error_string")]
	public static partial IntPtr ErrorString(int error);

	public static string? PtrToUtf8(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
}
