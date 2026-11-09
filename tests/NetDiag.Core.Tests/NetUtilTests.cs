using System.Net;
using NetDiag.Core.Net;

namespace NetDiag.Core.Tests;

public class NetUtilTests
{
    [Theory]
    [InlineData("169.254.10.1", true)]
    [InlineData("169.254.0.0", true)]
    [InlineData("169.253.1.1", false)]
    [InlineData("192.168.0.1", false)]
    public void IsApipa_detects_self_assigned_ipv4(string ip, bool expected) =>
        Assert.Equal(expected, NetUtil.IsApipa(IPAddress.Parse(ip)));

    [Fact]
    public void IsApipa_ignores_ipv6() =>
        Assert.False(NetUtil.IsApipa(IPAddress.Parse("fe80::1")));

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    public void IsPrivate_accepts_private_and_cgnat_ranges(string ip) =>
        Assert.True(NetUtil.IsPrivate(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("8.8.8.8")]
    public void IsPrivate_rejects_public_addresses(string ip) =>
        Assert.False(NetUtil.IsPrivate(IPAddress.Parse(ip)));

    [Fact]
    public void IsPrivate_treats_ipv6_link_local_as_private() =>
        Assert.True(NetUtil.IsPrivate(IPAddress.Parse("fe80::1")));

    [Theory]
    [InlineData(1000, "1 Gbps")]
    [InlineData(2500, "2,5 Gbps")]
    [InlineData(100, "100 Mbps")]
    public void FormatLink_switches_to_gbps_from_1000_mbps(double mbps, string expected)
    {
        using var _ = new CultureScope("pt-BR");
        Assert.Equal(expected, NetUtil.FormatLink(mbps));
    }

    [Fact]
    public void FormatMbps_shows_dash_when_unknown()
    {
        using var _ = new CultureScope("pt-BR");
        Assert.Equal("—", NetUtil.FormatMbps(null));
    }

    [Fact]
    public void FormatMbps_uses_one_decimal_place()
    {
        using var _ = new CultureScope("pt-BR");
        Assert.Equal("12,3 Mbps", NetUtil.FormatMbps(12.34));
    }

    [Theory]
    [InlineData(3.4, 3.0)]
    [InlineData(12.6, 13.0)]
    [InlineData(87.0, 85.0)]
    [InlineData(143.0, 140.0)]
    public void RoundEstimate_avoids_false_precision(double mbps, double expected) =>
        Assert.Equal(expected, NetUtil.RoundEstimate(mbps));
}
