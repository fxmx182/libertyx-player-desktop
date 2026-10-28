using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LibertyX.Player.Core;
using LibertyX.Player.Rede;
using SMBLibrary;
using SMBLibrary.Client;
using static LibertyX.Player.Mpv.LibMpv;

namespace LibertyX.Player.Mpv;

/// <summary>
/// O SMB entregue ao mpv como um protocolo dele, <c>lxsmb://</c> (stream_cb do libmpv): o mpv pede
/// "n bytes a partir daqui" e o app lê do servidor. A senha nunca passa pelo endereço — ele só leva
/// o id do servidor salvo (a lição do Android, onde o VLC descartava a senha da URL em silêncio).
///
/// Cada vídeo aberto tem a própria conexão: o player e o gerador de miniaturas leem ao mesmo tempo
/// sem disputar a mesma sessão.
/// </summary>
public static unsafe class SmbStream
{
    private static readonly ConcurrentDictionary<IntPtr, Leitor> Abertos = new();
    private static long _proximo;

    public static void Registrar(MpvCore mpv)
    {
        var nome = Utf8("lxsmb");
        try { mpv_stream_cb_add_ro(mpv.Handle, (byte*)nome, IntPtr.Zero, &Abrir); }
        finally { Marshal.FreeCoTaskMem(nome); }
    }

    [UnmanagedCallersOnly]
    private static int Abrir(IntPtr _, byte* uri, StreamCbInfo* info)
    {
        try
        {
            var endereco = Marshal.PtrToStringUTF8((IntPtr)uri) ?? "";
            if (ConexaoSmb.Partes(endereco) is not { } p || Servidores.Achar(p.id) is not { } srv) return ErrorLoadingFailed;
            var l = new Leitor(srv, p.comp, p.caminho);
            var cookie = (IntPtr)Interlocked.Increment(ref _proximo);
            Abertos[cookie] = l;
            info->cookie = cookie;
            info->read_fn = &Ler;
            info->seek_fn = &Buscar;
            info->size_fn = &Tamanho;
            info->close_fn = &Fechar;
            info->cancel_fn = &Cancelar;
            return 0;
        }
        catch (Exception e) { Diario.Escrever("smb abrir: " + e.Message); return ErrorLoadingFailed; }
    }

    [UnmanagedCallersOnly]
    private static long Ler(IntPtr cookie, byte* buf, ulong n)
    {
        try { return Abertos.TryGetValue(cookie, out var l) ? l.Ler(new Span<byte>(buf, (int)Math.Min(n, int.MaxValue))) : -1; }
        catch (Exception e) { Diario.Escrever("smb ler: " + e.Message); return -1; }
    }

    [UnmanagedCallersOnly]
    private static long Buscar(IntPtr cookie, long pos)
    {
        if (!Abertos.TryGetValue(cookie, out var l) || pos < 0) return ErrorGeneric;
        l.Posicao = pos;
        return pos;
    }

    [UnmanagedCallersOnly]
    private static long Tamanho(IntPtr cookie) => Abertos.TryGetValue(cookie, out var l) ? l.Total : ErrorUnsupported;

    [UnmanagedCallersOnly]
    private static void Fechar(IntPtr cookie) { if (Abertos.TryRemove(cookie, out var l)) l.Dispose(); }

    [UnmanagedCallersOnly]
    private static void Cancelar(IntPtr cookie) { if (Abertos.TryGetValue(cookie, out var l)) l.Cancelado = true; }

    /// <summary>
    /// Um arquivo aberto, com leitura em blocos: o mpv pede pedaços pequenos, e cada ida ao servidor
    /// custa uma volta na rede — então busca 1–4 MB por vez e serve dali.
    /// </summary>
    private sealed class Leitor : IDisposable
    {
        private readonly ConexaoSmb _con;
        private readonly ISMBFileStore _arv;
        private readonly object _h;
        private readonly int _bloco;
        private byte[] _buf = [];
        private long _bufInicio = -1;
        public long Posicao;
        public long Total { get; }
        public volatile bool Cancelado;

        public Leitor(Servidor s, string comp, string caminho)
        {
            _con = ConexaoSmb.Abrir(s);
            try
            {
                _arv = _con.Arvore(comp);
                var st = _arv.CreateFile(out _h, out _, ConexaoSmb.Win(caminho), AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                    SMBLibrary.FileAttributes.Normal, ShareAccess.Read | ShareAccess.Write, CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_NONALERT, null);
                if (st != NTStatus.STATUS_SUCCESS) throw new SmbErro(st.ToString());
                _arv.GetFileInformation(out var fi, _h, FileInformationClass.FileStandardInformation);
                Total = (fi as FileStandardInformation)?.EndOfFile ?? -1;
                _bloco = (int)Math.Clamp(_con.MaxLeitura, 1u << 20, 4u << 20);
            }
            catch { _con.Dispose(); throw; }
        }

        public long Ler(Span<byte> destino)
        {
            if (Cancelado) return -1;
            if (Total >= 0 && Posicao >= Total) return 0;
            if (_bufInicio < 0 || Posicao < _bufInicio || Posicao >= _bufInicio + _buf.Length)
            {
                int pedir = (int)Math.Min(_bloco, Math.Max(destino.Length, 65536));
                var lidos = new List<byte[]>();
                int total = 0;
                // o servidor pode limitar cada leitura abaixo do bloco: junta até completar
                while (total < pedir)
                {
                    if (Cancelado) return -1;
                    int quanto = (int)Math.Min(pedir - total, _con.MaxLeitura);
                    var st = _arv.ReadFile(out var dados, _h, Posicao + total, quanto);
                    if (st == NTStatus.STATUS_END_OF_FILE || dados == null || dados.Length == 0) break;
                    if (st != NTStatus.STATUS_SUCCESS) { if (total == 0) return -1; break; }
                    lidos.Add(dados); total += dados.Length;
                    if (dados.Length < quanto) break;
                }
                if (total == 0) return 0;
                _buf = lidos.Count == 1 ? lidos[0] : lidos.SelectMany(b => b).ToArray();
                _bufInicio = Posicao;
            }
            int off = (int)(Posicao - _bufInicio);
            int n = Math.Min(destino.Length, _buf.Length - off);
            _buf.AsSpan(off, n).CopyTo(destino);
            Posicao += n;
            return n;
        }

        public void Dispose()
        {
            try { _arv.CloseFile(_h); } catch { }
            _con.Dispose();
        }
    }
}
