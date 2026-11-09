using System.Net;
using System.Net.Http;
using System.Text;
using NetDiag.Core.Updates;

namespace NetDiag.Core.Tests;

/// <summary>Responde às requisições com uma função, sem acessar a rede.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(respond(request));
    }
}

public class UpdateCheckerTests
{
    private static readonly Version Current = new(0, 1, 0);

    private static string ReleaseJson(string tag, bool withExe = true, bool withChecksum = true, bool prerelease = false)
    {
        var assets = new List<string>();
        if (withExe) assets.Add(Asset("NetDiag.exe"));
        if (withChecksum) assets.Add(Asset("NetDiag.exe.sha256"));

        return $$"""
            {
              "tag_name": "{{tag}}",
              "html_url": "https://github.com/leandrosconceicao/net_diag/releases/tag/{{tag}}",
              "body": "Notas da versão",
              "draft": false,
              "prerelease": {{(prerelease ? "true" : "false")}},
              "assets": [{{string.Join(",", assets)}}]
            }
            """;

        static string Asset(string name) =>
            $$"""{"name": "{{name}}", "browser_download_url": "https://github.com/leandrosconceicao/net_diag/releases/download/v9/{{name}}"}""";
    }

    private static (UpdateChecker Checker, FakeHandler Handler) Checker(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        return (new UpdateChecker(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Newer_release_is_offered_with_its_assets()
    {
        var (checker, _) = Checker(ReleaseJson("v0.2.0"));

        var update = await checker.CheckAsync(Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 0), update.Version);
        Assert.Equal("v0.2.0", update.TagName);
        Assert.Equal("Notas da versão", update.ReleaseNotes);
        Assert.EndsWith("/NetDiag.exe", update.ExeUrl.AbsoluteUri);
        Assert.EndsWith("/NetDiag.exe.sha256", update.ChecksumUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Request_goes_to_latest_release_of_the_repository_with_user_agent()
    {
        var (checker, handler) = Checker(ReleaseJson("v0.2.0"));

        await checker.CheckAsync(Current);

        Assert.Equal("https://api.github.com/repos/leandrosconceicao/net_diag/releases/latest",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Contains("NetDiag", handler.LastRequest.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    [InlineData("0.1")]
    public async Task Same_or_older_release_is_not_offered(string tag)
    {
        var (checker, _) = Checker(ReleaseJson(tag));

        Assert.Null(await checker.CheckAsync(Current));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v1.0.0-beta")]
    [InlineData("")]
    public async Task Invalid_or_prerelease_tag_is_ignored(string tag)
    {
        var (checker, _) = Checker(ReleaseJson(tag));

        Assert.Null(await checker.CheckAsync(Current));
    }

    [Fact]
    public async Task Release_marked_as_prerelease_is_ignored()
    {
        var (checker, _) = Checker(ReleaseJson("v0.2.0", prerelease: true));

        Assert.Null(await checker.CheckAsync(Current));
    }

    [Fact]
    public async Task Release_without_exe_is_ignored()
    {
        var (checker, _) = Checker(ReleaseJson("v0.2.0", withExe: false));

        Assert.Null(await checker.CheckAsync(Current));
    }

    [Fact]
    public async Task Release_without_checksum_is_ignored()
    {
        var (checker, _) = Checker(ReleaseJson("v0.2.0", withChecksum: false));

        Assert.Null(await checker.CheckAsync(Current));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]   // limite da API
    [InlineData(HttpStatusCode.NotFound)]    // repositório ainda sem releases
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Http_errors_mean_no_update(HttpStatusCode status)
    {
        var (checker, _) = Checker("{}", status);

        Assert.Null(await checker.CheckAsync(Current));
    }

    [Fact]
    public async Task Network_failure_means_no_update()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("sem rede"));

        Assert.Null(await new UpdateChecker(new HttpClient(handler)).CheckAsync(Current));
    }

    [Fact]
    public async Task Malformed_json_means_no_update()
    {
        var (checker, _) = Checker("isto não é json");

        Assert.Null(await checker.CheckAsync(Current));
    }
}

public class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0+3f2a9c1d", 0, 1, 0)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.2", 1, 2, 0)]
    [InlineData("1.2.3.4", 1, 2, 3)]
    public void Parse_normalizes_to_three_parts(string text, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), AppVersion.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.0.0-rc1")]
    public void Parse_rejects_invalid_and_prerelease(string? text) =>
        Assert.Null(AppVersion.Parse(text));

    [Fact]
    public void Two_and_three_part_versions_compare_equal() =>
        Assert.Equal(AppVersion.Parse("1.2"), AppVersion.Parse("1.2.0"));

    [Fact]
    public void Current_reads_the_assembly_version() =>
        Assert.NotNull(AppVersion.Current(typeof(UpdateChecker).Assembly));
}
