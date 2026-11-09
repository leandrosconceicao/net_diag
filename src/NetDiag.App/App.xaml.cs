using System.Windows;
using NetDiag.App.Services;
using NetDiag.Core.Updates;

namespace NetDiag.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apaga o exe da versão anterior deixado por uma atualização (em segundo plano, sem travar a abertura).
        if (UpdateEnvironment.IsSupported && Environment.ProcessPath is string exe)
            _ = Task.Run(() => UpdateInstaller.CleanupOldVersionAsync(exe));
    }
}
