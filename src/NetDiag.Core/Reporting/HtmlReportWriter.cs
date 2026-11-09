using System.Net;
using System.Text;
using NetDiag.Core.Model;

namespace NetDiag.Core.Reporting;

/// <summary>Gera um relatório HTML autocontido, para guardar ou enviar ao suporte/provedor.</summary>
public static class HtmlReportWriter
{
    public static string Render(DiagnosticReport report)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Diagnóstico de conexão</title>
            <style>
              :root { --bg:#f4f6f9; --card:#fff; --text:#1f2933; --muted:#616e7c; --line:#e2e6ec;
                      --ok:#16a34a; --warn:#d97706; --err:#dc2626; --info:#2563eb; }
              * { box-sizing: border-box; }
              body { margin:0; background:var(--bg); color:var(--text); font:15px/1.5 "Segoe UI", system-ui, sans-serif; }
              main { max-width: 860px; margin: 0 auto; padding: 32px 16px; }
              h1 { margin: 0 0 4px; font-size: 26px; }
              h2 { margin: 32px 0 12px; font-size: 18px; }
              .muted { color: var(--muted); }
              .verdict { color:#fff; border-radius:12px; padding:20px; margin:20px 0; }
              .verdict h2 { margin:0 0 4px; font-size:22px; }
              .card { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:16px; margin-bottom:12px; }
              .finding { border-left-width: 5px; }
              .tag { float:right; font-size:12px; color:var(--muted); background:var(--bg); border-radius:10px; padding:2px 10px; }
              .title { font-weight:600; font-size:16px; }
              table { width:100%; border-collapse:collapse; font-size:14px; }
              td { padding:4px 0; border-bottom:1px solid var(--line); vertical-align:top; }
              td:first-child { color:var(--muted); width:45%; padding-right:12px; }
              pre { background:var(--bg); padding:10px; border-radius:8px; overflow-x:auto; font-size:12px; margin:10px 0 0; }
            </style>
            </head>
            <body><main>
            """);

        sb.Append("<h1>Diagnóstico de conexão</h1>");
        sb.Append($"<div class=\"muted\">{E(report.StartedAt.ToString("dd/MM/yyyy HH:mm"))} · {E(report.MachineName)} · {E(report.OsDescription)}</div>");

        var verdictColor = report.Overall switch
        {
            Severity.Critical => "var(--err)",
            Severity.Warning => "var(--warn)",
            _ => "var(--ok)"
        };
        sb.Append($"<div class=\"verdict\" style=\"background:{verdictColor}\"><h2>{E(report.OverallTitle)}</h2>{E(report.Headline)}</div>");

        sb.Append("<h2>O que foi encontrado</h2>");
        foreach (var f in report.Findings)
        {
            var color = f.Severity switch
            {
                Severity.Critical => "var(--err)",
                Severity.Warning => "var(--warn)",
                _ => "var(--info)"
            };
            sb.Append($"<div class=\"card finding\" style=\"border-left-color:{color}\">");
            sb.Append($"<span class=\"tag\">{E(f.Area)}</span>");
            sb.Append($"<div class=\"title\">{E(f.Title)}</div>");
            sb.Append($"<p>{E(f.Explanation)}</p>");
            if (f.HasRecommendation) sb.Append($"<p><strong>O que fazer:</strong> {E(f.Recommendation)}</p>");
            sb.Append($"<div class=\"muted\" style=\"font-size:12px\">{E(f.SeverityLabel)} · confiança {E(f.ConfidenceLabel)}</div>");
            sb.Append("</div>");
        }

        sb.Append("<h2>Detalhes técnicos</h2>");
        foreach (var r in report.Results)
        {
            sb.Append("<div class=\"card\">");
            sb.Append($"<div class=\"title\">{E(r.Name)}</div>");
            sb.Append($"<div class=\"muted\">{E(r.Summary)}</div>");
            if (r.Metrics.Count > 0)
            {
                sb.Append("<table>");
                foreach (var m in r.Metrics)
                    sb.Append($"<tr><td>{E(m.Label)}</td><td>{E(m.Display)}</td></tr>");
                sb.Append("</table>");
            }
            if (r.Details.Count > 0)
                sb.Append($"<pre>{E(string.Join("\n", r.Details))}</pre>");
            sb.Append("</div>");
        }

        sb.Append($"<p class=\"muted\">Gerado pelo NetDiag em {E(report.FinishedAt.ToString("dd/MM/yyyy HH:mm:ss"))}.</p>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
}
