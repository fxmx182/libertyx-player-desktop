using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibertyX.Player.Core;
using static LibertyX.Player.Mpv.LibMpv;

namespace LibertyX.Player.Mpv;

/// <summary>
/// Miniaturas e durações. Um mpv só para isso, sem som e sem janela, desenhando um quadro na
/// memória (a render API em modo "sw") — vale para tudo que o mpv abre, inclusive o SMB, sem
/// depender de ffmpegthumbnailer instalado.
///
/// Uma de cada vez, numa thread própria; quem pediu por último é atendido primeiro (é o que está
/// na tela). Ficam em ~/.cache/libertyx-player/miniaturas, com nome pelo endereço + tamanho + data.
/// </summary>
public static unsafe class Miniaturas
{
    public const int Largura = 384;

    private static readonly ConcurrentDictionary<string, Bitmap?> Memoria = new();
    private static readonly Stack<(Midia m, Action<Bitmap?> pronto)> Fila = new();
    private static readonly Lock Trava = new();
    private static readonly SemaphoreSlim Sinal = new(0);
    private static Thread? _thread;
    private static Dictionary<string, double> _duracoes = Carregar();
    private static bool _duracoesSujas;

    private static string ArquivoDuracoes => Path.Combine(Caminhos.Cache, "duracoes.json");

    public static double? Duracao(Midia m) => _duracoes.TryGetValue(Nome(m), out var d) ? d : null;

    /// <summary>A miniatura já pronta, se houver (na memória ou no disco), sem enfileirar nada.</summary>
    public static Bitmap? Pronta(Midia m)
    {
        var n = Nome(m);
        if (Memoria.TryGetValue(n, out var b)) return b;
        var f = Path.Combine(Caminhos.Miniaturas, n + ".png");
        if (!File.Exists(f)) return null;
        try { b = new Bitmap(f); Memoria[n] = b; return b; } catch { return null; }
    }

    public static void Pedir(Midia m, Action<Bitmap?> pronto)
    {
        if (Pronta(m) is { } b) { pronto(b); return; }
        if (Memoria.ContainsKey(Nome(m))) { pronto(null); return; }   // já falhou antes
        lock (Trava)
        {
            Fila.Push((m, pronto));
            if (_thread == null) { _thread = new Thread(Laco) { IsBackground = true, Name = "miniaturas", Priority = ThreadPriority.BelowNormal }; _thread.Start(); }
        }
        Sinal.Release();
    }

    private static string Nome(Midia m) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{m.Endereco}|{m.Tamanho}|{m.Data.Ticks}")))[..24].ToLowerInvariant();

