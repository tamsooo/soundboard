using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace SoundboardApp
{
    public partial class MainWindow : Window
    {
        private AudioManager audioManager;
        private string? selectedSoundFolder;
        private List<string> soundFiles = new List<string>();
        private readonly string[] supportedExtensions = { ".wav", ".mp3", ".m4a", ".wma", ".aac" };

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                Console.WriteLine("MainWindow initialized successfully");
                
                audioManager = new AudioManager(); // Initialize here
                Console.WriteLine("AudioManager created successfully");
                
                InitializeAudioManager();
                Console.WriteLine("AudioManager initialization completed");
                
                // Ensure window can receive keyboard events
                this.Focus();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MainWindow constructor: {ex.Message}");
                MessageBox.Show($"Failed to initialize application:\n{ex.Message}",
                              "Initialization Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        private void InitializeAudioManager()
        {
            try
            {
                audioManager.MicrophoneLevelChanged += OnMicrophoneLevelChanged;
                LoadOutputDevices(); // Load available output devices into the ComboBox
                
                // Check if VB-Cable is available
                var vbCableDevice = audioManager.OutputDevices.FirstOrDefault(d => 
                    d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
                
                if (vbCableDevice != null)
                {
                    UpdateStatus("Audio manager initialized successfully. VB-Cable detected! Select 'CABLE Input' as output device and start monitoring.");
                }
                else
                {
                    UpdateStatus("Audio manager initialized. VB-Cable not detected - install it for Discord integration.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to initialize audio manager: {ex.Message}");
                MessageBox.Show($"Failed to initialize audio system:\n{ex.Message}",
                              "Audio Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        private void LoadOutputDevices()
        {
            if (audioManager == null) return;

            try
            {
                OutputDeviceComboBox.Items.Clear();

                if (audioManager.OutputDevices.Count == 0)
                {
                    UpdateStatus("No output devices found. Check your audio drivers and try refreshing (F5).");
                    return;
                }

                foreach (var device in audioManager.OutputDevices)
                {
                    OutputDeviceComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = device.FriendlyName,
                        Tag = device
                    });
                }

                UpdateStatus($"Loaded {audioManager.OutputDevices.Count} output devices");

                // Debug: List all available devices
                foreach (var device in audioManager.OutputDevices)
                {
                    Console.WriteLine($"Available device: {device.FriendlyName}");
                }

                // Ensure ComboBox is enabled and interactive
                OutputDeviceComboBox.IsEnabled = true;
                OutputDeviceComboBox.IsEditable = false;
                OutputDeviceComboBox.IsReadOnly = true;

                // Select default device
                if (audioManager.SelectedOutputDevice != null)
                {
                    for (int i = 0; i < OutputDeviceComboBox.Items.Count; i++)
                    {
                        var item = (ComboBoxItem)OutputDeviceComboBox.Items[i];
                        var device = (MMDevice)item.Tag;
                        if (device.ID == audioManager.SelectedOutputDevice.ID)
                        {
                            OutputDeviceComboBox.SelectedIndex = i;
                            UpdateStatus($"Selected default device: {device.FriendlyName}");
                            break;
                        }
                    }
                }
                else if (OutputDeviceComboBox.Items.Count > 0)
                {
                    // If no default device is set, select the first available device
                    OutputDeviceComboBox.SelectedIndex = 0;
                    var firstItem = (ComboBoxItem)OutputDeviceComboBox.Items[0];
                    var firstDevice = (MMDevice)firstItem.Tag;
                    UpdateStatus($"Selected first available device: {firstDevice.FriendlyName}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to load output devices: {ex.Message}");
            }
        }

        private void RefreshOutputDevices()
        {
            if (audioManager == null) return;

            try
            {
                // Re-initialize the audio manager to refresh device list
                audioManager.Dispose();
                audioManager = new AudioManager();
                audioManager.MicrophoneLevelChanged += OnMicrophoneLevelChanged;
                LoadOutputDevices();
                UpdateStatus("Output devices refreshed");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to refresh output devices: {ex.Message}");
            }
        }

        private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshOutputDevices();
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Test button clicked - testing device loading...");
                UpdateStatus("Testing device loading...");
                
                // Test direct device enumeration
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active).ToList();
                
                Console.WriteLine($"Direct enumeration found {devices.Count} devices:");
                foreach (var device in devices)
                {
                    Console.WriteLine($"  - {device.FriendlyName}");
                }
                
                UpdateStatus($"Direct test found {devices.Count} devices. Check console for details.");
                
                // Also test the current audio manager
                if (audioManager != null)
                {
                    Console.WriteLine($"AudioManager has {audioManager.OutputDevices.Count} devices:");
                    foreach (var device in audioManager.OutputDevices)
                    {
                        Console.WriteLine($"  - {device.FriendlyName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed: {ex.Message}");
                UpdateStatus($"Test failed: {ex.Message}");
            }
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Sound Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                selectedSoundFolder = dialog.FolderName;
                FolderPathTextBox.Text = selectedSoundFolder;
                LoadSoundFiles();
            }
        }

        private void LoadSoundFiles()
        {
            if (string.IsNullOrEmpty(selectedSoundFolder) || !Directory.Exists(selectedSoundFolder))
            {
                UpdateStatus("Invalid sound folder selected");
                return;
            }

            try
            {
                soundFiles.Clear();
                SoundButtonsPanel.Children.Clear();

                // Get all supported audio files from the folder
                var files = Directory.GetFiles(selectedSoundFolder)
                    .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .OrderBy(file => Path.GetFileName(file))
                    .ToList();

                soundFiles.AddRange(files);

                // Create buttons for each sound file
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var button = new Button
                    {
                        Content = fileName,
                        Style = (Style)FindResource("SoundButtonStyle"),
                        Tag = file,
                        ToolTip = $"Play: {fileName}\nFile: {Path.GetFileName(file)}"
                    };

                    button.Click += SoundButton_Click;
                    SoundButtonsPanel.Children.Add(button);
                }

                UpdateSoundCount();
                UpdateStatus($"Loaded {files.Count} sound files from folder");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to load sound files: {ex.Message}");
                MessageBox.Show($"Failed to load sound files:\n{ex.Message}",
                              "File Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
            }
        }

        private void SoundButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filePath)
            {
                try
                {
                    audioManager.PlaySoundFile(filePath);

                    // Visual feedback
                    var originalContent = button.Content;
                    button.Content = "Playing...";
                    button.IsEnabled = false;

                    // Re-enable button after a short delay
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(500)
                    };
                    timer.Tick += (s, args) =>
                    {
                        button.Content = originalContent;
                        button.IsEnabled = true;
                        timer.Stop();
                        UpdateStatus($"Playing: {Path.GetFileNameWithoutExtension(filePath)}");
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Failed to play sound: {ex.Message}");
                }
            }
        }

        private void MonitorMicrophoneCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Update local monitoring setting
            if (audioManager != null)
            {
                try
                {
                    audioManager.SetLocalMonitoring(MonitorMicrophoneCheckBox.IsChecked == true);
                    UpdateStatus($"Local monitoring {(MonitorMicrophoneCheckBox.IsChecked == true ? "enabled" : "disabled")}");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Failed to update local monitoring: {ex.Message}");
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening link: {ex.Message}");
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                audioManager?.StartMicrophoneMonitoring(MonitorMicrophoneCheckBox.IsChecked == true);
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                UpdateStatus("Microphone monitoring started");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Audio Error\n\nFailed to start microphone monitoring:\n{ex.Message}",
                               "Audio Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Failed to start monitoring");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                audioManager?.StopMicrophoneMonitoring();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                MicrophoneLevelProgressBar.Value = 0;
                UpdateStatus("Microphone monitoring stopped");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to stop monitoring: {ex.Message}");
            }
        }

        private void StopSoundsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                audioManager?.StopAllSounds();
                UpdateStatus("All sounds stopped");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to stop sounds: {ex.Message}");
            }
        }

        private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatus($"Selection changed - SelectedItem: {OutputDeviceComboBox.SelectedItem}");
            
            if (OutputDeviceComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is MMDevice device &&
                audioManager != null)
            {
                try
                {
                    audioManager.SetOutputDevice(device);
                    UpdateStatus($"Output device changed to: {device.FriendlyName}");
                    
                    // Special message for VB-Cable
                    if (device.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateStatus($"VB-Cable selected! Now set Discord microphone to 'CABLE Output (VB-Audio Virtual Cable)'");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Failed to change output device: {ex.Message}");
                }
            }
            else
            {
                UpdateStatus("Selection changed but could not process device selection");
            }
        }

        private void OnMicrophoneLevelChanged(object? sender, float level)
        {
            // Update UI on the main thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MicrophoneLevelProgressBar.Value = Math.Min(level * 10, 1.0); // Amplify for visibility
            }));
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusTextBlock.Text = message;
            }));
        }

        private void UpdateSoundCount()
        {
            SoundCountTextBlock.Text = $"{soundFiles.Count} sounds loaded";
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                audioManager?.Dispose();
            }
            catch (Exception ex)
            {
                // Log but don't show error on close
                Console.WriteLine($"Error disposing audio manager: {ex.Message}");
            }
            base.OnClosed(e);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            this.Focus(); // Ensure keyboard focus when window is activated
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            Console.WriteLine($"Key pressed: {e.Key}");
            
            // Keyboard shortcuts for sound buttons
            if (e.Key >= System.Windows.Input.Key.F1 && e.Key <= System.Windows.Input.Key.F12)
            {
                int index = e.Key - System.Windows.Input.Key.F1;
                if (index < SoundButtonsPanel.Children.Count)
                {
                    var button = (Button)SoundButtonsPanel.Children[index];
                    // Simulate a click on the button to trigger the sound
                    SoundButton_Click(button, new RoutedEventArgs());
                }
            }
            
            // F5 to refresh output devices
            if (e.Key == System.Windows.Input.Key.F5)
            {
                Console.WriteLine("F5 pressed - refreshing devices");
                RefreshOutputDevices();
            }
            
            // ESC to stop all sounds
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Console.WriteLine("ESC pressed - stopping all sounds");
                UpdateStatus("ESC pressed - stopping all sounds");
                StopSoundsButton_Click(sender, e);
            }
        }
    }

    // Helper class for folder selection dialog
    public class OpenFolderDialog
    {
        public string Title { get; set; } = "Select Folder";
        public string FolderName { get; private set; } = string.Empty;

        public bool? ShowDialog()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = Title;
                dialog.UseDescriptionForTitle = true;

                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    FolderName = dialog.SelectedPath;
                    return true;
                }
                return false;
            }
        }
    }
}