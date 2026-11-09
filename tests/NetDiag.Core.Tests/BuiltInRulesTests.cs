using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Rules;

namespace NetDiag.Core.Tests;

public class BuiltInRulesTests
{
    private static CheckResult Result(string id, CheckStatus status = CheckStatus.Ok, params string[] flags)
    {
        var r = new CheckResult(id, id) { Status = status };
        foreach (var f in flags) r.Flags.Add(f);
        return r;
    }

    private static RuleContext Build(DiagnosticOptions options, CheckResult[] results)
    {
        var report = new DiagnosticReport();
        report.Results.AddRange(results);
        return new RuleContext(report, options);
    }

    private static RuleContext Context(params CheckResult[] results) =>
        Build(new DiagnosticOptions(), results);

    private static RuleContext WithPlan(double contractedMbps, params CheckResult[] results) =>
        Build(new DiagnosticOptions { ContractedDownloadMbps = contractedMbps }, results);

    private static List<Finding> Evaluate(RuleContext ctx) => new BuiltInRules().Evaluate(ctx).ToList();

    private static Finding Find(List<Finding> findings, string id) => Assert.Single(findings, f => f.Id == id);

    [Fact]
    public void Without_results_there_are_no_findings() =>
        Assert.Empty(Evaluate(Context()));

    [Fact]
    public void No_interface_is_critical_and_stops_the_other_rules()
    {
        var findings = Evaluate(Context(
            Result("adapter", CheckStatus.Error, "no_interface"),
            Result("internet", CheckStatus.Error, "offline")));

        var only = Assert.Single(findings);
        Assert.Equal("conn.no_interface", only.Id);
        Assert.Equal(Severity.Critical, only.Severity);
    }

    [Fact]
    public void Apipa_points_to_dhcp_and_offers_ip_renewal()
    {
        var f = Find(Evaluate(Context(Result("adapter", CheckStatus.Error, "apipa"))), "conn.apipa");

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("renew_ip", f.FixId);
    }

    [Fact]
    public void Missing_gateway_is_critical()
    {
        var f = Find(Evaluate(Context(Result("adapter", CheckStatus.Error, "no_gateway"))), "conn.no_gateway");

        Assert.Equal(Severity.Critical, f.Severity);
    }

    [Fact]
    public void Unreachable_router_with_no_internet_is_a_local_problem()
    {
        var findings = Evaluate(Context(
            Result("gateway", CheckStatus.Error, "gw_unreachable"),
            Result("internet", CheckStatus.Error, "offline")));

        var f = Find(findings, "lan.gw_down");
        Assert.Equal(Severity.Critical, f.Severity);
    }

    [Fact]
    public void Router_ignoring_ping_is_only_informational_when_internet_works()
    {
        var findings = Evaluate(Context(
            Result("gateway", CheckStatus.Error, "gw_unreachable"),
            Result("internet", CheckStatus.Ok)));

        Assert.Equal(Severity.Info, Find(findings, "lan.gw_no_ping").Severity);
    }

    [Fact]
    public void Healthy_router_and_no_internet_points_to_the_provider()
    {
        var findings = Evaluate(Context(
            Result("gateway", CheckStatus.Ok),
            Result("internet", CheckStatus.Error, "offline")));

        var f = Find(findings, "isp.offline");
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal(Confidence.Alta, f.Confidence);
    }

