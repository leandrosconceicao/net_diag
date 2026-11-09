using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NetDiag.Core.Abstractions;
using NetDiag.Core.Model;
using NetDiag.Core.Net;

namespace NetDiag.Core.Checks;

/// <summary>
/// Descobre o limite do próprio equipamento: a velocidade máxima que a placa de rede suporta,
/// se a velocidade foi fixada manualmente e o modo duplex. Usa WMI (root\StandardCimv2),
/// a mesma fonte do Get-NetAdapter, sem exigir administrador.
/// O teto do Wi-Fi é calculado nas regras, a partir da taxa de conexão lida no teste de Wi-Fi.
/// </summary>
public sealed class CapabilityCheck : IDiagnosticCheck
{
    public string Id => "capability";
    public string Name => "Capacidade do equipamento";
    public string Description => "Descobre o limite de velocidade da placa de rede e como ela está configurada.";

    private const string Scope = @"root\StandardCimv2";

    public Task RunAsync(DiagnosticContext ctx, CheckResult r, CancellationToken ct) =>
        Task.Run(() => Run(ctx, r), ct); // WMI é bloqueante: fora da thread da interface

    private static void Run(DiagnosticContext ctx, CheckResult r)
    {
        var nic = ctx.ActiveInterface;
        if (nic is null)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "Pulado: nenhum adaptador ativo.";
            return;
        }

        var name = EscapeWql(nic.Name);
        double? linkMbps = nic.Speed > 0 ? nic.Speed / 1_000_000.0 : null;

        try
        {
            var adapter = QueryFirst($"SELECT * FROM MSFT_NetAdapter WHERE Name = '{name}'",
                "FullDuplex", "DriverVersionString", "PnPDeviceID");

            if (adapter is null)
            {
                r.Status = CheckStatus.Skipped;
                r.Summary = "O Windows não informou os dados da placa de rede.";
                return;
            }

            if (adapter["DriverVersionString"] is string driver)
                r.SetText("driver_version", "Versão do driver", driver);

            if (adapter["PnPDeviceID"] is string pnp && pnp.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            {
                r.Flags.Add("usb_adapter");
                r.SetText("bus", "Conexão do adaptador", "USB");
            }

            if (ctx.IsWireless)
            {
                // Wi-Fi não tem configuração de velocidade/duplex fixa; o teto vem da taxa de conexão.
                r.Summary = "O limite do Wi-Fi depende do padrão do adaptador e do sinal (veja a análise).";
                return;
            }

            if (adapter["FullDuplex"] is bool fullDuplex)
            {
                r.SetText("duplex", "Modo duplex", fullDuplex ? "full-duplex" : "half-duplex");
                if (!fullDuplex)
                {
                    r.Flags.Add("half_duplex");
                    r.Escalate(CheckStatus.Warning);
                }
            }

            ReadSpeedDuplexSetting(name, linkMbps, r);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            r.Status = CheckStatus.Skipped;
            r.Summary = "Não foi possível consultar o Windows sobre a placa de rede.";
            r.Details.Add(ex.Message);
            return;
        }

        var max = r.Get("card_max_mbps");
        r.Summary = max is double m && linkMbps is double l
            ? $"A placa suporta até {NetUtil.FormatLink(m)}; o link atual é de {NetUtil.FormatLink(l)}."
            : linkMbps is double l2
                ? $"Link atual de {NetUtil.FormatLink(l2)} (o driver não informa o máximo da placa)."
                : "O driver não informa a velocidade da placa.";
    }

    /// <summary>
    /// Lê a propriedade padrão "*SpeedDuplex" do driver: as opções válidas revelam o máximo da placa,
    /// e o valor atual revela se alguém fixou a velocidade manualmente (0 = negociação automática).
    /// </summary>
    private static void ReadSpeedDuplexSetting(string escapedName, double? linkMbps, CheckResult r)
    {
        var setting = QueryFirst(
            "SELECT * FROM MSFT_NetAdapterAdvancedPropertySettingData " +
            $"WHERE Name = '{escapedName}' AND RegistryKeyword = '*SpeedDuplex'",
            "DisplayValue", "RegistryValue", "ValidDisplayValues");

        if (setting is null) return;

        var valid = setting["ValidDisplayValues"] as string[] ?? [];
        var max = valid.Select(ParseMbps).Max(); // ignora opções sem número, como "Auto Negotiation"
        r.Set("card_max_mbps", "Velocidade máxima suportada pela placa", max, "Mbps");

        if (setting["DisplayValue"] is string display)
            r.SetText("speed_setting", "Configuração de velocidade", display);

        var registryValue = setting["RegistryValue"] switch
        {
            string[] { Length: > 0 } arr => arr[0],
            string s => s,
            _ => null
        };

        if (registryValue is not null && registryValue != "0")
        {
            r.Flags.Add("speed_forced");
            if (max is double m && linkMbps < m) r.Escalate(CheckStatus.Warning);
        }
    }

    /// <summary>Executa uma consulta WMI e copia as propriedades pedidas (o objeto não sobrevive ao Dispose).</summary>
    private static Dictionary<string, object?>? QueryFirst(string wql, params string[] properties)
    {
        using var searcher = new ManagementObjectSearcher(Scope, wql);
        using var results = searcher.Get();

        foreach (ManagementBaseObject obj in results)
        {
            using (obj)
            {
                var values = new Dictionary<string, object?>();
                foreach (var p in properties)
                {
                    try { values[p] = obj[p]; }
                    catch (ManagementException) { values[p] = null; } // propriedade ausente nesta versão do Windows
                }
                return values;
            }
        }
        return null;
    }

    /// <summary>"1.0 Gbps Full Duplex" → 1000; "100 Mbps Half Duplex" → 100; "Auto Negotiation" → null.</summary>
    private static double? ParseMbps(string text)
    {
        var m = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*([GM])b", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        var value = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return m.Groups[2].Value.Equals("G", StringComparison.OrdinalIgnoreCase) ? value * 1000 : value;
    }

    private static string EscapeWql(string value) => value.Replace(@"\", @"\\").Replace("'", @"\'");
}
