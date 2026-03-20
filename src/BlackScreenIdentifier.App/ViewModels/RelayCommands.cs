using System.Windows.Input;

namespace BlackScreenIdentifier.App.ViewModels;

public sealed class AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null) : ICommand
{
    private bool isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isExecuting && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await executeAsync(parameter).ConfigureAwait(true);
        }
        finally
        {
            isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
