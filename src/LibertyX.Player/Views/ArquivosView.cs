using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;
using Item = LibertyX.Player.Rede.ServidorDeMidia.Item;

namespace LibertyX.Player.Views;

/// <summary>
/// A aba Arquivos, como no celular: o caminho de quem sabe onde o vídeo está. As pastas do
/// computador numa lista enxuta e, abaixo, cada servidor salvo — SMB, WebDAV (Nextcloud), Jellyfin e
/// Emby (Pro) — e os que a busca acha sozinha na rede. Dentro deles, pastas e vídeos que tocam direto
/// do servidor, sem baixar; o Jellyfin e o Emby abrem na tela inicial deles (continuar, a seguir,
/// recentes).
/// </summary>
public sealed partial class ArquivosView : DockPanel
{
    /// <summary>Um lugar dentro da aba; a pilha deles é o "voltar".</summary>
    private abstract record Lugar;
    private sealed record PastaLocal(PastaDeVideos Pasta) : Lugar;
    private sealed record NoSmb(Servidor Srv, string? Comp, string Caminho) : Lugar;
    private sealed record NoWebDav(Conta Conta, string Caminho, string Nome) : Lugar;
    private sealed record InicioDoServidor(Conta Conta) : Lugar;
    private sealed record NoServidor(Conta Conta, Item Item) : Lugar;
    private sealed record PaginaDoVideo(Conta Conta, Item Item) : Lugar { public EscolhasDaPagina Escolhas { get; } = new(); }
    private sealed record PaginaDaSerie(Conta Conta, string SerieId, string Nome) : Lugar { public string? Escolhida { get; set; } }
    private sealed record PaginaDaPessoa(Conta Conta, ServidorDeMidia.Pessoa Pessoa) : Lugar;
    private sealed record CanaisAoVivo(Conta Conta) : Lugar;

    private readonly Action<List<Midia>, int> _tocar;
    private readonly Action<Pro.Recurso> _mostrarPro;
    private readonly Func<List<PastaDeVideos>> _pastas;
    private readonly Action _adicionarPasta;
    private readonly Func<Midia, MenuFlyout?> _menuLocal;
    private readonly StackPanel _corpo = new() { Spacing = 18, Margin = new Thickness(28, 6, 28, 28) };
    private readonly TextBlock _titulo;
    private readonly Button _voltar, _adicionar;
    private readonly ScrollViewer _rolagem;
    private CancellationTokenSource? _busca;
    private readonly List<Achado> _achados = [];
    private bool _procurando;
    private readonly List<Lugar> _pilha = [];
    private ConexaoSmb? _con;
    private int _geracao;
    /// <summary>Aberta como aba própria de um servidor (barra de abas): o "voltar" para no começo dele.</summary>
    private int _piso;

    /// <summary>Um servidor foi aberto daqui de dentro (depois de cadastrar): a janela mostra a aba dele ("smb:…", "conta:…") ou esta.</summary>
    public event Action<string>? Abriu;

