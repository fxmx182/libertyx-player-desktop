using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LibertyX.Player.Rede;

public sealed record Achado(string Nome, string Endereco);

/// <summary>
/// Achar o servidor sozinho — as mesmas duas técnicas do celular, porque cada uma sozinha deixa
/// buraco: o anúncio mDNS (<c>_smb._tcp</c>, pelo avahi do próprio desktop) traz o nome legível de
/// quem se anuncia; a varredura da porta 445 acha qualquer um que aceite SMB, mas só pelo endereço.
/// </summary>
public static class Descoberta
{
    public static async Task Procurar(Action<Achado> achou, CancellationToken ct)
    {
        var vistos = new HashSet<string>();
        var trava = new Lock();
        void Novo(Achado a)
        {
            lock (trava) { if (!vistos.Add(a.Endereco)) return; }
            achou(a);
        }
        // no Windows não há avahi; o nome vem pelo NetBIOS/LLMNR na resolução reversa da varredura
        await Task.WhenAll(OperatingSystem.IsWindows() ? Task.CompletedTask : Avahi(Novo, ct), Varrer(Novo, ct));
    }

    private static async Task Avahi(Action<Achado> achou, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("avahi-browse", "-rptk _smb._tcp") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p == null) return;
            using var reg = ct.Register(() => { try { p.Kill(); } catch { } });
            string? linha;
            while ((linha = await p.StandardOutput.ReadLineAsync(ct)) != null)
            {
                // =;eth0;IPv4;NOME;_smb._tcp;local;host.local;192.168.0.10;445;
                var f = linha.Split(';');
                if (f.Length < 9 || f[0] != "=" || f[2] != "IPv4") continue;
                achou(new Achado(Desescapar(f[3]), f[7]));
            }
        }
        catch { /* sem avahi: fica a varredura */ }
    }

    private static string Desescapar(string s)
    {
        // o avahi escreve caracteres especiais como \032 (decimal)
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 3 < s.Length && int.TryParse(s.AsSpan(i + 1, 3), out var c)) { sb.Append((char)c); i += 3; }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>As /24 das redes do computador e dos servidores já salvos — 32 de cada vez, 700 ms de prazo.</summary>
    private static async Task Varrer(Action<Achado> achou, CancellationToken ct)
    {
        var redes = new HashSet<string>();
        var meus = new HashSet<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            // redes virtuais do próprio computador (docker, VMs) não têm servidor de ninguém
            if (ni.Name.StartsWith("docker") || ni.Name.StartsWith("br-") || ni.Name.StartsWith("veth") || ni.Name.StartsWith("virbr")) continue;
            if (OperatingSystem.IsWindows() && (ni.Name.StartsWith("vEthernet") || ni.Description.Contains("Hyper-V")
                || ni.Description.Contains("VirtualBox") || ni.Description.Contains("VMware") || ni.Description.Contains("WSL"))) continue;
            foreach (var a in ni.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.PrefixLength < 16) continue;
                var b = a.Address.GetAddressBytes();
                redes.Add($"{b[0]}.{b[1]}.{b[2]}");
                meus.Add(a.Address.ToString());
            }
        }
        foreach (var s in Servidores.Lista)
            if (IPAddress.TryParse(s.Host.Split(':')[0], out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            { var b = ip.GetAddressBytes(); redes.Add($"{b[0]}.{b[1]}.{b[2]}"); }

        var alvos = redes.SelectMany(r => Enumerable.Range(1, 254).Select(i => $"{r}.{i}")).Where(a => !meus.Contains(a)).ToList();
        using var sem = new SemaphoreSlim(32);
        await Task.WhenAll(alvos.Select(async a =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!await Responde(a, 445, 700, ct)) return;
                string nome = a;
                try
                {
                    var t = Dns.GetHostEntryAsync(a);
                    if (await Task.WhenAny(t, Task.Delay(800, ct)) == t && t.Result.HostName is { Length: > 0 } h && h != a) nome = h.Split('.')[0];
                }
                catch { }
                achou(new Achado(nome, a));
            }
            catch (OperationCanceledException) { }
            finally { sem.Release(); }
        }));
    }

    public static async Task<bool> Responde(string host, int porta, int ms, CancellationToken ct)
    {
        using var c = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ms);
        try { await c.ConnectAsync(host, porta, cts.Token); return true; }
        catch { return false; }
    }
}
