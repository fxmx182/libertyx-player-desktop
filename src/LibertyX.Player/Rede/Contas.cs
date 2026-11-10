using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibertyX.Player.Core;

namespace LibertyX.Player.Rede;

public enum TipoDeConta { WebDav, Jellyfin, Emby }

/// <summary>
/// Uma conta de servidor que fala HTTP: WebDAV (o Nextcloud entre eles), Jellyfin ou Emby — o mesmo
/// cadastro do Android e do iPhone. O SMB continua com o próprio (<see cref="Servidor"/>): ele tem
/// compartilhamento, convidado e procura na rede, que nada disto usa. Aqui o endereço é sempre uma
/// URL completa.
/// </summary>
public sealed class Conta
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public TipoDeConta Tipo { get; set; }
    public string Nome { get; set; } = "";
    /// <summary>Já com http(s)://.</summary>
    public string Endereco { get; set; } = "";
    public string Usuario { get; set; } = "";
    /// <summary>A senha (WebDAV) ou o token de acesso (Jellyfin/Emby: a senha não fica guardada).</summary>
    public string Segredo { get; set; } = "";
    /// <summary>Jellyfin/Emby: o id do usuário no servidor, que vem do login.</summary>
    public string? IdNoServidor { get; set; }

    /// <summary>Jellyfin e Emby são o mesmo servidor por dentro (o Jellyfin nasceu de uma cópia do Emby).</summary>
    [JsonIgnore] public bool EhServidorDeMidia => Tipo != TipoDeConta.WebDav;
    [JsonIgnore] public string NomeDoTipo => Tipo switch { TipoDeConta.WebDav => "WebDAV", TipoDeConta.Jellyfin => "Jellyfin", _ => "Emby" };
    [JsonIgnore] public string EnderecoCurto => Endereco.Replace("https://", "").Replace("http://", "").Trim('/');
}

/// <summary>
/// As contas salvas, em contas.json só com permissão do dono — como os servidores SMB. E o que liga os
/// endereços do app (<c>lxdav://</c>, <c>lxmidia://</c>) ao que o mpv abre de verdade.
/// </summary>
public static class Contas
{
    private static string Arquivo => Path.Combine(Caminhos.Config, "contas.json");
    private static List<Conta>? _lista;
    public static event Action? Mudou;

