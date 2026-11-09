using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>Camada 3: a ligação entre o computador e o roteador está estável?</summary>
public sealed class GatewayCheck : IDiagnosticCheck
{
    public string Id => "gateway";
    public string Name => "Roteador";
    public string Description => "Testa a estabilidade da ligação entre o computador e o roteador.";

    /// <summary>Latência média aceitável até o roteador.</summary>
    public const double WiredLimitMs = 10;
    public const double WirelessLimitMs = 30;

    public async Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct)
    {
        if (ctx.Gateway is null)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "Sem roteador configurado para testar.";
            return;
        }

        var gw = ctx.Gateway;
        var stats = await Probe.SeriesAsync(t => Probe.PingOnceAsync(gw, 1000, t), 30, 100, ct)
            .ConfigureAwait(false);

        r.Set("gw_avg", "Latência média", stats.Avg, "ms");
        r.Set("gw_max", "Latência máxima", stats.Max, "ms");
        r.Set("gw_jitter", "Variação (jitter)", stats.Jitter, "ms");
        r.Set("gw_loss", "Perda de pacotes", stats.LossPercent, "%");
        r.Details.Add($"{gw}: {stats}");

        if (stats.AllLost)
        {
            r.Flags.Add("gw_unreachable");
            r.Status = CheckStatus.Error;
            r.Summary = $"O roteador ({gw}) não respondeu.";
            return;
        }

        var limit = ctx.IsWireless ? WirelessLimitMs : WiredLimitMs;
        if (stats.LossPercent > 2 || stats.Avg > limit)
        {
            r.Flags.Add("gw_unstable");
            r.Status = CheckStatus.Warning;
        }

        r.Summary = $"Média {stats.Avg:0} ms, perda {stats.LossPercent:0}%.";
    }
}
