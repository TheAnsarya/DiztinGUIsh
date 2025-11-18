using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Diz.Core.Interfaces
{
    /// <summary>
    /// Represents the connection status of the Mesen2 streaming client
    /// </summary>
    public enum Mesen2ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        HandshakeComplete,
        Error
    }

    /// <summary>
    /// Represents a CPU state snapshot received from Mesen2
    /// </summary>
    public struct Mesen2CpuState
    {
        public ushort A { get; set; }           // Accumulator
        public ushort X { get; set; }           // X register
        public ushort Y { get; set; }           // Y register
        public ushort S { get; set; }           // Stack pointer
        public ushort D { get; set; }           // Direct page
        public byte DB { get; set; }            // Data bank register
        public uint PC { get; set; }            // 24-bit program counter
        public byte P { get; set; }             // Processor status
        public bool EmulationMode { get; set; } // 6502 emulation mode
        public DateTime Timestamp { get; set; } // When this state was captured
    }

    /// <summary>
    /// Represents a memory dump received from Mesen2
    /// </summary>
    public class Mesen2MemoryDump
    {
        public byte MemoryType { get; set; }    // Memory region type
        public uint StartAddress { get; set; }  // Starting address
        public uint Length { get; set; }        // Data length
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime Timestamp { get; set; } // When this dump was captured
    }

    /// <summary>
    /// Represents a breakpoint operation for Mesen2
    /// </summary>
    public class Mesen2Breakpoint
    {
        public uint Address { get; set; }       // 24-bit SNES address
        public byte Type { get; set; }          // 0=Execute, 1=Read, 2=Write
        public bool Enabled { get; set; }      // Whether breakpoint is active
    }

    /// <summary>
    /// Represents an execution trace entry from Mesen2
    /// </summary>
    public class Mesen2TraceEntry
    {
        public uint PC { get; set; }            // Program counter
        public byte[] Instruction { get; set; } = Array.Empty<byte>(); // Instruction bytes
        public string Disassembly { get; set; } = string.Empty;        // Disassembled instruction
        public Mesen2CpuState CpuState { get; set; }                   // CPU state before execution
        public DateTime Timestamp { get; set; } // When this instruction was executed
    }

    /// <summary>
    /// Event arguments for Mesen2 connection status changes
    /// </summary>
    public class Mesen2ConnectionEventArgs : EventArgs
    {
        public Mesen2ConnectionStatus Status { get; set; }
        public string? Message { get; set; }
        public Exception? Error { get; set; }
    }

    /// <summary>
    /// Event arguments for CPU state updates
    /// </summary>
    public class Mesen2CpuStateEventArgs : EventArgs
    {
        public Mesen2CpuState CpuState { get; set; }
    }

    /// <summary>
    /// Event arguments for memory dump completion
    /// </summary>
    public class Mesen2MemoryDumpEventArgs : EventArgs
    {
        public Mesen2MemoryDump MemoryDump { get; set; } = new();
    }

    /// <summary>
    /// Event arguments for execution trace data
    /// </summary>
    public class Mesen2TraceEventArgs : EventArgs
    {
        public List<Mesen2TraceEntry> TraceEntries { get; set; } = new();
    }

    /// <summary>
    /// Interface for the Mesen2 streaming client
    /// </summary>
    public interface IMesen2StreamingClient
    {
        /// <summary>
        /// Current connection status
        /// </summary>
        Mesen2ConnectionStatus Status { get; }

        /// <summary>
        /// Whether the client is currently connected to Mesen2
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Server host address
        /// </summary>
        string Host { get; set; }

        /// <summary>
        /// Server port
        /// </summary>
        int Port { get; set; }

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        int TimeoutMs { get; set; }

        /// <summary>
        /// Last known CPU state
        /// </summary>
        Mesen2CpuState? LastCpuState { get; }

        /// <summary>
        /// Statistics: Messages sent
        /// </summary>
        long MessagesSent { get; }

        /// <summary>
        /// Statistics: Messages received
        /// </summary>
        long MessagesReceived { get; }

        /// <summary>
        /// Statistics: Bytes sent
        /// </summary>
        long BytesSent { get; }

        /// <summary>
        /// Statistics: Bytes received
        /// </summary>
        long BytesReceived { get; }

        // Events
        event EventHandler<Mesen2ConnectionEventArgs>? ConnectionStatusChanged;
        event EventHandler<Mesen2CpuStateEventArgs>? CpuStateReceived;
        event EventHandler<Mesen2MemoryDumpEventArgs>? MemoryDumpReceived;
        event EventHandler<Mesen2TraceEventArgs>? ExecutionTraceReceived;

        // Connection methods
        Task<bool> ConnectAsync();
        Task DisconnectAsync();

        // Data request methods
        Task<bool> RequestCpuStateAsync();
        Task<bool> RequestMemoryDumpAsync(byte memoryType, uint startAddress, uint length);
        Task<bool> SendHeartbeatAsync();

        // Breakpoint control methods
        Task<bool> AddBreakpointAsync(Mesen2Breakpoint breakpoint);
        Task<bool> RemoveBreakpointAsync(Mesen2Breakpoint breakpoint);
        Task<bool> ClearAllBreakpointsAsync();

        // Label management methods
        Task<bool> AddLabelAsync(uint address, string label, byte type);
        Task<bool> UpdateLabelAsync(uint address, string label, byte type);
        Task<bool> DeleteLabelAsync(uint address);
        Task<bool> SyncLabelsAsync();

        // Configuration methods
        Task<bool> SetStreamingConfigAsync(bool enableExecTrace, bool enableMemoryAccess, bool enableCdlUpdates, 
                                         int traceFrameInterval, int maxTracesPerFrame);
    }

    /// <summary>
    /// Interface for Mesen2 integration UI controllers
    /// </summary>
    public interface IMesen2IntegrationController
    {
        /// <summary>
        /// The streaming client instance
        /// </summary>
        IMesen2StreamingClient? Client { get; }

        /// <summary>
        /// The configuration instance
        /// </summary>
        IMesen2Configuration Configuration { get; }

        /// <summary>
        /// The streaming client (alias for Client for compatibility)
        /// </summary>
        IMesen2StreamingClient? StreamingClient { get; }

        /// <summary>
        /// Whether auto-connect is enabled
        /// </summary>
        bool AutoConnectEnabled { get; set; }

        /// <summary>
        /// Auto-connect retry interval in seconds
        /// </summary>
        int AutoConnectIntervalSeconds { get; set; }

        /// <summary>
        /// Initialize the controller
        /// </summary>
        void Initialize();

        /// <summary>
        /// Shutdown the controller and cleanup resources
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Attempt to connect to Mesen2
        /// </summary>
        Task<bool> ConnectToMesen2Async();

        /// <summary>
        /// Disconnect from Mesen2
        /// </summary>
        Task DisconnectFromMesen2Async();

        /// <summary>
        /// Show the Mesen2 connection dialog
        /// </summary>
        void ShowConnectionDialog();

        /// <summary>
        /// Show the Mesen2 status window
        /// </summary>
        void ShowStatusWindow();

        /// <summary>
        /// Show the execution trace viewer
        /// </summary>
        void ShowTraceViewer();

        /// <summary>
        /// Show the integration dashboard
        /// </summary>
        void ShowDashboard();

        /// <summary>
        /// Show the advanced configuration dialog
        /// </summary>
        void ShowAdvancedConfigurationDialog();
    }

    /// <summary>
    /// Configuration interface for Mesen2 streaming client
    /// </summary>
    public interface IMesen2Configuration
    {
        /// <summary>
        /// Default host for Mesen2 connections
        /// </summary>
        string DefaultHost { get; set; }

        /// <summary>
        /// Default port for Mesen2 connections
        /// </summary>
        int DefaultPort { get; set; }

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        int ConnectionTimeoutMs { get; set; }

        /// <summary>
        /// Whether to auto-reconnect on disconnection
        /// </summary>
        bool AutoReconnect { get; set; }

        /// <summary>
        /// Auto-reconnect delay in milliseconds
        /// </summary>
        int AutoReconnectDelayMs { get; set; }

        /// <summary>
        /// Maximum auto-reconnect attempts
        /// </summary>
        int MaxReconnectAttempts { get; set; }

        /// <summary>
        /// Enable verbose logging for Mesen2 integration
        /// </summary>
        bool VerboseLogging { get; set; }

        /// <summary>
        /// Save configuration to persistent storage
        /// </summary>
        void Save();

        /// <summary>
        /// Load configuration from persistent storage
        /// </summary>
        void Load();
    }

    /// <summary>
    /// Factory interface for creating Mesen2 streaming clients
    /// </summary>
    public interface IMesen2StreamingClientFactory
    {
        /// <summary>
        /// Create a new streaming client instance
        /// </summary>
        IMesen2StreamingClient CreateClient();

        /// <summary>
        /// Create a streaming client with specific configuration
        /// </summary>
        IMesen2StreamingClient CreateClient(IMesen2Configuration configuration);
    }
}