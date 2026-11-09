namespace NetDiag.Core.Model;

public enum Severity { Info, Warning, Critical }

public enum Confidence { Baixa, Media, Alta }

/// <summary>
/// Conclusão do diagnóstico em linguagem simples: o que foi encontrado, por que importa e o que fazer.
/// </summary>
public sealed record Finding(
    string Id,
    Severity Severity,
    string Area,
    string Title,
    string Explanation,
    string Recommendation,
    Confidence Confidence = Confidence.Media)
{
    /// <summary>Reservado para correções automáticas futuras (ver IRemediation).</summary>
    public string? FixId { get; init; }

    public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);

    public string ConfidenceLabel => Confidence switch
    {
        Confidence.Baixa => "baixa",
        Confidence.Media => "média",
        _ => "alta"
    };

    public string SeverityLabel => Severity switch
    {
        Severity.Critical => "Problema",
        Severity.Warning => "Atenção",
        _ => "Informação"
    };
}
