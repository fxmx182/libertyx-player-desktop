using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using static LibertyX.Player.Mpv.LibMpv;

namespace LibertyX.Player.Mpv;

/// <summary>
/// O vídeo desenhado pelo mpv dentro da própria janela, por OpenGL (a render API do libmpv).
/// Por ser um controle comum, e não uma janela nativa enfiada no meio, os controles do player
/// ficam por cima do vídeo sem truque nenhum — o problema que o VLC tem no desktop.
/// </summary>
public sealed unsafe class MpvGlView : OpenGlControlBase
{
    private MpvCore? _core;
    private IntPtr _render;
    private GCHandle _eu, _gl;

    /// <summary>Verdadeiro depois que o OpenGL abriu; se nunca abrir, o player troca para o <see cref="MpvSwView"/>.</summary>
    public bool Iniciou { get; private set; }
    /// <summary>O contexto de vídeo do mpv existe: já dá para abrir arquivo (antes disso o mpv desliga o vídeo).</summary>
    public event Action? Pronto;
    public string? Falha { get; private set; }

    /// <summary>O OpenGL quebrou ao desenhar (Windows: ANGLE sem textura compartilhada — driver antigo,
    /// área de trabalho remota, Wine). Daí em diante o vídeo é sempre por software.</summary>
    public static string? Quebrado { get; private set; }
    public static event Action? AoQuebrar;
    public static void Quebrar(string motivo)
    {
        if (Quebrado != null) return;
        Quebrado = motivo;
        AoQuebrar?.Invoke();
    }

    public MpvCore? Core
    {
        get => _core;
        set { _core = value; RequestNextFrameRendering(); }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (_core == null) return;
        _eu = GCHandle.Alloc(this);
        _gl = GCHandle.Alloc(gl);
        var api = Utf8("opengl");
        try
        {
            var init = new OpenGlInitParams
            {
                get_proc_address = (IntPtr)(delegate* unmanaged<IntPtr, byte*, IntPtr>)&GetProc,
                get_proc_address_ctx = GCHandle.ToIntPtr(_gl),
            };
            var ps = stackalloc RenderParam[]
            {
                new() { type = RenderParamType.ApiType, data = api },
                new() { type = RenderParamType.OpenGlInitParams, data = (IntPtr)(&init) },
                new() { type = RenderParamType.Invalid },
            };
            int r = mpv_render_context_create(out _render, _core.Handle, ps);
            if (r < 0) { Falha = "mpv_render_context_create: " + Erro(r); _render = IntPtr.Zero; return; }
            mpv_render_context_set_update_callback(_render, &AoAtualizar, GCHandle.ToIntPtr(_eu));
            Iniciou = true;
            Dispatcher.UIThread.Post(() => Pronto?.Invoke());
        }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(api); }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_render == IntPtr.Zero) return;
        double s = VisualRoot?.RenderScaling ?? 1;
        int w = Math.Max(1, (int)(Bounds.Width * s)), h = Math.Max(1, (int)(Bounds.Height * s));
        mpv_render_context_update(_render);
        var fbo = new OpenGlFbo { fbo = fb, w = w, h = h };
        int flip = 1, bloquear = 0;
        var ps = stackalloc RenderParam[]
        {
            new() { type = RenderParamType.OpenGlFbo, data = (IntPtr)(&fbo) },
            new() { type = RenderParamType.FlipY, data = (IntPtr)(&flip) },
            new() { type = RenderParamType.BlockForTargetTime, data = (IntPtr)(&bloquear) },
            new() { type = RenderParamType.Invalid },
        };
        mpv_render_context_render(_render, ps);
        // o mpv termina com o framebuffer 0 ligado; o do Avalonia volta, para quem desenhar depois
        gl.BindFramebuffer(0x8D40 /* GL_FRAMEBUFFER */, fb);
        mpv_render_context_report_swap(_render);
    }


    protected override void OnOpenGlDeinit(GlInterface gl) => Liberar();

    protected override void OnOpenGlLost() { Liberar(); base.OnOpenGlLost(); }

    /// <summary>O contexto de renderização sai antes do mpv (exigência do libmpv).</summary>
    public void Liberar()
    {
        if (_render != IntPtr.Zero)
        {
            mpv_render_context_set_update_callback(_render, null, IntPtr.Zero);
            mpv_render_context_free(_render);
            _render = IntPtr.Zero;
        }
        if (_eu.IsAllocated) _eu.Free();
        if (_gl.IsAllocated) _gl.Free();
    }

    [UnmanagedCallersOnly]
    private static IntPtr GetProc(IntPtr ctx, byte* nome)
    {
        var gl = (GlInterface)GCHandle.FromIntPtr(ctx).Target!;
        return gl.GetProcAddress(System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)nome)!);
    }

    [UnmanagedCallersOnly]
    private static void AoAtualizar(IntPtr ctx)
    {
        if (GCHandle.FromIntPtr(ctx).Target is MpvGlView v)
            Dispatcher.UIThread.Post(v.RequestNextFrameRendering, DispatcherPriority.Render);
    }
}

