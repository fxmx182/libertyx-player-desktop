using System.Net;
using System.Text.Json;
using LibertyX.Player.Core;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace LibertyX.Player.Rede;

/// <summary>Um servidor SMB salvo (PC com pasta compartilhada, NAS, Samba do homelab).</summary>
public sealed class Servidor
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Nome { get; set; } = "";
    public string Host { get; set; } = "";
    public string? Compartilhamento { get; set; }   // vazio = mostrar todos
    public string Usuario { get; set; } = "";
    public string Senha { get; set; } = "";
    public string Dominio { get; set; } = "";
    public bool Convidado { get; set; }
}

/// <summary>
/// Os servidores salvos, em ~/.config/libertyx-player/servidores.json só com permissão do dono
/// (600) — a senha fica ali, como o Android guarda nas preferências do app.
/// </summary>
public static class Servidores
{
    private static string Arquivo => Path.Combine(Caminhos.Config, "servidores.json");
    private static List<Servidor>? _lista;
    public static event Action? Mudou;

    public static List<Servidor> Lista
    {
        get
        {
            if (_lista != null) return _lista;
            try { _lista = File.Exists(Arquivo) ? JsonSerializer.Deserialize<List<Servidor>>(File.ReadAllText(Arquivo)) ?? [] : []; }
            catch { _lista = []; }
            return _lista;
        }
    }

    public static Servidor? Achar(string id) => Lista.FirstOrDefault(s => s.Id == id);

    public static void Salvar(Servidor s)
    {
        var i = Lista.FindIndex(x => x.Id == s.Id);
        if (i >= 0) Lista[i] = s; else Lista.Add(s);
        Gravar();
    }

    public static void Remover(Servidor s) { Lista.RemoveAll(x => x.Id == s.Id); Gravar(); }

    private static void Gravar()
    {
        try
        {
            var tmp = Arquivo + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Lista, new JsonSerializerOptions { WriteIndented = true }));
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, Arquivo, true);
        }
        catch (Exception e) { Diario.Erro(e); }
        Mudou?.Invoke();
    }
}

public sealed record ItemSmb(string Nome, string Caminho, bool Pasta, long Tamanho, DateTime Data);

/// <summary>
/// Uma conexão SMB (SMB2/3, pelo SMBLibrary — C# puro, nada a instalar). Serve para navegar e,
/// pelo <see cref="SmbStream"/>, para o mpv ler o vídeo sem baixar.
/// </summary>
public sealed class ConexaoSmb : IDisposable
{
    private readonly SMB2Client _cliente = new();
    private readonly Dictionary<string, ISMBFileStore> _arvores = [];
    public Servidor Servidor { get; }
    public uint MaxLeitura => Math.Max(65536u, _cliente.MaxReadSize);

    private ConexaoSmb(Servidor s) => Servidor = s;

    /// <summary>Conecta e entra; a exceção traz a frase para mostrar à pessoa.</summary>
    public static ConexaoSmb Abrir(Servidor s)
    {
        var c = new ConexaoSmb(s);
        try { c.Conectar(); return c; }
        catch { c.Dispose(); throw; }
    }

    private void Conectar()
    {
        var host = Servidor.Host.Trim();
        int porta = 445;
        if (host.LastIndexOf(':') is int dp and > 0 && !host.Contains(']') && host.Count(ch => ch == ':') == 1 && int.TryParse(host[(dp + 1)..], out var p))
        { porta = p; host = host[..dp]; }
        IPAddress? ip;
        if (!IPAddress.TryParse(host.Trim('[', ']'), out ip))
        {
            try { ip = Dns.GetHostAddresses(host).OrderBy(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1).FirstOrDefault(); }
            catch { ip = null; }
            if (ip == null) throw new SmbErro(Textos.F("Não achei \"{0}\" na rede.", host));
        }
        bool ok = porta == 445
            ? _cliente.Connect(ip, SMBTransportType.DirectTCPTransport)
            : _cliente.Connect(ip, SMBTransportType.DirectTCPTransport, porta);
        if (!ok) throw new SmbErro(Textos.F("{0} não respondeu na porta {1}. O compartilhamento de arquivos está ligado nele?", host, porta));
        var st = Servidor.Convidado || Servidor.Usuario.Length == 0
            ? Entrar("Guest", "")
            : _cliente.Login(Servidor.Dominio ?? "", Servidor.Usuario, Servidor.Senha ?? "");
        if (st != NTStatus.STATUS_SUCCESS)
            throw new SmbErro(st is NTStatus.STATUS_LOGON_FAILURE or NTStatus.STATUS_ACCESS_DENIED or NTStatus.STATUS_ACCOUNT_RESTRICTION
                ? Textos.S("Usuário ou senha não conferem.")
                : Textos.F("O servidor recusou a entrada ({0}).", st));
    }

