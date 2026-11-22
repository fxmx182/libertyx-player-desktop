namespace LibertyX.Player.Core;

/// <summary>
/// O LibertyX como player padrão de vídeos no Linux. Nenhum instalador do Linux pergunta nada (apt,
/// Flatpak e Snap instalam calados), então quem pergunta é o app, na primeira vez que abre.
/// Instalado pelo .deb, pelo .rpm ou como AppImage, ele mesmo grava a escolha no mimeapps.list
/// (o arquivo que GNOME, KDE, COSMIC, Cinnamon e XFCE leem). No Flatpak e no Snap a caixinha da loja
/// não deixa mexer nesse arquivo: o app só explica onde escolher.
/// </summary>
public static class PlayerPadrao
{
    /// <summary>Os mesmos tipos do MimeType= do .desktop.</summary>
    public static readonly string[] Tipos =
    [
        "video/mp4", "video/x-matroska", "video/webm", "video/quicktime", "video/x-msvideo", "video/mpeg", "video/x-flv",
        "video/x-ms-wmv", "video/3gpp", "video/ogg", "video/mp2t", "video/x-m4v", "video/vnd.avi", "application/x-matroska",
    ];

    public static bool Flatpak => Environment.GetEnvironmentVariable("FLATPAK_ID") is { Length: > 0 };
    public static bool Snap => Environment.GetEnvironmentVariable("SNAP_NAME") is { Length: > 0 };
    public static bool DeLoja => Flatpak || Snap;

    /// <summary>O nome do .desktop deste jeito de instalar; null quando não há atalho (o .tar.gz solto)
    /// — sem atalho o sistema não tem o que chamar de padrão.</summary>
    public static string? Atalho
    {
        get
        {
            if (!OperatingSystem.IsLinux()) return null;
            if (Flatpak) return Environment.GetEnvironmentVariable("FLATPAK_ID") + ".desktop";
            if (Snap) return Environment.GetEnvironmentVariable("SNAP_NAME") + "_libertyx-player.desktop";
            if (Atualizacao.AppImage != null) return "libertyx-player-appimage.desktop";
            return File.Exists("/usr/share/applications/libertyx-player.desktop") ? "libertyx-player.desktop" : null;
        }
    }

    /// <summary>O mimeapps.list de quem usa. No Flatpak o XDG_CONFIG_HOME é o da caixinha: o de verdade
    /// fica no ~/.config (só para ler).</summary>
    private static string Lista => Path.Combine(
        !Flatpak && Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x && Path.IsPathRooted(x) ? x
            : Path.Combine(Caminhos.Home, ".config"), "mimeapps.list");

    /// <summary>O mp4 já abre no LibertyX (o tipo que todo mundo tem; os outros vão juntos).</summary>
    public static bool EhPadrao()
    {
        if (Atalho is not { } a) return false;
        try
        {
            if (!File.Exists(Lista)) return false;
            string? secao = null;
            foreach (var l in File.ReadLines(Lista))
            {
                var t = l.Trim();
                if (t.StartsWith('[')) { secao = t; continue; }
                if (secao == "[Default Applications]" && t.StartsWith("video/mp4="))
                    return t["video/mp4=".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == a;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Grava o LibertyX como padrão de todos os tipos de vídeo. Só fora das lojas.</summary>
    public static bool Tornar()
    {
        if (DeLoja || Atalho is not { } a) return false;
        try
        {
            var linhas = File.Exists(Lista) ? File.ReadAllLines(Lista).ToList() : [];
            foreach (var tipo in Tipos)
            {
                Definir(linhas, "[Default Applications]", tipo, _ => a + ";");
                // em "Abrir com", o LibertyX primeiro, sem tirar os outros que a pessoa já tinha
                Definir(linhas, "[Added Associations]", tipo, antes =>
                    string.Join(";", new[] { a }.Concat((antes ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Where(x => x != a))) + ";");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Lista)!);
            var tmp = Lista + ".libertyx";
            File.WriteAllLines(tmp, linhas);
            File.Move(tmp, Lista, true);
            Diario.Escrever("player padrão: " + a);
            return true;
        }
        catch (Exception e) { Diario.Erro(e); return false; }
    }

    /// <summary>Troca (ou acrescenta) "chave=valor" dentro da seção, criando a seção se faltar.</summary>
    private static void Definir(List<string> linhas, string secao, string chave, Func<string?, string> valor)
    {
        int ini = linhas.FindIndex(l => l.Trim() == secao);
        if (ini < 0)
        {
            if (linhas.Count > 0 && linhas[^1].Trim().Length > 0) linhas.Add("");
            linhas.Add(secao);
            linhas.Add($"{chave}={valor(null)}");
            return;
        }
        int fim = ini + 1;
        while (fim < linhas.Count && !linhas[fim].TrimStart().StartsWith('[')) fim++;
        for (int i = ini + 1; i < fim; i++)
        {
            var t = linhas[i].Trim();
            if (t.StartsWith(chave + "="))
            {
                linhas[i] = $"{chave}={valor(t[(chave.Length + 1)..])}";
                return;
            }
        }
        // logo depois da última linha com conteúdo da seção (não depois da linha em branco que a separa da próxima)
        int pos = fim;
        while (pos > ini + 1 && linhas[pos - 1].Trim().Length == 0) pos--;
        linhas.Insert(pos, $"{chave}={valor(null)}");
    }
}
