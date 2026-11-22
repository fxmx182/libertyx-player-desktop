using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LibertyX.Player.Core;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>Ajustes: o Pro, idioma, tamanho, pastas da biblioteca, reprodução e o "sobre".</summary>
public sealed class AjustesView : ScrollViewer
{
    private readonly Action _reconstruir;
    private readonly Action _adicionarPasta;
    private readonly Action _pastasMudaram;
    private readonly Action<Pro.Recurso?> _mostrarPro;
    private readonly StackPanel _corpo = new() { Spacing = 18, Margin = new Thickness(28, 18, 28, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

    public AjustesView(Action reconstruir, Action adicionarPasta, Action pastasMudaram, Action<Pro.Recurso?> mostrarPro)
    {
        _reconstruir = reconstruir; _adicionarPasta = adicionarPasta; _pastasMudaram = pastasMudaram; _mostrarPro = mostrarPro;
        Content = _corpo;
        Pro.Mudou += () => Avalonia.Threading.Dispatcher.UIThread.Post(Montar);
    }

    // sem isto o Avalonia procura um tema para "AjustesView", não acha, e a página fica sem a parte
    // que rola: o conteúdo aparece, mas o que passa da janela nunca chega à tela
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public void Montar()
    {
        _corpo.Children.Clear();
        _corpo.Children.Add(Ui.Linha(S("Ajustes"), 26, FontWeight.ExtraBold));
        _corpo.Children.Add(CartaoPro());
        // no Windows o tamanho vem da escala do próprio sistema (Configurações › Tela)
        _corpo.Children.Add(OperatingSystem.IsWindows() ? Secao(S("Aparência"), Linha(S("Idioma"), Idioma()))
            : Secao(S("Aparência"), Linha(S("Idioma"), Idioma()), Linha(S("Tamanho da interface"), Escala())));
        _corpo.Children.Add(Secao(S("Biblioteca"), Pastas()));
        _corpo.Children.Add(BarraDeAbas());
        _corpo.Children.Add(Secao(S("Reprodução"),
            Chave(S("Decodificação por hardware"), OperatingSystem.IsWindows()
                    ? S("Usa a placa de vídeo (D3D11, DXVA2) quando dá: menos processador e menos calor. Vale ao abrir o próximo vídeo.")
                    : S("Usa a placa de vídeo (VA-API, NVDEC) quando dá: menos processador e menos calor. Vale ao abrir o próximo vídeo."),
                Prefs.Atual.Decodificacao, v => { Prefs.Atual.Decodificacao = v; App.Mpv?.SetString("hwdec", v ? "auto-safe" : "no"); }),
            Chave(S("Rolagem quadro a quadro"), S("Ao arrastar, mostra o quadro exato debaixo do mouse. Desligada, pula de quadro-chave em quadro-chave (mais leve em vídeo 4K)."),
                Prefs.Atual.RolagemExata, v => Prefs.Atual.RolagemExata = v),
            Linha(S("Salto das setas"), Opcoes([5, 10, 15, 30], Prefs.Atual.Salto, v => Prefs.Atual.Salto = v, v => F("{0} s", v))),
            Linha(S("Velocidade ao segurar o clique"), Opcoes([1.5, 2, 3, 4], Prefs.Atual.VelocidadeSegurar, v => Prefs.Atual.VelocidadeSegurar = v, v => v.ToString("0.#", Textos.Cultura) + "×")),
            Linha(S("Capturas de quadro"), Ui.Linha((Prefs.Atual.PastaCapturas ?? Path.Combine(Caminhos.Imagens, "LibertyX")).Replace(Caminhos.Home, "~"), 13, FontWeight.Normal, "apagado"))));
        _corpo.Children.Add(Secao("LibertyX", Grupo(
            LinhaDeMenu(Icones.Gesto, S("Ver o guia dos gestos e ferramentas"), null, null, false, MostrarGuia),
            LinhaDeMenu(Icones.Documento, S("Licenças de código aberto"), null, null, false, MostrarLicencas),
            LinhaDeMenu(Icones.Inseto, S("Registro de erros"), S("Para copiar e mandar ao suporte"), null, false, MostrarRegistro))));
        _corpo.Children.Add(Secao(S("Fale com a gente"), FaleConosco()));
        _corpo.Children.Add(Rodape());
    }

    // ───────── barra de abas ─────────

    /// <summary>
    /// Ajustes › Barra de abas, como a dock do celular: o que fica a um clique além de Início e Ajustes.
    /// Lotada (três escolhidas), as outras chaves apagam até abrir vaga.
    /// </summary>
    private Control BarraDeAbas()
    {
        var escolhidas = Abas.Escolhidas();
        bool cheia = escolhidas.Count >= Abas.Maximo;
        var opcoes = new List<(string aba, string nome, Control icone)>
        {
            ("arquivos", S("Arquivos"), Ui.Icone(Icones.Pasta, 20, Ui.Pincel("Ouro"))),
            ("procurar", S("Procurar"), Ui.Icone(Icones.Buscar, 20, Ui.Pincel("Ouro"))),
        };
        foreach (var s in Servidores.Lista) opcoes.Add(("smb:" + s.Id, s.Nome, MainWindow.Logo("smb", 20)));
        foreach (var c in Contas.Lista)
            opcoes.Add(("conta:" + c.Id, c.Nome, c.Tipo == TipoDeConta.Jellyfin ? MainWindow.Logo("jellyfin", 20)
                : Ui.Icone(c.Tipo == TipoDeConta.WebDav ? Icones.Nuvem : Icones.Servidor, 20, Ui.Pincel("Ouro"))));
        var linhas = new List<Control>();
        foreach (var (aba, nome, icone) in opcoes)
        {
            bool ligada = escolhidas.Contains(aba);
            var ts = new ToggleSwitch { IsChecked = ligada, OnContent = "", OffContent = "", IsEnabled = ligada || !cheia, VerticalAlignment = VerticalAlignment.Center };
            ts.IsCheckedChanged += (_, _) => { Abas.Alternar(aba); Avalonia.Threading.Dispatcher.UIThread.Post(Montar); };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Opacity = ligada || !cheia ? 1 : 0.45 };
            icone.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icone);
            var t = Ui.Linha(nome, 14, FontWeight.Medium);
            t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(14, 0);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            Grid.SetColumn(ts, 2);
            g.Children.Add(ts);
            linhas.Add(g);
        }
        linhas.Add(Ui.Texto(S("Início e Ajustes ficam sempre. Escolha até três: um servidor na barra abre direto nele."), 12, FontWeight.Normal, "tenue"));
        return Secao(S("Barra de abas"), [.. linhas]);
    }

