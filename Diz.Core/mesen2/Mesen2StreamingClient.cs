using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Diz.Core.Interfaces;

namespace Diz.Core.Mesen2
{
    /// <summary>
    /// Mesen2 streaming protocol message types
    /// Matches the protocol defined in DiztinguishProtocol.h
    /// </summary>
    internal enum Mesen2MessageType : byte
    {
        Handshake = 0,
        HandshakeAck = 1,
        ConfigStream = 2,
        ExecTrace = 3,
        CdlUpdate = 4,
        MemoryAccess = 5,
        CpuState = 6,
        CpuStateRequest = 7,
        LabelAdd = 8,
        LabelUpdate = 9,
        LabelDelete = 10,
        LabelSyncRequest = 11,
        LabelSyncResponse = 12,
        BreakpointAdd = 13,
        BreakpointRemove = 14,
        MemoryDumpRequest = 15,
        MemoryDumpResponse = 16,
        Heartbeat = 17,
        Disconnect = 18
    }

    /// <summary>
    /// Core implementation of the Mesen2 streaming client
    /// Handles the binary protocol communication with Mesen2's DiztinGUIsh server
    /// </summary>
    public class Mesen2StreamingClient : IMesen2StreamingClient, IDisposable
    {
        private readonly object _lockObject = new();
        private readonly IMesen2Configuration _configuration;
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
        
        private bool _disposed;
        private Mesen2ConnectionStatus _status = Mesen2ConnectionStatus.Disconnected;
        private Mesen2CpuState? _lastCpuState;
        
        // Statistics
        private long _messagesSent;
        private long _messagesReceived;
        private long _bytesSent;
        private long _bytesReceived;

        /// <summary>
        /// Constructor for dependency injection
        /// </summary>
        public Mesen2StreamingClient(IMesen2Configuration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Parameterless constructor for backward compatibility
        /// </summary>
        public Mesen2StreamingClient() : this(new Mesen2Configuration())
        {
        }

        // Configuration properties (now delegated to injected configuration)
        public string Host
        {
            get => _configuration.DefaultHost;
            set => _configuration.DefaultHost = value;
        }

        public int Port
        {
            get => _configuration.DefaultPort;
            set => _configuration.DefaultPort = value;
        }

        public int TimeoutMs
        {
            get => _configuration.ConnectionTimeoutMs;
            set => _configuration.ConnectionTimeoutMs = value;
        }

        // Properties
        public Mesen2ConnectionStatus Status
        {
            get { lock (_lockObject) { return _status; } }
            private set
            {
                lock (_lockObject)
                {
                    if (_status != value)
                    {
                        _status = value;
                        ConnectionStatusChanged?.Invoke(this, new Mesen2ConnectionEventArgs { Status = value });
                    }
                }
            }
        }

        public bool IsConnected => Status == Mesen2ConnectionStatus.Connected || Status == Mesen2ConnectionStatus.HandshakeComplete;

        public Mesen2CpuState? LastCpuState
        {
            get { lock (_lockObject) { return _lastCpuState; } }
            private set { lock (_lockObject) { _lastCpuState = value; } }
        }

        public long MessagesSent => Interlocked.Read(ref _messagesSent);
        public long MessagesReceived => Interlocked.Read(ref _messagesReceived);
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);

        // Events
        public event EventHandler<Mesen2ConnectionEventArgs>? ConnectionStatusChanged;
        public event EventHandler<Mesen2CpuStateEventArgs>? CpuStateReceived;
        public event EventHandler<Mesen2MemoryDumpEventArgs>? MemoryDumpReceived;
        public event EventHandler<Mesen2TraceEventArgs>? ExecutionTraceReceived;

        public async Task<bool> ConnectAsync()
        {
            if (IsConnected)
                return true;

            try
            {
                Status = Mesen2ConnectionStatus.Connecting;

                _tcpClient = new TcpClient();
                _tcpClient.ReceiveTimeout = TimeoutMs;
                _tcpClient.SendTimeout = TimeoutMs;

                await _tcpClient.ConnectAsync(Host, Port).ConfigureAwait(false);
                _networkStream = _tcpClient.GetStream();

                Status = Mesen2ConnectionStatus.Connected;

                // Start receive loop
                _cancellationTokenSource = new CancellationTokenSource();
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));

                // Perform handshake
                if (await PerformHandshakeAsync().ConfigureAwait(false))
                {
                    Status = Mesen2ConnectionStatus.HandshakeComplete;
                    return true;
                }
                else
                {
                    await DisconnectAsync().ConfigureAwait(false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Status = Mesen2ConnectionStatus.Error;
                ConnectionStatusChanged?.Invoke(this, new Mesen2ConnectionEventArgs 
                { 
                    Status = Status, 
                    Error = ex, 
                    Message = $"Connection failed: {ex.Message}" 
                });
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                // Send disconnect message if connected
                if (IsConnected)
                {
                    await SendMessageAsync(Mesen2MessageType.Disconnect, Array.Empty<byte>()).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore errors during disconnect
            }

            _cancellationTokenSource?.Cancel();

            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore cancellation exceptions
                }
            }

