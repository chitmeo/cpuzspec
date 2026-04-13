using System.Windows.Input;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// A lightweight <see cref="ICommand"/> implementation that delegates execution
/// and can-execute logic to caller-supplied delegates.
/// No external MVVM framework is required.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Initialises a new <see cref="RelayCommand"/> with the given execute action
    /// and an optional can-execute predicate.
    /// </summary>
    /// <param name="execute">The action to invoke when the command is executed.</param>
    /// <param name="canExecute">
    /// Optional predicate that determines whether the command can execute.
    /// When <c>null</c> the command is always executable.
    /// </param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Manually raises <see cref="CanExecuteChanged"/> to force WPF to re-query
    /// <see cref="CanExecute"/>.
    /// </summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// A lightweight generic <see cref="ICommand"/> implementation that delegates
/// execution and can-execute logic to caller-supplied delegates that receive a
/// typed parameter.
/// </summary>
/// <typeparam name="T">The type of the command parameter.</typeparam>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    /// <summary>
    /// Initialises a new <see cref="RelayCommand{T}"/> with the given execute action
    /// and an optional can-execute predicate.
    /// </summary>
    /// <param name="execute">The action to invoke when the command is executed.</param>
    /// <param name="canExecute">
    /// Optional predicate that determines whether the command can execute.
    /// When <c>null</c> the command is always executable.
    /// </param>
    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        if (_canExecute is null)
            return true;

        return parameter switch
        {
            T typed => _canExecute(typed),
            null => _canExecute(default),
            _ => false
        };
    }

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        T? typed = parameter switch
        {
            T t => t,
            null => default,
            _ => throw new ArgumentException(
                $"Parameter must be of type {typeof(T).Name} or null.", nameof(parameter))
        };

        _execute(typed);
    }

    /// <summary>
    /// Manually raises <see cref="CanExecuteChanged"/> to force WPF to re-query
    /// <see cref="CanExecute"/>.
    /// </summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
