using System.Runtime.InteropServices;

namespace LibertyX.Player.Mpv;

/// <summary>
/// A API C do libmpv, só o que o app usa. O app não leva o mpv dentro dos pacotes .deb/.rpm: usa o
/// do sistema (libmpv2 no Debian/Ubuntu/Fedora; libmpv1 no Ubuntu 22.04 — a API que usamos é a mesma).
/// </summary>
public static unsafe class LibMpv
{
    private const string Lib = "mpv";

    static LibMpv()
    {
        // o nome do arquivo muda entre distribuições e versões; o NativeLibrary procura na ordem
        NativeLibrary.SetDllImportResolver(typeof(LibMpv).Assembly, (name, asm, path) =>
        {
            if (name != Lib) return IntPtr.Zero;
            foreach (var cand in new[] { "libmpv.so.2", "libmpv.so.1", "libmpv.so" })
                if (NativeLibrary.TryLoad(cand, asm, path, out var h)) return h;
            return IntPtr.Zero;
        });
    }

    /// <summary>Carrega a biblioteca agora, para dizer à pessoa o que falta em vez de quebrar no primeiro vídeo.</summary>
    public static bool Disponivel(out string? erro)
    {
        try { _ = mpv_client_api_version(); erro = null; return true; }
        catch (DllNotFoundException e) { erro = e.Message; return false; }
    }

    public enum Format { None = 0, String = 1, OsdString = 2, Flag = 3, Int64 = 4, Double = 5, Node = 6 }

    public enum EventId
    {
        None = 0, Shutdown = 1, LogMessage = 2, GetPropertyReply = 3, SetPropertyReply = 4, CommandReply = 5,
        StartFile = 6, EndFile = 7, FileLoaded = 8, ClientMessage = 16, VideoReconfig = 17, AudioReconfig = 18,
        Seek = 20, PlaybackRestart = 21, PropertyChange = 22, QueueOverflow = 24, Hook = 25,
    }

    public enum EndFileReason { Eof = 0, Stop = 2, Quit = 3, Error = 4, Redirect = 5 }

    [StructLayout(LayoutKind.Sequential)]
    public struct Event { public EventId event_id; public int error; public ulong reply_userdata; public IntPtr data; }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventProperty { public IntPtr name; public Format format; public IntPtr data; }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventEndFile { public EndFileReason reason; public int error; }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventLogMessage { public IntPtr prefix; public IntPtr level; public IntPtr text; public int log_level; }

    // ───────── render API ─────────

    public enum RenderParamType
    {
        Invalid = 0, ApiType = 1, OpenGlInitParams = 2, OpenGlFbo = 3, FlipY = 4, AdvancedControl = 10,
        BlockForTargetTime = 12, SwSize = 17, SwFormat = 18, SwStride = 19, SwPointer = 20,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RenderParam { public RenderParamType type; public IntPtr data; }

    /// <summary>Três ponteiros: o terceiro (extra_exts) só existe no libmpv1, e nulo serve aos dois.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OpenGlInitParams { public IntPtr get_proc_address; public IntPtr get_proc_address_ctx; public IntPtr extra_exts; }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpenGlFbo { public int fbo; public int w; public int h; public int internal_format; }

    public const ulong RenderUpdateFrame = 1;

    // ───────── stream_cb: o SMB entra por aqui ─────────

    [StructLayout(LayoutKind.Sequential)]
    public struct StreamCbInfo
    {
        public IntPtr cookie;
        public delegate* unmanaged<IntPtr, byte*, ulong, long> read_fn;
        public delegate* unmanaged<IntPtr, long, long> seek_fn;
        public delegate* unmanaged<IntPtr, long> size_fn;
        public delegate* unmanaged<IntPtr, void> close_fn;
        public delegate* unmanaged<IntPtr, void> cancel_fn;
    }

    public const int ErrorLoadingFailed = -13;
    public const int ErrorUnsupported = -18;
    public const int ErrorGeneric = -20;

    [DllImport(Lib)] public static extern ulong mpv_client_api_version();
    [DllImport(Lib)] public static extern IntPtr mpv_create();
    [DllImport(Lib)] public static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Lib)] public static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Lib)] public static extern IntPtr mpv_error_string(int error);
    [DllImport(Lib)] public static extern void mpv_free(IntPtr data);
    [DllImport(Lib)] public static extern int mpv_set_option_string(IntPtr ctx, byte* name, byte* data);
    [DllImport(Lib)] public static extern int mpv_command(IntPtr ctx, byte** args);
    [DllImport(Lib)] public static extern int mpv_command_async(IntPtr ctx, ulong reply, byte** args);
    [DllImport(Lib)] public static extern int mpv_set_property_string(IntPtr ctx, byte* name, byte* data);
    [DllImport(Lib)] public static extern int mpv_set_property(IntPtr ctx, byte* name, Format format, void* data);
    [DllImport(Lib)] public static extern int mpv_get_property(IntPtr ctx, byte* name, Format format, void* data);
    [DllImport(Lib)] public static extern IntPtr mpv_get_property_string(IntPtr ctx, byte* name);
    [DllImport(Lib)] public static extern int mpv_observe_property(IntPtr ctx, ulong reply, byte* name, Format format);
    [DllImport(Lib)] public static extern int mpv_request_log_messages(IntPtr ctx, byte* minLevel);
    [DllImport(Lib)] public static extern Event* mpv_wait_event(IntPtr ctx, double timeout);
    [DllImport(Lib)] public static extern void mpv_wakeup(IntPtr ctx);

    [DllImport(Lib)] public static extern int mpv_render_context_create(out IntPtr res, IntPtr mpv, RenderParam* parameters);
    [DllImport(Lib)] public static extern void mpv_render_context_set_update_callback(IntPtr ctx, delegate* unmanaged<IntPtr, void> cb, IntPtr data);
    [DllImport(Lib)] public static extern ulong mpv_render_context_update(IntPtr ctx);
    [DllImport(Lib)] public static extern int mpv_render_context_render(IntPtr ctx, RenderParam* parameters);
    [DllImport(Lib)] public static extern void mpv_render_context_report_swap(IntPtr ctx);
    [DllImport(Lib)] public static extern void mpv_render_context_free(IntPtr ctx);

    [DllImport(Lib)] public static extern int mpv_stream_cb_add_ro(IntPtr ctx, byte* protocol, IntPtr userData, delegate* unmanaged<IntPtr, byte*, StreamCbInfo*, int> open);

    /// <summary>O mpv recusa abrir com LC_NUMERIC diferente de "C" (vírgula decimal quebraria as opções).</summary>
    [DllImport("libc", EntryPoint = "setlocale")] private static extern IntPtr setlocale(int category, byte* locale);
    public static void LocaleC()
    {
        var c = Utf8("C");
        try { setlocale(1 /* LC_NUMERIC */, (byte*)c); } catch { } finally { Marshal.FreeCoTaskMem(c); }
    }

    public static string Erro(int code) => Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? code.ToString();

    /// <summary>Texto C (UTF-8, terminado em zero) alocado fora do GC; libere com <see cref="Marshal.FreeCoTaskMem"/>.</summary>
    public static IntPtr Utf8(string s) => Marshal.StringToCoTaskMemUTF8(s);
}
