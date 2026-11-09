using NetDiag.Core.Checks;

namespace NetDiag.Core.Tests;

/// <summary>Saída de "netsh wlan show interfaces" em português (chaves em pt-BR).</summary>
public class NetshParserTests
{
    private const string Sample =
        "Nome                   : Wi-Fi\n" +
        "Descrição              : Intel(R) Wi-Fi 6 AX201\n" +
        "SSID                   : Casa\n" +
        "Sinal                  : 80%\n" +
        "Canal                  : 36\n" +
        "\n" +
        "Nome                   : Ethernet\n" +
        "Canal                  : 1\n";

    [Fact]
    public void ParseBlocks_starts_a_new_block_on_each_block_key()
    {
        var blocks = NetshParser.ParseBlocks(Sample, "name", "nome");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Casa", NetshParser.Find(blocks[0], "ssid"));
        Assert.Equal("1", NetshParser.Find(blocks[1], "canal"));
    }

    [Fact]
    public void Find_matches_by_key_prefix_in_either_language()
    {
        var block = NetshParser.ParseBlocks(Sample, "nome")[0];

        Assert.Equal("80%", NetshParser.Find(block, "signal", "sinal"));
        Assert.Equal("36", NetshParser.Find(block, "channel", "canal"));
    }

    [Fact]
    public void Find_returns_null_for_missing_key()
    {
        var blocks = NetshParser.ParseBlocks(Sample, "nome");

        Assert.Null(NetshParser.Find(blocks[1], "ssid"));
    }

    [Fact]
    public void ParseBlocks_keeps_the_first_value_of_a_repeated_key()
    {
        var blocks = NetshParser.ParseBlocks("Canal : 36\nCanal : 149\n");

        Assert.Single(blocks);
        Assert.Equal("36", NetshParser.Find(blocks[0], "canal"));
    }

    [Theory]
    [InlineData("80%", 80.0)]
    [InlineData("54,5 Mbps", 54.5)]
    [InlineData("2.4", 2.4)]
    [InlineData("  144 Mbps", 144.0)]
    public void ParseNumber_reads_first_number_with_dot_or_comma(string text, double expected) =>
        Assert.Equal<double?>(expected, NetshParser.ParseNumber(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sem valor")]
    public void ParseNumber_returns_null_when_there_is_no_number(string text) =>
        Assert.Null(NetshParser.ParseNumber(text));

    [Fact]
    public void ParseNumber_returns_null_for_null_input() =>
        Assert.Null(NetshParser.ParseNumber(null));
}
