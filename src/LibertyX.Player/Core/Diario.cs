using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LibertyX.Player.Core;

/// <summary>
/// O registro de erros que a pessoa copia em Ajustes › Registro de erros e cola na conversa de
/// suporte, como no iPhone: o que o player fez (abrir, falhar), os avisos e erros do mpv, as exceções
/// que escaparam e o aviso de que a sessão anterior terminou de repente. Só o fim interessa — o que
/// veio logo antes do problema —, então o arquivo é cortado ao passar do teto. Senhas, tokens e
/// chaves de acesso saem apagados antes de gravar.
/// </summary>
public static partial class Diario
{
    private static readonly Lock Trava = new();
    public static string Arquivo => Path.Combine(Caminhos.Cache, "diario.log");
    private static string Marca => Path.Combine(Caminhos.Cache, "sessao-aberta");

    /// <summary>Acima disto o arquivo é cortado e fica só a metade mais nova.</summary>
    private const long Teto = 512 * 1024;

    /// <summary>O mpv manda o mesmo aviso centenas de vezes; uma linha com a conta basta.</summary>
    private static string? _ultima;
    private static int _repeticoes;

    [GeneratedRegex(@"(?i)\b(api_?key|token|access_?token|pw|password|senha)(=|"":\s*""|="")[^&\s""',;]+")]
    private static partial Regex Segredo();
    [GeneratedRegex(@"(?i)(authorization:\s*\w+)\s+\S+")]
    private static partial Regex Autorizacao();

    /// <summary>Ao abrir o app: o cabeçalho da sessão e o aviso se a anterior não fechou direito.</summary>
    public static void Iniciar(string versao)
    {
        Aparar();
        if (File.Exists(Marca)) Escrever("⚠️ A sessão anterior terminou de repente, com o app aberto (travamento, falta de memória ou o computador desligou).");
        try { File.WriteAllText(Marca, ""); } catch { }
        Escrever($"── App aberto · LibertyX Player {versao} · {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture} · .NET {Environment.Version}");
    }

    /// <summary>O app fechou do jeito certo: a próxima abertura não acusa queda.</summary>
    public static void Encerrar()
    {
        Escrever("── App fechado");
        try { File.Delete(Marca); } catch { }
    }

    public static void Escrever(string linha)
    {
        linha = Autorizacao().Replace(Segredo().Replace(linha, "$1$2***"), "$1 ***");
        lock (Trava)
        {
            try
            {
                if (linha == _ultima) { _repeticoes++; return; }
                var texto = "";
                if (_repeticoes > 0) texto += $"{DateTime.Now:dd/MM HH:mm:ss.fff}   (a linha de cima se repetiu mais {_repeticoes}×)\n";
                _ultima = linha; _repeticoes = 0;
                File.AppendAllText(Arquivo, texto + $"{DateTime.Now:dd/MM HH:mm:ss.fff} {linha}\n");
            }
            catch { }
        }
    }

    public static void Erro(Exception? e) { if (e != null) Escrever("💥 " + e); }

    /// <summary>O registro inteiro (já cortado no teto), do mais velho ao mais novo.</summary>
    public static string Texto()
    {
        lock (Trava)
        {
            try { return File.Exists(Arquivo) ? File.ReadAllText(Arquivo) : ""; }
            catch { return ""; }
        }
    }

    public static void Limpar()
    {
        lock (Trava)
        {
            try { File.Delete(Arquivo); File.Delete(Arquivo + ".1"); } catch { }
            _ultima = null; _repeticoes = 0;
        }
    }

    /// <summary>Mantém só a metade mais nova quando o arquivo passa do teto, começando numa linha inteira.</summary>
    private static void Aparar()
    {
        lock (Trava)
        {
            try
            {
                var fi = new FileInfo(Arquivo);
                if (!fi.Exists || fi.Length <= Teto) return;
                var texto = File.ReadAllText(Arquivo);
                var resto = texto[^(int)(Teto / 2)..];
                int quebra = resto.IndexOf('\n');
                File.WriteAllText(Arquivo, quebra >= 0 ? resto[(quebra + 1)..] : resto);
            }
            catch { }
        }
    }
}
