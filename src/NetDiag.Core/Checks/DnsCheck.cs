using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>
/// Camada 5: o DNS (que traduz nomes de sites em endereços) funciona e é rápido?
/// Compara o DNS configurado com DNS públicos.
/// </summary>
public sealed class DnsCheck : IDiagnosticCheck
{
    public string Id => "dns";
    public string Name => "DNS";
    public string Description => "Verifica se os nomes de sites são resolvidos corretamente e com rapidez.";

    public const double SlowMs = 150;

    private sealed record ServerResult(IPAddress Server, bool IsSystem, double? MedianMs, int Failures, int Total);

    public async Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct)
    {
        if (!ctx.InternetReachable)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "Pulado: sem acesso à internet.";
            return;
        }

        var system = ctx.DnsServers.Distinct().ToList();
        var publics = ctx.Options.PublicDnsServers.Where(p => !system.Contains(p)).ToList();
        var domains = ctx.Options.DnsTestDomains;

        async Task<ServerResult> MeasureAsync(IPAddress server, bool isSystem)
        {
            var times = new List<double>();
            var failures = 0;
            foreach (var domain in domains)
            {
                var t = await DnsProbe.QueryAsync(server, domain, 2000, ct).ConfigureAwait(false);
                if (t is double v) times.Add(v); else failures++;
            }
            return new ServerResult(server, isSystem, Stats.Median(times), failures, domains.Length);
        }

        var measurements = system.Select(s => MeasureAsync(s, true))
            .Concat(publics.Select(p => MeasureAsync(p, false)));
        var osTask = MeasureOsResolverAsync(ct);

        var results = await Task.WhenAll(measurements).ConfigureAwait(false);
        var os = await osTask.ConfigureAwait(false);
        // Reserva: se o teste com nome inexistente falhar, tenta um domínio real.
        if (os is null && domains.Length > 0)
            os = await MeasureOsResolverAsync(ct, domains[0]).ConfigureAwait(false);

        foreach (var s in results)
        {
            var label = s.IsSystem ? "configurado" : "público";
            r.Details.Add(s.MedianMs is double m
                ? $"{s.Server} ({label}): mediana {m:0} ms, falhas {s.Failures}/{s.Total}"
                : $"{s.Server} ({label}): não respondeu");
        }

        var sysBest = results.Where(x => x.IsSystem && x.MedianMs.HasValue).MinBy(x => x.MedianMs);
        var pubBest = results.Where(x => !x.IsSystem && x.MedianMs.HasValue).MinBy(x => x.MedianMs);

        r.Set("dns_sys_ms", "DNS configurado (mediana)", sysBest?.MedianMs, "ms");
        r.Set("dns_pub_ms", "Melhor DNS público (mediana)", pubBest?.MedianMs, "ms");
        if (pubBest is not null) r.SetText("dns_pub_server", "Melhor DNS público", pubBest.Server.ToString());
        r.Set("dns_os_ms", "Resolução pelo Windows", os, "ms");

        if (system.Count == 0) r.Flags.Add("no_dns");

        if (os is null)
        {
            r.Flags.Add("dns_fail");
            r.Status = CheckStatus.Error;
            r.Summary = "O Windows não conseguiu resolver nomes de sites.";
            return;
        }

        if (system.Count > 0 && sysBest is null && pubBest is not null)
        {
            r.Flags.Add("dns_system_fail");
            r.Status = CheckStatus.Warning;
            r.Summary = "O DNS configurado não respondeu às consultas diretas.";
            return;
        }

        if (sysBest?.MedianMs is double sys && pubBest?.MedianMs is double pub
            && sys > SlowMs && pub < sys / 2)
        {
            r.Flags.Add("dns_slow");
            r.Status = CheckStatus.Warning;
        }

        r.Summary = sysBest?.MedianMs is double sm
            ? $"DNS configurado responde em {sm:0} ms" + (pubBest?.MedianMs is double pm ? $" (público: {pm:0} ms)." : ".")
            : $"Resolução de nomes funcionando ({os:0} ms).";
    }

    /// <summary>Resolução pelo caminho normal do Windows (o mesmo que o navegador usa).</summary>
    private static async Task<double?> MeasureOsResolverAsync(CancellationToken ct, string? host = null)
    {
        // Subdomínio aleatório evita o cache; NXDOMAIN ainda prova que o DNS respondeu.
        host ??= $"netdiag-{Guid.NewGuid():N}.example.com";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(5000);

        var sw = Stopwatch.StartNew();
        try
        {
            await Dns.GetHostAddressesAsync(host, cts.Token).ConfigureAwait(false);
            return sw.Elapsed.TotalMilliseconds;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return sw.Elapsed.TotalMilliseconds; // resposta "não existe" = DNS funcionando
        }
        catch (SocketException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
