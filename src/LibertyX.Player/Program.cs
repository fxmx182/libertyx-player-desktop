using Avalonia;
using LibertyX.Player.Core;

namespace LibertyX.Player;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Diario.Erro(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Diario.Erro(e.Exception); e.SetObserved(); };
        if (args.Contains("--version")) { Console.WriteLine("LibertyX Player " + Views.AjustesView.Versao()); return; }
        int at = Array.IndexOf(args, "--autoteste");
        if (at >= 0)
        {
            Environment.Exit(Autoteste.Rodar(at + 1 < args.Length ? args[at + 1] : "autoteste.txt", at + 2 < args.Length ? args[at + 2] : null));
            return;
        }
        Textos.Iniciar(Prefs.Atual.Idioma);
        Escala.Aplicar(Prefs.Atual.Escala);
        Pro.Iniciar();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var b = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                WmClass = "libertyx-player",
                // LIBERTYX_LLVMPIPE=1: aceita o OpenGL por software do Mesa (testes no Xvfb); o Avalonia o recusa por padrão
                GlxRendererBlacklist = Environment.GetEnvironmentVariable("LIBERTYX_LLVMPIPE") == "1" ? [] : ["llvmpipe"],
            });
        // LIBERTYX_LOG=1: o diário do Avalonia no terminal (escolha de OpenGL, janelas)
        return Environment.GetEnvironmentVariable("LIBERTYX_LOG") == "1"
            ? b.LogToTextWriter(Console.Out, Avalonia.Logging.LogEventLevel.Debug)
            : b.LogToTrace();
    }
}
