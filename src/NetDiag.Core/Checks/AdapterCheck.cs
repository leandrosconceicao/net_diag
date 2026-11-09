using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>Camada 1: o computador está conectado e recebeu configuração (IP, gateway, DNS)?</summary>
public sealed class AdapterCheck : IDiagnosticCheck
{
    public string Id => "adapter";
    public string Name => "Adaptador de rede";
    public string Description => "Verifica se o computador está conectado e recebeu configuração do roteador.";

    private static readonly string[] VirtualKeywords =
        ["virtual", "hyper-v", "vmware", "virtualbox", "vethernet", "loopback", "bluetooth"];

    private static readonly string[] VpnKeywords =
        ["vpn", "wireguard", "tap-", "tap ", "tailscale", "zerotier", "openvpn", "fortinet",
         "anyconnect", "pangp", "wintun", "nordlynx", "proton", "radmin", "hamachi"];

    public Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct)
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToList();

        DetectVpn(up, r);
        DetectProxy(r);

        // Preferimos uma placa física com gateway; se não houver, aceitamos virtual (ex.: switch externo do Hyper-V).
        var withGateway = up.Where(n => GetIPv4Gateway(n) is not null).ToList();
        var active = withGateway.FirstOrDefault(n => !IsVirtual(n) && !IsVpn(n))
                  ?? withGateway.FirstOrDefault(n => !IsVpn(n))
                  ?? withGateway.FirstOrDefault();

        if (active is null)
        {
            var physical = up.Where(n => !IsVirtual(n) && !IsVpn(n)).ToList();
            ctx.InternetReachable = false;

            if (physical.Count == 0)
            {
                r.Flags.Add("no_interface");
                r.Status = CheckStatus.Error;
                r.Summary = "Nenhum adaptador de rede conectado (cabo solto, Wi-Fi desconectado ou modo avião).";
                return Task.CompletedTask;
            }

            active = physical[0];
            r.Flags.Add("no_gateway");
            r.Status = CheckStatus.Error;
        }

        ctx.ActiveInterface = active;
        ctx.IsWireless = active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        if (ctx.IsWireless) r.Flags.Add("wireless");

        var props = active.GetIPProperties();
        ctx.Gateway = GetIPv4Gateway(active);

        var ipv4 = props.UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address)
            .ToList();

        ctx.DnsServers.AddRange(props.DnsAddresses.Where(a =>
            a.AddressFamily == AddressFamily.InterNetwork ||
            (a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6SiteLocal && !a.IsIPv6LinkLocal)));

        var type = ctx.IsWireless ? "Wi-Fi"
            : active.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Cabo (Ethernet)"
            : active.NetworkInterfaceType.ToString();

        r.SetText("name", "Adaptador", active.Name);
        r.SetText("description", "Modelo", active.Description);
        r.SetText("type", "Tipo de conexão", type);

        double? linkMbps = active.Speed > 0 ? active.Speed / 1_000_000.0 : null;
        r.Set("link_mbps", ctx.IsWireless ? "Taxa do link Wi-Fi" : "Velocidade do link", linkMbps, "Mbps");

        r.SetText("ipv4", "Endereço IP", ipv4.Count > 0 ? string.Join(", ", ipv4) : "nenhum");
        r.SetText("gateway", "Gateway (roteador)", ctx.Gateway?.ToString() ?? "nenhum");
        r.SetText("dns", "Servidores DNS",
            ctx.DnsServers.Count > 0 ? string.Join(", ", ctx.DnsServers) : "nenhum");

        if (ipv4.Any(NetUtil.IsApipa))
        {
            r.Flags.Add("apipa");
            r.Status = CheckStatus.Error;
            r.Summary = "O computador não recebeu endereço IP do roteador (DHCP não respondeu).";
            ctx.InternetReachable = false;
        }
        else if (r.Has("no_gateway"))
        {
            r.Summary = $"\"{active.Name}\" está ativo, mas sem gateway (roteador) configurado.";
        }
        else
        {
            r.Summary = linkMbps is double l
                ? $"Conectado via {type} — link de {l:0} Mbps."
                : $"Conectado via {type}.";
        }

        if (ctx.DnsServers.Count == 0)
        {
            r.Flags.Add("no_dns");
            r.Escalate(CheckStatus.Warning);
        }

        ReadErrorStats(active, r);
        return Task.CompletedTask;
    }

    private static void ReadErrorStats(NetworkInterface nic, CheckResult r)
    {
        try
        {
            var st = nic.GetIPv4Statistics();
            var packets = st.UnicastPacketsReceived + st.NonUnicastPacketsReceived + st.UnicastPacketsSent;
            var errors = st.IncomingPacketsWithErrors + st.OutgoingPacketsWithErrors;
            if (packets < 1000) return; // pouca amostra

            var rate = errors * 100.0 / packets;
            r.Set("error_rate", "Pacotes com erro", rate, "%");
            if (rate > 1) r.Escalate(CheckStatus.Warning);
        }
        catch (NetworkInformationException)
        {
            // alguns drivers não expõem estatísticas
        }
    }

    private static void DetectVpn(List<NetworkInterface> up, CheckResult r)
    {
        var vpns = up.Where(IsVpn).ToList();
        if (vpns.Count == 0) return;
        r.Flags.Add("vpn");
        r.SetText("vpn", "VPN ativa", string.Join(", ", vpns.Select(v => v.Name)));
    }

    private static void DetectProxy(CheckResult r)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key is null) return;

            var enabled = key.GetValue("ProxyEnable") is int i && i == 1;
            var server = key.GetValue("ProxyServer") as string;
            var pac = key.GetValue("AutoConfigURL") as string;

            if (enabled && !string.IsNullOrWhiteSpace(server))
            {
                r.Flags.Add("proxy");
                r.SetText("proxy", "Proxy", server);
            }
            else if (!string.IsNullOrWhiteSpace(pac))
            {
                r.Flags.Add("proxy");
                r.SetText("proxy", "Proxy (script automático)", pac);
            }
        }
        catch
        {
            // sem acesso ao registro: ignora
        }
    }

    private static IPAddress? GetIPv4Gateway(NetworkInterface n)
    {
        try
        {
            return n.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static string Text(NetworkInterface n) => (n.Name + " " + n.Description).ToLowerInvariant();

    private static bool IsVirtual(NetworkInterface n) => VirtualKeywords.Any(Text(n).Contains);

    // Túneis IPv6 do próprio Windows (Teredo, ISATAP…) aparecem como "Tunnel", mas não são VPN.
    private static readonly string[] SystemTunnels = ["teredo", "isatap", "6to4", "ip-https"];

    private static bool IsVpn(NetworkInterface n)
    {
        var text = Text(n);
        if (SystemTunnels.Any(text.Contains)) return false;
        return n.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel
            || VpnKeywords.Any(text.Contains);
    }
}
