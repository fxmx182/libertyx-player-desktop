using Avalonia.Platform;

namespace LibertyX.Player.Core;

/// <summary>
/// AppImage não entra no menu de aplicativos sozinho (o .deb e o .rpm entram): o app cria o atalho e o
/// ícone na pasta de quem usa (~/.local/share), apontando para onde o AppImage está agora — e refaz se
/// ele mudar de lugar. Com o atalho vem o "Abrir com" nos vídeos. O mesmo que o Partition faz.
/// </summary>
public static class AtalhoAppImage
{
    public static void Criar()
    {
        try
        {
            if (Atualizacao.AppImage is not { } ai) return;
            string share = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            string desktop = Path.Combine(share, "applications", "libertyx-player-appimage.desktop");
            string icone = Path.Combine(share, "icons", "hicolor", "256x256", "apps", "libertyx-player.png");
            string conteudo = $"""
                [Desktop Entry]
                Type=Application
                Name=LibertyX Player
                GenericName=Player de vídeo
                GenericName[en]=Video player
                GenericName[es]=Reproductor de video
                Comment=Seus vídeos e o servidor de casa, com rolagem quadro a quadro
                Comment[en]=Your videos and your home server, with frame-accurate seeking
                Comment[es]=Tus videos y el servidor de casa, con búsqueda fotograma a fotograma
                Exec="{ai}" %U
                Icon=libertyx-player
                Terminal=false
                Categories=AudioVideo;Video;Player;
                Keywords=video;player;movie;mkv;subtitles;smb;samba;nas;mx player;vídeo;filme;legenda;
                StartupWMClass=libertyx-player
                MimeType=video/mp4;video/x-matroska;video/webm;video/quicktime;video/x-msvideo;video/mpeg;video/x-flv;video/x-ms-wmv;video/3gpp;video/ogg;video/mp2t;video/x-m4v;video/vnd.avi;application/x-matroska;
                X-AppImage-Path={ai}

                """;
            if (File.Exists(desktop) && File.ReadAllText(desktop) == conteudo && File.Exists(icone)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(desktop)!);
            Directory.CreateDirectory(Path.GetDirectoryName(icone)!);
            File.WriteAllText(desktop, conteudo);
            using (var origem = AssetLoader.Open(new Uri("avares://LibertyXPlayer/Assets/icone-256.png")))
            using (var saida = File.Create(icone))
                origem.CopyTo(saida);
            try { System.Diagnostics.Process.Start("update-desktop-database", ["-q", Path.GetDirectoryName(desktop)!])?.Dispose(); } catch { }
        }
        catch (Exception e) { Diario.Erro(e); }
    }
}