    public static List<Conta> Lista
    {
        get
        {
            if (_lista != null) return _lista;
            try { _lista = File.Exists(Arquivo) ? JsonSerializer.Deserialize<List<Conta>>(File.ReadAllText(Arquivo), Json) ?? [] : []; }
            catch { _lista = []; }
            return _lista;
        }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Conta? Achar(string id) => Lista.FirstOrDefault(c => c.Id == id);

    public static void Salvar(Conta c)
    {
        var i = Lista.FindIndex(x => x.Id == c.Id);
        if (i >= 0) Lista[i] = c; else Lista.Add(c);
        Gravar();
    }

    public static void Remover(Conta c) { Lista.RemoveAll(x => x.Id == c.Id); Gravar(); }

    private static void Gravar()
    {
        try
        {
            var tmp = Arquivo + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Lista, Json));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, Arquivo, true);
        }
        catch (Exception e) { Diario.Erro(e); }
        Mudou?.Invoke();
    }

    /// <summary>Um cliente HTTP para o app inteiro (o WebDAV lista pasta com PROPFIND, que ele aceita).</summary>
    public static readonly HttpClient Http = CriarHttp();

    private static HttpClient CriarHttp()
    {
        var h = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LibertyXPlayer", "1.0"));
        return h;
    }

    // ───────── endereços ─────────

    /// <summary>lxdav://&lt;conta&gt;/&lt;caminho no servidor&gt;, cada pedaço codificado.</summary>
    public static string EnderecoWebDav(Conta c, string caminho) =>
        "lxdav://" + c.Id + "/" + string.Join('/', caminho.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    /// <summary>lxmidia://&lt;conta&gt;/&lt;id do item&gt;.</summary>
    public static string EnderecoMidia(Conta c, string itemId) => "lxmidia://" + c.Id + "/" + itemId;

    /// <summary>A conta e o resto (caminho do WebDAV ou id do item) de um endereço do app.</summary>
    public static (Conta conta, string resto)? Partes(string endereco)
    {
        string? semEsquema = endereco.StartsWith("lxdav://") ? endereco[8..] : endereco.StartsWith("lxmidia://") ? endereco[10..] : null;
        if (semEsquema == null) return null;
        int barra = semEsquema.IndexOf('/');
        if (barra <= 0 || Achar(semEsquema[..barra]) is not { } c) return null;
        var resto = semEsquema[(barra + 1)..];
        return (c, endereco.StartsWith("lxdav://") ? "/" + string.Join('/', resto.Split('/').Select(Uri.UnescapeDataString)) : resto);
    }

    /// <summary>
    /// O que o mpv abre e o cabeçalho que vai junto. WebDAV: a URL do arquivo, com a senha no
    /// cabeçalho (nunca no endereço, que fica no histórico); Jellyfin/Emby: o arquivo original
    /// (static=true), sem o servidor converter nada, com o token na consulta como os apps deles fazem.
    /// Os outros endereços passam como estão.
    /// </summary>
    public static (string url, string? cabecalho) ParaMpv(string endereco)
    {
        if (Partes(endereco) is not { } p) return (endereco, null);
        if (p.conta.Tipo == TipoDeConta.WebDav)
        {
            var dav = new WebDav(p.conta);
            return (dav.Url(p.resto, false).ToString(), dav.Autorizacao is { } a ? "Authorization: " + a : null);
        }
        // Jellyfin/Emby: o canal ao vivo já aberto, a versão convertida pelo servidor ou o original
        if (Conversoes.Canal(p.resto) is { } canal) return (canal, null);
        var s = new ServidorDeMidia(p.conta);
        if (Qualidade.Da(p.conta.Id) is > 0 and var kbps)
            return (s.UrlDaConversao(p.resto, kbps, Conversoes.Comecar(p.resto), Conversoes.Audio(p.resto)), null);
        Conversoes.Encerrar(p.resto);
        return (s.UrlDoVideo(p.resto), null);
    }

    /// <summary>
    /// Endereço digitado sem http(s): <c>maujellyfin.uk</c> atrás do Cloudflare só fala HTTPS, e
    /// <c>192.168.0.10:8096</c> em casa só fala HTTP. Tenta HTTPS primeiro; cai para HTTP só se nem
    /// conectar — erro de senha num deles já é resposta.
    /// </summary>
    public static IEnumerable<string> Candidatos(string digitado)
    {
        var t = digitado.Trim();
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return [t];
        return ["https://" + t, "http://" + t];
    }

    /// <summary>Não chegou a falar com o servidor (nome, porta, TLS): vale tentar o outro esquema.</summary>
    public static bool NemConectou(Exception e) => e is HttpRequestException { StatusCode: null } or TaskCanceledException;
}

/// <summary>Erro de servidor HTTP com uma frase que diga o que fazer.</summary>
public sealed class ErroDeServidor(string msg) : Exception(msg)
{
    /// <summary>O servidor recusou a entrada (401/403): senha errada ou chave de acesso vencida.</summary>
    public bool Recusou { get; init; }

    public static ErroDeServidor Http(HttpStatusCode codigo) => new((int)codigo switch
    {
        401 or 403 => Textos.S("Usuário ou senha não conferem."),
        404 or 405 => Textos.S("O endereço não é de uma pasta WebDAV."),
        var n => Textos.F("O servidor respondeu com erro {0}.", n),
    }) { Recusou = (int)codigo is 401 or 403 };
}

/// <summary>
/// Cliente WebDAV — o suficiente para navegar por pastas e entregar ao mpv o endereço de cada vídeo.
/// É por aqui que entra o Nextcloud (<c>/remote.php/dav/files/&lt;usuário&gt;/</c>). Quem toca é o
/// mpv, por HTTP comum, com busca por faixa de bytes; listar pasta é a única coisa que exige o
/// vocabulário do WebDAV (PROPFIND).
/// </summary>
public sealed class WebDav(Conta conta)
{
    public sealed record Entrada(string Nome, string Caminho, bool Pasta, long Tamanho, DateTime Data);

    /// <summary>Pasta de partida: o caminho que veio no endereço da conta.</summary>
    public string Raiz => Uri.TryCreate(conta.Endereco, UriKind.Absolute, out var u) && Uri.UnescapeDataString(u.AbsolutePath) is { Length: > 0 } p ? p : "/";

    public string? Autorizacao => conta.Usuario.Length == 0 ? null
        : "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(conta.Usuario + ":" + conta.Segredo));

