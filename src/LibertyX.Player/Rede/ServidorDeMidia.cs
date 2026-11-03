using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using LibertyX.Player.Core;

namespace LibertyX.Player.Rede;

/// <summary>
/// Cliente de Jellyfin e Emby, igual ao do Android. O app lê só o catálogo pela API e toca o
/// <b>arquivo original</b> pelo mpv: num servidor sem aceleração de vídeo isso muda tudo — o MKV com
/// HEVC e áudio AC-3 sai do disco como está, sem o servidor converter nada, e a rolagem é a mesma do
/// arquivo local.
/// </summary>
public sealed class ServidorDeMidia(Conta conta)
{
    /// <summary>O servidor conta o tempo em décimos de microssegundo.</summary>
    private const double Ticks = 10_000_000.0;

    public sealed record Item(
        string Id, string Nome, string Tipo, bool EhPasta, string? Colecao, double? Duracao, int? Ano, int? Numero,
        DateTime Criado, string? Serie, string? SerieId, string? TemporadaId, int? Temporada,
        double? Posicao, bool Visto, int? NaoVistos, DateTime? UltimaVez)
    {
        /// <summary>Episódio com o número na frente, filme com o ano: é como se acha o que se procura numa lista longa.</summary>
        public string Titulo => Tipo == "Episode" && Numero != null ? $"{Numero}. {Nome}" : Tipo == "Movie" && Ano != null ? $"{Nome} ({Ano})" : Nome;

        public bool EhVideo => !EhPasta && Tipo is "Movie" or "Episode" or "Video" or "MusicVideo";

        /// <summary>"T1 · E3" do episódio, para a capa.</summary>
        public string? Episodio => Tipo == "Episode" && Numero != null ? (Temporada != null ? $"T{Temporada} · E{Numero}" : $"E{Numero}") : null;
    }

    public Conta Conta => conta;

    private string UsuarioId => conta.IdNoServidor ?? "";

    /// <summary>Bibliotecas do usuário (Filmes, Séries, Vídeos caseiros…) — música, livros e fotos ficam de fora.</summary>
    public async Task<List<Item>> Bibliotecas() =>
        (await Lista($"/Users/{UsuarioId}/Views")).Where(i => i.Colecao is not ("music" or "books" or "photos" or "playlists" or "livetv" or "musicvideos")).ToList();

    /// <summary>
    /// O que há dentro de uma biblioteca, série ou temporada. Bibliotecas de filmes e de séries são
    /// lidas de forma recursiva, filtradas pelo tipo: quem navega espera os filmes e as séries, não a
    /// estrutura de pastas do disco.
    /// </summary>
    public Task<List<Item>> Itens(Item pai)
    {
        var q = new List<(string, string)> { ("ParentId", pai.Id), ("Fields", "DateCreated"), ("SortBy", "ParentIndexNumber,IndexNumber,SortName"), ("EnableUserData", "true") };
        if (pai.Colecao == "movies") q.AddRange([("Recursive", "true"), ("IncludeItemTypes", "Movie")]);
        if (pai.Colecao == "tvshows") q.AddRange([("Recursive", "true"), ("IncludeItemTypes", "Series")]);
        return Lista($"/Users/{UsuarioId}/Items", q);
    }

    /// <summary>Filmes e episódios cujo nome tem <paramref name="termo"/>, de todas as bibliotecas.</summary>
    public async Task<List<Item>> Buscar(string termo, CancellationToken ct = default) =>
        (await Lista($"/Users/{UsuarioId}/Items", [("searchTerm", termo), ("Recursive", "true"), ("IncludeItemTypes", "Movie,Episode,Video,MusicVideo"),
            ("Fields", "DateCreated"), ("Limit", "40"), ("EnableUserData", "true")], ct)).Where(i => i.EhVideo).ToList();

    /// <summary>O que a pessoa começou e não terminou, do mais recente ao mais antigo.</summary>
    public async Task<List<Item>> Continuar() =>
        (await Lista($"/Users/{UsuarioId}/Items/Resume", [("Limit", "24"), ("MediaTypes", "Video"), ("Fields", "DateCreated"), ("EnableUserData", "true")])).Where(i => i.EhVideo).ToList();

    /// <summary>O próximo episódio de cada série que a pessoa está vendo.</summary>
    public async Task<List<Item>> ASeguir() =>
        (await Lista("/Shows/NextUp", [("UserId", UsuarioId), ("Limit", "24"), ("Fields", "DateCreated"), ("EnableUserData", "true")])).Where(i => i.EhVideo).ToList();

