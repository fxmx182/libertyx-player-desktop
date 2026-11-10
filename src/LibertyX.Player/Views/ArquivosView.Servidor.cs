using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;
using Item = LibertyX.Player.Rede.ServidorDeMidia.Item;

namespace LibertyX.Player.Views;

/// <summary>
/// O lado Jellyfin e Emby da aba Arquivos — o mesmo cliente do celular e da TV: a tela inicial do
/// servidor (continuar, a seguir, TV ao vivo, favoritos, recentes), as bibliotecas com a ordem e os
/// filtros dele, e as páginas do filme ou episódio, da série e da pessoa, com fundo, logotipo,
/// faixas, sinopse, elenco e extras. Nada disto é do app: textos, fotos e logotipos vêm dos
/// provedores de metadados do servidor, e as faixas são a leitura que ele fez do arquivo.
/// </summary>
public sealed partial class ArquivosView
{
    /// <summary>O áudio e a legenda escolhidos na página antes de tocar. Legenda: null é a regra de sempre (só a forçada), −1 desligada.</summary>
    private sealed class EscolhasDaPagina
    {
        public int? Audio { get; set; }
        public int? Legenda { get; set; }
    }

    /// <summary>A ordem e os filtros de cada biblioteca, enquanto o app está aberto.</summary>
    private readonly Dictionary<string, ServidorDeMidia.Filtro> _filtros = [];
    private readonly Dictionary<string, List<string>> _generos = [];

    /// <summary>Um item vindo de outra aba (a busca): a página dele, dentro do servidor.</summary>
    public void AbrirItem(Conta c, Item i)
    {
        _busca?.Cancel();
        _pilha.Clear();
        _piso = 0;
        _pilha.Add(new InicioDoServidor(c));
        _rolagem.Offset = default;
        Abrir(c, i);
    }

    /// <summary>Vídeo abre a página dele (é de lá que se toca); série, a página da série; o resto, a pasta.</summary>
    private void Abrir(Conta c, Item i)
    {
        if (i.EhVideo) Ir(new PaginaDoVideo(c, i));
        else if (i.Tipo == "Series") Ir(new PaginaDaSerie(c, i.Id, i.Nome));
        else Ir(new NoServidor(c, i));
    }

    // ───────── a tela inicial do servidor ─────────

    private sealed record Tela(List<Item> Continuar, List<Item> ASeguir, List<ServidorDeMidia.Canal> Canais, List<Item> Favoritos,
        List<(Item biblioteca, List<Item> itens)> Recentes, List<Item> Bibliotecas);