    public async Task<List<Entrada>> Listar(string caminho, CancellationToken ct = default)
    {
        const string corpo = """
            <?xml version="1.0" encoding="utf-8"?>
            <d:propfind xmlns:d="DAV:"><d:prop><d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>
            """;
        using var pedido = new HttpRequestMessage(new HttpMethod("PROPFIND"), Url(caminho, true))
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/xml"),
        };
        // só o nível de baixo: "infinity" faria o servidor descer a árvore inteira, e muitos recusam
        pedido.Headers.Add("Depth", "1");
        if (Autorizacao is { } a) pedido.Headers.TryAddWithoutValidation("Authorization", a);
        using var r = await Contas.Http.SendAsync(pedido, ct);
        // 207 é a resposta própria do WebDAV; um 200 aqui costuma ser uma página de login
        if (r.StatusCode == HttpStatusCode.OK) throw new ErroDeServidor(Textos.S("O endereço responde, mas não é WebDAV. Confira o caminho."));
        if ((int)r.StatusCode != 207) throw ErroDeServidor.Http(r.StatusCode);
        var xml = await r.Content.ReadAsStringAsync(ct);
        string pedida = Normalizar(caminho);
        var saida = new List<Entrada>();
        foreach (var resp in System.Xml.Linq.XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "response"))
        {
            // o prefixo do espaço de nomes muda de servidor para servidor (d:, D:, nenhum): só o nome local conta
            string? Valor(string nome) => resp.Descendants().FirstOrDefault(e => e.Name.LocalName == nome)?.Value.Trim();
            var href = Valor("href");
            if (string.IsNullOrEmpty(href)) continue;
            var c = CaminhoDoHref(href);
            if (Normalizar(c) == pedida) continue;   // a própria pasta vem junto
            var nome = c.TrimEnd('/');
            nome = nome[(nome.LastIndexOf('/') + 1)..];
            if (nome.Length == 0 || nome.StartsWith('.')) continue;
            bool pasta = resp.Descendants().Any(e => e.Name.LocalName == "collection");
            long.TryParse(Valor("getcontentlength"), out var tam);
            DateTime.TryParse(Valor("getlastmodified"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var data);
            saida.Add(new Entrada(nome, c, pasta, tam, data == default ? default : data.ToLocalTime()));
        }
        return saida;
    }

    /// <summary>Um arquivo pequeno inteiro (as legendas ao lado do vídeo).</summary>
    public async Task<byte[]> Baixar(string caminho, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, Url(caminho, false));
        if (Autorizacao is { } a) pedido.Headers.TryAddWithoutValidation("Authorization", a);
        using var r = await Contas.Http.SendAsync(pedido, ct);
        if (!r.IsSuccessStatusCode) throw ErroDeServidor.Http(r.StatusCode);
        return await r.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Esquema, servidor e porta da conta, com o caminho codificado pedaço a pedaço (espaço, acento e # são o normal).</summary>
    public Uri Url(string caminho, bool pasta)
    {
        var b = new Uri(conta.Endereco);
        var cod = "/" + string.Join('/', caminho.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        if (pasta && !cod.EndsWith('/')) cod += "/";
        return new Uri(b.GetLeftPart(UriPartial.Authority) + cod);
    }

    /// <summary>O href vem como caminho (/dav/a%20b/) ou como URL completa.</summary>
    private static string CaminhoDoHref(string href) =>
        Uri.UnescapeDataString(href.StartsWith("http") && Uri.TryCreate(href, UriKind.Absolute, out var u) ? u.AbsolutePath : href);

    private static string Normalizar(string c) => c.TrimEnd('/') is { Length: > 0 } n ? n : "/";

    /// <summary>
    /// Confere a conta listando a pasta do endereço; se ele for só o servidor, tenta o caminho do
    /// Nextcloud antes de desistir. Devolve a conta que funcionou.
    /// </summary>
    public static async Task<Conta> Testar(Conta c)
    {
        var d = new WebDav(c);
        try { await d.Listar(d.Raiz); return c; }
        catch (Exception e) when (!Contas.NemConectou(e) && d.Raiz == "/" && c.Usuario.Length > 0)
        {
            var nc = new Conta
            {
                Id = c.Id, Tipo = c.Tipo, Nome = c.Nome, Usuario = c.Usuario, Segredo = c.Segredo,
                Endereco = c.Endereco.TrimEnd('/') + "/remote.php/dav/files/" + Uri.EscapeDataString(c.Usuario) + "/",
            };
            var outro = new WebDav(nc);
            try { await outro.Listar(outro.Raiz); return nc; }
            catch { throw e; }
        }
    }
}
