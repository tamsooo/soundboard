using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

namespace SoundboardApp
{
    public class AudioManager : IDisposable
    {
        private WaveInEvent? microphoneInput;
        private WaveOutEvent? audioOutput;
        private WaveOutEvent? localAudioOutput; // For local monitoring
        private MixingSampleProvider? mixer;
        private BufferedWaveProvider? microphoneBuffer;
        private bool isRecording = false;
        private bool isPlaying = false;
        private bool _monitorMicrophoneLocally = false; // New flag for local mic monitoring

        public event EventHandler<float>? MicrophoneLevelChanged;
        public List<MMDevice> OutputDevices { get; private set; } = new List<MMDevice>();
        public MMDevice? SelectedOutputDevice { get; set; }

        public AudioManager()
        {
            InitializeOutputDevices();
        }

        private void InitializeOutputDevices()
        {
            try
            {
                Console.WriteLine("Initializing output devices...");
                var enumerator = new MMDeviceEnumerator();
                // Enumerate all active playback devices
                OutputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                Console.WriteLine($"Found {OutputDevices.Count} output devices");

                // Prioritize VB-Cable Output if available
                var vbCableOutput = OutputDevices.FirstOrDefault(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
                if (vbCableOutput != null)
                {
                    SelectedOutputDevice = vbCableOutput;
                    Console.WriteLine($"Selected VB-Cable device: {vbCableOutput.FriendlyName}");
                }
                else if (OutputDevices.Any())
                {
                    SelectedOutputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    Console.WriteLine($"Selected default device: {SelectedOutputDevice?.FriendlyName}");
                }
                else
                {
                    Console.WriteLine("No output devices found!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing output devices: {ex.Message}");
                throw;
            }
        }

        public void StartMicrophoneMonitoring(bool monitorLocally)
        {
            if (isRecording) return;

            _monitorMicrophoneLocally = monitorLocally;

            try
            {
                // Use default microphone device with higher quality settings
                microphoneInput = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(48000, 16, 1), // 48kHz, 16-bit, mono for better quality
                    BufferMilliseconds = 50 // Larger buffer for better stability
                };

                microphoneBuffer = new BufferedWaveProvider(microphoneInput.WaveFormat)
                {
                    BufferLength = 48000 * 2, // 2 seconds buffer at 48kHz
                    DiscardOnBufferOverflow = true
                };

                microphoneInput.DataAvailable += OnMicrophoneDataAvailable;
                microphoneInput.StartRecording();
                isRecording = true;

                Console.WriteLine("Microphone monitoring started successfully");
                InitializeAudioOutput();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start microphone monitoring: {ex.Message}");
                throw new InvalidOperationException($"Failed to start microphone monitoring: {ex.Message}", ex);
            }
        }

        public void StopMicrophoneMonitoring()
        {
            if (!isRecording) return;

            microphoneInput?.StopRecording();
            microphoneInput?.Dispose();
            microphoneInput = null;
            isRecording = false;

            StopAudioOutput();
        }

        private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (microphoneBuffer != null)
            {
                microphoneBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

                // Calculate microphone level for visualization
                float level = CalculateAudioLevel(e.Buffer, e.BytesRecorded);
                MicrophoneLevelChanged?.Invoke(this, level);
                
                // Debug: Log microphone activity (only occasionally to avoid spam)
                if (level > 0.01f) // Only log when there's significant audio
                {
                    Console.WriteLine($"Microphone level: {level:F3}");
                }
            }
        }

        private float CalculateAudioLevel(byte[] buffer, int bytesRecorded)
        {
            float sum = 0;
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                if (i + 1 < bytesRecorded)
                {
                    short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                    sum += Math.Abs(sample);
                }
            }
            return sum / (bytesRecorded / 2) / 32768f; // Normalize to 0-1
        }

