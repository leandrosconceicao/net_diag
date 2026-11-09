using NetDiag.Core.Model;

namespace NetDiag.Core.Tests;

public class DiagnosticReportTests
{
    private static Finding F(string id, Severity severity, string title) =>
        new(id, severity, "Área", title, "explicação", "recomendação");

    [Fact]
    public void Without_findings_the_connection_is_reported_healthy()
    {
        var report = new DiagnosticReport();

        Assert.Equal(Severity.Info, report.Overall);
        Assert.Equal("Conexão saudável", report.OverallTitle);
        Assert.Equal("Nenhum problema foi detectado nos testes.", report.Headline);
    }

    [Fact]
    public void Overall_is_the_most_severe_finding()
    {
        var report = new DiagnosticReport();
        report.Findings.Add(F("a", Severity.Warning, "Atenção aqui"));
        report.Findings.Add(F("b", Severity.Critical, "Problema grave"));

        Assert.Equal(Severity.Critical, report.Overall);
        Assert.Equal("Problema encontrado", report.OverallTitle);
    }

    [Fact]
    public void Warning_only_gives_attention_title()
    {
        var report = new DiagnosticReport();
        report.Findings.Add(F("a", Severity.Warning, "Atenção aqui"));

        Assert.Equal("Há pontos de atenção", report.OverallTitle);
    }

    [Fact]
    public void Headline_uses_first_warning_or_worse_and_skips_info()
    {
        var report = new DiagnosticReport();
        report.Findings.Add(F("info", Severity.Info, "Só informação"));
        report.Findings.Add(F("warn", Severity.Warning, "Primeiro alerta"));
        report.Findings.Add(F("crit", Severity.Critical, "Segundo alerta"));

        Assert.Equal("Primeiro alerta", report.Headline);
    }

    [Fact]
    public void Get_returns_result_by_check_id_or_null()
    {
        var report = new DiagnosticReport();
        report.Results.Add(new CheckResult("dns", "DNS"));

        Assert.Equal("DNS", report.Get("dns")?.Name);
        Assert.Null(report.Get("speed"));
    }

    [Fact]
    public void ToPlainText_lists_findings_and_metrics()
    {
        using var _ = new CultureScope("pt-BR");
        var report = new DiagnosticReport();
        report.Findings.Add(F("a", Severity.Critical, "Sem internet"));

        var adapter = new CheckResult("adapter", "Adaptador de rede") { Summary = "Conectado via Cabo." };
        adapter.Set("link_mbps", "Velocidade do link", 1000, "Mbps");
        report.Results.Add(adapter);

        var text = report.ToPlainText();

        Assert.Contains("[Problema] Sem internet (Área)", text);
        Assert.Contains("Adaptador de rede: Conectado via Cabo.", text);
        Assert.Contains("Velocidade do link: 1000 Mbps", text);
    }
}
