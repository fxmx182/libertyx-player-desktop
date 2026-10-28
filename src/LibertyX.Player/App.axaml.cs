using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LibertyX.Player.Core;
using LibertyX.Player.Mpv;
using LibertyX.Player.Views;

namespace LibertyX.Player;

public partial class App : Application
{
    /// <summary>O mpv do player — um só, a vida inteira do app.</summary>
    public static MpvCore? Mpv { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            if (!LibMpv.Disponivel(out var erro))
            {
                Diario.Escrever("libmpv não encontrada: " + erro);
                d.MainWindow = SemMpv();
            }
            else
            {
                Mpv = new MpvCore(PlayerView.Opcoes());
                PlayerView.Observar(Mpv);
                var w = new MainWindow(Mpv);
                d.MainWindow = w;
                w.Opened += (_, _) => { if (d.Args is { Length: > 0 } a) w.Abrir(a); };
                d.Exit += (_, _) => { Prefs.Atual.Salvar(); };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Sem o libmpv o app não toca nada: diz o que instalar, em vez de quebrar.</summary>
    private static Window SemMpv()
    {
        var cmd = new SelectableTextBlock
        {
            Text = "sudo apt install libmpv2        # Debian, Ubuntu, Mint\nsudo dnf install mpv-libs       # Fedora\nsudo pacman -S mpv              # Arch, Manjaro\nsudo zypper install libmpv2     # openSUSE",
            FontFamily = new FontFamily("monospace"), FontSize = 13,
        };
        return new Window
        {
            Title = "LibertyX Player", Width = 620, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Ui.Pincel("Fundo"), Foreground = Ui.Pincel("Texto"),
            Content = new StackPanel
            {
                Margin = new Thickness(28), Spacing = 14,
                Children =
                {
                    Ui.Pilha(12, Orientation.Horizontal, Ui.Marca(40), Ui.Linha("LibertyX Player", 22, FontWeight.ExtraBold)),
                    Ui.Texto(Textos.S("Falta o motor de vídeo (libmpv) neste computador. Instale com o comando da sua distribuição e abra o app de novo:"), 14),
                    new Border { Classes = { "painel" }, Padding = new Thickness(14), Child = cmd },
                },
            },
        };
    }
}