    /// <summary>O que chegou por último numa biblioteca (numa de séries, o servidor agrupa os episódios na série).</summary>
    public async Task<List<Item>> Recentes(Item biblioteca)
    {
        using var doc = await Pedir($"/Users/{UsuarioId}/Items/Latest", [("ParentId", biblioteca.Id), ("Limit", "16"), ("Fields", "DateCreated"), ("EnableUserData", "true")]);
        return Ler(doc.RootElement);
    }

    /// <summary>Os episódios da temporada, na ordem: é a fila do "próximo" no player.</summary>
    public async Task<List<Item>> Temporada(Item episodio)
    {
        if (episodio.SerieId is not { } serie) return [episodio];
        var q = new List<(string, string)> { ("UserId", UsuarioId), ("Fields", "DateCreated"), ("EnableUserData", "true") };
        if (episodio.TemporadaId is { } t) q.Add(("SeasonId", t));
        var l = (await Lista($"/Shows/{serie}/Episodes", q)).Where(i => i.EhVideo).ToList();
        return l.Count > 0 ? l : [episodio];
    }

    /// <summary>
    /// O episódio depois deste, pelo "A seguir" do servidor — que sabe das outras temporadas e do
    /// que já foi visto em outro aparelho. Null para filme ou quando a série acabou.
    /// </summary>
    public async Task<Item?> ProximoEpisodio(string itemId)
    {
        using var doc = await Pedir($"/Users/{UsuarioId}/Items/{itemId}");
        var atual = Ler(doc.RootElement).FirstOrDefault();
        if (atual?.SerieId is not { } serie) return null;
        return (await Lista("/Shows/NextUp", [("UserId", UsuarioId), ("SeriesId", serie), ("Limit", "1"), ("Fields", "DateCreated"), ("EnableUserData", "true")]))
            .FirstOrDefault(i => i.EhVideo && i.Id != itemId);
    }

    /// <summary>
    /// As legendas "externas" do vídeo: os .srt que o servidor achou ao lado do arquivo. As de dentro
    /// do arquivo o mpv já lê; estas ele não vê, porque recebe só o vídeo. Cada uma como (nome do
    /// arquivo, texto); o nome leva "forced" quando a faixa é forçada, que é como a regra da legenda a
    /// reconhece.
    /// </summary>
    public async Task<List<(string nome, string texto)>> LegendasExternas(string itemId)
    {
        using var doc = await Pedir($"/Users/{UsuarioId}/Items/{itemId}", [("Fields", "MediaSources,MediaStreams")]);
        if (!doc.RootElement.TryGetProperty("MediaSources", out var fontes) || fontes.GetArrayLength() == 0) return [];
        var fonte = fontes[0];
        var fonteId = Str(fonte, "Id") ?? itemId;
        if (!fonte.TryGetProperty("MediaStreams", out var fluxos)) return [];
        var saida = new List<(string, string)>();
        foreach (var f in fluxos.EnumerateArray())
        {
            if (Str(f, "Type") != "Subtitle" || !Bool(f, "IsExternal") || !f.TryGetProperty("Index", out var ix)) continue;
            int indice = ix.GetInt32();
            // o servidor converte qualquer legenda de texto para SRT; ASS vem como ASS, para não perder cor e posição
            var codec = Str(f, "Codec")?.ToLowerInvariant();
            var formato = codec is "ass" or "ssa" ? codec : "srt";
            string texto;
            try { texto = await PedirTexto($"/Videos/{itemId}/{fonteId}/Subtitles/{indice}/0/Stream.{formato}", [], null); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(texto)) continue;
            var titulo = Str(f, "DisplayTitle") ?? Str(f, "Language") ?? "Legenda";
            if (Bool(f, "IsForced") && !titulo.Contains("forced", StringComparison.OrdinalIgnoreCase)) titulo += " forced";
            // o índice no nome: duas faixas com o mesmo título não se sobrescrevem
            saida.Add(($"{string.Concat(titulo.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' or ':' ? '_' : ch))} ({indice}).{formato}", texto));
        }
        return saida;
    }

