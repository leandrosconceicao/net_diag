using System.Net;
using System.Net.Sockets;

namespace NetDiag.Core.Net;

public static class NetUtil
{
    /// <summary>IP de autoconfiguração (169.254.x.x): o DHCP não respondeu.</summary>
    public static bool IsApipa(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    /// <summary>Faixas privadas e CGNAT (100.64.0.0/10, usada por provedores).</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    public static string FormatMbps(double? mbps) => mbps is double v ? $"{v:0.#} Mbps" : "—";

    /// <summary>Velocidade de link: 1000 → "1 Gbps", 2500 → "2,5 Gbps", 100 → "100 Mbps".</summary>
    public static string FormatLink(double mbps) =>
        mbps >= 1000 ? $"{mbps / 1000:0.#} Gbps" : $"{mbps:0} Mbps";

    /// <summary>Arredonda estimativas para não passar falsa precisão: 87 → 85, 143 → 140.</summary>
    public static double RoundEstimate(double mbps) => mbps switch
    {
        < 20 => Math.Round(mbps),
        < 100 => Math.Round(mbps / 5) * 5,
        _ => Math.Round(mbps / 10) * 10
    };
}
