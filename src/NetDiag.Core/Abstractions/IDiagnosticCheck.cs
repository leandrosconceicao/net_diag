using System.Net;
using System.Net.NetworkInformation;
using NetDiag.Core.Model;

namespace NetDiag.Core.Abstractions;

/// <summary>
/// Um teste de diagnóstico. Para adicionar um novo, implemente esta interface
/// e registre em <see cref="DiagnosticRunner.CreateDefault"/>.
/// </summary>
public interface IDiagnosticCheck
{
    string Id { get; }
    string Name { get; }
    string Description { get; }

    Task RunAsync(DiagnosticContext context, CheckResult result, CancellationToken ct);
}

/// <summary>Estado compartilhado entre os testes de uma execução.</summary>
public sealed class DiagnosticContext
{
    public DiagnosticContext(DiagnosticOptions options) => Options = options;

    public DiagnosticOptions Options { get; }

    public NetworkInterface? ActiveInterface { get; set; }
    public bool IsWireless { get; set; }
    public IPAddress? Gateway { get; set; }
    public List<IPAddress> DnsServers { get; } = new();

    /// <summary>Preenchido pelo teste de internet; testes seguintes pulam se for false.</summary>
    public bool InternetReachable { get; set; } = true;

    /// <summary>A rede bloqueia ping (ICMP); usar conexão TCP para medir latência.</summary>
    public bool IcmpBlocked { get; set; }

    public Dictionary<string, CheckResult> Results { get; } = new();

    /// <summary>Mensagem de andamento para a interface (ex.: "Medindo upload…").</summary>
    public Action<string>? ReportStatus { get; set; }
}
