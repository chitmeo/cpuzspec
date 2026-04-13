using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// Base class for all ViewModels. Implements <see cref="INotifyPropertyChanged"/>
/// using <see cref="CallerMemberNameAttribute"/> so property setters do not need
/// to pass the property name as a string literal.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for the calling property.
    /// </summary>
    /// <param name="propertyName">
    /// Automatically supplied by the compiler via <see cref="CallerMemberNameAttribute"/>.
    /// </param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
    /// <see cref="PropertyChanged"/> only when the value actually changes.
    /// </summary>
    /// <typeparam name="T">Type of the backing field.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">New value to assign.</param>
    /// <param name="propertyName">
    /// Automatically supplied by the compiler via <see cref="CallerMemberNameAttribute"/>.
    /// </param>
    /// <returns><c>true</c> if the value changed; <c>false</c> otherwise.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