    /// <summary>
    /// A primeira tela do servidor, como no app dele: o que ficou pela metade, o próximo episódio de
    /// cada série, os canais ao vivo, os favoritos e o que chegou por último em cada biblioteca; as
    /// bibliotecas inteiras embaixo. De novo a cada volta do player: o que acabou de ser visto sai do
    /// "Continuar".
    /// </summary>
    private void MostrarInicioDoServidor(Conta conta)
    {
        var s = new ServidorDeMidia(conta);
        Carregar(async () =>
        {
            // os avisos ao servidor ainda a caminho: sem esperar, o "Continuar" viria sem o filme de agora
            await AvisoAoServidor.EmDia();
            var bibliotecas = await s.Bibliotecas();
            var continuar = Tentar(s.Continuar());
            var aSeguir = Tentar(s.ASeguir());
            var favoritos = Tentar(s.Favoritos());
            var canais = Task.Run(async () => { try { return await s.Canais(30); } catch { return []; } });
            // coleções só repetem filmes que já aparecem na biblioteca deles
            var recentes = bibliotecas.Where(b => b.Colecao != "boxsets").Select(async b => (b, await Tentar(s.Recentes(b)))).ToList();
            var c = await continuar;
            var comecados = c.Select(i => i.Id).ToHashSet();
            return new Tela(c, (await aSeguir).Where(i => !comecados.Contains(i.Id)).ToList(), await canais, await favoritos,
                (await Task.WhenAll(recentes)).Where(r => r.Item2.Count > 0).ToList(), bibliotecas);
        }, t =>
        {
            if (t.Continuar.Count > 0) _corpo.Children.Add(Cartoes.Faixa(S("Continuar assistindo"), t.Continuar.Select(i => CartaoDoServidor(s, i))));
            // o "A seguir" mostra a capa da série, e não a cena do episódio, como no app do Jellyfin
            if (t.ASeguir.Count > 0) _corpo.Children.Add(Cartoes.Faixa(S("A seguir"), t.ASeguir.Select(i => CartaoDoServidor(s, i, capaDaSerie: true))));
            if (t.Canais.Count > 0)
                _corpo.Children.Add(Cartoes.Faixa(S("TV ao vivo"), t.Canais.Select(c => CartaoDeCanal(s, c)),
                    Ui.BotaoIcone(Icones.Seta, S("Todos os canais"), () => Ir(new CanaisAoVivo(conta)), "plano", 18)));
            if (t.Favoritos.Count > 0) _corpo.Children.Add(Cartoes.Faixa(S("Favoritos"), t.Favoritos.Select(i => Cartaz(s, i))));
            foreach (var (b, itens) in t.Recentes)
                _corpo.Children.Add(Cartoes.Faixa(F("Recentes em {0}", b.Nome), itens.Select(i => CartaoDoServidor(s, i)),
                    Ui.BotaoIcone(Icones.Seta, S("Ver tudo"), () => Ir(new NoServidor(conta, b)), "plano", 18)));
            if (t.Bibliotecas.Count == 0) { _corpo.Children.Add(Ui.Texto(S("Nenhum vídeo nesta pasta."), 14, FontWeight.Normal, "apagado")); return; }
            _corpo.Children.Add(Ui.Texto(S("Bibliotecas"), 17, FontWeight.Bold));
            var w = new WrapPanel();
            foreach (var b in t.Bibliotecas) { var c = CartaoDoServidor(s, b); c.Margin = new Thickness(0, 0, 14, 14); w.Children.Add(c); }
            _corpo.Children.Add(w);
        }, conta);
    }

    private static async Task<List<Item>> Tentar(Task<List<Item>> t) { try { return await t; } catch { return []; } }

    // ───────── bibliotecas e pastas ─────────

