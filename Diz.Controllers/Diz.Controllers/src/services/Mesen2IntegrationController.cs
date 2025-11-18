using System;
using System.Threading;
using System.Threading.Tasks;
using Diz.Core.Interfaces;
using Diz.Controllers.interfaces;

namespace Diz.Controllers.services
{
    /// <summary>
    /// Controller for managing Mesen2 integration and UI interactions
    /// </summary>
    public class Mesen2IntegrationController : IMesen2IntegrationController, IDisposable
    {
        private readonly IMesen2StreamingClientFactory _clientFactory;
        private readonly IMesen2Configuration _configuration;
        private readonly ICommonGui _gui;
        private IMesen2StreamingClient? _client;
        private Timer? _autoConnectTimer;
        private bool _disposed;
        private int _reconnectAttempts;

        public IMesen2StreamingClient? Client => _client;
        public IMesen2Configuration Configuration => _configuration;
        public IMesen2StreamingClient? StreamingClient => _client;

        public bool AutoConnectEnabled { get; set; }
        public int AutoConnectIntervalSeconds { get; set; } = 30;

        public Mesen2IntegrationController(
            IMesen2StreamingClientFactory clientFactory,
            IMesen2Configuration configuration,
            ICommonGui gui)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _gui = gui ?? throw new ArgumentNullException(nameof(gui));
        }

        public void Initialize()
        {
            if (_client != null)
                return;

            _client = _clientFactory.CreateClient();
            
            // Subscribe to events
            _client.ConnectionStatusChanged += OnConnectionStatusChanged;
            _client.CpuStateReceived += OnCpuStateReceived;
            _client.MemoryDumpReceived += OnMemoryDumpReceived;
            _client.ExecutionTraceReceived += OnExecutionTraceReceived;

            // Start auto-connect timer if enabled
            if (AutoConnectEnabled)
            {
                StartAutoConnectTimer();
            }
        }

        public void Shutdown()
        {
            StopAutoConnectTimer();
            
            if (_client != null)
            {
                // Unsubscribe from events
                _client.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _client.CpuStateReceived -= OnCpuStateReceived;
                _client.MemoryDumpReceived -= OnMemoryDumpReceived;
                _client.ExecutionTraceReceived -= OnExecutionTraceReceived;

                // Disconnect
                if (_client.IsConnected)
                {
                    _client.DisconnectAsync().Wait(2000); // Brief wait for graceful disconnect
                }
                
                // Dispose if client implements IDisposable
                if (_client is IDisposable disposableClient)
                {
                    disposableClient.Dispose();
                }
                _client = null;
            }
        }

        public async Task<bool> ConnectToMesen2Async()
        {
            if (_client?.IsConnected == true)
                return true;

            try
            {
                Initialize(); // Ensure client is created
                
                if (_client == null)
                    return false;

                var result = await _client.ConnectAsync().ConfigureAwait(false);
                if (result)
                {
                    _reconnectAttempts = 0; // Reset on successful connection
                }
                return result;
            }
            catch (Exception ex)
            {
                _gui?.ShowError($"Failed to connect to Mesen2: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectFromMesen2Async()
        {
            if (_client?.IsConnected == true)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
            StopAutoConnectTimer();
        }

        public void ShowConnectionDialog()
        {
            // Initialize client if needed
            if (_client == null)
            {
                Initialize();
            }

            // Show connection configuration dialog  
            try
            {
                // This would be implemented by the UI layer (WinForms, etc.)
                // The actual dialog creation should be handled by the UI composition root
                _gui?.ShowMessage("Connection configuration dialog would be shown here.");
            }
            catch (Exception ex)
            {
                _gui?.ShowError($"Failed to open connection configuration: {ex.Message}");
            }
        }

        public void ShowStatusWindow()
        {
            if (_client == null)
            {
                _gui?.ShowError("Mesen2 client not initialized");
                return;
            }

            // Show the advanced status window
            try
            {
                // This would be implemented by the UI layer (WinForms, etc.)
                _gui?.ShowMessage("Status window would be shown here.");
            }
            catch (Exception ex)
            {
                _gui?.ShowError($"Failed to open status window: {ex.Message}");
            }
        }

        public void ShowTraceViewer()
        {
            if (_client == null)
            {
                _gui?.ShowError("Mesen2 client not initialized");
                return;
            }

            // Show the execution trace viewer
            try
            {
                // This would be implemented by the UI layer (WinForms, etc.)
                _gui?.ShowMessage("Trace viewer would be shown here.");
            }
            catch (Exception ex)
            {
                _gui?.ShowError($"Failed to open trace viewer: {ex.Message}");
            }
        }

        public void ShowDashboard()
        {
            // Show the integration dashboard
            try
            {
                // This would be implemented by the UI layer (WinForms, etc.)
                _gui?.ShowMessage("Dashboard would be shown here.");
            }
            catch (Exception ex)
            {
                _gui?.ShowError($"Failed to open dashboard: {ex.Message}");
            }
        }

        public void ShowAdvancedConfigurationDialog()
        {
            // Show the advanced configuration dialog
            try
            {
                // This would be implemented by the UI layer (WinForms, etc.)
                _gui?.ShowMessage("Advanced configuration dialog would be shown here.");
            }
            catch (Exception ex)
            {
                _gui?.ShowError($"Failed to open advanced configuration: {ex.Message}");
            }
        }

        private void StartAutoConnectTimer()
        {
            if (_autoConnectTimer != null)
                return;

            var interval = TimeSpan.FromSeconds(AutoConnectIntervalSeconds);
            _autoConnectTimer = new Timer(AutoConnectTimerCallback, null, interval, interval);
        }

        private void StopAutoConnectTimer()
        {
            _autoConnectTimer?.Dispose();
            _autoConnectTimer = null;
        }

        private async void AutoConnectTimerCallback(object? state)
        {
            if (_client?.IsConnected != true && _configuration.AutoReconnect)
            {
                if (_reconnectAttempts < _configuration.MaxReconnectAttempts)
                {
                    _reconnectAttempts++;
                    await ConnectToMesen2Async().ConfigureAwait(false);
                }
                else
                {
                    StopAutoConnectTimer(); // Stop trying after max attempts
                }
            }
        }

        private void OnConnectionStatusChanged(object? sender, Mesen2ConnectionEventArgs e)
        {
            if (e.Status == Mesen2ConnectionStatus.Disconnected && _configuration.AutoReconnect)
            {
                // Restart auto-connect timer on disconnection
                Task.Run(async () =>
                {
                    await Task.Delay(_configuration.AutoReconnectDelayMs);
                    StartAutoConnectTimer();
                });
            }
        }

        private void OnCpuStateReceived(object? sender, Mesen2CpuStateEventArgs e)
        {
            // This would trigger updates to the DiztinGUIsh data model
            // Implementation depends on integration with ISnesData
            if (_configuration.VerboseLogging)
            {
                // Log CPU state updates if verbose logging is enabled
            }
        }

        private void OnMemoryDumpReceived(object? sender, Mesen2MemoryDumpEventArgs e)
        {
            // Handle memory dump data
            if (_configuration.VerboseLogging)
            {
                // Log memory dump reception
            }
        }

        private void OnExecutionTraceReceived(object? sender, Mesen2TraceEventArgs e)
        {
            // Handle execution trace data
            if (_configuration.VerboseLogging)
            {
                // Log trace data reception
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Shutdown();
            _disposed = true;
        }
    }
}