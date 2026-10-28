using LibertyX.Player.Core;
using LibertyX.Player.Mpv;

namespace LibertyX.Player;

/// <summary>
/// O autoteste do CI, sem tela: <c>LibertyXPlayer --autoteste relatorio.txt video.mp4</c>.
/// Prova que o libmpv da distribuição carrega, que o vídeo abre e que a renderização em memória
/// (a das miniaturas e a de quem não tem OpenGL) devolve imagem de verdade — e confere a chave.
/// Cada linha é "OK …" ou "FALHA …"; a última resume. Sai com 1 se alguma falhou.
/// </summary>
public static class Autoteste
{
    public static int Rodar(string saida, string? video)
    {
        var linhas = new List<string>();
        int falhas = 0;
        void Ok(string t) => linhas.Add("OK " + t);
        void Falha(string t) { linhas.Add("FALHA " + t); falhas++; }

        if (!LibMpv.Disponivel(out var erro)) Falha("libmpv não carregou: " + erro);
        else
        {
            Ok($"libmpv carregou (API {LibMpv.mpv_client_api_version() >> 16}.{LibMpv.mpv_client_api_version() & 0xffff})");
            if (video == null) Falha("sem vídeo de teste");
            else
            {
                var q = Miniaturas.Quadro(Path.GetFullPath(video), false);
                if (q == null) Falha("o vídeo não abriu ou não desenhou");
                else
                {
                    Ok($"quadro {q.Largura}×{q.Altura}, duração {q.Duracao:0.0} s");
                    if (q.Duracao <= 0) Falha("duração não lida");
                    if (Miniaturas.Escuro(q.Pixels)) Falha("o quadro saiu preto"); else Ok("o quadro tem imagem");
                }
            }
        }
        if (Chave.Conferir("LXP1-000000-000000") is Chave.Resultado.Recusada) Ok("chave falsa recusada"); else Falha("chave falsa aceita");
        foreach (var lang in new[] { "pt", "en", "es" })
        {
            Textos.Iniciar(lang);
            var t = Textos.S("Continuar assistindo");
            if (lang == "pt" || t != "Continuar assistindo") Ok($"idioma {lang}: {t}"); else Falha($"idioma {lang} sem tradução");
        }
        linhas.Add(falhas == 0 ? "TUDO OK" : $"{falhas} FALHA(S)");
        File.WriteAllLines(saida, linhas);
        foreach (var l in linhas) Console.WriteLine(l);
        return falhas == 0 ? 0 : 1;
    }
}
