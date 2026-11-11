using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NWOS.DesktopShell.Ui
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. UI-Thread Exceptions abfangen (WPF-spezifisch)
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 2. Allgemeine AppDomain-Exceptions (Hintergrundthreads)
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // 3. Nicht abgefangene Task-Exceptions (async/await)
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"UI-Fehler aufgetreten:\n\n{e.Exception.Message}\n\nStacktrace:\n{e.Exception.StackTrace}",
                "NWOS Crash Guard", MessageBoxButton.OK, MessageBoxImage.Error);

            // Verhindert den sofortigen Programmabsturz
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Kritischer Systemfehler:\n\n{ex.Message}",
                    "NWOS Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            MessageBox.Show($"Async Task Fehler:\n\n{e.Exception.InnerException?.Message ?? e.Exception.Message}",
                "NWOS Task Error", MessageBoxButton.OK, MessageBoxImage.Warning);

            // Verhindert das Hochsteigen der Exception zum Prozessabbruch
            e.SetObserved();
        }
    }
}