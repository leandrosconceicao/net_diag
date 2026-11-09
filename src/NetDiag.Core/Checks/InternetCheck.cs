using System.Net;
using System.Net.Http;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>Camada 4: há acesso à internet? Com que latência, estabilidade e perda?</summary>
public sealed class InternetCheck : IDiagnosticCheck
{
    public string Id => "internet";
    public string Name => "Acesso à internet";
    public string Description => "Mede latência, oscilação e perda até servidores na internet.";

    public const double HighLatencyMs = 100;
    public const double HighJitterMs = 30;

    public async Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct)
    {
        if (!ctx.InternetReachable)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "Pulado: o computador não está conectado à rede.";
            return;
        }

        var targets = ctx.Options.InternetTargets;

        var icmpTasks = targets
            .Select(t => Probe.SeriesAsync(c => Probe.PingOnceAsync(t, 1500, c), 20, 150, ct))
            .ToArray();
        var tcpTasks = targets
            .Select(t => Probe.SeriesAsync(c => Probe.TcpConnectOnceAsync(t, 443, 2000, c), 5, 200, ct))
            .ToArray();
        var portalTask = DetectCaptivePortalAsync(ct);

        await Task.WhenAll(icmpTasks.Cast<Task>().Concat(tcpTasks).Append(portalTask)).ConfigureAwait(false);

        for (var i = 0; i < targets.Length; i++)
        {
            r.Details.Add($"ping {targets[i]}: {icmpTasks[i].Result}");
            r.Details.Add($"tcp {targets[i]}:443: {tcpTasks[i].Result}");
        }

        var icmpBest = Best(icmpTasks.Select(t => t.Result));
        var tcpBest = Best(tcpTasks.Select(t => t.Result));

        LatencyStats used;
        if (!icmpBest.AllLost)
        {
            used = icmpBest;
        }
        else if (!tcpBest.AllLost)
        {
            // Ping bloqueado pela rede (comum em empresas), mas a internet funciona.
            ctx.IcmpBlocked = true;
            r.Flags.Add("icmp_blocked");
            used = tcpBest;
        }
        else
        {
            ctx.InternetReachable = false;
            r.Flags.Add("offline");
            r.Status = CheckStatus.Error;
            r.Summary = "Sem acesso à internet: nenhum servidor externo respondeu.";
            return;
        }

        r.Set("inet_avg", "Latência média", used.Avg, "ms");
        r.Set("inet_max", "Latência máxima", used.Max, "ms");
        r.Set("inet_jitter", "Variação (jitter)", used.Jitter, "ms");
        r.Set("inet_loss", "Perda de pacotes", used.LossPercent, "%");
        r.SetText("inet_method", "Método", ctx.IcmpBlocked ? "conexão TCP (ping bloqueado)" : "ping (ICMP)");

        if (portalTask.Result == true)
        {
            r.Flags.Add("captive_portal");
            r.Escalate(CheckStatus.Warning);
        }

        if (used.LossPercent > 2 || used.Avg > HighLatencyMs || used.Jitter > HighJitterMs)
        {
            r.Flags.Add("inet_unstable");
            r.Escalate(CheckStatus.Warning);
        }

        r.Summary = $"Média {used.Avg:0} ms, jitter {used.Jitter ?? 0:0} ms, perda {used.LossPercent:0}%.";
    }

    private static LatencyStats Best(IEnumerable<LatencyStats> stats) =>
        stats.OrderBy(s => s.LossPercent).ThenBy(s => s.Avg ?? double.MaxValue).First();

    /// <summary>
    /// Mesmo teste que o Windows usa: se a resposta não for a esperada,
    /// a rede está interceptando (portal de login de hotel, aeroporto etc.).
    /// </summary>
    private static async Task<bool?> DetectCaptivePortalAsync(CancellationToken ct)
    {
        try
        {
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync("http://www.msftconnecttest.com/connecttest.txt", ct)
                .ConfigureAwait(false);

            if ((int)response.StatusCode is >= 300 and < 400 and not 304)
                return true; // redirecionou para uma página de login

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode && body.Trim() != "Microsoft Connect Test";
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null; // inconclusivo
        }
    }
}
