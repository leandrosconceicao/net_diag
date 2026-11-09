using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetDiag.Core.Net;

/// <summary>
/// Consulta DNS mínima via UDP, direto a um servidor específico.
/// Permite comparar o DNS configurado com DNS públicos sem depender do cache do Windows.
/// </summary>
public static class DnsProbe
{
    /// <summary>Tempo de resposta em ms, ou null em caso de timeout/erro do servidor.</summary>
    public static async Task<double?> QueryAsync(IPAddress server, string name, int timeoutMs, CancellationToken ct)
    {
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var query = BuildQuery(name, id);

        using var udp = new UdpClient(server.AddressFamily);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(query.AsMemory(), new IPEndPoint(server, 53), cts.Token).ConfigureAwait(false);
            while (true)
            {
                var response = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                var buf = response.Buffer;
                if (buf.Length < 4 || ((buf[0] << 8) | buf[1]) != id) continue;

                sw.Stop();
                var rcode = buf[3] & 0x0F;
                // 0 = NOERROR, 3 = NXDOMAIN: ambos significam que o servidor respondeu.
                return rcode is 0 or 3 ? sw.Elapsed.TotalMilliseconds : null;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static byte[] BuildQuery(string name, ushort id)
    {
        var bytes = new List<byte>(64)
        {
            (byte)(id >> 8), (byte)id,
            0x01, 0x00,             // flags: recursion desired
            0x00, 0x01,             // QDCOUNT = 1
            0x00, 0x00, 0x00, 0x00, // ANCOUNT, NSCOUNT
            0x00, 0x00              // ARCOUNT
        };

        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var l = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)l.Length);
            bytes.AddRange(l);
        }
        bytes.Add(0x00);
        bytes.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // QTYPE=A, QCLASS=IN
        return bytes.ToArray();
    }
}
