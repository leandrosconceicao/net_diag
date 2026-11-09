using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NetDiag.Core;
using NetDiag.Core.Model;
using NetDiag.Core.Reporting;

namespace NetDiag.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private DiagnosticReport? _report;
    private bool _isRunning;
    private bool _runSpeedTest = true;
    private string _contractedSpeedText = "";
    private string _statusText = "Pronto.";
    private double _progressValue;
    private int _completed;

    public MainViewModel()
    {
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        SaveReportCommand = new RelayCommand(SaveReport, () => Report is not null && !IsRunning);
        CopySummaryCommand = new RelayCommand(CopySummary, () => Report is not null && !IsRunning);

        foreach (var check in DiagnosticRunner.CreateDefault().Checks)
            Steps.Add(new StepViewModel(check.Id, check.Name, check.Description));
    }

    public ObservableCollection<StepViewModel> Steps { get; } = new();
    public ObservableCollection<Finding> Findings { get; } = new();
    public ObservableCollection<CheckResult> Results { get; } = new();

    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SaveReportCommand { get; }
    public ICommand CopySummaryCommand { get; }

    public string PlaceholderText =>
        "Clique em \"Diagnosticar\" para começar. O teste leva cerca de 40 segundos. " +
        "Para um resultado mais fiel, feche downloads e vídeos que estejam rodando.";

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsIdle => !IsRunning;

    public bool RunSpeedTest
    {
        get => _runSpeedTest;
        set => Set(ref _runSpeedTest, value);
    }

    public string ContractedSpeedText
    {
        get => _contractedSpeedText;
        set => Set(ref _contractedSpeedText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => Set(ref _progressValue, value);
    }

    public DiagnosticReport? Report
    {
        get => _report;
        private set
        {
            if (!Set(ref _report, value)) return;
            OnPropertyChanged(nameof(HasReport));
            OnPropertyChanged(nameof(Overall));
            OnPropertyChanged(nameof(VerdictTitle));
            OnPropertyChanged(nameof(VerdictText));
        }
    }

    public bool HasReport => Report is not null;
    public Severity Overall => Report?.Overall ?? Severity.Info;
    public string VerdictTitle => Report?.OverallTitle ?? "";
    public string VerdictText => Report?.Headline ?? "";

    private async Task RunAsync()
    {
        if (!TryParseContracted(out var contracted))
        {
            StatusText = "Velocidade contratada inválida. Use só números, por exemplo 300.";
            return;
        }

        var runner = DiagnosticRunner.CreateDefault();
        Steps.Clear();
        foreach (var check in runner.Checks)
            Steps.Add(new StepViewModel(check.Id, check.Name, "Aguardando…"));
        Findings.Clear();
        Results.Clear();
        Report = null;
        _completed = 0;
        ProgressValue = 0;

        var options = new DiagnosticOptions
        {
            RunSpeedTest = RunSpeedTest,
            ContractedDownloadMbps = contracted
        };

        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusText = "Iniciando diagnóstico…";

        try
        {
            var progress = new Progress<CheckProgress>(OnProgress);
            var report = await runner.RunAsync(options, progress, _cts.Token);

            foreach (var finding in report.Findings) Findings.Add(finding);
            foreach (var result in report.Results) Results.Add(result);
            Report = report;
            ProgressValue = 1;
            StatusText = $"Diagnóstico concluído em {(report.FinishedAt - report.StartedAt).TotalSeconds:0} segundos.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Diagnóstico cancelado.";
            foreach (var step in Steps.Where(s => s.State is StepState.Pending or StepState.Running))
            {
                step.State = StepState.Skipped;
                step.Summary = "Cancelado.";
            }
        }
        catch (Exception ex)
        {
            StatusText = "Erro inesperado: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void OnProgress(CheckProgress p)
    {
        var step = Steps.FirstOrDefault(s => s.Id == p.CheckId);
        if (step is null) return;

        if (p.State == CheckState.Running)
        {
            step.State = StepState.Running;
            step.Summary = p.Message ?? "Testando…";
            StatusText = $"{p.Name}: {p.Message ?? "testando…"}";
        }
        else if (p.State == CheckState.Done && p.Result is not null)
        {
            step.Complete(p.Result);
            _completed++;
            ProgressValue = (double)_completed / Steps.Count;
        }
    }

    private bool TryParseContracted(out double? value)
    {
        value = null;
        var text = ContractedSpeedText.Trim();
        if (text.Length == 0) return true;

        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
        {
            value = v;
            return true;
        }
        return false;
    }

    private void SaveReport()
    {
        if (Report is null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "Página HTML (*.html)|*.html",
            FileName = $"diagnostico-rede-{Report.StartedAt:yyyyMMdd-HHmm}.html"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, HtmlReportWriter.Render(Report), new UTF8Encoding(false));
            StatusText = "Relatório salvo.";
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Não foi possível salvar: " + ex.Message;
        }
    }

    private void CopySummary()
    {
        if (Report is null) return;
        try
        {
            Clipboard.SetText(Report.ToPlainText());
            StatusText = "Resumo copiado. Cole em um e-mail ou mensagem para o suporte.";
        }
        catch (Exception ex)
        {
            StatusText = "Não foi possível copiar: " + ex.Message;
        }
    }
}
