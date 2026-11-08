using LibertyX.Player.Rede;

namespace LibertyX.Player.Core;

/// <summary>
/// As abas da barra de cima além de Início e Ajustes, que estão sempre nela — como a dock do celular:
/// Arquivos, Procurar e cada servidor SMB, WebDAV, Jellyfin ou Emby entram e saem por Ajustes › Barra
/// de abas. Servidor na barra abre direto nele. Cabem três; sem escolha nenhuma ainda, Arquivos e
/// Procurar, que é como o app sempre foi.
/// </summary>
public static class Abas
{
    public const int Maximo = 3;
    public static event Action? Mudou;

    private static List<string> Guardadas => Prefs.Atual.Abas ?? ["arquivos", "procurar"];

    /// <summary>Sem as contas e os servidores que já foram apagados, até o limite.</summary>
    public static List<string> Escolhidas() => Guardadas.Where(a => a switch
    {
        "arquivos" or "procurar" => true,
        _ when a.StartsWith("conta:") => Contas.Achar(a[6..]) != null,
        _ when a.StartsWith("smb:") => Servidores.Achar(a[4..]) != null,
        _ => false,
    }).Take(Maximo).ToList();

    public static void Alternar(string aba)
    {
        var atuais = Escolhidas();
        if (!atuais.Remove(aba)) atuais.Add(aba);
        Prefs.Atual.Abas = atuais;
        Prefs.Atual.Salvar();
        Mudou?.Invoke();
    }
}
