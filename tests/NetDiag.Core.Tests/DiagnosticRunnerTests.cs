using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Rules;

namespace NetDiag.Core.Tests;

public class DiagnosticRunnerTests
{
    private sealed class FakeCheck : IDiagnosticCheck
    {
        private readonly Func<DiagnosticContext, CheckResult, Task> _run;

        public FakeCheck(string id, Func<DiagnosticContext, CheckResult, Task>? run = null)
        {
            Id = id;
            _run = run ?? ((_, _) => Task.CompletedTask);
        }

        public string Id { get; }
        public string Name => Id;
        public string Description => "";

        public Task RunAsync(DiagnosticContext context, CheckResult result, CancellationToken ct) =>
            _run(context, result);
    }

    private sealed class NoRule : IDiagnosticRule
    {
        public IEnumerable<Finding> Evaluate(RuleContext context) => [];
    }

    private sealed class ThrowingRule : IDiagnosticRule
    {
        public IEnumerable<Finding> Evaluate(RuleContext context) =>
            throw new InvalidOperationException("regra quebrada");
    }

    private sealed class ProgressCollector : IProgress<CheckProgress>
    {
        public List<CheckProgress> Items { get; } = new();
        public void Report(CheckProgress value) => Items.Add(value);
    }

    [Fact]
    public async Task Checks_run_in_registration_order()
    {
        var order = new List<string>();
        var runner = new DiagnosticRunner(
            [
                new FakeCheck("a", (_, _) => { order.Add("a"); return Task.CompletedTask; }),
                new FakeCheck("b", (_, _) => { order.Add("b"); return Task.CompletedTask; }),
                new FakeCheck("c", (_, _) => { order.Add("c"); return Task.CompletedTask; })
            ],
            [new NoRule()]);

        var report = await runner.RunAsync(new DiagnosticOptions());

        Assert.Equal(["a", "b", "c"], order);
        Assert.Equal(["a", "b", "c"], report.Results.Select(r => r.CheckId));
    }

    [Fact]
    public async Task A_failing_check_is_marked_as_error_and_the_others_still_run()
    {
        var ranAfter = false;
        var runner = new DiagnosticRunner(
            [
                new FakeCheck("boom", (_, _) => throw new InvalidOperationException("falhou")),
                new FakeCheck("after", (_, _) => { ranAfter = true; return Task.CompletedTask; })
            ],
            [new NoRule()]);

        var report = await runner.RunAsync(new DiagnosticOptions());

        var boom = report.Get("boom")!;
        Assert.Equal(CheckStatus.Error, boom.Status);
        Assert.True(boom.Has("check_failed"));
        Assert.Contains("falhou", boom.Summary);
        Assert.True(ranAfter);
    }

    [Fact]
    public async Task Checks_share_the_same_context()
    {
        bool? seen = null;
        var runner = new DiagnosticRunner(
            [
                new FakeCheck("adapter", (ctx, _) => { ctx.InternetReachable = false; return Task.CompletedTask; }),
                new FakeCheck("internet", (ctx, _) => { seen = ctx.InternetReachable; return Task.CompletedTask; })
            ],
            [new NoRule()]);

        await runner.RunAsync(new DiagnosticOptions());

        Assert.False(seen);
    }

    [Fact]
    public async Task Without_problems_a_single_ok_finding_is_added()
    {
        var runner = new DiagnosticRunner([new FakeCheck("a")], [new NoRule()]);

        var report = await runner.RunAsync(new DiagnosticOptions());

        var only = Assert.Single(report.Findings);
        Assert.Equal("ok", only.Id);
        Assert.Equal(Severity.Info, report.Overall);
    }

    [Fact]
    public async Task A_rule_exception_is_reported_as_a_finding_instead_of_crashing()
    {
        var runner = new DiagnosticRunner(Array.Empty<IDiagnosticCheck>(), [new ThrowingRule()]);

        var report = await runner.RunAsync(new DiagnosticOptions());

        var f = Assert.Single(report.Findings, x => x.Id == "engine.rule_error");
        Assert.Contains("regra quebrada", f.Explanation);
    }

    [Fact]
    public async Task Cancellation_before_start_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new DiagnosticRunner([new FakeCheck("a")], [new NoRule()]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(new DiagnosticOptions(), null, cts.Token));
    }

    [Fact]
    public async Task Progress_reports_running_and_done_for_each_check()
    {
        var progress = new ProgressCollector();
        var runner = new DiagnosticRunner([new FakeCheck("a"), new FakeCheck("b")], [new NoRule()]);

        await runner.RunAsync(new DiagnosticOptions(), progress);

        Assert.Equal(2, progress.Items.Count(p => p.State == CheckState.Done));
        Assert.All(progress.Items.Where(p => p.State == CheckState.Done), p => Assert.NotNull(p.Result));
    }

    [Fact]
    public void Default_setup_covers_every_layer_in_order()
    {
        var runner = DiagnosticRunner.CreateDefault();

        Assert.Equal(
            ["adapter", "wifi", "capability", "gateway", "internet", "dns", "traceroute", "speed"],
            runner.Checks.Select(c => c.Id));
        Assert.Single(runner.Rules);
    }
}
