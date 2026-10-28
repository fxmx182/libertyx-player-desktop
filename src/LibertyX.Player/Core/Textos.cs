using System.Globalization;
using Avalonia.Markup.Xaml;

namespace LibertyX.Player.Core;

/// <summary>
/// Idiomas — o mesmo esquema do iPhone: o texto em português no código é a chave, e as tabelas de
/// inglês e espanhol (<see cref="Textos.Tabelas"/>) traduzem. Inglês é o padrão de qualquer língua
/// sem tradução. Texto novo entra nas duas tabelas.
/// </summary>
public static partial class Textos
{
    /// <summary>"pt", "en" ou "es".</summary>
    public static string Idioma { get; private set; } = "en";

    public static CultureInfo Cultura { get; private set; } = CultureInfo.InvariantCulture;

    public static readonly (string Codigo, string Nome)[] Opcoes = [("pt", "Português"), ("en", "English"), ("es", "Español")];

    public static void Iniciar(string? escolhido)
    {
        string sistema = (Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } a ? a
            : Environment.GetEnvironmentVariable("LC_MESSAGES") is { Length: > 0 } m ? m
            : Environment.GetEnvironmentVariable("LANG") ?? "").ToLowerInvariant();
        Idioma = escolhido is "pt" or "en" or "es" ? escolhido
            : sistema.StartsWith("pt") ? "pt" : sistema.StartsWith("es") ? "es" : "en";
        string cult = sistema.Length >= 5 && sistema[..2] == Idioma ? sistema[..5].Replace('_', '-') : Idioma switch { "pt" => "pt-BR", "es" => "es-ES", _ => "en-US" };
        try { Cultura = CultureInfo.GetCultureInfo(cult); } catch { Cultura = CultureInfo.GetCultureInfo(Idioma); }
        CultureInfo.DefaultThreadCurrentCulture = Cultura;
        CultureInfo.DefaultThreadCurrentUICulture = Cultura;
        CultureInfo.CurrentCulture = Cultura;
        CultureInfo.CurrentUICulture = Cultura;
    }

    /// <summary>O texto no idioma da pessoa.</summary>
    public static string S(string pt)
    {
        if (Idioma == "pt") return pt;
        var tab = Idioma == "es" ? Es : En;
        return tab.TryGetValue(pt, out var t) ? t : En.TryGetValue(pt, out var e) ? e : pt;
    }

    /// <summary>Com parâmetros: <c>F("Dormir em {0} min", 15)</c>.</summary>
    public static string F(string pt, params object[] args) => string.Format(Cultura, S(pt), args);

    /// <summary>Singular e plural: <c>P(n, "{0} vídeo", "{0} vídeos")</c>.</summary>
    public static string P(long n, string um, string varios) => F(n == 1 ? um : varios, n);

    // ───────── formatos ─────────

    public static string Tempo(double segundos)
    {
        if (double.IsNaN(segundos) || segundos < 0) segundos = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(segundos));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string Tamanho(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i == 0 ? v.ToString("0", Cultura) : v.ToString(v < 10 ? "0.0" : "0", Cultura)) + " " + u[i];
    }

    public static string Data(DateTime d) => d.Year == DateTime.Now.Year
        ? d.ToString(Idioma == "en" ? "MMM d" : "d MMM", Cultura).Replace(".", "")
        : d.ToString("d", Cultura);
}

/// <summary>No XAML: <c>Text="{c:T 'Biblioteca'}"</c>.</summary>
public sealed class T(string texto) : MarkupExtension
{
    public string Texto { get; set; } = texto;
    public override object ProvideValue(IServiceProvider serviceProvider) => Textos.S(Texto);
}
