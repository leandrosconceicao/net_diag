namespace NetDiag.Core.Model;

public enum CheckStatus { Ok, Warning, Error, Skipped }

/// <summary>Uma medição exibida nos detalhes técnicos e lida pelas regras.</summary>
public sealed record Metric(string Key, string Label, double? Value, string Unit = "", string? Text = null)
{
    public string Display => Text ?? (Value is double v ? $"{v:0.##} {Unit}".Trim() : "—");
}

/// <summary>Resultado de um teste (uma camada do diagnóstico).</summary>
public sealed class CheckResult
{
    public CheckResult(string checkId, string name)
    {
        CheckId = checkId;
        Name = name;
    }

    public string CheckId { get; }
    public string Name { get; }
    public CheckStatus Status { get; set; } = CheckStatus.Ok;
    public string Summary { get; set; } = "";
    public List<Metric> Metrics { get; } = new();

    /// <summary>Fatos booleanos usados pelas regras (ex.: "apipa", "vpn", "offline").</summary>
    public HashSet<string> Flags { get; } = new();

    /// <summary>Linhas livres para os detalhes técnicos (ex.: saltos do traceroute).</summary>
    public List<string> Details { get; } = new();

    public TimeSpan Duration { get; set; }
    public string? Error { get; set; }

    public double? Get(string key) => Metrics.FirstOrDefault(m => m.Key == key)?.Value;
    public string? GetText(string key) => Metrics.FirstOrDefault(m => m.Key == key)?.Text;
    public bool Has(string flag) => Flags.Contains(flag);

    public CheckResult Set(string key, string label, double? value, string unit = "")
    {
        Metrics.RemoveAll(m => m.Key == key);
        Metrics.Add(new Metric(key, label, value, unit));
        return this;
    }

    public CheckResult SetText(string key, string label, string text)
    {
        Metrics.RemoveAll(m => m.Key == key);
        Metrics.Add(new Metric(key, label, null, "", text));
        return this;
    }

    /// <summary>Piora o status (nunca melhora).</summary>
    public void Escalate(CheckStatus status)
    {
        if (Rank(status) > Rank(Status)) Status = status;
    }

    private static int Rank(CheckStatus s) => s switch
    {
        CheckStatus.Error => 3,
        CheckStatus.Warning => 2,
        CheckStatus.Ok => 1,
        _ => 0
    };
}
