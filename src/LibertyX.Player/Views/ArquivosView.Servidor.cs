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
    /// A primeira tela do servidor: as bibliotecas no topo, em banners; o destaque grande que passa
    /// sozinho; e as faixas — o que ficou pela metade, o próximo episódio de cada série, os canais ao
    /// vivo, os favoritos e o que chegou por último em cada biblioteca. De novo a cada volta do
    /// player: o que acabou de ser visto sai do "Continuar".
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
            int ger = _geracao;
            _corpo.Spacing = 30;
            // as bibliotecas sempre no topo, como banners: é por elas que se entra no servidor
            if (t.Bibliotecas.Count > 0)
            {
                var w = new WrapPanel();
                foreach (var b in t.Bibliotecas) w.Children.Add(BannerDeBiblioteca(s, b));
                _corpo.Children.Add(Ui.Pilha(14, Orientation.Vertical, CabecalhoDeSecao(S("Bibliotecas")), w));
            }
            else _corpo.Children.Add(Ui.Texto(S("Nenhum vídeo nesta pasta."), 14, FontWeight.Normal, "apagado"));
            // o destaque: o que ficou pela metade e o que chegou por último, um de cada vez
            var candidatos = t.Continuar.Concat(t.Recentes.SelectMany(r => r.itens).Where(i => i.Tipo is "Movie" or "Series"))
                .DistinctBy(i => i.Id).Take(6).ToList();
            if (candidatos.Count > 0) _corpo.Children.Add(Destaque(s, candidatos, ger));
            if (t.Continuar.Count > 0) _corpo.Children.Add(FaixaComSetas(S("Continuar assistindo"), t.Continuar.Select(i => (Control)CartaoDoServidor(s, i))));
            // o "A seguir" mostra a capa da série, e não a cena do episódio, como no app do Jellyfin
            if (t.ASeguir.Count > 0) _corpo.Children.Add(FaixaComSetas(S("A seguir"), t.ASeguir.Select(i => (Control)CartaoDoServidor(s, i, capaDaSerie: true))));
            if (t.Canais.Count > 0)
                _corpo.Children.Add(FaixaComSetas(S("TV ao vivo"), t.Canais.Select(c => (Control)CartaoDeCanal(s, c)), () => Ir(new CanaisAoVivo(conta)), S("Todos os canais")));
            if (t.Favoritos.Count > 0) _corpo.Children.Add(FaixaComSetas(S("Favoritos"), t.Favoritos.Select(i => (Control)Cartaz(s, i))));
            // filmes e séries que chegaram: o cartaz em pé, como no Jellyfin; o resto (vídeos caseiros), deitado
            foreach (var (b, itens) in t.Recentes)
                _corpo.Children.Add(FaixaComSetas(F("Recentes em {0}", b.Nome),
                    itens.Select(i => (Control)(b.Colecao is "movies" or "tvshows" ? Cartaz(s, i) : CartaoDoServidor(s, i))), () => Ir(new NoServidor(conta, b))));
        }, conta);
    }

    // ───────── peças da tela inicial ─────────

    /// <summary>O título da seção: maior, com o traço dourado na frente, e o "Ver tudo" na ponta direita.</summary>
    private static Grid CabecalhoDeSecao(string titulo, params Control[] direita)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var nome = Ui.Pilha(10, Orientation.Horizontal,
            new Border { Width = 4, Height = 22, CornerRadius = new CornerRadius(2), Background = Ui.Pincel("Ouro"), VerticalAlignment = VerticalAlignment.Center },
            Ui.Linha(titulo, 21, FontWeight.ExtraBold));
        g.Children.Add(nome);
        var lado = Ui.Pilha(6, Orientation.Horizontal, direita);
        lado.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lado, 2);
        g.Children.Add(lado);
        return g;
    }

    /// <summary>
    /// Uma faixa que corre de lado pelas setas ‹ › do cabeçalho — sem a barra de rolagem embaixo de
    /// cada uma — e o "Ver tudo" quando há onde ver tudo. As setas apagam na ponta.
    /// </summary>
    private static Control FaixaComSetas(string titulo, IEnumerable<Control> itens, Action? verTudo = null, string? nomeDoVerTudo = null)
    {
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        linha.Children.AddRange(itens);
        var rolo = new ScrollViewer { Content = linha, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DispatcherTimer? anima = null;
        // desliza em uns 200 ms até o destino, em vez de pular
        void Correr(int sentido)
        {
            double destino = Math.Clamp(rolo.Offset.X + sentido * Math.Max(200, rolo.Viewport.Width * 0.85), 0, Math.Max(0, rolo.Extent.Width - rolo.Viewport.Width));
            anima?.Stop();
            double inicio = rolo.Offset.X;
            int passo = 0;
            anima = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            anima.Tick += (_, _) =>
            {
                passo++;
                double f = Math.Min(1, passo / 12.0);
                double suave = 1 - Math.Pow(1 - f, 3);
                rolo.Offset = new Vector(inicio + (destino - inicio) * suave, 0);
                if (f >= 1) anima.Stop();
            };
            anima.Start();
        }
        var esquerda = Ui.BotaoIcone(Icones.Seta, S("Anterior"), () => Correr(-1), "redondo", 18);
        esquerda.RenderTransform = new ScaleTransform(-1, 1);
        var direita = Ui.BotaoIcone(Icones.Seta, S("Próximo"), () => Correr(1), "redondo", 18);
        foreach (var b in new[] { esquerda, direita }) { b.Width = 36; b.Height = 36; b.CornerRadius = new CornerRadius(18); b.Padding = default; }
        void Setas()
        {
            double fim = rolo.Extent.Width - rolo.Viewport.Width;
            esquerda.IsVisible = direita.IsVisible = fim > 1;
            esquerda.IsEnabled = rolo.Offset.X > 1;
            direita.IsEnabled = rolo.Offset.X < fim - 1;
        }
        rolo.ScrollChanged += (_, _) => Setas();
        rolo.SizeChanged += (_, _) => Setas();
        var lado = new List<Control>();
        if (verTudo != null)
        {
            var ver = Ui.Botao(null, null, "plano", verTudo);
            ver.Content = Ui.Pilha(4, Orientation.Horizontal, Ui.Linha(nomeDoVerTudo ?? S("Ver tudo"), 13, FontWeight.Bold, "apagado"), Ui.Icone(Icones.Seta, 16, Ui.Pincel("Apagado")));
            lado.Add(ver);
        }
        lado.Add(esquerda);
        lado.Add(direita);
        return Ui.Pilha(12, Orientation.Vertical, CabecalhoDeSecao(titulo, [.. lado]), rolo);
    }

    /// <summary>
    /// Uma biblioteca como banner: a imagem dela e, embaixo, o ícone do tipo e o nome — por cima não,
    /// porque a imagem que o Jellyfin monta já traz o nome escrito.
    /// </summary>
    private Button BannerDeBiblioteca(ServidorDeMidia s, Item b)
    {
        var icone = b.Colecao switch { "movies" => Icones.Filme, "tvshows" => Icones.Tv, "livetv" => Icones.Tv, _ => Icones.Biblioteca };
        var img = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill };
        var g = new Grid
        {
            // sem imagem, o banner fica com um degradê da casa
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#2A2618"), 0), new GradientStop(Color.Parse("#141317"), 1) },
            },
        };
        // sem imagem: o ícone grande no meio do degradê
        g.Children.Add(new PathIcon { Data = StreamGeometry.Parse(icone), Width = 44, Height = 44, Foreground = Ui.Pincel("OuroBorda") });
        g.Children.Add(img);
        Cartoes.ImagemDoServidor(s, b.Id, img, ["Primary", "Thumb", "Backdrop"], 640);
        var capa = new Border { Classes = { "capa" }, Width = 288, Height = 162, Child = g };
        var nome = Ui.Pilha(8, Orientation.Horizontal, Ui.Icone(icone, 16, Ui.Pincel("Ouro")), Ui.Linha(b.Nome, 15, FontWeight.Bold));
        nome.Margin = new Thickness(4, 0, 4, 2);
        var botao = new Button
        {
            Classes = { "cartao" }, Padding = new Thickness(6), Margin = new Thickness(0, 0, 12, 12), Width = 300,
            Content = Ui.Pilha(8, Orientation.Vertical, capa, nome),
        };
        ToolTip.SetTip(botao, b.Nome);
        botao.Click += (_, _) => Ir(new NoServidor(s.Conta, b));
        return botao;
    }

    /// <summary>
    /// O destaque do início, como nos apps de streaming: o fundo do filme ou da série na largura toda,
    /// o logotipo (ou o nome), ano, duração e nota, a sinopse curta, ▶ e "Mais informações". Passa
    /// sozinho a cada 9 s; os pontos embaixo e as setas escolhem. Os detalhes chegam depois da tela,
    /// para o início não esperar por eles; item sem fundo no servidor fica de fora.
    /// </summary>
    private Control Destaque(ServidorDeMidia s, List<Item> candidatos, int ger)
    {
        var palco = new Grid { ClipToBounds = true, Background = Ui.Pincel("Superficie"), Opacity = 0,
            Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260) }] };
        var borda = new Border { CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = palco, Height = 380, IsVisible = false };
        // estreito, a sinopse sai: o nome, os botões e a imagem são o que importa
        Control? sinopseAtual = null;
        borda.SizeChanged += (_, e) =>
        {
            borda.Height = Math.Clamp(e.NewSize.Width * 0.36, 260, 470);
            if (sinopseAtual != null) sinopseAtual.IsVisible = e.NewSize.Width > 780;
        };
        var itens = new List<(Item item, ServidorDeMidia.Detalhes d)>();
        int atual = 0;
        DispatcherTimer? relogio = null;

        void Mostrar(int k)
        {
            if (itens.Count == 0) return;
            atual = (k + itens.Count) % itens.Count;
            var (item, d) = itens[atual];
            palco.Opacity = 0;
            DispatcherTimer.RunOnce(() =>
            {
                if (ger != _geracao) return;
                palco.Children.Clear();
                var fundo = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
                Cartoes.ImagemDoServidor(s, d.FundoDe!, fundo, ["Backdrop"], 1280);
                palco.Children.Add(fundo);
                // o texto fica do lado esquerdo, sobre o escuro; o lado direito mostra a imagem
                palco.Children.Add(new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#F00C0B0A"), 0), new GradientStop(Color.Parse("#B30C0B0A"), 0.38), new GradientStop(Color.Parse("#000C0B0A"), 0.75) },
                    },
                });
                palco.Children.Add(new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#000C0B0A"), 0.55), new GradientStop(Color.Parse("#CC0C0B0A"), 1) },
                    },
                });
                var texto = new StackPanel { Spacing = 10, MaxWidth = 540, Margin = new Thickness(40, 0, 40, 44), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
                bool episodio = item.Tipo == "Episode";
                var titulo = Ui.Texto(episodio ? item.Serie ?? item.Nome : item.Nome, 38, FontWeight.ExtraBold);
                titulo.MaxLines = 2; titulo.TextTrimming = TextTrimming.WordEllipsis;
                if (d.LogoDe != null)
                {
                    var logo = new Avalonia.Controls.Image { Stretch = Stretch.Uniform, MaxHeight = 120, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
                    titulo.IsVisible = true;
                    Cartoes.ImagemDoServidor(s, d.LogoDe, logo, ["Logo"], 800, () => titulo.IsVisible = false);
                    texto.Children.Add(logo);
                }
                texto.Children.Add(titulo);
                var meta = new List<string?>
                {
                    episodio ? string.Join(" · ", new[] { item.Episodio, item.Nome }.OfType<string>()) : item.Ano?.ToString(Textos.Cultura),
                    item.Duracao is { } dur ? Tempo(dur) : null,
                    d.Generos.Count > 0 ? string.Join(", ", d.Generos.Take(3)) : null,
                };
                texto.Children.Add(Meta(meta, d.Nota, d.Classificacao));
                if (d.Sinopse is { } sinopse)
                {
                    var t = Ui.Texto(sinopse, 14, FontWeight.Normal);
                    t.MaxLines = 3; t.TextTrimming = TextTrimming.WordEllipsis; t.LineHeight = 21; t.Opacity = 0.86;
                    t.IsVisible = borda.Bounds.Width is 0 or > 780;
                    sinopseAtual = t;
                    texto.Children.Add(t);
                }
                double? parou = Prefs.Atual.Posicoes.TryGetValue(Contas.EnderecoMidia(s.Conta, item.Id), out var r) ? r.Posicao : item.Posicao;
                bool comecado = item.EhVideo && parou is > 0;
                var tocar = Ui.Botao(comecado ? S("Continuar") : S("Assistir"), Icones.Play, "ouro", () => TocarDoDestaque(s, item, comecado), 20);
                tocar.Padding = new Thickness(22, 11); tocar.FontSize = 15; tocar.CornerRadius = new CornerRadius(24);
                var info = Ui.Botao(S("Mais informações"), Icones.Info, "vidro", () => Abrir(s.Conta, item), 20);
                info.Padding = new Thickness(20, 11); info.FontSize = 15; info.CornerRadius = new CornerRadius(24);
                var botoes = Ui.Pilha(12, Orientation.Horizontal, tocar, info);
                botoes.Margin = new Thickness(0, 6, 0, 0);
                texto.Children.Add(botoes);
                if (comecado && item.Duracao is > 0 and var total)
                    texto.Children.Add(new Border { Width = 260, Height = 4, CornerRadius = new CornerRadius(2), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left,
                        Background = new SolidColorBrush(Color.Parse("#40FFFFFF")), Child = Ui.Progresso(Math.Clamp(parou!.Value / total, 0, 1)) });
                palco.Children.Add(texto);
                if (itens.Count > 1)
                {
                    // os pontos: o atual comprido e dourado
                    var pontos = Ui.Pilha(6, Orientation.Horizontal);
                    for (int i = 0; i < itens.Count; i++)
                    {
                        int alvo = i;
                        var p = new Border
                        {
                            Width = i == atual ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                            Background = i == atual ? Ui.Pincel("Ouro") : new SolidColorBrush(Color.Parse("#66FFFFFF")),
                        };
                        p.PointerPressed += (_, _) => { Mostrar(alvo); relogio?.Stop(); relogio?.Start(); };
                        pontos.Children.Add(p);
                    }
                    pontos.HorizontalAlignment = HorizontalAlignment.Right; pontos.VerticalAlignment = VerticalAlignment.Bottom; pontos.Margin = new Thickness(0, 0, 28, 24);
                    palco.Children.Add(pontos);
                    var dir = Ui.BotaoIcone(Icones.Seta, S("Próximo"), () => { Mostrar(atual + 1); relogio?.Stop(); relogio?.Start(); }, "redondo", 20);
                    dir.Width = 44; dir.Height = 44; dir.CornerRadius = new CornerRadius(22); dir.Padding = default;
                    dir.Background = new SolidColorBrush(Color.Parse("#66000000"));
                    dir.HorizontalAlignment = HorizontalAlignment.Right; dir.VerticalAlignment = VerticalAlignment.Center; dir.Margin = new Thickness(0, 0, 16, 0);
                    palco.Children.Add(dir);
                }
                palco.Opacity = 1;
            }, TimeSpan.FromMilliseconds(itens.Count > 1 && palco.Children.Count > 0 ? 200 : 0));
        }

        _ = Task.Run(async () =>
        {
            var lidos = await Task.WhenAll(candidatos.Select(async c =>
            {
                try { return await s.LerDetalhes(c.Id); } catch { return default; }
            }));
            Dispatcher.UIThread.Post(() =>
            {
                if (ger != _geracao) return;
                foreach (var l in lidos) if (l.item != null && l.detalhes.FundoDe != null) itens.Add((l.item, l.detalhes));
                if (itens.Count == 0) return;
                borda.IsVisible = true;
                Mostrar(0);
                if (itens.Count < 2) return;
                relogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
                relogio.Tick += (_, _) =>
                {
                    // saiu da tela (outra pasta, outra aba): o relógio para
                    if (ger != _geracao || !borda.IsEffectivelyVisible) { relogio.Stop(); return; }
                    if (!borda.IsPointerOver) Mostrar(atual + 1);
                };
                relogio.Start();
            });
        });
        return borda;
    }

    /// <summary>O ▶ do destaque: vídeo toca (episódio com a temporada em volta, do ponto onde parou); série, o próximo episódio dela.</summary>
    private async void TocarDoDestaque(ServidorDeMidia s, Item item, bool continuar)
    {
        try
        {
            Item? alvo = item;
            if (item.Tipo == "Series")
            {
                // o "A seguir" da série; sem ele (nada visto ainda), o primeiro episódio
                alvo = await s.ProximoDaSerie(item.Id);
                if (alvo == null && (await s.Temporadas(item.Id)).FirstOrDefault() is { } t1) alvo = (await s.Episodios(item.Id, t1.Id)).FirstOrDefault();
                if (alvo == null) { Abrir(s.Conta, item); return; }
            }
            var (fila, k) = await s.Fila(alvo);
            if (continuar) EscolhaDeFaixas.PedirInicio(EscolhaDeFaixas.Inicio.Continuar, fila[k].Endereco);
            _tocar(fila, k);
        }
        catch (Exception e) { MostrarErro(item.Nome, e); }
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
        if (i.EhVideo) g.Children.Add(Cartoes.TocarAoPassar());
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
