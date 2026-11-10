using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;
using Item = LibertyX.Player.Rede.ServidorDeMidia.Item;

namespace LibertyX.Player.Views;

/// <summary>As páginas do Jellyfin e do Emby: filme ou episódio, série, pessoa e canais ao vivo.</summary>
public sealed partial class ArquivosView
{
    // ───────── peças comuns ─────────

    /// <summary>
    /// O fundo do item com o logotipo por cima — a cara que o app do servidor dá às páginas. Como no
    /// celular deitado: a capa 16:9 vai inteira no centro, com a mesma imagem desfocada dos lados, em
    /// vez de esticada à largura e cortada em cima e embaixo. A altura acompanha a janela.
    /// </summary>
    private static Control Cabeca(ServidorDeMidia s, string fundoDe, string? logoDe)
    {
        var desfocada = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill, Opacity = 0.5, Effect = new BlurEffect { Radius = 40 } };
        var capa = new Avalonia.Controls.Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        var g = new Grid { Height = 380, ClipToBounds = true, Background = Ui.Pincel("Superficie") };
        var borda = new Border { CornerRadius = new CornerRadius(18), ClipToBounds = true, Child = g, IsVisible = false };
        borda.SizeChanged += (_, e) => g.Height = Math.Clamp(e.NewSize.Width * 0.34, 200, 440);
        // sem fundo nenhum no servidor, a página começa no título, sem uma faixa vazia em cima
        Cartoes.ImagemDoServidor(s, fundoDe, capa, ["Backdrop", "Thumb", "Primary"], 1280, () => { desfocada.Source = capa.Source; borda.IsVisible = true; });
        g.Children.Add(desfocada);
        g.Children.Add(capa);
        g.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Transparent, 0.45), new GradientStop(Color.Parse("#990C0B0A"), 0.8), new GradientStop(Color.Parse("#0C0B0A"), 1) },
            },
        });
        if (logoDe != null)
        {
            // o logotipo é PNG transparente: vai inteiro, sem corte
            var logo = new Avalonia.Controls.Image
            {
                Stretch = Stretch.Uniform, MaxWidth = 380, MaxHeight = 120, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(26, 0, 0, 20),
            };
            Cartoes.ImagemDoServidor(s, logoDe, logo, ["Logo"], 800);
            g.Children.Add(logo);
        }
        return borda;
    }

    /// <summary>Um botão da linha de ícones, como no app do Jellyfin: só o símbolo, aceso na cor dele quando marcado. O de tocar vem no círculo dourado.</summary>
    private static Button BotaoRedondo(string icone, string dica, Action clique, IBrush? aceso = null, bool destaque = false)
    {
        var b = new Button
        {
            Classes = { destaque ? "redondoouro" : "redondo" }, Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Padding = default,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = Ui.Icone(icone, destaque ? 30 : 24, destaque ? Ui.Pincel("SobreOuro") : aceso ?? Ui.Pincel("Texto")),
        };
        ToolTip.SetTip(b, dica);
        b.Click += (_, _) => clique();
        return b;
    }

    /// <summary>A linha "2019   1 h 42 min   Termina às 23:10   ★ 7.4   [14]".</summary>
    private static Control Meta(IEnumerable<string?> partes, double? nota, string? classificacao)
    {
        var p = Ui.Pilha(14, Orientation.Horizontal);
        foreach (var t in partes.OfType<string>()) p.Children.Add(Ui.Linha(t, 13, FontWeight.Medium, "apagado"));
        if (nota is { } n)
            p.Children.Add(Ui.Pilha(4, Orientation.Horizontal, Ui.Icone(Icones.Estrela, 14, Ui.Pincel("Ouro")),
                Ui.Linha(n.ToString("0.0", Textos.Cultura), 13, FontWeight.Medium, "apagado")));
        if (classificacao != null)
            p.Children.Add(new Border
            {
                BorderBrush = Ui.Pincel("Tenue"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0),
                VerticalAlignment = VerticalAlignment.Center, Child = Ui.Linha(classificacao, 11, FontWeight.SemiBold, "apagado"),
            });
        foreach (var c in p.Children) c.VerticalAlignment = VerticalAlignment.Center;
        return p;
    }

    /// <summary>Sinopse em quatro linhas; um clique abre o resto.</summary>
    private static Control Sinopse(string texto)
    {
        var t = Ui.Texto(texto, 14, FontWeight.Normal);
        t.MaxLines = 4; t.TextTrimming = TextTrimming.WordEllipsis; t.MaxWidth = 900; t.HorizontalAlignment = HorizontalAlignment.Left;
        t.Opacity = 0.9; t.LineHeight = 21; t.Cursor = new Cursor(StandardCursorType.Hand);
        t.PointerPressed += (_, _) => t.MaxLines = t.MaxLines == 4 ? 0 : 4;
        return t;
    }

    private static Control Credito(string rotulo, IEnumerable<string> nomes)
    {
        var l = nomes.Take(6).ToList();
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*"), IsVisible = l.Count > 0, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(Ui.Linha(rotulo, 13, FontWeight.SemiBold, "apagado"));
        var v = Ui.Texto(string.Join(", ", l), 13);
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    /// <summary>Uma linha "Áudio  [Brazilian Portuguese - AC3 ▾]".</summary>
    private static Control Seletor(string rotulo, Control valor)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        var r = Ui.Linha(rotulo, 13, FontWeight.SemiBold, "apagado");
        r.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(r);
        Grid.SetColumn(valor, 1);
        valor.HorizontalAlignment = HorizontalAlignment.Stretch;
        g.Children.Add(valor);
        return g;
    }

    /// <summary>O elenco com foto; clicar numa pessoa abre a página dela.</summary>
    private Control FaixaDoElenco(ServidorDeMidia s, List<ServidorDeMidia.Pessoa> elenco) =>
        Cartoes.Faixa(S("Elenco"), elenco.Select(p =>
        {
            var img = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill };
            var g = new Grid();
            g.Children.Add(new PathIcon { Data = StreamGeometry.Parse(Icones.Pessoa), Width = 34, Height = 34, Foreground = Ui.Pincel("Tenue") });
            g.Children.Add(img);
            Cartoes.ImagemDoServidor(s, p.Id, img, ["Primary"], 240);
            var b = new Button
            {
                Classes = { "cartao" }, Width = 132, Padding = new Thickness(6),
                Content = Ui.Pilha(3, Orientation.Vertical, new Border { Classes = { "capa" }, Width = 120, Height = 180, Child = g },
                    Ui.Linha(p.Nome, 13, FontWeight.SemiBold), Ui.Linha(p.Papel ?? "", 12, FontWeight.Normal, "tenue")),
            };
            ToolTip.SetTip(b, p.Papel != null ? $"{p.Nome} — {p.Papel}" : p.Nome);
            b.Click += (_, _) => Ir(new PaginaDaPessoa(s.Conta, p));
            return (Control)b;
        }));

    private static string Hora(DateTime d) => d.ToString("t", Textos.Cultura);

    // ───────── página do filme ou episódio ─────────

    private void MostrarPaginaDoVideo(PaginaDoVideo pg)
    {
        var s = new ServidorDeMidia(pg.Conta);
        Carregar(async () =>
        {
            // quem acabou de fechar o player chega antes do "parou": sem esperar, o ponto onde parou viria velho
            await AvisoAoServidor.EmDia();
            var (item, d) = await s.LerDetalhes(pg.Item.Id);
            var temporada = item.Tipo == "Episode" ? await Tentar(s.Temporada(item)) : [];
            var extras = d.Extras > 0 ? await Tentar(s.Extras(item.Id)) : [];
            return (item, d, temporada, extras);
        }, r => DesenharPaginaDoVideo(pg, s, r.item, r.d, r.temporada, r.extras), pg.Conta);
    }

    private void DesenharPaginaDoVideo(PaginaDoVideo pg, ServidorDeMidia s, Item item, ServidorDeMidia.Detalhes d, List<Item> temporada, List<Item> extras)
    {
        var conta = pg.Conta;
        var e = pg.Escolhas;
        var audios = d.Faixas("Audio");
        var legendas = d.Faixas("Subtitle");
        e.Audio ??= (audios.FirstOrDefault(a => a.Padrao) ?? audios.FirstOrDefault())?.Indice;
        var endereco = Contas.EnderecoMidia(conta, item.Id);
        double? parou = Prefs.Atual.Posicoes.TryGetValue(endereco, out var r) ? r.Posicao : item.Posicao;

        // leva os seletores ao player: a posição conta só as faixas de dentro do arquivo
        void PedirFaixas(string chave)
        {
            var internos = audios.Where(a => !a.Externo).ToList();
            var padrao = (internos.FirstOrDefault(a => a.Padrao) ?? internos.FirstOrDefault())?.Indice;
            var pedido = new EscolhaDeFaixas.Pedido();
            // na conversão o servidor manda uma faixa só: a escolhida
            if (Qualidade.Da(conta.Id) > 0) Conversoes.PedirAudio(item.Id, e.Audio);
            else if (e.Audio != null && e.Audio != padrao && internos.FindIndex(a => a.Indice == e.Audio) is >= 0 and var pa) pedido = pedido with { Audio = pa };
            if (e.Legenda == -1) pedido = pedido with { Legenda = new EscolhaDeFaixas.Legenda.Desligada() };
            else if (e.Legenda is { } li && legendas.FirstOrDefault(x => x.Indice == li) is { } f)
            {
                if (f.Externo) pedido = pedido with { Legenda = new EscolhaDeFaixas.Legenda.Externa(f.Indice) };
                else if (legendas.Where(x => !x.Externo).ToList().FindIndex(x => x.Indice == f.Indice) is >= 0 and var pl)
                    pedido = pedido with { Legenda = new EscolhaDeFaixas.Legenda.Interna(pl) };
            }
            EscolhaDeFaixas.Pedir(pedido, chave);
        }

        // episódio toca com a temporada em volta, para o "próximo" do player ir ao seguinte
        void Tocar(EscolhaDeFaixas.Inicio? inicio)
        {
            var lista = item.Tipo == "Episode" && temporada.Any(x => x.Id == item.Id) ? temporada : [item];
            var midias = lista.Select(s.ParaTocar).ToList();
            int k = Math.Max(0, lista.FindIndex(x => x.Id == item.Id));
            PedirFaixas(midias[k].Endereco);
            if (inicio is { } i) EscolhaDeFaixas.PedirInicio(i, midias[k].Endereco);
            _tocar(midias, k);
        }

        void TocarSozinho(Item v) => _tocar([s.ParaTocar(v)], 0);

        _corpo.Children.Add(Cabeca(s, d.FundoDe ?? item.SerieId ?? item.Id, d.LogoDe));

        // ── títulos ──
        var titulos = Ui.Pilha(6, Orientation.Vertical);
        if (item.Tipo == "Episode" && item.SerieId is { } serieId)
        {
            var serie = Ui.Botao(null, null, "plano", () => Ir(new PaginaDaSerie(conta, serieId, item.Serie ?? "")));
            serie.Padding = new Thickness(0, 2, 8, 2);
            serie.Content = Ui.Pilha(4, Orientation.Horizontal, Ui.Linha(item.Serie ?? item.Nome, 28, FontWeight.ExtraBold), Ui.Icone(Icones.Seta, 26, Ui.Pincel("Apagado")));
            ToolTip.SetTip(serie, S("Ir para a série"));
            titulos.Children.Add(serie);
            titulos.Children.Add(Ui.Texto(string.Join(" · ", new[] { d.NomeDaTemporada, item.Numero is { } n ? $"{n}. {item.Nome}" : item.Nome }.OfType<string>()), 16, FontWeight.SemiBold));
        }
        else titulos.Children.Add(Ui.Texto(item.Nome, 28, FontWeight.ExtraBold));
        titulos.Children.Add(Meta(
        [
            item.Tipo == "Episode" && d.Estreia is { } est ? est.ToString("d", Textos.Cultura) : item.Ano?.ToString(Textos.Cultura),
            item.Duracao is { } dur ? Tempo(dur) : null,
            item.Duracao is { } dd ? F("Termina às {0}", Hora(DateTime.Now.AddSeconds(dd - (parou ?? 0)))) : null,
        ], d.Nota, d.Classificacao));
        _corpo.Children.Add(titulos);

        // ── botões ──
        bool comecado = parou is > 0;
        var mais = BotaoRedondo(Icones.MaisDeLado, S("Mais"), () => { }, null);
        var menu = new MenuFlyout();
        if (d.TrailersNoDisco > 0 || d.TrailersDaInternet.Count > 0)
        {
            var t = new MenuItem { Header = S("Trailer") };
            t.Click += async (_, _) =>
            {
                var local = d.TrailersNoDisco > 0 ? (await Tentar(s.TrailersNoDisco(item.Id))).FirstOrDefault() : null;
                if (local != null) TocarSozinho(local);
                else if (d.TrailersDaInternet.FirstOrDefault() is { } url) Ui.Abrir(url);
            };
            menu.Items.Add(t);
        }
        if (item.Tipo == "Episode" && item.SerieId is { } sid)
        {
            var t = new MenuItem { Header = S("Ir para a série") };
            t.Click += (_, _) => Ir(new PaginaDaSerie(conta, sid, item.Serie ?? ""));
            menu.Items.Add(t);
        }
        if (d.IdsExternos.TryGetValue("Imdb", out var imdb))
        {
            var t = new MenuItem { Header = S("Abrir no IMDb") };
            t.Click += (_, _) => Ui.Abrir("https://www.imdb.com/title/" + imdb);
            menu.Items.Add(t);
        }
        if (item.Tipo == "Movie" && d.IdsExternos.TryGetValue("Tmdb", out var tmdb))
        {
            var t = new MenuItem { Header = S("Abrir no TMDB") };
            t.Click += (_, _) => Ui.Abrir("https://www.themoviedb.org/movie/" + tmdb);
            menu.Items.Add(t);
        }
        mais.Flyout = menu;
        mais.IsVisible = menu.Items.Count > 0;
        var botoes = Ui.Pilha(12, Orientation.Horizontal,
            BotaoRedondo(Icones.Play, comecado ? S("Continuar") : S("Assistir"), () => Tocar(comecado ? EscolhaDeFaixas.Inicio.Continuar : null), destaque: true));
        if (comecado) botoes.Children.Add(BotaoRedondo(Icones.Recomecar, S("Do início"), () => Tocar(EscolhaDeFaixas.Inicio.DoComeco)));
        botoes.Children.Add(BotaoRedondo(Icones.Ok, item.Visto ? S("Já reproduzido (desmarcar)") : S("Marcar como já reproduzido"),
            () => MarcarVisto(s, item, !item.Visto), item.Visto ? Ui.Pincel("Ouro") : null));
        botoes.Children.Add(BotaoRedondo(item.Favorito ? Icones.Coracao : Icones.CoracaoVazio, item.Favorito ? S("Favorito (desmarcar)") : S("Favorito"),
            () => Marcar(() => s.MarcarFavorito(item.Id, !item.Favorito), item.Titulo), item.Favorito ? Ui.Pincel("Vermelho") : null));
        botoes.Children.Add(mais);
        var acoes = Ui.Pilha(10, Orientation.Vertical, botoes);
        if (comecado && item.Duracao is { } total)
        {
            acoes.Children.Add(new Border { Width = 360, Height = 4, CornerRadius = new CornerRadius(2), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.Parse("#40FFFFFF")), Child = Ui.Progresso(Math.Clamp(parou!.Value / total, 0, 1)) });
            acoes.Children.Add(Ui.Linha(F("Parou em {0} · faltam {1}", Tempo(parou.Value), Tempo(Math.Max(0, total - parou.Value))), 12, FontWeight.Normal, "tenue"));
        }
        _corpo.Children.Add(acoes);

        // ── faixas ──
        var faixas = Ui.Pilha(10, Orientation.Vertical);
        if (d.Faixas("Video").FirstOrDefault() is { } video) faixas.Children.Add(Seletor(S("Vídeo"), Ui.Linha(video.Titulo, 13)));
        if (audios.Count > 0)
        {
            var c = new ComboBox();
            foreach (var a in audios) c.Items.Add(a.Titulo);
            c.SelectedIndex = Math.Max(0, audios.FindIndex(a => a.Indice == e.Audio));
            c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) e.Audio = audios[c.SelectedIndex].Indice; };
            faixas.Children.Add(Seletor(S("Áudio"), c));
        }
        {
            var c = new ComboBox();
            c.Items.Add(legendas.Count == 0 ? S("Nenhuma") : S("Automática (só a forçada)"));
            c.Items.Add(S("Desligada"));
            foreach (var l in legendas) c.Items.Add(l.Titulo);
            c.SelectedIndex = e.Legenda switch { null => 0, -1 => 1, var li => 2 + Math.Max(0, legendas.FindIndex(x => x.Indice == li)) };
            c.SelectionChanged += (_, _) => e.Legenda = c.SelectedIndex switch { <= 0 => null, 1 => -1, var k => legendas[k - 2].Indice };
            faixas.Children.Add(Seletor(S("Legendas"), c));
        }
        var aviso = Ui.Texto(S("O servidor converte o vídeo na hora: gasta o processador dele e a imagem perde um pouco. Em casa, prefira o original."), 12, FontWeight.Normal, "tenue");
        {
            var c = new ComboBox();
            foreach (var k in Qualidade.Opcoes) c.Items.Add(Qualidade.Nome(k));
            c.SelectedIndex = Math.Max(0, Array.IndexOf(Qualidade.Opcoes, Qualidade.Da(conta.Id)));
            c.SelectionChanged += (_, _) =>
            {
                if (c.SelectedIndex < 0) return;
                Qualidade.Definir(conta.Id, Qualidade.Opcoes[c.SelectedIndex]);
                aviso.IsVisible = Qualidade.Opcoes[c.SelectedIndex] > 0;
            };
            faixas.Children.Add(Seletor(S("Qualidade"), c));
        }
        aviso.IsVisible = Qualidade.Da(conta.Id) > 0;
        faixas.Children.Add(aviso);
        _corpo.Children.Add(new Border { Classes = { "painel" }, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Child = faixas });

        // ── sinopse e créditos ──
        if (d.Sinopse is { } sinopse) _corpo.Children.Add(Sinopse(sinopse));
        _corpo.Children.Add(Ui.Pilha(4, Orientation.Vertical,
            Credito(S("Gêneros"), d.Generos), Credito(S("Direção"), d.PessoasCom("Director").Select(p => p.Nome)), Credito(S("Roteiro"), d.PessoasCom("Writer").Select(p => p.Nome))));

        // ── os outros episódios da temporada ──
        if (temporada.Count > 1)
            _corpo.Children.Add(Cartoes.Faixa(F("Mais de {0}", d.NomeDaTemporada ?? S("Temporada")), temporada.Select(ep =>
            {
                var c = CartaoDoServidor(s, ep, atual: ep.Id == item.Id);
                // outro episódio troca esta página, e não empilha uma sobre a outra
                if (ep.Id != item.Id) c.Click += (_, _) => { if (_pilha.Count > 1 && _pilha[^2] is PaginaDoVideo) _pilha.RemoveAt(_pilha.Count - 2); };
                return (Control)c;
            })));
        var elenco = d.PessoasCom("Actor", "GuestStar").Take(30).ToList();
        if (elenco.Count > 0) _corpo.Children.Add(FaixaDoElenco(s, elenco));
        if (extras.Count > 0)
            _corpo.Children.Add(Cartoes.Faixa(S("Extras"), extras.Select(x =>
            {
                var capa = Ui.Capa(250, 141, out var img);
                Cartoes.ImagemDoServidor(s, x.Id, img, ["Primary", "Thumb"], 480);
                var b = new Button { Classes = { "cartao" }, Width = 262, Padding = new Thickness(6), Content = Ui.Pilha(4, Orientation.Vertical, capa, Ui.Linha(x.Nome, 13, FontWeight.SemiBold)) };
                b.Click += (_, _) => TocarSozinho(x);
                return (Control)b;
            })));
    }

    // ───────── página da série ─────────

    /// <summary>
    /// A página da série, como no app do Jellyfin: fundo e logotipo, sinopse, o próximo episódio a ver
    /// no ▶, as temporadas em abas e os episódios da escolhida, com cena, duração e o quanto já foi visto.
    /// </summary>
    private void MostrarPaginaDaSerie(PaginaDaSerie pg)
    {
        var s = new ServidorDeMidia(pg.Conta);
        Carregar(async () =>
        {
            await AvisoAoServidor.EmDia();
            var (serie, d) = await s.LerDetalhes(pg.SerieId);
            var temporadas = await Tentar(s.Temporadas(pg.SerieId));
            Item? proximo = null;
            try { proximo = await s.ProximoDaSerie(pg.SerieId); } catch { }
            // abre na temporada do próximo episódio a ver; sem ele, na primeira
            if (pg.Escolhida == null || temporadas.All(t => t.Id != pg.Escolhida))
                pg.Escolhida = proximo?.TemporadaId is { } tp && temporadas.Any(t => t.Id == tp) ? tp : temporadas.FirstOrDefault()?.Id;
            var episodios = pg.Escolhida is { } esc ? await Tentar(s.Episodios(pg.SerieId, esc)) : [];
            return (serie, d, temporadas, proximo, episodios);
        }, r => DesenharPaginaDaSerie(pg, s, r.serie, r.d, r.temporadas, r.proximo, r.episodios), pg.Conta);
    }

    private void DesenharPaginaDaSerie(PaginaDaSerie pg, ServidorDeMidia s, Item serie, ServidorDeMidia.Detalhes d, List<Item> temporadas, Item? proximo, List<Item> episodios)
    {
        _corpo.Children.Add(Cabeca(s, d.FundoDe ?? pg.SerieId, d.LogoDe));
        _corpo.Children.Add(Ui.Pilha(6, Orientation.Vertical, Ui.Texto(serie.Nome, 28, FontWeight.ExtraBold),
            Meta([serie.Ano?.ToString(Textos.Cultura), temporadas.Count > 0 ? P(temporadas.Count, "{0} temporada", "{0} temporadas") : null], d.Nota, d.Classificacao)));

        async void TocarProximo()
        {
            if (proximo == null) return;
            var (fila, k) = await s.Fila(proximo);
            _tocar(fila, k);
        }
        var botoes = Ui.Pilha(12, Orientation.Horizontal);
        if (proximo != null)
            botoes.Children.Add(BotaoRedondo(Icones.Play, proximo.Posicao is > 0 ? S("Continuar") : S("Assistir"), TocarProximo, destaque: true));
        botoes.Children.Add(BotaoRedondo(Icones.Ok, serie.Visto ? S("Já reproduzido (desmarcar)") : S("Marcar como já reproduzido"),
            () => MarcarVisto(s, serie, !serie.Visto), serie.Visto ? Ui.Pincel("Ouro") : null));
        botoes.Children.Add(BotaoRedondo(serie.Favorito ? Icones.Coracao : Icones.CoracaoVazio, serie.Favorito ? S("Favorito (desmarcar)") : S("Favorito"),
            () => Marcar(() => s.MarcarFavorito(serie.Id, !serie.Favorito), serie.Nome), serie.Favorito ? Ui.Pincel("Vermelho") : null));
        var acoes = Ui.Pilha(8, Orientation.Vertical, botoes);
        if (proximo != null)
            acoes.Children.Add(Ui.Linha(string.Join(" · ", new[] { proximo.Episodio, proximo.Nome }.OfType<string>()), 12, FontWeight.Normal, "tenue"));
        _corpo.Children.Add(acoes);
        if (d.Sinopse is { } sinopse) _corpo.Children.Add(Sinopse(sinopse));
        if (d.Generos.Count > 0) _corpo.Children.Add(Ui.Texto(string.Join(" · ", d.Generos.Take(5)), 13, FontWeight.Normal, "apagado"));

        // ── temporadas em abas, episódios da escolhida ──
        var lista = new StackPanel { Spacing = 8 };
        void MostrarEpisodios(List<Item> eps)
        {
            lista.Children.Clear();
            foreach (var ep in eps) lista.Children.Add(LinhaDoEpisodio(s, ep));
        }
        if (temporadas.Count > 0)
        {
            var abas = new WrapPanel();
            foreach (var t in temporadas)
            {
                var b = Ui.Botao(t.Nome, null, t.Id == pg.Escolhida ? "ouro" : "vidro", null);
                b.Margin = new Thickness(0, 0, 8, 8);
                b.CornerRadius = new CornerRadius(17);
                b.Click += async (_, _) =>
                {
                    if (t.Id == pg.Escolhida) return;
                    pg.Escolhida = t.Id;
                    foreach (var o in abas.Children.OfType<Button>()) { o.Classes.Set("ouro", o == b); o.Classes.Set("vidro", o != b); }
                    int ger = _geracao;
                    lista.Children.Clear();
                    lista.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20) });
                    var eps = await Tentar(s.Episodios(pg.SerieId, t.Id));
                    if (ger == _geracao && pg.Escolhida == t.Id) MostrarEpisodios(eps);
                };
                abas.Children.Add(b);
            }
            _corpo.Children.Add(abas);
        }
        MostrarEpisodios(episodios);
        _corpo.Children.Add(Ui.Limitar(lista, 900));
        var elenco = d.PessoasCom("Actor", "GuestStar").Take(30).ToList();
        if (elenco.Count > 0) _corpo.Children.Add(FaixaDoElenco(s, elenco));
    }

    /// <summary>Um episódio na lista da série: a cena, o ✓ ou a barrinha, o número e o nome, a duração.</summary>
    private Button LinhaDoEpisodio(ServidorDeMidia s, Item ep)
    {
        var capa = Ui.Capa(200, 112, out var img);
        var g = (Grid)capa.Child!;
        Cartoes.ImagemDoServidor(s, ep.Id, img, ["Primary", "Thumb"], 400);
        double frac = Prefs.Atual.Posicoes.TryGetValue(Contas.EnderecoMidia(s.Conta, ep.Id), out var r) ? r.Fracao : ep.Progresso ?? 0;
        g.Children.Add(Ui.Progresso(frac));
        if (ep.Visto && frac <= 0.01) g.Children.Add(Cartoes.MarcaDeVisto(20));
        var textos = Ui.Pilha(4, Orientation.Vertical, Ui.Linha(ep.Titulo, 15, FontWeight.SemiBold),
            Ui.Linha(ep.Duracao is { } d ? Tempo(d) : "", 12, FontWeight.Normal, "tenue"));
        textos.VerticalAlignment = VerticalAlignment.Center;
        textos.Margin = new Thickness(16, 0);
        var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        linha.Children.Add(capa);
        Grid.SetColumn(textos, 1);
        linha.Children.Add(textos);
        var b = new Button { Classes = { "cartao" }, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Stretch, Content = linha };
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.ContextFlyout = MenuDoVisto(s, ep);
        b.Click += (_, _) => Ir(new PaginaDoVideo(s.Conta, ep));
        return b;
    }

    // ───────── página da pessoa ─────────

    /// <summary>Foto, nascimento, biografia e os filmes e séries dela que estão no servidor.</summary>
    private void MostrarPaginaDaPessoa(PaginaDaPessoa pg)
    {
        var s = new ServidorDeMidia(pg.Conta);
        Carregar(async () =>
        {
            ServidorDeMidia.Detalhes? d = null;
            try { d = (await s.LerDetalhes(pg.Pessoa.Id)).detalhes; } catch { }
            return (d, obras: await Tentar(s.Obras(pg.Pessoa.Id)));
        }, r =>
        {
            var foto = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill };
            var g = new Grid();
            g.Children.Add(new PathIcon { Data = StreamGeometry.Parse(Icones.Pessoa), Width = 48, Height = 48, Foreground = Ui.Pincel("Tenue") });
            g.Children.Add(foto);
            Cartoes.ImagemDoServidor(s, pg.Pessoa.Id, foto, ["Primary"], 360);
            var info = Ui.Pilha(6, Orientation.Vertical, Ui.Texto(pg.Pessoa.Nome, 28, FontWeight.ExtraBold));
            if (r.d?.Estreia is { } nasc) info.Children.Add(Ui.Texto(F("Nascimento: {0}", nasc.ToString("D", Textos.Cultura)), 13, FontWeight.Normal, "apagado"));
            if (r.obras.Count > 0) info.Children.Add(Ui.Texto(P(r.obras.Count, "{0} título no servidor", "{0} títulos no servidor"), 13, FontWeight.Normal, "apagado"));
            if (r.d?.Sinopse is { } bio) info.Children.Add(Sinopse(bio));
            var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            topo.Children.Add(new Border { Classes = { "capa" }, Width = 170, Height = 255, Child = g, VerticalAlignment = VerticalAlignment.Top });
            Grid.SetColumn(info, 1);
            info.Margin = new Thickness(22, 0, 0, 0);
            topo.Children.Add(info);
            _corpo.Children.Add(topo);
            if (r.obras.Count == 0) return;
            _corpo.Children.Add(Ui.Texto(S("No servidor"), 17, FontWeight.Bold));
            var w = new WrapPanel();
            foreach (var o in r.obras) { var c = Cartaz(s, o); c.Margin = new Thickness(0, 0, 12, 12); w.Children.Add(c); }
            _corpo.Children.Add(w);
        }, pg.Conta);
    }

    // ───────── TV ao vivo ─────────

    /// <summary>Todos os canais, com o programa de agora, o horário e quanto já passou.</summary>
    private void MostrarCanais(CanaisAoVivo pg)
    {
        var s = new ServidorDeMidia(pg.Conta);
        Carregar(() => s.Canais(), canais =>
        {
            if (canais.Count == 0) { _corpo.Children.Add(Ui.Texto(S("Este servidor não tem canais ao vivo."), 14, FontWeight.Normal, "apagado")); return; }
            var lista = new StackPanel { Spacing = 6 };
            foreach (var c in canais)
            {
                var logo = new Border { Classes = { "capa" }, Width = 128, Height = 72, Child = LogoDoCanal(s, c) };
                var textos = Ui.Pilha(3, Orientation.Vertical, Ui.Linha(string.Join(" · ", new[] { c.Numero, c.Nome }.Where(x => !string.IsNullOrEmpty(x))), 15, FontWeight.SemiBold));
                if (c.Programa is { } p)
                {
                    textos.Children.Add(Ui.Linha(p.Nome, 13, FontWeight.Normal, "apagado"));
                    if (p.Inicio is { } i && p.Fim is { } f)
                        textos.Children.Add(Ui.Pilha(10, Orientation.Horizontal, Ui.Linha($"{Hora(i)} – {Hora(f)}", 12, FontWeight.Normal, "tenue"),
                            new Border { Width = 110, Height = 3, CornerRadius = new CornerRadius(2), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center,
                                Background = new SolidColorBrush(Color.Parse("#40FFFFFF")), Child = Ui.Progresso(p.Andamento ?? 0) }));
                }
                textos.VerticalAlignment = VerticalAlignment.Center;
                textos.Margin = new Thickness(16, 0);
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                g.Children.Add(logo);
                Grid.SetColumn(textos, 1);
                g.Children.Add(textos);
                var b = new Button { Classes = { "cartao" }, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = g };
                b.Click += (_, _) => TocarCanal(s, c, b);
                lista.Children.Add(b);
            }
            _corpo.Children.Add(Ui.Limitar(lista, 900));
        }, pg.Conta);
    }
}