        private void InitializeAudioOutput()
        {
            if (isPlaying) return;

            try
            {
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); // 48kHz, stereo, IEEE float for better quality
                mixer = new MixingSampleProvider(waveFormat);

                // ALWAYS add microphone input to mixer for VB-Cable routing
                // This ensures Discord gets both mic and sound effects
                if (microphoneBuffer != null)
                {
                    var micSampleProvider = microphoneBuffer.ToSampleProvider();
                    var stereoMic = new MonoToStereoSampleProvider(micSampleProvider);
                    mixer.AddMixerInput(stereoMic);
                }

                // Main audio output (to VB-Cable)
                audioOutput = new WaveOutEvent();

                // Set specific output device if selected
                if (SelectedOutputDevice != null)
                {
                    // Find the device number by matching device name
                    for (int i = 0; i < WaveOut.DeviceCount; i++)
                    {
                        var capabilities = WaveOut.GetCapabilities(i);
                        if (capabilities.ProductName.Contains(SelectedOutputDevice.FriendlyName, StringComparison.OrdinalIgnoreCase) ||
                            SelectedOutputDevice.FriendlyName.Contains(capabilities.ProductName, StringComparison.OrdinalIgnoreCase))
                        {
                            audioOutput.DeviceNumber = i;
                            break;
                        }
                    }
                }

                // Start the main audio output
                audioOutput.Init(mixer);
                audioOutput.Play();

                // Local monitoring output (to speakers/headphones)
                if (_monitorMicrophoneLocally)
                {
                    InitializeLocalMonitoring();
                }

                isPlaying = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize audio output: {ex.Message}", ex);
            }
        }

        private void InitializeLocalMonitoring()
        {
            try
            {
                if (localAudioOutput != null)
                {
                    localAudioOutput.Stop();
                    localAudioOutput.Dispose();
                }

                localAudioOutput = new WaveOutEvent();
                // Use default device for local monitoring (speakers/headphones)
                localAudioOutput.DeviceNumber = -1; // Default device
                localAudioOutput.Init(mixer);
                localAudioOutput.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize local monitoring: {ex.Message}");
            }
        }

        private void StopAudioOutput()
        {
            if (audioOutput != null)
            {
                audioOutput.Stop();
                audioOutput.Dispose();
                audioOutput = null;
            }
            
            if (localAudioOutput != null)
            {
                localAudioOutput.Stop();
                localAudioOutput.Dispose();
                localAudioOutput = null;
            }
            
            isPlaying = false;
            mixer = null;
        }

        public void PlaySoundFile(string filePath)
        {
            if (!File.Exists(filePath) || mixer == null) return;

            try
            {
                ISampleProvider sampleProvider;
                IDisposable? disposableResource = null;

                // Support different audio formats
                string extension = Path.GetExtension(filePath).ToLower();
                switch (extension)
                {
                    case ".mp3":
                        var mp3Reader = new Mp3FileReader(filePath);
                        sampleProvider = mp3Reader.ToSampleProvider();
                        disposableResource = mp3Reader;
                        break;
                    case ".wav":
                        var wavReader = new WaveFileReader(filePath);
                        sampleProvider = wavReader.ToSampleProvider();
                        disposableResource = wavReader;
                        break;
                    default:
                        var audioReader = new AudioFileReader(filePath);
                        sampleProvider = audioReader;
                        disposableResource = audioReader;
                        break;
                }

                // Convert to the mixer's sample rate and format with high quality resampling
                var resampler = new WdlResamplingSampleProvider(sampleProvider, mixer.WaveFormat.SampleRate);

                // Create a disposable sample provider that will clean up after playback
                var disposableSampleProvider = new DisposableSampleProvider(resampler, disposableResource, mixer);

                mixer.AddMixerInput(disposableSampleProvider);
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                Console.WriteLine($"Error playing sound file {filePath}: {ex.Message}");
            }
        }

        public void SetOutputDevice(MMDevice device)
        {
            SelectedOutputDevice = device;

            // Restart audio output with new device if currently recording
            if (isRecording)
            {
                StopAudioOutput();
                InitializeAudioOutput();
            }
        }

        public void SetLocalMonitoring(bool enabled)
        {
            _monitorMicrophoneLocally = enabled;
            
            if (isRecording)
            {
                if (enabled)
                {
                    // Enable local monitoring
                    InitializeLocalMonitoring();
                }
                else
                {
                    // Disable local monitoring
                    if (localAudioOutput != null)
                    {
                        localAudioOutput.Stop();
                        localAudioOutput.Dispose();
                        localAudioOutput = null;
                    }
                }
            }
        }

        public void StopAllSounds()
        {
            if (mixer != null)
            {
                try
                {
                    // Clear all mixer inputs except the microphone
                    var inputs = mixer.MixerInputs.ToList();
                    foreach (var input in inputs)
                    {
                        if (input != null && !(input is MonoToStereoSampleProvider))
                        {
                            mixer.RemoveMixerInput(input);
                        }
                    }
                    Console.WriteLine("All sounds stopped");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping sounds: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            StopMicrophoneMonitoring();
            StopAudioOutput();
            microphoneBuffer = null;
        }
    }

    // Helper class to automatically dispose audio file readers after playback
    public class DisposableSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider sampleProvider;
        private readonly IDisposable? disposableResource;
        private readonly MixingSampleProvider? mixer;
        private bool disposed = false;

        public DisposableSampleProvider(ISampleProvider sampleProvider, IDisposable? disposableResource, MixingSampleProvider? mixer)
        {
            this.sampleProvider = sampleProvider;
            this.disposableResource = disposableResource;
            this.mixer = mixer;
        }

        public WaveFormat WaveFormat => sampleProvider.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (disposed) return 0;

            int samplesRead = sampleProvider.Read(buffer, offset, count);

            // If no more samples, dispose the resource and remove from mixer
            if (samplesRead == 0 && !disposed)
            {
                disposed = true;
                disposableResource?.Dispose();
                
                // Remove this provider from the mixer to stop looping
                if (mixer != null)
                {
                    try
                    {
                        mixer.RemoveMixerInput(this);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error removing mixer input: {ex.Message}");
                    }
                }
            }

            return samplesRead;
        }
    }
}