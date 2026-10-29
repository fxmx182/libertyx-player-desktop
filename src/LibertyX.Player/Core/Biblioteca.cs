namespace LibertyX.Player.Core;

public enum Origem { Arquivo, Smb, Url }

/// <summary>
/// Um vídeo, de onde quer que venha. <see cref="Endereco"/> é o que o mpv abre: o caminho do
/// arquivo, <c>lxsmb://…</c> (o SMB do próprio app) ou um http. É também a chave da retomada e
/// da miniatura.
/// </summary>
public sealed class Midia
{
    public required string Endereco { get; init; }
    public required string Titulo { get; init; }
    public Origem Origem { get; init; }
    public long Tamanho { get; init; }
    public DateTime Data { get; init; }
    /// <summary>O nome da pasta, para o cabeçalho e para o "continuar assistindo".</summary>
    public string? Pasta { get; init; }

    public static Midia DeArquivo(string caminho)
    {
        var fi = new FileInfo(caminho);
        // link simbólico: o nome é o do link; tamanho e data, os do arquivo de verdade
        var dados = fi.LinkTarget != null && fi.ResolveLinkTarget(true) is FileInfo alvo && alvo.Exists ? alvo : fi;
        return new Midia
        {
            Endereco = fi.FullName, Titulo = Path.GetFileNameWithoutExtension(fi.Name), Origem = Origem.Arquivo,
            Tamanho = dados.Exists ? dados.Length : 0, Data = dados.Exists ? dados.LastWriteTime : default,
            Pasta = Path.GetFileName(Path.GetDirectoryName(fi.FullName)),
        };
    }

    public static Midia DeUrl(string url) => new()
    {
        Endereco = url, Origem = Origem.Url,
        Titulo = Uri.TryCreate(url, UriKind.Absolute, out var u) && Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(u.AbsolutePath)) is { Length: > 0 } n ? n : url,
    };
}

public sealed class PastaDeVideos
{
    public required string Caminho { get; init; }
    public required string Nome { get; init; }
    public required List<Midia> Videos { get; init; }
    public DateTime MaisRecente => Videos.Count == 0 ? default : Videos.Max(v => v.Data);
}

