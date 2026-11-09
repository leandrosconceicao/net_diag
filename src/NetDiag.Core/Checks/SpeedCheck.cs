using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>
/// Camada 7: velocidade de download/upload e "bufferbloat" — o quanto a latência
/// piora enquanto a conexão está sendo usada (causa travamentos em chamadas e jogos).
/// </summary>
public sealed class SpeedCheck : IDiagnosticCheck
{
    public string Id => "speed";
    public string Name => "Velocidade";
    public string Description => "Mede download, upload e se a conexão trava quando está em uso.";

    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(1.5);

    private sealed class Counter
    {
        public long Bytes;
        public int Errors;
    }

    public async Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct)
    {
        if (!ctx.Options.RunSpeedTest)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "Teste de velocidade desativado.";
            return;
        }
        if (!ctx.InternetReachable)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "Pulado: sem acesso à internet.";
            return;
        }

        var o = ctx.Options;
        var latencyTarget = o.InternetTargets[0];
        Func<CancellationToken, Task<double?>> latencySample = ctx.IcmpBlocked
            ? t => Probe.TcpConnectOnceAsync(latencyTarget, 443, 1500, t)
            : t => Probe.PingOnceAsync(latencyTarget, 1500, t);

        using var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.None
        };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NetDiag/0.1");

        ctx.ReportStatus?.Invoke("Medindo latência em repouso…");
        var idle = await Probe.SeriesAsync(latencySample, 10, 100, ct).ConfigureAwait(false);
        r.Set("idle_ms", "Latência em repouso", idle.Median, "ms");

        ctx.ReportStatus?.Invoke("Medindo download…");
        var down = await MeasureAsync(
            async (c, t) =>
            {
                using var resp = await http.GetAsync(o.SpeedDownloadUrl, HttpCompletionOption.ResponseHeadersRead, t)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(t).ConfigureAwait(false);
                var buffer = new byte[64 * 1024];
                int n;
                while ((n = await stream.ReadAsync(buffer, t).ConfigureAwait(false)) > 0)
                    Interlocked.Add(ref c.Bytes, n);
            },
            o.SpeedTestStreams, o.DownloadTestDuration, latencySample, ct).ConfigureAwait(false);

        ctx.ReportStatus?.Invoke("Medindo upload…");
        var up = await MeasureAsync(
            async (c, t) =>
            {
                using var content = new CountingUploadContent(8_000_000, c);
                using var resp = await http.PostAsync(o.SpeedUploadUrl, content, t).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
            },
            o.SpeedTestStreams, o.UploadTestDuration, latencySample, ct).ConfigureAwait(false);

        r.Set("down_mbps", "Download", down.Mbps, "Mbps");
        r.Set("up_mbps", "Upload", up.Mbps, "Mbps");
        r.Set("loaded_down_ms", "Latência durante download", down.LoadedLatencyMs, "ms");
        r.Set("loaded_up_ms", "Latência durante upload", up.LoadedLatencyMs, "ms");

        if (idle.Median is double idleMs)
        {
            var worst = new[] { down.LoadedLatencyMs, up.LoadedLatencyMs }.Max();
            if (worst is double w) r.Set("bloat_ms", "Aumento de latência sob uso", Math.Max(0, w - idleMs), "ms");
        }

        if (down.Mbps is null && up.Mbps is null)
        {
            r.Flags.Add("speed_failed");
            r.Status = CheckStatus.Warning;
            r.Summary = "Não foi possível medir a velocidade (servidor de teste inacessível).";
            return;
        }

        r.Summary = $"Download {NetUtil.FormatMbps(down.Mbps)}, upload {NetUtil.FormatMbps(up.Mbps)}.";
    }

    private sealed record Measurement(double? Mbps, double? LoadedLatencyMs);

    /// <summary>
    /// Roda vários fluxos em paralelo pelo tempo definido, amostrando latência ao mesmo tempo.
    /// O primeiro 1,5 s é descartado (aceleração inicial do TCP).
    /// </summary>
    private static async Task<Measurement> MeasureAsync(
        Func<Counter, CancellationToken, Task> worker,
        int streams,
        TimeSpan duration,
        Func<CancellationToken, Task<double?>> latencySample,
        CancellationToken ct)
    {
        var counter = new Counter();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = Enumerable.Range(0, streams).Select(_ => LoopAsync(worker, counter, cts.Token)).ToList();

        var latencies = new List<double>();
        var sw = Stopwatch.StartNew();
        long bytesAtWarm = 0;
        double secondsAtWarm = 0;
        var warmed = false;

        try
        {
            while (sw.Elapsed < duration)
            {
                if (await latencySample(ct).ConfigureAwait(false) is double l) latencies.Add(l);

                if (!warmed && sw.Elapsed >= WarmUp)
                {
                    bytesAtWarm = Interlocked.Read(ref counter.Bytes);
                    secondsAtWarm = sw.Elapsed.TotalSeconds;
                    warmed = true;
                }
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            cts.Cancel();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }

        var bytes = Interlocked.Read(ref counter.Bytes) - bytesAtWarm;
        var seconds = sw.Elapsed.TotalSeconds - secondsAtWarm;
        double? mbps = bytes > 0 && seconds > 0 ? bytes * 8 / seconds / 1_000_000 : null;

        return new Measurement(mbps, Stats.Median(latencies));
    }

    /// <summary>Repete o trabalho até ser cancelado; erros pontuais não interrompem o teste.</summary>
    private static async Task LoopAsync(Func<Counter, CancellationToken, Task> worker, Counter counter, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await worker(counter, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                Interlocked.Increment(ref counter.Errors);
                try { await Task.Delay(300, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Conteúdo de upload que contabiliza os bytes à medida que são enviados.</summary>
    private sealed class CountingUploadContent : HttpContent
    {
        private static readonly byte[] Chunk = CreateChunk();
        private readonly long _length;
        private readonly Counter _counter;

        public CountingUploadContent(long length, Counter counter)
        {
            _length = length;
            _counter = counter;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        private static byte[] CreateChunk()
        {
            var b = new byte[64 * 1024];
            Random.Shared.NextBytes(b); // dados aleatórios: não comprimem
            return b;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            long sent = 0;
            while (sent < _length)
            {
                var n = (int)Math.Min(Chunk.Length, _length - sent);
                await stream.WriteAsync(Chunk.AsMemory(0, n), ct).ConfigureAwait(false);
                sent += n;
                Interlocked.Add(ref _counter.Bytes, n);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
