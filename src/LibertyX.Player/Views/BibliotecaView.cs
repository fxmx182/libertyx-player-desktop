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
/// A biblioteca: "continuar assistindo", as pastas com vídeo (capa = o vídeo mais novo) e, dentro
/// de cada uma, os vídeos em grade ou em lista. Igual ao celular, só que varrendo o disco.
/// </summary>
public sealed class BibliotecaView : DockPanel
{
    private readonly Action<List<Midia>, int> _tocar;
    private readonly Action _adicionarPasta, _abrirArquivo;
    private readonly StackPanel _corpo = new() { Spacing = 22, Margin = new Thickness(28, 6, 28, 28) };
    private readonly TextBox _busca;
    private readonly TextBlock _contagem;
    private readonly Button _voltar;
    private List<PastaDeVideos> _pastas = [];
    private PastaDeVideos? _aberta;
    private bool _varrendo;
    private CancellationTokenSource? _cts;

    public BibliotecaView(Action<List<Midia>, int> tocar, Action adicionarPasta, Action abrirArquivo)
    {
        _tocar = tocar; _adicionarPasta = adicionarPasta; _abrirArquivo = abrirArquivo;

        _voltar = Ui.BotaoIcone(Icones.Voltar, S("Todas as pastas"), () => { _aberta = null; Montar(); }, "plano");
        _voltar.IsVisible = false;
        _contagem = Ui.Linha("", 13, FontWeight.Medium, "apagado");
        _contagem.VerticalAlignment = VerticalAlignment.Center;
        _busca = new TextBox { Watermark = S("Buscar vídeos"), Width = 240, VerticalAlignment = VerticalAlignment.Center, InnerLeftContent = Ui.Icone(Icones.Buscar, 16, Ui.Pincel("Apagado")) };
        ((PathIcon)_busca.InnerLeftContent).Margin = new Thickness(10, 0, 0, 0);
        _busca.TextChanged += (_, _) => Montar();

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

        var titulo = Ui.Linha(S("Vídeos"), 26, FontWeight.ExtraBold);
        var cabecalho = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(28, 18, 28, 10) };
        cabecalho.Children.Add(_voltar);
        Grid.SetColumn(titulo, 1); titulo.Margin = new Thickness(4, 0, 12, 0);
        cabecalho.Children.Add(titulo);
        Grid.SetColumn(_contagem, 2);
        cabecalho.Children.Add(_contagem);
        var acoes = Ui.Pilha(4, Orientation.Horizontal, _busca, ordem, modo,
            Ui.BotaoIcone(Icones.Atualizar, S("Procurar de novo"), () => Varrer(), "plano"),
            Ui.BotaoIcone(Icones.Adicionar, S("Acrescentar uma pasta à biblioteca"), adicionarPasta, "plano"));
        Grid.SetColumn(acoes, 4);
        cabecalho.Children.Add(acoes);
        SetDock(cabecalho, Dock.Top);
        Children.Add(cabecalho);
        Children.Add(new ScrollViewer { Content = _corpo });
    }

    public void Varrer()
    {
        if (_varrendo) return;
        _varrendo = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        if (_pastas.Count == 0) { _corpo.Children.Clear(); _corpo.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, Margin = new Thickness(0, 60) }); }
        Task.Run(() => Biblioteca.Varrer(ct), ct).ContinueWith(t =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _varrendo = false;
                if (t.IsCompletedSuccessfully) _pastas = t.Result;
                if (_aberta != null) _aberta = _pastas.FirstOrDefault(p => p.Caminho == _aberta.Caminho);
                Montar();
            });
        });
    }

    /// <summary>Redesenha com o que já se sabe (depois de voltar do player, para o "continuar" e o progresso).</summary>
    public void Montar()
    {
        _corpo.Children.Clear();
        _voltar.IsVisible = _aberta != null;
        string q = _busca.Text?.Trim() ?? "";
        int total = _pastas.Sum(p => p.Videos.Count);

        if (q.Length > 0)
        {
            var achados = Biblioteca.Ordenar(_pastas.SelectMany(p => p.Videos).Where(v => v.Titulo.Contains(q, StringComparison.CurrentCultureIgnoreCase)), Prefs.Atual.Ordem).ToList();
            _contagem.Text = P(achados.Count, "{0} vídeo", "{0} vídeos");
            if (achados.Count == 0) _corpo.Children.Add(Ui.Texto(F("Nada com \"{0}\".", q), 15, FontWeight.Normal, "apagado"));
            else _corpo.Children.Add(Videos(achados));
            return;
        }

        if (_aberta != null)
        {
            var lista = Biblioteca.Ordenar(_aberta.Videos, Prefs.Atual.Ordem).ToList();
            _contagem.Text = $"{_aberta.Nome} · {P(lista.Count, "{0} vídeo", "{0} vídeos")}";
            _corpo.Children.Add(Videos(lista));
            return;
        }

        _contagem.Text = total > 0 ? P(total, "{0} vídeo", "{0} vídeos") : "";
        if (Continuar() is { } c) _corpo.Children.Add(c);
        if (_pastas.Count == 0 && !_varrendo) { _corpo.Children.Add(Vazio()); return; }
        if (_pastas.Count > 0)
        {
            _corpo.Children.Add(Ui.Texto(S("Pastas"), 17, FontWeight.Bold));
            // o botão grade/lista vale também para as pastas da tela inicial
            Panel painel = Prefs.Atual.Lista ? new StackPanel { Spacing = 2 } : new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var p in _pastas) painel.Children.Add(Prefs.Atual.Lista ? LinhaPasta(p) : CartaoPasta(p));
            _corpo.Children.Add(painel);
        }
    }

    // ───────── continuar assistindo ─────────

    private Control? Continuar()
    {
        var itens = new List<(Midia m, Retomada r)>();
        foreach (var (end, r) in Prefs.Atual.Posicoes.OrderByDescending(p => p.Value.Quando))
        {
            if (itens.Count >= 12) break;
            Midia? m = null;
            // arquivo do computador: /… no Linux, C:\… no Windows
            if (!end.Contains("://") && Path.IsPathFullyQualified(end)) { if (File.Exists(end)) m = Midia.DeArquivo(end); }
            else if (ConexaoSmb.Partes(end) is { } sp && Servidores.Achar(sp.id) is { } srv)
                m = new Midia { Endereco = end, Titulo = r.Titulo ?? Path.GetFileNameWithoutExtension(sp.caminho), Origem = Origem.Smb, Pasta = srv.Nome };
            else if (end.Contains("://")) m = new Midia { Endereco = end, Titulo = r.Titulo ?? Midia.DeUrl(end).Titulo, Origem = Origem.Url };
            if (m != null) itens.Add((m, r));
        }
        if (itens.Count == 0) return null;
        var faixa = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        foreach (var (m, r) in itens)
        {
            var capa = Ui.Capa(250, 141, out var img);
            var g = (Grid)capa.Child!;
            g.Children.Add(Ui.Progresso(r.Fracao));
            var resta = Ui.Selo(F("faltam {0}", Tempo(r.Duracao - r.Posicao)));
            resta.HorizontalAlignment = HorizontalAlignment.Right; resta.VerticalAlignment = VerticalAlignment.Bottom; resta.Margin = new Thickness(0, 0, 6, 10);
            g.Children.Add(resta);
            CarregarQuandoVisivel(capa, m, img);
            var b = new Button
            {
                Classes = { "cartao" }, Width = 262, Padding = new Thickness(6),
                Content = Ui.Pilha(6, Orientation.Vertical, capa, Ui.Linha(m.Titulo, 13, FontWeight.SemiBold), Ui.Linha(m.Pasta ?? "", 12, FontWeight.Normal, "tenue")),
            };
            b.Click += (_, _) => TocarComVizinhos(m);
            var menu = new MenuFlyout();
            var tirar = new MenuItem { Header = S("Tirar de \"continuar assistindo\"") };
            tirar.Click += (_, _) => { Prefs.Atual.Posicoes.Remove(m.Endereco); Prefs.Atual.Salvar(); Montar(); };
            menu.Items.Add(tirar);
            b.ContextFlyout = menu;
            faixa.Children.Add(b);
        }
        var limpar = Ui.Botao(S("Limpar"), Icones.Apagar, "plano", () =>
        {
            Prefs.Atual.Posicoes.Clear(); Prefs.Atual.Salvar(); Montar();
        }, 15);
        ToolTip.SetTip(limpar, S("Apagar o histórico de reprodução"));
        var topo = Ui.Pilha(12, Orientation.Horizontal, Ui.Texto(S("Continuar assistindo"), 17, FontWeight.Bold), limpar);
        return Ui.Pilha(10, Orientation.Vertical, topo,
            new ScrollViewer { Content = faixa, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
    }

    private void TocarComVizinhos(Midia m)
    {
        if (m.Origem == Origem.Arquivo)
        {
            var viz = _pastas.FirstOrDefault(p => p.Caminho == Path.GetDirectoryName(m.Endereco))?.Videos is { } l
                ? Biblioteca.Ordenar(l, "nome").ToList() : Biblioteca.Vizinhos(m.Endereco);
            int i = viz.FindIndex(v => v.Endereco == m.Endereco);
            if (i >= 0) { _tocar(viz, i); return; }
        }
        _tocar([m], 0);
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
        CarregarQuandoVisivel(capa, maisNovo, img);
        string onde = p.Caminho.StartsWith(Caminhos.Home) ? "~" + p.Caminho[Caminhos.Home.Length..] : p.Caminho;
        var b = new Button
        {
            Classes = { "cartao" }, Width = 262, Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 14),
            Content = Ui.Pilha(4, Orientation.Vertical, capa,
                Ui.Pilha(6, Orientation.Horizontal, Ui.Icone(Icones.Pasta, 15, Ui.Pincel("Ouro")), Ui.Linha(p.Nome, 14, FontWeight.Bold)),
                Ui.Linha(onde, 12, FontWeight.Normal, "tenue")),
        };
        ToolTip.SetTip(b, p.Caminho);
        b.Click += (_, _) => { _aberta = p; Montar(); };
        b.ContextFlyout = MenuPasta(p);
        return b;
    }

    private Control LinhaPasta(PastaDeVideos p)
    {
        var capa = Ui.Capa(128, 72, out var img);
        CarregarQuandoVisivel(capa, p.Videos.OrderByDescending(v => v.Data).First(), img);
        string onde = p.Caminho.StartsWith(Caminhos.Home) ? "~" + p.Caminho[Caminhos.Home.Length..] : p.Caminho;
        var textos = Ui.Pilha(4, Orientation.Vertical,
            Ui.Pilha(6, Orientation.Horizontal, Ui.Icone(Icones.Pasta, 15, Ui.Pincel("Ouro")), Ui.Linha(p.Nome, 14, FontWeight.SemiBold)),
            Ui.Linha($"{P(p.Videos.Count, "{0} vídeo", "{0} vídeos")} · {onde}", 12, FontWeight.Normal, "tenue"));
        textos.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(capa);
        Grid.SetColumn(textos, 1); textos.Margin = new Thickness(14, 0);
        g.Children.Add(textos);
        var b = new Button { Classes = { "cartao" }, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Stretch, Content = g };
        ToolTip.SetTip(b, p.Caminho);
        b.Click += (_, _) => { _aberta = p; Montar(); };
        b.ContextFlyout = MenuPasta(p);
        return b;
    }

    private MenuFlyout MenuPasta(PastaDeVideos p)
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
            _pastas.Remove(p); Montar();
        };
        menu.Items.Add(tocar); menu.Items.Add(abrir); menu.Items.Add(ocultar);
        return menu;
    }

    // ───────── vídeos ─────────

    private Control Videos(List<Midia> lista)
    {
        if (Prefs.Atual.Lista)
        {
            var pilha = new StackPanel { Spacing = 2 };
            for (int i = 0; i < lista.Count; i++) pilha.Children.Add(LinhaVideo(lista, i));
            return pilha;
        }
        var wrap = new WrapPanel();
        for (int i = 0; i < lista.Count; i++) wrap.Children.Add(CartaoVideo(lista, i));
        return wrap;
    }

    private Control CartaoVideo(List<Midia> lista, int i)
    {
        var m = lista[i];
        var capa = Ui.Capa(250, 141, out var img);
        var g = (Grid)capa.Child!;
        Prefs.Atual.Posicoes.TryGetValue(m.Endereco, out var r);
        g.Children.Add(Ui.Progresso(r?.Fracao ?? 0));
        var dur = Ui.Selo(Miniaturas.Duracao(m) is { } d ? Tempo(d) : "");
        dur.IsVisible = Miniaturas.Duracao(m) != null;
        dur.HorizontalAlignment = HorizontalAlignment.Right; dur.VerticalAlignment = VerticalAlignment.Bottom; dur.Margin = new Thickness(0, 0, 6, 10);
        g.Children.Add(dur);
        CarregarQuandoVisivel(capa, m, img, () => { if (Miniaturas.Duracao(m) is { } dd) { ((TextBlock)dur.Child!).Text = Tempo(dd); dur.IsVisible = true; } });
        var b = new Button
        {
            Classes = { "cartao" }, Width = 262, Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 14),
            Content = Ui.Pilha(4, Orientation.Vertical, capa, Ui.Linha(m.Titulo, 13, FontWeight.SemiBold),
                Ui.Linha($"{Tamanho(m.Tamanho)} · {Data(m.Data)}", 12, FontWeight.Normal, "tenue")),
        };
        ToolTip.SetTip(b, m.Titulo);
        b.Click += (_, _) => _tocar(lista, i);
        b.ContextFlyout = MenuVideo(m);
        return b;
    }

    private Control LinhaVideo(List<Midia> lista, int i)
    {
        var m = lista[i];
        var capa = Ui.Capa(128, 72, out var img);
        Prefs.Atual.Posicoes.TryGetValue(m.Endereco, out var r);
        ((Grid)capa.Child!).Children.Add(Ui.Progresso(r?.Fracao ?? 0));
        var info = Ui.Linha("", 12, FontWeight.Normal, "tenue");
        void Info() => info.Text = string.Join(" · ", new[] { Miniaturas.Duracao(m) is { } d ? Tempo(d) : null, Tamanho(m.Tamanho), Data(m.Data) }.Where(x => x != null));
        Info();
        CarregarQuandoVisivel(capa, m, img, Info);
        var textos = Ui.Pilha(4, Orientation.Vertical, Ui.Linha(m.Titulo, 14, FontWeight.SemiBold), info);
        textos.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(capa);
        Grid.SetColumn(textos, 1); textos.Margin = new Thickness(14, 0);
        g.Children.Add(textos);
        var b = new Button { Classes = { "cartao" }, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Stretch, Content = g };
        b.Click += (_, _) => _tocar(lista, i);
        b.ContextFlyout = MenuVideo(m);
        return b;
    }

    private MenuFlyout MenuVideo(Midia m)
    {
        var menu = new MenuFlyout();
        var pasta = new MenuItem { Header = S("Abrir a pasta") };
        pasta.Click += (_, _) => Ui.Abrir(Path.GetDirectoryName(m.Endereco)!);
        menu.Items.Add(pasta);
        if (Prefs.Atual.Posicoes.ContainsKey(m.Endereco))
        {
            var zerar = new MenuItem { Header = S("Marcar como não visto") };
            zerar.Click += (_, _) => { Prefs.Atual.Posicoes.Remove(m.Endereco); Prefs.Atual.Salvar(); Montar(); };
            menu.Items.Add(zerar);
        }
        return menu;
    }

    /// <summary>Pede a miniatura só quando o cartão aparece na tela — numa pasta com mil vídeos, não gera mil de uma vez.</summary>
    public static void CarregarQuandoVisivel(Control alvo, Midia m, Image img, Action? depois = null)
    {
        if (Miniaturas.Pronta(m) is { } pronta) { img.Source = pronta; return; }
        bool pedido = false;
        void Ver(object? s, Avalonia.Layout.EffectiveViewportChangedEventArgs e)
        {
            if (pedido || e.EffectiveViewport.Width <= 0 || !e.EffectiveViewport.Intersects(new Rect(alvo.Bounds.Size))) return;
            pedido = true;
            alvo.EffectiveViewportChanged -= Ver;
            Miniaturas.Pedir(m, bmp => { img.Source = bmp; depois?.Invoke(); });
        }
        alvo.EffectiveViewportChanged += Ver;
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
        p.Margin = new Thickness(0, 50, 0, 0);
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