/// <summary>
/// O mesmo vídeo sem OpenGL: o mpv desenha num bloco de memória e a tela copia. Mais lento —
/// só entra quando o OpenGL não abre (máquina virtual sem aceleração, driver quebrado).
/// </summary>
public sealed unsafe class MpvSwView : Avalonia.Controls.Control
{
    private MpvCore? _core;
    private IntPtr _render;
    private GCHandle _eu;
    private WriteableBitmap? _quadro;
    private int _pendente;

    public MpvCore? Core
    {
        get => _core;
        set { _core = value; Iniciar(); }
    }

    private void Iniciar()
    {
        if (_core == null || _render != IntPtr.Zero) return;
        _eu = GCHandle.Alloc(this);
        var api = Utf8("sw");
        try
        {
            var ps = stackalloc RenderParam[]
            {
                new() { type = RenderParamType.ApiType, data = api },
                new() { type = RenderParamType.Invalid },
            };
            if (mpv_render_context_create(out _render, _core.Handle, ps) < 0) { _render = IntPtr.Zero; return; }
            mpv_render_context_set_update_callback(_render, &AoAtualizar, GCHandle.ToIntPtr(_eu));
        }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(api); }
    }

    private void Desenhar()
    {
        Interlocked.Exchange(ref _pendente, 0);
        if (_render == IntPtr.Zero) return;
        if ((mpv_render_context_update(_render) & RenderUpdateFrame) == 0 && _quadro != null) return;
        double s = VisualRoot?.RenderScaling ?? 1;
        // teto de 1920 de largura: acima disso a cópia pela CPU custa mais do que mostra
        double esc = Math.Min(1, 1920 / Math.Max(1, Bounds.Width * s));
        int w = Math.Max(2, (int)(Bounds.Width * s * esc)) & ~1, h = Math.Max(2, (int)(Bounds.Height * s * esc)) & ~1;
        if (_quadro == null || _quadro.PixelSize.Width != w || _quadro.PixelSize.Height != h)
        {
            _quadro?.Dispose();
            _quadro = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using (var fb = _quadro.Lock())
        {
            var tam = stackalloc int[2] { w, h };
            nuint passo = (nuint)fb.RowBytes;
            var fmt = Utf8("bgr0");
            try
            {
                var ps = stackalloc RenderParam[]
                {
                    new() { type = RenderParamType.SwSize, data = (IntPtr)tam },
                    new() { type = RenderParamType.SwFormat, data = fmt },
                    new() { type = RenderParamType.SwStride, data = (IntPtr)(&passo) },
                    new() { type = RenderParamType.SwPointer, data = fb.Address },
                    new() { type = RenderParamType.Invalid },
                };
                mpv_render_context_render(_render, ps);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(fmt); }
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_quadro != null) context.DrawImage(_quadro, new Rect(_quadro.Size), new Rect(Bounds.Size));
    }

    public void Liberar()
    {
        if (_render != IntPtr.Zero)
        {
            mpv_render_context_set_update_callback(_render, null, IntPtr.Zero);
            mpv_render_context_free(_render);
            _render = IntPtr.Zero;
        }
        if (_eu.IsAllocated) _eu.Free();
    }

    [UnmanagedCallersOnly]
    private static void AoAtualizar(IntPtr ctx)
    {
        if (GCHandle.FromIntPtr(ctx).Target is MpvSwView v && Interlocked.Exchange(ref v._pendente, 1) == 0)
            Dispatcher.UIThread.Post(v.Desenhar, DispatcherPriority.Render);
    }
}
