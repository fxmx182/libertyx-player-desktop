using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Mpv;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>
/// O player. A interface conversa com o <see cref="MpvCore"/> e nunca com o vídeo direto — como
/// no Android (VlcEngine) e no iPhone (PlaybackEngine): os gestos são a cara do app.
///
/// No desktop os gestos do MX Player viram mouse: arrastar o vídeo para os lados rola com o quadro
/// exato debaixo do ponteiro, arrastar para cima/baixo mexe no volume (direita) ou no brilho
/// (esquerda), segurar acelera, a roda mexe no volume. Clique pausa, clique duplo é tela cheia.
/// </summary>
public sealed class PlayerView : Grid
{
    private readonly MpvCore _mpv;
    private Control _video;
    private readonly Action<MpvEvento> _aoEvento;
    private readonly Action _aoQuebrar;
    private readonly Border _escuro, _hud, _topo, _baixo, _painel, _retomar, _carregando;
    private readonly TextBlock _hudTexto, _titulo, _tempo, _total, _carregandoTexto;
    private readonly BarraDeTempo _barra;
    private readonly Button _play, _telaCheia, _mudo, _voltar;
    private readonly Slider _volume;
    private readonly DispatcherTimer _esconder, _hudTimer, _salvarTimer;
    private IDisposable? _dormir;
    private DateTime? _dormirAte;
    private bool _dormirNoFim;

    private List<Midia> _fila = [];
    private int _indice;
    private Midia? _atual;
    private double _pos, _dur;
    private bool _pausado, _fim, _abrindo, _soltandoBusca, _volumeDoMpv;
    private long _volumeMexido;
    private double _escurecer;
    private string _trilhasJson = "[]";
    private readonly Random _sorte = new();

    /// <summary>Volta à biblioteca.</summary>
    public event Action? Fechou;
    public event Action? AlternarTelaCheia;
    public event Action? AlternarMiniJanela;
    public event Action<string>? TituloMudou;
    /// <summary>A porta dos recursos Pro: devolve falso (e mostra a tela do Pro) se estiver travado.</summary>
    public Func<Pro.Recurso, bool> Exigir { get; set; } = _ => true;
    public Func<TopLevel?> Janela { get; set; } = () => null;
    public bool TelaCheia { get; set; }
    public bool Mini { get; set; }
    public bool Tocando => _atual != null;

