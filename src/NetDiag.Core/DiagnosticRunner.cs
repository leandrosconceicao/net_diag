using System.Diagnostics;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Checks;
using NetDiag.Core.Model;
using NetDiag.Core.Rules;

namespace NetDiag.Core;

public enum CheckState { Pending, Running, Done }

/// <summary>Notificação de andamento enviada para a interface.</summary>
public sealed record CheckProgress(string CheckId, string Name, CheckState State, CheckResult? Result, string? Message = null);

/// <summary>
/// Executa os testes em sequência (de dentro para fora) e aplica as regras.
/// Não depende de interface gráfica: pode ser reutilizado por um modo de monitoramento
/// em segundo plano ou por uma versão de linha de comando.
/// </summary>
public sealed class DiagnosticRunner
{
    public DiagnosticRunner(IEnumerable<IDiagnosticCheck> checks, IEnumerable<IDiagnosticRule> rules)
    {
        Checks = checks.ToList();
        Rules = rules.ToList();
    }

    public IReadOnlyList<IDiagnosticCheck> Checks { get; }
    public IReadOnlyList<IDiagnosticRule> Rules { get; }

    public static DiagnosticRunner CreateDefault() => new(
        [
            new AdapterCheck(),
            new WifiCheck(),
            new CapabilityCheck(),
            new GatewayCheck(),
            new InternetCheck(),
            new DnsCheck(),
            new TracerouteCheck(),
            new SpeedCheck()
        ],
        [new BuiltInRules()]);

    public async Task<DiagnosticReport> RunAsync(
        DiagnosticOptions options, IProgress<CheckProgress>? progress = null, CancellationToken ct = default)
    {
        var context = new DiagnosticContext(options);
        var report = new DiagnosticReport { StartedAt = DateTimeOffset.Now };

        foreach (var check in Checks)
        {
            ct.ThrowIfCancellationRequested();

            var result = new CheckResult(check.Id, check.Name);
            context.ReportStatus = msg => progress?.Report(
                new CheckProgress(check.Id, check.Name, CheckState.Running, null, msg));
            progress?.Report(new CheckProgress(check.Id, check.Name, CheckState.Running, null));

            var sw = Stopwatch.StartNew();
            try
            {
                await check.RunAsync(context, result, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Status = CheckStatus.Error;
                result.Error = ex.ToString();
                result.Summary = "O teste não pôde ser concluído: " + ex.Message;
                result.Flags.Add("check_failed");
            }

            result.Duration = sw.Elapsed;
            context.Results[check.Id] = result;
            report.Results.Add(result);
            progress?.Report(new CheckProgress(check.Id, check.Name, CheckState.Done, result));
        }
        context.ReportStatus = null;

        ApplyRules(report, options);
        report.FinishedAt = DateTimeOffset.Now;
        return report;
    }

    private void ApplyRules(DiagnosticReport report, DiagnosticOptions options)
    {
        var ruleContext = new RuleContext(report, options);
        var findings = new List<Finding>();

        foreach (var rule in Rules)
        {
            try
            {
                findings.AddRange(rule.Evaluate(ruleContext));
            }
            catch (Exception ex)
            {
                findings.Add(new Finding("engine.rule_error", Severity.Info, "Diagnóstico",
                    "Uma regra de análise falhou", ex.Message, ""));
            }
        }

        if (!findings.Any(f => f.Severity >= Severity.Warning))
        {
            findings.Insert(0, new Finding("ok", Severity.Info, "Resumo",
                "Nenhum problema detectado",
                "Todos os testes ficaram dentro do esperado. Se a lentidão acontece só em um site ou aplicativo, " +
                "o problema provavelmente está no serviço, não na sua conexão. Se for intermitente, rode o " +
                "diagnóstico de novo no momento em que o problema acontecer.",
                "", Confidence.Media));
        }

        report.Findings.AddRange(findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Id == "ok")
            .ThenByDescending(f => f.Confidence));
    }
}
