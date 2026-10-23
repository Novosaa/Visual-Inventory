using System.Windows;
using System.Windows.Threading;
using VisualInventory.Services;

namespace VisualInventory;

public partial class App : Application
{
    public static ProjectPaths Paths { get; } = new(AppContext.BaseDirectory);

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUiException;
        base.OnStartup(e);
    }

    /// <summary>Shows what went wrong. Nothing is written anywhere; the app only reads.</summary>
    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Something went wrong. Visual Inventory only reads your files, so nothing was changed.\n\n" + e.Exception.Message,
            "Visual Inventory", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