/// <summary>
/// A biblioteca: os vídeos do computador agrupados por pasta. O Linux não tem um índice de mídia
/// como o MediaStore do Android, então o app varre — Vídeos, Downloads, as pastas que a pessoa
/// acrescentou e os pendrives/HDs montados (/media e /run/media).
/// </summary>
public static class Biblioteca
{
    public static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".wmv", ".flv", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts",
        ".3gp", ".ogv", ".vob", ".divx", ".rmvb", ".rm", ".asf", ".f4v", ".mxf", ".y4m",
    };

    public static readonly HashSet<string> ExtensoesLegenda = new(StringComparer.OrdinalIgnoreCase)
    { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".sup", ".idx" };

    public static bool EhVideo(string caminho) => Extensoes.Contains(Path.GetExtension(caminho));

    /// <summary>As raízes da varredura, sem repetição (uma dentro da outra conta uma vez).</summary>
    public static List<string> Raizes()
    {
        var l = new List<string> { Caminhos.Videos };
        if (OperatingSystem.IsWindows()) l.Add(Caminhos.DownloadsWindows);
        else if (Caminhos.PastaXdg("XDG_DOWNLOAD_DIR") is { } d) l.Add(d); else l.Add(Path.Combine(Caminhos.Home, "Downloads"));
        l.AddRange(Prefs.Atual.Pastas);
        l.AddRange(Removiveis());
        if (OperatingSystem.IsWindows())
        {
            // "E:\" fica "E:\" (sem a barra, "E:" seria a pasta atual do drive); maiúscula/minúscula não contam
            var win = l.Where(Directory.Exists).Select(p => Path.GetFullPath(p))
                .Select(p => p.Length > (Path.GetPathRoot(p)?.Length ?? 0) ? p.TrimEnd('\\') : p)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return win.Where(p => !win.Any(o => !o.Equals(p, StringComparison.OrdinalIgnoreCase)
                && p.StartsWith(o.EndsWith('\\') ? o : o + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
        }
        var unicas = l.Where(Directory.Exists).Select(p => Path.GetFullPath(p).TrimEnd('/')).Distinct().ToList();
        return unicas.Where(p => !unicas.Any(o => o != p && p.StartsWith(o + "/"))).ToList();
    }

    /// <summary>Pendrives e HDs que o desktop montou para o usuário. No Windows: os outros drives (D:, E:,
    /// pendrive) — o C: inteiro não, que é o sistema; dele entram só as pastas do usuário.</summary>
    public static IEnumerable<string> Removiveis()
    {
        if (OperatingSystem.IsWindows())
        {
            string sistema = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            // a lista de drives pode falhar (o Wine, um drive de rede caído): sem ela, a biblioteca ainda varre o resto
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { yield break; }
            foreach (var di in drives)
            {
                bool ok;
                try { ok = di.IsReady && di.DriveType is DriveType.Fixed or DriveType.Removable; } catch { continue; }
                if (ok && !di.Name.Equals(sistema, StringComparison.OrdinalIgnoreCase)) yield return di.Name;
            }
            yield break;
        }
        string user = Environment.UserName;
        foreach (var b in new[] { $"/media/{user}", $"/run/media/{user}" })
        {
            string[] dirs;
            try { dirs = Directory.Exists(b) ? Directory.GetDirectories(b) : []; } catch { continue; }
            foreach (var d in dirs) yield return d;
        }
    }

    public static List<PastaDeVideos> Varrer(CancellationToken ct = default)
    {
        var porPasta = new Dictionary<string, List<Midia>>();
        var ocultas = Prefs.Atual.PastasOcultas.ToHashSet();
        foreach (var raiz in Raizes()) Andar(raiz, 0, porPasta, ct);
        return porPasta
            .Where(kv => !ocultas.Contains(kv.Key))
            .Select(kv => new PastaDeVideos { Caminho = kv.Key, Nome = Path.GetFileName(kv.Key) is { Length: > 0 } n ? n : kv.Key, Videos = kv.Value })
            .OrderByDescending(p => p.MaisRecente)
            .ToList();
    }

    private static void Andar(string dir, int nivel, Dictionary<string, List<Midia>> saida, CancellationToken ct)
    {
        if (nivel > 8 || ct.IsCancellationRequested) return;
        try
        {
            if (File.Exists(Path.Combine(dir, ".nomedia"))) return;
            var videos = new List<Midia>();
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (!EhVideo(f) || Path.GetFileName(f).StartsWith('.')) continue;
                try { var m = Midia.DeArquivo(f); if (m.Tamanho > 0) videos.Add(m); } catch { }
            }
            if (videos.Count > 0) saida[dir] = videos;
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var nome = Path.GetFileName(d);
                if (nome.StartsWith('.') || nome is "node_modules" or "$RECYCLE.BIN" or "System Volume Information" or "lost+found") continue;
                // no Windows as pastas do sistema se escondem pelo atributo, não pelo ponto
                if (OperatingSystem.IsWindows() && (nome is "Windows" or "Program Files" or "Program Files (x86)" or "ProgramData" or "$WinREAgent"
                    || (File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) != 0)) continue;
                if (new DirectoryInfo(d).LinkTarget != null) continue;   // links não: evitam laço e repetição
                Andar(d, nivel + 1, saida, ct);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>Os vídeos ao lado de um arquivo (para próximo/anterior e para o "abrir com").</summary>
    public static List<Midia> Vizinhos(string arquivo)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(arquivo))!;
            return Directory.EnumerateFiles(dir).Where(EhVideo).Order(Natural.Comparador).Select(Midia.DeArquivo).ToList();
        }
        catch { return [Midia.DeArquivo(arquivo)]; }
    }

    public static IEnumerable<Midia> Ordenar(IEnumerable<Midia> l, string ordem) => ordem switch
    {
        "data" => l.OrderByDescending(m => m.Data),
        "tamanho" => l.OrderByDescending(m => m.Tamanho),
        _ => l.OrderBy(m => m.Titulo, Natural.Comparador),
    };
}

/// <summary>"Episódio 2" antes de "Episódio 10", como qualquer gerenciador de arquivos ordena.</summary>
public sealed class Natural : IComparer<string>
{
    public static readonly Natural Comparador = new();

    public int Compare(string? a, string? b)
    {
        if (a == null || b == null) return string.Compare(a, b, StringComparison.Ordinal);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length - nb.Length;
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = string.Compare(a[i].ToString(), b[j].ToString(), Textos.Cultura, System.Globalization.CompareOptions.IgnoreCase);
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i) - (b.Length - j);
    }
}
