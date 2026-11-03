using LibertyX.Player.Core;

namespace LibertyX.Player.Rede;

/// <summary>
/// As legendas que moram ao lado do vídeo, em arquivo próprio (.srt, .ass), num servidor — o mesmo
/// do Android e do iPhone. O app lista a pasta do vídeo (ou pede ao Jellyfin as legendas "externas"
/// dele), copia as que combinam para o cache — são poucos KB — e o player as entrega ao mpv como
/// arquivo local. Combina o que começa com o nome do vídeo, como no Jellyfin e no Plex:
/// Filme.srt, Filme.pt.srt, Filme.pt.forced.srt.
/// </summary>
public static class LegendasAoLado
{
    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase) { ".srt", ".ass", ".ssa", ".vtt" };

    /// <summary>Legenda bem maior que isto não é legenda: não vale baixar.</summary>
    private const long Teto = 2L * 1024 * 1024;

    public static bool Combina(string video, string arquivo)
    {
        if (!Extensoes.Contains(Path.GetExtension(arquivo))) return false;
        var b = Path.GetFileNameWithoutExtension(video);
        return b.Length > 0 && arquivo.StartsWith(b + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Os arquivos já no cache, prontos para o mpv. Nunca lança: sem rede ou sem legenda, o vídeo toca igual.</summary>
    public static async Task<List<string>> Buscar(Midia m)
    {
        var raiz = Path.Combine(Caminhos.Cache, "legendas");
        // só as do vídeo da vez: as dos anteriores não servem mais
        try { if (Directory.Exists(raiz)) Directory.Delete(raiz, true); } catch { }
        var destino = Directory.CreateDirectory(Path.Combine(raiz, Convert.ToHexString(BitConverter.GetBytes(m.Endereco.GetHashCode())))).FullName;
        try
        {
            var achadas = m.Origem switch
            {
                Origem.Smb => DoSmb(m.Endereco),
                Origem.WebDav => await DoWebDav(m.Endereco),
                Origem.Servidor => await DoServidor(m.Endereco),
                _ => [],
            };
            var saida = new List<string>();
            foreach (var (nome, dados) in achadas)
            {
                var f = Path.Combine(destino, string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));
                await File.WriteAllBytesAsync(f, dados);
                saida.Add(f);
            }
            return saida;
        }
        catch (Exception e) { Diario.Escrever("legendas ao lado: " + e.Message); return []; }
    }

    private static List<(string, byte[])> DoSmb(string endereco)
    {
        if (ConexaoSmb.Partes(endereco) is not { } p || Servidores.Achar(p.id) is not { } srv) return [];
        var video = p.caminho[(p.caminho.LastIndexOf('/') + 1)..];
        var pasta = p.caminho.Contains('/') ? p.caminho[..p.caminho.LastIndexOf('/')] : "";
        using var con = ConexaoSmb.Abrir(srv);
        var saida = new List<(string, byte[])>();
        foreach (var i in con.Listar(p.comp, pasta, tudo: true).Where(i => !i.Pasta && i.Tamanho <= Teto && Combina(video, i.Nome)))
            if (con.Ler(p.comp, i.Caminho, Teto) is { } dados) saida.Add((i.Nome, dados));
        return saida;
    }

    private static async Task<List<(string, byte[])>> DoWebDav(string endereco)
    {
        if (Contas.Partes(endereco) is not { } p) return [];
        var dav = new WebDav(p.conta);
        var caminho = p.resto.TrimEnd('/');
        var video = caminho[(caminho.LastIndexOf('/') + 1)..];
        var pasta = caminho.LastIndexOf('/') > 0 ? caminho[..caminho.LastIndexOf('/')] : "/";
        var saida = new List<(string, byte[])>();
        foreach (var e in (await dav.Listar(pasta)).Where(e => !e.Pasta && e.Tamanho <= Teto && Combina(video, e.Nome)))
        {
            try { saida.Add((e.Nome, await dav.Baixar(e.Caminho))); } catch { }
        }
        return saida;
    }

    private static async Task<List<(string, byte[])>> DoServidor(string endereco)
    {
        if (Contas.Partes(endereco) is not { } p) return [];
        return (await new ServidorDeMidia(p.conta).LegendasExternas(p.resto))
            .Select(l => (l.nome, System.Text.Encoding.UTF8.GetBytes(l.texto))).ToList();
    }
}
