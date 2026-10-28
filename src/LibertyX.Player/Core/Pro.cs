using System.Security.Cryptography;
using System.Text;

namespace LibertyX.Player.Core;

/// <summary>
/// O LibertyX Pro no Linux — o mesmo modelo do celular: o player inteiro é grátis, e uma compra
/// única (US$ 5, a chave LibertyX do Player) libera quatro coisas. Os sete primeiros dias vêm com
/// tudo liberado, contados pelo próprio app.
///
/// Terminado o teste, nada some: bloqueia só o que é Pro. A biblioteca, o player, a rolagem quadro
/// a quadro, legendas e faixas de áudio continuam.
///
/// A chave é a mesma do Android (produto 2) — quem comprou o Player para a TV já tem o do PC — e o
/// pacote com todos os apps (9) também vale. Ela é conferida de novo a cada abertura, então editar
/// o prefs.json não libera nada.
/// </summary>
public static class Pro
{
    /// <summary>A ordem é a da tela do Pro.</summary>
    public enum Recurso { Rede, Janela, Dormir, Captura }

    public const int DiasDeTeste = 7;
    public const string BotTelegram = "https://t.me/libertx_partition_bot?start=player";
    public const string BotTelegramCurto = "t.me/libertx_partition_bot";
    public const string Preco = "US$ 5";

    public abstract record Estado
    {
        public sealed record Comprado : Estado;
        public sealed record Teste(int DiasRestantes) : Estado;
        public sealed record Expirado : Estado;
    }

    public static Estado Atual { get; private set; } = new Estado.Expirado();
    public static bool Ativo => Atual is not Estado.Expirado;
    public static event Action? Mudou;

    /// <summary>
    /// Build desta máquina (sem a variável CI) = o do dono, liberado — como o dist/ do Android.
    /// O do CI (o release público) é travado. <c>-p:LibertyxLoja=true</c> trava aqui também, para testar.
    /// </summary>
#if LIBERADO
    public const bool Liberado = true;
#else
    public const bool Liberado = false;
#endif

    public static void Iniciar()
    {
        var p = Prefs.Atual;
        // a primeira abertura começa a semana de teste
        if (p.InicioDoTeste == null) { p.InicioDoTeste = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); p.Salvar(); }
        Recalcular();
    }

    public static void Recalcular()
    {
        var p = Prefs.Atual;
        if (Liberado || (p.Chave != null && Chave.Conferir(p.Chave) is Chave.Resultado.Valida)) Atual = new Estado.Comprado();
        else
        {
            long inicio = p.InicioDoTeste ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // relógio atrasado não vira dia negativo nem teste maior
            long passados = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - inicio) / 86_400_000;
            int restam = DiasDeTeste - (int)passados;
            Atual = restam > 0 ? new Estado.Teste(restam) : new Estado.Expirado();
        }
        Mudou?.Invoke();
    }

    public static Chave.Resultado AtivarChave(string texto)
    {
        var r = Chave.Conferir(texto);
        if (r is Chave.Resultado.Valida v) { Prefs.Atual.Chave = v.Texto; Prefs.Atual.Salvar(); Recalcular(); }
        return r;
    }

    /// <summary>O número da licença ativada, para mostrar em Ajustes; null sem chave.</summary>
    public static string? Licenca => Prefs.Atual.Chave is { } c && Chave.Conferir(c) is Chave.Resultado.Valida v ? v.Id : null;

    public static string Nome(Recurso r) => r switch
    {
        Recurso.Rede => Textos.S("Pastas da rede"),
        Recurso.Janela => Textos.S("Mini janela"),
        Recurso.Dormir => Textos.S("Timer para dormir"),
        _ => Textos.S("Captura de quadro"),
    };

    public static string Descricao(Recurso r) => r switch
    {
        Recurso.Rede => Textos.S("Assista direto do PC, do NAS ou do HD na rede, sem copiar nada."),
        Recurso.Janela => Textos.S("O vídeo numa janelinha sempre por cima enquanto você usa outros programas."),
        Recurso.Dormir => Textos.S("Pausa sozinho na hora que você escolher."),
        _ => Textos.S("Salva a cena em Imagens, na resolução original do vídeo."),
    };
}

/// <summary>
/// A chave LibertyX comprada pelo Telegram: "LXP1-" + base32 (Crockford) de 12 bytes de dados e
/// 64 de assinatura ECDSA P-256. Os dados: versão (1), produto (1 Partition, 2 Player, 3 Folder,
/// 9 pacote), dias desde 2026-01-01 e o número da licença. Só a chave pública vem no app.
/// </summary>
public static class Chave
{
    public const byte Produto = 2, Pacote = 9;
    private const string Prefixo = "LXP1";
    private const string ChavePublica = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEGpvUuBp8j84J//alLtBfrcVrOFbf0DhDOIczndBFwCsaOIR6qBKfXWpTNuWoBdXIWXzaPmpT60Q80TXgUCRsRw==";
    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford: sem I, L, O, U

    public abstract record Resultado
    {
        public sealed record Valida(string Texto, string Id) : Resultado;
        public sealed record Recusada(string Motivo) : Resultado;
    }

    public static Resultado Conferir(string texto)
    {
        var s = Normalizar(texto);
        if (!s.StartsWith(Prefixo, StringComparison.Ordinal))
            return new Resultado.Recusada(Textos.S("Isso não parece uma chave completa. Copie de novo no Telegram e cole inteira."));
        var bruto = Decodificar(s[Prefixo.Length..]);
        if (bruto == null || bruto.Length != 76)
            return new Resultado.Recusada(Textos.S("Isso não parece uma chave completa. Copie de novo no Telegram e cole inteira."));
        var dados = bruto.AsSpan(0, 12);
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(ChavePublica), out _);
        if (!ec.VerifyData(dados, bruto.AsSpan(12, 64), HashAlgorithmName.SHA256))
            return new Resultado.Recusada(Textos.S("Esta chave não é válida. Confira se copiou ela toda."));
        byte produto = dados[1];
        if (dados[0] == 1 && produto is 1 or 3) return new Resultado.Recusada(Textos.S("Esta chave é de outro app LibertyX, não do Player."));
        if (dados[0] != 1 || produto is not (Produto or Pacote))
            return new Resultado.Recusada(Textos.S("Esta chave é de uma versão mais nova. Atualize o app e tente de novo."));
        return new Resultado.Valida(texto.Trim(), Convert.ToHexString(dados[4..12]));
    }

    private static string Normalizar(string t)
    {
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t.ToUpperInvariant())
        {
            if (ch is '-' or ' ' or '\t' or '\r' or '\n') continue;
            sb.Append(ch switch { 'O' => '0', 'I' or 'L' => '1', _ => ch });
        }
        // o prefixo tem um L de verdade: desfaz a troca só nele
        var s = sb.ToString();
        return s.StartsWith("1XP1", StringComparison.Ordinal) ? "LXP1" + s[4..] : s;
    }

    private static byte[]? Decodificar(string s)
    {
        var o = new List<byte>();
        int buf = 0, bits = 0;
        foreach (var ch in s)
        {
            int v = Alfabeto.IndexOf(ch);
            if (v < 0) return null;
            buf = (buf << 5) | v; bits += 5;
            if (bits >= 8) { o.Add((byte)(buf >> (bits - 8))); bits -= 8; }
            buf &= (1 << bits) - 1;
        }
        return [.. o];
    }
}
