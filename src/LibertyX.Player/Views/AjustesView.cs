using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using LibertyX.Player.Core;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>Ajustes: o Pro, idioma, tamanho, pastas da biblioteca, reprodução e o "sobre".</summary>
public sealed class AjustesView : ScrollViewer
{
    private readonly Action _reconstruir;
    private readonly Action _adicionarPasta;
    private readonly Action<Pro.Recurso?> _mostrarPro;
    private readonly StackPanel _corpo = new() { Spacing = 18, Margin = new Thickness(28, 18, 28, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

    public AjustesView(Action reconstruir, Action adicionarPasta, Action<Pro.Recurso?> mostrarPro)
    {
        _reconstruir = reconstruir; _adicionarPasta = adicionarPasta; _mostrarPro = mostrarPro;
        Content = _corpo;
        Pro.Mudou += () => Avalonia.Threading.Dispatcher.UIThread.Post(Montar);
    }

    public void Montar()
    {
        _corpo.Children.Clear();
        _corpo.Children.Add(Ui.Linha(S("Ajustes"), 26, FontWeight.ExtraBold));
        _corpo.Children.Add(CartaoPro());
        _corpo.Children.Add(Secao(S("Aparência"), Linha(S("Idioma"), Idioma()), Linha(S("Tamanho da interface"), Escala())));
        _corpo.Children.Add(Secao(S("Biblioteca"), Pastas()));
        _corpo.Children.Add(Secao(S("Reprodução"),
            Chave(S("Decodificação por hardware"), S("Usa a placa de vídeo (VA-API, NVDEC) quando dá: menos processador e menos calor. Vale ao abrir o próximo vídeo."),
                Prefs.Atual.Decodificacao, v => { Prefs.Atual.Decodificacao = v; App.Mpv?.SetString("hwdec", v ? "auto-safe" : "no"); }),
            Chave(S("Rolagem quadro a quadro"), S("Ao arrastar, mostra o quadro exato debaixo do mouse. Desligada, pula de quadro-chave em quadro-chave (mais leve em vídeo 4K)."),
                Prefs.Atual.RolagemExata, v => Prefs.Atual.RolagemExata = v),
            Linha(S("Salto das setas"), Opcoes([5, 10, 15, 30], Prefs.Atual.Salto, v => Prefs.Atual.Salto = v, v => F("{0} s", v))),
            Linha(S("Velocidade ao segurar o clique"), Opcoes([1.5, 2, 3, 4], Prefs.Atual.VelocidadeSegurar, v => Prefs.Atual.VelocidadeSegurar = v, v => v.ToString("0.#", Textos.Cultura) + "×")),
            Linha(S("Capturas de quadro"), Ui.Linha((Prefs.Atual.PastaCapturas ?? Path.Combine(Caminhos.Imagens, "LibertyX")).Replace(Caminhos.Home, "~"), 13, FontWeight.Normal, "apagado"))));
        _corpo.Children.Add(Sobre());
    }

    // ───────── Pro ─────────

    private Control CartaoPro()
    {
        var estado = Pro.Atual switch
        {
            Pro.Estado.Comprado => Pro.Liberado && Pro.Licenca == null ? S("Pro ativo (versão do desenvolvedor)") : S("Pro ativo — obrigado!"),
            Pro.Estado.Teste t => P(t.DiasRestantes, "Teste grátis · falta {0} dia", "Teste grátis · faltam {0} dias"),
            _ => S("Seu teste grátis terminou"),
        };
        var topo = Ui.Pilha(12, Orientation.Horizontal, Ui.Marca(44), Ui.Pilha(2, Orientation.Vertical,
            Ui.Linha("LibertyX Pro", 20, FontWeight.ExtraBold), Ui.Linha(estado, 14, FontWeight.SemiBold, "apagado")));
        var p = Ui.Pilha(12, Orientation.Vertical, topo);
        if (Pro.Licenca is { } lic) p.Children.Add(Ui.Texto(F("Licença {0}", lic), 13, FontWeight.Normal, "tenue"));
        if (Pro.Atual is not Pro.Estado.Comprado)
            p.Children.Add(Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(S("Conhecer o Pro"), Icones.Estrela, "ouro", () => _mostrarPro(null)),
                Ui.Botao(S("Tenho uma chave"), null, "vidro", () => ProDialogo.Chave())));
        return new Border { Classes = { "painel" }, Child = p, BorderBrush = Ui.Pincel("OuroBorda") };
    }

    // ───────── aparência ─────────

    private Control Idioma()
    {
        var c = new ComboBox { Width = 200 };
        c.Items.Add(S("Do sistema"));
        foreach (var (_, n) in Textos.Opcoes) c.Items.Add(n);
        c.SelectedIndex = Prefs.Atual.Idioma is { } i ? Array.FindIndex(Textos.Opcoes, o => o.Codigo == i) + 1 : 0;
        c.SelectionChanged += (_, _) =>
        {
            Prefs.Atual.Idioma = c.SelectedIndex <= 0 ? null : Textos.Opcoes[c.SelectedIndex - 1].Codigo;
            Prefs.Atual.Salvar();
            Textos.Iniciar(Prefs.Atual.Idioma);
            _reconstruir();
        };
        return c;
    }

    private Control Escala()
    {
        var c = new ComboBox { Width = 200 };
        c.Items.Add(F("Automático ({0})", Core.Escala.Auto is { } a ? Core.Escala.Pct(a) : "100%"));
        foreach (var e in Core.Escala.Opcoes) c.Items.Add(Core.Escala.Pct(e));
        c.SelectedIndex = Prefs.Atual.Escala is { } s ? Array.IndexOf(Core.Escala.Opcoes, s) + 1 : 0;
        var aviso = Ui.Linha(S("Vale ao abrir o app de novo."), 12, FontWeight.Normal, "tenue");
        aviso.IsVisible = false;
        c.SelectionChanged += (_, _) =>
        {
            Prefs.Atual.Escala = c.SelectedIndex <= 0 ? null : Core.Escala.Opcoes[c.SelectedIndex - 1];
            Prefs.Atual.Salvar();
            aviso.IsVisible = true;
        };
        return Ui.Pilha(4, Orientation.Vertical, c, aviso);
    }

    // ───────── biblioteca ─────────

    private Control Pastas()
    {
        var p = Ui.Pilha(6, Orientation.Vertical,
            Ui.Texto(S("O LibertyX procura vídeos em Vídeos, Downloads, pendrives e HDs conectados, e nas pastas abaixo."), 13, FontWeight.Normal, "apagado"));
        foreach (var raiz in Biblioteca.Raizes())
        {
            bool minha = Prefs.Atual.Pastas.Contains(raiz);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            g.Children.Add(Ui.Icone(Icones.Pasta, 18, Ui.Pincel("Ouro")));
            var t = Ui.Linha(raiz.Replace(Caminhos.Home, "~"), 14, FontWeight.Medium);
            t.Margin = new Thickness(10, 0); t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1); g.Children.Add(t);
            if (minha)
            {
                var r = Ui.BotaoIcone(Icones.Apagar, S("Tirar da biblioteca"), () => { Prefs.Atual.Pastas.Remove(raiz); Prefs.Atual.Salvar(); Montar(); }, "plano", 16);
                Grid.SetColumn(r, 2); g.Children.Add(r);
            }
            p.Children.Add(g);
        }
        if (Prefs.Atual.PastasOcultas.Count > 0)
        {
            var mostrar = Ui.Botao(P(Prefs.Atual.PastasOcultas.Count, "Mostrar de novo {0} pasta oculta", "Mostrar de novo {0} pastas ocultas"), Icones.Ocultar, "plano",
                () => { Prefs.Atual.PastasOcultas.Clear(); Prefs.Atual.Salvar(); Montar(); }, 16);
            p.Children.Add(mostrar);
        }
        p.Children.Add(Ui.Botao(S("Acrescentar pasta"), Icones.Adicionar, "vidro", _adicionarPasta));
        return p;
    }

