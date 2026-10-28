using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace LibertyX.Player.Views;

/// <summary>Uma janelinha dentro da janela (fundo escurecido, painel no meio). Uma de cada vez.</summary>
public static class Dialogo
{
    private static Panel? _camada;

    public static void Iniciar(Panel camada)
    {
        _camada = camada;
        camada.IsVisible = false;
        camada.Background = new SolidColorBrush(Color.Parse("#B3000000"));
        camada.PointerPressed += (_, e) => { if (e.Source == camada) Fechar(); };
    }

    public static bool Aberto => _camada?.IsVisible == true;

    public static void Mostrar(Control conteudo, double largura = 460)
    {
        if (_camada == null) return;
        _camada.Children.Clear();
        _camada.Children.Add(new Border
        {
            Classes = { "painel" }, Width = largura, MaxHeight = 640, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(24),
            BoxShadow = BoxShadows.Parse("0 20 60 0 #A0000000"),
            Child = new ScrollViewer { Content = conteudo },
        });
        _camada.IsVisible = true;
    }

    public static void Fechar()
    {
        if (_camada == null) return;
        _camada.IsVisible = false;
        _camada.Children.Clear();
    }

    /// <summary>Esc fecha.</summary>
    public static bool Tecla(KeyEventArgs e)
    {
        if (!Aberto || e.Key != Key.Escape) return false;
        Fechar();
        return true;
    }

    /// <summary>Pergunta com um campo de texto (endereço de vídeo, por exemplo).</summary>
    public static void Pedir(string titulo, string texto, string marca, string botao, Func<string, string?> ok)
    {
        var campo = new TextBox { Watermark = marca };
        var erro = Ui.Texto("", 13); erro.Foreground = Ui.Pincel("Vermelho"); erro.IsVisible = false;
        void Confirmar()
        {
            var r = ok(campo.Text?.Trim() ?? "");
            if (r == null) Fechar(); else { erro.Text = r; erro.IsVisible = true; }
        }
        campo.KeyDown += (_, e) => { if (e.Key == Key.Enter) Confirmar(); };
        var botoes = Ui.Pilha(10, Orientation.Horizontal, Ui.Botao(Core.Textos.S("Cancelar"), null, "vidro", Fechar), Ui.Botao(botao, null, "ouro", Confirmar));
        botoes.HorizontalAlignment = HorizontalAlignment.Right;
        Mostrar(Ui.Pilha(14, Orientation.Vertical, Ui.Texto(titulo, 20, FontWeight.ExtraBold), Ui.Texto(texto, 14, FontWeight.Normal, "apagado"), campo, erro, botoes), 520);
        campo.AttachedToVisualTree += (_, _) => campo.Focus();
    }
}
