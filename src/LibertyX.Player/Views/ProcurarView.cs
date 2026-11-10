using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibertyX.Player.Core;
using LibertyX.Player.Rede;
using static LibertyX.Player.Core.Textos;

namespace LibertyX.Player.Views;

/// <summary>
/// A aba Procurar, como no celular: pelo nome, nos vídeos do computador e nos Jellyfin e Emby. SMB e
/// WebDAV ficam de fora — não têm busca, e procurar neles seria descer pasta por pasta do servidor
/// inteiro a cada letra digitada.
/// </summary>
public sealed class ProcurarView : DockPanel
{
    private readonly Action<List<Midia>, int> _tocar;
    private readonly Func<List<PastaDeVideos>> _pastas;
    private readonly Action<Conta, ServidorDeMidia.Item> _abrirNoServidor;
    private readonly TextBox _campo;
    private readonly StackPanel _corpo = new() { Spacing = 14, Margin = new Thickness(28, 6, 28, 28) };
    private CancellationTokenSource? _cts;
    private string _termo = "";

    public ProcurarView(Action<List<Midia>, int> tocar, Func<List<PastaDeVideos>> pastas, Action<Conta, ServidorDeMidia.Item> abrirNoServidor)
    {
        _tocar = tocar; _pastas = pastas; _abrirNoServidor = abrirNoServidor;
        _campo = new TextBox
        {
            Watermark = S("Filme, episódio, nome do arquivo…"), FontSize = 16, MinHeight = 44,
            InnerLeftContent = Ui.Icone(Icones.Buscar, 18, Ui.Pincel("Apagado")), VerticalContentAlignment = VerticalAlignment.Center,
        };
        ((PathIcon)_campo.InnerLeftContent).Margin = new Thickness(12, 0, 4, 0);
        _campo.TextChanged += (_, _) => Buscar();
        _campo.KeyDown += (_, e) => { if (e.Key == Key.Escape && _campo.Text?.Length > 0) { _campo.Text = ""; e.Handled = true; } };
        var cab = Ui.Pilha(8, Orientation.Vertical,
            Ui.Linha(S("Procurar"), 26, FontWeight.ExtraBold),
            Ui.Texto(S("Vídeos do computador e dos servidores Jellyfin e Emby"), 13, FontWeight.Normal, "apagado"),
            Ui.Limitar(_campo, 560));
        cab.Margin = new Thickness(28, 18, 28, 12);
        _campo.Margin = new Thickness(0, 6, 0, 0);
        SetDock(cab, Dock.Top);
        Children.Add(cab);
        Children.Add(new ScrollViewer { Content = _corpo });
    }

    /// <summary>O botão da dock: a busca começa vazia, como no celular.</summary>
    public void Raiz()
    {
        _campo.Text = "";
        Dispatcher.UIThread.Post(() => _campo.Focus(), DispatcherPriority.Input);
    }

    /// <summary>De volta do player: o progresso das capas muda.</summary>
    public void Atualizar() { if (_termo.Length > 0) Buscar(true); }

    private static readonly CompareInfo Comparar = CultureInfo.InvariantCulture.CompareInfo;

    private void Buscar(bool mesmoTermo = false)
    {
        var termo = _campo.Text?.Trim() ?? "";
        if (!mesmoTermo && termo == _termo) return;
        _termo = termo;
        _cts?.Cancel();
        _corpo.Children.Clear();
        if (termo.Length == 0) return;

        // "Ação" acha "acao": ninguém digita acento para procurar
        var locais = _pastas().SelectMany(p => p.Videos)
            .Where(v => Comparar.IndexOf(v.Titulo, termo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .Take(100).ToList();
        if (locais.Count > 0)
        {
            _corpo.Children.Add(Ui.Pilha(8, Orientation.Horizontal, Ui.Texto(S("Neste computador"), 17, FontWeight.Bold), Ui.Linha(locais.Count.ToString(Textos.Cultura), 13, FontWeight.Medium, "apagado")));
            _corpo.Children.Add(Cartoes.Videos(Biblioteca.Ordenar(locais, Prefs.Atual.Ordem).ToList(), _tocar));
        }

        var alvos = Pro.Ativo ? Contas.Lista.Where(c => c.EhServidorDeMidia).ToList() : [];
        if (termo.Length < 2 || alvos.Count == 0) { SemNada(locais.Count); return; }
        var procurando = Ui.Pilha(10, Orientation.Horizontal, new ProgressBar { IsIndeterminate = true, Width = 60, VerticalAlignment = VerticalAlignment.Center },
            Ui.Linha(S("Procurando nos servidores…"), 13, FontWeight.Medium, "apagado"));
        _corpo.Children.Add(procurando);
        var cts = _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            // espera a pessoa parar de digitar: uma consulta por letra só ocuparia o servidor
            await Task.Delay(450, cts.Token);
            var achados = new List<(ServidorDeMidia s, List<ServidorDeMidia.Item> itens)>();
            foreach (var c in alvos)
            {
                var s = new ServidorDeMidia(c);
                try { if (await s.Buscar(termo, cts.Token) is { Count: > 0 } l) achados.Add((s, l)); } catch { }
            }
            return achados;
        }, cts.Token).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (cts.IsCancellationRequested || !t.IsCompletedSuccessfully) return;
            _corpo.Children.Remove(procurando);
            foreach (var (s, itens) in t.Result)
            {
                _corpo.Children.Add(Ui.Pilha(8, Orientation.Horizontal, Ui.Texto(s.Conta.Nome, 17, FontWeight.Bold), Ui.Linha(itens.Count.ToString(Textos.Cultura), 13, FontWeight.Medium, "apagado")));
                var midias = itens.Select(s.ParaTocar).ToList();
                // filme, episódio e série abrem a página deles no servidor, como no celular
                _corpo.Children.Add(Cartoes.Videos(midias, (_, k) => _abrirNoServidor(s.Conta, itens[k])));
            }
            SemNada(locais.Count + t.Result.Sum(r => r.itens.Count));
        }));
    }

    private void SemNada(int achados)
    {
        if (achados == 0) _corpo.Children.Add(Ui.Texto(F("Nada encontrado para \"{0}\".", _termo), 14, FontWeight.Medium, "apagado"));
    }
}
