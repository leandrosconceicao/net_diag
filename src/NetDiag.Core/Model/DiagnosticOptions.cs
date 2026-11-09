using System.Net;

namespace NetDiag.Core.Model;

public sealed class DiagnosticOptions
{
    public bool RunSpeedTest { get; set; } = true;

    /// <summary>Velocidade de download contratada (Mbps), se o usuário informar.</summary>
    public double? ContractedDownloadMbps { get; set; }

    public TimeSpan DownloadTestDuration { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan UploadTestDuration { get; set; } = TimeSpan.FromSeconds(6);
    public int SpeedTestStreams { get; set; } = 4;

    public IPAddress[] InternetTargets { get; set; } =
        [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("8.8.8.8")];

    public IPAddress TraceTarget { get; set; } = IPAddress.Parse("8.8.8.8");

    public IPAddress[] PublicDnsServers { get; set; } =
        [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("8.8.8.8")];

    public string[] DnsTestDomains { get; set; } =
        ["www.google.com", "www.microsoft.com", "www.wikipedia.org", "www.globo.com", "www.youtube.com"];

    public string SpeedDownloadUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=25000000";
    public string SpeedUploadUrl { get; set; } = "https://speed.cloudflare.com/__up";
}
