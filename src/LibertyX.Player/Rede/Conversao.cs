using LibertyX.Player.Core;

namespace LibertyX.Player.Rede;

/// <summary>
/// A qualidade de um Jellyfin ou Emby: o arquivo original, como sempre, ou uma versão mais leve que o
/// próprio servidor converte na hora — para ver fora de casa com a internet fraca. É escolha de cada
/// conta: em casa, pela rede local, o original é melhor em tudo, e a conversão custa processador ao
/// servidor, então só acontece quando alguém pede.
/// </summary>
public static class Qualidade
{
    /// <summary>Em kbit/s; zero é o original.</summary>
    public static readonly int[] Opcoes = [0, 20_000, 10_000, 6_000, 4_000, 2_000, 1_000];

    public static string Nome(int kbps) => kbps == 0 ? Textos.S("Original (sem converter)") : $"{kbps / 1000} Mbps";

    public static int Da(string contaId) => Prefs.Atual.Qualidades.TryGetValue(contaId, out var k) ? k : 0;

    public static void Definir(string contaId, int kbps)
    {
        if (kbps == 0) Prefs.Atual.Qualidades.Remove(contaId); else Prefs.Atual.Qualidades[contaId] = kbps;
        Prefs.Atual.Salvar();
    }
}

/// <summary>
/// As conversões e os canais de TV ao vivo em andamento. O servidor os reconhece pelo número da
/// sessão, que tem de ser o mesmo no endereço do vídeo e nos avisos de reprodução — é o "parou" com
/// esse número que manda ele desligar a conversão.
/// </summary>
public static class Conversoes
{
    private static readonly Dictionary<string, string> Sessoes = [];
    private static readonly Dictionary<string, int?> Audios = [];
    private static readonly Dictionary<string, string> Canais = [];
    private static readonly Dictionary<string, string?> Transmissoes = [];
    private static readonly Lock Trava = new();

    public static string Comecar(string item) { lock (Trava) return Sessoes[item] = Guid.NewGuid().ToString(); }
    public static string? Sessao(string item) { lock (Trava) return Sessoes.GetValueOrDefault(item); }

    public static void Encerrar(string item)
    {
        lock (Trava) { Sessoes.Remove(item); Canais.Remove(item); Transmissoes.Remove(item); }
    }

    /// <summary>Na conversão vai uma faixa de áudio só: a escolhida na página, pelo número do servidor.</summary>
    public static void PedirAudio(string item, int? indice) { lock (Trava) Audios[item] = indice; }
    public static int? Audio(string item) { lock (Trava) return Audios.GetValueOrDefault(item); }

    public static void RegistrarCanal(string item, string url, string sessao, string? transmissao)
    {
        lock (Trava) { Canais[item] = url; Sessoes[item] = sessao; Transmissoes[item] = transmissao; }
    }

    public static string? Canal(string item) { lock (Trava) return Canais.GetValueOrDefault(item); }
    public static string? Transmissao(string item) { lock (Trava) return Transmissoes.GetValueOrDefault(item); }
}

/// <summary>
/// O áudio e a legenda escolhidos na página do vídeo, antes de tocar — como os seletores do app do
/// Jellyfin. A página conhece as faixas pelo que o servidor leu do arquivo; o mpv, pela ordem em que
/// as encontra. As duas listas seguem a ordem do arquivo, então a escolha viaja como posição: "o
/// segundo áudio", "a primeira legenda de dentro". A legenda de fora (o .srt ao lado) viaja pelo
/// número que o servidor dá a ela, que vai no nome do arquivo baixado. Vale uma vez: o player consome
/// a escolha ao abrir aquele vídeo.
/// </summary>
public static class EscolhaDeFaixas
{
    public abstract record Legenda
    {
        /// <summary>Desligada de vez, sem nem a forçada.</summary>
        public sealed record Desligada : Legenda;
        public sealed record Interna(int Posicao) : Legenda;
        public sealed record Externa(int Indice) : Legenda;
    }

    /// <summary><c>Legenda</c> null: a regra de sempre (só a forçada).</summary>
    public sealed record Pedido(int? Audio = null, Legenda? Legenda = null);

    /// <summary>"Continuar" ou "Do início", escolhido na página: o player não pergunta de novo.</summary>
    public enum Inicio { Continuar, DoComeco }

    private static readonly Dictionary<string, Pedido> Pedidos = [];
    private static readonly Dictionary<string, Inicio> Inicios = [];

    public static void Pedir(Pedido p, string chave)
    {
        if (p.Audio == null && p.Legenda == null) Pedidos.Remove(chave); else Pedidos[chave] = p;
    }

    public static Pedido? Consumir(string chave) => Pedidos.Remove(chave, out var p) ? p : null;

    public static void PedirInicio(Inicio i, string chave) => Inicios[chave] = i;

    public static Inicio? ConsumirInicio(string chave) => Inicios.Remove(chave, out var i) ? i : null;
}