            _networkStream?.Close();
            _tcpClient?.Close();

            _networkStream = null;
            _tcpClient = null;
            _cancellationTokenSource = null;
            _receiveTask = null;

            Status = Mesen2ConnectionStatus.Disconnected;
        }

        private async Task<bool> PerformHandshakeAsync()
        {
            try
            {
                // Send handshake message with version 1.0
                var handshakePayload = new byte[4];
                BitConverter.GetBytes((ushort)1).CopyTo(handshakePayload, 0); // Major version
                BitConverter.GetBytes((ushort)0).CopyTo(handshakePayload, 2); // Minor version

                return await SendMessageAsync(Mesen2MessageType.Handshake, handshakePayload).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendMessageAsync(Mesen2MessageType messageType, byte[] payload)
        {
            if (_networkStream == null || !IsConnected)
                return false;

            try
            {
                // Message format: [Type:1][Length:4][Payload:Length]
                var header = new byte[5];
                header[0] = (byte)messageType;
                BitConverter.GetBytes(payload.Length).CopyTo(header, 1);

                await _networkStream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                
                if (payload.Length > 0)
                {
                    await _networkStream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                }

                await _networkStream.FlushAsync().ConfigureAwait(false);

                Interlocked.Increment(ref _messagesSent);
                Interlocked.Add(ref _bytesSent, header.Length + payload.Length);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];

            try
            {
                while (!cancellationToken.IsCancellationRequested && _networkStream != null)
                {
                    // Read message header (5 bytes)
                    if (!await ReadExactAsync(buffer, 0, 5, cancellationToken).ConfigureAwait(false))
                        break;

                    var messageType = (Mesen2MessageType)buffer[0];
                    var payloadLength = BitConverter.ToInt32(buffer, 1);

                    // Validate payload length
                    if (payloadLength < 0 || payloadLength > 1024 * 1024) // Max 1MB
                    {
                        // Invalid message size, disconnect
                        break;
                    }

                    // Read payload
                    byte[] payload = new byte[payloadLength];
                    if (payloadLength > 0)
                    {
                        if (!await ReadExactAsync(payload, 0, payloadLength, cancellationToken).ConfigureAwait(false))
                            break;
                    }

                    Interlocked.Increment(ref _messagesReceived);
                    Interlocked.Add(ref _bytesReceived, 5 + payloadLength);

                    // Process message
                    await ProcessReceivedMessageAsync(messageType, payload).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Connection lost or error occurred
            }

            // Connection ended
            await DisconnectAsync().ConfigureAwait(false);
        }

        private async Task<bool> ReadExactAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_networkStream == null)
                return false;

            int totalRead = 0;
            while (totalRead < count && !cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await _networkStream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    return false; // Connection closed

                totalRead += bytesRead;
            }

            return totalRead == count;
        }

        private async Task ProcessReceivedMessageAsync(Mesen2MessageType messageType, byte[] payload)
        {
            try
            {
                switch (messageType)
                {
                    case Mesen2MessageType.HandshakeAck:
                        // Handshake acknowledgment received
                        break;

                    case Mesen2MessageType.CpuState:
                        if (payload.Length >= 17)
                        {
                            var cpuState = ParseCpuState(payload);
                            LastCpuState = cpuState;
                            CpuStateReceived?.Invoke(this, new Mesen2CpuStateEventArgs { CpuState = cpuState });
                        }
                        break;

                    case Mesen2MessageType.MemoryDumpResponse:
                        if (payload.Length >= 9)
                        {
                            var memoryDump = ParseMemoryDump(payload);
                            MemoryDumpReceived?.Invoke(this, new Mesen2MemoryDumpEventArgs { MemoryDump = memoryDump });
                        }
                        break;

                    case Mesen2MessageType.ExecTrace:
                        var traceEntries = ParseExecutionTrace(payload);
                        if (traceEntries.Count > 0)
                        {
                            ExecutionTraceReceived?.Invoke(this, new Mesen2TraceEventArgs { TraceEntries = traceEntries });
                        }
                        break;

                    default:
                        // Unknown or unhandled message type
                        break;
                }
            }
            catch
            {
                // Error processing message, ignore and continue
            }

            await Task.CompletedTask;
        }

        private static Mesen2CpuState ParseCpuState(byte[] payload)
        {
            using var reader = new BinaryReader(new MemoryStream(payload));
            
            return new Mesen2CpuState
            {
                A = reader.ReadUInt16(),
                X = reader.ReadUInt16(),
                Y = reader.ReadUInt16(),
                S = reader.ReadUInt16(),
                D = reader.ReadUInt16(),
                DB = reader.ReadByte(),
                PC = reader.ReadUInt32(),
                P = reader.ReadByte(),
                EmulationMode = reader.ReadBoolean(),
                Timestamp = DateTime.UtcNow
            };
        }

        private static Mesen2MemoryDump ParseMemoryDump(byte[] payload)
        {
            using var reader = new BinaryReader(new MemoryStream(payload));
            
            var dump = new Mesen2MemoryDump
            {
                MemoryType = reader.ReadByte(),
                StartAddress = reader.ReadUInt32(),
                Length = reader.ReadUInt32(),
                Timestamp = DateTime.UtcNow
            };

            // Read remaining bytes as data
            var remainingBytes = payload.Length - 9;
            if (remainingBytes > 0)
            {
                dump.Data = reader.ReadBytes(remainingBytes);
            }

            return dump;
        }

        private static List<Mesen2TraceEntry> ParseExecutionTrace(byte[] payload)
        {
            var entries = new List<Mesen2TraceEntry>();
            
            try
            {
                using var stream = new MemoryStream(payload);
                using var reader = new BinaryReader(stream);
                
                while (stream.Position < stream.Length)
                {
                    var pc = ReadUInt24(reader); // 24-bit PC
                    var opcode = reader.ReadByte();
                    var operandLow = reader.ReadByte();
                    var operandHigh = reader.ReadByte();
                    var accumulator = reader.ReadUInt16();
                    var xRegister = reader.ReadUInt16();
                    var yRegister = reader.ReadUInt16();
                    var stackPointer = reader.ReadUInt16();
                    var directPage = reader.ReadUInt16();
                    var dataBank = reader.ReadByte();
                    var processorStatus = reader.ReadByte();
                    var emulationMode = reader.ReadBoolean();
                    var cycleCount = reader.ReadUInt64();

                    // Create instruction bytes array
                    var instructionBytes = operandHigh != 0 
                        ? new byte[] { opcode, operandLow, operandHigh }
                        : operandLow != 0 
                            ? new byte[] { opcode, operandLow }
                            : new byte[] { opcode };

                    // Create CPU state
                    var cpuState = new Mesen2CpuState
                    {
                        A = accumulator,
                        X = xRegister,
                        Y = yRegister,
                        S = stackPointer,
                        D = directPage,
                        DB = dataBank,
                        PC = pc,
                        P = processorStatus,
                        EmulationMode = emulationMode,
                        Timestamp = DateTime.UtcNow
                    };

                    // Create trace entry with proper structure
                    var entry = new Mesen2TraceEntry
                    {
                        PC = pc,
                        Instruction = instructionBytes,
                        Disassembly = $"${pc:X6}: {opcode:X2}", // Basic disassembly, could be enhanced
                        CpuState = cpuState,
                        Timestamp = DateTime.UtcNow
                    };
                    
                    entries.Add(entry);
                }
            }
            catch (Exception)
            {
                // Return partial entries if parsing fails
            }
            
            return entries;
        }

        public async Task<bool> RequestCpuStateAsync()
        {
            return await SendMessageAsync(Mesen2MessageType.CpuStateRequest, Array.Empty<byte>()).ConfigureAwait(false);
        }

        public async Task<bool> RequestMemoryDumpAsync(byte memoryType, uint startAddress, uint length)
        {
            var payload = new byte[9];
            payload[0] = memoryType;
            BitConverter.GetBytes(startAddress).CopyTo(payload, 1);
            BitConverter.GetBytes(length).CopyTo(payload, 5);

            return await SendMessageAsync(Mesen2MessageType.MemoryDumpRequest, payload).ConfigureAwait(false);
        }

        public async Task<bool> SendHeartbeatAsync()
        {
            return await SendMessageAsync(Mesen2MessageType.Heartbeat, Array.Empty<byte>()).ConfigureAwait(false);
        }

        public async Task<bool> AddBreakpointAsync(Mesen2Breakpoint breakpoint)
        {
            var payload = new byte[6];
            BitConverter.GetBytes(breakpoint.Address).CopyTo(payload, 0);
            payload[4] = breakpoint.Type;
            payload[5] = breakpoint.Enabled ? (byte)1 : (byte)0;

            return await SendMessageAsync(Mesen2MessageType.BreakpointAdd, payload).ConfigureAwait(false);
        }

        public async Task<bool> RemoveBreakpointAsync(Mesen2Breakpoint breakpoint)
        {
            var payload = new byte[6];
            BitConverter.GetBytes(breakpoint.Address).CopyTo(payload, 0);
            payload[4] = breakpoint.Type;
            payload[5] = (byte)0; // Disabled for removal

            return await SendMessageAsync(Mesen2MessageType.BreakpointRemove, payload).ConfigureAwait(false);
        }

        public async Task<bool> ClearAllBreakpointsAsync()
        {
            try
            {
                // Send bulk clear command - use special address 0xFFFFFFFF to indicate "clear all"
                var payload = new byte[5];
                payload[0] = 0xFF; // Special "clear all" marker
                payload[1] = 0xFF;
                payload[2] = 0xFF;
                payload[3] = 0xFF;
                payload[4] = 0x07; // All breakpoint types (Execute | Read | Write)
                
                return await SendMessageAsync(Mesen2MessageType.BreakpointRemove, payload).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> AddLabelAsync(uint address, string label, byte type)
        {
            try
            {
                var labelBytes = System.Text.Encoding.UTF8.GetBytes(label ?? string.Empty);
                var payload = new byte[8 + labelBytes.Length];
                
                // Write address (24-bit, little-endian)
                payload[0] = (byte)(address & 0xFF);
                payload[1] = (byte)((address >> 8) & 0xFF);
                payload[2] = (byte)((address >> 16) & 0xFF);
                
                // Write label type
                payload[3] = type;
                
                // Write label length (32-bit little-endian)
                var length = (uint)labelBytes.Length;
                payload[4] = (byte)(length & 0xFF);
                payload[5] = (byte)((length >> 8) & 0xFF);
                payload[6] = (byte)((length >> 16) & 0xFF);
                payload[7] = (byte)((length >> 24) & 0xFF);
                
                // Write label text
                Array.Copy(labelBytes, 0, payload, 8, labelBytes.Length);
                
                return await SendMessageAsync(Mesen2MessageType.LabelAdd, payload).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> UpdateLabelAsync(uint address, string label, byte type)
        {
            try
            {
                var labelBytes = System.Text.Encoding.UTF8.GetBytes(label ?? string.Empty);
                var payload = new byte[8 + labelBytes.Length];
                
                // Write address (24-bit, little-endian)
                payload[0] = (byte)(address & 0xFF);
                payload[1] = (byte)((address >> 8) & 0xFF);
                payload[2] = (byte)((address >> 16) & 0xFF);
                
                // Write label type
                payload[3] = type;
                
                // Write label length (32-bit little-endian)
                var length = (uint)labelBytes.Length;
                payload[4] = (byte)(length & 0xFF);
                payload[5] = (byte)((length >> 8) & 0xFF);
                payload[6] = (byte)((length >> 16) & 0xFF);
                payload[7] = (byte)((length >> 24) & 0xFF);
                
                // Write label text
                Array.Copy(labelBytes, 0, payload, 8, labelBytes.Length);
                
                return await SendMessageAsync(Mesen2MessageType.LabelUpdate, payload).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> DeleteLabelAsync(uint address)
        {
            try
            {
                var payload = new byte[3];
                
                // Write address (24-bit, little-endian)
                payload[0] = (byte)(address & 0xFF);
                payload[1] = (byte)((address >> 8) & 0xFF);
                payload[2] = (byte)((address >> 16) & 0xFF);
                
                return await SendMessageAsync(Mesen2MessageType.LabelDelete, payload).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> SyncLabelsAsync()
        {
            return await SendMessageAsync(Mesen2MessageType.LabelSyncRequest, Array.Empty<byte>()).ConfigureAwait(false);
        }

        public async Task<bool> SetStreamingConfigAsync(bool enableExecTrace, bool enableMemoryAccess, bool enableCdlUpdates, 
                                                       int traceFrameInterval, int maxTracesPerFrame)
        {
            var payload = new byte[10];
            payload[0] = enableExecTrace ? (byte)1 : (byte)0;
            payload[1] = enableMemoryAccess ? (byte)1 : (byte)0;
            payload[2] = enableCdlUpdates ? (byte)1 : (byte)0;
            BitConverter.GetBytes(traceFrameInterval).CopyTo(payload, 3);
            BitConverter.GetBytes(maxTracesPerFrame).CopyTo(payload, 7);

            return await SendMessageAsync(Mesen2MessageType.ConfigStream, payload).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a 24-bit unsigned integer from a binary reader (little-endian)
        /// </summary>
        private static uint ReadUInt24(BinaryReader reader)
        {
            var low = reader.ReadByte();
            var mid = reader.ReadByte();
            var high = reader.ReadByte();
            return (uint)(low | (mid << 8) | (high << 16));
        }

        /// <summary>
        /// Write a 24-bit unsigned integer to a binary writer (little-endian)
        /// </summary>
        private static void WriteUInt24(BinaryWriter writer, uint value)
        {
            writer.Write((byte)(value & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                DisconnectAsync().GetAwaiter().GetResult();
                _disposed = true;
            }
        }
    }
}