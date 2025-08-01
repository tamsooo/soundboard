using System;
using System.Windows;

namespace SoundboardApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            System.Diagnostics.Debug.WriteLine("Application starting...");
            Console.WriteLine("Application starting...");
            
            // Handle unhandled exceptions
            DispatcherUnhandledException += (sender, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.Exception.Message}");
                Console.WriteLine($"Unhandled exception: {args.Exception.Message}");
                MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}", 
                              "Error", 
                              MessageBoxButton.OK, 
                              MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}