    // ───────── sobre ─────────

    public static string Versao()
    {
        var a = Assembly.GetExecutingAssembly();
        var info = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        return info.Replace("+", " · ");
    }

    private Control Sobre()
    {
        string mpv = App.Mpv?.GetString("mpv-version") ?? "mpv";
        string ffmpeg = App.Mpv?.GetString("ffmpeg-version") is { } f ? "FFmpeg " + f : "";
        var diario = Ui.Botao(S("Abrir o registro de erros"), null, "plano", () => Ui.Abrir(Diario.Arquivo));
        return Secao(S("Sobre"),
            Ui.Texto($"LibertyX Player {Versao()}", 14, FontWeight.Bold),
            Ui.Texto(S("Sua mídia. Sua liberdade."), 13, FontWeight.Normal, "apagado"),
            Ui.Texto($"{mpv} · {ffmpeg}", 12, FontWeight.Normal, "tenue"),
            Ui.Texto(S("Feito sobre projetos de código aberto: mpv e FFmpeg (LGPL/GPL, usados como estão instalados no sistema), Avalonia (MIT), SMBLibrary (LGPL), fonte Manrope (OFL)."), 12, FontWeight.Normal, "tenue"),
            Ui.Pilha(6, Orientation.Horizontal,
                Ui.Botao("fxmx182.github.io", Icones.Link, "plano", () => Ui.Abrir("https://fxmx182.github.io/#/player")), diario));
    }