    /// <summary>
    /// Conta ao servidor o que está tocando, para o "Continuar assistindo" e o "visto" valerem também
    /// no app dele, na TV e no navegador. <paramref name="evento"/> é null no começo, "Progress"
    /// durante e "Stopped" no fim — e é no "Stopped" perto do fim que o servidor marca como visto.
    /// </summary>
    public Task Informar(string? evento, string itemId, double segundos, bool pausado, string sessao)
    {
        var corpo = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ItemId"] = itemId, ["MediaSourceId"] = itemId, ["PositionTicks"] = (long)(Math.Max(0, segundos) * Ticks),
            ["IsPaused"] = pausado, ["CanSeek"] = true, ["PlayMethod"] = "DirectPlay", ["PlaySessionId"] = sessao,
        });
        return PedirTexto("/Sessions/Playing" + (evento != null ? "/" + evento : ""), [], corpo);
    }

    /// <summary>O arquivo original, sem conversão (static=true).</summary>
    public string UrlDoVideo(string id) => Url($"/Videos/{id}/stream", [("static", "true"), ("MediaSourceId", id), .. ChaveNaConsulta]);

    /// <summary>
    /// A imagem do item: Thumb e Backdrop são deitadas, como as capas do app; Primary de filme é o
    /// cartaz em pé, e fica por último.
    /// </summary>
    public async Task<byte[]?> Imagem(string id, int largura = 480)
    {
        foreach (var tipo in new[] { "Thumb", "Backdrop", "Primary" })
        {
            try
            {
                using var r = await Contas.Http.GetAsync(Url($"/Items/{id}/Images/{tipo}", [("maxWidth", largura.ToString()), ("quality", "80"), .. ChaveNaConsulta]));
                if (r.StatusCode == HttpStatusCode.OK) return await r.Content.ReadAsByteArrayAsync();
            }
            catch { }
        }
        return null;
    }

    /// <summary>O Jellyfin novo lê ApiKey; o Emby, api_key. Os dois servem aos dois.</summary>
    private (string, string)[] ChaveNaConsulta => conta.Segredo.Length > 0 ? [("ApiKey", conta.Segredo), ("api_key", conta.Segredo)] : [];

    private string Cabecalho =>
        $"MediaBrowser Client=\"LibertyX Player\", Device=\"{(OperatingSystem.IsWindows() ? "Windows" : "Linux")}\", DeviceId=\"{IdDoAparelho}\", Version=\"1.0\""
        + (conta.Segredo.Length > 0 ? $", Token=\"{conta.Segredo}\"" : "");

    private string Url(string caminho, IEnumerable<(string k, string v)> consulta)
    {
        var q = string.Join('&', consulta.Select(p => Uri.EscapeDataString(p.k) + "=" + Uri.EscapeDataString(p.v)));
        return conta.Endereco.TrimEnd('/') + caminho + (q.Length > 0 ? "?" + q : "");
    }

    private async Task<string> PedirTexto(string caminho, IEnumerable<(string, string)> consulta, string? corpoJson, CancellationToken ct = default)
    {
        using var pedido = new HttpRequestMessage(corpoJson != null ? HttpMethod.Post : HttpMethod.Get, Url(caminho, consulta));
        // o Jellyfin lê Authorization; o Emby, X-Emby-Authorization
        pedido.Headers.TryAddWithoutValidation("Authorization", Cabecalho);
        pedido.Headers.TryAddWithoutValidation("X-Emby-Authorization", Cabecalho);
        pedido.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (corpoJson != null) pedido.Content = new StringContent(corpoJson, Encoding.UTF8, "application/json");
        using var r = await Contas.Http.SendAsync(pedido, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) throw NaoEhServidor();
        if (!r.IsSuccessStatusCode) throw r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new ErroDeServidor(Textos.S("Usuário ou senha não conferem.")) : ErroDeServidor.Http(r.StatusCode);
        return await r.Content.ReadAsStringAsync(ct);
    }

    private async Task<JsonDocument> Pedir(string caminho, IEnumerable<(string, string)>? consulta = null, string? corpo = null, CancellationToken ct = default)
    {
        var texto = await PedirTexto(caminho, consulta ?? [], corpo, ct);
        try { return JsonDocument.Parse(texto); }
        catch { throw NaoEhServidor(); }
    }

    private ErroDeServidor NaoEhServidor() => new(Textos.F("O endereço não responde como um servidor {0}.", conta.NomeDoTipo));

    private async Task<List<Item>> Lista(string caminho, IEnumerable<(string, string)>? consulta = null, CancellationToken ct = default)
    {
        using var doc = await Pedir(caminho, consulta, null, ct);
        return Ler(doc.RootElement.TryGetProperty("Items", out var itens) ? itens : doc.RootElement);
    }

    private List<Item> Ler(JsonElement el)
    {
        var lidos = new List<Item>();
        foreach (var o in el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().ToArray() : [el])
        {
            if (Str(o, "Id") is not { } id) continue;
            o.TryGetProperty("UserData", out var u);
            bool temU = u.ValueKind == JsonValueKind.Object;
            long Long(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.TryGetInt64(out var x) ? x : 0;
            int? Int(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : null;
            long dur = Long(o, "RunTimeTicks"), pos = temU ? Long(u, "PlaybackPositionTicks") : 0;
            lidos.Add(new Item(
                id, Str(o, "Name") ?? "", Str(o, "Type") ?? "", Bool(o, "IsFolder"), Str(o, "CollectionType"),
                dur > 0 ? dur / Ticks : null, Int(o, "ProductionYear") is > 0 and var ano ? ano : null, Int(o, "IndexNumber"),
                Data(Str(o, "DateCreated")) ?? default, Str(o, "SeriesName"), Str(o, "SeriesId"), Str(o, "SeasonId"), Int(o, "ParentIndexNumber"),
                pos > 0 ? pos / Ticks : null, temU && Bool(u, "Played"), temU && Int(u, "UnplayedItemCount") is > 0 and var nv ? nv : null,
                temU ? Data(Str(u, "LastPlayedDate")) : null));
        }
        LembrarOndeParou(lidos);
        return lidos;
    }

    /// <summary>
    /// Toda lista que chega do servidor diz onde a pessoa parou em cada vídeo. Guardar isso junto das
    /// marcas do app é o que faz o "Continuar de…" valer para o que foi visto na TV ou no navegador —
    /// a marca mais nova vence.
    /// </summary>
    private void LembrarOndeParou(List<Item> itens)
    {
        var doServidor = itens.Where(i => i.EhVideo && i.UltimaVez != null).ToList();
        if (doServidor.Count == 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            bool mudou = false;
            foreach (var i in doServidor)
            {
                var chave = Contas.EnderecoMidia(conta, i.Id);
                long quando = new DateTimeOffset(i.UltimaVez!.Value).ToUnixTimeSeconds();
                var pos = Prefs.Atual.Posicoes;
                if (pos.TryGetValue(chave, out var local) && local.Quando >= quando) continue;
                // visto e com posição é quem está revendo: a posição vale (o Jellyfin guarda as duas coisas)
                if (i.Posicao is not > 0) { mudou |= pos.Remove(chave); continue; }
                if (local != null && Math.Abs(local.Posicao - i.Posicao.Value) < 1) continue;
                pos[chave] = new Retomada { Posicao = i.Posicao.Value, Duracao = i.Duracao ?? 0, Quando = quando, Titulo = i.Titulo };
                mudou = true;
            }
            if (mudou) Prefs.Atual.Salvar();
        });
    }

    /// <summary>O vídeo como o app toca: endereço sem token, título, duração.</summary>
    public Midia ParaTocar(Item i) => new()
    {
        Endereco = Contas.EnderecoMidia(conta, i.Id), Titulo = i.Serie != null && i.Tipo == "Episode" ? $"{i.Serie} · {i.Titulo}" : i.Titulo,
        Origem = Origem.Servidor, Data = i.Criado, Duracao = i.Duracao, Pasta = i.Serie ?? conta.Nome,
    };

    /// <summary>Episódio toca com a temporada em volta, para o "próximo" do player ir ao episódio seguinte.</summary>
    public async Task<(List<Midia> fila, int indice)> Fila(Item i)
    {
        if (i.Tipo != "Episode") return ([ParaTocar(i)], 0);
        List<Item> temp;
        try { temp = await Temporada(i); } catch { temp = [i]; }
        int k = temp.FindIndex(x => x.Id == i.Id);
        return k < 0 ? ([ParaTocar(i)], 0) : (temp.Select(ParaTocar).ToList(), k);
    }

    private static string? Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
    private static bool Bool(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Sete casas de fração e fuso no fim: só os segundos interessam para ordenar.</summary>
    private static DateTime? Data(string? t) =>
        t is { Length: >= 19 } && DateTime.TryParse(t[..19], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime() : null;

    /// <summary>O servidor lista os aparelhos conectados por este id.</summary>
    private static string IdDoAparelho
    {
        get
        {
            if (Prefs.Atual.IdDoAparelho is { } id) return id;
            Prefs.Atual.IdDoAparelho = Guid.NewGuid().ToString();
            Prefs.Atual.Salvar();
            return Prefs.Atual.IdDoAparelho;
        }
    }

    /// <summary>Entra com usuário e senha e devolve o id do usuário e o token (a senha não fica guardada).</summary>
    public static async Task<(string usuario, string token)> Entrar(Conta c, string senha)
    {
        var s = new ServidorDeMidia(new Conta { Id = c.Id, Tipo = c.Tipo, Endereco = c.Endereco, Usuario = c.Usuario });
        using var doc = await s.Pedir("/Users/AuthenticateByName", null, JsonSerializer.Serialize(new Dictionary<string, string> { ["Username"] = c.Usuario, ["Pw"] = senha }));
        var usuario = doc.RootElement.TryGetProperty("User", out var u) ? Str(u, "Id") : null;
        var token = Str(doc.RootElement, "AccessToken");
        if (usuario == null || token == null) throw s.NaoEhServidor();
        return (usuario, token);
    }
}

/// <summary>
/// Conta ao Jellyfin ou ao Emby o que o player está tocando — o filme pela metade vai para o
/// "Continuar assistindo" do servidor, e o episódio fica marcado como visto no app dele. Para os
/// outros vídeos, não faz nada. Os avisos saem um de cada vez e na ordem: um "parou" que chegasse
/// antes do "começou" deixaria o servidor achando que o filme ainda está tocando.
/// </summary>
public static class AvisoAoServidor
{
    private sealed record Sessao(ServidorDeMidia Servidor, string ItemId, string Id, string Chave);

    private static Sessao? _sessao;
    private static long _ultimo;
    private static bool _pausado;
    private static Task _fila = Task.CompletedTask;
    private static readonly Lock Trava = new();

    public static void Comecou(Midia m, double segundos)
    {
        Parou(segundos);
        if (m.Origem != Origem.Servidor || Contas.Partes(m.Endereco) is not { } p) return;
        var s = new Sessao(new ServidorDeMidia(p.conta), p.resto, Guid.NewGuid().ToString(), m.Endereco);
        _sessao = s; _pausado = false; _ultimo = Environment.TickCount64;
        Avisar(s, null, segundos);
    }

    /// <summary>A cada atualização de tempo, mas só avisa a cada dez segundos — ou na hora, ao pausar e ao voltar.</summary>
    public static void Andou(double segundos, bool pausado)
    {
        if (_sessao is not { } s) return;
        if (pausado == _pausado && Environment.TickCount64 - _ultimo < 10_000) return;
        _pausado = pausado; _ultimo = Environment.TickCount64;
        Avisar(s, "Progress", segundos);
    }

    /// <summary>
    /// Só o primeiro chega ao servidor. <paramref name="visto"/>: assistido até o fim — depois que o
    /// servidor marca o episódio, o "A seguir" dele já aponta o próximo, e é esse que entra no
    /// histórico no lugar do que terminou.
    /// </summary>
    public static void Parou(double segundos, bool visto = false)
    {
        if (_sessao is not { } s) return;
        _sessao = null;
        Avisar(s, "Stopped", segundos);
        if (!visto) return;
        Enfileirar(async () =>
        {
            ServidorDeMidia.Item? prox = null;
            try { prox = await s.Servidor.ProximoEpisodio(s.ItemId); } catch { }
            var m = prox != null ? s.Servidor.ParaTocar(prox) : null;
            Dispatcher.UIThread.Post(() => Historico.Terminou(s.Chave, m));
        });
    }

    private static void Avisar(Sessao s, string? evento, double segundos)
    {
        bool pausado = _pausado;
        // sem rede o aviso se perde, e o vídeo segue tocando: a marca do app continua valendo
        Enfileirar(async () => { try { await s.Servidor.Informar(evento, s.ItemId, segundos, pausado, s.Id); } catch { } });
    }

    private static void Enfileirar(Func<Task> trabalho)
    {
        lock (Trava) _fila = _fila.ContinueWith(_ => trabalho()).Unwrap();
    }

    /// <summary>Espera os avisos a caminho — no máximo uns segundos, para a tela não travar sem rede.</summary>
    public static Task EmDia()
    {
        Task f;
        lock (Trava) f = _fila;
        return Task.WhenAny(f, Task.Delay(4000));
    }
}
