using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>
/// Camada 6: rota até a internet. Mostra em qual "salto" (roteador no caminho) a perda
/// ou a lentidão começa — separando problema da casa do problema do provedor.
/// </summary>
public sealed class TracerouteCheck : IDiagnosticCheck
{
    public string Id => "traceroute";
    public string Name => "Rota até a internet";
    public string Description => "Descobre em que ponto do caminho surgem perdas ou atrasos.";

    private const int MaxHops = 20;
    private const int ProbesPerHop = 4;
    private const int TimeoutMs = 1500;
    private const double LossThreshold = 25;
    private const double JumpThresholdMs = 50;

    private sealed record Hop(int Ttl, IPAddress? Address, int Sent, int Received, double? AvgMs, bool IsDestination)
    {
        public double LossPercent => 100.0 * (Sent - Received) / Sent;
        public bool Silent => Received == 0;
    }

    public async Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct)
    {
        if (!ctx.InternetReachable || ctx.IcmpBlocked)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = ctx.IcmpBlocked
                ? "Pulado: a rede bloqueia ping, então não é possível traçar a rota."
                : "Pulado: sem acesso à internet.";
            return;
        }

        var target = ctx.Options.TraceTarget;
        // Todos os saltos são sondados em paralelo: leva poucos segundos em vez de minutos.
        var hops = await Task.WhenAll(
                Enumerable.Range(1, MaxHops).Select(ttl => ProbeHopAsync(target, ttl, ct)))
            .ConfigureAwait(false);

        var destIndex = Array.FindIndex(hops, h => h.IsDestination);
        var path = destIndex >= 0 ? hops.Take(destIndex + 1).ToList() : hops.ToList();
        while (destIndex < 0 && path.Count > 0 && path[^1].Silent) path.RemoveAt(path.Count - 1);

        foreach (var h in path)
        {
            var addr = h.Address?.ToString() ?? "*";
            var where = h.Ttl == 1 ? " (seu roteador)" : h.Address is not null && NetUtil.IsPrivate(h.Address) ? " (rede privada)" : "";
            r.Details.Add(h.Silent
                ? $"{h.Ttl,2}  *  sem resposta (comum: alguns roteadores não respondem)"
                : $"{h.Ttl,2}  {addr}{where}  {h.AvgMs:0} ms  perda {h.LossPercent:0}%");
        }

        r.Set("trace_hops", "Saltos até o destino", destIndex >= 0 ? destIndex + 1 : null);

        if (destIndex < 0)
        {
            r.Flags.Add("trace_stops");
            r.Status = CheckStatus.Warning;
            r.Summary = $"A rota para {target} não foi concluída (último salto que respondeu: {path.Count}).";
            return;
        }

        AnalyzeLoss(path, r);
        AnalyzeLatencyJump(path, r);

        r.Summary = r.Has("trace_loss")
            ? $"Perda começa no salto {r.Get("trace_loss_hop"):0} ({r.GetText("trace_loss_ip")})."
            : r.Has("trace_jump")
                ? $"Atraso de +{r.Get("trace_jump_ms"):0} ms a partir do salto {r.Get("trace_jump_hop"):0}."
                : $"{path.Count} saltos até {target}, sem perdas relevantes.";
    }

    /// <summary>
    /// A perda só é real se continuar até o destino. Um salto intermediário que descarta ping
    /// mas deixa os seguintes passarem está apenas limitando respostas — não é problema.
    /// </summary>
    private static void AnalyzeLoss(List<Hop> path, CheckResult r)
    {
        var dest = path[^1];
        if (dest.LossPercent < LossThreshold) return;

        var responding = path.Where(h => !h.Silent).ToList();
        for (var i = 0; i < responding.Count; i++)
        {
            if (responding.Skip(i).All(h => h.LossPercent >= LossThreshold))
            {
                var origin = responding[i];
                r.Flags.Add("trace_loss");
                r.Set("trace_loss_hop", "Perda começa no salto", origin.Ttl);
                r.SetText("trace_loss_ip", "Endereço do salto com perda", origin.Address?.ToString() ?? "*");
                if (origin.Address is not null && NetUtil.IsPrivate(origin.Address)) r.Flags.Add("trace_loss_private");
                r.Status = CheckStatus.Warning;
                return;
            }
        }
    }

    /// <summary>Maior aumento de latência que persiste até o destino.</summary>
    private static void AnalyzeLatencyJump(List<Hop> path, CheckResult r)
    {
        var responding = path.Where(h => h.AvgMs.HasValue).ToList();
        if (responding.Count < 2) return;

        var destAvg = responding[^1].AvgMs!.Value;
        Hop? jumpHop = null;
        double jump = 0;
        double previousMax = responding[0].AvgMs!.Value;

        foreach (var h in responding.Skip(1))
        {
            var delta = h.AvgMs!.Value - previousMax;
            if (delta > jump && destAvg >= h.AvgMs.Value * 0.8)
            {
                jump = delta;
                jumpHop = h;
            }
            previousMax = Math.Max(previousMax, h.AvgMs.Value);
        }

        if (jumpHop is null || jump < JumpThresholdMs) return;

        r.Flags.Add("trace_jump");
        r.Set("trace_jump_hop", "Maior atraso surge no salto", jumpHop.Ttl);
        r.Set("trace_jump_ms", "Aumento de latência", jump, "ms");
        r.SetText("trace_jump_ip", "Endereço do salto com atraso", jumpHop.Address?.ToString() ?? "*");
        r.Escalate(CheckStatus.Warning);
    }

    private static async Task<Hop> ProbeHopAsync(IPAddress target, int ttl, CancellationToken ct)
    {
        IPAddress? address = null;
        var times = new List<double>();
        var reached = false;
        var buffer = new byte[32];

        for (var i = 0; i < ProbesPerHop; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var ping = new Ping();
            var sw = Stopwatch.StartNew();
            try
            {
                var reply = await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(TimeoutMs), buffer,
                    new PingOptions(ttl, dontFragment: true), ct).ConfigureAwait(false);

                if (reply.Status is IPStatus.Success or IPStatus.TtlExpired or IPStatus.TimeExceeded)
                {
                    // Em respostas de "TTL expirado" o RoundtripTime vem zerado; por isso o cronômetro próprio.
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    address ??= reply.Address;
                    if (reply.Status == IPStatus.Success) reached = true;
                }
            }
            catch (PingException)
            {
                // sem resposta neste salto
            }
        }

        return new Hop(ttl, address, ProbesPerHop, times.Count, times.Count > 0 ? times.Average() : null, reached);
    }
}
