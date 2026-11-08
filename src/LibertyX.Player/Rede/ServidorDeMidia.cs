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
        double? Posicao, bool Visto, int? NaoVistos, DateTime? UltimaVez, bool Favorito = false)
    {
        /// <summary>Episódio com o número na frente, filme com o ano: é como se acha o que se procura numa lista longa.</summary>
        public string Titulo => Tipo == "Episode" && Numero != null ? $"{Numero}. {Nome}" : Tipo == "Movie" && Ano != null ? $"{Nome} ({Ano})" : Nome;

        public bool EhVideo => !EhPasta && Tipo is "Movie" or "Episode" or "Video" or "MusicVideo";

        /// <summary>"T1 · E3" do episódio, para a capa.</summary>
        public string? Episodio => Tipo == "Episode" && Numero != null ? (Temporada != null ? $"T{Temporada} · E{Numero}" : $"E{Numero}") : null;

        /// <summary>De 0 a 1, só quando há o que retomar.</summary>
        public double? Progresso => Posicao is > 0 and var p && Duracao is > 0 and var d ? Math.Clamp(p / d, 0, 1) : null;
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

    /// <summary>Filmes, séries e episódios cujo nome tem <paramref name="termo"/>, de todas as bibliotecas.</summary>
    public async Task<List<Item>> Buscar(string termo, CancellationToken ct = default) =>
        (await Lista($"/Users/{UsuarioId}/Items", [("searchTerm", termo), ("Recursive", "true"), ("IncludeItemTypes", "Movie,Series,Episode,Video,MusicVideo"),
            ("Fields", "DateCreated"), ("Limit", "40"), ("EnableUserData", "true")], ct)).Where(i => i.EhVideo || i.Tipo == "Series").ToList();

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
    public Task Informar(string? evento, string itemId, double segundos, bool pausado, string sessao, bool convertendo = false)
    {
        var corpo = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ItemId"] = itemId, ["MediaSourceId"] = itemId, ["PositionTicks"] = (long)(Math.Max(0, segundos) * Ticks),
            ["IsPaused"] = pausado, ["CanSeek"] = true, ["PlayMethod"] = convertendo ? "Transcode" : "DirectPlay", ["PlaySessionId"] = sessao,
        });
        return PedirTexto("/Sessions/Playing" + (evento != null ? "/" + evento : ""), [], corpo);
    }

    /// <summary>O arquivo original, sem conversão (static=true).</summary>
    public string UrlDoVideo(string id) => Url($"/Videos/{id}/stream", [("static", "true"), ("MediaSourceId", id), .. ChaveNaConsulta]);

    /// <summary>
    /// A imagem do item: Thumb e Backdrop são deitadas, como as capas do app; Primary de filme é o
    /// cartaz em pé, e fica por último.
    /// </summary>
    public async Task<byte[]?> Imagem(string id, int largura = 480) => (await BaixarImagem(id, ["Thumb", "Backdrop", "Primary"], largura)).bytes;

    /// <summary>
    /// A imagem, na ordem dos <paramref name="tipos"/>, ou por que não veio: <c>caiu</c> é o pedido que
    /// falhou (adianta pedir de novo); sem ele, o servidor não tem a imagem (não adianta).
    /// </summary>
    public async Task<(byte[]? bytes, bool caiu)> BaixarImagem(string id, string[] tipos, int largura)
    {
        bool caiu = false;
        foreach (var tipo in tipos)
        {
            try
            {
                using var r = await Contas.Http.GetAsync(Url($"/Items/{id}/Images/{tipo}", [("maxWidth", largura.ToString()), ("quality", "80"), .. ChaveNaConsulta]));
                if (r.StatusCode == HttpStatusCode.OK) return (await r.Content.ReadAsByteArrayAsync(), false);
                if (r.StatusCode != HttpStatusCode.NotFound) caiu = true;
            }
            catch { caiu = true; }
        }
        return (null, caiu);
    }

    // ───────── detalhes, séries, pessoas ─────────

    /// <summary>Uma pessoa do elenco ou da equipe; <c>Tipo</c> é "Actor", "Director", "Writer"…</summary>
    public sealed record Pessoa(string Id, string Nome, string? Papel, string Tipo);

    /// <summary>Uma faixa do arquivo como o servidor a leu: "1080p HEVC SDR", "Portuguese - AC3 - 5.1".</summary>
    public sealed record Fluxo(string Tipo, string Titulo, int Indice, bool Externo, bool Padrao, string Codec, int Altura, int Canais, string FaixaDinamica);

    /// <summary>O que a página de um filme, episódio, série ou pessoa mostra.</summary>
    public sealed record Detalhes(
        string? Sinopse, DateTime? Estreia, double? Nota, string? Classificacao, List<string> Generos, List<Pessoa> Pessoas,
        List<Fluxo> Fluxos, string? NomeDaTemporada, List<string> TrailersDaInternet, int TrailersNoDisco, int Extras,
        Dictionary<string, string> IdsExternos, string? LogoDe, string? FundoDe)
    {
        public List<Fluxo> Faixas(string tipo) => Fluxos.Where(f => f.Tipo == tipo).ToList();
        public List<Pessoa> PessoasCom(params string[] tipos) => Pessoas.Where(p => tipos.Contains(p.Tipo)).ToList();
    }

    /// <summary>O item de novo (o "visto" e onde parou podem ter mudado) e os detalhes dele.</summary>
    public async Task<(Item item, Detalhes detalhes)> LerDetalhes(string id)
    {
        using var doc = await Pedir($"/Users/{UsuarioId}/Items/{id}",
            [("Fields", "Overview,People,Genres,MediaSources,MediaStreams,PremiereDate,DateCreated,RemoteTrailers,ProviderIds")]);
        var o = doc.RootElement;
        var item = Ler(o).First();
        var fluxos = o.TryGetProperty("MediaStreams", out var soltos) && soltos.ValueKind == JsonValueKind.Array && soltos.GetArrayLength() > 0 ? soltos
            : o.TryGetProperty("MediaSources", out var fontes) && fontes.ValueKind == JsonValueKind.Array && fontes.GetArrayLength() > 0
              && fontes[0].TryGetProperty("MediaStreams", out var deFonte) ? deFonte : default;
        static int Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var x) ? x : 0;
        List<string> Textos(string n) => o.TryGetProperty(n, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
        var d = new Detalhes(
            Str(o, "Overview"),
            Data(Str(o, "PremiereDate")),
            o.TryGetProperty("CommunityRating", out var nota) && nota.TryGetDouble(out var n) && n > 0 ? n : null,
            Str(o, "OfficialRating"),
            Textos("Genres"),
            o.TryGetProperty("People", out var ps) && ps.ValueKind == JsonValueKind.Array
                ? ps.EnumerateArray().Select(x => new Pessoa(Str(x, "Id") ?? "", Str(x, "Name") ?? "", Str(x, "Role"), Str(x, "Type") ?? "")).Where(x => x.Id.Length > 0).ToList() : [],
            fluxos.ValueKind == JsonValueKind.Array
                ? fluxos.EnumerateArray().Select(f => new Fluxo(
                    Str(f, "Type") ?? "",
                    Str(f, "DisplayTitle") ?? string.Join(" - ", new[] { Str(f, "Language"), Str(f, "Codec") }.Where(x => x != null)),
                    f.TryGetProperty("Index", out var ix) && ix.TryGetInt32(out var i) ? i : -1,
                    Bool(f, "IsExternal"), Bool(f, "IsDefault"), Str(f, "Codec") ?? "", Num(f, "Height"), Num(f, "Channels"),
                    Str(f, "VideoRangeType") ?? Str(f, "VideoRange") ?? "")).ToList() : [],
            Str(o, "SeasonName"),
            o.TryGetProperty("RemoteTrailers", out var rt) && rt.ValueKind == JsonValueKind.Array
                ? rt.EnumerateArray().Select(t => Str(t, "Url")).OfType<string>().ToList() : [],
            Num(o, "LocalTrailerCount"), Num(o, "SpecialFeatureCount"),
            o.TryGetProperty("ProviderIds", out var ids) && ids.ValueKind == JsonValueKind.Object
                ? ids.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String).ToDictionary(x => x.Name, x => x.Value.GetString() ?? "") : [],
            o.TryGetProperty("ImageTags", out var tags) && tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("Logo", out _) ? id : Str(o, "ParentLogoItemId"),
            o.TryGetProperty("BackdropImageTags", out var fundos) && fundos.ValueKind == JsonValueKind.Array && fundos.GetArrayLength() > 0 ? id : Str(o, "ParentBackdropItemId"));
        return (item, d);
    }

    public Task<List<Item>> Temporadas(string serieId) =>
        Lista($"/Shows/{serieId}/Seasons", [("UserId", UsuarioId), ("Fields", "DateCreated"), ("EnableUserData", "true")]);

    public async Task<List<Item>> Episodios(string serieId, string temporadaId) =>
        (await Lista($"/Shows/{serieId}/Episodes", [("UserId", UsuarioId), ("SeasonId", temporadaId), ("Fields", "DateCreated"), ("EnableUserData", "true")])).Where(i => i.EhVideo).ToList();

    /// <summary>O episódio que a pessoa deve ver agora: o "A seguir" da série.</summary>
    public async Task<Item?> ProximoDaSerie(string serieId) =>
        (await Lista("/Shows/NextUp", [("UserId", UsuarioId), ("SeriesId", serieId), ("Limit", "1"), ("Fields", "DateCreated"), ("EnableUserData", "true")])).FirstOrDefault(i => i.EhVideo);

    /// <summary>Os filmes e as séries de que a pessoa participou, do mais novo ao mais velho.</summary>
    public Task<List<Item>> Obras(string pessoaId) =>
        Lista($"/Users/{UsuarioId}/Items", [("PersonIds", pessoaId), ("Recursive", "true"), ("IncludeItemTypes", "Movie,Series"),
            ("SortBy", "PremiereDate,ProductionYear,SortName"), ("SortOrder", "Descending"), ("Fields", "DateCreated"), ("EnableUserData", "true")]);

    /// <summary>Filmes, séries e episódios marcados com o coração.</summary>
    public Task<List<Item>> Favoritos() =>
        Lista($"/Users/{UsuarioId}/Items", [("Filters", "IsFavorite"), ("Recursive", "true"), ("IncludeItemTypes", "Movie,Series,Episode,Video"),
            ("SortBy", "SortName"), ("Fields", "DateCreated"), ("EnableUserData", "true")]);

    /// <summary>O coração do app do Jellyfin.</summary>
    public Task MarcarFavorito(string itemId, bool favorito) =>
        PedirTexto($"/Users/{UsuarioId}/FavoriteItems/{itemId}", [], favorito ? "{}" : null, metodo: favorito ? HttpMethod.Post : HttpMethod.Delete);

    public async Task<List<string>> Generos(Item biblioteca) =>
        (await Lista("/Genres", [("ParentId", biblioteca.Id), ("UserId", UsuarioId), ("SortBy", "SortName")])).Select(i => i.Nome).Where(n => n.Length > 0).ToList();

    /// <summary>Ordem e filtros de uma biblioteca, como no app do Jellyfin.</summary>
    public sealed record Filtro(string Ordem = "SortName", bool Decrescente = false, bool NaoVistos = false, bool Favoritos = false, string? Genero = null)
    {
        /// <summary>O campo do servidor e o nome na tela.</summary>
        public static (string campo, string nome)[] Ordens =>
            [("SortName", Textos.S("Nome")), ("DateCreated", Textos.S("Adicionado")), ("PremiereDate", Textos.S("Lançamento")),
             ("CommunityRating", Textos.S("Nota")), ("Random", Textos.S("Aleatório"))];

        public bool Ativo => NaoVistos || Favoritos || Genero != null || Ordem != "SortName" || Decrescente;
    }

    /// <summary>Uma biblioteca de filmes ou séries já ordenada e filtrada pelo servidor.</summary>
    public Task<List<Item>> Itens(Item biblioteca, Filtro filtro)
    {
        var q = new List<(string, string)>
        {
            ("ParentId", biblioteca.Id), ("Recursive", "true"), ("IncludeItemTypes", biblioteca.Colecao == "tvshows" ? "Series" : "Movie"),
            ("SortBy", filtro.Ordem == "SortName" ? "SortName" : filtro.Ordem + ",SortName"), ("SortOrder", filtro.Decrescente ? "Descending" : "Ascending"),
            ("Fields", "DateCreated"), ("EnableUserData", "true"),
        };
        var filtros = new[] { filtro.NaoVistos ? "IsUnplayed" : null, filtro.Favoritos ? "IsFavorite" : null }.OfType<string>().ToList();
        if (filtros.Count > 0) q.Add(("Filters", string.Join(',', filtros)));
        if (filtro.Genero != null) q.Add(("Genres", filtro.Genero));
        return Lista($"/Users/{UsuarioId}/Items", q);
    }

    // ───────── trailers, extras, marcações ─────────

    public async Task<List<Item>> TrailersNoDisco(string id)
    {
        using var doc = await Pedir($"/Users/{UsuarioId}/Items/{id}/LocalTrailers");
        return Ler(doc.RootElement);
    }

    public async Task<List<Item>> Extras(string id)
    {
        using var doc = await Pedir($"/Users/{UsuarioId}/Items/{id}/SpecialFeatures");
        return Ler(doc.RootElement);
    }

    /// <summary>Um trecho marcado do vídeo — abertura, créditos, recapitulação — em segundos.</summary>
    public sealed record Segmento(string Tipo, double Inicio, double Fim);

    /// <summary>
    /// As marcações do vídeo (Jellyfin 10.10 em diante). Quem as cria é um plugin do servidor — o
    /// Intro Skipper, por exemplo; sem ele a lista vem vazia e o player só não oferece o "Pular".
    /// </summary>
    public async Task<List<Segmento>> Segmentos(string id)
    {
        try
        {
            using var doc = await Pedir($"/MediaSegments/{id}");
            if (!doc.RootElement.TryGetProperty("Items", out var itens)) return [];
            static long T(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt64(out var x) ? x : 0;
            return itens.EnumerateArray().Select(i => new Segmento(Str(i, "Type") ?? "", T(i, "StartTicks") / Ticks, T(i, "EndTicks") / Ticks))
                .Where(x => x.Fim > x.Inicio).ToList();
        }
        catch { return []; }
    }

    // ───────── conversão ─────────

    /// <summary>
    /// A versão convertida pelo servidor (ver <see cref="Qualidade"/>): uma lista HLS em H.264 e AAC
    /// estéreo, no teto de bits pedido.
    /// </summary>
    public string UrlDaConversao(string id, int kbps, string sessao, int? audio)
    {
        int largura = kbps >= 10_000 ? 1920 : kbps >= 4_000 ? 1280 : kbps >= 2_000 ? 960 : 640;
        var q = new List<(string, string)>
        {
            ("MediaSourceId", id), ("DeviceId", IdDoAparelho), ("PlaySessionId", sessao), ("VideoCodec", "h264"), ("AudioCodec", "aac"),
            ("VideoBitrate", Math.Max(300_000, kbps * 1000 - 192_000).ToString()), ("AudioBitrate", "192000"),
            ("MaxAudioChannels", "2"), ("TranscodingMaxAudioChannels", "2"), ("MaxWidth", largura.ToString()),
            ("SegmentContainer", "ts"), ("MinSegments", "1"), ("BreakOnNonKeyFrames", "true"),
        };
        if (audio is { } a) q.Add(("AudioStreamIndex", a.ToString()));
        return Url($"/Videos/{id}/master.m3u8", [.. q, .. ChaveNaConsulta]);
    }

    /// <summary>Desliga a conversão no servidor quando o vídeo fecha.</summary>
    public async Task PararConversao(string sessao)
    {
        try { await PedirTexto("/Videos/ActiveEncodings", [("DeviceId", IdDoAparelho), ("PlaySessionId", sessao)], null, metodo: HttpMethod.Delete); }
        catch { }
    }

    // ───────── TV ao vivo ─────────

    /// <summary>Um canal da TV ao vivo do servidor, com o programa de agora.</summary>
    public sealed record Canal(string Id, string Nome, string? Numero, Programa? Programa);

    public sealed record Programa(string Nome, DateTime? Inicio, DateTime? Fim)
    {
        /// <summary>De 0 a 1: quanto do programa já passou.</summary>
        public double? Andamento => Inicio is { } i && Fim is { } f && f > i ? Math.Clamp((DateTime.Now - i) / (f - i), 0, 1) : null;
    }

    /// <summary>Vazio quando o servidor não tem TV ao vivo: aí o app não mostra a faixa.</summary>
    public async Task<List<Canal>> Canais(int? limite = null)
    {
        var q = new List<(string, string)> { ("UserId", UsuarioId), ("AddCurrentProgram", "true"), ("EnableImages", "true"), ("EnableUserData", "false") };
        if (limite is { } l) q.Add(("Limit", l.ToString()));
        using var doc = await Pedir("/LiveTv/Channels", q);
        if (!doc.RootElement.TryGetProperty("Items", out var itens)) return [];
        return itens.EnumerateArray().Where(o => Str(o, "Id") != null).Select(o =>
        {
            Programa? p = o.TryGetProperty("CurrentProgram", out var cp) && cp.ValueKind == JsonValueKind.Object
                ? new Programa(Str(cp, "Name") ?? "", Data(Str(cp, "StartDate")), Data(Str(cp, "EndDate"))) : null;
            return new Canal(Str(o, "Id")!, Str(o, "Name") ?? "", Str(o, "Number") ?? Str(o, "ChannelNumber"), p);
        }).ToList();
    }

    /// <summary>
    /// O canal pronto para tocar: o servidor abre a transmissão e a entrega em HLS. O perfil aceita
    /// H.264, HEVC e MPEG-2 com AAC, AC-3 e MP2 — o que a TV aberta manda —, então ele só reempacota,
    /// sem converter o vídeo. Devolve a URL, a sessão e a transmissão (para fechar ao sair).
    /// </summary>
    public async Task<(string url, string sessao, string? transmissao)> AbrirCanal(string id)
    {
        var perfil = new Dictionary<string, object>
        {
            ["Name"] = "LibertyX Player", ["MaxStreamingBitrate"] = 120_000_000, ["DirectPlayProfiles"] = Array.Empty<object>(),
            ["TranscodingProfiles"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["Container"] = "ts", ["Type"] = "Video", ["Protocol"] = "hls", ["Context"] = "Streaming", ["VideoCodec"] = "h264,hevc,mpeg2video",
                    ["AudioCodec"] = "aac,ac3,eac3,mp2,mp3", ["BreakOnNonKeyFrames"] = true, ["MinSegments"] = 1, ["MaxAudioChannels"] = "6",
                },
            },
            ["CodecProfiles"] = Array.Empty<object>(),
            ["SubtitleProfiles"] = new[] { new Dictionary<string, object> { ["Format"] = "srt", ["Method"] = "External" } },
        };
        var corpo = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["UserId"] = UsuarioId, ["DeviceProfile"] = perfil, ["AutoOpenLiveStream"] = true, ["EnableDirectPlay"] = false,
            ["EnableDirectStream"] = false, ["EnableTranscoding"] = true, ["MaxStreamingBitrate"] = 120_000_000,
        });
        using var doc = await Pedir($"/Items/{id}/PlaybackInfo", [("UserId", UsuarioId), ("AutoOpenLiveStream", "true"), ("IsPlayback", "true")], corpo);
        var r = doc.RootElement;
        var fonte = r.TryGetProperty("MediaSources", out var fs) && fs.ValueKind == JsonValueKind.Array && fs.GetArrayLength() > 0 ? fs[0] : default;
        if (fonte.ValueKind != JsonValueKind.Object || Str(fonte, "TranscodingUrl") is not { } caminho)
            throw new ErroDeServidor(Textos.S("O servidor não abriu este canal."));
        var url = conta.Endereco.TrimEnd('/') + caminho;
        url += (url.Contains('?') ? "&" : "?") + string.Join('&', ChaveNaConsulta.Select(p => p.Item1 + "=" + Uri.EscapeDataString(p.Item2)));
        return (url, Str(r, "PlaySessionId") ?? Guid.NewGuid().ToString(), Str(fonte, "LiveStreamId"));
    }

    /// <summary>Fecha a transmissão — sem isto o sintonizador ficaria ocupado com um canal que ninguém vê.</summary>
    public async Task FecharCanal(string transmissao)
    {
        try { await PedirTexto("/LiveStreams/Close", [("LiveStreamId", transmissao)], "{}"); }
        catch { }
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

    private async Task<string> PedirTexto(string caminho, IEnumerable<(string, string)> consulta, string? corpoJson, CancellationToken ct = default, HttpMethod? metodo = null)
    {
        using var pedido = new HttpRequestMessage(metodo ?? (corpoJson != null ? HttpMethod.Post : HttpMethod.Get), Url(caminho, consulta));
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
                temU ? Data(Str(u, "LastPlayedDate")) : null, temU && Bool(u, "IsFavorite")));
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

    /// <summary>
    /// A caixinha "Já reproduzido" do app do Jellyfin: marca ou desmarca o item. Numa série ou
    /// temporada, vale para todos os episódios; e o servidor zera onde parou, então o vídeo sai do
    /// "Continuar" dos dois jeitos.
    /// </summary>
    public Task MarcarVisto(string itemId, bool visto) =>
        PedirTexto($"/Users/{UsuarioId}/PlayedItems/{itemId}", [], visto ? "{}" : null, metodo: visto ? HttpMethod.Post : HttpMethod.Delete);

    /// <summary>O vídeo como o app toca: endereço sem token, título, duração.</summary>
    public Midia ParaTocar(Item i) => new()
    {
        Endereco = Contas.EnderecoMidia(conta, i.Id), Titulo = i.Serie != null && i.Tipo == "Episode" ? $"{i.Serie} · {i.Titulo}" : i.Titulo,
        Origem = Origem.Servidor, Data = i.Criado, Duracao = i.Duracao, Pasta = i.Serie ?? conta.Nome, Visto = i.Visto,
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
    internal static string IdDoAparelho
    {
        get
        {
            if (Prefs.Atual.IdDoAparelho is { } id) return id;
            Prefs.Atual.IdDoAparelho = Guid.NewGuid().ToString();
            Prefs.Atual.Salvar();
            return Prefs.Atual.IdDoAparelho;
        }
    }

    /// <summary>Os usuários que o servidor mostra na tela de entrada — para escolher sem digitar.</summary>
    public static async Task<List<string>> UsuariosVisiveis(Conta c)
    {
        try
        {
            var s = new ServidorDeMidia(new Conta { Id = c.Id, Tipo = c.Tipo, Endereco = c.Endereco });
            using var doc = await s.Pedir("/Users/Public");
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Select(u => Str(u, "Name")).OfType<string>().ToList() : [];
        }
        catch { return []; }
    }

    /// <summary>O código do Quick Connect e o segredo para esperar a aprovação.</summary>
    public static async Task<(string codigo, string segredo)> IniciarQuickConnect(Conta c)
    {
        var s = new ServidorDeMidia(new Conta { Id = c.Id, Tipo = c.Tipo, Endereco = c.Endereco });
        JsonDocument doc;
        // sem conexão sobe como está: quem chama tenta o outro endereço
        try { doc = await s.Pedir("/QuickConnect/Initiate", null, "{}"); }
        catch (ErroDeServidor) { throw new ErroDeServidor(Textos.S("O Quick Connect não está ligado neste servidor (Painel › Geral).")); }
        using (doc)
        {
            if (Str(doc.RootElement, "Code") is not { } codigo || Str(doc.RootElement, "Secret") is not { } segredo)
                throw new ErroDeServidor(Textos.S("O Quick Connect não está ligado neste servidor (Painel › Geral)."));
            return (codigo, segredo);
        }
    }

    /// <summary>Espera a aprovação (num app já conectado) e entra. Devolve o nome, o id do usuário e o token.</summary>
    public static async Task<(string nome, string usuario, string token)> EsperarQuickConnect(Conta c, string segredo, CancellationToken ct)
    {
        var s = new ServidorDeMidia(new Conta { Id = c.Id, Tipo = c.Tipo, Endereco = c.Endereco });
        while (true)
        {
            await Task.Delay(3000, ct);
            using var estado = await s.Pedir("/QuickConnect/Connect", [("Secret", segredo)], null, ct);
            if (estado.RootElement.TryGetProperty("Authenticated", out var a) && a.ValueKind == JsonValueKind.True) break;
        }
        using var doc = await s.Pedir("/Users/AuthenticateWithQuickConnect", null, JsonSerializer.Serialize(new Dictionary<string, string> { ["Secret"] = segredo }), ct);
        var u = doc.RootElement.TryGetProperty("User", out var x) ? x : default;
        return (Str(u, "Name") ?? "", Str(u, "Id") ?? "", Str(doc.RootElement, "AccessToken") ?? "");
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
    /// <summary><c>Convertendo</c>: o servidor converte ou reempacota — o <c>Id</c> é o que já foi no endereço do vídeo.</summary>
    private sealed record Sessao(ServidorDeMidia Servidor, string ItemId, string Id, string Chave, bool Convertendo);

    private static Sessao? _sessao;
    private static long _ultimo;
    private static bool _pausado;
    private static Task _fila = Task.CompletedTask;
    private static readonly Lock Trava = new();

    public static void Comecou(Midia m, double segundos)
    {
        Parou(segundos);
        if (m.Origem != Origem.Servidor || Contas.Partes(m.Endereco) is not { } p) return;
        var conversao = Conversoes.Sessao(p.resto);
        var s = new Sessao(new ServidorDeMidia(p.conta), p.resto, conversao ?? Guid.NewGuid().ToString(), m.Endereco, conversao != null);
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
        if (s.Convertendo)
        {
            // sem isto o ffmpeg do servidor seguiria convertendo, e o sintonizador ficaria preso num canal que ninguém vê
            var transmissao = Conversoes.Transmissao(s.ItemId);
            Conversoes.Encerrar(s.ItemId);
            Enfileirar(async () =>
            {
                await s.Servidor.PararConversao(s.Id);
                if (transmissao != null) await s.Servidor.FecharCanal(transmissao);
            });
        }
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
        Enfileirar(async () => { try { await s.Servidor.Informar(evento, s.ItemId, segundos, pausado, s.Id, s.Convertendo); } catch { } });
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
