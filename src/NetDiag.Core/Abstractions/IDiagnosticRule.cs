using NetDiag.Core.Model;

namespace NetDiag.Core.Abstractions;

/// <summary>Transforma medições em conclusões (causas prováveis + recomendações).</summary>
public interface IDiagnosticRule
{
    IEnumerable<Finding> Evaluate(RuleContext context);
}

public sealed class RuleContext
{
    public RuleContext(DiagnosticReport report, DiagnosticOptions options)
    {
        Report = report;
        Options = options;
    }

    public DiagnosticReport Report { get; }
    public DiagnosticOptions Options { get; }

    public CheckResult? Adapter => Report.Get("adapter");
    public CheckResult? Wifi => Report.Get("wifi");
    public CheckResult? Capability => Report.Get("capability");
    public CheckResult? Gateway => Report.Get("gateway");
    public CheckResult? Internet => Report.Get("internet");
    public CheckResult? Dns => Report.Get("dns");
    public CheckResult? Trace => Report.Get("traceroute");
    public CheckResult? Speed => Report.Get("speed");

    public bool IsWireless => Adapter?.Has("wireless") == true;

    /// <summary>True quando o teste rodou (não foi pulado nem falhou).</summary>
    public static bool Ran(CheckResult? r) => r is not null && r.Status != CheckStatus.Skipped && !r.Has("check_failed");
}
