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

        // Show the advanced status window with real connection details
        try
        {
            var status = $"Mesen2 Connection Status\n\n" +
                        $"Connected: {_client.IsConnected}\n" +
                        $"Server: {_configuration.DefaultHost}:{_configuration.DefaultPort}\n" +
                        $"Timeout: {_configuration.ConnectionTimeoutMs}ms\n" +
                        $"Auto-Reconnect: {(_configuration.AutoReconnect ? "Enabled" : "Disabled")}\n" +
                        $"Connection Attempts: {_reconnectAttempts}\n\n" +
                        $"Status: {(_client.IsConnected ? "Active and streaming" : "Disconnected")}";
            
            _gui?.ShowMessage(status);
        }
        catch (Exception ex)
        {
            _gui?.ShowError($"Failed to open status window: {ex.Message}");
        }
    }    public void ShowTraceViewer()
    {
        if (_client == null)
        {
            _gui?.ShowError("Mesen2 client not initialized");
            return;
        }

        // Show the execution trace viewer information
        try
        {
            var message = "Execution Trace Viewer\n\n" +
                         "The trace viewer displays real-time CPU execution data from Mesen2.\n\n" +
                         "Features:\n" +
                         "• Live CPU register states (A, X, Y, PC, SP, P)\n" +
                         "• Instruction disassembly\n" +
                         "• Memory bank tracking (DB, DP)\n" +
                         "• M/X flag monitoring\n" +
                         "• Effective address calculation\n\n" +
                         $"Status: {(_client.IsConnected ? "Receiving trace data" : "Not connected")}\n\n" +
                         "To view traces, ensure:\n" +
                         "1. Mesen2 is running with a ROM loaded\n" +
                         "2. Connection is established\n" +
                         "3. DiztinGUIsh server is active in Mesen2";
            
            _gui?.ShowMessage(message);
        }
        catch (Exception ex)
        {
            _gui?.ShowError($"Failed to open trace viewer: {ex.Message}");
        }
    }    public void ShowDashboard()
    {
        // Show the integration dashboard
        try
        {
            var connectionStatus = _client?.IsConnected == true ? "✓ Connected" : "✗ Disconnected";
            var dashboard = $"Mesen2 Integration Dashboard\n\n" +
                           $"═══════════════════════════════════════\n" +
                           $"Connection Status: {connectionStatus}\n" +
                           $"Server: {_configuration.DefaultHost}:{_configuration.DefaultPort}\n" +
                           $"Timeout: {_configuration.ConnectionTimeoutMs}ms\n" +
                           $"═══════════════════════════════════════\n\n" +
                           $"Quick Actions:\n" +
                           $"• Use 'Connect to Mesen2' (Ctrl+F6) to establish connection\n" +
                           $"• Use 'Show Status Window' to view detailed statistics\n" +
                           $"• Use 'Show Trace Viewer' (Ctrl+F7) to inspect execution traces\n" +
                           $"• Use 'Configuration' to adjust connection settings\n\n" +
                           $"Import Options:\n" +
                           $"• File → Import → From Mesen2 (Live Stream)\n" +
                           $"• File → Import → From Mesen2 (Binary Files)\n\n" +
                           $"Current Configuration:\n" +
                           $"• Auto-Reconnect: {(_configuration.AutoReconnect ? "Enabled" : "Disabled")}\n" +
                           $"• Reconnect Delay: {_configuration.AutoReconnectDelayMs}ms\n" +
                           $"• Max Attempts: {_configuration.MaxReconnectAttempts}\n" +
                           $"• Connection Attempts: {_reconnectAttempts}";
            
            _gui?.ShowMessage(dashboard);
        }
        catch (Exception ex)
        {
            _gui?.ShowError($"Failed to open dashboard: {ex.Message}");
        }
    }    public void ShowAdvancedConfigurationDialog()
    {
        // Show the advanced configuration dialog
        try
        {
            var config = $"Advanced Mesen2 Configuration\n\n" +
                        $"═══════════════════════════════════════\n" +
                        $"Current Settings:\n" +
                        $"═══════════════════════════════════════\n" +
                        $"Host: {_configuration.DefaultHost}\n" +
                        $"Port: {_configuration.DefaultPort}\n" +
                        $"Connection Timeout: {_configuration.ConnectionTimeoutMs}ms\n" +
                        $"Auto-Reconnect: {(_configuration.AutoReconnect ? "Enabled" : "Disabled")}\n" +
                        $"Reconnect Delay: {_configuration.AutoReconnectDelayMs}ms\n" +
                        $"Max Reconnect Attempts: {_configuration.MaxReconnectAttempts}\n\n" +
                        $"Current Session:\n" +
                        $"• Connection Attempts: {_reconnectAttempts}\n" +
                        $"• Auto-Connect Enabled: {AutoConnectEnabled}\n" +
                        $"• Auto-Connect Interval: {AutoConnectIntervalSeconds}s\n\n" +
                        $"Note: To modify these settings, use the\n" +
                        $"Configuration dialog (Tools → Mesen2 Integration → Configuration)";
            
            _gui?.ShowMessage(config);
        }
        catch (Exception ex)
        {
            _gui?.ShowError($"Failed to open advanced configuration: {ex.Message}");
        }
    }        private void StartAutoConnectTimer()
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