    [Fact]
    public void Weak_wifi_signal_is_a_warning()
    {
        var wifi = Result("wifi");
        wifi.Set("signal", "Intensidade do sinal", 30, "%");

        var f = Find(Evaluate(Context(wifi)), "wifi.weak");

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Captive_portal_is_a_warning()
    {
        var f = Find(Evaluate(Context(Result("internet", CheckStatus.Warning, "captive_portal"))),
            "net.captive_portal");

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Internet_packet_loss_is_reported_as_isp_problem()
    {
        var internet = Result("internet", CheckStatus.Warning, "inet_unstable");
        internet.Set("inet_loss", "Perda de pacotes", 5, "%");
        internet.Set("inet_avg", "Latência média", 20, "ms");
        internet.Set("inet_jitter", "Variação (jitter)", 1, "ms");

        var f = Find(Evaluate(Context(internet)), "isp.loss");

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(Confidence.Media, f.Confidence);
    }

    [Fact]
    public void Dns_resolution_failure_is_critical_with_public_dns_fix()
    {
        var f = Find(Evaluate(Context(Result("dns", CheckStatus.Error, "dns_fail"))), "dns.fail");

        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("set_public_dns", f.FixId);
    }

    [Fact]
    public void Configured_dns_not_answering_is_a_warning()
    {
        var f = Find(Evaluate(Context(Result("dns", CheckStatus.Warning, "dns_system_fail"))), "dns.system_fail");

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Active_vpn_is_mentioned_as_information()
    {
        var adapter = Result("adapter");
        adapter.Flags.Add("vpn");
        adapter.SetText("vpn", "VPN ativa", "Tailscale");

        var f = Find(Evaluate(Context(adapter)), "pc.vpn");

        Assert.Equal(Severity.Info, f.Severity);
    }

    [Fact]
    public void Cable_negotiated_below_the_card_limit_is_the_ceiling_when_it_hits_the_plan()
    {
        var adapter = Result("adapter");
        adapter.Set("link_mbps", "Velocidade do link", 100, "Mbps");
        var capability = Result("capability");
        capability.Set("card_max_mbps", "Velocidade máxima suportada pela placa", 1000, "Mbps");

        // 100 Mbps × 0,94 = 94 Mbps; plano de 500 Mbps não cabe.
        var f = Find(Evaluate(WithPlan(500, adapter, capability)), "pc.ceiling");

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Ceiling_is_not_reported_when_the_plan_fits()
    {
        var adapter = Result("adapter");
        adapter.Set("link_mbps", "Velocidade do link", 1000, "Mbps");
        var capability = Result("capability");
        capability.Set("card_max_mbps", "Velocidade máxima suportada pela placa", 1000, "Mbps");

        // 1000 × 0,94 = 940 Mbps; plano de 500 Mbps cabe com folga.
        var findings = Evaluate(WithPlan(500, adapter, capability));

        Assert.DoesNotContain(findings, f => f.Id == "pc.ceiling");
    }

    [Fact]
    public void Manually_forced_card_speed_is_flagged_with_auto_negotiation_fix()
    {
        var adapter = Result("adapter");
        adapter.Set("link_mbps", "Velocidade do link", 100, "Mbps");
        var capability = Result("capability", CheckStatus.Ok, "speed_forced");
        capability.Set("card_max_mbps", "Velocidade máxima suportada pela placa", 1000, "Mbps");
        capability.SetText("speed_setting", "Configuração de velocidade", "100 Mbps Full Duplex");

        // Sem plano informado, a placa forçada ainda assim é apontada.
        var f = Find(Evaluate(Context(adapter, capability)), "pc.ceiling");

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("reset_speed_duplex", f.FixId);
    }

    [Fact]
    public void Half_duplex_is_a_warning()
    {
        var f = Find(Evaluate(Context(Result("capability", CheckStatus.Warning, "half_duplex"))), "pc.half_duplex");

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Bufferbloat_of_100_ms_or_more_is_a_warning()
    {
        var speed = Result("speed");
        speed.Set("idle_ms", "Latência em repouso", 20, "ms");
        speed.Set("bloat_ms", "Aumento de latência sob uso", 150, "ms");

        var f = Find(Evaluate(Context(speed)), "speed.bufferbloat");

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Loss_starting_at_first_hop_points_to_the_router()
    {
        var trace = Result("traceroute", CheckStatus.Warning, "trace_loss");
        trace.Set("trace_loss_hop", "Perda começa no salto", 1);
        trace.SetText("trace_loss_ip", "Endereço do salto com perda", "192.168.0.1");

        var f = Find(Evaluate(Context(trace)), "route.loss_router");

        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Loss_starting_beyond_the_home_points_to_the_provider()
    {
        var trace = Result("traceroute", CheckStatus.Warning, "trace_loss");
        trace.Set("trace_loss_hop", "Perda começa no salto", 5);
        trace.SetText("trace_loss_ip", "Endereço do salto com perda", "189.10.20.30");

        var f = Find(Evaluate(Context(trace)), "route.loss_isp");

        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(Confidence.Alta, f.Confidence);
    }

    [Fact]
    public void Healthy_measurements_produce_no_findings()
    {
        var internet = Result("internet");
        internet.Set("inet_avg", "Latência média", 20, "ms");
        internet.Set("inet_loss", "Perda de pacotes", 0, "%");
        internet.Set("inet_jitter", "Variação (jitter)", 2, "ms");

        var findings = Evaluate(Context(
            Result("adapter"),
            Result("gateway"),
            internet,
            Result("dns")));

        Assert.Empty(findings);
    }
}
