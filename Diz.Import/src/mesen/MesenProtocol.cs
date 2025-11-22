namespace Diz.Import.mesen;

/// <summary>
/// Mesen2 Protocol message types for DiztinGUIsh live streaming.
/// This maps exactly to the C++ DiztinguishProtocol.h in Mesen2.
/// IMPORTANT: These values must match the C++ enum exactly!
/// </summary>
public enum MesenMessageType : byte
{
    // Connection lifecycle (matches C++ DiztinguishProtocol.h)
    Handshake = 0x01,
    HandshakeAck = 0x02,
    ConfigStream = 0x03,
    Heartbeat = 0x04,
    Disconnect = 0x05,
    
    // Execution trace streaming (matches C++ DiztinguishProtocol.h)
    ExecTrace = 0x10,
    ExecTraceBatch = 0x11,
    
    // Memory and CDL (matches C++ DiztinguishProtocol.h)
    MemoryAccess = 0x12,
    CdlUpdate = 0x13,
    CdlSnapshot = 0x14,
    
    // CPU state (matches C++ DiztinguishProtocol.h)
    CpuState = 0x20,
    CpuStateRequest = 0x21,
    
    // Label synchronization (matches C++ DiztinguishProtocol.h)
    LabelAdd = 0x30,
    LabelUpdate = 0x31,
    LabelDelete = 0x32,
    LabelSyncRequest = 0x33,
    LabelSyncResponse = 0x34,
    
    // Breakpoint control (matches C++ DiztinguishProtocol.h)
    BreakpointAdd = 0x40,
    BreakpointRemove = 0x41,
    BreakpointHit = 0x42,
    BreakpointList = 0x43,
    
    // Error handling (matches C++ DiztinguishProtocol.h)
    Error = 0xFF
}

/// <summary>
/// Handshake message from Mesen2 server.
/// Sent immediately upon connection to identify protocol version and ROM info.
/// This EXACTLY matches the C++ HandshakeMessage struct (268 bytes total).
/// </summary>
public struct MesenHandshakeMessage
{
    public ushort ProtocolVersionMajor; // uint16_t (2 bytes)
    public ushort ProtocolVersionMinor; // uint16_t (2 bytes) 
    public uint RomChecksum;            // uint32_t CRC32 (4 bytes)
    public uint RomSize;                // uint32_t (4 bytes)
    public string RomName;              // char[256] null-terminated (256 bytes)
    
    public override string ToString() => 
        $"Protocol v{ProtocolVersionMajor}.{ProtocolVersionMinor}, ROM: '{RomName}' ({RomSize} bytes, CRC32: 0x{RomChecksum:X8})";
}

/// <summary>
/// Handshake acknowledgment message (DiztinGUIsh → Mesen2).
/// This EXACTLY matches the C++ HandshakeAckMessage struct.
/// </summary>
public struct MesenHandshakeAckMessage
{
    public ushort ProtocolVersionMajor; // uint16_t (2 bytes)
    public ushort ProtocolVersionMinor; // uint16_t (2 bytes)
    public byte Accepted;               // uint8_t 0=rejected, 1=accepted (1 byte)
    public string ClientName;           // char[64] e.g., "DiztinGUIsh v2.0" (64 bytes)
    
    public override string ToString() => 
        $"HandshakeAck: Protocol v{ProtocolVersionMajor}.{ProtocolVersionMinor}, {(Accepted != 0 ? "Accepted" : "Rejected")}, Client: '{ClientName}'";
}

/// <summary>
/// Configuration message (DiztinGUIsh → Mesen2).
/// This EXACTLY matches the C++ ConfigStreamMessage struct (6 bytes total).
/// Sent after handshake to enable/configure trace streaming.
/// </summary>
public struct MesenConfigStreamMessage
{
    public byte EnableExecTrace;       // uint8_t 0/1 (1 byte)
    public byte EnableMemoryAccess;    // uint8_t 0/1 (1 byte)
    public byte EnableCdlUpdates;      // uint8_t 0/1 (1 byte)
    public byte TraceFrameInterval;    // uint8_t Send every N frames (1-60) (1 byte)
    public ushort MaxTracesPerFrame;   // uint16_t Max traces per batch (2 bytes)
    
    public override string ToString() => 
        $"Config: ExecTrace={EnableExecTrace}, MemAccess={EnableMemoryAccess}, CDL={EnableCdlUpdates}, Interval={TraceFrameInterval}, MaxTraces={MaxTracesPerFrame}";
}

/// <summary>
/// CPU execution trace entry.
/// This EXACTLY matches the C++ ExecTraceEntry struct (15 bytes total).
/// Sent as part of ExecTraceBatch messages.
/// </summary>
public struct MesenExecTraceMessage
{
    public uint PC;              // uint32_t Program Counter (24-bit, padded) (4 bytes)
    public byte Opcode;          // uint8_t Opcode byte (1 byte)
    public byte MFlag;           // uint8_t M flag (0/1) (1 byte)
    public byte XFlag;           // uint8_t X flag (0/1) (1 byte)
    public byte DBRegister;      // uint8_t Data Bank register (1 byte)
    public ushort DPRegister;    // uint16_t Direct Page register (2 bytes)
    public uint EffectiveAddr;   // uint32_t Calculated effective address (24-bit, padded) (4 bytes)
    
