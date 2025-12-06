using System;
using System.Threading;
using System.Windows;

namespace PulseHost
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Ensure only one instance runs at a time
            const string mutexName = "PulseHostSingleInstanceMutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                MessageBox.Show("Pulse Host is already running.", "Already Running", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