    // ───────── registro de erros ─────────

    /// <summary>O registro, com o mais novo embaixo (a caixa já abre lá), para copiar e colar na conversa de suporte.</summary>
    private void MostrarRegistro()
    {
        var texto = Diario.Texto();
        // colar meio megabyte numa conversa não ajuda ninguém: o que importa é o que veio logo antes do problema
        const int tetoDaCopia = 60_000;
        var caixa = new TextBox
        {
            Text = texto.Length == 0 ? S("Nada registrado ainda.") : texto, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("monospace"), FontSize = 11, Height = 420,
        };
        caixa.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => caixa.CaretIndex = caixa.Text?.Length ?? 0);
        var copiar = Ui.Botao(S("Copiar"), Icones.Ok, "ouro", null);
        copiar.IsEnabled = texto.Length > 0;
        copiar.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } area) return;
            await area.SetTextAsync(texto.Length > tetoDaCopia ? texto[^tetoDaCopia..] : texto);
            copiar.Content = S("Copiado");
        };
        var limpar = Ui.Botao(S("Limpar"), Icones.Apagar, "plano", () => { Diario.Limpar(); Dialogo.Fechar(); });
        var botoes = Ui.Pilha(10, Orientation.Horizontal, limpar, Ui.Botao(S("Fechar"), null, "vidro", Dialogo.Fechar), copiar);
        botoes.HorizontalAlignment = HorizontalAlignment.Right;
        Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(S("Registro de erros"), 20, FontWeight.ExtraBold),
            Ui.Texto(S("Clique em Copiar e cole na conversa de suporte. As senhas não aparecem aqui."), 13, FontWeight.Normal, "apagado"),
            caixa, botoes), 760);
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

    /// <summary>
    /// Onde a biblioteca procura, tudo editável: os lugares automáticos ligam e desligam, as pastas
    /// acrescentadas trocam de lugar ou saem, e as que ficaram fora da busca (ou ocultas) voltam.
    /// Nada disso mexe nos arquivos do disco.
    /// </summary>
    private Control Pastas()
    {
        var p = Ui.Pilha(8, Orientation.Vertical,
            Ui.Texto(S("Escolha onde o LibertyX procura vídeos. Desligar um lugar ou tirar uma pasta não apaga nada do disco."), 13, FontWeight.Normal, "apagado"));
        var pref = Prefs.Atual;

        p.Children.Add(Subtitulo(S("Procura automática")));
        var videos = Biblioteca.Normalizar(Caminhos.Videos);
        foreach (var raiz in Biblioteca.Automaticas())
        {
            string tipo = Biblioteca.Igual(raiz, videos) ? S("Pasta Vídeos")
                : Biblioteca.Removiveis().Any(r => Biblioteca.Igual(r, raiz)) ? S("Disco ou pendrive") : S("Pasta Downloads");
            var ts = new ToggleSwitch { IsChecked = !Biblioteca.Desligada(raiz), OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(ts, S("Procurar vídeos aqui"));
            ts.IsCheckedChanged += (_, _) =>
            {
                pref.RaizesDesligadas.RemoveAll(d => Biblioteca.Igual(d, raiz));
                if (ts.IsChecked != true) pref.RaizesDesligadas.Add(raiz);
                pref.Salvar(); _pastasMudaram();
            };
            p.Children.Add(LinhaDePasta(Icones.Pasta, raiz, tipo, ts));
        }

        if (pref.Pastas.Count > 0)
        {
            p.Children.Add(Subtitulo(S("Pastas acrescentadas")));
            foreach (var pasta in pref.Pastas.ToList())
            {
                var trocar = Ui.BotaoIcone(Icones.Editar, S("Trocar por outra pasta"), () => TrocarPasta(pasta), "plano", 16);
                var tirar = Ui.BotaoIcone(Icones.Apagar, S("Tirar da biblioteca"), () =>
                {
                    pref.Pastas.Remove(pasta); pref.Salvar(); Montar(); _pastasMudaram();
                }, "plano", 16);
                p.Children.Add(LinhaDePasta(Icones.Pasta, pasta, Directory.Exists(pasta) ? null : S("Não encontrada"),
                    Ui.Pilha(2, Orientation.Horizontal, trocar, tirar)));
            }
        }

        if (pref.PastasExcluidas.Count > 0)
        {
            p.Children.Add(Subtitulo(S("Fora da busca (com as subpastas)")));
            foreach (var pasta in pref.PastasExcluidas.ToList())
            {
                var voltar = Ui.Botao(S("Procurar de novo"), null, "plano", () =>
                {
                    pref.PastasExcluidas.Remove(pasta); pref.Salvar(); Montar(); _pastasMudaram();
                });
                p.Children.Add(LinhaDePasta(Icones.Fechar, pasta, null, voltar));
            }
        }

        if (pref.PastasOcultas.Count > 0)
        {
            p.Children.Add(Subtitulo(S("Pastas ocultas")));
            foreach (var pasta in pref.PastasOcultas.ToList())
            {
                var mostrar = Ui.Botao(S("Mostrar"), null, "plano", () =>
                {
                    pref.PastasOcultas.Remove(pasta); pref.Salvar(); Montar(); _pastasMudaram();
                });
                p.Children.Add(LinhaDePasta(Icones.Ocultar, pasta, null, mostrar));
            }
            if (pref.PastasOcultas.Count > 1)
                p.Children.Add(Ui.Botao(P(pref.PastasOcultas.Count, "Mostrar de novo {0} pasta oculta", "Mostrar de novo {0} pastas ocultas"), Icones.Ocultar, "plano",
                    () => { pref.PastasOcultas.Clear(); pref.Salvar(); Montar(); _pastasMudaram(); }, 16));
        }

        var botoes = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var acrescentar = Ui.Botao(S("Acrescentar pasta"), Icones.Adicionar, "vidro", _adicionarPasta);
        acrescentar.Margin = new Thickness(0, 0, 10, 8);
        var excluir = Ui.Botao(S("Deixar uma pasta fora da busca"), Icones.Fechar, "vidro", ExcluirPasta);
        excluir.Margin = new Thickness(0, 0, 10, 8);
        botoes.Children.Add(acrescentar); botoes.Children.Add(excluir);
        p.Children.Add(botoes);
        return p;
    }

    private static Control Subtitulo(string t)
    {
        var l = Ui.Linha(t, 13, FontWeight.Bold, "apagado");
        l.Margin = new Thickness(0, 8, 0, 0);
        return l;
    }

    private static Control LinhaDePasta(string icone, string caminho, string? nota, Control direita)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 34 };
        var i = Ui.Icone(icone, 18, Ui.Pincel("Ouro"));
        i.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(i);
        // o nome em destaque e o caminho embaixo: num caminho longo, o fim (o nome) é o que some
        var nome = Ui.Linha(Path.GetFileName(caminho) is { Length: > 0 } n ? n : caminho, 14, FontWeight.SemiBold);
        var onde = caminho.Replace(Caminhos.Home, "~");
        var textos = Ui.Pilha(1, Orientation.Vertical, nome, Ui.Linha(nota == null ? onde : nota + " · " + onde, 12, FontWeight.Normal, "tenue"));
        ToolTip.SetTip(textos, caminho);
        textos.Margin = new Thickness(10, 0); textos.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(textos, 1); g.Children.Add(textos);
        direita.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(direita, 2); g.Children.Add(direita);
        return g;
    }

    private async Task<string?> EscolherPasta(string titulo)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return null;
        var r = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = titulo, AllowMultiple = false });
        return r.Select(x => x.TryGetLocalPath()).OfType<string>().FirstOrDefault() is { } f ? Biblioteca.Normalizar(f) : null;
    }

    private async void TrocarPasta(string antiga)
    {
        if (await EscolherPasta(S("Trocar por outra pasta")) is not { } nova) return;
        var pref = Prefs.Atual;
        int i = pref.Pastas.IndexOf(antiga);
        if (pref.Pastas.Any(p => Biblioteca.Igual(p, nova))) pref.Pastas.Remove(antiga);
        else if (i >= 0) pref.Pastas[i] = nova; else pref.Pastas.Add(nova);
        pref.PastasExcluidas.RemoveAll(e => Biblioteca.Igual(e, nova));
        pref.Salvar(); Montar(); _pastasMudaram();
    }

    private async void ExcluirPasta()
    {
        if (await EscolherPasta(S("Deixar uma pasta fora da busca")) is not { } pasta) return;
        var pref = Prefs.Atual;
        if (!pref.PastasExcluidas.Any(e => Biblioteca.Igual(e, pasta))) pref.PastasExcluidas.Add(pasta);
        pref.Salvar(); Montar(); _pastasMudaram();
    }

    // ───────── sobre ─────────

    public static string Versao()
    {
        var a = Assembly.GetExecutingAssembly();
        var info = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        return info.Replace("+", " · ");
    }

    // ───────── LibertyX e fale com a gente (como no celular) ─────────

    private const string Site = "https://fxmx182.github.io/#/player";
    private const string EmailDoSuporte = "libertyx.suporte@gmail.com";

    /// <summary>Linhas de menu, uma embaixo da outra, com um fio entre elas.</summary>
    private static Control Grupo(params Control[] linhas)
    {
        var p = new StackPanel { Margin = new Thickness(-12, -4, -12, -6) };
        for (int i = 0; i < linhas.Length; i++)
        {
            if (i > 0) p.Children.Add(new Border { Height = 1, Background = Ui.Pincel("VidroBorda"), Margin = new Thickness(48, 0, 12, 0) });
            p.Children.Add(linhas[i]);
        }
        return p;
    }

    private static Button LinhaDeMenu(string icone, string titulo, string? sub, string? valor, bool externo, Action clique)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var i = Ui.Icone(icone, 22, Ui.Pincel("Ouro"));
        i.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(i);
        var tit = Ui.Linha(titulo, 15, FontWeight.SemiBold);
        var textos = sub == null ? (Control)tit : Ui.Pilha(2, Orientation.Vertical, tit, Ui.Linha(sub, 12.5, FontWeight.Normal, "tenue"));
        textos.Margin = new Thickness(14, 0); textos.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(textos, 1); g.Children.Add(textos);
        if (valor != null)
        {
            var v = Ui.Linha(valor, 13, FontWeight.Medium, "tenue");
            v.VerticalAlignment = VerticalAlignment.Center; v.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(v, 2); g.Children.Add(v);
        }
        var seta = Ui.Icone(externo ? Icones.Externo : Icones.Chevron, externo ? 16 : 20, Ui.Pincel("Tenue"));
        seta.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(seta, 3); g.Children.Add(seta);
        var b = new Button { Classes = { "linha" }, Content = g };
        b.Click += (_, _) => clique();
        return b;
    }

    /// <summary>"1.0.12 · aea9c33": o commit com 7 letras, como no celular.</summary>
    private static string VersaoCurta()
    {
        var v = Versao();
        int i = v.IndexOf(" · ", StringComparison.Ordinal);
        return i >= 0 && v.Length > i + 3 + 7 ? v[..(i + 3 + 7)] : v;
    }

    private Control FaleConosco()
    {
        Button? compartilhar = null;
        compartilhar = LinhaDeMenu(Icones.Compartilhar, S("Compartilhar o LibertyX"), null, null, true, async () =>
        {
            // no computador não há a folha de compartilhar do celular: o convite vai para a área de transferência
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } area) return;
            await area.SetTextAsync(S("O LibertyX Player toca qualquer vídeo, do computador ou direto do servidor de casa.") + " " + Site);
            Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
                Ui.Texto(S("Convite copiado"), 20, FontWeight.ExtraBold),
                Ui.Texto(S("O texto e o link do LibertyX estão na área de transferência. Cole numa conversa, num grupo ou onde quiser."), 14, FontWeight.Normal, "apagado"),
                Direita(Ui.Botao(S("Abrir o site"), Icones.Link, "plano", () => { Ui.Abrir(Site); Dialogo.Fechar(); }),
                    Ui.Botao("OK", null, "ouro", Dialogo.Fechar))));
        });
        var x = LinhaDeMenu(Icones.LogoX, S("Seguir no X"), null, S("Em breve"), true, () =>
            Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
                Ui.Texto(S("Seguir no X"), 20, FontWeight.ExtraBold),
                Ui.Texto(S("Nosso perfil no X chega em breve."), 14, FontWeight.Normal, "apagado"),
                Direita(Ui.Botao("OK", null, "ouro", Dialogo.Fechar)))));
        var email = LinhaDeMenu(Icones.Email, S("Enviar um email"), EmailDoSuporte, null, true, () =>
            Ui.Abrir($"mailto:{EmailDoSuporte}?subject={Uri.EscapeDataString("LibertyX Player " + Versao())}"));
        return Grupo(compartilhar, x, email);
    }

    private static Control Direita(params Control[] botoes)
    {
        var p = Ui.Pilha(10, Orientation.Horizontal, botoes);
        p.HorizontalAlignment = HorizontalAlignment.Right;
        return p;
    }

    /// <summary>Qual versão está instalada, à mão — e o mpv e o FFmpeg que vieram com ela.</summary>
    private static Control Rodape()
    {
        string mpv = App.Mpv?.GetString("mpv-version") ?? "mpv";
        string ffmpeg = App.Mpv?.GetString("ffmpeg-version") is { } f ? " · FFmpeg " + f : "";
        var p = Ui.Pilha(3, Orientation.Vertical,
            Ui.Linha($"LibertyX Player {VersaoCurta()}", 12, FontWeight.SemiBold, "tenue"),
            Ui.Linha(S("Sua mídia. Sua liberdade.") + $" · {mpv}{ffmpeg}", 11, FontWeight.Normal, "tenue"));
        p.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var c in p.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
        p.Margin = new Thickness(0, 0, 0, 10);
        return p;
    }

    /// <summary>O guia: os gestos do mouse, o teclado e as ferramentas do menu do player.</summary>
    private void MostrarGuia()
    {
        var lista = new StackPanel { Spacing = 2 };
        void Titulo(string t) { var l = Ui.Linha(t, 13, FontWeight.Bold, "apagado"); l.Margin = new Thickness(0, 10, 0, 4); lista.Children.Add(l); }
        void Item(string k, string d)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), Margin = new Thickness(0, 3) };
            var a = Ui.Texto(k, 13, FontWeight.Bold); a.Foreground = Ui.Pincel("Ouro");
            var b = Ui.Texto(d, 13); Grid.SetColumn(b, 1);
            g.Children.Add(a); g.Children.Add(b);
            lista.Children.Add(g);
        }
        Titulo(S("Teclado e mouse"));
        foreach (var (k, d) in PlayerView.ListaDeAtalhos()) Item(k, d);
        Titulo(S("Ferramentas do menu ⋯ do player"));
        Item(S("Velocidade"), S("De 0,25× a 4×, sem mudar o tom da voz"));
        Item(S("Faixa de áudio") + " · " + S("Legendas"), S("Troca o idioma, abre uma legenda de arquivo, acerta o atraso e o tamanho"));
        Item(S("Proporção e zoom"), S("Preenche a tela, corta as tarjas ou volta ao original"));
        Item(S("Modo noturno"), S("Escurece a imagem em passos, para ver no escuro"));
        Item(S("Tempo para dormir"), S("Pausa sozinho depois de um tempo"));
        Item(S("Captura de quadro"), S("Salva a imagem na pasta de capturas"));
        Item(S("Mini janela"), S("O vídeo continua num canto, por cima das outras janelas"));
        Item(S("Informações do vídeo"), S("Formato, resolução, faixas e taxa de dados"));
        var rolagem = new ScrollViewer { Content = lista, MaxHeight = 460 };
        Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(S("Guia dos gestos e ferramentas"), 20, FontWeight.ExtraBold),
            rolagem, Direita(Ui.Botao("OK", null, "ouro", Dialogo.Fechar))), 620);
    }

    private static readonly (string nome, string autor, string licenca, string fonte)[] Componentes =
    [
        ("mpv / libmpv", "mpv contributors", "LGPL 2.1+", "https://github.com/mpv-player/mpv"),
        ("FFmpeg", "FFmpeg", "LGPL 2.1+", "https://ffmpeg.org"),
        ("libass, FreeType, HarfBuzz, FriBidi", "libass, FreeType, HarfBuzz, FriBidi", "ISC · FTL · MIT · LGPL 2.1+", "https://github.com/libass/libass"),
        ("Avalonia", "AvaloniaUI", "MIT", "https://github.com/AvaloniaUI/Avalonia"),
        ("SkiaSharp", "Microsoft / Google", "MIT · BSD 3", "https://github.com/mono/SkiaSharp"),
        ("SMBLibrary", "Tal Aloni", "LGPL 3+", "https://github.com/TalAloni/SMBLibrary"),
        (".NET", ".NET Foundation", "MIT", "https://github.com/dotnet/runtime"),
        ("Manrope", "The Manrope Project Authors", "SIL OFL 1.1", "https://github.com/googlefonts/manrope"),
    ];

    private void MostrarLicencas()
    {
        var lista = new StackPanel();
        foreach (var c in Componentes)
            lista.Children.Add(LinhaDeMenu(Icones.Documento, c.nome, $"{c.autor} · {c.licenca}", null, true, () => Ui.Abrir(c.fonte)));
        var rolagem = new ScrollViewer { Content = lista, MaxHeight = 440 };
        Dialogo.Mostrar(Ui.Pilha(12, Orientation.Vertical,
            Ui.Texto(S("Licenças de código aberto"), 20, FontWeight.ExtraBold),
            Ui.Texto(S("O LibertyX é feito sobre estes projetos de código aberto. Clique num deles para ver o código e a licença. O libmpv que vem com o app é compilado pelos scripts públicos do repositório."), 13, FontWeight.Normal, "apagado"),
            rolagem, Direita(Ui.Botao(S("Fechar"), null, "ouro", Dialogo.Fechar))), 600);
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
        p.Children.Add(Ui.Texto(OperatingSystem.IsWindows()
            ? F("Pagamento único de {0}, sem assinatura. A mesma chave vale no Android, na TV, no Linux e no Windows.", Pro.Preco)
            : F("Pagamento único de {0}, sem assinatura. A mesma chave vale no Android, na TV e no Linux.", Pro.Preco), 12, FontWeight.Normal, "tenue"));
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
