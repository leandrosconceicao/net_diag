using System.Reflection;

namespace NetDiag.Core.Updates;

/// <summary>Uma versão publicada no GitHub, mais nova que a instalada.</summary>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleaseNotes,
    Uri ReleasePage,
    Uri ExeUrl,
    Uri ChecksumUrl);

/// <summary>Falha esperada durante a atualização, com mensagem pronta para o usuário.</summary>
public sealed class UpdateException(string message) : Exception(message);

public static class AppVersion
{
    /// <summary>
    /// Lê "v1.2.3", "1.2" ou "0.1.0+abc123" (o SDK acrescenta o hash do commit) como 1.2.3 / 1.2.0 / 0.1.0.
    /// Pré-lançamentos ("1.0.0-beta") retornam null: não são oferecidos como atualização.
    /// </summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        if (s.Contains('-')) return null;

        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    /// <summary>Versão do executável em uso.</summary>
    public static Version Current(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Parse(informational)
               ?? (assembly.GetName().Version is Version v ? Normalize(v) : new Version(0, 0, 0));
    }

    /// <summary>Sempre três partes, para que 1.2 e 1.2.0 sejam iguais.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
