namespace LibertyX.Player.Core;

/// <summary>Registro de erros em ~/.cache/libertyx-player/diario.log — o que pedir quando algo der errado.</summary>
public static class Diario
{
    private static readonly Lock Trava = new();
    public static string Arquivo => Path.Combine(Caminhos.Cache, "diario.log");

    public static void Escrever(string linha)
    {
        lock (Trava)
        {
            try
            {
                var fi = new FileInfo(Arquivo);
                if (fi.Exists && fi.Length > 1_000_000) File.Move(Arquivo, Arquivo + ".1", true);
                File.AppendAllText(Arquivo, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {linha}\n");
            }
            catch { }
        }
    }

    public static void Erro(Exception? e) { if (e != null) Escrever("ERRO " + e); }
}
