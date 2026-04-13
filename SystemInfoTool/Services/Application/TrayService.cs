using System.Windows;
using SystemInfoTool.Models;
using WinForms = System.Windows.Forms;

namespace SystemInfoTool.Services.Application;

/// <summary>
/// Manages the system tray icon, context menu, and window minimize/restore lifecycle.
/// Wraps <see cref="WinForms.NotifyIcon"/> and fires events consumed by the main ViewModel.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Window _window;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _contextMenu;

    private WindowPlacement? _savedPlacement;
    private bool _disposed;

    /// <summary>Raised when the user requests the main window to be restored (double-click or "Open").</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>Raised when the user selects "Exit" from the tray context menu.</summary>
    public event EventHandler? ExitRequested;

    public TrayService(System.Windows.Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));

        // Build context menu
        _contextMenu = new WinForms.ContextMenuStrip();

        var openItem = new WinForms.ToolStripMenuItem("Open");
        openItem.Click += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        // Build NotifyIcon
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "System Info Tool",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = _contextMenu,
            Visible = false
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hides the main window, removes it from the taskbar, and shows the tray icon.
    /// Records the current window placement so it can be restored later.
    /// </summary>
    public void MinimizeToTray()
    {
        // Record placement before hiding
        _savedPlacement = new WindowPlacement(
            _window.Left,
            _window.Top,
            _window.Width,
            _window.Height);

        _window.Hide();
        _window.ShowInTaskbar = false;
        _notifyIcon.Visible = true;
    }

    /// <summary>
    /// Shows the main window, restores it to the previously recorded size and position,
    /// and hides the tray icon.
    /// </summary>
    public void RestoreFromTray()
    {
        _notifyIcon.Visible = false;
        _window.ShowInTaskbar = true;

        if (_savedPlacement is { } placement)
        {
            _window.Left = placement.Left;
            _window.Top = placement.Top;
            _window.Width = placement.Width;
            _window.Height = placement.Height;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Gets the last recorded window placement, or <c>null</c> if the window has not yet been minimized.</summary>
    public WindowPlacement? SavedPlacement => _savedPlacement;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _contextMenu.Dispose();
        _notifyIcon.Dispose();
    }
}
