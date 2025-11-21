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
        Handshake = 0x01,
        HandshakeAck = 0x02,
        ConfigStream = 0x03,
        Heartbeat = 0x04,
        Disconnect = 0x05,
        
        ExecTrace = 0x10,
        ExecTraceBatch = 0x11,
        
        MemoryAccess = 0x12,
        CdlUpdate = 0x13,
        CdlSnapshot = 0x14,
        
        CpuState = 0x20,
        CpuStateRequest = 0x21,
        
        LabelAdd = 0x30,
        LabelUpdate = 0x31,
        LabelDelete = 0x32,
        LabelSyncRequest = 0x33,
        LabelSyncResponse = 0x34,
        
        BreakpointAdd = 0x40,
        BreakpointRemove = 0x41,
        BreakpointHit = 0x42,
        BreakpointList = 0x43,
        
        MemoryDumpRequest = 0x50,
        MemoryDumpResponse = 0x51,
        
        Error = 0xFF
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
                Console.WriteLine($"[Mesen2StreamingClient] ConnectAsync starting - Host: {Host}, Port: {Port}, Timeout: {TimeoutMs}ms");
                Status = Mesen2ConnectionStatus.Connecting;

                _tcpClient = new TcpClient();
                _tcpClient.ReceiveTimeout = TimeoutMs;
                _tcpClient.SendTimeout = TimeoutMs;

                Console.WriteLine($"[Mesen2StreamingClient] TcpClient created, attempting connection...");
                await _tcpClient.ConnectAsync(Host, Port).ConfigureAwait(false);
                Console.WriteLine($"[Mesen2StreamingClient] TcpClient.ConnectAsync completed successfully!");
                
                _networkStream = _tcpClient.GetStream();
                Console.WriteLine($"[Mesen2StreamingClient] NetworkStream obtained");

                Status = Mesen2ConnectionStatus.Connected;

                Console.WriteLine($"[Mesen2StreamingClient] Starting receive loop task...");
                // Start receive loop
                _cancellationTokenSource = new CancellationTokenSource();
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));

                Console.WriteLine($"[Mesen2StreamingClient] Beginning handshake...");
                // Perform handshake
                if (await PerformHandshakeAsync().ConfigureAwait(false))
                {
                    Console.WriteLine($"[Mesen2StreamingClient] Handshake successful!");
                    Status = Mesen2ConnectionStatus.HandshakeComplete;
                    
                    // Send streaming configuration automatically after handshake
                    // This is required for Mesen2 to start streaming data
                    Console.WriteLine($"[Mesen2StreamingClient] Sending streaming configuration...");
                    bool configSent = await SetStreamingConfigAsync(
                        enableExecTrace: true,
                        enableMemoryAccess: true,
                        enableCdlUpdates: true,
                        traceFrameInterval: 1,  // Send every frame
                        maxTracesPerFrame: 10000 // Max traces per frame
                    ).ConfigureAwait(false);
                    
                    if (!configSent)
                    {
                        Console.WriteLine($"[Mesen2StreamingClient] ERROR: Failed to send streaming configuration!");
                        await DisconnectAsync().ConfigureAwait(false);
                        return false;
                    }
                    
                    Console.WriteLine($"[Mesen2StreamingClient] Connection fully established and configured!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[Mesen2StreamingClient] ERROR: Handshake failed!");
                    await DisconnectAsync().ConfigureAwait(false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Mesen2StreamingClient] EXCEPTION during connect: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[Mesen2StreamingClient] Stack trace: {ex.StackTrace}");
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
            lock (_lockObject)
            {
                // Prevent double-disconnect
                if (Status == Mesen2ConnectionStatus.Disconnected)
                    return;
                    
                Status = Mesen2ConnectionStatus.Disconnected;
            }

            try
            {
                // Send disconnect message if we have a network stream
                if (_networkStream != null && _tcpClient?.Connected == true)
                {
                    try
                    {
                        await SendMessageAsync(Mesen2MessageType.Disconnect, Array.Empty<byte>()).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore errors during disconnect message send
                    }
                }
            }
            catch
            {
                // Ignore errors during disconnect
            }

            // Cancel the receive loop
            try
            {
                _cancellationTokenSource?.Cancel();
            }
            catch
            {
                // Ignore cancellation errors
            }

            // Wait for receive task to complete
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

            // Clean up resources
            try
            {
                _networkStream?.Close();
                _networkStream?.Dispose();
            }
            catch { }

            try
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
            }
            catch { }

            try
            {
                _cancellationTokenSource?.Dispose();
            }
            catch { }

            _networkStream = null;
            _tcpClient = null;
            _cancellationTokenSource = null;
            _receiveTask = null;
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
                    {
                        // Connection closed gracefully
                        break;
                    }

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
                        {
                            // Connection closed while reading payload
                            break;
                        }
                    }

                    Interlocked.Increment(ref _messagesReceived);
                    Interlocked.Add(ref _bytesReceived, 5 + payloadLength);

                    // Process message
                    try
                    {
                        await ProcessReceivedMessageAsync(messageType, payload).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Log message processing error but continue receiving
                        System.Diagnostics.Debug.WriteLine($"Error processing message type {messageType}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                // Connection lost or error occurred
                System.Diagnostics.Debug.WriteLine($"Receive loop error: {ex.Message}");
            }
            finally
            {
                // Connection ended - ensure proper cleanup
                await DisconnectAsync().ConfigureAwait(false);
            }
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
                    case Mesen2MessageType.ExecTraceBatch:
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
                
                // Check if this is a batch message (has 6-byte header)
                if (payload.Length >= 6)
                {
                    // Read batch header
                    var frameNumber = reader.ReadUInt32();
                    var entryCount = reader.ReadUInt16();
                    
                    // Parse each trace entry (15 bytes each)
                    for (int i = 0; i < entryCount && stream.Position + 15 <= stream.Length; i++)
                    {
                        var pc = reader.ReadUInt32();         // 4 bytes (24-bit padded)
                        var opcode = reader.ReadByte();       // 1 byte
                        var mFlag = reader.ReadByte();        // 1 byte
                        var xFlag = reader.ReadByte();        // 1 byte
                        var dbRegister = reader.ReadByte();   // 1 byte
                        var dpRegister = reader.ReadUInt16(); // 2 bytes
                        var effectiveAddr = reader.ReadUInt32(); // 4 bytes (24-bit padded)
                        
                        // Mask PC to 24-bit
                        pc &= 0xFFFFFF;
                        effectiveAddr &= 0xFFFFFF;
                        
                        // Create instruction bytes array (just opcode for now)
                        var instructionBytes = new byte[] { opcode };
                        
                        // Create CPU state with available information
                        var cpuState = new Mesen2CpuState
                        {
                            PC = pc,
                            DB = dbRegister,
                            D = dpRegister,
                            Timestamp = DateTime.UtcNow,
                            EmulationMode = false
                        };
                        
                        // Create trace entry
                        var entry = new Mesen2TraceEntry
                        {
                            PC = pc,
                            Instruction = instructionBytes,
                            Disassembly = $"${pc:X6}: {opcode:X2}",
                            CpuState = cpuState,
                            Timestamp = DateTime.UtcNow
                        };
                        
                        entries.Add(entry);
                    }
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