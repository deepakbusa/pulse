using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace PulseHost
{
    /// <summary>
    /// Configuration stored locally on the host machine
    /// </summary>
    public class HostConfig
    {
        public string? DeviceToken { get; set; }
        public string? DeviceName { get; set; }
        public bool StartOnLogin { get; set; }
        public string ServerUrl { get; set; } = "ws://localhost:3001/ws";
    }

    public partial class MainWindow : Window
    {
        private WebSocketClient? wsClient;
        private ScreenCaptureService? screenCapture;
        private InputSimulator? inputSimulator;
        private string? currentSessionId;
        private int frameCount = 0;
        private HostConfig config = new();
        private readonly string configPath;

        public MainWindow()
        {
            InitializeComponent();
            
            // Set config path in AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var pulseDir = Path.Combine(appDataPath, "PulseHost");
            Directory.CreateDirectory(pulseDir);
            configPath = Path.Combine(pulseDir, "config.json");

            LoadConfig();
            
            // Set device name default
            if (string.IsNullOrEmpty(config.DeviceName))
            {
                DeviceNameTextBox.Text = Environment.MachineName;
            }
            else
            {
                DeviceNameTextBox.Text = config.DeviceName;
            }

            // Set server URL default
            ServerUrlTextBox.Text = config.ServerUrl;

            // If we have a device token, auto-connect
            if (!string.IsNullOrEmpty(config.DeviceToken))
            {
                Task.Run(async () => 
                {
                    await Task.Delay(1000); // Small delay to let UI initialize
                    Dispatcher.Invoke(() => AutoConnect());
                });
            }

            // Set startup checkbox
            StartOnLoginCheckBox.IsChecked = config.StartOnLogin;
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    config = JsonConvert.DeserializeObject<HostConfig>(json) ?? new HostConfig();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load config: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save config: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void AutoConnect()
        {
            try
            {
                StatusText.Text = "Connecting to server...";
                ShowConnectedPanel();
                await ConnectToServer(config.DeviceToken!, config.DeviceName!);
                StatusText.Text = "Connected! Waiting for remote session...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Auto-connect failed: {ex.Message}\n\nPlease check server URL and try pairing again.", 
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowPairingPanel();
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var deviceName = DeviceNameTextBox.Text.Trim();
            var pairingCode = PairingCodeTextBox.Text.Trim();
            var serverUrl = ServerUrlTextBox.Text.Trim();

            if (string.IsNullOrEmpty(deviceName))
            {
                MessageBox.Show("Please enter a device name.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(pairingCode) || pairingCode.Length != 6)
            {
                MessageBox.Show("Please enter a valid 6-digit pairing code.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(serverUrl) || !serverUrl.StartsWith("ws://") && !serverUrl.StartsWith("wss://"))
            {
                MessageBox.Show("Please enter a valid WebSocket URL (ws:// or wss://)", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save server URL to config
            config.ServerUrl = serverUrl;
            SaveConfig();

            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "Connecting...";

            try
            {
                await PairDevice(pairingCode, deviceName);
                ShowConnectedPanel();
                StatusText.Text = "Connected! Waiting for remote session...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}\n\nPlease check your server URL and pairing code.", "Connection Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Connect";
            }
        }

        private async System.Threading.Tasks.Task PairDevice(string pairingCode, string deviceName)
        {
            wsClient = new WebSocketClient(config.ServerUrl);
            
            // Set up event handlers
            wsClient.OnPaired += (deviceId, deviceToken) =>
            {
                Dispatcher.Invoke(() =>
                {
                    config.DeviceToken = deviceToken;
                    config.DeviceName = deviceName;
                    SaveConfig();
                });
            };

            SetupWebSocketHandlers();

            await wsClient.Connect();
            await wsClient.SendPairingRequest(pairingCode, deviceName, GetOSInfo());
        }

        private async System.Threading.Tasks.Task ConnectToServer(string deviceToken, string deviceName)
        {
            wsClient = new WebSocketClient(config.ServerUrl);
            SetupWebSocketHandlers();
            await wsClient.Connect();
            await wsClient.SendAuthentication(deviceToken);
        }

        private void SetupWebSocketHandlers()
        {
            if (wsClient == null) return;

            wsClient.OnSessionRequest += (sessionId, userName) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Auto-accept all remote access requests
                    wsClient.SendSessionResponse(sessionId, true);
                    StartSession(sessionId);
                    
                    // Show notification that session started
                    StatusText.Text = $"Remote session with {userName} started";
                });
            };

            wsClient.OnSessionEnded += (sessionId) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StopSession();
                });
            };

            wsClient.OnInputEvent += (sessionId, inputType, data) =>
            {
                if (sessionId != currentSessionId || inputSimulator == null) return;
                inputSimulator.ProcessInput(inputType, data);
            };

            wsClient.OnDisconnected += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    Console.WriteLine($"\n🔴 HOST DISCONNECTION EVENT TRIGGERED");
                    Console.WriteLine($"   Current Session: {currentSessionId ?? "None"}");
                    Console.WriteLine($"   Frame Count: {frameCount}");
                    Console.WriteLine($"   Screen Capture Running: {screenCapture != null}");
                    Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}\n");
                    
                    // Stop session cleanly
                    if (currentSessionId != null)
                    {
                        Console.WriteLine($"⚠️ Stopping active session due to disconnection");
                        StopSession();
                    }
                    
                    MessageBox.Show($"Disconnected from server.\n\nServer: {config.ServerUrl}\n\nCheck console for detailed error information.\n\nPlease check your internet connection and server status.", "Connection Lost", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ShowPairingPanel();
                });
            };
        }

        private void StartSession(string sessionId)
        {
            Console.WriteLine($"\n✅ STARTING SESSION: {sessionId}");
            Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}\n");
            
            currentSessionId = sessionId;
            frameCount = 0;

            SessionStatusText.Text = "Active";
            StatusText.Text = "Remote session in progress...";

            // Initialize screen capture
            screenCapture = new ScreenCaptureService();
            screenCapture.OnFrameCaptured += (imageData) =>
            {
                if (wsClient != null && currentSessionId != null)
                {
                    frameCount++;
                    
                    try
                    {
                        wsClient.SendFrame(currentSessionId, frameCount, imageData);
                        
                        Dispatcher.Invoke(() =>
                        {
                            FrameCountText.Text = frameCount.ToString();
                        });
                        
                        // Log every 100 frames
                        if (frameCount % 100 == 0)
                        {
                            Console.WriteLine($"📤 Sent frame {frameCount} successfully");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\n❌ FRAME SEND ERROR at frame {frameCount}:");
                        Console.WriteLine($"   Type: {ex.GetType().Name}");
                        Console.WriteLine($"   Message: {ex.Message}");
                        Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}\n");
                    }
                }
            };

            screenCapture.Start();

            // Initialize input simulator
            inputSimulator = new InputSimulator();
        }

        private void StopSession()
        {
            Console.WriteLine($"\n🛑 STOPPING SESSION");
            Console.WriteLine($"   Session ID: {currentSessionId ?? "None"}");
            Console.WriteLine($"   Total Frames Sent: {frameCount}");
            Console.WriteLine($"   Time: {DateTime.Now:HH:mm:ss.fff}\n");
            
            screenCapture?.Stop();
            screenCapture = null;
            inputSimulator = null;
            currentSessionId = null;

            SessionStatusText.Text = "Idle";
            StatusText.Text = "Waiting for remote session...";
        }

        private void StopSharingButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentSessionId != null)
            {
                wsClient?.SendEndSession(currentSessionId);
                StopSession();
            }

            wsClient?.Disconnect();
            ShowPairingPanel();
        }

        private void ShowPairingPanel()
        {
            PairingPanel.Visibility = Visibility.Visible;
            ConnectedPanel.Visibility = Visibility.Collapsed;
            ConnectButton.IsEnabled = true;
            ConnectButton.Content = "Connect";
        }

        private void ShowConnectedPanel()
        {
            PairingPanel.Visibility = Visibility.Collapsed;
            ConnectedPanel.Visibility = Visibility.Visible;
        }

        private string GetOSInfo()
        {
            return $"Windows {Environment.OSVersion.Version}";
        }

        private void StartOnLoginCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SetStartupShortcut(true);
            config.StartOnLogin = true;
            SaveConfig();
        }

        private void StartOnLoginCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SetStartupShortcut(false);
            config.StartOnLogin = false;
            SaveConfig();
        }

        private void SetStartupShortcut(bool enable)
        {
            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupFolder, "PulseHost.lnk");

                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null)
                    {
                        CreateShortcut(shortcutPath, exePath);
                    }
                }
                else
                {
                    if (File.Exists(shortcutPath))
                    {
                        File.Delete(shortcutPath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update startup setting: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CreateShortcut(string shortcutPath, string targetPath)
        {
            // Use IWshRuntimeLibrary for creating shortcuts
            // This requires adding COM reference to "Windows Script Host Object Model"
            // For simplicity in this demo, we'll use a basic approach
            
            var shell = Type.GetTypeFromProgID("WScript.Shell");
            if (shell != null)
            {
                dynamic? shellInstance = Activator.CreateInstance(shell);
                if (shellInstance != null)
                {
                    var shortcut = shellInstance.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                    shortcut.Description = "Pulse Host - Remote Desktop";
                    shortcut.Save();
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            screenCapture?.Stop();
            wsClient?.Disconnect();
            base.OnClosed(e);
        }
    }
}
