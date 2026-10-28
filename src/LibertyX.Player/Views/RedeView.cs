using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>
/// Pastas da rede (Pro): os servidores salvos, os que a busca acha sozinha, e a navegação dentro
/// deles — compartilhamentos, pastas e vídeos, que tocam direto do servidor, sem baixar.
/// </summary>
public sealed class RedeView : DockPanel
{
    private readonly Action<List<Midia>, int> _tocar;
    private readonly Action<Pro.Recurso> _mostrarPro;
    private readonly StackPanel _corpo = new() { Spacing = 18, Margin = new Thickness(28, 6, 28, 28) };
    private readonly TextBlock _titulo;
    private readonly Button _voltar;
    private Button? _adicionar;
    private CancellationTokenSource? _busca;
    private readonly List<Achado> _achados = [];
    private bool _procurando;

    // navegação
    private Servidor? _srv;
    private ConexaoSmb? _con;
    private string? _comp;
    private string _caminho = "";

    public RedeView(Action<List<Midia>, int> tocar, Action<Pro.Recurso> mostrarPro)
    {
        _tocar = tocar; _mostrarPro = mostrarPro;
        _voltar = Ui.BotaoIcone(Icones.Voltar, S("Voltar"), Subir, "plano");
        _titulo = Ui.Linha(S("Rede"), 26, FontWeight.ExtraBold);
        _titulo.VerticalAlignment = VerticalAlignment.Center;
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(28, 18, 28, 10) };
        cab.Children.Add(_voltar);
        Grid.SetColumn(_titulo, 1); _titulo.Margin = new Thickness(4, 0);
        cab.Children.Add(_titulo);
        var add = Ui.Botao(S("Adicionar servidor"), Icones.Adicionar, "vidro", () => Formulario(null, null));
        _adicionar = add;
        Grid.SetColumn(add, 2);
        cab.Children.Add(add);
        SetDock(cab, Dock.Top);
        Children.Add(cab);
        Children.Add(new ScrollViewer { Content = _corpo });
        Servidores.Mudou += () => { if (_srv == null) Dispatcher.UIThread.Post(Montar); };
        Pro.Mudou += () => Dispatcher.UIThread.Post(Montar);
    }

    public void Entrar()
    {
        Montar();
        if (_srv == null && Pro.Ativo) Procurar();
    }

    public void Sair() { _busca?.Cancel(); }

    private void Montar()
    {
        _corpo.Children.Clear();
        _voltar.IsVisible = _srv != null;
        if (_adicionar != null) _adicionar.IsVisible = Pro.Ativo;
        if (!Pro.Ativo) { _titulo.Text = S("Rede"); _corpo.Children.Add(Bloqueado()); return; }
        if (_srv == null) { _titulo.Text = S("Rede"); Inicio(); return; }
        _titulo.Text = _comp == null ? _srv.Nome : $"{_srv.Nome} › {_comp}{(_caminho.Length > 0 ? " › " + _caminho.Replace("/", " › ") : "")}";
        Listar();
    }

    private Control Bloqueado()
    {
        var p = Ui.Pilha(14, Orientation.Vertical,
            Ui.Icone(Icones.Servidor, 56, Ui.Pincel("Ouro")),
            Ui.Texto(Pro.Nome(Pro.Recurso.Rede), 22, FontWeight.ExtraBold),
            Ui.Texto(Pro.Descricao(Pro.Recurso.Rede), 14, FontWeight.Normal, "apagado"),
            Ui.Texto(S("Funciona com Windows, Samba, NAS e roteadores com HD."), 13, FontWeight.Normal, "tenue"),
            Ui.Botao(S("Conhecer o LibertyX Pro"), Icones.Estrela, "ouro", () => _mostrarPro(Pro.Recurso.Rede)));
        p.MaxWidth = 520; p.HorizontalAlignment = HorizontalAlignment.Center; p.Margin = new Thickness(0, 50, 0, 0);
        foreach (var c in p.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var t in p.Children.OfType<TextBlock>()) t.TextAlignment = TextAlignment.Center;
        return p;
    }

    // ───────── servidores e busca ─────────

    private void Inicio()
    {
        if (Servidores.Lista.Count > 0)
        {
            _corpo.Children.Add(Ui.Texto(S("Seus servidores"), 17, FontWeight.Bold));
            var wrap = new WrapPanel();
            foreach (var s in Servidores.Lista) wrap.Children.Add(CartaoServidor(s));
            _corpo.Children.Add(wrap);
        }
        var cab = Ui.Pilha(10, Orientation.Horizontal, Ui.Texto(S("Na sua rede"), 17, FontWeight.Bold));
        if (_procurando) cab.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 80, VerticalAlignment = VerticalAlignment.Center });
        else cab.Children.Add(Ui.BotaoIcone(Icones.Atualizar, S("Procurar de novo"), Procurar, "plano", 16));
        _corpo.Children.Add(cab);
        var novos = _achados.Where(a => !Servidores.Lista.Any(s => s.Host == a.Endereco)).ToList();
        if (novos.Count == 0)
            _corpo.Children.Add(Ui.Texto(_procurando ? S("Procurando computadores com pastas compartilhadas…")
                : S("Nenhum servidor apareceu sozinho. Use \"Adicionar servidor\" e digite o endereço (ex.: 192.168.0.10)."), 14, FontWeight.Normal, "apagado"));
        var wrap2 = new WrapPanel();
        foreach (var a in novos)
        {
            var b = new Button
            {
                Classes = { "vidro" }, Margin = new Thickness(0, 0, 12, 12), Width = 250, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = Ui.Pilha(12, Orientation.Horizontal, Ui.Icone(Icones.Servidor, 22, Ui.Pincel("Apagado")),
                    Ui.Pilha(2, Orientation.Vertical, Ui.Linha(a.Nome, 14, FontWeight.Bold), Ui.Linha(a.Endereco, 12, FontWeight.Normal, "tenue"))),
            };
            b.Click += (_, _) => Formulario(null, a);
            wrap2.Children.Add(b);
        }
        _corpo.Children.Add(wrap2);
    }

    private Control CartaoServidor(Servidor s)
    {
        var b = new Button
        {
            Classes = { "vidro" }, Margin = new Thickness(0, 0, 12, 12), Width = 270, Padding = new Thickness(16, 14), HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = Ui.Pilha(14, Orientation.Horizontal, Ui.Icone(Icones.Servidor, 28, Ui.Pincel("Ouro")),
                Ui.Pilha(2, Orientation.Vertical, Ui.Linha(s.Nome, 15, FontWeight.Bold),
                    Ui.Linha(s.Host + (s.Compartilhamento is { Length: > 0 } c ? " / " + c : ""), 12, FontWeight.Normal, "tenue"))),
        };
        b.Click += (_, _) => AbrirServidor(s);
        var menu = new MenuFlyout();
        var editar = new MenuItem { Header = S("Editar") };
        editar.Click += (_, _) => Formulario(s, null);
        var remover = new MenuItem { Header = S("Remover") };
        remover.Click += (_, _) => Servidores.Remover(s);
        menu.Items.Add(editar); menu.Items.Add(remover);
        b.ContextFlyout = menu;
        return b;
    }

    private void Procurar()
    {
        _busca?.Cancel();
        _busca = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var ct = _busca.Token;
        _procurando = true;
        Montar();
        _ = Task.Run(async () =>
        {
            try
            {
                await Descoberta.Procurar(a => Dispatcher.UIThread.Post(() =>
                {
                    if (_achados.Any(x => x.Endereco == a.Endereco)) return;
                    _achados.Add(a);
                    if (_srv == null) Montar();
                }), ct);
            }
            catch { }
            Dispatcher.UIThread.Post(() => { _procurando = false; if (_srv == null) Montar(); });
        });
    }

    // ───────── formulário ─────────

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
        };
        var botoes = Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(S("Cancelar"), null, "vidro", Dialogo.Fechar), salvar);
        botoes.HorizontalAlignment = HorizontalAlignment.Right;
        Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(existente == null ? S("Adicionar servidor") : S("Editar servidor"), 20, FontWeight.ExtraBold),
            Ui.Texto(S("Um computador com pasta compartilhada (Windows ou Samba), um NAS ou o HD do roteador."), 13, FontWeight.Normal, "apagado"),
            nome, host, comp, convidado, usuario, senha, dominio, erro, botoes), 480);
    }

    // ───────── navegação ─────────

    private void AbrirServidor(Servidor s)
    {
        _busca?.Cancel();
        _con?.Dispose(); _con = null;
        _srv = s; _comp = s.Compartilhamento; _caminho = "";
        Montar();
    }

    private void Subir()
    {
        if (_srv == null) return;
        if (_caminho.Length > 0) _caminho = _caminho.Contains('/') ? _caminho[.._caminho.LastIndexOf('/')] : "";
        else if (_comp != null && _srv.Compartilhamento == null) _comp = null;
        else { _con?.Dispose(); _con = null; _srv = null; _comp = null; }
        Montar();
    }

    private void Listar()
    {
        var srv = _srv!; var comp = _comp; var caminho = _caminho;
        _corpo.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, Margin = new Thickness(0, 40) });
        Task.Run(() =>
        {
            try
            {
                _con ??= ConexaoSmb.Abrir(srv);
                if (comp == null) return (object)_con.Compartilhamentos();
                return _con.Listar(comp, caminho);
            }
            catch (Exception e)
            {
                try { _con?.Dispose(); } catch { }
                _con = null;
                return e;
            }
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (_srv != srv || _comp != comp || _caminho != caminho) return;   // a pessoa já foi para outro lugar
            _corpo.Children.Clear();
            switch (t.Result)
            {
                case Exception e:
                    _corpo.Children.Add(Ui.Texto(e is SmbErro ? e.Message : F("Não consegui conectar: {0}", e.Message), 15, FontWeight.SemiBold));
                    _corpo.Children.Add(Ui.Pilha(10, Orientation.Horizontal,
                        Ui.Botao(S("Tentar de novo"), Icones.Atualizar, "ouro", Montar),
                        Ui.Botao(S("Editar servidor"), Icones.Editar, "vidro", () => Formulario(srv, null))));
                    break;
                case List<string> shares:
                    var w = new WrapPanel();
                    foreach (var sh in shares) w.Children.Add(Pasta(sh, () => { _comp = sh; _caminho = ""; Montar(); }));
                    if (shares.Count == 0) _corpo.Children.Add(Ui.Texto(S("Este servidor não tem pastas compartilhadas visíveis."), 14, FontWeight.Normal, "apagado"));
                    _corpo.Children.Add(w);
                    break;
                case List<ItemSmb> itens:
                    MostrarItens(srv, comp!, itens);
                    break;
            }
        }));
    }

    private static Button Pasta(string nome, Action abrir)
    {
        var b = new Button
        {
            Classes = { "vidro" }, Margin = new Thickness(0, 0, 12, 12), Width = 250, Padding = new Thickness(14, 12), HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = Ui.Pilha(12, Orientation.Horizontal, Ui.Icone(Icones.Pasta, 22, Ui.Pincel("Ouro")), Ui.Linha(nome, 14, FontWeight.SemiBold)),
        };
        b.Click += (_, _) => abrir();
        return b;
    }

    private void MostrarItens(Servidor srv, string comp, List<ItemSmb> itens)
    {
        var pastas = itens.Where(i => i.Pasta).OrderBy(i => i.Nome, Natural.Comparador).ToList();
        var videos = itens.Where(i => !i.Pasta).Select(i => new Midia
        {
            Endereco = ConexaoSmb.Endereco(srv, comp, i.Caminho), Titulo = Path.GetFileNameWithoutExtension(i.Nome), Origem = Origem.Smb,
            Tamanho = i.Tamanho, Data = i.Data, Pasta = srv.Nome,
        });
        var lista = Biblioteca.Ordenar(videos, Prefs.Atual.Ordem).ToList();
        if (pastas.Count == 0 && lista.Count == 0) { _corpo.Children.Add(Ui.Texto(S("Nenhum vídeo nesta pasta."), 14, FontWeight.Normal, "apagado")); return; }
        if (pastas.Count > 0)
        {
            var w = new WrapPanel();
            foreach (var p in pastas) w.Children.Add(Pasta(p.Nome, () => { _caminho = p.Caminho; Montar(); }));
            _corpo.Children.Add(w);
        }
        if (lista.Count > 0)
        {
            var w = new WrapPanel();
            for (int i = 0; i < lista.Count; i++)
            {
                var m = lista[i]; int idx = i;
                var capa = Ui.Capa(250, 141, out var img);
                Prefs.Atual.Posicoes.TryGetValue(m.Endereco, out var r);
                ((Grid)capa.Child!).Children.Add(Ui.Progresso(r?.Fracao ?? 0));
                BibliotecaView.CarregarQuandoVisivel(capa, m, img);
                var b = new Button
                {
                    Classes = { "cartao" }, Width = 262, Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 14),
                    Content = Ui.Pilha(4, Orientation.Vertical, capa, Ui.Linha(m.Titulo, 13, FontWeight.SemiBold),
                        Ui.Linha($"{Tamanho(m.Tamanho)} · {Data(m.Data)}", 12, FontWeight.Normal, "tenue")),
                };
                ToolTip.SetTip(b, m.Titulo);
                b.Click += (_, _) => _tocar(lista, idx);
                w.Children.Add(b);
            }
            _corpo.Children.Add(w);
        }
    }
}
