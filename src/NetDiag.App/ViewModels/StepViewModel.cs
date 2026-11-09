using NetDiag.Core.Model;

namespace NetDiag.App.ViewModels;

public enum StepState { Pending, Running, Ok, Warning, Error, Skipped }

/// <summary>Uma etapa na lista lateral ("Adaptador de rede", "Wi-Fi"…).</summary>
public sealed class StepViewModel : ObservableObject
{
    private StepState _state = StepState.Pending;
    private string _summary;

    public StepViewModel(string id, string name, string description)
    {
        Id = id;
        Name = name;
        _summary = description;
    }

    public string Id { get; }
    public string Name { get; }

    public StepState State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public string Summary
    {
        get => _summary;
        set => Set(ref _summary, value);
    }

    public void Complete(CheckResult result)
    {
        State = result.Status switch
        {
            CheckStatus.Ok => StepState.Ok,
            CheckStatus.Warning => StepState.Warning,
            CheckStatus.Error => StepState.Error,
            _ => StepState.Skipped
        };
        Summary = result.Summary;
    }
}
