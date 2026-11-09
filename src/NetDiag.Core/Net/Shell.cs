using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NetDiag.Core.Net;

/// <summary>Executa comandos do Windows (ex.: netsh) e devolve a saída como texto.</summary>
public static class Shell
{
    public static async Task<string> RunAsync(string fileName, string arguments, CancellationToken ct, int timeoutMs = 15000)
    {
        var encoding = ConsoleEncoding();
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Não foi possível executar {fileName}.");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* já terminou */ }
            throw;
        }

        return await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
    }

    /// <summary>
    /// netsh escreve na code page OEM do sistema (ex.: 850 no Windows em português);
    /// sem isso, acentos como "rádio" e "recepção" chegam corrompidos.
    /// </summary>
    private static Encoding ConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(GetOEMCP());
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern int GetOEMCP();
}