    private static Dictionary<string, double> Carregar()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(Path.Combine(Caminhos.Cache, "duracoes.json"))) ?? []; }
        catch { return []; }
    }

    // ───────── o trabalho ─────────

    private static MpvCore? _mpv;
    private static IntPtr _render;

    private static void Laco()
    {
        while (true)
        {
            if (!Sinal.Wait(TimeSpan.FromSeconds(30)))
            {
                SalvarDuracoes();
                continue;
            }
            (Midia m, Action<Bitmap?> pronto) item;
            lock (Trava) { if (Fila.Count == 0) continue; item = Fila.Pop(); }
            var n = Nome(item.m);
            if (Memoria.TryGetValue(n, out var ja)) { Dispatcher.UIThread.Post(() => item.pronto(ja)); continue; }
            Bitmap? bmp = null;
            try { bmp = Gerar(item.m, n); }
            catch (Exception e) { Diario.Escrever("miniatura: " + e.Message); Reiniciar(); }
            Memoria[n] = bmp;
            Dispatcher.UIThread.Post(() => item.pronto(bmp));
            if (Fila.Count == 0) SalvarDuracoes();
        }
    }

    private static void SalvarDuracoes()
    {
        if (!_duracoesSujas) return;
        _duracoesSujas = false;
        try { File.WriteAllText(ArquivoDuracoes, JsonSerializer.Serialize(_duracoes)); } catch { }
    }

    private static void Reiniciar()
    {
        if (_render != IntPtr.Zero) { mpv_render_context_free(_render); _render = IntPtr.Zero; }
        _mpv?.Dispose();
        _mpv = null;
    }

    private static void Abrir()
    {
        if (_mpv != null) return;
        _mpv = new MpvCore(new Dictionary<string, string>
        {
            ["config"] = "no", ["terminal"] = "no", ["load-scripts"] = "no", ["ytdl"] = "no", ["osc"] = "no",
            ["vo"] = "libmpv", ["ao"] = "null", ["audio"] = "no", ["sid"] = "no", ["hwdec"] = "no",
            ["pause"] = "yes", ["keep-open"] = "yes", ["hr-seek"] = "yes", ["idle"] = "yes",
            ["demuxer-max-bytes"] = "8MiB", ["cache"] = "no", ["vd-lavc-threads"] = "2", ["msg-level"] = "all=no",
        });
        var api = Utf8("sw");
        try
        {
            var ps = stackalloc RenderParam[] { new() { type = RenderParamType.ApiType, data = api }, new() { type = RenderParamType.Invalid } };
            if (mpv_render_context_create(out _render, _mpv.Handle, ps) < 0) throw new InvalidOperationException("render sw");
        }
        finally { Marshal.FreeCoTaskMem(api); }
    }

    private static Bitmap? Gerar(Midia m, string nome)
    {
        if (Quadro(m.Endereco, m.Origem != Origem.Arquivo) is not { } q) return null;
        if (q.Duracao > 0) { _duracoes[nome] = q.Duracao; _duracoesSujas = true; }
        fixed (byte* p = q.Pixels)
        {
            var bmp = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, (IntPtr)p, new PixelSize(q.Largura, q.Altura), new Vector(96, 96), q.Largura * 4);
            try { bmp.Save(Path.Combine(Caminhos.Miniaturas, nome + ".png")); } catch { }
            return bmp;
        }
    }

    public sealed record Captura(byte[] Pixels, int Largura, int Altura, double Duracao);

    /// <summary>
    /// Um quadro do vídeo em BGRA, com <see cref="Largura"/> de largura. 1º a 1/3 do vídeo (fora da
    /// abertura e dos créditos); se sair quase preto, tenta na metade. Também usado pelo autoteste.
    /// </summary>
    public static Captura? Quadro(string endereco, bool remoto)
    {
        Abrir();
        var mpv = _mpv!;
        var tam = stackalloc int[2];
        var ps = stackalloc RenderParam[5];
        foreach (var onde in new[] { "33%", "50%" })
        {
            mpv.SetString("start", onde);
            mpv.Command("loadfile", endereco, "replace");
            if (!Esperar(mpv, remoto ? 25 : 10)) { mpv.Command("stop"); return null; }
            double dur = mpv.GetDouble("duration") ?? 0;
            long w = mpv.GetLong("dwidth") ?? 0, h = mpv.GetLong("dheight") ?? 0;
            if (w <= 0 || h <= 0) { mpv.Command("stop"); return null; }
            int tw = Largura, th = Math.Clamp((int)(Largura * h / w) & ~1, 2, Largura * 2);
            var px = new byte[tw * th * 4];
            mpv_render_context_update(_render);
            fixed (byte* p = px)
            {
                tam[0] = tw; tam[1] = th;
                nuint passo = (nuint)(tw * 4);
                var fmt = Utf8("bgr0");
                try
                {
                    ps[0] = new() { type = RenderParamType.SwSize, data = (IntPtr)tam };
                    ps[1] = new() { type = RenderParamType.SwFormat, data = fmt };
                    ps[2] = new() { type = RenderParamType.SwStride, data = (IntPtr)(&passo) };
                    ps[3] = new() { type = RenderParamType.SwPointer, data = (IntPtr)p };
                    ps[4] = new() { type = RenderParamType.Invalid };
                    if (mpv_render_context_render(_render, ps) < 0) { mpv.Command("stop"); return null; }
                }
                finally { Marshal.FreeCoTaskMem(fmt); }
            }
            if (onde == "33%" && Escuro(px)) continue;
            mpv.Command("stop");
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return new Captura(px, tw, th, dur);
        }
        mpv.Command("stop");
        return null;
    }

    public static bool Escuro(byte[] px)
    {
        long soma = 0; int n = 0;
        for (int i = 0; i < px.Length; i += 4 * 17) { soma += px[i] + px[i + 1] + px[i + 2]; n += 3; }
        return n > 0 && soma / n < 14;
    }

    /// <summary>Espera o arquivo abrir e o primeiro quadro estar decodificado (ou erro, ou o prazo).</summary>
    private static bool Esperar(MpvCore mpv, double segundos)
    {
        var fim = DateTime.UtcNow.AddSeconds(segundos);
        bool aberto = false;
        while (DateTime.UtcNow < fim)
        {
            var e = mpv_wait_event(mpv.Handle, 0.5);
            switch (e->event_id)
            {
                case EventId.FileLoaded: aberto = true; break;
                case EventId.PlaybackRestart when aberto: return true;
                case EventId.EndFile when ((EventEndFile*)e->data)->reason is EndFileReason.Error: return false;
            }
        }
        return false;
    }
}