    // ───────── peças ─────────

    private static Control Secao(string titulo, params Control[] itens)
    {
        var p = Ui.Pilha(14, Orientation.Vertical, Ui.Texto(titulo, 17, FontWeight.Bold));
        p.Children.AddRange(itens);
        return new Border { Classes = { "painel" }, Child = p };
    }

    private static Control Linha(string rotulo, Control controle)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var t = Ui.Texto(rotulo, 14, FontWeight.Medium);
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        Grid.SetColumn(controle, 1);
        g.Children.Add(controle);
        return g;
    }

    private static Control Chave(string rotulo, string ajuda, bool valor, Action<bool> mudou)
    {
        var ts = new ToggleSwitch { IsChecked = valor, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
        ts.IsCheckedChanged += (_, _) => { mudou(ts.IsChecked == true); Prefs.Atual.Salvar(); };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var textos = Ui.Pilha(2, Orientation.Vertical, Ui.Texto(rotulo, 14, FontWeight.Medium), Ui.Texto(ajuda, 12, FontWeight.Normal, "tenue"));
        textos.Margin = new Thickness(0, 0, 20, 0);
        g.Children.Add(textos);
        Grid.SetColumn(ts, 1);
        g.Children.Add(ts);
        return g;
    }

    private static Control Opcoes<TV>(TV[] valores, TV atual, Action<TV> mudou, Func<TV, string> nome) where TV : IEquatable<TV>
    {
        var c = new ComboBox { Width = 120 };
        foreach (var v in valores) c.Items.Add(nome(v));
        c.SelectedIndex = Math.Max(0, Array.FindIndex(valores, v => v.Equals(atual)));
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) { mudou(valores[c.SelectedIndex]); Prefs.Atual.Salvar(); } };
        return c;
    }
}

