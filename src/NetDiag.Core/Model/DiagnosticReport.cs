using System.Runtime.InteropServices;
using System.Text;

namespace NetDiag.Core.Model;

public sealed class DiagnosticReport
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public string MachineName { get; init; } = Environment.MachineName;
    public string OsDescription { get; init; } = RuntimeInformation.OSDescription;

    public List<CheckResult> Results { get; } = new();
    public List<Finding> Findings { get; } = new();

    public CheckResult? Get(string checkId) => Results.FirstOrDefault(r => r.CheckId == checkId);

    public Severity Overall => Findings.Count == 0 ? Severity.Info : Findings.Max(f => f.Severity);

    public string OverallTitle => Overall switch
    {
        Severity.Critical => "Problema encontrado",
        Severity.Warning => "Há pontos de atenção",
        _ => "Conexão saudável"
    };

    /// <summary>Frase curta para o topo da tela.</summary>
    public string Headline
    {
        get
        {
            var main = Findings.FirstOrDefault(f => f.Severity >= Severity.Warning);
            return main?.Title ?? "Nenhum problema foi detectado nos testes.";
        }
    }

    public string ToPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Diagnóstico de conexão — {StartedAt:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Computador: {MachineName} ({OsDescription})");
        sb.AppendLine($"Resultado: {OverallTitle}");
        sb.AppendLine();

        foreach (var f in Findings)
        {
            sb.AppendLine($"[{f.SeverityLabel}] {f.Title} ({f.Area})");
            sb.AppendLine($"  {f.Explanation}");
            if (f.HasRecommendation) sb.AppendLine($"  O que fazer: {f.Recommendation}");
            sb.AppendLine();
        }

        sb.AppendLine("Medições:");
        foreach (var r in Results)
        {
            sb.AppendLine($"- {r.Name}: {r.Summary}");
            foreach (var m in r.Metrics) sb.AppendLine($"    {m.Label}: {m.Display}");
        }
        return sb.ToString();
    }
}
