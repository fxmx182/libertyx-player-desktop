using LibertyX.Player.Rede;

namespace LibertyX.Player.Core;

/// <summary>Um vídeo do histórico, com a origem inteira — a senha não vem junto: o endereço só aponta para o servidor salvo.</summary>
public sealed class ItemDoHistorico
{
    public string Endereco { get; set; } = "";
    public string Titulo { get; set; } = "";
    public Origem Origem { get; set; }
    public string? Pasta { get; set; }
    public long Tamanho { get; set; }
    public DateTime Data { get; set; }
    public double? Duracao { get; set; }

    public Midia ParaMidia() => new() { Endereco = Endereco, Titulo = Titulo, Origem = Origem, Pasta = Pasta, Tamanho = Tamanho, Data = Data, Duracao = Duracao };

    public static ItemDoHistorico De(Midia m) => new()
    {
        Endereco = m.Endereco, Titulo = m.Titulo, Origem = m.Origem, Pasta = m.Pasta, Tamanho = m.Tamanho, Data = m.Data, Duracao = m.Duracao,
    };
}

/// <summary>
/// Os vídeos abertos por último, de qualquer origem — como no Android e no iPhone. O "continuar
/// assistindo" vinha só das marcas de retomada; aqui fica o próprio vídeo, e tocar de novo pelo
/// histórico funciona igual para o computador, SMB, WebDAV e Jellyfin.
/// </summary>
public static class Historico
{
    private const int Limite = 40;
    public static event Action? Mudou;

    private static List<ItemDoHistorico> Lista
    {
        get
        {
            if (Prefs.Atual.Historico is { } l) return l;
            // 1ª vez: começa pelo "continuar assistindo" de antes, para ninguém perder o que estava vendo
            l = [];
            foreach (var (end, r) in Prefs.Atual.Posicoes.OrderByDescending(p => p.Value.Quando).Take(Limite))
            {
                var origem = end.StartsWith("lxsmb://") ? Origem.Smb : end.Contains("://") ? Origem.Url : Origem.Arquivo;
                l.Add(new ItemDoHistorico
                {
                    Endereco = end, Origem = origem, Duracao = r.Duracao > 0 ? r.Duracao : null,
                    Titulo = r.Titulo ?? Path.GetFileNameWithoutExtension(origem == Origem.Arquivo ? end : Uri.UnescapeDataString(end)),
                    Pasta = origem == Origem.Arquivo ? Path.GetFileName(Path.GetDirectoryName(end)) : ConexaoSmb.Partes(end) is { } p ? Servidores.Achar(p.id)?.Nome : null,
                });
            }
            Prefs.Atual.Historico = l;
            return l;
        }
    }

    /// <summary>O que ainda dá para abrir: arquivo que existe, servidor que continua salvo.</summary>
    public static List<Midia> Itens() => Lista.Where(Disponivel).Select(i => i.ParaMidia()).ToList();

    private static bool Disponivel(ItemDoHistorico i) => i.Origem switch
    {
        Origem.Arquivo => File.Exists(i.Endereco),
        Origem.Smb => ConexaoSmb.Partes(i.Endereco) is { } p && Servidores.Achar(p.id) != null,
        Origem.WebDav or Origem.Servidor => Contas.Partes(i.Endereco) != null,
        _ => true,
    };

    /// <summary>Vai para o começo; o mesmo vídeo não aparece duas vezes.</summary>
    public static void Registrar(Midia m)
    {
        if (m.Endereco == Views.Demonstracao.Midia.Endereco) return;
        var l = Lista;
        l.RemoveAll(i => i.Endereco == m.Endereco);
        l.Insert(0, ItemDoHistorico.De(m));
        if (l.Count > Limite) l.RemoveRange(Limite, l.Count - Limite);
        Gravar();
    }

    /// <summary>Visto até o fim: sai, e o próximo episódio, se houver, entra no lugar — como o "A seguir" do Jellyfin.</summary>
    public static void Terminou(string chave, Midia? proximo)
    {
        var l = Lista;
        int lugar = l.FindIndex(i => i.Endereco == chave);
        l.RemoveAll(i => i.Endereco == chave);
        if (proximo != null)
        {
            l.RemoveAll(i => i.Endereco == proximo.Endereco);
            l.Insert(Math.Clamp(lugar, 0, l.Count), ItemDoHistorico.De(proximo));
        }
        Gravar();
    }

    public static void Remover(string chave) { Lista.RemoveAll(i => i.Endereco == chave); Gravar(); }

    public static void Limpar() { Lista.Clear(); Gravar(); }

    private static void Gravar() { Prefs.Atual.Salvar(); Mudou?.Invoke(); }

    /// <summary>Assistido: a partir de 90% ou no último segundo (a regra do Jellyfin) — os créditos não contam.</summary>
    public static bool Visto(double posicao, double duracao) => duracao > 0 && (posicao >= duracao * 0.9 || posicao >= duracao - 1);
}
