using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NetDiag.Core.Updates;

/// <summary>
/// Baixa o novo NetDiag.exe ao lado do atual, confere o SHA-256 e troca os arquivos.
/// O Windows permite renomear um exe em execução: o atual vira "NetDiag.exe.old" e é
/// apagado na próxima abertura.
/// </summary>
public sealed class UpdateInstaller
{
    private readonly HttpClient _http;

    public UpdateInstaller(HttpClient http) => _http = http;

    public static string NewPath(string exePath) => exePath + ".new";
    public static string OldPath(string exePath) => exePath + ".old";

    /// <summary>Baixa, verifica e instala. Em caso de erro, o exe atual fica intacto.</summary>
    public async Task InstallAsync(UpdateInfo update, string exePath, IProgress<double>? progress, CancellationToken ct)
    {
        var expected = ParseChecksum(await _http.GetStringAsync(update.ChecksumUrl, ct).ConfigureAwait(false));
        var newPath = NewPath(exePath);

        try
        {
            await DownloadAsync(update.ExeUrl, newPath, progress, ct).ConfigureAwait(false);

            if (!string.Equals(ComputeSha256(newPath), expected, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("o arquivo baixado está corrompido (o hash não confere). Tente de novo.");
        }
        catch
        {
            TryDelete(newPath);
            throw;
        }

        Swap(exePath, newPath);
    }

    /// <summary>Aceita só o hash ("ab12…") ou o formato do sha256sum ("ab12…  NetDiag.exe").</summary>
    public static string ParseChecksum(string text)
    {
        var token = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!Regex.IsMatch(token, "^[0-9a-fA-F]{64}$"))
            throw new UpdateException("o arquivo de verificação do download é inválido.");
        return token.ToLowerInvariant();
    }

    /// <summary>Troca o exe atual pelo novo; se o segundo passo falhar, desfaz o primeiro.</summary>
    public static void Swap(string exePath, string newPath)
    {
        var oldPath = OldPath(exePath);
        if (File.Exists(oldPath)) File.Delete(oldPath);

        File.Move(exePath, oldPath);
        try
        {
            File.Move(newPath, exePath);
        }
        catch
        {
            File.Move(oldPath, exePath);
            throw;
        }
    }

    /// <summary>Volta para a versão anterior (usado se o novo exe não conseguir iniciar).</summary>
    public static void Revert(string exePath)
    {
        var oldPath = OldPath(exePath);
        if (!File.Exists(oldPath)) return;
        if (File.Exists(exePath)) File.Delete(exePath);
        File.Move(oldPath, exePath);
    }

    /// <summary>
    /// Apaga sobras de uma atualização. Logo após o reinício, a versão anterior ainda pode estar
    /// fechando, então tenta algumas vezes.
    /// </summary>
    public static async Task CleanupOldVersionAsync(string exePath, int attempts = 5, TimeSpan? delay = null)
    {
        TryDelete(NewPath(exePath));
        var oldPath = OldPath(exePath);

        for (var i = 0; i < attempts && File.Exists(oldPath); i++)
        {
            if (TryDelete(oldPath)) return;
            await Task.Delay(delay ?? TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    /// <summary>A pasta do exe aceita gravação? (Ex.: Program Files não aceita sem administrador.)</summary>
    public static bool CanWriteTo(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".netdiag-write-test-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task DownloadAsync(Uri url, string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long done = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            done += n;
            if (total > 0) progress?.Report((double)done / total.Value);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