    public PlayerView(MpvCore mpv)
    {
        _mpv = mpv;
        Background = Brushes.Black;
        ClipToBounds = true;
        Focusable = true;

        // ── o vídeo ──
        // Windows com ANGLE (o padrão) sem textura compartilhada: o OpenGL quebraria no meio e congelaria a janela
        bool semGl = Environment.GetEnvironmentVariable("LIBERTYX_SW") == "1" || MpvGlView.Quebrado != null
            || (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("LIBERTYX_GL") != "wgl" && !Win.TexturaCompartilhada());
        if (semGl) { _video = new MpvSwView { Core = mpv }; _videoPronto = true; }
        else
        {
            var gl = new MpvGlView { Core = mpv };
            gl.Pronto += VideoPronto;
            _video = gl;
        }
        Children.Add(_video);
        _escuro = new Border { Background = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
        Children.Add(_escuro);

        // ── o que fica por cima: a área dos gestos ──
        var gestos = new Border { Background = Brushes.Transparent };
        gestos.PointerPressed += GestoInicio;
        gestos.PointerMoved += GestoMove;
        gestos.PointerReleased += GestoFim;
        gestos.PointerWheelChanged += Roda;
        Children.Add(gestos);

        // ── carregando ──
        _carregandoTexto = Ui.Texto("", 14, FontWeight.SemiBold);
        _carregandoTexto.TextAlignment = TextAlignment.Center;
        _carregando = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, IsVisible = false,
            Child = Ui.Pilha(14, Orientation.Vertical, new ProgressBar { IsIndeterminate = true, Width = 160 }, _carregandoTexto),
        };
        Children.Add(_carregando);

        // ── o balão (volume, +10 s, 2×…) ──
        _hudTexto = Ui.Texto("", 22, FontWeight.Bold);
        _hudTexto.TextAlignment = TextAlignment.Center;
        _hud = new Border
        {
            Classes = { "vidroplayer" }, Padding = new Thickness(22, 12), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 90, 0, 0), IsHitTestVisible = false, Opacity = 0,
            Child = _hudTexto,
            Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }],
        };
        Children.Add(_hud);

        // ── barra de cima ──
        _titulo = Ui.Linha("", 16, FontWeight.Bold);
        _titulo.VerticalAlignment = VerticalAlignment.Center;
        var topoGrade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 10) };
        // na mini janela, o mesmo canto volta à janela grande com o vídeo tocando — não fecha o vídeo
        _voltar = Ui.BotaoIcone(Icones.Voltar, S("Voltar à biblioteca (Esc)"), () => { if (Mini) AlternarMiniJanela?.Invoke(); else Fechar(); });
        topoGrade.Children.Add(_voltar);
        Grid.SetColumn(_titulo, 1);
        _titulo.Margin = new Thickness(10, 0);
        topoGrade.Children.Add(_titulo);
        var direita = Ui.Pilha(2, Orientation.Horizontal,
            Ui.BotaoIcone(Icones.Audio, S("Faixa de áudio (A)"), () => MostrarTrilhas("audio")),
            Ui.BotaoIcone(Icones.Legenda, S("Legendas (V)"), () => MostrarTrilhas("sub")),
            Ui.BotaoIcone(Icones.Mais, S("Mais opções"), MostrarMenu));
        Grid.SetColumn(direita, 2);
        topoGrade.Children.Add(direita);
        _topo = new Border
        {
            VerticalAlignment = VerticalAlignment.Top, Child = topoGrade,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#CC000000"), 0), new GradientStop(Colors.Transparent, 1) },
            },
        };
        Children.Add(_topo);

        // ── barra de baixo ──
        _barra = new BarraDeTempo();
        _barra.Arrastou += f => Rolar(f * _dur, true);
        _barra.Soltou += f => { Rolar(f * _dur, false); };
        _barra.Sobrevoou += f => { if (f is { } x && !_barra.Arrastando && _dur > 0) Hud(Tempo(x * _dur), 900); };
        _tempo = Ui.Linha("0:00", 13, FontWeight.SemiBold);
        _total = Ui.Linha("0:00", 13, FontWeight.SemiBold, "apagado");
        _play = Ui.Botao(null, Icones.Play, "playgrande", AlternarPausa, 26);
        ToolTip.SetTip(_play, S("Tocar / pausar (Espaço)"));
        _telaCheia = Ui.BotaoIcone(Icones.TelaCheia, S("Tela cheia (F)"), () => AlternarTelaCheia?.Invoke());
        _mudo = Ui.BotaoIcone(Icones.Volume, S("Sem som (M)"), () => _mpv.Command("cycle", "mute"));
        _volume = new Slider { Minimum = 0, Maximum = 150, Width = 110, VerticalAlignment = VerticalAlignment.Center, Value = Prefs.Atual.Volume };
        _volume.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty || _volumeDoMpv) return;
            _volumeMexido = Environment.TickCount64;
            _mpv.SetDoubleAsync("volume", _volume.Value);
        };

        var linhaTempo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(14, 0) };
        _tempo.VerticalAlignment = _total.VerticalAlignment = VerticalAlignment.Center;
        linhaTempo.Children.Add(_tempo);
        Grid.SetColumn(_barra, 1); _barra.Margin = new Thickness(10, 0);
        linhaTempo.Children.Add(_barra);
        Grid.SetColumn(_total, 2);
        linhaTempo.Children.Add(_total);

        var esquerda = Ui.Pilha(4, Orientation.Horizontal,
            Ui.BotaoIcone(Icones.Anterior, S("Anterior (P)"), () => Pular(-1)),
            Ui.BotaoIcone(Icones.Retroceder, S("Voltar (←)"), () => Saltar(-Prefs.Atual.Salto)),
            _play,
            Ui.BotaoIcone(Icones.Avancar, S("Avançar (→)"), () => Saltar(Prefs.Atual.Salto)),
            Ui.BotaoIcone(Icones.Proximo, S("Próximo (N)"), () => Pular(1)),
            new Border { Width = 10 }, _mudo, _volume);
        esquerda.VerticalAlignment = VerticalAlignment.Center;
        var dir = Ui.Pilha(2, Orientation.Horizontal,
            Ui.BotaoIcone(Icones.Velocidade, S("Velocidade"), MostrarVelocidade),
            Ui.BotaoIcone(Icones.Janela, S("Mini janela"), () => { if (Exigir(Pro.Recurso.Janela)) AlternarMiniJanela?.Invoke(); }),
            _telaCheia);
        dir.VerticalAlignment = VerticalAlignment.Center;
        var linhaBotoes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 2, 10, 12) };
        linhaBotoes.Children.Add(esquerda);
        Grid.SetColumn(dir, 1);
        linhaBotoes.Children.Add(dir);
        _baixo = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(0, 30, 0, 0),
            Child = Ui.Pilha(0, Orientation.Vertical, linhaTempo, linhaBotoes),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Color.Parse("#D9000000"), 1) },
            },
        };
        Children.Add(_baixo);

        // ── continuar de onde parou ──
        _retomar = new Border
        {
            Classes = { "vidroplayer" }, Padding = new Thickness(20, 16), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false,
        };
        Children.Add(_retomar);

        // ── painel lateral (menu, faixas) ──
        _painel = new Border
        {
            Classes = { "vidroplayer" }, Width = 330, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 64, 12, 110),
            Padding = new Thickness(8), IsVisible = false, VerticalAlignment = VerticalAlignment.Top,
        };
        Children.Add(_painel);

        _esconder = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        _esconder.Tick += (_, _) => { _esconder.Stop(); MostrarControles(false); };
        _hudTimer = new DispatcherTimer();
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); _hud.Opacity = 0; };
        _salvarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _salvarTimer.Tick += (_, _) => SalvarPosicao();

        PointerMoved += (_, e) =>
        {
            Mexeu();
            if (Mini) Cursor = Beirada(e.GetPosition(this)) is { } b ? Cursores[b] : null;
        };
        // a mini janela não tem borda do sistema: as beiradas redimensionam (antes de qualquer botão ou gesto)
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!Mini || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Beirada(e.GetPosition(this)) is not { } b) return;
            (Janela() as Window)?.BeginResizeDrag(b, e);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => ConferirVideo();
        // (o OpenGL pode quebrar depois de abrir: a troca vale mesmo com Iniciou)
        _aoQuebrar = () => Dispatcher.UIThread.Post(() => { if (_video is MpvGlView g) TrocarPorSoftware(g); });
        MpvGlView.AoQuebrar += _aoQuebrar;
        _aoEvento = e => Dispatcher.UIThread.Post(() => AoEvento(e));
        _mpv.Evento += _aoEvento;
        // o que o mpv avisou antes desta tela existir
        Propriedade("pause", _mpv.GetFlag("pause") ?? true);
        Propriedade("mute", _mpv.GetFlag("mute") ?? false);
    }

    /// <summary>Solta o mpv (a janela é remontada ao trocar o idioma).</summary>
    public void Desligar()
    {
        _mpv.Evento -= _aoEvento;
        MpvGlView.AoQuebrar -= _aoQuebrar;
        if (_video is MpvGlView gl) gl.Liberar();
        if (_video is MpvSwView sw) sw.Liberar();
    }

    /// <summary>
    /// Sem OpenGL (máquina virtual sem aceleração, driver quebrado) o controle de vídeo nunca abre:
    /// troca pelo que desenha na memória, para o filme aparecer de qualquer jeito.
    /// </summary>
    private void ConferirVideo()
    {
        if (_video is not MpvGlView gl || gl.Iniciou) return;
        DispatcherTimer.RunOnce(() =>
        {
            if (_video is not MpvGlView g || g.Iniciou) return;
            TrocarPorSoftware(g);
        }, TimeSpan.FromMilliseconds(1500));
    }

    private void TrocarPorSoftware(MpvGlView g)
    {
        var motivo = g.Falha ?? MpvGlView.Quebrado;
        Diario.Escrever("OpenGL não abriu" + (motivo != null ? $" ({motivo})" : "") + ": vídeo por software");
        g.Liberar();
        int i = Children.IndexOf(g);
        Children.RemoveAt(i);
        _video = new MpvSwView { Core = _mpv };
        Children.Insert(i, _video);
        if (_esperando != null) { VideoPronto(); return; }
        _videoPronto = true;
        // com um vídeo já aberto sem imagem, reabre no mesmo ponto
        if (_atual != null)
        {
            _mpv.SetString("start", _pos.ToString("0.###", CultureInfo.InvariantCulture));
            _mpv.Command("loadfile", _atual.Endereco, "replace");
        }
    }

    /// <summary>As opções e propriedades que o player observa (uma vez, ao criar o mpv).</summary>
    public static Dictionary<string, string> Opcoes()
    {
        var p = Prefs.Atual;
        string lang = Textos.Idioma switch { "pt" => "pt,por,pob,pt-BR", "es" => "es,spa,es-419", _ => "en,eng" };
        return new()
        {
            ["config"] = "no", ["terminal"] = "no", ["osc"] = "no", ["input-default-bindings"] = "no", ["input-vo-keyboard"] = "no",
            ["vo"] = "libmpv", ["gpu-sw"] = "yes", ["hwdec"] = p.Decodificacao ? "auto-safe" : "no", ["keep-open"] = "yes", ["idle"] = "yes",
            ["hr-seek"] = "yes", ["hr-seek-framedrop"] = "no", ["sub-auto"] = "fuzzy", ["volume-max"] = "150",
            ["volume"] = p.Volume.ToString(CultureInfo.InvariantCulture), ["cache"] = "yes", ["demuxer-max-bytes"] = "150MiB",
            ["demuxer-readahead-secs"] = "20", ["alang"] = p.IdiomaAudio ?? lang, ["slang"] = p.IdiomaLegenda ?? lang,
            ["screenshot-format"] = "png", ["screenshot-high-bit-depth"] = "no", ["audio-client-name"] = "LibertyX Player",
            ["title"] = "LibertyX Player", ["user-agent"] = "LibertyX Player", ["ytdl"] = "yes", ["msg-level"] = "all=warn",
            ["sub-font"] = "Manrope", ["sub-font-size"] = "44", ["sub-border-size"] = "2.5",
        };
    }

    /// <summary>Uma vez por mpv (a janela pode ser remontada; o mpv não).</summary>
    public static void Observar(MpvCore _mpv)
    {
        _mpv.Observar("time-pos", LibMpv.Format.Double);
        _mpv.Observar("duration", LibMpv.Format.Double);
        _mpv.Observar("pause", LibMpv.Format.Flag);
        _mpv.Observar("eof-reached", LibMpv.Format.Flag);
        _mpv.Observar("volume", LibMpv.Format.Double);
        _mpv.Observar("mute", LibMpv.Format.Flag);
        _mpv.Observar("speed", LibMpv.Format.Double);
        _mpv.Observar("track-list", LibMpv.Format.String);
        _mpv.Observar("demuxer-cache-time", LibMpv.Format.Double);
        _mpv.Observar("paused-for-cache", LibMpv.Format.Flag);
        _mpv.Observar("media-title", LibMpv.Format.String);
        _mpv.IniciarEventos();
    }

    // ───────── abrir ─────────

    /// <summary>Toca <paramref name="fila"/> a partir de <paramref name="indice"/>; a fila é a pasta (próximo/anterior).</summary>
    public void Tocar(List<Midia> fila, int indice)
    {
        SalvarPosicao();
        _fila = fila;
        _indice = Math.Clamp(indice, 0, Math.Max(0, fila.Count - 1));
        if (_fila.Count == 0) return;
        Abrir(_fila[_indice]);
    }

    private bool _videoPronto;
    private Midia? _esperando;

    private void VideoPronto()
    {
        _videoPronto = true;
        if (_esperando is { } m) { _esperando = null; Abrir(m); }
    }

    private void Abrir(Midia m)
    {
        // aberto pela linha de comando, o arquivo chega antes de o OpenGL abrir: espera
        if (!_videoPronto) { _esperando = m; _atual = m; _titulo.Text = m.Titulo; Carregando(S("Abrindo…")); return; }
        _atual = m;
        _pos = 0; _dur = Miniaturas.Duracao(m) ?? 0; _fim = false; _abrindo = true;
        _titulo.Text = m.Titulo;
        TituloMudou?.Invoke(m.Titulo);
        _barra.Fracao = 0; _barra.Cache = 0;
        _retomar.IsVisible = false;
        _painel.IsVisible = false;
        Carregando(m.Origem == Origem.Arquivo ? null : S("Conectando…"));
        double inicio = 0;
        if (Prefs.Atual.Posicoes.TryGetValue(m.Endereco, out var r) && r.Posicao > 10 && (r.Duracao <= 0 || r.Posicao < r.Duracao - 20))
            inicio = r.Posicao;
        _mpv.SetString("start", inicio > 0 ? inicio.ToString("0.###", CultureInfo.InvariantCulture) : "none");
        _mpv.SetFlag("pause", inicio > 0);
        Propriedade("pause", inicio > 0);
        _mpv.SetString("loop-file", Prefs.Atual.Repetir ? "inf" : "no");
        _mpv.Command("loadfile", m.Endereco, "replace");
        if (inicio > 0) PerguntarRetomada(inicio);
        _salvarTimer.Start();
        Mexeu();
        Focus();
    }

    private void Carregando(string? texto)
    {
        _carregando.IsVisible = texto != null || _abrindo;
        _carregandoTexto.Text = texto ?? "";
    }

    private void PerguntarRetomada(double pos)
    {
        var continuar = Ui.Botao(F("Continuar de {0}", Tempo(pos)), Icones.Play, "ouro", () => { _retomar.IsVisible = false; _mpv.SetFlag("pause", false); });
        var inicio = Ui.Botao(S("Do início"), null, "vidro", () =>
        {
            _retomar.IsVisible = false;
            _mpv.Command("seek", "0", "absolute+exact");
            _mpv.SetFlag("pause", false);
        });
        _retomar.Child = Ui.Pilha(14, Orientation.Vertical,
            Ui.Texto(S("Continuar de onde parou?"), 17, FontWeight.Bold),
            Ui.Pilha(10, Orientation.Horizontal, continuar, inicio));
        _retomar.IsVisible = true;
        Dispatcher.UIThread.Post(() => continuar.Focus(), DispatcherPriority.Input);
    }

    // ───────── eventos do mpv ─────────

    private void AoEvento(MpvEvento e)
    {
        switch (e)
        {
            case MpvEvento.ArquivoAberto:
                _abrindo = false;
                Carregando(null);
                break;
            case MpvEvento.Fim f when f.Motivo == LibMpv.EndFileReason.Error:
                _abrindo = false;
                Carregando(null);
                Hud(S("Não consegui abrir este vídeo.") + (f.Erro != null ? "\n" + f.Erro : ""), 5000);
                Diario.Escrever($"falha ao abrir {_atual?.Endereco}: {f.Erro}");
                break;
            case MpvEvento.Log l when l.Nivel is "error" or "fatal" || Environment.GetEnvironmentVariable("LIBERTYX_LOG") == "1":
                Diario.Escrever($"mpv [{l.Prefixo}] {l.Texto}");
                break;
            case MpvEvento.Propriedade p:
                Propriedade(p.Nome, p.Valor);
                break;
        }
    }

    private void Propriedade(string nome, object? v)
    {
        switch (nome)
        {
            case "time-pos" when v is double t:
                _pos = t;
                if (!_barra.Arrastando && !_soltandoBusca) _barra.Fracao = _dur > 0 ? t / _dur : 0;
                _tempo.Text = Tempo(t);
                break;
            case "duration" when v is double d:
                _dur = d;
                _total.Text = Tempo(d);
                break;
            case "pause" when v is bool b:
                _pausado = b;
                ((PathIcon)_play.Content!).Data = StreamGeometry.Parse(b ? Icones.Play : Icones.Pausa);
                Inibir(!b);
                if (b) MostrarControles(true); else Mexeu();
                break;
            case "eof-reached" when v is bool b:
                if (b && !_fim) { _fim = true; ChegouAoFim(); }
                else if (!b) _fim = false;
                break;
            case "volume" when v is double vol:
                Prefs.Atual.Volume = vol;
                // arrastando a barra, o mpv ainda devolve os valores de antes: eles não podem puxar a barra de
                // volta (nem voltar ao mpv) — era o vai e volta que prendia a interface
                if (Environment.TickCount64 - _volumeMexido < 500 || Math.Abs(_volume.Value - vol) <= 0.5) break;
                _volumeDoMpv = true;
                _volume.Value = vol;
                _volumeDoMpv = false;
                break;
            case "mute" when v is bool m:
                ((PathIcon)_mudo.Content!).Data = StreamGeometry.Parse(m ? Icones.Mudo : Icones.Volume);
                break;
            case "track-list" when v is string j:
                _trilhasJson = j;
                break;
            case "demuxer-cache-time" when v is double c:
                _barra.Cache = _dur > 0 ? c / _dur : 0;
                break;
            case "paused-for-cache" when v is bool pc:
                Carregando(pc ? S("Carregando…") : null);
                break;
        }
    }

    private void ChegouAoFim()
    {
        if (_atual != null) { Prefs.Atual.Posicoes.Remove(_atual.Endereco); Prefs.Atual.Salvar(); }
        if (_dormirNoFim) { _dormirNoFim = false; Hud(S("Bom descanso"), 2500); return; }
        if (Prefs.Atual.Repetir) return;   // o loop-file cuida
        if (Prefs.Atual.Aleatorio && _fila.Count > 1) { _indice = (_indice + 1 + _sorte.Next(_fila.Count - 1)) % _fila.Count; Abrir(_fila[_indice]); }
        else if (_indice + 1 < _fila.Count) { _indice++; Abrir(_fila[_indice]); }
        else MostrarControles(true);
    }

    // ───────── comandos ─────────

    public void AlternarPausa()
    {
        if (_retomar.IsVisible) { _retomar.IsVisible = false; _mpv.SetFlag("pause", false); return; }
        if (_fim) { _mpv.Command("seek", "0", "absolute"); _mpv.SetFlag("pause", false); return; }
        _mpv.Command("cycle", "pause");
    }

    public void Saltar(double s)
    {
        _mpv.Command("seek", s.ToString(CultureInfo.InvariantCulture), "relative+exact");
        Hud((s > 0 ? "+" : "−") + F("{0} s", Math.Abs(s)), 700);
    }

    /// <summary>Busca para <paramref name="t"/>; arrastando, sem esperar (o mpv junta as buscas acumuladas).</summary>
    private void Rolar(double t, bool arrastando)
    {
        if (_dur <= 0) return;
        t = Math.Clamp(t, 0, _dur);
        string modo = arrastando && !Prefs.Atual.RolagemExata ? "absolute+keyframes" : "absolute+exact";
        _mpv.CommandAsync("seek", t.ToString("0.###", CultureInfo.InvariantCulture), modo);
        _tempo.Text = Tempo(t);
        if (arrastando) Hud(Tempo(t), 900);
        else
        {
            // segura a barra no lugar soltado até o mpv chegar lá (senão ela pisca para trás)
            _soltandoBusca = true;
            _barra.Fracao = t / _dur;
            DispatcherTimer.RunOnce(() => _soltandoBusca = false, TimeSpan.FromMilliseconds(400));
        }
    }

    public void Pular(int direcao)
    {
        if (_fila.Count == 0) return;
        if (direcao < 0 && _pos > 5) { _mpv.Command("seek", "0", "absolute"); return; }
        int novo = Prefs.Atual.Aleatorio && _fila.Count > 1 ? _sorte.Next(_fila.Count) : _indice + direcao;
        if (novo < 0 || novo >= _fila.Count) { Hud(direcao > 0 ? S("Este é o último") : S("Este é o primeiro"), 1200); return; }
        SalvarPosicao();
        _indice = novo;
        Abrir(_fila[_indice]);
    }

    public void Volume(double delta)
    {
        double v = Math.Clamp((_mpv.GetDouble("volume") ?? 100) + delta, 0, 150);
        _mpv.SetDouble("volume", v);
        _mpv.SetFlag("mute", false);
        Hud(F("Volume {0}%", Math.Round(v)), 800);
    }

    public void Velocidade(double v)
    {
        v = Math.Clamp(Math.Round(v, 2), 0.25, 4);
        _mpv.SetDouble("speed", v);
        Hud(v.ToString("0.##", Textos.Cultura) + "×", 800);
    }

    public void Capturar()
    {
        if (!Exigir(Pro.Recurso.Captura)) return;
        var dir = Prefs.Atual.PastaCapturas ?? Path.Combine(Caminhos.Imagens, "LibertyX");
        Directory.CreateDirectory(dir);
        var nome = $"{Seguro(_atual?.Titulo ?? "LibertyX")} {Tempo(_pos).Replace(':', '.')}.png";
        var f = Path.Combine(dir, nome);
        int r = _mpv.Command("screenshot-to-file", f, "video");
        Hud(r >= 0 ? F("Quadro salvo em {0}", dir.Replace(Caminhos.Home, "~")) : S("Não consegui salvar o quadro."), 2500);
    }

    private static string Seguro(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public void CicloEscuro()
    {
        _escurecer = _escurecer switch { < 0.01 => 0.3, < 0.31 => 0.5, < 0.51 => 0.7, _ => 0 };
        _escuro.Opacity = _escurecer;
        Hud(_escurecer == 0 ? S("Modo noturno desligado") : F("Modo noturno {0}%", Math.Round(_escurecer * 100)), 1000);
    }

    public void CicloTrilha(string tipo)
    {
        _mpv.Command("cycle", tipo == "audio" ? "aid" : "sid");
        DispatcherTimer.RunOnce(() =>
        {
            var t = Trilhas(tipo).FirstOrDefault(x => x.Selecionada);
            Hud(t != null ? t.Nome : tipo == "audio" ? S("Sem áudio") : S("Sem legenda"), 1200);
        }, TimeSpan.FromMilliseconds(150));
    }

    public void Fechar()
    {
        SalvarPosicao();
        _salvarTimer.Stop();
        _dormir?.Dispose(); _dormir = null; _dormirAte = null;
        _mpv.Command("stop");
        Inibir(false);
        _atual = null;
        _painel.IsVisible = false;
        _retomar.IsVisible = false;
        Fechou?.Invoke();
    }

    public void SalvarPosicao()
    {
        if (_atual == null || _dur <= 0 || _abrindo) return;
        var p = Prefs.Atual;
        if (_pos > 10 && _pos < _dur - 20)
            p.Posicoes[_atual.Endereco] = new Retomada { Posicao = _pos, Duracao = _dur, Quando = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Titulo = _atual.Titulo };
        else if (_pos >= _dur - 20) p.Posicoes.Remove(_atual.Endereco);
        p.Salvar();
    }

    // ───────── teclado ─────────

    public bool Tecla(KeyEventArgs e)
    {
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Space or Key.K or Key.MediaPlayPause: AlternarPausa(); break;
            case Key.Left: Saltar(shift ? -60 : ctrl ? -3 : -Prefs.Atual.Salto); break;
            case Key.Right: Saltar(shift ? 60 : ctrl ? 3 : Prefs.Atual.Salto); break;
            case Key.J: Saltar(-Prefs.Atual.Salto); break;
            case Key.L: Saltar(Prefs.Atual.Salto); break;
            case Key.Up: Volume(5); break;
            case Key.Down: Volume(-5); break;
            case Key.M: _mpv.Command("cycle", "mute"); break;
            case Key.F or Key.F11: AlternarTelaCheia?.Invoke(); break;
            case Key.OemComma: _mpv.Command("frame-back-step"); Hud(S("Quadro anterior"), 600); break;
            case Key.OemPeriod: _mpv.Command("frame-step"); Hud(S("Próximo quadro"), 600); break;
            case Key.OemOpenBrackets: Velocidade((_mpv.GetDouble("speed") ?? 1) - 0.25); break;
            case Key.OemCloseBrackets: Velocidade((_mpv.GetDouble("speed") ?? 1) + 0.25); break;
            case Key.Back: Velocidade(1); break;
            case Key.S when !ctrl: Capturar(); break;
            case Key.N or Key.MediaNextTrack: Pular(1); break;
            case Key.P or Key.MediaPreviousTrack: Pular(-1); break;
            case Key.A: CicloTrilha("audio"); break;
            case Key.V: CicloTrilha("sub"); break;
            case Key.D: CicloEscuro(); break;
            case Key.Home: _mpv.Command("seek", "0", "absolute"); break;
            case >= Key.D0 and <= Key.D9 when _dur > 0:
                Rolar((e.Key - Key.D0) / 10.0 * _dur, false); break;
            case Key.Escape:
                if (_painel.IsVisible) _painel.IsVisible = false;
                else if (Mini) AlternarMiniJanela?.Invoke();
                else if (TelaCheia) AlternarTelaCheia?.Invoke();
                else Fechar();
                break;
            case Key.Enter when _retomar.IsVisible: AlternarPausa(); break;
            case Key.F1 or Key.OemQuestion: MostrarAtalhos(); break;
            default: return false;
        }
        Mexeu();
        return true;
    }

    // ───────── mouse ─────────

    private Point _ini;
    private double _iniPos, _iniValor;
    private enum Gesto { Nada, Rolar, Volume, Brilho }
    private Gesto _gesto;
    private bool _pressionado, _acelerando;
    private IDisposable? _segurar;
    private double _velocidadeAntes = 1;

    private void GestoInicio(object? s, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (_painel.IsVisible) { _painel.IsVisible = false; e.Handled = true; return; }
        if (pt.Properties.IsRightButtonPressed) { MostrarMenu(); return; }
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            // o primeiro clique já pausou: desfaz e vai para a tela cheia (como no YouTube)
            _mpv.Command("cycle", "pause");
            if (Mini) AlternarMiniJanela?.Invoke(); else AlternarTelaCheia?.Invoke();
            _pressionado = false;
            return;
        }
        if (Mini) { (Janela() as Window)?.BeginMoveDrag(e); return; }
        _pressionado = true;
        _gesto = Gesto.Nada;
        _ini = pt.Position;
        _iniPos = _pos;
        e.Pointer.Capture((IInputElement?)s);
        _segurar?.Dispose();
        _segurar = DispatcherTimer.RunOnce(() =>
        {
            if (!_pressionado || _gesto != Gesto.Nada || _pausado) return;
            _acelerando = true;
            _velocidadeAntes = _mpv.GetDouble("speed") ?? 1;
            _mpv.SetDouble("speed", Prefs.Atual.VelocidadeSegurar);
            Hud(Prefs.Atual.VelocidadeSegurar.ToString("0.##", Textos.Cultura) + "× ▶▶", 60_000);
        }, TimeSpan.FromMilliseconds(450));
    }

    private void GestoMove(object? s, PointerEventArgs e)
    {
        if (!_pressionado || _acelerando) return;
        var p = e.GetPosition(this);
        double dx = p.X - _ini.X, dy = p.Y - _ini.Y;
        if (_gesto == Gesto.Nada)
        {
            if (Math.Abs(dx) < 10 && Math.Abs(dy) < 10) return;
            _segurar?.Dispose();
            if (Math.Abs(dx) >= Math.Abs(dy)) _gesto = _dur > 0 ? Gesto.Rolar : Gesto.Nada;
            else if (_ini.X > Bounds.Width / 2) { _gesto = Gesto.Volume; _iniValor = _mpv.GetDouble("volume") ?? 100; }
            else { _gesto = Gesto.Brilho; _iniValor = _mpv.GetDouble("brightness") ?? 0; }
            if (_gesto == Gesto.Nada) return;
        }
        switch (_gesto)
        {
            case Gesto.Rolar:
                // a largura toda da tela = 2 min, ou o vídeo inteiro se for mais curto
                double alcance = Math.Min(_dur, 120);
                double alvo = Math.Clamp(_iniPos + dx / Math.Max(1, Bounds.Width) * alcance, 0, _dur);
                _mpv.CommandAsync("seek", alvo.ToString("0.###", CultureInfo.InvariantCulture), Prefs.Atual.RolagemExata ? "absolute+exact" : "absolute+keyframes");
                _barra.Fracao = alvo / _dur;
                double d = alvo - _iniPos;
                Hud($"{Tempo(alvo)}\n{(d >= 0 ? "+" : "−")}{Tempo(Math.Abs(d))}", 900);
                break;
            case Gesto.Volume:
                double v = Math.Clamp(_iniValor - dy / Math.Max(1, Bounds.Height) * 150, 0, 150);
                _mpv.SetDouble("volume", v);
                Hud(F("Volume {0}%", Math.Round(v)), 800);
                break;
            case Gesto.Brilho:
                double b = Math.Clamp(_iniValor - dy / Math.Max(1, Bounds.Height) * 100, -60, 60);
                _mpv.SetDouble("brightness", Math.Round(b));
                Hud(F("Brilho {0}", (b > 0 ? "+" : "") + Math.Round(b)), 800);
                break;
        }
    }

    private void GestoFim(object? s, PointerReleasedEventArgs e)
    {
        if (!_pressionado) return;
        _pressionado = false;
        _segurar?.Dispose();
        e.Pointer.Capture(null);
        if (_acelerando)
        {
            _acelerando = false;
            _mpv.SetDouble("speed", _velocidadeAntes);
            Hud(_velocidadeAntes.ToString("0.##", Textos.Cultura) + "×", 600);
            return;
        }
        if (_gesto == Gesto.Rolar) { _soltandoBusca = true; DispatcherTimer.RunOnce(() => _soltandoBusca = false, TimeSpan.FromMilliseconds(400)); }
        if (_gesto == Gesto.Nada && e.InitialPressMouseButton == MouseButton.Left) AlternarPausa();
        _gesto = Gesto.Nada;
    }

    private void Roda(object? s, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double z = Math.Clamp((_mpv.GetDouble("video-zoom") ?? 0) + (e.Delta.Y > 0 ? 0.1 : -0.1), -1, 2.6);
            _mpv.SetDouble("video-zoom", z);
            Hud(F("Zoom {0}%", Math.Round(Math.Pow(2, z) * 100)), 800);
        }
        else Volume(e.Delta.Y > 0 ? 5 : -5);
        e.Handled = true;
    }

    // ───────── controles na tela ─────────

    private void Mexeu()
    {
        MostrarControles(true);
        _esconder.Stop();
        if (!_pausado && !_painel.IsVisible && !_retomar.IsVisible) _esconder.Start();
    }

    private void MostrarControles(bool sim)
    {
        if (!sim && (_painel.IsVisible || _barra.Arrastando || _baixo.IsPointerOver || _topo.IsPointerOver)) { _esconder.Start(); return; }
        _topo.IsVisible = _baixo.IsVisible = sim;
        Cursor = sim ? Cursor.Default : new Cursor(StandardCursorType.None);
    }

    private void Hud(string texto, int ms)
    {
        _hudTexto.Text = texto;
        _hud.Opacity = 1;
        _hudTimer.Stop();
        _hudTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _hudTimer.Start();
    }

    // ───────── painéis ─────────

    private void AbrirPainel(string titulo, params Control[] itens)
    {
        var lista = Ui.Pilha(2, Orientation.Vertical);
        var cab = Ui.Texto(titulo, 13, FontWeight.Bold, "apagado");
        cab.Margin = new Thickness(14, 8, 14, 6);
        lista.Children.Add(cab);
        lista.Children.AddRange(itens);
        _painel.Child = new ScrollViewer { Content = lista, MaxHeight = Math.Max(200, Bounds.Height - 190) };
        _painel.IsVisible = true;
        MostrarControles(true);
        _esconder.Stop();
    }

    private Button Item(string texto, string? icone, Action acao, bool marcado = false, string? lateral = null, bool pro = false, bool fechar = true)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (icone != null) g.Children.Add(Ui.Icone(icone, 18));
        var t = Ui.Linha(texto, 14, FontWeight.Medium);
        t.Margin = new Thickness(icone != null ? 12 : 0, 0, 8, 0);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        Control? dir = marcado ? Ui.Icone(Icones.Ok, 18) : lateral != null ? Ui.Linha(lateral, 13, FontWeight.SemiBold, "apagado")
            : pro && !Core.Pro.Ativo ? Ui.Selo("PRO", Ui.Pincel("OuroSuave"), Ui.Pincel("Ouro")) : null;
        if (dir != null) { Grid.SetColumn(dir, 2); dir.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(dir); }
        var b = new Button { Classes = { "item" }, Content = g };
        if (marcado) b.Classes.Add("marcado");
        b.Click += (_, _) => { if (fechar) _painel.IsVisible = false; acao(); Mexeu(); };
        return b;
    }

    public void MostrarMenu()
    {
        double vel = _mpv.GetDouble("speed") ?? 1;
        string dormir = _dormirNoFim ? S("fim do vídeo") : _dormirAte is { } ate ? F("{0} min", Math.Max(1, (int)Math.Ceiling((ate - DateTime.Now).TotalMinutes))) : S("desligado");
        AbrirPainel(S("Opções"),
            Item(S("Velocidade"), Icones.Velocidade, MostrarVelocidade, lateral: vel.ToString("0.##", Textos.Cultura) + "×"),
            Item(S("Faixa de áudio"), Icones.Audio, () => MostrarTrilhas("audio")),
            Item(S("Legendas"), Icones.Legenda, () => MostrarTrilhas("sub")),
            Item(S("Proporção e zoom"), Icones.Proporcao, MostrarProporcao),
            Item(S("Modo noturno"), Icones.Lua, CicloEscuro, lateral: _escurecer > 0 ? $"{Math.Round(_escurecer * 100)}%" : null),
            Item(S("Tempo para dormir"), Icones.Timer, () => { if (Exigir(Core.Pro.Recurso.Dormir)) MostrarDormir(); }, lateral: Core.Pro.Ativo ? dormir : null, pro: true),
            Item(S("Captura de quadro"), Icones.Camera, Capturar, pro: true),
            Item(S("Mini janela"), Icones.Janela, () => { if (Exigir(Core.Pro.Recurso.Janela)) AlternarMiniJanela?.Invoke(); }, pro: true),
            Item(S("Aleatório"), Icones.Aleatorio, () => { Prefs.Atual.Aleatorio = !Prefs.Atual.Aleatorio; Prefs.Atual.Salvar(); Hud(Prefs.Atual.Aleatorio ? S("Aleatório ligado") : S("Aleatório desligado"), 1000); }, marcado: Prefs.Atual.Aleatorio),
            Item(S("Repetir este vídeo"), Icones.Repetir, () =>
            {
                Prefs.Atual.Repetir = !Prefs.Atual.Repetir; Prefs.Atual.Salvar();
                _mpv.SetString("loop-file", Prefs.Atual.Repetir ? "inf" : "no");
                Hud(Prefs.Atual.Repetir ? S("Repetir ligado") : S("Repetir desligado"), 1000);
            }, marcado: Prefs.Atual.Repetir),
            Item(S("Rolagem quadro a quadro"), Icones.Quadro, () =>
            {
                Prefs.Atual.RolagemExata = !Prefs.Atual.RolagemExata; Prefs.Atual.Salvar();
                Hud(Prefs.Atual.RolagemExata ? S("Rolagem quadro a quadro ligada") : S("Rolagem rápida (por quadro-chave)"), 1400);
            }, marcado: Prefs.Atual.RolagemExata),
            Item(S("Atalhos do teclado"), Icones.Teclado, MostrarAtalhos),
            Item(S("Informações do vídeo"), Icones.Info, MostrarInfo));
    }

    private void MostrarVelocidade()
    {
        double vel = _mpv.GetDouble("speed") ?? 1;
        AbrirPainel(S("Velocidade"), new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4 }
            .Select(v => (Control)Item(v.ToString("0.##", Textos.Cultura) + "×" + (v == 1 ? "  " + S("(normal)") : ""), null, () => Velocidade(v), marcado: Math.Abs(v - vel) < 0.01))
            .ToArray());
    }

    private void MostrarProporcao()
    {
        string atual = _mpv.GetString("video-aspect-override") ?? "-1";
        bool preencher = (_mpv.GetDouble("panscan") ?? 0) > 0.5;
        Control Op(string nome, string valor, double panscan) => Item(nome, null, () =>
        {
            _mpv.SetString("video-aspect-override", valor);
            _mpv.SetDouble("panscan", panscan);
            Hud(nome, 900);
        }, marcado: panscan > 0.5 ? preencher : !preencher && (atual == valor || (valor == "-1" && atual.StartsWith("-1"))));
        AbrirPainel(S("Proporção e zoom"),
            Op(S("Original"), "-1", 0), Op(S("Preencher a tela (corta as bordas)"), "-1", 1),
            Op("16:9", "16:9", 0), Op("4:3", "4:3", 0), Op("21:9", "2.33", 0), Op("1:1", "1", 0),
            Item(S("Zoom e posição normais"), null, () => { _mpv.SetDouble("video-zoom", 0); _mpv.SetDouble("video-pan-x", 0); _mpv.SetDouble("video-pan-y", 0); Hud(S("Zoom 100%"), 800); }));
    }

    private void MostrarDormir()
    {
        Control Op(string nome, int minutos) => Item(nome, null, () =>
        {
            _dormir?.Dispose(); _dormir = null; _dormirAte = null; _dormirNoFim = false;
            if (minutos > 0)
            {
                _dormirAte = DateTime.Now.AddMinutes(minutos);
                _dormir = DispatcherTimer.RunOnce(() => { _mpv.SetFlag("pause", true); _dormirAte = null; Hud(S("Bom descanso"), 2500); }, TimeSpan.FromMinutes(minutos));
                Hud(F("Dormir em {0} min", minutos), 1400);
            }
            else if (minutos < 0) { _dormirNoFim = true; Prefs.Atual.Repetir = false; _mpv.SetString("loop-file", "no"); Hud(S("Para no fim deste vídeo"), 1400); }
            else Hud(S("Tempo para dormir desligado"), 1200);
        });
        AbrirPainel(S("Tempo para dormir"), Op(S("Desligado"), 0), Op("15 min", 15), Op("30 min", 30), Op("45 min", 45),
            Op("60 min", 60), Op("90 min", 90), Op(S("No fim deste vídeo"), -1));
    }

    private sealed record Trilha(long Id, string Nome, bool Selecionada);

    private List<Trilha> Trilhas(string tipo)
    {
        var l = new List<Trilha>();
        try
        {
            using var doc = JsonDocument.Parse(_trilhasJson);
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                if (t.GetProperty("type").GetString() != tipo) continue;
                long id = t.GetProperty("id").GetInt64();
                string? titulo = t.TryGetProperty("title", out var ti) ? ti.GetString() : null;
                string? lang = t.TryGetProperty("lang", out var la) ? la.GetString() : null;
                string? codec = t.TryGetProperty("codec", out var co) ? co.GetString() : null;
                bool ext = t.TryGetProperty("external", out var ex) && ex.GetBoolean();
                var partes = new List<string>();
                if (lang != null) partes.Add(NomeIdioma(lang));
                if (titulo != null && titulo != lang) partes.Add(titulo);
                if (tipo == "audio" && t.TryGetProperty("demux-channel-count", out var ch)) partes.Add(ch.GetInt32() switch { 1 => "mono", 2 => S("estéreo"), 6 => "5.1", 8 => "7.1", var n => $"{n} ch" });
                if (codec != null) partes.Add(codec.ToUpperInvariant());
                if (ext) partes.Add(S("arquivo"));
                string nome = partes.Count > 0 ? string.Join(" · ", partes) : F("Faixa {0}", id);
                l.Add(new Trilha(id, nome, t.TryGetProperty("selected", out var se) && se.GetBoolean()));
            }
        }
        catch { }
        return l;
    }

    // ISO 639-2/B (o que os MKV costumam trazer) → o código que a CultureInfo conhece
    private static readonly Dictionary<string, string> CodigoB = new()
    {
        ["ger"] = "de", ["fre"] = "fr", ["dut"] = "nl", ["chi"] = "zh", ["cze"] = "cs", ["gre"] = "el", ["per"] = "fa",
        ["rum"] = "ro", ["slo"] = "sk", ["alb"] = "sq", ["arm"] = "hy", ["baq"] = "eu", ["bur"] = "my", ["geo"] = "ka",
        ["ice"] = "is", ["mac"] = "mk", ["may"] = "ms", ["tib"] = "bo", ["wel"] = "cy", ["scc"] = "sr", ["scr"] = "hr",
    };

    private static string NomeIdioma(string codigo)
    {
        if (CodigoB.TryGetValue(codigo, out var iso)) codigo = iso;
        try
        {
            var c = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                .FirstOrDefault(x => x.ThreeLetterISOLanguageName == codigo || x.TwoLetterISOLanguageName == codigo || x.Name == codigo);
            if (codigo is "pob") return Textos.Idioma == "en" ? "Portuguese (Brazil)" : Textos.Idioma == "es" ? "Portugués (Brasil)" : "Português (Brasil)";
            if (c != null) { var n = c.NativeName; return char.ToUpper(n[0]) + n[1..]; }
        }
        catch { }
        return codigo;
    }

    private void MostrarTrilhas(string tipo)
    {
        var l = Trilhas(tipo);
        var itens = new List<Control>
        {
            Item(tipo == "audio" ? S("Sem áudio") : S("Sem legenda"), null, () => _mpv.SetString(tipo == "audio" ? "aid" : "sid", "no"), marcado: !l.Any(t => t.Selecionada)),
        };
        itens.AddRange(l.Select(t => (Control)Item(t.Nome, null, () => _mpv.SetString(tipo == "audio" ? "aid" : "sid", t.Id.ToString(CultureInfo.InvariantCulture)), marcado: t.Selecionada)));
        if (tipo == "sub")
        {
            itens.Add(Item(S("Abrir arquivo de legenda…"), Icones.Arquivo, AbrirLegenda));
            double atraso = _mpv.GetDouble("sub-delay") ?? 0;
            itens.Add(Item(S("Adiantar legenda 0,1 s"), null, () => Atraso("sub-delay", -0.1), fechar: false));
            itens.Add(Item(S("Atrasar legenda 0,1 s"), null, () => Atraso("sub-delay", 0.1), fechar: false, lateral: atraso != 0 ? $"{atraso:+0.0;-0.0} s" : null));
        }
        else
        {
            double atraso = _mpv.GetDouble("audio-delay") ?? 0;
            itens.Add(Item(S("Adiantar áudio 0,1 s"), null, () => Atraso("audio-delay", -0.1), fechar: false));
            itens.Add(Item(S("Atrasar áudio 0,1 s"), null, () => Atraso("audio-delay", 0.1), fechar: false, lateral: atraso != 0 ? $"{atraso:+0.0;-0.0} s" : null));
        }
        AbrirPainel(tipo == "audio" ? S("Faixa de áudio") : S("Legendas"), [.. itens]);
    }

    private void Atraso(string prop, double delta)
    {
        double v = Math.Round((_mpv.GetDouble(prop) ?? 0) + delta, 2);
        _mpv.SetDouble(prop, v);
        Hud((prop == "sub-delay" ? S("Legenda") : S("Áudio")) + $" {v.ToString("+0.0;-0.0;0.0", Textos.Cultura)} s", 900);
    }

    private async void AbrirLegenda()
    {
        if (Janela()?.StorageProvider is not { } sp) return;
        var r = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = S("Abrir arquivo de legenda"),
            FileTypeFilter = [new FilePickerFileType(S("Legendas")) { Patterns = Biblioteca.ExtensoesLegenda.Select(e => "*" + e).ToList() }, FilePickerFileTypes.All],
            SuggestedStartLocation = _atual?.Origem == Origem.Arquivo ? await sp.TryGetFolderFromPathAsync(Path.GetDirectoryName(_atual.Endereco)!) : null,
        });
        if (r.FirstOrDefault()?.TryGetLocalPath() is { } f)
        {
            _mpv.Command("sub-add", f, "select");
            Hud(S("Legenda carregada"), 1000);
        }
    }

    /// <summary>Uma legenda solta em cima do player (arrastar e soltar o .srt).</summary>
    public bool AdicionarLegenda(string arquivo)
    {
        if (_atual == null || !Biblioteca.ExtensoesLegenda.Contains(Path.GetExtension(arquivo))) return false;
        _mpv.Command("sub-add", arquivo, "select");
        Hud(S("Legenda carregada"), 1000);
        return true;
    }

    private void MostrarInfo()
    {
        string Prop(string p) => _mpv.GetString(p) ?? "—";
        var linhas = new List<(string, string)>
        {
            (S("Arquivo"), _atual?.Endereco.StartsWith("lxsmb://") == true ? _atual.Titulo : _atual?.Endereco ?? "—"),
            (S("Formato"), Prop("file-format")),
            (S("Vídeo"), $"{Prop("video-codec")}  {Prop("width")}×{Prop("height")}  {Prop("container-fps")} fps"),
            (S("Decodificação"), Prop("hwdec-current") is "no" or "—" ? S("processador (software)") : Prop("hwdec-current")),
            (S("Áudio"), $"{Prop("audio-codec-name")}  {Prop("audio-params/channel-count")} ch  {Prop("audio-params/samplerate")} Hz"),
        };
        AbrirPainel(S("Informações do vídeo"), linhas.Select(l => (Control)new StackPanel
        {
            Margin = new Thickness(14, 4), Spacing = 2,
            Children = { Ui.Texto(l.Item1, 12, FontWeight.SemiBold, "apagado"), Ui.Texto(l.Item2, 13) },
        }).ToArray());
    }

    public void MostrarAtalhos()
    {
        (string, string)[] a =
        [
            (S("Espaço / clique"), S("Tocar / pausar")),
            ("← →", F("Voltar / avançar {0} s", Prefs.Atual.Salto)),
            ("Shift + ← →", S("Voltar / avançar 1 min")),
            (", .", S("Quadro anterior / próximo")),
            ("↑ ↓ / " + S("roda do mouse"), S("Volume")),
            (S("Arrastar o vídeo ↔"), S("Rolar com o quadro exato")),
            (S("Arrastar ↕ à direita / esquerda"), S("Volume / brilho")),
            (S("Segurar o clique"), S("Acelera enquanto segura")),
            ("Ctrl + " + S("roda do mouse"), S("Zoom")),
            ("F / " + S("clique duplo"), S("Tela cheia")),
            ("[ ]  ⌫", S("Velocidade − / + / normal")),
            ("A / V", S("Próxima faixa de áudio / legenda")),
            ("N / P", S("Próximo / anterior")),
            ("S", S("Captura de quadro")),
            ("D", S("Modo noturno")),
            ("M", S("Sem som")),
            ("0 – 9", S("Ir para 0% … 90%")),
            ("Esc", S("Sair da tela cheia / voltar")),
        ];
        AbrirPainel(S("Atalhos do teclado"), a.Select(x =>
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), Margin = new Thickness(14, 5) };
            var k = Ui.Texto(x.Item1, 13, FontWeight.Bold); k.Foreground = Ui.Pincel("Ouro");
            var d = Ui.Texto(x.Item2, 13); Grid.SetColumn(d, 1);
            g.Children.Add(k); g.Children.Add(d);
            return (Control)g;
        }).ToArray());
    }

    // ───────── mini janela e tela cheia ─────────

    private static readonly Dictionary<WindowEdge, Cursor> Cursores = new()
    {
        [WindowEdge.NorthWest] = new(StandardCursorType.TopLeftCorner), [WindowEdge.NorthEast] = new(StandardCursorType.TopRightCorner),
        [WindowEdge.SouthWest] = new(StandardCursorType.BottomLeftCorner), [WindowEdge.SouthEast] = new(StandardCursorType.BottomRightCorner),
        [WindowEdge.West] = new(StandardCursorType.LeftSide), [WindowEdge.East] = new(StandardCursorType.RightSide),
        [WindowEdge.North] = new(StandardCursorType.TopSide), [WindowEdge.South] = new(StandardCursorType.BottomSide),
    };

    /// <summary>Que beirada da mini janela está debaixo do ponteiro (10 px; os cantos pegam 18).</summary>
    private WindowEdge? Beirada(Point p)
    {
        double w = Bounds.Width, h = Bounds.Height;
        bool cantoE = p.X < 18, cantoD = p.X > w - 18, cantoC = p.Y < 18, cantoB = p.Y > h - 18;
        if (cantoC && cantoE) return WindowEdge.NorthWest;
        if (cantoC && cantoD) return WindowEdge.NorthEast;
        if (cantoB && cantoE) return WindowEdge.SouthWest;
        if (cantoB && cantoD) return WindowEdge.SouthEast;
        if (p.X < 10) return WindowEdge.West;
        if (p.X > w - 10) return WindowEdge.East;
        if (p.Y < 10) return WindowEdge.North;
        if (p.Y > h - 10) return WindowEdge.South;
        return null;
    }

    public void AjustarModo()
    {
        if (!Mini) Cursor = null;
        ((PathIcon)_telaCheia.Content!).Data = StreamGeometry.Parse(TelaCheia ? Icones.SairTelaCheia : Icones.TelaCheia);
        ToolTip.SetTip(_telaCheia, TelaCheia ? S("Sair da tela cheia (F)") : S("Tela cheia (F)"));
        _volume.IsVisible = !Mini;
        _titulo.IsVisible = !Mini;
        ((PathIcon)_voltar.Content!).Data = StreamGeometry.Parse(Mini ? Icones.Ampliar : Icones.Voltar);
        ToolTip.SetTip(_voltar, Mini ? S("Voltar à janela grande (Esc)") : S("Voltar à biblioteca (Esc)"));
    }

    // ───────── protetor de tela ─────────

    private bool _inibido;

    /// <summary>Filme tocando não deixa a tela apagar (xdg-screensaver, que fala com GNOME, KDE, Xfce…;
    /// no Windows, SetThreadExecutionState na linha da interface).</summary>
    private void Inibir(bool sim)
    {
        if (sim == _inibido) return;
        _inibido = sim;
        if (OperatingSystem.IsWindows()) { Win.TelaAcesa(sim); return; }
        try
        {
            if (Janela()?.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero) return;
            var psi = new System.Diagnostics.ProcessStartInfo("xdg-screensaver", $"{(sim ? "suspend" : "resume")} 0x{h.ToInt64():x}")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }
}
