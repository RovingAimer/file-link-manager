using System.Windows.Input;

namespace FileLinkManager.App.Presentation.Commands;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Func<object?, bool> canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute ?? (_ => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute(parameter);

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> executeAsync;
    private readonly Func<object?, bool> canExecute;
    private bool isExecuting;

    public AsyncRelayCommand(
        Func<object?, Task> executeAsync,
        Func<object?, bool>? canExecute = null)
    {
        this.executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        this.canExecute = canExecute ?? (_ => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isExecuting && canExecute(parameter);

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await executeAsync(parameter);
        }
        finally
        {
            isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
