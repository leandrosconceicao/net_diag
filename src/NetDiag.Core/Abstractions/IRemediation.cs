using NetDiag.Core.Model;

namespace NetDiag.Core.Abstractions;

/// <summary>
/// Ponto de extensão para correções automáticas (ainda não implementado).
/// Ideia: cada Finding pode apontar um <see cref="Finding.FixId"/>; a interface
/// mostra um botão "Corrigir" quando existir uma IRemediation com esse Id.
/// Exemplos futuros: limpar cache DNS, trocar DNS, resetar Winsock, renovar IP.
/// </summary>
public interface IRemediation
{
    string Id { get; }
    string Title { get; }
    string Description { get; }

    /// <summary>Se exige executar como administrador (UAC).</summary>
    bool RequiresAdmin { get; }

    /// <summary>Se a correção pode ser desfeita (ex.: guardar DNS antigo).</summary>
    bool IsReversible { get; }

    Task<RemediationResult> ApplyAsync(CancellationToken ct);
}

public sealed record RemediationResult(bool Success, string Message);
