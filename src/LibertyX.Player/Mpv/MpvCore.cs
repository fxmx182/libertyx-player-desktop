using System.Runtime.InteropServices;
using static LibertyX.Player.Mpv.LibMpv;

namespace LibertyX.Player.Mpv;

/// <summary>
/// Uma instância do mpv: opções, comandos, propriedades e a fila de eventos. Não sabe desenhar —
/// quem desenha é o <see cref="MpvGlView"/> (ou o <see cref="MpvSwView"/>, sem OpenGL) e o
/// <see cref="Miniaturas"/>, cada um com seu contexto de renderização.
///
/// Os eventos chegam numa thread própria; quem quiser mexer na tela recebe por <see cref="Evento"/>
/// e despacha para a thread da interface.
/// </summary>
public sealed unsafe class MpvCore : IDisposable
{
    public IntPtr Handle { get; private set; }
    private Thread? _eventos;
    private volatile bool _fechando;
    private readonly Dictionary<ulong, string> _observadas = [];

    /// <summary>Evento do mpv, já traduzido; chamado na thread de eventos.</summary>
    public event Action<MpvEvento>? Evento;

    public MpvCore(IReadOnlyDictionary<string, string> opcoes, bool registrarSmb = true)
    {
        LocaleC();
        Handle = mpv_create();
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("mpv_create falhou");
        foreach (var (k, v) in opcoes) SetOption(k, v);
        int r = mpv_initialize(Handle);
        if (r < 0) throw new InvalidOperationException("mpv_initialize: " + Erro(r));
        if (registrarSmb) SmbStream.Registrar(this);
        var lvl = Utf8(Environment.GetEnvironmentVariable("LIBERTYX_MPVLOG") ?? "warn");
        try { mpv_request_log_messages(Handle, (byte*)lvl); } finally { Marshal.FreeCoTaskMem(lvl); }
    }

    public void IniciarEventos()
    {
        _eventos = new Thread(LacoDeEventos) { IsBackground = true, Name = "mpv-eventos" };
        _eventos.Start();
    }

    // ───────── opções, comandos, propriedades ─────────

    public void SetOption(string nome, string valor)
    {
        var n = Utf8(nome); var v = Utf8(valor);
        try { mpv_set_option_string(Handle, (byte*)n, (byte*)v); }
        finally { Marshal.FreeCoTaskMem(n); Marshal.FreeCoTaskMem(v); }
    }

    public int Command(params string[] args)
    {
        if (Handle == IntPtr.Zero) return -1;
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = Utf8(args[i]);
            fixed (IntPtr* p = ptrs) return mpv_command(Handle, (byte**)p);
        }
        finally { foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>Comando que não espera (busca durante o arraste): o mpv junta os que se acumulam.</summary>
    public void CommandAsync(params string[] args)
    {
        if (Handle == IntPtr.Zero) return;
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = Utf8(args[i]);
            fixed (IntPtr* p = ptrs) mpv_command_async(Handle, 0, (byte**)p);
        }
        finally { foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
    }

    public int SetString(string nome, string valor)
    {
        if (Handle == IntPtr.Zero) return -1;
        var n = Utf8(nome); var v = Utf8(valor);
        try { return mpv_set_property_string(Handle, (byte*)n, (byte*)v); }
        finally { Marshal.FreeCoTaskMem(n); Marshal.FreeCoTaskMem(v); }
    }

    public void SetDouble(string nome, double valor)
    {
        if (Handle == IntPtr.Zero) return;
        var n = Utf8(nome);
        try { mpv_set_property(Handle, (byte*)n, Format.Double, &valor); } finally { Marshal.FreeCoTaskMem(n); }
    }

    public void SetFlag(string nome, bool valor)
    {
        if (Handle == IntPtr.Zero) return;
        int f = valor ? 1 : 0;
        var n = Utf8(nome);
        try { mpv_set_property(Handle, (byte*)n, Format.Flag, &f); } finally { Marshal.FreeCoTaskMem(n); }
    }

    public string? GetString(string nome)
    {
        if (Handle == IntPtr.Zero) return null;
        var n = Utf8(nome);
        try
        {
            var p = mpv_get_property_string(Handle, (byte*)n);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(p); } finally { mpv_free(p); }
        }
        finally { Marshal.FreeCoTaskMem(n); }
    }

