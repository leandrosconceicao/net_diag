using System.IO;
using System.Text.Json;

namespace NetDiag.App.Services;

/// <summary>Onde o app está rodando e quando foi a última verificação de atualização.</summary>
internal static class UpdateEnvironment
{
    /// <summary>
    /// Só o exe publicado (single-file) se atualiza. Em "dotnet run" o assembly tem caminho próprio
    /// e trocar o exe da pasta bin não faria sentido.
    /// </summary>
    public static bool IsSupported =>
#pragma warning disable IL3000 // em single-file, Location é vazio: é exatamente isso que queremos detectar
        !string.IsNullOrEmpty(Environment.ProcessPath) && string.IsNullOrEmpty(typeof(App).Assembly.Location);
#pragma warning restore IL3000

    private static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetDiag", "update.json");

    private sealed record State(DateTimeOffset LastCheck);

    public static bool IsCheckDue(TimeSpan interval)
    {
        try
        {
            if (!File.Exists(StateFile)) return true;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile));
            return state is null || DateTimeOffset.UtcNow - state.LastCheck >= interval;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }

    public static void MarkChecked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(new State(DateTimeOffset.UtcNow)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem onde gravar: verifica de novo na próxima abertura
        }
    }
}
