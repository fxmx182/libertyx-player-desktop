using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Mpv;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>
/// O Início: só o que está no computador — as pastas com vídeo (capa = o vídeo mais novo) e, dentro
/// de cada uma, os vídeos em grade ou em lista. Igual ao celular, só que varrendo o disco; os
/// servidores ficam na barra de abas (Ajustes › Barra de abas) e na aba Arquivos.
/// </summary>
public sealed class BibliotecaView : DockPanel
{
    private readonly Action<List<Midia>, int> _tocar;
    private readonly Action _adicionarPasta, _abrirArquivo;
    private readonly Action<TipoDeConta?> _adicionarServidor;
    private readonly StackPanel _corpo = new() { Spacing = 22, Margin = new Thickness(28, 6, 28, 28) };
    private readonly TextBlock _titulo, _contagem;
    private readonly Button _voltar;
    private readonly ScrollViewer _rolagem;
    private List<PastaDeVideos> _pastas = [];
    private PastaDeVideos? _aberta;
    private bool _varrendo;
    private CancellationTokenSource? _cts;

    /// <summary>A última varredura — a aba Arquivos e a busca leem daqui.</summary>
    public List<PastaDeVideos> Pastas => _pastas;
    public event Action? Varreu;

    public BibliotecaView(Action<List<Midia>, int> tocar, Action adicionarPasta, Action abrirArquivo,
        Action<TipoDeConta?> adicionarServidor)
    {
        _tocar = tocar; _adicionarPasta = adicionarPasta; _abrirArquivo = abrirArquivo;
        _adicionarServidor = adicionarServidor;

        _voltar = Ui.BotaoIcone(Icones.Voltar, S("Voltar"), Raiz, "plano");
        _voltar.IsVisible = false;
        _contagem = Ui.Linha("", 13, FontWeight.Medium, "apagado");
        _contagem.VerticalAlignment = VerticalAlignment.Center;

        var ordem = Ui.BotaoIcone(Icones.Ordenar, S("Ordenar"), () => { }, "plano");
        var menuOrdem = new MenuFlyout();
        foreach (var (k, n) in new[] { ("nome", S("Por nome")), ("data", S("Mais novos primeiro")), ("tamanho", S("Maiores primeiro")) })
        {
            var mi = new MenuItem { Header = n };
            mi.Click += (_, _) => { Prefs.Atual.Ordem = k; Prefs.Atual.Salvar(); Montar(); };
            menuOrdem.Items.Add(mi);
        }
        ordem.Flyout = menuOrdem;
        var modo = Ui.BotaoIcone(Prefs.Atual.Lista ? Icones.Grade : Icones.Lista, S("Grade ou lista"), () => { }, "plano");
        modo.Click += (_, _) =>
        {
            Prefs.Atual.Lista = !Prefs.Atual.Lista; Prefs.Atual.Salvar();
            ((PathIcon)modo.Content!).Data = StreamGeometry.Parse(Prefs.Atual.Lista ? Icones.Grade : Icones.Lista);
            Montar();
        };
        // o + do celular: pasta do computador ou servidor de qualquer tipo
        var mais = Ui.BotaoIcone(Icones.Adicionar, S("Adicionar"), () => { }, "plano");
        var menuMais = new MenuFlyout();
        void Opcao(string nome, Action a) { var mi = new MenuItem { Header = nome }; mi.Click += (_, _) => a(); menuMais.Items.Add(mi); }
        Opcao(S("Pasta do computador"), adicionarPasta);
        Opcao(S("Servidor SMB (Windows, Samba, NAS)"), () => adicionarServidor(null));
        Opcao(S("WebDAV / Nextcloud"), () => adicionarServidor(TipoDeConta.WebDav));
        Opcao("Jellyfin", () => adicionarServidor(TipoDeConta.Jellyfin));
        Opcao("Emby", () => adicionarServidor(TipoDeConta.Emby));
        mais.Flyout = menuMais;

        _titulo = Ui.Linha(S("Início"), 26, FontWeight.ExtraBold);
        var cabecalho = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(28, 18, 28, 10) };
        cabecalho.Children.Add(_voltar);
        Grid.SetColumn(_titulo, 1); _titulo.Margin = new Thickness(4, 0, 12, 0);
        cabecalho.Children.Add(_titulo);
        Grid.SetColumn(_contagem, 2);
        cabecalho.Children.Add(_contagem);
        var acoes = Ui.Pilha(4, Orientation.Horizontal, ordem, modo,
            Ui.BotaoIcone(Icones.Atualizar, S("Procurar de novo"), () => Varrer(), "plano"), mais);
        Grid.SetColumn(acoes, 4);
        cabecalho.Children.Add(acoes);
        SetDock(cabecalho, Dock.Top);
        Children.Add(cabecalho);
        _rolagem = new ScrollViewer { Content = _corpo };
        Children.Add(_rolagem);
    }

    /// <summary>O botão da dock: volta ao começo da aba, como no celular.</summary>
    public void Raiz()
    {
        _aberta = null;
        Montar();
        _rolagem.Offset = default;
    }

    public void Varrer()
    {
        if (_varrendo) return;
        _varrendo = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        if (_pastas.Count == 0 && _aberta == null) Montar();
        Task.Run(() => Biblioteca.Varrer(ct), ct).ContinueWith(t =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _varrendo = false;
                if (t.IsCompletedSuccessfully) _pastas = t.Result;
                if (_aberta != null) _aberta = _pastas.FirstOrDefault(p => p.Caminho == _aberta.Caminho);
                Montar();
                Varreu?.Invoke();
            });
        });
    }

    /// <summary>Redesenha com o que já se sabe (depois de voltar do player, para o histórico e o progresso).</summary>
    public void Montar()
    {
        _corpo.Children.Clear();
        _voltar.IsVisible = _aberta != null;
        int total = _pastas.Sum(p => p.Videos.Count);

        if (_aberta != null)
        {
            var lista = Biblioteca.Ordenar(_aberta.Videos, Prefs.Atual.Ordem).ToList();
            _titulo.Text = _aberta.Nome;
            _contagem.Text = P(lista.Count, "{0} vídeo", "{0} vídeos");
            _corpo.Children.Add(Cartoes.Videos(lista, _tocar, MenuVideo));
            return;
        }

        _titulo.Text = S("Início");
        _contagem.Text = total > 0 ? P(total, "{0} vídeo", "{0} vídeos") : "";
        if (_pastas.Count == 0)
        {
            if (_varrendo) _corpo.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, Margin = new Thickness(0, 40) });
            else _corpo.Children.Add(Vazio());
            return;
        }
        _corpo.Children.Add(Ui.Texto(S("Local"), 17, FontWeight.Bold));
        // o botão grade/lista vale também para as pastas da tela inicial
        Panel painel = Prefs.Atual.Lista ? new StackPanel { Spacing = 2 } : new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var p in _pastas) painel.Children.Add(Prefs.Atual.Lista ? LinhaPasta(p) : CartaoPasta(p));
        _corpo.Children.Add(painel);
    }

    // ───────── pastas ─────────

    private Control CartaoPasta(PastaDeVideos p)
    {
        var capa = Ui.Capa(250, 141, out var img);
        var g = (Grid)capa.Child!;
        var n = Ui.Selo(p.Videos.Count.ToString(Textos.Cultura));
        n.HorizontalAlignment = HorizontalAlignment.Right; n.VerticalAlignment = VerticalAlignment.Bottom; n.Margin = new Thickness(0, 0, 6, 6);
        g.Children.Add(n);
        var maisNovo = p.Videos.OrderByDescending(v => v.Data).First();
        Cartoes.CarregarQuandoVisivel(capa, maisNovo, img);
        var b = new Button
        {
            Classes = { "cartao" }, Width = 262, Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 14),
            Content = Ui.Pilha(4, Orientation.Vertical, capa,
                Ui.Pilha(6, Orientation.Horizontal, Ui.Icone(Icones.Pasta, 15, Ui.Pincel("Ouro")), Ui.Linha(p.Nome, 14, FontWeight.Bold)),
                Ui.Linha(Onde(p), 12, FontWeight.Normal, "tenue")),
        };
        ToolTip.SetTip(b, p.Caminho);
        b.Click += (_, _) => Abrir(p);
        b.ContextFlyout = MenuPasta(p);
        return b;
    }

    private Control LinhaPasta(PastaDeVideos p)
    {
        var capa = Ui.Capa(128, 72, out var img);
        Cartoes.CarregarQuandoVisivel(capa, p.Videos.OrderByDescending(v => v.Data).First(), img);
        var textos = Ui.Pilha(4, Orientation.Vertical,
            Ui.Pilha(6, Orientation.Horizontal, Ui.Icone(Icones.Pasta, 15, Ui.Pincel("Ouro")), Ui.Linha(p.Nome, 14, FontWeight.SemiBold)),
            Ui.Linha($"{P(p.Videos.Count, "{0} vídeo", "{0} vídeos")} · {Onde(p)}", 12, FontWeight.Normal, "tenue"));
        textos.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(capa);
        Grid.SetColumn(textos, 1); textos.Margin = new Thickness(14, 0);
        g.Children.Add(textos);
        var b = new Button { Classes = { "cartao" }, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Stretch, Content = g };
        ToolTip.SetTip(b, p.Caminho);
        b.Click += (_, _) => Abrir(p);
        b.ContextFlyout = MenuPasta(p);
        return b;
    }

    public static string Onde(PastaDeVideos p) => p.Caminho.StartsWith(Caminhos.Home) ? "~" + p.Caminho[Caminhos.Home.Length..] : p.Caminho;

    private void Abrir(PastaDeVideos p) { _aberta = p; Montar(); _rolagem.Offset = default; }

    public MenuFlyout MenuPasta(PastaDeVideos p)
    {
        var menu = new MenuFlyout();
        var tocar = new MenuItem { Header = S("Tocar tudo") };
        tocar.Click += (_, _) => _tocar(Biblioteca.Ordenar(p.Videos, "nome").ToList(), 0);
        var abrir = new MenuItem { Header = S("Abrir no gerenciador de arquivos") };
        abrir.Click += (_, _) => Ui.Abrir(p.Caminho);
        var ocultar = new MenuItem { Header = S("Ocultar esta pasta") };
        ocultar.Click += (_, _) =>
        {
            Prefs.Atual.PastasOcultas.Add(p.Caminho); Prefs.Atual.Salvar();
            _pastas.Remove(p); Montar(); Varreu?.Invoke();
        };
        menu.Items.Add(tocar); menu.Items.Add(abrir); menu.Items.Add(ocultar);
        return menu;
    }

    public MenuFlyout? MenuVideo(Midia m)
    {
        var menu = new MenuFlyout();
        if (m.Origem == Origem.Arquivo)
        {
            var pasta = new MenuItem { Header = S("Abrir a pasta") };
            pasta.Click += (_, _) => Ui.Abrir(Path.GetDirectoryName(m.Endereco)!);
            menu.Items.Add(pasta);
        }
        if (Prefs.Atual.Posicoes.ContainsKey(m.Endereco))
        {
            var zerar = new MenuItem { Header = S("Marcar como não visto") };
            zerar.Click += (_, _) => { Prefs.Atual.Posicoes.Remove(m.Endereco); Prefs.Atual.Salvar(); Montar(); };
            menu.Items.Add(zerar);
        }
        return menu.Items.Count > 0 ? menu : null;
    }

    // ───────── vazio ─────────

    private Control Vazio()
    {
        var demo = Ui.Botao(S("Ver a demonstração"), Icones.Play, "vidro", () => _tocar([Demonstracao.Midia], 0));
        var p = Ui.Pilha(16, Orientation.Vertical,
            Ui.Marca(96),
            Ui.Texto(S("Nenhum vídeo por aqui ainda"), 22, FontWeight.ExtraBold),
            Ui.Texto(S("O LibertyX procura em Vídeos, em Downloads e nos pendrives e HDs conectados. Acrescente outras pastas, abra um arquivo ou veja a demonstração."), 14, FontWeight.Normal, "apagado"),
            Ui.Pilha(10, Orientation.Horizontal,
                Ui.Botao(S("Acrescentar pasta"), Icones.Adicionar, "ouro", _adicionarPasta),
                Ui.Botao(S("Abrir arquivo"), Icones.Arquivo, "vidro", _abrirArquivo), demo),
            Ui.Texto(Demonstracao.Credito, 11, FontWeight.Normal, "tenue"));
        p.HorizontalAlignment = HorizontalAlignment.Center;
        p.MaxWidth = 560;
        p.Margin = new Thickness(0, 30, 0, 0);
        foreach (var c in p.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var t in p.Children.OfType<TextBlock>()) t.TextAlignment = TextAlignment.Center;
        return p;
    }
}

/// <summary>
/// Um filme para quem ainda não tem nenhum (e para o revisor de loja): Sintel, filme aberto da
/// Blender (CC-BY), em MKV com legendas em vários idiomas — toca direto do servidor deles.
/// </summary>
public static class Demonstracao
{
    public static readonly Midia Midia = new()
    {
        Endereco = "https://download.blender.org/demo/movies/Sintel.2010.1080p.mkv", Titulo = "Sintel", Origem = Origem.Url,
    };
    public const string Credito = "Sintel © Blender Foundation · CC-BY";
}
