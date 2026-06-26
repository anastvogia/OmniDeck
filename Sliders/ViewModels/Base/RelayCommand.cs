using System.Windows.Input;

namespace Sliders.ViewModels.Base;

/// <summary>
/// Lightweight ICommand that delegates to Action/Func delegates.
/// Integrates with WPF's CommandManager for automatic CanExecute re-evaluation.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is not null ? _ => canExecute() : null)
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Forces WPF to re-query CanExecute on all commands.</summary>
    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