    private void ListarServidor(NoServidor n)
    {
        var s = new ServidorDeMidia(n.Conta);
        // bibliotecas de filmes e séries: a ordem e os filtros do servidor, como no app dele
        bool comFiltro = n.Item.Colecao is "movies" or "tvshows";
        var filtro = _filtros.GetValueOrDefault(n.Item.Id) ?? new ServidorDeMidia.Filtro();
        if (comFiltro) _corpo.Children.Add(BarraDeFiltro(s, n.Item, filtro));
        int ger = _geracao;
        var lista = new StackPanel { Spacing = 18 };
        _corpo.Children.Add(lista);
        lista.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, Margin = new Thickness(0, 40) });
        Task.Run(() => comFiltro ? s.Itens(n.Item, filtro) : s.Itens(n.Item)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (ger != _geracao) return;
            lista.Children.Clear();
            if (!t.IsCompletedSuccessfully)
            {
                var e = t.Exception?.InnerException ?? t.Exception;
                _corpo.Children.Clear();
                Erro(e is ErroDeServidor ? e.Message : F("Não consegui conectar: {0}", e?.Message ?? ""), () => FormularioConta(n.Conta.Tipo, n.Conta));
                return;
            }
            var itens = t.Result;
            var pastas = itens.Where(i => !i.EhVideo).ToList();
            var videos = itens.Where(i => i.EhVideo).ToList();
            if (pastas.Count == 0 && videos.Count == 0)
            {
                lista.Children.Add(Ui.Texto(filtro.Ativo ? S("Nada com estes filtros.") : S("Nenhum vídeo nesta pasta."), 14, FontWeight.Normal, "apagado"));
                return;
            }
            if (pastas.Count > 0)
            {
                var w = new WrapPanel();
                foreach (var p in pastas) { var c = CartaoDoServidor(s, p); c.Margin = new Thickness(0, 0, 14, 14); w.Children.Add(c); }
                lista.Children.Add(w);
            }
            if (videos.Count == 0) return;
            // a ordem é a do servidor (temporada, episódio); o vídeo abre a página dele, de onde se toca
            var midias = videos.Select(s.ParaTocar).ToList();
            lista.Children.Add(Cartoes.Videos(midias, (_, k) => Abrir(n.Conta, videos[k]), m => midias.IndexOf(m) is >= 0 and var k ? MenuDoVisto(s, videos[k]) : null));
        }));
    }

    /// <summary>Ordem, crescente ou decrescente, não vistos, favoritos e gênero — quem filtra é o servidor.</summary>
    private Control BarraDeFiltro(ServidorDeMidia s, Item biblioteca, ServidorDeMidia.Filtro filtro)
    {
        void Mudar(ServidorDeMidia.Filtro novo) { _filtros[biblioteca.Id] = novo; Montar(); }
        var ordem = new ComboBox { MinWidth = 170, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (_, nome) in ServidorDeMidia.Filtro.Ordens) ordem.Items.Add(nome);
        ordem.SelectedIndex = Math.Max(0, Array.FindIndex(ServidorDeMidia.Filtro.Ordens, o => o.campo == filtro.Ordem));
        ordem.SelectionChanged += (_, _) => { if (ordem.SelectedIndex >= 0) Mudar(filtro with { Ordem = ServidorDeMidia.Filtro.Ordens[ordem.SelectedIndex].campo }); };
        var sentido = Ui.BotaoIcone(Icones.Ordenar, filtro.Decrescente ? S("Decrescente") : S("Crescente"), () => Mudar(filtro with { Decrescente = !filtro.Decrescente }), "vidro", 18);
        sentido.RenderTransform = filtro.Decrescente ? null : new ScaleTransform(1, -1);
        ToggleButton Alternar(string nome, bool ligado, Func<bool, ServidorDeMidia.Filtro> novo)
        {
            var b = new ToggleButton { Content = nome, IsChecked = ligado, Classes = { "filtro" }, VerticalAlignment = VerticalAlignment.Center };
            b.IsCheckedChanged += (_, _) => Mudar(novo(b.IsChecked == true));
            return b;
        }
        var genero = new ComboBox { MinWidth = 170, VerticalAlignment = VerticalAlignment.Center };
        genero.Items.Add(S("Todos os gêneros"));
        void PorGeneros(List<string> l)
        {
            foreach (var g in l) genero.Items.Add(g);
            genero.SelectedIndex = filtro.Genero is { } atual && l.IndexOf(atual) is >= 0 and var k ? k + 1 : 0;
            genero.SelectionChanged += (_, _) => Mudar(filtro with { Genero = genero.SelectedIndex > 0 ? genero.SelectedItem as string : null });
        }
        if (_generos.TryGetValue(biblioteca.Id, out var conhecidos)) PorGeneros(conhecidos);
        else
        {
            genero.SelectedIndex = 0;
            Task.Run(async () => { try { return await s.Generos(biblioteca); } catch { return []; } })
                .ContinueWith(t => Dispatcher.UIThread.Post(() => { _generos[biblioteca.Id] = t.Result; PorGeneros(t.Result); }));
        }
        // quebra a linha na janela estreita
        var barra = new WrapPanel();
        barra.Children.AddRange([ordem, sentido, Alternar(S("Não vistos"), filtro.NaoVistos, v => filtro with { NaoVistos = v }),
            Alternar(S("Favoritos"), filtro.Favoritos, v => filtro with { Favoritos = v }), genero]);
        if (filtro.Ativo) barra.Children.Add(Ui.Botao(S("Limpar filtros"), null, "plano", () => Mudar(new ServidorDeMidia.Filtro())));
        foreach (var c in barra.Children) c.Margin = new Thickness(0, 0, 10, 8);
        return barra;
    }

    // ───────── cartões ─────────

    /// <summary>Num episódio solto, a capa da série; numa temporada, a da série; o resto, a dele.</summary>
    private static string IdDaCapa(Item i, bool capaDaSerie) =>
        (capaDaSerie && i.Tipo == "Episode") || i.Tipo == "Season" ? i.SerieId ?? i.Id : i.Id;

    /// <summary>
    /// Uma capa deitada do servidor: o vídeo abre a página dele, série e temporada abrem a delas. A
    /// barrinha é o progresso combinado — o do app e o do servidor; o selo, o episódio, os por ver ou o ✓.
    /// </summary>
    private Button CartaoDoServidor(ServidorDeMidia s, Item i, bool capaDaSerie = false, bool atual = false)
    {
        var capa = Ui.Capa(250, 141, out var img);
        var g = (Grid)capa.Child!;
        Midia? m = i.EhVideo ? s.ParaTocar(i) : null;
        double frac = 0;
        if (m != null)
        {
            frac = Prefs.Atual.Posicoes.TryGetValue(m.Endereco, out var r) ? r.Fracao : i.Progresso ?? 0;
            if (capaDaSerie) Cartoes.ImagemDoServidor(s, IdDaCapa(i, true), img);
            else Cartoes.CarregarQuandoVisivel(capa, m, img);
        }
        else Cartoes.ImagemDoServidor(s, IdDaCapa(i, false), img);
        g.Children.Add(Ui.Progresso(frac));
        if (i.Visto && i.NaoVistos == null && frac <= 0.01) g.Children.Add(Cartoes.MarcaDeVisto());
        string? selo = i.Episodio ?? (i.NaoVistos is { } nv ? nv.ToString(Textos.Cultura) : null);
        if (selo != null)
        {
            var b = Ui.Selo(selo);
            b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 6, 10);
            g.Children.Add(b);
        }
        if (atual) capa.BorderBrush = Ui.Pincel("Ouro");
        if (atual) capa.BorderThickness = new Thickness(2);
        // episódio: a série em cima, o episódio embaixo — é como se reconhece numa faixa
        string titulo = i.Tipo == "Episode" && i.Serie != null ? i.Serie : i.Titulo;
        string sub = i.Tipo == "Episode" && i.Serie != null ? i.Titulo
            : i.Duracao is { } d ? Tempo(d) : i.Ano?.ToString(Textos.Cultura) ?? "";
        var botao = new Button
        {
            Classes = { "cartao" }, Width = 262, Padding = new Thickness(6),
            Content = Ui.Pilha(4, Orientation.Vertical, capa, Ui.Linha(titulo, 13, FontWeight.SemiBold), Ui.Linha(sub, 12, FontWeight.Normal, "tenue")),
        };
        ToolTip.SetTip(botao, i.Titulo);
        botao.ContextFlyout = MenuDoVisto(s, i);
        botao.Click += (_, _) => { if (!atual) Abrir(s.Conta, i); };
        return botao;
    }

    /// <summary>O cartaz em pé (favoritos, os títulos de uma pessoa), com os episódios por ver ou o ✓.</summary>
    private Button Cartaz(ServidorDeMidia s, Item i)
    {
        var img = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill };
        var g = new Grid();
        g.Children.Add(new PathIcon { Data = StreamGeometry.Parse(i.Tipo == "Series" ? Icones.Tv : Icones.Filme), Width = 30, Height = 30, Foreground = Ui.Pincel("Tenue") });
        g.Children.Add(img);
        // episódio solto mostra o cartaz da série: a cena dele, cortada em pé, não diria de onde ele é
        Cartoes.ImagemDoServidor(s, i.Tipo == "Episode" ? i.SerieId ?? i.Id : i.Id, img, ["Primary"], 300);
        if (i.NaoVistos is { } nv)
        {
            var selo = Ui.Selo(nv.ToString(Textos.Cultura), Ui.Pincel("Ouro"), Ui.Pincel("SobreOuro"));
            selo.HorizontalAlignment = HorizontalAlignment.Right; selo.VerticalAlignment = VerticalAlignment.Top; selo.Margin = new Thickness(0, 6, 6, 0);
            g.Children.Add(selo);
        }
        else if (i.Visto) g.Children.Add(Cartoes.MarcaDeVisto(20));
        g.Children.Add(Ui.Progresso(i.Progresso ?? 0));
        var capa = new Border { Classes = { "capa" }, Width = 150, Height = 225, Child = g };
        var b = new Button
        {
            Classes = { "cartao" }, Width = 162, Padding = new Thickness(6),
            Content = Ui.Pilha(4, Orientation.Vertical, capa,
                Ui.Linha(i.Tipo == "Episode" ? i.Serie ?? i.Nome : i.Nome, 13, FontWeight.SemiBold),
                Ui.Linha(i.Ano?.ToString(Textos.Cultura) ?? "", 12, FontWeight.Normal, "tenue")),
        };
        ToolTip.SetTip(b, i.Titulo);
        b.ContextFlyout = MenuDoVisto(s, i);
        b.Click += (_, _) => Abrir(s.Conta, i);
        return b;
    }

    /// <summary>O logotipo do canal inteiro sobre o fundo escuro — logotipo de emissora é largo e não pode ser cortado.</summary>
    private static Grid LogoDoCanal(ServidorDeMidia s, ServidorDeMidia.Canal c)
    {
        var g = new Grid
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#232228"), 0), new GradientStop(Color.Parse("#16161A"), 1) },
            },
        };
        g.Children.Add(Ui.Linha(c.Numero ?? (c.Nome.Length > 3 ? c.Nome[..3] : c.Nome), 18, FontWeight.ExtraBold, "tenue"));
        ((TextBlock)g.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)g.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        var img = new Avalonia.Controls.Image { Stretch = Stretch.Uniform, Margin = new Thickness(14) };
        Cartoes.ImagemDoServidor(s, c.Id, img, ["Primary"], 320, () => g.Children[0].IsVisible = false);
        g.Children.Add(img);
        return g;
    }

    /// <summary>"AO VIVO" em vermelho, como em toda TV.</summary>
    private static Border SeloAoVivo() => new()
    {
        Background = Ui.Pincel("Vermelho"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6),
        Child = new TextBlock { Text = S("AO VIVO"), FontSize = 10, FontWeight = FontWeight.ExtraBold, Foreground = Brushes.White },
    };

    /// <summary>O cartão da faixa do início do servidor: o logotipo, o canal e o que está passando.</summary>
    private Button CartaoDeCanal(ServidorDeMidia s, ServidorDeMidia.Canal c)
    {
        var g = LogoDoCanal(s, c);
        g.Children.Add(SeloAoVivo());
        g.Children.Add(Ui.Progresso(c.Programa?.Andamento ?? 0));
        var capa = new Border { Classes = { "capa" }, Width = 208, Height = 117, Child = g };
        var b = new Button
        {
            Classes = { "cartao" }, Width = 220, Padding = new Thickness(6),
            Content = Ui.Pilha(4, Orientation.Vertical, capa,
                Ui.Linha(string.Join(" · ", new[] { c.Numero, c.Nome }.Where(x => !string.IsNullOrEmpty(x))), 13, FontWeight.SemiBold),
                Ui.Linha(c.Programa?.Nome ?? " ", 12, FontWeight.Normal, "tenue")),
        };
        b.Click += (_, _) => TocarCanal(s, c, b);
        return b;
    }

    /// <summary>
    /// Abre o canal e começa a tocar. O servidor abre a transmissão antes (ver
    /// <see cref="ServidorDeMidia.AbrirCanal"/>); o endereço fica em <see cref="Conversoes"/>, onde o
    /// player o procura. Ao vivo não tem "onde parou": entra sempre no agora.
    /// </summary>
    private async void TocarCanal(ServidorDeMidia s, ServidorDeMidia.Canal c, Control botao)
    {
        if (!botao.IsEnabled) return;
        botao.IsEnabled = false;
        try
        {
            var (url, sessao, transmissao) = await Task.Run(() => s.AbrirCanal(c.Id));
            Conversoes.RegistrarCanal(c.Id, url, sessao, transmissao);
            var m = new Midia { Endereco = Contas.EnderecoMidia(s.Conta, c.Id), Titulo = c.Nome, Origem = Origem.Servidor, Pasta = s.Conta.Nome, AoVivo = true };
            EscolhaDeFaixas.PedirInicio(EscolhaDeFaixas.Inicio.DoComeco, m.Endereco);
            _tocar([m], 0);
        }
        catch (Exception e) { MostrarErro(c.Nome, e); }
        finally { botao.IsEnabled = true; }
    }

    // ───────── marcar ─────────

    /// <summary>
    /// Botão direito num filme, série, temporada ou episódio: a caixinha "Já reproduzido", como no
    /// app do servidor e no LibertyX do celular. A tela recarrega quando o servidor confirma.
    /// </summary>
    private MenuFlyout? MenuDoVisto(ServidorDeMidia s, Item i)
    {
        if (!i.EhVideo && i.Tipo is not ("Series" or "Season" or "BoxSet")) return null;
        // a caixinha à mostra, marcada ou não, como no celular (o ToggleType só desenha o ✓)
        var visto = new MenuItem { Header = S("Já reproduzido"), Icon = Caixinha(i.Visto) };
        visto.Click += (_, _) => MarcarVisto(s, i, !i.Visto);
        var favorito = new MenuItem { Header = S("Favorito"), Icon = Caixinha(i.Favorito) };
        favorito.Click += (_, _) => Marcar(() => s.MarcarFavorito(i.Id, !i.Favorito), i.Titulo);
        var menu = new MenuFlyout();
        menu.Items.Add(visto);
        menu.Items.Add(favorito);
        return menu;
    }

    private void MarcarVisto(ServidorDeMidia s, Item i, bool visto) => Marcar(async () =>
    {
        await s.MarcarVisto(i.Id, visto);
        // o servidor zera onde parou nos dois sentidos; a marca daqui segue, ou a capa voltaria com a barrinha
        if (Prefs.Atual.Posicoes.Remove(Contas.EnderecoMidia(s.Conta, i.Id))) Prefs.Atual.Salvar();
    }, i.Titulo);

    /// <summary>Manda ao servidor e, quando ele confirma, refaz a tela; se ele recusar, diz por quê.</summary>
    private async void Marcar(Func<Task> acao, string titulo)
    {
        try { await acao(); }
        catch (Exception e) { MostrarErro(titulo, e); return; }
        Montar();
    }

    private static void MostrarErro(string titulo, Exception e)
    {
        Diario.Escrever($"servidor: {titulo}: {e.Message}");
        Dialogo.Mostrar(Ui.Pilha(14, Orientation.Vertical, Ui.Texto(titulo, 18, FontWeight.ExtraBold),
            Ui.Texto(e is ErroDeServidor ? e.Message : F("Não consegui conectar: {0}", e.Message), 14, FontWeight.Normal, "apagado"),
            Ui.Botao(S("Fechar"), null, "vidro", Dialogo.Fechar)));
    }

    private static Border Caixinha(bool marcada) => new()
    {
        Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(marcada ? 0 : 1.5),
        BorderBrush = Ui.Pincel("Apagado"), Background = marcada ? Ui.Pincel("Ouro") : Brushes.Transparent,
        Child = marcada ? Ui.Icone(Icones.Ok, 12, Brushes.Black) : null,
    };
}
