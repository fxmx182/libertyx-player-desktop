using System.Globalization;
using System.Runtime.InteropServices;

namespace LibertyX.Player.Core;

/// <summary>
/// Tamanho da interface (o mesmo raciocínio do Partition Linux): o Avalonia só aumenta a janela se
/// a área de trabalho disser (variáveis do GTK/Qt ou Xft.dpi); num monitor 4K com o desktop em 100%
/// tudo sai minúsculo. A escala vem, em ordem: da escolha em Ajustes, das variáveis, do Xft.dpi e,
/// por último, do tamanho físico do monitor. Decidida antes de o Avalonia abrir.
/// </summary>
public static class Escala
{
    public static readonly double[] Opcoes = [1, 1.25, 1.5, 1.75, 2, 2.5];
    public static double? Auto { get; private set; }

    public static string Pct(double s) => $"{Math.Round(s * 100):0}%";

    public static void Aplicar(double? escolhida)
    {
        if (Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR") is { Length: > 0 } g && Parse(g) is { } gv) Auto = gv;
        else Auto = Detectar();
        double s = escolhida is >= 0.5 and <= 4 ? escolhida.Value : Auto ?? 1;
        if (Math.Abs(s - 1) > 0.01) Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR", s.ToString(CultureInfo.InvariantCulture));
    }

    private static double Detectar()
    {
        double gdk = (int.TryParse(Environment.GetEnvironmentVariable("GDK_SCALE"), out var gi) && gi > 0 ? gi : 1)
            * (Parse(Environment.GetEnvironmentVariable("GDK_DPI_SCALE")) is { } gd and > 0 ? gd : 1);
        if (gdk > 1.01) return gdk;
        if (Parse(Environment.GetEnvironmentVariable("QT_SCALE_FACTOR")) is { } q and > 1.01) return q;
        IntPtr dpy = IntPtr.Zero;
        try
        {
            dpy = XOpenDisplay(IntPtr.Zero);
            if (dpy == IntPtr.Zero) return 1;
            if (XftDpi(dpy) is { } dpi && dpi > 100) return Math.Round(dpi / 96.0 / 0.05) * 0.05;
            return Monitores(dpy).Select(Fisico).DefaultIfEmpty(1).Max();
        }
        catch { return 1; }
        finally { if (dpy != IntPtr.Zero) XCloseDisplay(dpy); }
    }

    /// <summary>4K de 27″ (163 dpi) → 150%; 1440p de 27″ → 100%. O 0,88 é o que o Windows recomenda.</summary>
    private static double Fisico((int w, int h, int mw, int mh) m)
    {
        if (m.mw < 200 || m.mh < 110 || m.w <= 0) return 1;
        if (Math.Abs((double)m.w / m.h / ((double)m.mw / m.mh) - 1) > 0.2) return 1;
        double dpi = m.w * 25.4 / m.mw;
        if (dpi < 125) return 1;
        return Math.Clamp(Math.Round(dpi / 96.0 * 0.88 / 0.25) * 0.25, 1, 3);
    }

    private static double? Parse(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? XftDpi(IntPtr dpy)
    {
        var p = XResourceManagerString(dpy);
        if (p == IntPtr.Zero) return null;
        foreach (var line in (Marshal.PtrToStringUTF8(p) ?? "").Split('\n'))
        {
            var kv = line.Split(':', 2);
            if (kv.Length == 2 && kv[0].Trim() == "Xft.dpi" && Parse(kv[1].Trim()) is { } d && d > 0) return d;
        }
        return null;
    }

    private static List<(int, int, int, int)> Monitores(IntPtr dpy)
    {
        var l = new List<(int, int, int, int)>();
        var arr = XRRGetMonitors(dpy, XDefaultRootWindow(dpy), 1, out int n);
        if (arr == IntPtr.Zero) return l;
        try
        {
            int size = Marshal.SizeOf<XRRMonitorInfo>();
            for (int i = 0; i < n; i++)
            {
                var m = Marshal.PtrToStructure<XRRMonitorInfo>(arr + i * size);
                l.Add((m.width, m.height, m.mwidth, m.mheight));
            }
        }
        finally { XRRFreeMonitors(arr); }
        return l;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XRRMonitorInfo
    {
        public nuint name;
        public int primary, automatic, noutput, x, y, width, height, mwidth, mheight;
        public IntPtr outputs;
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr dpy);
    [DllImport("libX11.so.6")] private static extern IntPtr XResourceManagerString(IntPtr dpy);
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(IntPtr dpy);
    [DllImport("libXrandr.so.2")] private static extern IntPtr XRRGetMonitors(IntPtr dpy, nuint window, int getActive, out int nmonitors);
    [DllImport("libXrandr.so.2")] private static extern void XRRFreeMonitors(IntPtr monitors);
}
