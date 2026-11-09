using System.Globalization;
using System.Text.RegularExpressions;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>
/// Camada 2: qualidade do Wi-Fi (sinal, faixa, canal, congestionamento).
/// Usa "netsh wlan", cuja saída muda conforme o idioma do Windows — por isso
/// as chaves são procuradas em português e inglês.
/// </summary>
public sealed class WifiCheck : IDiagnosticCheck
{
    public string Id => "wifi";
    public string Name => "Wi-Fi";
    public string Description => "Mede o sinal, a faixa (2,4/5 GHz) e o congestionamento do canal.";

    public async Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct)
    {
        if (!ctx.IsWireless)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "A conexão atual não é Wi-Fi.";
            return;
        }

        var output = await Shell.RunAsync("netsh", "wlan show interfaces", ct).ConfigureAwait(false);
        var blocks = NetshParser.ParseBlocks(output, "name", "nome");

        var block = blocks.FirstOrDefault(b =>
                        NetshParser.Find(b, "name", "nome") is string n &&
                        n.Equals(ctx.ActiveInterface?.Name, StringComparison.OrdinalIgnoreCase))
                    ?? blocks.FirstOrDefault(b => NetshParser.Find(b, "ssid") is not null);

        var ssid = block is null ? null : NetshParser.Find(block, "ssid");
        if (block is null || ssid is null)
        {
            if (LooksLikeLocationBlocked(output))
            {
                r.Flags.Add("location_blocked");
                r.Status = CheckStatus.Warning;
                r.Summary = "O Windows bloqueou a leitura dos dados do Wi-Fi (permissão de localização desativada).";
                r.Details.Add("Ative em: Configurações > Privacidade e segurança > Localização > " +
                              "\"Permitir que aplicativos da área de trabalho acessem sua localização\".");
            }
            else
            {
                r.Status = CheckStatus.Skipped;
                r.Summary = "Não foi possível ler os dados do Wi-Fi.";
            }
            return;
        }

        var signal = NetshParser.ParseNumber(NetshParser.Find(block, "signal", "sinal"));
        var channel = NetshParser.ParseNumber(NetshParser.Find(block, "channel", "canal"));
        var rx = NetshParser.ParseNumber(NetshParser.Find(block, "receive rate", "taxa de recep"));
        var tx = NetshParser.ParseNumber(NetshParser.Find(block, "transmit rate", "taxa de transmiss"));
        var radio = NetshParser.Find(block, "radio type", "tipo de r");
        var band = NetshParser.Find(block, "band", "banda");

        band ??= channel switch
        {
            <= 14 => "2,4 GHz",
            >= 32 => "5 GHz",
            _ => null
        };
        var is24 = band is not null && band.StartsWith("2", StringComparison.Ordinal);
        if (is24) r.Flags.Add("band_24");

        r.SetText("ssid", "Rede (SSID)", ssid);
        r.Set("signal", "Intensidade do sinal", signal, "%");
        if (signal is double s) r.Set("signal_dbm", "Sinal aproximado", s / 2 - 100, "dBm");
        if (band is not null) r.SetText("band", "Faixa", band);
        r.Set("channel", "Canal", channel);
        if (radio is not null) r.SetText("radio", "Padrão", radio);
        r.Set("rx_mbps", "Taxa de recepção", rx, "Mbps");
        r.Set("tx_mbps", "Taxa de transmissão", tx, "Mbps");

        if (channel is double ch)
        {
            ctx.ReportStatus?.Invoke("Procurando redes vizinhas…");
            var neighbors = await CountNeighborsAsync((int)ch, is24, ct).ConfigureAwait(false);
            if (neighbors is int count)
            {
                r.Set("neighbors", is24 ? "Redes no mesmo canal ou sobrepostas" : "Redes no mesmo canal", count);
                if (count >= 4) r.Flags.Add("congested");
            }
        }

        if (signal is < 40)
        {
            r.Flags.Add("weak_signal");
            r.Status = CheckStatus.Warning;
        }
        if (r.Has("congested")) r.Escalate(CheckStatus.Warning);

        r.Summary = $"\"{ssid}\" — sinal {signal:0}%{(band is null ? "" : $", {band}")}{(channel is null ? "" : $", canal {channel:0}")}.";
    }

    /// <summary>
    /// Conta redes que disputam o mesmo espaço: no 2,4 GHz canais a menos de 5 de distância se sobrepõem.
    /// Desconta a própria rede.
    /// </summary>
    private static async Task<int?> CountNeighborsAsync(int channel, bool is24, CancellationToken ct)
    {
        try
        {
            var output = await Shell.RunAsync("netsh", "wlan show networks mode=bssid", ct).ConfigureAwait(false);
            var channels = Regex.Matches(output, @"^\s*(?:Channel|Canal)\s*:\s*(\d+)",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToList();

            if (channels.Count == 0) return null;

            var competing = channels.Count(c => is24 ? Math.Abs(c - channel) < 5 : c == channel);
            return Math.Max(0, competing - 1);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool LooksLikeLocationBlocked(string output)
    {
        var o = output.ToLowerInvariant();
        return o.Contains("location") || o.Contains("localiza");
    }
}

/// <summary>Leitura da saída "Chave : Valor" do netsh.</summary>
internal static class NetshParser
{
    /// <summary>Divide a saída em blocos; um novo bloco começa em cada chave de início (ex.: "Nome").</summary>
    public static List<Dictionary<string, string>> ParseBlocks(string text, params string[] blockStartKeys)
    {
        var blocks = new List<Dictionary<string, string>>();
        Dictionary<string, string>? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;

            var key = line[..idx].Trim().ToLowerInvariant();
            var value = line[(idx + 1)..].Trim();
            if (key.Length == 0) continue;

            if (current is null || blockStartKeys.Contains(key))
            {
                current = new Dictionary<string, string>();
                blocks.Add(current);
            }
            current.TryAdd(key, value);
        }
        return blocks;
    }

    /// <summary>Primeiro valor cuja chave começa com algum dos prefixos.</summary>
    public static string? Find(Dictionary<string, string> block, params string[] keyPrefixes)
    {
        foreach (var (key, value) in block)
            if (keyPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
                return value;
        return null;
    }

    public static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"\d+(?:[.,]\d+)?");
        if (!m.Success) return null;
        return double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
