using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Linq;

namespace Diz.Import.mesen.tracelog;

/// <summary>
/// TCP client for connecting to Mesen2's DiztinGUIsh streaming server.
/// Handles binary protocol parsing and event-driven message processing.
/// </summary>
public class MesenLiveTraceClient : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;
    private bool _disposed;

    // Connection state
    public bool IsConnected => _tcpClient?.Connected == true;
    public string? ConnectedHost { get; private set; }
    public int ConnectedPort { get; private set; }
    
    // Statistics
    public long MessagesReceived { get; private set; }
    public long BytesReceived { get; private set; }
    public DateTime? ConnectionTime { get; private set; }
    public TimeSpan? ConnectionDuration => ConnectionTime.HasValue ? DateTime.Now - ConnectionTime.Value : null;

    // Events for different message types
    public event EventHandler<MesenHandshakeMessage>? HandshakeReceived;
    public event EventHandler<MesenExecTraceMessage>? ExecTraceReceived;
    public event EventHandler<MesenCdlUpdateMessage>? CdlUpdateReceived;
    public event EventHandler<MesenCpuStateMessage>? CpuStateReceived;
    public event EventHandler<MesenMemoryDumpMessage>? MemoryDumpReceived;
    public event EventHandler<MesenLabelMessage>? LabelReceived;
    public event EventHandler<MesenFrameMessage>? FrameReceived;
    public event EventHandler<MesenErrorMessage>? ErrorReceived;
    public event EventHandler? Disconnected;
    
    // Configuration
    public int ReceiveTimeoutMs { get; set; } = 5000;
    public int ConnectTimeoutMs { get; set; } = 3000;
    public bool LogRawMessages { get; set; } = false;

    /// <summary>
    /// Connect to Mesen2 DiztinGUIsh server.
    /// </summary>
    /// <param name="host">Server hostname/IP (default: localhost)</param>
    /// <param name="port">Server port (default: 9998)</param>
    /// <returns>True if connected successfully</returns>
    public async Task<bool> ConnectAsync(string host = "localhost", int port = 9998)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MesenLiveTraceClient));

        if (IsConnected)
            return true;

        try
        {
            Console.WriteLine($"[MesenLiveTraceClient] ConnectAsync starting - Host: {host}, Port: {port}, Timeout: {ConnectTimeoutMs}ms");
            _tcpClient = new TcpClient();
            _tcpClient.ReceiveTimeout = ReceiveTimeoutMs;
            
            Console.WriteLine($"[MesenLiveTraceClient] TcpClient created, attempting connection...");
            // Connect with timeout
            using var timeoutCts = new CancellationTokenSource(ConnectTimeoutMs);
            await _tcpClient.ConnectAsync(host, port, timeoutCts.Token);
            
            Console.WriteLine($"[MesenLiveTraceClient] Connection successful! Getting network stream...");
            _stream = _tcpClient.GetStream();
            ConnectedHost = host;
            ConnectedPort = port;
            ConnectionTime = DateTime.Now;
            
            Console.WriteLine($"[MesenLiveTraceClient] Starting receive loop...");
            // Start receive loop
            _cancellationTokenSource = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_cancellationTokenSource.Token);
            
            Console.WriteLine($"[MesenLiveTraceClient] ConnectAsync completed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MesenLiveTraceClient] EXCEPTION during connect: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[MesenLiveTraceClient] Stack trace: {ex.StackTrace}");
            CleanupConnection();
            return false;
        }
    }

    /// <summary>
    /// Disconnect from server gracefully.
    /// </summary>
    public void Disconnect()
    {
        if (!IsConnected)
            return;

        _cancellationTokenSource?.Cancel();
        CleanupConnection();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Get connection statistics as formatted string.
    /// </summary>
    public string GetConnectionStats()
    {
        if (!IsConnected)
            return "Not connected";

        var duration = ConnectionDuration?.ToString(@"mm\:ss") ?? "Unknown";
        var bytesPerSec = ConnectionDuration?.TotalSeconds > 0 ? BytesReceived / ConnectionDuration.Value.TotalSeconds : 0;
        var messagesPerSec = ConnectionDuration?.TotalSeconds > 0 ? MessagesReceived / ConnectionDuration.Value.TotalSeconds : 0;
        
        return $"Connected to {ConnectedHost}:{ConnectedPort} for {duration} | " +
               $"Received: {MessagesReceived:N0} messages, {BytesReceived:N0} bytes | " +
               $"Rate: {messagesPerSec:F1} msg/sec, {bytesPerSec:F1} bytes/sec";
    }

    /// <summary>
    /// Send handshake acknowledgment to Mesen2 server.
    /// </summary>
    public async Task<bool> SendHandshakeAckAsync(bool accepted = true, string clientName = "DiztinGUIsh")
    {
        if (!IsConnected || _stream == null)
            return false;

        try
        {
            // Create handshake ack message (69 bytes total)
            var ackMessage = new MesenHandshakeAckMessage
            {
                ProtocolVersionMajor = 1,
                ProtocolVersionMinor = 0,
                Accepted = (byte)(accepted ? 1 : 0),
                ClientName = clientName.PadRight(64, '\0').Substring(0, 64) // Ensure exactly 64 bytes
            };

            // Build binary payload (5 bytes for handshake ack)
            var payload = new byte[5 + 64]; // 2 + 2 + 1 + 64 = 69 bytes total
            BitConverter.GetBytes(ackMessage.ProtocolVersionMajor).CopyTo(payload, 0);
            BitConverter.GetBytes(ackMessage.ProtocolVersionMinor).CopyTo(payload, 2);
            payload[4] = ackMessage.Accepted;
            Encoding.ASCII.GetBytes(ackMessage.ClientName).CopyTo(payload, 5);

            // Build message header (5 bytes: type + length)
            var header = new byte[5];
            header[0] = (byte)MesenMessageType.HandshakeAck;
            BitConverter.GetBytes((uint)payload.Length).CopyTo(header, 1);

            // Send header + payload
            await _stream.WriteAsync(header.Concat(payload).ToArray());
            await _stream.FlushAsync();

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Main message receive loop. Runs on background thread.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                // Read message header (5 bytes: type + length)
                var headerBytes = await ReadExactBytesAsync(5, cancellationToken);
                if (headerBytes == null)
                    break; // Connection closed
                
                var messageType = (MesenMessageType)headerBytes[0];
                var messageLength = BitConverter.ToUInt32(headerBytes, 1);
                
                // Read message payload
                byte[]? payloadBytes = null;
                if (messageLength > 0)
                {
                    payloadBytes = await ReadExactBytesAsync((int)messageLength, cancellationToken);
                    if (payloadBytes == null)
                        break; // Connection closed
                }
                
                // Update statistics
                MessagesReceived++;
                BytesReceived += 5 + (payloadBytes?.Length ?? 0);
                
                // Parse and dispatch message
                ProcessMessage(messageType, payloadBytes);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling
        }
        catch (Exception)
        {
            // Connection error
        }
        finally
        {
            if (IsConnected)
                Disconnect();
        }
    }

    /// <summary>
    /// Read exact number of bytes from stream with cancellation support.
    /// </summary>
    private async Task<byte[]?> ReadExactBytesAsync(int count, CancellationToken cancellationToken)
    {
        if (_stream == null || count <= 0)
            return null;

        var buffer = new byte[count];
        var totalRead = 0;

        while (totalRead < count && !cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await _stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), cancellationToken);
            if (bytesRead == 0)
                return null; // Connection closed
            totalRead += bytesRead;
        }

        return totalRead == count ? buffer : null;
    }

    /// <summary>
    /// Parse binary message and raise appropriate event.
    /// </summary>
    private void ProcessMessage(MesenMessageType messageType, byte[]? payload)
    {
        try
        {
            switch (messageType)
            {
                case MesenMessageType.Handshake:
                    if (payload != null && payload.Length >= 268) // Expected handshake size (268 bytes)
                    {
                        var handshake = ParseHandshakeMessage(payload);
                        HandshakeReceived?.Invoke(this, handshake);
                    }
                    break;

                case MesenMessageType.ExecTrace:
                case MesenMessageType.ExecTraceBatch:
                    if (payload != null && payload.Length >= 15) // Expected trace entry size (15 bytes)
                    {
                        var trace = ParseExecTraceMessage(payload);
                        ExecTraceReceived?.Invoke(this, trace);
                    }
                    break;

                case MesenMessageType.CdlUpdate:
                    if (payload != null && payload.Length >= 5) // Expected CDL size
                    {
                        var cdl = ParseCdlUpdateMessage(payload);
                        CdlUpdateReceived?.Invoke(this, cdl);
                    }
                    break;

                case MesenMessageType.CpuState:
                    if (payload != null && payload.Length >= 17) // Expected CPU state size
                    {
                        var cpuState = ParseCpuStateMessage(payload);
                        CpuStateReceived?.Invoke(this, cpuState);
                    }
                    break;

                case MesenMessageType.Error:
                    if (payload != null && payload.Length >= 2)
                    {
                        var error = ParseErrorMessage(payload);
                        ErrorReceived?.Invoke(this, error);
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // Error processing message
        }
    }

    /// <summary>
    /// Parse handshake message from binary data.
    /// Matches C++ HandshakeMessage struct exactly (268 bytes):
    /// - uint16_t protocolVersionMajor (2 bytes)
    /// - uint16_t protocolVersionMinor (2 bytes) 
    /// - uint32_t romChecksum (4 bytes)
    /// - uint32_t romSize (4 bytes)
    /// - char romName[256] (256 bytes)
    /// </summary>
    private static MesenHandshakeMessage ParseHandshakeMessage(byte[] data)
    {
        return new MesenHandshakeMessage
        {
            ProtocolVersionMajor = BitConverter.ToUInt16(data, 0),      // bytes 0-1
            ProtocolVersionMinor = BitConverter.ToUInt16(data, 2),      // bytes 2-3
            RomChecksum = BitConverter.ToUInt32(data, 4),               // bytes 4-7
            RomSize = BitConverter.ToUInt32(data, 8),                   // bytes 8-11
            RomName = System.Text.Encoding.ASCII.GetString(data, 12, 256).TrimEnd('\0') // bytes 12-267
        };
    }

    /// <summary>
    /// Parse execution trace message from binary data.
    /// Matches C++ ExecTraceEntry struct exactly (15 bytes):
    /// - uint32_t pc (4 bytes) 
    /// - uint8_t opcode (1 byte)
    /// - uint8_t mFlag (1 byte)
    /// - uint8_t xFlag (1 byte) 
    /// - uint8_t dbRegister (1 byte)
    /// - uint16_t dpRegister (2 bytes)
    /// - uint32_t effectiveAddr (4 bytes)
    /// </summary>
    private static MesenExecTraceMessage ParseExecTraceMessage(byte[] data)
    {
        return new MesenExecTraceMessage
        {
            PC = BitConverter.ToUInt32(data, 0) & 0xFFFFFF, // bytes 0-3 (24-bit address)
            Opcode = data[4],                                // byte 4
            MFlag = data[5],                                 // byte 5 (0 or 1)
            XFlag = data[6],                                 // byte 6 (0 or 1) 
            DBRegister = data[7],                            // byte 7
            DPRegister = BitConverter.ToUInt16(data, 8),     // bytes 8-9
            EffectiveAddr = BitConverter.ToUInt32(data, 10) & 0xFFFFFF // bytes 10-13 (24-bit address)
        };
    }

    /// <summary>
    /// Parse CDL update message from binary data.
    /// </summary>
    private static MesenCdlUpdateMessage ParseCdlUpdateMessage(byte[] data)
    {
        return new MesenCdlUpdateMessage
        {
            Address = BitConverter.ToUInt32(data, 0) & 0xFFFFFF, // 24-bit address
            CdlFlags = data[4]
        };
    }

    /// <summary>
    /// Parse CPU state message from binary data.
    /// </summary>
    private static MesenCpuStateMessage ParseCpuStateMessage(byte[] data)
    {
        return new MesenCpuStateMessage
        {
            PC = BitConverter.ToUInt32(data, 0) & 0xFFFFFF, // 24-bit
            A = BitConverter.ToUInt16(data, 4),
            X = BitConverter.ToUInt16(data, 6),
            Y = BitConverter.ToUInt16(data, 8),
            SP = BitConverter.ToUInt16(data, 10),
            ProcessorFlags = data[12],
            DataBank = data[13],
            DirectPage = BitConverter.ToUInt16(data, 14),
            EmulationMode = data[16] != 0
        };
    }

    /// <summary>
    /// Parse frame message from binary data.
    /// </summary>
    private static MesenFrameMessage ParseFrameMessage(byte[] data, bool isStart)
    {
        return new MesenFrameMessage
        {
            FrameNumber = BitConverter.ToUInt32(data, 0),
            IsStart = isStart
        };
    }

    /// <summary>
    /// Parse error message from binary data.
    /// </summary>
    private static MesenErrorMessage ParseErrorMessage(byte[] data)
    {
        var errorCode = BitConverter.ToUInt16(data, 0);
        var errorText = System.Text.Encoding.UTF8.GetString(data, 2, data.Length - 2);
        
        return new MesenErrorMessage
        {
            ErrorCode = errorCode,
            ErrorText = errorText
        };
    }

    /// <summary>
    /// Clean up connection resources.
    /// </summary>
    private void CleanupConnection()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _stream?.Close();
            _tcpClient?.Close();
        }
        catch (Exception)
        {
            // Ignore cleanup errors
        }
        finally
        {
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _cancellationTokenSource?.Dispose();
            
            _stream = null;
            _tcpClient = null;
            _cancellationTokenSource = null;
            _receiveTask = null;
            ConnectedHost = null;
            ConnectedPort = 0;
            ConnectionTime = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Disconnect();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}