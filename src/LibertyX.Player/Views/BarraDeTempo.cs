using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace LibertyX.Player.Views;

/// <summary>
/// A barra de progresso do player: trilho, o que já está no cache, o que já passou e a bolinha.
/// Arrastar avisa a cada movimento (o player busca o quadro exato debaixo do mouse); passar o
/// mouse por cima avisa onde ele está, para o balão mostrar o tempo.
/// </summary>
public sealed class BarraDeTempo : Control
{
    private double _fracao, _cache;
    private double? _sobre;
    private bool _arrastando;

    public event Action<double>? Arrastou;
    public event Action<double>? Soltou;
    public event Action<double?>? Sobrevoou;

    public bool Arrastando => _arrastando;

    public double Fracao
    {
        get => _fracao;
        set { if (!_arrastando && Math.Abs(value - _fracao) > 1e-5) { _fracao = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
    }

    public double Cache
    {
        get => _cache;
        set { if (Math.Abs(value - _cache) > 1e-3) { _cache = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
    }

    public BarraDeTempo()
    {
        Height = 26;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private double Em(PointerEventArgs e) => Math.Clamp((e.GetPosition(this).X - 8) / Math.Max(1, Bounds.Width - 16), 0, 1);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _arrastando = true;
        e.Pointer.Capture(this);
        _fracao = Em(e);
        InvalidateVisual();
        Arrastou?.Invoke(_fracao);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var f = Em(e);
        if (_arrastando) { _fracao = f; InvalidateVisual(); Arrastou?.Invoke(f); }
        _sobre = f;
        Sobrevoou?.Invoke(f);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_arrastando) return;
        _arrastando = false;
        e.Pointer.Capture(null);
        _fracao = Em(e);
        Soltou?.Invoke(_fracao);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _sobre = null;
        Sobrevoou?.Invoke(null);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width - 16, y = Bounds.Height / 2;
        bool grossa = _arrastando || _sobre != null;
        double h = grossa ? 6 : 4;
        var trilho = new Rect(8, y - h / 2, w, h);
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#40FFFFFF")), null, trilho, h / 2, h / 2);
        if (_cache > _fracao)
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#66FFFFFF")), null, new Rect(8, y - h / 2, w * _cache, h), h / 2, h / 2);
        var ouro = Ui.Pincel("Ouro");
        ctx.DrawRectangle(ouro, null, new Rect(8, y - h / 2, w * _fracao, h), h / 2, h / 2);
        if (_sobre is { } s && !_arrastando)
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#AAFFFFFF")), null, new Rect(8 + w * s - 1, y - h / 2 - 2, 2, h + 4));
        double r = grossa ? 8 : 6;
        ctx.DrawEllipse(ouro, null, new Point(8 + w * _fracao, y), r, r);
    }
}