    public ArquivosView(Action<List<Midia>, int> tocar, Action<Pro.Recurso> mostrarPro, Func<List<PastaDeVideos>> pastas,
        Action adicionarPasta, Func<Midia, MenuFlyout?> menuLocal)
    {
        _tocar = tocar; _mostrarPro = mostrarPro; _pastas = pastas; _adicionarPasta = adicionarPasta; _menuLocal = menuLocal;
        _voltar = Ui.BotaoIcone(Icones.Voltar, S("Voltar"), Subir, "plano");
        _titulo = Ui.Linha(S("Arquivos"), 26, FontWeight.ExtraBold);
        _titulo.VerticalAlignment = VerticalAlignment.Center;
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(28, 18, 28, 10) };
        cab.Children.Add(_voltar);
        Grid.SetColumn(_titulo, 1); _titulo.Margin = new Thickness(4, 0);
        cab.Children.Add(_titulo);
        _adicionar = Ui.Botao(S("Adicionar servidor"), Icones.Adicionar, "vidro", () => Adicionar());
        Grid.SetColumn(_adicionar, 2);
        cab.Children.Add(_adicionar);
        SetDock(cab, Dock.Top);
        Children.Add(cab);
        _rolagem = new ScrollViewer { Content = _corpo };
        Children.Add(_rolagem);
        Servidores.Mudou += () => { if (_pilha.Count == 0) Dispatcher.UIThread.Post(Montar); };
        Contas.Mudou += () => { if (_pilha.Count == 0) Dispatcher.UIThread.Post(Montar); };
        Pro.Mudou += () => Dispatcher.UIThread.Post(Montar);
    }

    public void Entrar()
    {
        Montar();
        if (_pilha.Count == 0 && Pro.Ativo && !_procurando && _achados.Count == 0) Procurar();
    }

    public void Sair() { _busca?.Cancel(); }

    /// <summary>O botão da dock: volta ao começo da aba, como no celular.</summary>
    public void Raiz()
    {
        _piso = 0;
        _pilha.Clear();
        _con?.Dispose(); _con = null;
        _rolagem.Offset = default;
    }

    /// <summary>A varredura do Início terminou: a lista "Neste computador" muda.</summary>
    public void PastasMudaram() { if (_pilha.Count == 0 || _pilha[^1] is PastaLocal) Montar(); }

    /// <summary>De volta do player: refaz a tela (progresso, "continuar" do servidor) sem perder o lugar.</summary>
    public void Atualizar() => Montar();

    public void AbrirServidor(Servidor s, bool comoAba = false)
    {
        _busca?.Cancel();
        _pilha.Clear();
        _piso = comoAba ? 1 : 0;
        _pilha.Add(new NoSmb(s, s.Compartilhamento, ""));
        _rolagem.Offset = default;
        Montar();
    }

    public void AbrirConta(Conta c, bool comoAba = false)
    {
        _busca?.Cancel();
        _pilha.Clear();
        _piso = comoAba ? 1 : 0;
        _pilha.Add(c.Tipo == TipoDeConta.WebDav ? new NoWebDav(c, new WebDav(c).Raiz, c.Nome) : new InicioDoServidor(c));
        _rolagem.Offset = default;
        Montar();
    }

    private void Ir(Lugar l) { _pilha.Add(l); _rolagem.Offset = default; Montar(); }

    private void Subir()
    {
        if (_pilha.Count <= _piso) return;
        _pilha.RemoveAt(_pilha.Count - 1);
        if (!_pilha.Any(l => l is NoSmb)) { _con?.Dispose(); _con = null; }
        Montar();
    }

    private void Montar()
    {
        _geracao++;
        _corpo.Children.Clear();
        _voltar.IsVisible = _pilha.Count > _piso;
        _adicionar.IsVisible = _pilha.Count == 0 && Pro.Ativo;
        if (_pilha.Count == 0) { _titulo.Text = S("Arquivos"); Inicio(); return; }
        switch (_pilha[^1])
        {
            case PastaLocal p:
                _titulo.Text = p.Pasta.Nome;
                _corpo.Children.Add(Cartoes.Videos(Biblioteca.Ordenar(p.Pasta.Videos, Prefs.Atual.Ordem).ToList(), _tocar, _menuLocal));
                break;
            case NoSmb n:
                _titulo.Text = n.Comp == null ? n.Srv.Nome : $"{n.Srv.Nome} › {n.Comp}{(n.Caminho.Length > 0 ? " › " + n.Caminho.Replace("/", " › ") : "")}";
                ListarSmb(n);
                break;
            case NoWebDav d:
                _titulo.Text = d.Conta.Nome + (d.Nome != d.Conta.Nome ? " › " + d.Nome : "");
                ListarWebDav(d);
                break;
            case InicioDoServidor i:
                _titulo.Text = i.Conta.Nome;
                MostrarInicioDoServidor(i.Conta);
                break;
            case NoServidor s:
                _titulo.Text = $"{s.Conta.Nome} › {s.Item.Titulo}";
                ListarServidor(s);
                break;
            case PaginaDoVideo v:
                _titulo.Text = $"{v.Conta.Nome} › {(v.Item.Tipo == "Episode" && v.Item.Serie != null ? v.Item.Serie : v.Item.Titulo)}";
                MostrarPaginaDoVideo(v);
                break;
            case PaginaDaSerie ps:
                _titulo.Text = $"{ps.Conta.Nome} › {ps.Nome}";
                MostrarPaginaDaSerie(ps);
                break;
            case PaginaDaPessoa pp:
                _titulo.Text = $"{pp.Conta.Nome} › {pp.Pessoa.Nome}";
                MostrarPaginaDaPessoa(pp);
                break;
            case CanaisAoVivo c:
                _titulo.Text = $"{c.Conta.Nome} › {S("TV ao vivo")}";
                MostrarCanais(c);
                break;
        }
    }

    // ───────── a raiz ─────────

    private void Inicio()
    {
        // neste computador
        var pastas = _pastas();
        _corpo.Children.Add(Ui.Texto(S("Neste computador"), 17, FontWeight.Bold));
        var locais = new WrapPanel();
        foreach (var p in pastas)
            locais.Children.Add(Cartoes.Lugar(Icones.Pasta, p.Nome, $"{P(p.Videos.Count, "{0} vídeo", "{0} vídeos")} · {BibliotecaView.Onde(p)}", () => Ir(new PastaLocal(p))));
        locais.Children.Add(Cartoes.Lugar(Icones.Adicionar, S("Acrescentar pasta"), S("Outra pasta com vídeos"), _adicionarPasta, Ui.Pincel("Apagado")));
        _corpo.Children.Add(locais);

        // servidores
        _corpo.Children.Add(Ui.Texto(S("Servidores"), 17, FontWeight.Bold));
        if (!Pro.Ativo) { _corpo.Children.Add(Bloqueado()); return; }
        var wrap = new WrapPanel();
        foreach (var s in Servidores.Lista) wrap.Children.Add(CartaoServidor(MainWindow.Logo("smb", 26), s.Nome, s.Host + (s.Compartilhamento is { Length: > 0 } c ? " / " + c : "") + " · SMB",
            () => AbrirServidor(s), () => Formulario(s, null), () => Servidores.Remover(s)));
        foreach (var c in Contas.Lista)
            wrap.Children.Add(CartaoServidor(c.Tipo == TipoDeConta.Jellyfin ? MainWindow.Logo("jellyfin", 26) : Ui.Icone(c.Tipo == TipoDeConta.WebDav ? Icones.Nuvem : Icones.Servidor, 26, Ui.Pincel("Ouro")),
                c.Nome, c.EnderecoCurto + " · " + c.NomeDoTipo, () => AbrirConta(c), () => FormularioConta(c.Tipo, c), () => Contas.Remover(c)));
        wrap.Children.Add(Cartoes.Lugar(Icones.Adicionar, S("Adicionar servidor"), S("SMB, WebDAV, Jellyfin e Emby"), () => Adicionar(), Ui.Pincel("Apagado")));
        _corpo.Children.Add(wrap);

        // na rede
        var cab = Ui.Pilha(10, Orientation.Horizontal, Ui.Texto(S("Na sua rede"), 17, FontWeight.Bold));
        if (_procurando) cab.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 80, VerticalAlignment = VerticalAlignment.Center });
        else cab.Children.Add(Ui.BotaoIcone(Icones.Atualizar, S("Procurar de novo"), Procurar, "plano", 16));
        _corpo.Children.Add(cab);
        var novos = _achados.Where(a => !Servidores.Lista.Any(s => s.Host == a.Endereco)).ToList();
        if (novos.Count == 0)
            _corpo.Children.Add(Ui.Texto(_procurando ? S("Procurando computadores com pastas compartilhadas…")
                : S("Nenhum servidor apareceu sozinho. Use \"Adicionar servidor\" e digite o endereço (ex.: 192.168.0.10)."), 14, FontWeight.Normal, "apagado"));
        var wrap2 = new WrapPanel();
        foreach (var a in novos) wrap2.Children.Add(Cartoes.Lugar(Icones.Servidor, a.Nome, a.Endereco, () => Formulario(null, a), Ui.Pincel("Apagado")));
        _corpo.Children.Add(wrap2);
    }

    private Control Bloqueado()
    {
        var p = Ui.Pilha(12, Orientation.Vertical,
            Ui.Pilha(10, Orientation.Horizontal, Ui.Icone(Icones.Estrela, 22, Ui.Pincel("Ouro")), Ui.Texto(Pro.Nome(Pro.Recurso.Rede), 17, FontWeight.ExtraBold)),
            Ui.Texto(Pro.Descricao(Pro.Recurso.Rede), 14, FontWeight.Normal, "apagado"),
            Ui.Texto(S("SMB (Windows, Samba, NAS), WebDAV e Nextcloud, Jellyfin e Emby."), 13, FontWeight.Normal, "tenue"),
            Ui.Botao(S("Conhecer o LibertyX Pro"), Icones.Estrela, "ouro", () => _mostrarPro(Pro.Recurso.Rede)));
        return new Border { Classes = { "painel" }, Padding = new Thickness(20), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, Child = p };
    }

    private static Control CartaoServidor(Control icone, string nome, string sub, Action abrir, Action editar, Action remover)
    {
        var b = new Button
        {
            Classes = { "vidro" }, Margin = new Thickness(0, 0, 12, 12), Width = 270, MinHeight = 68, Padding = new Thickness(16, 12),
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
            Content = Ui.Pilha(14, Orientation.Horizontal, icone,
                Ui.Pilha(2, Orientation.Vertical, Ui.Linha(nome, 15, FontWeight.Bold), Ui.Linha(sub, 12, FontWeight.Normal, "tenue"))),
        };
        ToolTip.SetTip(b, sub);
        b.Click += (_, _) => abrir();
        var menu = new MenuFlyout();
        var e = new MenuItem { Header = S("Editar") };
        e.Click += (_, _) => editar();
        var r = new MenuItem { Header = S("Remover") };
        r.Click += (_, _) => remover();
        menu.Items.Add(e); menu.Items.Add(r);
        b.ContextFlyout = menu;
        return b;
    }

    private void Procurar()
    {
        _busca?.Cancel();
        _busca = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var ct = _busca.Token;
        _procurando = true;
        if (_pilha.Count == 0) Montar();
        _ = Task.Run(async () =>
        {
            try
            {
                await Descoberta.Procurar(a => Dispatcher.UIThread.Post(() =>
                {
                    if (_achados.Any(x => x.Endereco == a.Endereco)) return;
                    _achados.Add(a);
                    if (_pilha.Count == 0) Montar();
                }), ct);
            }
            catch { }
            Dispatcher.UIThread.Post(() => { _procurando = false; if (_pilha.Count == 0) Montar(); });
        });
    }

    // ───────── cadastro ─────────

    /// <summary>O "Adicionar servidor": null pergunta o tipo; o resto vai direto ao formulário dele.</summary>
    public void Adicionar(TipoDeConta? tipo = null, bool smb = false)
    {
        if (!Pro.Ativo) { _mostrarPro(Pro.Recurso.Rede); return; }
        if (smb) { Formulario(null, null); return; }
        if (tipo is { } t) { FormularioConta(t, null); return; }
        Control Opcao(string icone, string nome, string sub, Action a)
        {
            var b = Cartoes.Lugar(icone, nome, sub, a);
            b.Width = double.NaN; b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Margin = default;
            return b;
        }
        Dialogo.Mostrar(Ui.Pilha(10, Orientation.Vertical,
            Ui.Texto(S("Adicionar servidor"), 20, FontWeight.ExtraBold),
            Opcao(Icones.Servidor, "SMB", S("Windows, Samba, NAS, HD do roteador"), () => Formulario(null, null)),
            Opcao(Icones.Nuvem, "WebDAV / Nextcloud", S("Nextcloud, ownCloud e outros"), () => FormularioConta(TipoDeConta.WebDav, null)),
            Opcao(Icones.Filme, "Jellyfin", S("O arquivo original, sem converter"), () => FormularioConta(TipoDeConta.Jellyfin, null)),
            Opcao(Icones.Tv, "Emby", S("O arquivo original, sem converter"), () => FormularioConta(TipoDeConta.Emby, null)),
            Ui.Botao(S("Cancelar"), null, "vidro", Dialogo.Fechar)), 460);
    }

    private void Formulario(Servidor? existente, Achado? achado)
    {
        var s = existente ?? new Servidor { Nome = achado?.Nome ?? "", Host = achado?.Endereco ?? "" };
        var nome = new TextBox { Text = s.Nome, Watermark = S("Nome (ex.: PC da sala)") };
        var host = new TextBox { Text = s.Host, Watermark = S("Endereço (ex.: 192.168.0.10)") };
        var comp = new TextBox { Text = s.Compartilhamento, Watermark = S("Pasta compartilhada (opcional)") };
        var usuario = new TextBox { Text = s.Usuario, Watermark = S("Usuário") };
        var senha = new TextBox { Text = s.Senha, Watermark = S("Senha"), PasswordChar = '•', RevealPassword = false };
        var dominio = new TextBox { Text = s.Dominio, Watermark = S("Domínio (quase nunca precisa)") };
        var convidado = new CheckBox { Content = S("Entrar como convidado (sem senha)"), IsChecked = s.Convidado };
        void Conv() { usuario.IsEnabled = senha.IsEnabled = dominio.IsEnabled = convidado.IsChecked != true; }
        convidado.IsCheckedChanged += (_, _) => Conv();
        Conv();
        var erro = Ui.Texto("", 13); erro.Foreground = Ui.Pincel("Vermelho"); erro.IsVisible = false;
        var salvar = Ui.Botao(S("Conectar e salvar"), null, "ouro", null);
        salvar.Click += async (_, _) =>
        {
            var novo = new Servidor
            {
                Id = s.Id, Nome = string.IsNullOrWhiteSpace(nome.Text) ? host.Text?.Trim() ?? "" : nome.Text.Trim(), Host = host.Text?.Trim() ?? "",
                Compartilhamento = string.IsNullOrWhiteSpace(comp.Text) ? null : comp.Text.Trim().Trim('/', '\\'),
                Usuario = usuario.Text?.Trim() ?? "", Senha = senha.Text ?? "", Dominio = dominio.Text?.Trim() ?? "", Convidado = convidado.IsChecked == true,
            };
            if (novo.Host.Length == 0) { erro.Text = S("Digite o endereço do servidor."); erro.IsVisible = true; return; }
            novo.Host = novo.Host.Replace("smb://", "").Replace("\\\\", "").TrimEnd('/');
            salvar.IsEnabled = false; erro.IsVisible = false;
            salvar.Content = S("Conectando…");
            string? falha = await Task.Run(() =>
            {
                try
                {
                    using var c = ConexaoSmb.Abrir(novo);
                    if (novo.Compartilhamento != null) c.Arvore(novo.Compartilhamento); else c.Compartilhamentos();
                    return null;
                }
                catch (Exception e) { return e.Message; }
            });
            salvar.IsEnabled = true; salvar.Content = S("Conectar e salvar");
            if (falha != null) { erro.Text = falha; erro.IsVisible = true; return; }
            Servidores.Salvar(novo);
            Dialogo.Fechar();
            AbrirServidor(novo);
            Abriu?.Invoke("smb:" + novo.Id);
        };
        var botoes = Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(S("Cancelar"), null, "vidro", Dialogo.Fechar), salvar);
        botoes.HorizontalAlignment = HorizontalAlignment.Right;
        Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(existente == null ? S("Adicionar servidor") : S("Editar servidor"), 20, FontWeight.ExtraBold),
            Ui.Texto(S("Um computador com pasta compartilhada (Windows ou Samba), um NAS ou o HD do roteador."), 13, FontWeight.Normal, "apagado"),
            nome, host, comp, convidado, usuario, senha, dominio, erro, botoes), 480);
    }

    /// <summary>WebDAV, Jellyfin ou Emby: nome, endereço, usuário e senha — o mesmo formulário do celular.</summary>
    private void FormularioConta(TipoDeConta tipo, Conta? existente)
    {
        string nomeTipo = tipo switch { TipoDeConta.WebDav => "WebDAV", TipoDeConta.Jellyfin => "Jellyfin", _ => "Emby" };
        bool midia = tipo != TipoDeConta.WebDav;
        var nome = new TextBox { Text = existente?.Nome, Watermark = S("Nome (ex.: Filmes de casa)") };
        var endereco = new TextBox
        {
            Text = existente?.Endereco,
            Watermark = midia ? F("Endereço (ex.: {0})", "192.168.0.10:8096") : F("Endereço (ex.: {0})", "nuvem.exemplo.com"),
        };
        var usuario = new TextBox { Text = existente?.Usuario, Watermark = S("Usuário") };
        var senha = new TextBox
        {
            Text = existente != null && !midia ? existente.Segredo : "", PasswordChar = '•',
            Watermark = existente != null && midia ? S("Senha (só para entrar de novo)") : S("Senha"),
        };
        var erro = Ui.Texto("", 13); erro.Foreground = Ui.Pincel("Vermelho"); erro.IsVisible = false;
        // Jellyfin/Emby: os usuários que o servidor mostra na tela de entrada, para escolher sem digitar
        var usuarios = new WrapPanel { IsVisible = false };
        string? lidoDe = null;
        async void LerUsuarios()
        {
            var digitado = endereco.Text?.Trim() ?? "";
            if (!midia || digitado.Length == 0 || digitado == lidoDe) return;
            lidoDe = digitado;
            foreach (var cand in Contas.Candidatos(digitado))
            {
                var nomes = await ServidorDeMidia.UsuariosVisiveis(new Conta { Tipo = tipo, Endereco = cand.TrimEnd('/') });
                if (lidoDe != digitado) return;
                if (nomes.Count == 0) continue;
                usuarios.Children.Clear();
                foreach (var n in nomes)
                {
                    var b = Ui.Botao(n, Icones.Pessoa, "vidro", () => { usuario.Text = n; senha.Focus(); }, 16);
                    b.Margin = new Thickness(0, 0, 8, 8);
                    usuarios.Children.Add(b);
                }
                usuarios.IsVisible = true;
                return;
            }
        }
        endereco.LostFocus += (_, _) => LerUsuarios();
        if (existente != null && midia) Dispatcher.UIThread.Post(LerUsuarios);
        StackPanel form = null!;
        var quick = Ui.Botao(S("Entrar com Quick Connect"), Icones.Link, "vidro", null);
        quick.IsVisible = midia && tipo == TipoDeConta.Jellyfin;
        ToolTip.SetTip(quick, S("Sem digitar a senha: aprove este computador num app do Jellyfin já conectado."));
        quick.Click += async (_, _) =>
        {
            var digitado = endereco.Text?.Trim() ?? "";
            if (digitado.Length == 0) { erro.Text = S("Digite o endereço do servidor."); erro.IsVisible = true; return; }
            quick.IsEnabled = false; erro.IsVisible = false;
            Conta? base_ = null;
            (string codigo, string segredo) qc = ("", "");
            string? falha = null;
            var cands = Contas.Candidatos(digitado).ToList();
            for (int i = 0; i < cands.Count; i++)
            {
                var c = new Conta { Id = existente?.Id ?? Guid.NewGuid().ToString("N")[..10], Tipo = tipo, Endereco = cands[i].TrimEnd('/') };
                try { qc = await ServidorDeMidia.IniciarQuickConnect(c); base_ = c; break; }
                catch (Exception e)
                {
                    if (Contas.NemConectou(e) && i < cands.Count - 1) continue;
                    falha = e is ErroDeServidor ? e.Message : S("O servidor não respondeu. Confira o endereço e a porta.");
                    break;
                }
            }
            quick.IsEnabled = true;
            if (base_ == null) { erro.Text = falha ?? S("O servidor não respondeu. Confira o endereço e a porta."); erro.IsVisible = true; return; }
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var codigo = Ui.Texto(qc.codigo, 40, FontWeight.ExtraBold);
            codigo.Foreground = Ui.Pincel("Ouro");
            codigo.HorizontalAlignment = HorizontalAlignment.Center;
            bool desistiu = false;
            void VoltarAoFormulario()
            {
                cts.Cancel();
                if (form.Parent is ScrollViewer sv) sv.Content = null;
                Dialogo.Mostrar(form, 500);
            }
            Dialogo.Mostrar(Ui.Pilha(14, Orientation.Vertical,
                Ui.Texto(S("Quick Connect"), 20, FontWeight.ExtraBold),
                Ui.Texto(S("Num app do Jellyfin já conectado (celular, TV, navegador), abra Configurações › Quick Connect e digite este código:"), 13, FontWeight.Normal, "apagado"),
                codigo,
                new ProgressBar { IsIndeterminate = true },
                Ui.Texto(S("Esperando a aprovação…"), 13, FontWeight.Normal, "tenue"),
                Ui.Botao(S("Cancelar"), null, "vidro", () => { desistiu = true; VoltarAoFormulario(); })), 500);
            try
            {
                var (nomeDoUsuario, idUsuario, token) = await ServidorDeMidia.EsperarQuickConnect(base_, qc.segredo, cts.Token);
                base_.Usuario = nomeDoUsuario; base_.IdNoServidor = idUsuario; base_.Segredo = token;
                base_.Nome = string.IsNullOrWhiteSpace(nome.Text) ? base_.EnderecoCurto : nome.Text.Trim();
                Contas.Salvar(base_);
                Dialogo.Fechar();
                AbrirConta(base_);
                Abriu?.Invoke("conta:" + base_.Id);
            }
            catch (Exception e)
            {
                if (desistiu) return;
                VoltarAoFormulario();
                erro.Text = e is OperationCanceledException ? S("O código expirou. Tente de novo.") : e.Message;
                erro.IsVisible = true;
            }
        };
        var salvar = Ui.Botao(S("Conectar e salvar"), null, "ouro", null);
        salvar.Click += async (_, _) =>
        {
            var digitado = endereco.Text?.Trim() ?? "";
            if (digitado.Length == 0 || (midia && string.IsNullOrWhiteSpace(usuario.Text)))
            {
                erro.Text = midia ? S("Digite o endereço e o usuário.") : S("Digite o endereço do servidor.");
                erro.IsVisible = true;
                return;
            }
            salvar.IsEnabled = false; erro.IsVisible = false; salvar.Content = S("Conectando…");
            Conta? pronta = null;
            string? falha = null;
            var candidatos = Contas.Candidatos(digitado).ToList();
            for (int i = 0; i < candidatos.Count; i++)
            {
                var c = new Conta
                {
                    Id = existente?.Id ?? Guid.NewGuid().ToString("N")[..10], Tipo = tipo, Endereco = candidatos[i].TrimEnd('/') + (midia ? "" : "/"),
                    Usuario = usuario.Text?.Trim() ?? "", Segredo = senha.Text ?? "", IdNoServidor = existente?.IdNoServidor,
                };
                c.Nome = string.IsNullOrWhiteSpace(nome.Text) ? c.EnderecoCurto : nome.Text.Trim();
                try
                {
                    if (!midia) pronta = await WebDav.Testar(c);
                    // editar só o apelido não pede a senha de novo; trocar o servidor ou o usuário pede. Com a chave
                    // vencida (servidor refeito, sessão apagada no painel), entra de novo — usuário sem senha também
                    else if (existente != null && existente.Endereco == c.Endereco && existente.Usuario == c.Usuario && c.Segredo.Length == 0)
                    {
                        c.Segredo = existente.Segredo;
                        try { await new ServidorDeMidia(c).Bibliotecas(); }
                        catch (ErroDeServidor)
                        {
                            var (idUsuario, token) = await ServidorDeMidia.Entrar(c, "");
                            c.IdNoServidor = idUsuario; c.Segredo = token;
                        }
                        pronta = c;
                    }
                    else
                    {
                        var (idUsuario, token) = await ServidorDeMidia.Entrar(c, c.Segredo);
                        c.IdNoServidor = idUsuario; c.Segredo = token;
                        pronta = c;
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (Contas.NemConectou(e) && i < candidatos.Count - 1) continue;
                    falha = e is ErroDeServidor ? e.Message : Contas.NemConectou(e) ? S("O servidor não respondeu. Confira o endereço e a porta.") : e.Message;
                    break;
                }
            }
            salvar.IsEnabled = true; salvar.Content = S("Conectar e salvar");
            if (pronta == null) { erro.Text = falha ?? S("O servidor não respondeu. Confira o endereço e a porta."); erro.IsVisible = true; return; }
            Contas.Salvar(pronta);
            Dialogo.Fechar();
            AbrirConta(pronta);
            Abriu?.Invoke("conta:" + pronta.Id);
        };
        var botoes = Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(S("Cancelar"), null, "vidro", Dialogo.Fechar), salvar);
        botoes.HorizontalAlignment = HorizontalAlignment.Right;
        var rodape = Ui.Pilha(10, Orientation.Vertical, quick, botoes);
        Dialogo.Mostrar(form = Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(existente == null ? F("Novo servidor {0}", nomeTipo) : S("Editar servidor"), 20, FontWeight.ExtraBold),
            Ui.Texto(midia ? S("O app entra com o seu usuário e guarda só a chave de acesso, não a senha. Os vídeos tocam do arquivo original, sem o servidor converter nada.")
                : S("Do Nextcloud basta o endereço do servidor: o app acha a pasta WebDAV sozinho. Sem https://, tenta primeiro com ele."), 13, FontWeight.Normal, "apagado"),
            nome, endereco, usuarios, usuario, senha, erro, rodape), 500);
    }

    // ───────── SMB ─────────

    private void ListarSmb(NoSmb n)
    {
        int ger = _geracao;
        _corpo.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, Margin = new Thickness(0, 40) });
        Task.Run(() =>
        {
            try
            {
                if (_con != null && _con.Servidor.Id != n.Srv.Id) { _con.Dispose(); _con = null; }
                _con ??= ConexaoSmb.Abrir(n.Srv);
                if (n.Comp == null) return (object)_con.Compartilhamentos();
                return _con.Listar(n.Comp, n.Caminho);
            }
            catch (Exception e)
            {
                try { _con?.Dispose(); } catch { }
                _con = null;
                return e;
            }
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (ger != _geracao) return;   // a pessoa já foi para outro lugar
            _corpo.Children.Clear();
            switch (t.Result)
            {
                case Exception e:
                    Erro(e is SmbErro ? e.Message : F("Não consegui conectar: {0}", e.Message), () => Formulario(n.Srv, null));
                    break;
                case List<string> shares:
                    var w = new WrapPanel();
                    foreach (var sh in shares) w.Children.Add(Cartoes.Lugar(Icones.Pasta, sh, null, () => Ir(n with { Comp = sh, Caminho = "" })));
                    if (shares.Count == 0) _corpo.Children.Add(Ui.Texto(S("Este servidor não tem pastas compartilhadas visíveis."), 14, FontWeight.Normal, "apagado"));
                    _corpo.Children.Add(w);
                    break;
                case List<ItemSmb> itens:
                    Mostrar(itens.Where(i => i.Pasta).OrderBy(i => i.Nome, Natural.Comparador).Select(p => (p.Nome, (Action)(() => Ir(n with { Caminho = p.Caminho })))),
                        itens.Where(i => !i.Pasta).Select(i => new Midia
                        {
                            Endereco = ConexaoSmb.Endereco(n.Srv, n.Comp!, i.Caminho), Titulo = Path.GetFileNameWithoutExtension(i.Nome), Origem = Origem.Smb,
                            Tamanho = i.Tamanho, Data = i.Data, Pasta = n.Srv.Nome,
                        }));
                    break;
            }
        }));
    }

    /// <summary>Pastas em cima, vídeos embaixo (na ordem escolhida).</summary>
    private void Mostrar(IEnumerable<(string nome, Action abrir)> pastas, IEnumerable<Midia> videos)
    {
        var ps = pastas.ToList();
        var lista = Biblioteca.Ordenar(videos, Prefs.Atual.Ordem).ToList();
        if (ps.Count == 0 && lista.Count == 0) { _corpo.Children.Add(Ui.Texto(S("Nenhum vídeo nesta pasta."), 14, FontWeight.Normal, "apagado")); return; }
        if (ps.Count > 0)
        {
            var w = new WrapPanel();
            foreach (var (nome, abrir) in ps) w.Children.Add(Cartoes.Lugar(Icones.Pasta, nome, null, abrir));
            _corpo.Children.Add(w);
        }
        if (lista.Count > 0) _corpo.Children.Add(Cartoes.Videos(lista, _tocar));
    }

    private void Erro(string texto, Action editar)
    {
        _corpo.Children.Add(Ui.Texto(texto, 15, FontWeight.SemiBold));
        _corpo.Children.Add(Ui.Pilha(10, Orientation.Horizontal,
            Ui.Botao(S("Tentar de novo"), Icones.Atualizar, "ouro", Montar),
            Ui.Botao(S("Editar servidor"), Icones.Editar, "vidro", editar)));
    }

    /// <summary>Carrega fora da thread da tela e mostra só se a pessoa continua no mesmo lugar.</summary>
    private void Carregar<T>(Func<Task<T>> ler, Action<T> mostrar, Conta conta)
    {
        int ger = _geracao;
        _corpo.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, Margin = new Thickness(0, 40) });
        Task.Run(async () =>
        {
            try { return await ler(); }
            catch (ErroDeServidor e) when (e.Recusou && conta.EhServidorDeMidia)
            {
                // a chave venceu (servidor refeito, sessão apagada no painel): quem entra sem senha entra
                // de novo sozinho, como no celular; com senha, cai no aviso abaixo
                var (id, token) = await ServidorDeMidia.Entrar(conta, "");
                conta.IdNoServidor = id; conta.Segredo = token;
                Dispatcher.UIThread.Post(() => Contas.Salvar(conta));
                return await ler();
            }
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (ger != _geracao) return;
            _corpo.Children.Clear();
            if (t.IsCompletedSuccessfully) { mostrar(t.Result); return; }
            var e = t.Exception?.InnerException ?? t.Exception;
            Erro(e is ErroDeServidor { Recusou: true } && conta.EhServidorDeMidia ? S("O servidor não aceita mais a entrada guardada. Clique em Editar servidor e digite a senha de novo.")
                : e is ErroDeServidor ? e.Message : F("Não consegui conectar: {0}", e?.Message ?? ""), () => FormularioConta(conta.Tipo, conta));
        }));
    }

    // ───────── WebDAV ─────────

    private void ListarWebDav(NoWebDav d) => Carregar(() => new WebDav(d.Conta).Listar(d.Caminho), entradas =>
        Mostrar(entradas.Where(e => e.Pasta).OrderBy(e => e.Nome, Natural.Comparador).Select(e => (e.Nome, (Action)(() => Ir(new NoWebDav(d.Conta, e.Caminho, e.Nome))))),
            entradas.Where(e => !e.Pasta && Biblioteca.EhVideo(e.Nome)).Select(e => new Midia
            {
                Endereco = Contas.EnderecoWebDav(d.Conta, e.Caminho), Titulo = Path.GetFileNameWithoutExtension(e.Nome), Origem = Origem.WebDav,
                Tamanho = e.Tamanho, Data = e.Data, Pasta = d.Conta.Nome,
            })), d.Conta);
}