    private NTStatus Entrar(string usuario, string senha)
    {
        var st = _cliente.Login("", usuario, senha);
        return st == NTStatus.STATUS_SUCCESS ? st : _cliente.Login("", "", "");
    }

    public List<string> Compartilhamentos()
    {
        var l = _cliente.ListShares(out var st);
        if (st != NTStatus.STATUS_SUCCESS || l == null) throw new SmbErro(Textos.F("Não consegui listar as pastas compartilhadas ({0}).", st));
        return l.Where(n => !n.EndsWith('$')).Order(Natural.Comparador).ToList();
    }

    public ISMBFileStore Arvore(string compartilhamento)
    {
        if (_arvores.TryGetValue(compartilhamento, out var a)) return a;
        a = _cliente.TreeConnect(compartilhamento, out var st);
        if (st != NTStatus.STATUS_SUCCESS || a == null)
            throw new SmbErro(st == NTStatus.STATUS_ACCESS_DENIED ? Textos.S("Sem permissão para esta pasta.") : Textos.F("Não consegui abrir \"{0}\" ({1}).", compartilhamento, st));
        return _arvores[compartilhamento] = a;
    }

    /// <summary>O conteúdo de uma pasta: subpastas e vídeos (o resto não interessa a um player).</summary>
    public List<ItemSmb> Listar(string compartilhamento, string caminho)
    {
        var arv = Arvore(compartilhamento);
        var st = arv.CreateFile(out var h, out _, Win(caminho), AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            FileAttributes.Directory, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT, null);
        if (st != NTStatus.STATUS_SUCCESS) throw new SmbErro(Textos.F("Não consegui abrir a pasta ({0}).", st));
        var itens = new List<ItemSmb>();
        try
        {
            arv.QueryDirectory(out var lista, h, "*", FileInformationClass.FileDirectoryInformation);
            foreach (var i in lista?.OfType<FileDirectoryInformation>() ?? [])
            {
                if (i.FileName is "." or ".." || i.FileName.StartsWith('.') || (i.FileAttributes & FileAttributes.Hidden) != 0) continue;
                bool pasta = (i.FileAttributes & FileAttributes.Directory) != 0;
                if (!pasta && !Biblioteca.EhVideo(i.FileName)) continue;
                itens.Add(new ItemSmb(i.FileName, caminho.Length == 0 ? i.FileName : caminho + "/" + i.FileName, pasta, i.EndOfFile, i.LastWriteTime));
            }
        }
        finally { arv.CloseFile(h); }
        return itens;
    }

    public static string Win(string caminho) => caminho.Replace('/', '\\').Trim('\\');

    public void Dispose()
    {
        foreach (var a in _arvores.Values) try { a.Disconnect(); } catch { }
        try { _cliente.Logoff(); } catch { }
        try { _cliente.Disconnect(); } catch { }
    }

    /// <summary>O endereço que o mpv abre: lxsmb://&lt;id do servidor&gt;/&lt;compartilhamento&gt;/&lt;caminho&gt;.</summary>
    public static string Endereco(Servidor s, string compartilhamento, string caminho) =>
        "lxsmb://" + s.Id + "/" + Uri.EscapeDataString(compartilhamento) + "/" + string.Join('/', caminho.Split('/').Select(Uri.EscapeDataString));

    public static (string id, string comp, string caminho)? Partes(string endereco)
    {
        if (!endereco.StartsWith("lxsmb://")) return null;
        var p = endereco["lxsmb://".Length..].Split('/');
        if (p.Length < 3) return null;
        return (p[0], Uri.UnescapeDataString(p[1]), string.Join('/', p.Skip(2).Select(Uri.UnescapeDataString)));
    }
}

public sealed class SmbErro(string msg) : Exception(msg);
