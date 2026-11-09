using System.Net.Http;
using System.Text.Json;

namespace NetDiag.Core.Updates;

/// <summary>
/// Consulta o último release publicado no GitHub. Qualquer falha (sem internet, limite da API,
/// resposta inesperada) resulta em "nenhuma atualização": o aviso de versão nova nunca atrapalha o uso.
/// </summary>
public sealed class UpdateChecker
{
    public const string DefaultRepository = "leandrosconceicao/net_diag";
    public const string ExeAssetName = "NetDiag.exe";
    public const string ChecksumAssetName = "NetDiag.exe.sha256";

    private readonly HttpClient _http;
    private readonly string _repository;

    public UpdateChecker(HttpClient http, string repository = DefaultRepository)
    {
        _http = http;
        _repository = repository;
    }

    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{_repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd($"NetDiag/{current}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null; // 403/429: limite da API; 404: ainda sem releases

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return Parse(doc.RootElement, current);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timeout
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Interpreta a resposta de /releases/latest. Público para facilitar testes.</summary>
    public static UpdateInfo? Parse(JsonElement release, Version current)
    {
        if (release.ValueKind != JsonValueKind.Object) return null;
        if (GetBool(release, "draft") || GetBool(release, "prerelease")) return null;

        var tag = GetString(release, "tag_name");
        if (AppVersion.Parse(tag) is not Version version || version <= current) return null;

        if (!TryGetHttpsUri(GetString(release, "html_url"), out var page)) return null;

        Uri? exe = null, checksum = null;
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = GetString(asset, "name");
                if (!TryGetHttpsUri(GetString(asset, "browser_download_url"), out var url)) continue;

                if (string.Equals(name, ExeAssetName, StringComparison.OrdinalIgnoreCase)) exe = url;
                else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) checksum = url;
            }
        }
        if (exe is null || checksum is null) return null;

        return new UpdateInfo(version, tag!, GetString(release, "body") ?? "", page, exe, checksum);
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static bool TryGetHttpsUri(string? text, out Uri uri)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
        {
            uri = u;
            return true;
        }
        uri = null!;
        return false;
    }
}
