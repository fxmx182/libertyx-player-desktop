using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibertyX.Player.Core;

/// <summary>Onde o app guarda as coisas, pelo padrão XDG (~/.config, ~/.cache).</summary>
public static class Caminhos
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Xdg(string var, string padrao) =>
        Environment.GetEnvironmentVariable(var) is { Length: > 0 } v && Path.IsPathRooted(v) ? v : Path.Combine(Home, padrao);

    public static string Config => Dir(Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "libertyx-player"));
    public static string Cache => Dir(Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "libertyx-player"));
    public static string Miniaturas => Dir(Path.Combine(Cache, "miniaturas"));

    /// <summary>A pasta "Vídeos" do usuário, do jeito que o desktop a chama (user-dirs.dirs).</summary>
    public static string? PastaXdg(string chave)
    {
        try
        {
            var f = Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "user-dirs.dirs");
            if (!File.Exists(f)) return null;
            foreach (var l in File.ReadAllLines(f))
                if (l.StartsWith(chave + "="))
                {
                    var v = l[(chave.Length + 1)..].Trim().Trim('"').Replace("$HOME", Home);
                    return v.TrimEnd('/') == Home ? null : v;
                }
        }
        catch { }
        return null;
    }

    public static string Videos => PastaXdg("XDG_VIDEOS_DIR") ?? Path.Combine(Home, "Videos");
    public static string Imagens => PastaXdg("XDG_PICTURES_DIR") ?? Path.Combine(Home, "Pictures");

    private static string Dir(string d) { Directory.CreateDirectory(d); return d; }
}

/// <summary>
/// As preferências, num JSON só (~/.config/libertyx-player/prefs.json). Gravadas a cada mudança —
/// são poucas e pequenas, e o app pode ser fechado de qualquer jeito.
/// </summary>
public sealed class Prefs
{
    public string? Idioma { get; set; }                 // null = o do sistema
    public double? Escala { get; set; }                 // null = automática
    public List<string> Pastas { get; set; } = [];      // além de Vídeos e Downloads
    public List<string> PastasOcultas { get; set; } = [];
    public bool Decodificacao { get; set; } = true;     // hwdec
    public bool RolagemExata { get; set; } = true;      // quadro exato debaixo do mouse ao arrastar
    public int Salto { get; set; } = 10;                // segundos das setas e do clique duplo nas laterais
    public double VelocidadeSegurar { get; set; } = 2;
    public double Volume { get; set; } = 100;
    public bool Aleatorio { get; set; }
    public bool Repetir { get; set; }
    public string Ordem { get; set; } = "nome";         // nome | data | tamanho
    public bool Lista { get; set; }                     // vídeos em lista em vez de grade
    public string? PastaCapturas { get; set; }
    public string? IdiomaAudio { get; set; }            // "por" "eng"… (alang do mpv)
    public string? IdiomaLegenda { get; set; }
    public bool GuiaVisto { get; set; }

    // Pro (o mesmo modelo do Android: 7 dias de teste contados pelo app, ou a chave LibertyX)
    public long? InicioDoTeste { get; set; }
    public string? Chave { get; set; }

    /// <summary>Onde cada vídeo parou (chave = caminho ou endereço), e quando foi visto.</summary>
    public Dictionary<string, Retomada> Posicoes { get; set; } = [];

    [JsonIgnore] public static Prefs Atual { get; private set; } = Carregar();

    private static string Arquivo => Path.Combine(Caminhos.Config, "prefs.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Lock Trava = new();

    private static Prefs Carregar()
    {
        try { if (File.Exists(Arquivo)) return JsonSerializer.Deserialize<Prefs>(File.ReadAllText(Arquivo)) ?? new(); }
        catch { }
        return new();
    }

    public void Salvar()
    {
        lock (Trava)
        {
            try
            {
                // poda: os 400 vistos mais recentemente bastam para "continuar assistindo"
                if (Posicoes.Count > 400)
                    foreach (var k in Posicoes.OrderBy(p => p.Value.Quando).Take(Posicoes.Count - 400).Select(p => p.Key).ToList())
                        Posicoes.Remove(k);
                var tmp = Arquivo + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
                File.Move(tmp, Arquivo, true);
            }
            catch { }
        }
    }
}

public sealed class Retomada
{
    public double Posicao { get; set; }
    public double Duracao { get; set; }
    public long Quando { get; set; }
    public string? Titulo { get; set; }
    [JsonIgnore] public double Fracao => Duracao > 0 ? Math.Clamp(Posicao / Duracao, 0, 1) : 0;
}