/// <summary>A tela do Pro e a de ativar a chave — o mesmo texto e a mesma ordem do celular.</summary>
public static class ProDialogo
{
    public static void Mostrar(Pro.Recurso? pedido)
    {
        var p = Ui.Pilha(14, Orientation.Vertical);
        var topo = Ui.Pilha(12, Orientation.Horizontal, Ui.Marca(52), Ui.Pilha(2, Orientation.Vertical,
            Ui.Linha("LibertyX Pro", 22, FontWeight.ExtraBold),
            Ui.Linha(Pro.Atual is Pro.Estado.Teste t ? P(t.DiasRestantes, "Teste grátis · falta {0} dia", "Teste grátis · faltam {0} dias")
                : Pro.Atual is Pro.Estado.Expirado ? S("Seu teste grátis terminou") : S("Pro ativo — obrigado!"), 14, FontWeight.SemiBold, "apagado")));
        p.Children.Add(topo);
        if (pedido is { } r)
            p.Children.Add(Ui.Texto(F("{0} faz parte do LibertyX Pro.", Pro.Nome(r)), 15, FontWeight.Bold));
        foreach (var rec in Enum.GetValues<Pro.Recurso>())
        {
            var icone = rec switch { Pro.Recurso.Rede => Icones.Servidor, Pro.Recurso.Janela => Icones.Janela, Pro.Recurso.Dormir => Icones.Timer, _ => Icones.Camera };
            var linha = Ui.Pilha(12, Orientation.Horizontal, Ui.Icone(icone, 22, Ui.Pincel("Ouro")),
                Ui.Pilha(1, Orientation.Vertical, Ui.Texto(Pro.Nome(rec), 14, FontWeight.Bold), Ui.Texto(Pro.Descricao(rec), 12, FontWeight.Normal, "apagado")));
            ((Control)linha.Children[1]).MaxWidth = 380;
            p.Children.Add(linha);
        }
        p.Children.Add(Ui.Texto(S("Todo o resto é grátis para sempre: biblioteca, player, rolagem quadro a quadro, legendas e faixas de áudio."), 12, FontWeight.Normal, "tenue"));
        p.Children.Add(Ui.Texto(F("Pagamento único de {0}, sem assinatura. A mesma chave vale no Android, na TV e no Linux.", Pro.Preco), 12, FontWeight.Normal, "tenue"));
        var comprar = Ui.Botao(null, null, "ouro", () => Ui.Abrir(Pro.BotTelegram));
        comprar.HorizontalAlignment = HorizontalAlignment.Stretch;
        comprar.Content = Ui.Pilha(1, Orientation.Vertical, Ui.Texto(S("Pagar com Bitcoin ou USDT"), 15, FontWeight.ExtraBold),
            Ui.Texto(S("Pelo Telegram — a chave chega na hora"), 12, FontWeight.SemiBold));
        p.Children.Add(comprar);
        var botoes = Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(S("Tenho uma chave"), null, "vidro", Chave), Ui.Botao(S("Agora não"), null, "plano", Dialogo.Fechar));
        p.Children.Add(botoes);
        p.Children.Add(Ui.Texto(F("Sem Telegram neste computador? Abra {0} no celular.", Pro.BotTelegramCurto), 11, FontWeight.Normal, "tenue"));
        Dialogo.Mostrar(p, 500);
    }

    public static void Chave()
    {
        var campo = new TextBox { Watermark = "LXP1-…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, FontFamily = new FontFamily("monospace") };
        var msg = Ui.Texto("", 13); msg.IsVisible = false;
        var colar = Ui.Botao(S("Colar"), null, "vidro", null);
        colar.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(campo)?.Clipboard is { } cb && await cb.TryGetTextAsync() is { } t) campo.Text = t.Trim();
        };
        var ativar = Ui.Botao(S("Ativar"), null, "ouro", () =>
        {
            var r = Pro.AtivarChave(campo.Text ?? "");
            msg.IsVisible = true;
            if (r is Core.Chave.Resultado.Recusada rec) { msg.Text = rec.Motivo; msg.Foreground = Ui.Pincel("Vermelho"); return; }
            msg.Text = S("Pro ativado com a sua chave — obrigado!"); msg.Foreground = Ui.Pincel("Ouro");
            Avalonia.Threading.DispatcherTimer.RunOnce(Dialogo.Fechar, TimeSpan.FromSeconds(1.6));
        });
        var botoes = Ui.Pilha(10, Orientation.Horizontal, colar, Ui.Botao(S("Cancelar"), null, "plano", Dialogo.Fechar), ativar);
        botoes.HorizontalAlignment = HorizontalAlignment.Right;
        Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(S("Ativar com uma chave"), 20, FontWeight.ExtraBold),
            Ui.Texto(S("Cole a chave que o bot da LibertyX mandou no Telegram. Ela começa com LXP1."), 13, FontWeight.Normal, "apagado"),
            campo, msg, botoes), 500);
    }
}
