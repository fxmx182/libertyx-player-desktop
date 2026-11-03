using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Mpv;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>
/// Os vídeos em grade ou em lista (o botão de cima decide), do mesmo jeito em toda tela: biblioteca,
/// pastas do computador, SMB, WebDAV, Jellyfin e a busca.
/// </summary>
public static class Cartoes
{
    public static Control Videos(List<Midia> lista, Action<List<Midia>, int> tocar, Func<Midia, MenuFlyout?>? menu = null)
    {
        Panel p = Prefs.Atual.Lista ? new StackPanel { Spacing = 2 } : new WrapPanel();
        for (int i = 0; i < lista.Count; i++)
        {
            var c = Prefs.Atual.Lista ? Linha(lista, i, tocar) : Cartao(lista, i, tocar);
            c.ContextFlyout = menu?.Invoke(lista[i]);
            p.Children.Add(c);
        }
        return p;
    }

    /// <summary>"1:42:10 · 2,1 GB · 3 nov" — o que a origem souber.</summary>
    private static string Info(Midia m, bool comDuracao) => string.Join(" · ", new[]
    {
        comDuracao && Miniaturas.Duracao(m) is { } d ? Tempo(d) : null,
        m.Tamanho > 0 ? Tamanho(m.Tamanho) : null,
        m.Data != default ? Data(m.Data) : null,
    }.Where(x => x != null));

    private static Button Cartao(List<Midia> lista, int i, Action<List<Midia>, int> tocar)
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
                Ui.Linha(Info(m, false), 12, FontWeight.Normal, "tenue")),
        };
        ToolTip.SetTip(b, m.Titulo);
        b.Click += (_, _) => tocar(lista, i);
        return b;
    }

    private static Button Linha(List<Midia> lista, int i, Action<List<Midia>, int> tocar)
    {
        var m = lista[i];
        var capa = Ui.Capa(128, 72, out var img);
        Prefs.Atual.Posicoes.TryGetValue(m.Endereco, out var r);
        ((Grid)capa.Child!).Children.Add(Ui.Progresso(r?.Fracao ?? 0));
        var info = Ui.Linha(Info(m, true), 12, FontWeight.Normal, "tenue");
        CarregarQuandoVisivel(capa, m, img, () => info.Text = Info(m, true));
        var textos = Ui.Pilha(4, Orientation.Vertical, Ui.Linha(m.Titulo, 14, FontWeight.SemiBold), info);
        textos.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(capa);
        Grid.SetColumn(textos, 1); textos.Margin = new Thickness(14, 0);
        g.Children.Add(textos);
        var b = new Button { Classes = { "cartao" }, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Stretch, Content = g };
        b.Click += (_, _) => tocar(lista, i);
        return b;
    }

    /// <summary>Uma pasta (do computador ou do servidor) na mesma linha dos servidores: ícone, nome, o que tem lá.</summary>
    public static Button Lugar(string icone, string titulo, string? sub, Action abrir, IBrush? cor = null)
    {
        var textos = Ui.Pilha(2, Orientation.Vertical, Ui.Linha(titulo, 14, FontWeight.SemiBold));
        if (sub != null) textos.Children.Add(Ui.Linha(sub, 12, FontWeight.Normal, "tenue"));
        textos.VerticalAlignment = VerticalAlignment.Center;
        var b = new Button
        {
            Classes = { "vidro" }, Margin = new Thickness(0, 0, 12, 12), Width = 270, MinHeight = 68, Padding = new Thickness(16, 12),
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
            Content = Ui.Pilha(12, Orientation.Horizontal, Ui.Icone(icone, 22, cor ?? Ui.Pincel("Ouro")), textos),
        };
        b.Click += (_, _) => abrir();
        return b;
    }

    /// <summary>Uma faixa que rola de lado, com o título em cima.</summary>
    public static Control Faixa(string titulo, IEnumerable<Control> itens, Control? extra = null)
    {
        var faixa = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        faixa.Children.AddRange(itens);
        var topo = Ui.Pilha(12, Orientation.Horizontal, Ui.Texto(titulo, 17, FontWeight.Bold));
        if (extra != null) topo.Children.Add(extra);
        return Ui.Pilha(10, Orientation.Vertical, topo,
            new ScrollViewer { Content = faixa, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
    }

    /// <summary>Pede a miniatura só quando o cartão aparece na tela — numa pasta com mil vídeos, não gera mil de uma vez.</summary>
    public static void CarregarQuandoVisivel(Control alvo, Midia m, Image img, Action? depois = null)
    {
        if (Miniaturas.Pronta(m) is { } pronta) { img.Source = pronta; return; }
        bool pedido = false;
        void Ver(object? s, EffectiveViewportChangedEventArgs e)
        {
            if (pedido || e.EffectiveViewport.Width <= 0 || !e.EffectiveViewport.Intersects(new Rect(alvo.Bounds.Size))) return;
            pedido = true;
            alvo.EffectiveViewportChanged -= Ver;
            Miniaturas.Pedir(m, bmp => { img.Source = bmp; depois?.Invoke(); });
        }
        alvo.EffectiveViewportChanged += Ver;
    }

    private static readonly ConcurrentDictionary<string, Bitmap?> Imagens = new();

    /// <summary>A imagem de uma série, temporada ou biblioteca do Jellyfin/Emby (os vídeos vão pelas <see cref="Miniaturas"/>).</summary>
    public static void ImagemDoServidor(ServidorDeMidia s, string itemId, Image img)
    {
        var chave = s.Conta.Id + "/" + itemId;
        if (Imagens.TryGetValue(chave, out var b)) { img.Source = b; return; }
        _ = Task.Run(async () =>
        {
            Bitmap? bmp = null;
            try { if (await s.Imagem(itemId, 480) is { } bytes) bmp = Bitmap.DecodeToWidth(new MemoryStream(bytes), 480); } catch { }
            Imagens[chave] = bmp;
            Dispatcher.UIThread.Post(() => img.Source = bmp);
        });
    }
}
