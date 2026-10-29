using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace LibertyX.Player.Core;

/// <summary>
/// Atualização ao abrir o app (o mesmo caminho do Partition): o CI publica um versao.txt junto de cada
/// versão; o app compara com o próprio número de build. No Windows instalado, baixa o instalador e
/// roda por cima em silêncio — o AppId fixo do Inno troca a versão mantendo ajustes, chave e histórico,
/// e o /UPDATE=1 faz o instalador reabrir o app. AppImage: baixa o novo do lado e troca o arquivo.
/// Portátil (zip), .deb, .rpm e .tar.gz: o navegador baixa a nova.
/// </summary>
public static class Atualizacao
{
    public const string Repo = "fxmx182/libertyx-player-desktop";

    /// <summary>Windows e Linux publicam em releases separados, cada um com a sua numeração.</summary>
    private static string Base => OperatingSystem.IsWindows()
        ? $"https://github.com/{Repo}/releases/download/windows-latest/"
        : $"https://github.com/{Repo}/releases/latest/download/";

    public static string Pagina => OperatingSystem.IsWindows()
        ? $"https://github.com/{Repo}/releases/tag/windows-latest"
        : $"https://github.com/{Repo}/releases/latest";

    public sealed record Info(int Build, string Versao, string Arquivo, long Tamanho, string Novidades);

    /// <summary>O CI carimba 1.0.N; o N é o número do build. Zero = build local, nunca se atualiza.
    /// LIBERTYX_BUILD finge outro número (para testar a atualização de ponta a ponta).</summary>
    public static int BuildAtual => int.TryParse(Environment.GetEnvironmentVariable("LIBERTYX_BUILD"), out int b) ? b
        : System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.Build ?? 0;

    /// <summary>Atualiza sozinho: Windows rodando da pasta do instalador (o desinstalador do Inno fica do
    /// lado), ou Linux rodando de um AppImage numa pasta onde dá para escrever.</summary>
    public static bool Instalado => _instalado.Value;
    private static readonly Lazy<bool> _instalado = new(() =>
    {
        if (OperatingSystem.IsWindows())
        {
            try { return File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe")); } catch { return false; }
        }
        if (AppImage is not { } ai) return false;
        try
        {
            string teste = ai + ".teste";
            File.WriteAllBytes(teste, []);
            File.Delete(teste);
            return true;
        }
        catch { return false; }
    });

    /// <summary>O arquivo .AppImage de onde o app está rodando (o runtime do AppImage diz em $APPIMAGE).</summary>
    public static string? AppImage => !OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } ai && File.Exists(ai) ? ai : null;

    private static bool Arm => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    private static HttpClient Http(TimeSpan prazo)
    {
        var h = new HttpClient { Timeout = prazo };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LibertyXPlayer", BuildAtual.ToString()));
        return h;
    }

    /// <summary>A versão publicada para este sistema, ou nulo (sem rede, release no meio da publicação…).</summary>
    public static async Task<Info?> Conferir(CancellationToken ct = default)
    {
        try
        {
            using var http = Http(TimeSpan.FromSeconds(20));
            string txt = await http.GetStringAsync(Base + "versao.txt", ct);
            var kv = txt.Split('\n').Select(l => l.Split('=', 2)).Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim()).ToDictionary(g => g.Key, g => g.First()[1].Trim());
            if (!kv.TryGetValue("versao", out var v) || !v.StartsWith("1.0.") || !int.TryParse(v[4..], out int build)) return null;
            // cada sistema tem a sua numeração: nunca oferecer a do outro
            string sistema = OperatingSystem.IsWindows() ? "windows" : "linux";
            if (kv.TryGetValue("sistema", out var so) && so != sistema) return null;
            string chave = Arm ? "_arm64" : "";
            string arquivo = OperatingSystem.IsWindows()
                ? kv.GetValueOrDefault("setup" + chave, Arm ? "LibertyXPlayer-Setup-arm64.exe" : "LibertyXPlayer-Setup.exe")
                : AppImage != null ? kv.GetValueOrDefault("appimage" + chave, "") : "";
            long.TryParse(kv.GetValueOrDefault((OperatingSystem.IsWindows() ? "tamanho" : "tamanho_appimage") + chave), out long tam);
            return new Info(build, v, arquivo, tam, kv.GetValueOrDefault("novidades", ""));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { return null; }
    }

    public static bool Nova(Info i) => (BuildAtual > 0 || Environment.GetEnvironmentVariable("LIBERTYX_BUILD") != null) && i.Build > BuildAtual;

    /// <summary>Só quando <see cref="Instalado"/>: baixa o instalador para a pasta temporária (Windows) ou o
    /// AppImage novo para o lado do atual (Linux — a troca depois é um rename na mesma pasta) e confere o tamanho.</summary>
    public static async Task<string> Baixar(Info i, IProgress<double> progresso, CancellationToken ct)
    {
        using var http = Http(TimeSpan.FromMinutes(20));
        string destino = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), $"LibertyXPlayer-Setup-{i.Versao}{(Arm ? "-arm64" : "")}.exe")
            : AppImage + ".novo";
        using (var resp = await http.GetAsync(Base + i.Arquivo, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? i.Tamanho;
            await using var origem = await resp.Content.ReadAsStreamAsync(ct);
            await using var saida = File.Create(destino);
            var buf = new byte[1 << 16];
            long feito = 0;
            int n;
            while ((n = await origem.ReadAsync(buf, ct)) > 0)
            {
                await saida.WriteAsync(buf.AsMemory(0, n), ct);
                feito += n;
                if (total > 0) progresso.Report((double)feito / total);
            }
        }
        if (i.Tamanho > 0 && new FileInfo(destino).Length != i.Tamanho) throw new IOException(Textos.S("O download veio incompleto — tente de novo."));
        return destino;
    }

    /// <summary>Windows: roda o instalador por cima (com a janelinha de progresso); ele reabre o app no fim.
    /// AppImage: troca o arquivo (rename atômico — esta cópia continua rodando do arquivo antigo até
    /// fechar) e abre o novo.</summary>
    public static void Instalar(string baixado)
    {
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(baixado,
                "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1") { UseShellExecute = true });
            return;
        }
        string alvo = AppImage!;
        File.SetUnixFileMode(baixado, File.GetUnixFileMode(alvo) | UnixFileMode.UserExecute | UnixFileMode.UserRead);
        File.Move(baixado, alvo, true);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(alvo) { UseShellExecute = false });
    }
}