    public double? GetDouble(string nome)
    {
        if (Handle == IntPtr.Zero) return null;
        double v;
        var n = Utf8(nome);
        try { return mpv_get_property(Handle, (byte*)n, Format.Double, &v) >= 0 ? v : null; }
        finally { Marshal.FreeCoTaskMem(n); }
    }

    public long? GetLong(string nome)
    {
        if (Handle == IntPtr.Zero) return null;
        long v;
        var n = Utf8(nome);
        try { return mpv_get_property(Handle, (byte*)n, Format.Int64, &v) >= 0 ? v : null; }
        finally { Marshal.FreeCoTaskMem(n); }
    }

    public bool? GetFlag(string nome)
    {
        if (Handle == IntPtr.Zero) return null;
        int v;
        var n = Utf8(nome);
        try { return mpv_get_property(Handle, (byte*)n, Format.Flag, &v) >= 0 ? v != 0 : null; }
        finally { Marshal.FreeCoTaskMem(n); }
    }

    public void Observar(string nome, Format formato)
    {
        ulong id = (ulong)_observadas.Count + 1;
        _observadas[id] = nome;
        var n = Utf8(nome);
        try { mpv_observe_property(Handle, id, (byte*)n, formato); } finally { Marshal.FreeCoTaskMem(n); }
    }

    // ───────── eventos ─────────

    private void LacoDeEventos()
    {
        var h = Handle;
        while (!_fechando)
        {
            var e = mpv_wait_event(h, -1);
            if (e == null) continue;
            switch (e->event_id)
            {
                case EventId.None: continue;
                case EventId.Shutdown: return;
                case EventId.FileLoaded: Evento?.Invoke(new MpvEvento.ArquivoAberto()); break;
                case EventId.StartFile: Evento?.Invoke(new MpvEvento.Abrindo()); break;
                case EventId.PlaybackRestart: Evento?.Invoke(new MpvEvento.Pronto()); break;
                case EventId.VideoReconfig: Evento?.Invoke(new MpvEvento.VideoMudou()); break;
                case EventId.EndFile:
                    var ef = (EventEndFile*)e->data;
                    Evento?.Invoke(new MpvEvento.Fim(ef->reason, ef->reason == EndFileReason.Error ? Erro(ef->error) : null));
                    break;
                case EventId.LogMessage:
                    var lm = (EventLogMessage*)e->data;
                    Evento?.Invoke(new MpvEvento.Log(Marshal.PtrToStringUTF8(lm->prefix) ?? "", Marshal.PtrToStringUTF8(lm->level) ?? "",
                        (Marshal.PtrToStringUTF8(lm->text) ?? "").TrimEnd()));
                    break;
                case EventId.PropertyChange:
                    var pc = (EventProperty*)e->data;
                    if (!_observadas.TryGetValue(e->reply_userdata, out var nome)) break;
                    object? valor = pc->format switch
                    {
                        Format.Double when pc->data != IntPtr.Zero => *(double*)pc->data,
                        Format.Int64 when pc->data != IntPtr.Zero => *(long*)pc->data,
                        Format.Flag when pc->data != IntPtr.Zero => *(int*)pc->data != 0,
                        Format.String when pc->data != IntPtr.Zero => Marshal.PtrToStringUTF8(*(IntPtr*)pc->data),
                        _ => null,
                    };
                    Evento?.Invoke(new MpvEvento.Propriedade(nome, valor));
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        _fechando = true;
        var h = Handle;
        mpv_wakeup(h);
        _eventos?.Join(1000);
        Handle = IntPtr.Zero;
        mpv_terminate_destroy(h);
    }
}

public abstract record MpvEvento
{
    public sealed record Abrindo : MpvEvento;
    public sealed record ArquivoAberto : MpvEvento;
    public sealed record Pronto : MpvEvento;
    public sealed record VideoMudou : MpvEvento;
    public sealed record Fim(LibMpv.EndFileReason Motivo, string? Erro) : MpvEvento;
    public sealed record Log(string Prefixo, string Nivel, string Texto) : MpvEvento;
    public sealed record Propriedade(string Nome, object? Valor) : MpvEvento;
}
