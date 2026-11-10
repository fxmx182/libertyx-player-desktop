using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Mpv;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>
/// A janela: a barra de cima com as abas do celular (Início, Arquivos, Procurar, Ajustes), a tela
/// escolhida, e o player por cima de tudo quando um vídeo toca. Arrastar arquivos para a janela toca; um .srt solto sobre o player
/// vira legenda.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly MpvCore _mpv;
    private PlayerView _player = null!;
    private BibliotecaView _biblioteca = null!;
    private ArquivosView _arquivos = null!;
    private ProcurarView _procurar = null!;
    private AjustesView _ajustes = null!;
    private Panel _casca = null!;
    private ContentControl _tela = null!;
    private readonly Dictionary<string, Button> _abas = [];
    private StackPanel _nav = null!;
    private Border _topo = null!;
    private Control _marca = null!, _direita = null!;
    private string _aba = "inicio";
    private WindowState _antesDaTelaCheia = WindowState.Normal;
    private (PixelPoint pos, Size tam, WindowState estado)? _antesDaMini;

    public MainWindow(MpvCore mpv)
    {
        _mpv = mpv;
        Title = "LibertyX Player";
        Width = 1280; Height = 800; MinWidth = 640; MinHeight = 420;
        Background = Ui.Pincel("Fundo");
        Foreground = Ui.Pincel("Texto");
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LibertyXPlayer/Assets/icone-256.png")));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, Soltou);
        AddHandler(KeyDownEvent, Tecla, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Construir();
        Opened += (_, _) => _ = ProcurarAtualizacao();
        PropertyChanged += (_, e) => { if (e.Property == ClientSizeProperty) AjustarBarra(); };
        Closing += (_, e) =>
        {
            // fechar a mini janela (Alt+F4, barra de tarefas) é voltar à janela grande, com o vídeo tocando
            if (_antesDaMini != null && !e.IsProgrammatic) { e.Cancel = true; SairDaMini(); return; }
            _player.Encerrar(); Prefs.Atual.Salvar();
        };
    }

    /// <summary>Monta tudo (de novo, ao trocar o idioma).</summary>
    private void Construir()
    {
        var tocando = _player?.Tocando == true;
        _player?.Desligar();
        // o player fica sempre na árvore, por baixo da casca: o OpenGL dele abre já na partida, e o mpv
        // encontra o contexto de vídeo pronto quando o primeiro arquivo carrega
        _player = new PlayerView(_mpv) { Janela = () => this, Exigir = Exigir };
        _player.Fechou += () => { SairDaMini(); SairDaTelaCheia(); _casca.IsVisible = true; Title = "LibertyX Player"; Atualizar(); };
        _player.AlternarTelaCheia += AlternarTelaCheia;
        _player.AlternarMiniJanela += AlternarMini;
        _player.TituloMudou += t => Title = $"{t} — LibertyX Player";

        _biblioteca = new BibliotecaView(Tocar, AdicionarPasta, AbrirArquivo, t => _arquivos.Adicionar(t, smb: t == null));
        _arquivos = new ArquivosView(Tocar, r => ProDialogo.Mostrar(r), () => _biblioteca.Pastas, AdicionarPasta, _biblioteca.MenuVideo);
        // servidor que está na barra de abas abre na aba dele; os outros, em Arquivos
        _arquivos.Abriu += aba => { if (Abas.Escolhidas().Contains(aba)) Ir(aba); else Ir("arquivos", raiz: false); };
        _procurar = new ProcurarView(Tocar, () => _biblioteca.Pastas, (c, i) => { Ir("arquivos"); _arquivos.AbrirItem(c, i); });
        _biblioteca.Varreu += () => { if (_aba == "arquivos") _arquivos.PastasMudaram(); };
        _ajustes = new AjustesView(Construir, AdicionarPasta, r => ProDialogo.Mostrar(r));
        Abas.Mudou -= MontarAbas; Abas.Mudou += MontarAbas;
        Rede.Contas.Mudou -= ServidoresMudaram; Rede.Contas.Mudou += ServidoresMudaram;
        Rede.Servidores.Mudou -= ServidoresMudaram; Rede.Servidores.Mudou += ServidoresMudaram;

        // ── barra de cima ──
        var marca = Ui.Pilha(10, Orientation.Horizontal, Ui.Marca(30), Ui.Linha("LibertyX", 18, FontWeight.ExtraBold, "rotulo"));
        _marca = marca;
        marca.VerticalAlignment = VerticalAlignment.Center;
        marca.Margin = new Thickness(0, 0, 18, 0);
        var nav = _nav = Ui.Pilha(4, Orientation.Horizontal);
        MontarAbas();
        var abrir = Ui.Botao(S("Abrir arquivo"), Icones.Arquivo, "vidro", AbrirArquivo);
        ((StackPanel)abrir.Content!).Children[1].Classes.Add("rotulo");
        ToolTip.SetTip(abrir, S("Abrir arquivo") + " (Ctrl+O)");
        var direita = Ui.Pilha(6, Orientation.Horizontal,
            ProSelo(),
            abrir,
            Ui.BotaoIcone(Icones.Link, S("Abrir endereço (http, https…)"), AbrirEndereco, "vidro", 18));
        direita.VerticalAlignment = VerticalAlignment.Center;
        _direita = direita;
        var barra = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(22, 12) };
        barra.Children.Add(marca);
        Grid.SetColumn(nav, 1); barra.Children.Add(nav);
        Grid.SetColumn(direita, 3); barra.Children.Add(direita);
        var topo = _topo = new Border { Child = barra, BorderBrush = Ui.Pincel("VidroBorda"), BorderThickness = new Thickness(0, 0, 0, 1), Background = Ui.Pincel("Superficie") };
        DockPanel.SetDock(topo, Dock.Top);

        _tela = new ContentControl();
        var casca = new DockPanel { Background = Ui.Pincel("Fundo") };
        casca.Children.Add(topo);
        casca.Children.Add(_tela);
        _casca = casca;

        var dialogos = new Panel();
        Dialogo.Iniciar(dialogos);
        var raiz = new Panel();
        raiz.Children.Add(_player);
        raiz.Children.Add(_casca);
        raiz.Children.Add(dialogos);
        Content = raiz;
        Ir(_aba);
        AjustarBarra();
        if (tocando) Dispatcher.UIThread.Post(() => _biblioteca.Varrer());
    }

    /// <summary>Início, as escolhidas em Ajustes › Barra de abas (até três) e Ajustes — como a dock do celular.</summary>
    private void MontarAbas()
    {
        _abas.Clear();
        _nav.Children.Clear();
        void Aba(string id, string nome, Control icone)
        {
            var b = new Button { Classes = { "nav" }, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icone, new TextBlock { Text = nome, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "rotulo" } } } } };
            ToolTip.SetTip(b, nome);
            b.Click += (_, _) => Ir(id);
            _abas[id] = b;
            _nav.Children.Add(b);
        }
        Aba("inicio", S("Início"), Ui.Icone(Icones.Biblioteca, 18));
        foreach (var a in Abas.Escolhidas())
        {
            if (a == "arquivos") Aba(a, S("Arquivos"), Ui.Icone(Icones.Pasta, 18));
            else if (a == "procurar") Aba(a, S("Procurar"), Ui.Icone(Icones.Buscar, 18));
            else if (a.StartsWith("smb:") && Rede.Servidores.Achar(a[4..]) is { } srv) Aba(a, srv.Nome, Logo("smb"));
            else if (a.StartsWith("conta:") && Rede.Contas.Achar(a[6..]) is { } c)
                Aba(a, c.Nome, c.Tipo == Rede.TipoDeConta.Jellyfin ? Logo("jellyfin") : Ui.Icone(c.Tipo == Rede.TipoDeConta.WebDav ? Icones.Nuvem : Icones.Servidor, 18));
        }
        Aba("ajustes", S("Ajustes"), Ui.Icone(Icones.Ajustes, 18));
        foreach (var (id, b) in _abas) b.Classes.Set("ativo", id == _aba);
        AjustarBarra();
    }

    /// <summary>
    /// A barra de cima cabe ou não com os nomes? Mede com eles; não cabendo, fica só com os ícones
    /// (o nome vai na dica) — a janela estreita, ou cheia de abas, não empurra nada para fora.
    /// </summary>
    private void AjustarBarra()
    {
        if (_topo == null || _direita == null) return;
        _topo.Classes.Set("compacto", false);
        var infinito = new Size(double.PositiveInfinity, double.PositiveInfinity);
        _marca.Measure(infinito); _nav.Measure(infinito); _direita.Measure(infinito);
        double precisa = _marca.DesiredSize.Width + _nav.DesiredSize.Width + _direita.DesiredSize.Width + 44 + 16;
        _topo.Classes.Set("compacto", precisa > ClientSize.Width);
    }

    /// <summary>O símbolo do próprio servidor (Jellyfin, SMB), com as cores dele.</summary>
    public static Control Logo(string nome, double tam = 18) => new Image
    {
        Source = new Bitmap(AssetLoader.Open(new Uri($"avares://LibertyXPlayer/Assets/{nome}.png"))), Width = tam, Height = tam,
    };

    private Control ProSelo()
    {
        var b = Ui.Botao(null, null, "plano", () => ProDialogo.Mostrar(null));
        void Atual()
        {
            b.IsVisible = Pro.Atual is not Pro.Estado.Comprado;
            b.Content = Ui.Pilha(6, Orientation.Horizontal, Ui.Icone(Icones.Estrela, 16, Ui.Pincel("Ouro")),
                Ui.Linha(Pro.Atual is Pro.Estado.Teste t ? P(t.DiasRestantes, "Pro grátis · {0} dia", "Pro grátis · {0} dias") : S("Desbloquear o Pro"), 13, FontWeight.Bold, "rotulo"));
            ToolTip.SetTip(b, S("Desbloquear o Pro"));
        }
        Atual();
        Pro.Mudou += () => Dispatcher.UIThread.Post(Atual);
        return b;
    }

    /// <summary>
    /// Troca de aba. Pelo botão (<paramref name="raiz"/>), a aba abre do começo — Início é o início,
    /// Arquivos é a lista de lugares —, como no celular, e não na última pasta deixada aberta.
    /// </summary>
    private void Ir(string aba, bool raiz = true)
    {
        // um servidor na barra de abas: a aba Arquivos, aberta direto nele
        Rede.Servidor? srv = aba.StartsWith("smb:") ? Rede.Servidores.Achar(aba[4..]) : null;
        Rede.Conta? conta = aba.StartsWith("conta:") ? Rede.Contas.Achar(aba[6..]) : null;
        if (aba.Contains(':') && srv == null && conta == null) aba = "inicio";
        if ((srv != null || conta != null) && !Exigir(Pro.Recurso.Rede)) return;
        _aba = aba;
        foreach (var (id, b) in _abas) b.Classes.Set("ativo", id == aba);
        if (aba != "arquivos") _arquivos.Sair();
        // a busca e a aba Arquivos leem as pastas da varredura do Início
        if (aba is "arquivos" or "procurar" && _biblioteca.Pastas.Count == 0) _biblioteca.Varrer();
        switch (aba)
        {
            case "arquivos": if (raiz) _arquivos.Raiz(); _tela.Content = _arquivos; _arquivos.Entrar(); break;
            case "procurar": _tela.Content = _procurar; if (raiz) _procurar.Raiz(); break;
            case "ajustes": _tela.Content = _ajustes; _ajustes.Montar(); break;
            case var _ when srv != null || conta != null:
                _tela.Content = _arquivos;
                if (raiz && srv != null) _arquivos.AbrirServidor(srv, comoAba: true);
                else if (raiz) _arquivos.AbrirConta(conta!, comoAba: true);
                break;
            default: if (raiz) _biblioteca.Raiz(); _tela.Content = _biblioteca; _biblioteca.Varrer(); break;
        }
    }

    /// <summary>De volta do player: a aba aberta refaz o progresso e o histórico, sem sair do lugar.</summary>
    private void Atualizar()
    {
        switch (_aba)
        {
            case "inicio": _biblioteca.Montar(); break;
            case "procurar": _procurar.Atualizar(); break;
            case "arquivos": _arquivos.Atualizar(); break;
            case var _ when _aba.Contains(':'): _arquivos.Atualizar(); break;
        }
    }

    /// <summary>Servidor apagado ou renomeado: a aba dele sai ou muda de nome.</summary>
    private void ServidoresMudaram() => Dispatcher.UIThread.Post(MontarAbas);

    // ───────── tocar ─────────

    public void Tocar(List<Midia> fila, int indice)
    {
        if (fila.Count == 0) return;
        if (fila.Any(m => m.DaRede) && !Exigir(Pro.Recurso.Rede)) return;
        Dialogo.Fechar();
        _casca.IsVisible = false;
        _player.Tocar(fila, indice);
    }

    private bool Exigir(Pro.Recurso r)
    {
        if (Pro.Ativo) return true;
        ProDialogo.Mostrar(r);
        return false;
    }

    /// <summary>Arquivos vindos da linha de comando ("Abrir com" do gerenciador de arquivos) ou soltos na janela.</summary>
    public void Abrir(IReadOnlyList<string> caminhos)
    {
        var arquivos = caminhos.Where(c => !c.StartsWith('-')).ToList();
        if (arquivos.Count == 0) return;
        if (arquivos.Count == 1 && arquivos[0].Contains("://") && !arquivos[0].StartsWith("file://")) { Tocar([Midia.DeUrl(arquivos[0])], 0); return; }
        var locais = arquivos.Select(a => a.StartsWith("file://") ? new Uri(a).LocalPath : Path.GetFullPath(a)).ToList();
        if (_player.Tocando && locais.Count == 1 && _player.AdicionarLegenda(locais[0])) return;
        var videos = locais.Where(File.Exists).ToList();
        foreach (var d in locais.Where(Directory.Exists))
            videos.AddRange(Directory.EnumerateFiles(d).Where(Biblioteca.EhVideo).Order(Natural.Comparador));
        if (videos.Count == 1)
        {
            // um arquivo só: a fila é a pasta dele, para o próximo/anterior funcionar como no celular
            var viz = Biblioteca.Vizinhos(videos[0]);
            int i = viz.FindIndex(v => v.Endereco == Path.GetFullPath(videos[0]));
            if (i >= 0) { Tocar(viz, i); return; }
            Tocar([Midia.DeArquivo(videos[0])], 0);
            return;
        }
        if (videos.Count > 0) Tocar(videos.Select(Midia.DeArquivo).ToList(), 0);
    }

    private void Soltou(object? s, DragEventArgs e)
    {
#pragma warning disable CS0618 // a API nova (DataTransfer) ainda não existe em todo o 11.3
        var itens = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
#pragma warning restore CS0618
        if (itens is { Count: > 0 }) Abrir(itens);
    }

    private async void AbrirArquivo()
    {
        var r = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = S("Abrir vídeo"), AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(S("Vídeos")) { Patterns = Biblioteca.Extensoes.Select(x => "*" + x).ToList(), MimeTypes = ["video/*"] }, FilePickerFileTypes.All],
        });
        var l = r.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (l.Count > 0) Abrir(l);
    }

    private void AbrirEndereco()
    {
        Dialogo.Pedir(S("Abrir endereço"), S("Um vídeo da internet ou da rede: http, https, rtsp, ou uma lista .m3u8. Se o yt-dlp estiver instalado, links de sites de vídeo também abrem."),
            "https://…", S("Abrir"), t =>
            {
                if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || u.Scheme is "file") return S("Isso não parece um endereço completo (começa com http:// ou https://).");
                Tocar([Midia.DeUrl(t)], 0);
                return null;
            });
    }

    private async void AdicionarPasta()
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = S("Acrescentar pasta à biblioteca"), AllowMultiple = true });
        foreach (var f in r.Select(x => x.TryGetLocalPath()).OfType<string>())
        {
            if (!Prefs.Atual.Pastas.Contains(f)) Prefs.Atual.Pastas.Add(f);
            Prefs.Atual.PastasOcultas.RemoveAll(o => o.StartsWith(f));
        }
        Prefs.Atual.Salvar();
        if (_aba == "ajustes") _ajustes.Montar();
        _biblioteca.Varrer();
    }

    // ───────── teclado ─────────

    private void Tecla(object? s, KeyEventArgs e)
    {
        if (Dialogo.Tecla(e)) { e.Handled = true; return; }
        if (Dialogo.Aberto) return;
        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { AbrirArquivo(); e.Handled = true; return; }
        if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Close(); e.Handled = true; return; }
        if (!_casca.IsVisible)
        {
            // campo de texto focado (busca) não perde as letras
            if (FocusManager?.GetFocusedElement() is TextBox) return;
            if (_player.Tecla(e)) e.Handled = true;
        }
    }

    // ───────── tela cheia e mini janela ─────────

    // Quem diz se está em tela cheia é o _player.TelaCheia, não o WindowState: no KDE (X11) a janela
    // em tela cheia volta como "Maximized" (o KWin manda os dois estados), e o botão entrava de novo
    // em vez de sair.
    private void AlternarTelaCheia()
    {
        if (_antesDaMini != null) SairDaMini();
        if (_player.TelaCheia) SairDaTelaCheia();
        else
        {
            _antesDaTelaCheia = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState;
            WindowState = WindowState.FullScreen;
            _player.TelaCheia = true;
        }
        _player.AjustarModo();
    }

    private void SairDaTelaCheia()
    {
        if (!_player.TelaCheia) return;
        // passa por FullScreen antes: se o estado lido já é o de destino (Maximized), o Avalonia não mexeria
        WindowState = WindowState.FullScreen;
        WindowState = _antesDaTelaCheia;
        _player.TelaCheia = false;
        _player.AjustarModo();
    }

    /// <summary>Mini janela (Pro): o vídeo numa janelinha sem bordas, sempre por cima, no canto da tela.</summary>
    private void AlternarMini()
    {
        if (_antesDaMini != null) { SairDaMini(); return; }
        SairDaTelaCheia();
        _antesDaMini = (Position, ClientSize, WindowState);
        WindowState = WindowState.Normal;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        MinWidth = 320; MinHeight = 180;
        // o tamanho que a pessoa deixou da última vez (as beiradas redimensionam: PlayerView.Beirada)
        double larg = Math.Max(320, Prefs.Atual.MiniLargura), alt = Math.Max(180, Prefs.Atual.MiniAltura);
        Width = larg; Height = alt;
        if (Screens.ScreenFromWindow(this) is { } tela)
        {
            var area = tela.WorkingArea;
            double esc = tela.Scaling;
            larg = Math.Min(larg, area.Width / esc - 40); alt = Math.Min(alt, area.Height / esc - 40);
            Width = larg; Height = alt;
            Position = new PixelPoint(area.Right - (int)((larg + 20) * esc), area.Bottom - (int)((alt + 20) * esc));
        }
        _player.Mini = true;
        _player.AjustarModo();
    }

    private void SairDaMini()
    {
        if (_antesDaMini is not { } a) return;
        _antesDaMini = null;
        Prefs.Atual.MiniLargura = Math.Round(ClientSize.Width);
        Prefs.Atual.MiniAltura = Math.Round(ClientSize.Height);
        Prefs.Atual.Salvar();
        Topmost = false;
        SystemDecorations = SystemDecorations.Full;
        MinWidth = 640; MinHeight = 420;
        Width = a.tam.Width; Height = a.tam.Height;
        Position = a.pos;
        WindowState = a.estado;
        _player.Mini = false;
        _player.AjustarModo();
    }

    // ───────── atualização ─────────

    /// <summary>Ao abrir: confere a versão publicada e, havendo nova, pergunta — sem interromper um vídeo nem outro diálogo.</summary>
    private async Task ProcurarAtualizacao()
    {
        if (Environment.GetEnvironmentVariable("LIBERTYX_SEM_ATUALIZACAO") == "1") return;
        await Task.Delay(TimeSpan.FromSeconds(4));
        var i = await Task.Run(() => Atualizacao.Conferir());
        if (i == null || !Atualizacao.Nova(i)) return;
        // espera o vídeo acabar ou o diálogo fechar (até umas duas horas); um filme não é cortado no meio
        for (int k = 0; k < 240 && (_player.Tocando || Dialogo.Aberto); k++) await Task.Delay(TimeSpan.FromSeconds(30));
        if (_player.Tocando || Dialogo.Aberto) return;
        OferecerAtualizacao(i);
    }

    private void OferecerAtualizacao(Atualizacao.Info i)
    {
        var p = Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(S("Atualização disponível"), 20, FontWeight.ExtraBold),
            Ui.Texto(F("Versão {0} · você está na {1}", i.Versao, "1.0." + Atualizacao.BuildAtual), 13, FontWeight.Normal, "apagado"));
        if (i.Novidades.Length > 0)
        {
            var n = Ui.Texto(S("O que mudou: ") + i.Novidades, 13, FontWeight.SemiBold);
            n.Foreground = Ui.Pincel("Ouro");
            p.Children.Add(n);
        }
        bool sozinho = Atualizacao.Instalado && i.Arquivo.Length > 0;
        p.Children.Add(Ui.Texto(sozinho
            ? S("O app baixa a versão nova, atualiza por cima e abre de novo sozinho. Ajustes, chave e histórico continuam.")
            : S("O navegador abre a página da versão nova para você baixar."), 13, FontWeight.Normal, "tenue"));
        var agora = Ui.Botao(sozinho ? S("Atualizar agora") : S("Baixar"), null, "ouro", () =>
        {
            if (sozinho) _ = BaixarAtualizacao(i);
            else { Ui.Abrir(Atualizacao.Pagina); Dialogo.Fechar(); }
        });
        var botoes = Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(S("Depois"), null, "plano", Dialogo.Fechar), agora);
        botoes.HorizontalAlignment = HorizontalAlignment.Right;
        p.Children.Add(botoes);
        Dialogo.Mostrar(p, 480);
    }

    private async Task BaixarAtualizacao(Atualizacao.Info i)
    {
        var barra = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6 };
        var texto = Ui.Texto(F("Baixando a versão {0}…", i.Versao), 14, FontWeight.SemiBold);
        Dialogo.Mostrar(Ui.Pilha(14, Orientation.Vertical, Ui.Texto(S("Atualizando"), 20, FontWeight.ExtraBold), texto, barra), 480);
        try
        {
            string setup = await Atualizacao.Baixar(i, new Progress<double>(x => barra.Value = x), CancellationToken.None);
            texto.Text = S("Instalando — o app fecha e abre de novo na versão nova.");
            _player.SalvarPosicao();
            Prefs.Atual.Salvar();
            Atualizacao.Instalar(setup);
            await Task.Delay(800);
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception e)
        {
            Diario.Erro(e);
            var erro = Ui.Texto(S("Não deu para baixar a atualização agora.") + "\n" + e.Message, 13);
            erro.Foreground = Ui.Pincel("Vermelho");
            var botoes = Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(S("Fechar"), null, "plano", Dialogo.Fechar),
                Ui.Botao(S("Abrir a página"), null, "vidro", () => { Ui.Abrir(Atualizacao.Pagina); Dialogo.Fechar(); }));
            botoes.HorizontalAlignment = HorizontalAlignment.Right;
            Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical, Ui.Texto(S("Atualização"), 20, FontWeight.ExtraBold), erro, botoes), 480);
        }
    }
}
