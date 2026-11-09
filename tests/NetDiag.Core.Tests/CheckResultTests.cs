using NetDiag.Core.Model;

namespace NetDiag.Core.Tests;

public class CheckResultTests
{
    [Fact]
    public void Set_replaces_metric_with_same_key()
    {
        var r = new CheckResult("x", "X");

        r.Set("a", "A", 1).Set("a", "A", 2);

        Assert.Single(r.Metrics);
        Assert.Equal<double?>(2, r.Get("a"));
    }

    [Fact]
    public void SetText_stores_text_without_numeric_value()
    {
        var r = new CheckResult("x", "X");

        r.SetText("ip", "Endereço IP", "192.168.0.2");

        Assert.Equal("192.168.0.2", r.GetText("ip"));
        Assert.Null(r.Get("ip"));
    }

    [Fact]
    public void Escalate_raises_status_to_warning()
    {
        var r = new CheckResult("x", "X");

        r.Escalate(CheckStatus.Warning);

        Assert.Equal(CheckStatus.Warning, r.Status);
    }

    [Fact]
    public void Escalate_never_lowers_status()
    {
        var r = new CheckResult("x", "X");
        r.Escalate(CheckStatus.Warning);

        r.Escalate(CheckStatus.Ok);

        Assert.Equal(CheckStatus.Warning, r.Status);
    }

    [Fact]
    public void Escalate_does_not_replace_error_with_warning()
    {
        var r = new CheckResult("x", "X");
        r.Escalate(CheckStatus.Error);

        r.Escalate(CheckStatus.Warning);

        Assert.Equal(CheckStatus.Error, r.Status);
    }

    [Fact]
    public void Has_reflects_added_flags()
    {
        var r = new CheckResult("x", "X");
        r.Flags.Add("vpn");

        Assert.True(r.Has("vpn"));
        Assert.False(r.Has("proxy"));
    }

    [Fact]
    public void Metric_display_formats_value_with_unit()
    {
        using var _ = new CultureScope("pt-BR");

        Assert.Equal("12,5 ms", new Metric("k", "L", 12.5, "ms").Display);
    }

    [Fact]
    public void Metric_display_shows_dash_without_value()
    {
        Assert.Equal("—", new Metric("k", "L", null).Display);
    }

    [Fact]
    public void Metric_display_prefers_text_over_value()
    {
        Assert.Equal("Casa", new Metric("k", "L", 5, "", "Casa").Display);
    }
}