    // Convenience properties for boolean flags
    public bool MFlagBool => MFlag != 0;
    public bool XFlagBool => XFlag != 0;
    
    public override string ToString() => 
        $"PC:${PC:X6} Op:${Opcode:X2} M:{(MFlagBool ? "8" : "16")} X:{(XFlagBool ? "8" : "16")} DB:${DBRegister:X2} DP:${DPRegister:X4} EA:${EffectiveAddr:X6}";
}

/// <summary>
/// CDL (Code/Data Logger) update message.
/// Sent when emulator determines a byte's usage type (code, data, etc.).
/// </summary>
public struct MesenCdlUpdateMessage
{
    public uint Address;     // SNES address that was updated
    public byte CdlFlags;    // CDL flags indicating usage type
    
    // CDL flag definitions (matches SNES emulator conventions)
    public bool IsCode => (CdlFlags & 0x01) != 0;           // Executed as CPU instruction
    public bool IsData => (CdlFlags & 0x02) != 0;           // Read as data
    public bool IsIndirectData => (CdlFlags & 0x04) != 0;   // Read as indirect pointer
    public bool IsIndexedData => (CdlFlags & 0x08) != 0;    // Read with indexing
    
    public override string ToString() => 
        $"${Address:X6}: {(IsCode ? "C" : "")}{(IsData ? "D" : "")}{(IsIndirectData ? "I" : "")}{(IsIndexedData ? "X" : "")} (0x{CdlFlags:X2})";
}

/// <summary>
/// CPU state snapshot message.
/// Contains complete CPU register state at a specific moment.
/// </summary>
public struct MesenCpuStateMessage
{
    public uint PC;            // Program Counter
    public ushort A;           // Accumulator
    public ushort X;           // X Index Register  
    public ushort Y;           // Y Index Register
    public ushort SP;          // Stack Pointer
    public byte ProcessorFlags; // P Register (Status flags)
    public byte DataBank;      // Data Bank Register
    public ushort DirectPage;  // Direct Page Register
    public bool EmulationMode; // 6502 emulation mode flag
    
    // Processor flag helpers
    public bool CarryFlag => (ProcessorFlags & 0x01) != 0;
    public bool ZeroFlag => (ProcessorFlags & 0x02) != 0;
    public bool InterruptFlag => (ProcessorFlags & 0x04) != 0;
    public bool DecimalFlag => (ProcessorFlags & 0x08) != 0;
    public bool IndexFlag => (ProcessorFlags & 0x10) != 0;     // X flag (16/8 bit index)
    public bool MemoryFlag => (ProcessorFlags & 0x20) != 0;    // M flag (16/8 bit accumulator)
    public bool OverflowFlag => (ProcessorFlags & 0x40) != 0;
    public bool NegativeFlag => (ProcessorFlags & 0x80) != 0;
    
    public override string ToString() => 
        $"PC:${PC:X6} A:${A:X4} X:${X:X4} Y:${Y:X4} SP:${SP:X4} P:${ProcessorFlags:X2} DB:${DataBank:X2} D:${DirectPage:X4} {(EmulationMode ? "EMU" : "NAT")}";
}

/// <summary>
/// Memory dump message.
/// Contains a contiguous block of memory data from specified address range.
/// </summary>
public struct MesenMemoryDumpMessage
{
    public uint StartAddress;      // Starting SNES address
    public ushort Size;            // Number of bytes in data array
    public byte[] Data;            // Memory contents (length = Size)
    
    public override string ToString() => 
        $"Memory ${StartAddress:X6}-${StartAddress + Size - 1:X6} ({Size} bytes)";
}

/// <summary>
/// Label operation message.
/// For synchronizing labels/symbols between Mesen2 and DiztinGUIsh.
/// </summary>
public struct MesenLabelMessage
{
    public uint Address;        // SNES address for label
    public string LabelName;    // Label text (empty for delete operations)
    public string Comment;      // Optional comment text
    
    public override string ToString() => 
        $"${Address:X6}: {LabelName}" + (string.IsNullOrEmpty(Comment) ? "" : $" // {Comment}");
}

/// <summary>
/// Frame boundary messages.
/// Sent at start/end of each emulated video frame for synchronization.
/// </summary>
public struct MesenFrameMessage
{
    public uint FrameNumber;    // Incrementing frame counter
    public bool IsStart;        // true for frame start, false for frame end
    
    public override string ToString() => 
        $"Frame #{FrameNumber} {(IsStart ? "START" : "END")}";
}

/// <summary>
/// Error message from Mesen2 server.
/// Sent when server encounters problems or needs to disconnect.
/// </summary>
public struct MesenErrorMessage
{
    public ushort ErrorCode;    // Error identifier
    public string ErrorText;    // Human-readable error description
    
    // Common error codes
    public const ushort ERROR_PROTOCOL_VERSION_MISMATCH = 1;
    public const ushort ERROR_ROM_NOT_LOADED = 2;
    public const ushort ERROR_EMULATION_STOPPED = 3;
    public const ushort ERROR_MEMORY_ACCESS_FAILED = 4;
    public const ushort ERROR_INVALID_REQUEST = 5;
    
    public override string ToString() => 
        $"Error {ErrorCode}: {ErrorText}";
}