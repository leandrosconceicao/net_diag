using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetDiag.Core.Net;

/// <summary>Medições básicas de latência: ping (ICMP) e conexão TCP.</summary>
public static class Probe
{
    /// <summary>Um ping. Retorna o tempo em ms ou null se não houve resposta.</summary>
    public static async Task<double?> PingOnceAsync(IPAddress target, int timeoutMs, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(timeoutMs), null, null, ct)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }

    /// <summary>Tempo para abrir uma conexão TCP (funciona mesmo quando a rede bloqueia ping).</summary>
    public static async Task<double?> TcpConnectOnceAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct)
    {
        using var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var sw = Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, port), cts.Token).ConfigureAwait(false);
            return sw.Elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timeout
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>Executa várias amostras em sequência, respeitando um intervalo entre elas.</summary>
    public static async Task<LatencyStats> SeriesAsync(
        Func<CancellationToken, Task<double?>> sample, int count, int intervalMs, CancellationToken ct)
    {
        var samples = new List<double?>(count);
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var start = Stopwatch.GetTimestamp();
            samples.Add(await sample(ct).ConfigureAwait(false));

            var wait = intervalMs - (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (wait > 0 && i < count - 1)
                await Task.Delay(wait, ct).ConfigureAwait(false);
        }
        return LatencyStats.From(samples);
    }
}

public sealed class LatencyStats
{
    public int Sent { get; private init; }
    public int Received { get; private init; }
    public double? Min { get; private init; }
    public double? Avg { get; private init; }
    public double? Max { get; private init; }
    public double? Median { get; private init; }

    /// <summary>Variação média entre amostras consecutivas (estilo RFC 3550).</summary>
    public double? Jitter { get; private init; }

    public double LossPercent => Sent == 0 ? 100 : 100.0 * (Sent - Received) / Sent;
    public bool AllLost => Received == 0;

    public static LatencyStats From(IReadOnlyList<double?> samples)
    {
        var ok = samples.Where(s => s.HasValue).Select(s => s!.Value).ToList();

        double? jitter = null;
        if (ok.Count > 1)
        {
            double sum = 0;
            for (var i = 1; i < ok.Count; i++) sum += Math.Abs(ok[i] - ok[i - 1]);
            jitter = sum / (ok.Count - 1);
        }

        return new LatencyStats
        {
            Sent = samples.Count,
            Received = ok.Count,
            Min = ok.Count > 0 ? ok.Min() : null,
            Avg = ok.Count > 0 ? ok.Average() : null,
            Max = ok.Count > 0 ? ok.Max() : null,
            Median = Stats.Median(ok),
            Jitter = jitter
        };
    }

    public override string ToString() => AllLost
        ? $"sem resposta ({Sent} tentativas)"
        : $"média {Avg:0} ms, mín {Min:0} / máx {Max:0} ms, jitter {Jitter ?? 0:0} ms, perda {LossPercent:0}%";
}

public static class Stats
{
    public static double? Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0) return null;
        var mid = list.Count / 2;
        return list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) / 2;
    }
}
