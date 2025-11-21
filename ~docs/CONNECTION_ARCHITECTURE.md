# Mesen2 Debugger Logs and Connection Architecture

## 📍 Where Are the Mesen Debugger Logs?

### Console Output (Real-time)
All `_debugger->Log()` calls output to **`std::cout`** in real-time.

**Location:** `Core/Debugger/Debugger.cpp:1047-1056`
```cpp
void Debugger::Log(string message)
{
    auto lock = _logLock.AcquireSafe();
    if(_debuggerLog.size() >= 1000) {
        _debuggerLog.pop_front();
    }
    _debuggerLog.push_back(message);

    std::cout << message << std::endl;
}
```

### How to See Logs

1. **If running Mesen2 from Visual Studio:**
   - Logs appear in Output window (Debug output)

2. **If running Mesen2.exe directly:**
   - Launch from command prompt to see console output:
     ```powershell
     cd c:\Users\me\source\repos\Mesen2\bin\win-x64\Release
     .\Mesen.exe
     ```

3. **In-Memory Log Buffer:**
   - Last 1000 messages stored in `_debuggerLog` (std::deque)
   - Not saved to file by default
   - Accessible via debugger API if needed

### DiztinGUIsh Server Logs
All our enhanced logging uses `[DiztinGUIsh]` prefix:
```
[DiztinGUIsh] Enabled SNES debugger for streaming
[DiztinGUIsh] Created socket, attempting to bind to port 9998...
[DiztinGUIsh] Socket bound successfully, calling Listen(1)...
[DiztinGUIsh] Server thread started, waiting for client connection...
[DiztinGUIsh] Calling Accept() - this will block until client connects...
[DiztinGUIsh] Client connected successfully! Sending handshake...
```

## 🔌 Connection Architecture Analysis

### Current Implementation: **CORRECT - Using `using` Statement**

**Location:** `ProjectController.ImportMesenTraceLive()`

```csharp
public async Task<long> ImportMesenTraceLive(string host = "localhost", int port = 9998, CancellationToken cancellationToken = default)
{
    using var importer = new MesenTraceLogImporter(Project.Data.GetSnesApi());
    
    // Connect
    var connected = await importer.ConnectAsync(host, port);
    if (!connected)
        throw new InvalidOperationException($"Failed to connect...");

    try
    {
        // Keep connection alive while streaming
        while (!cancellationToken.IsCancellationRequested && importer.IsConnected)
        {
            await Task.Delay(100, cancellationToken); // Polling loop
        }
    }
    finally
    {
        // Cleanup
        importer.Disconnect();
        importer.CopyTempGeneratedCommentsIntoMainSnesData();
    }

    return importer.CurrentStats.NumRomBytesModified;
}
```

### Connection Lifetime

**✅ Connection IS held for the entire streaming session:**

1. **`using var importer`** - Lives until method exits
2. **`await importer.ConnectAsync()`** - Creates connection, starts background receive thread
3. **`while (!cancelled && IsConnected)`** - Keeps method alive, polling connection status
4. **Background thread** - `MesenLiveTraceClient._receiveTask` runs continuously, processing messages
5. **Finally block** - Ensures cleanup even on exception

### Background Thread Architecture

**MesenLiveTraceClient** already has proper worker thread:

```csharp
public async Task<bool> ConnectAsync(string host, int port)
{
    // ... TCP connection ...
    
    // Start receive loop on background thread
    _cancellationTokenSource = new CancellationTokenSource();
    _receiveTask = ReceiveLoopAsync(_cancellationTokenSource.Token);
    
    return true;
}

private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested && IsConnected)
    {
        // Read message header (5 bytes)
        var headerBytes = await ReadExactBytesAsync(5, cancellationToken);
        
        // Read payload
        // Process message
        ProcessMessage(messageType, payloadBytes);
    }
}
```

### Event-Driven Processing

Messages are processed via **events**, NOT polling:

```csharp
// MesenTraceLogImporter constructor
_client.HandshakeReceived += OnHandshakeReceived;
_client.ExecTraceReceived += OnExecTraceReceived;
_client.CdlUpdateReceived += OnCdlUpdateReceived;
_client.CpuStateReceived += OnCpuStateReceived;
_client.ErrorReceived += OnErrorReceived;
_client.Disconnected += OnDisconnected;
```

When background thread receives a message, it **fires an event** immediately:
```csharp
private void ProcessMessage(MesenMessageType type, byte[] payload)
{
    switch(type) {
        case MesenMessageType.Handshake:
            HandshakeReceived?.Invoke(this, ParseHandshake(payload));
            break;
        case MesenMessageType.ExecTrace:
            ExecTraceReceived?.Invoke(this, ParseExecTrace(payload));
            break;
        // ...
    }
}
```

## ✅ Architecture is CORRECT - No Changes Needed!

### Why the Current Design Works

1. **Connection Lifetime:**
   - ✅ `using` statement ensures cleanup
   - ✅ Connection held for entire streaming session
   - ✅ Method doesn't exit until cancelled or disconnected

2. **Background Processing:**
   - ✅ `_receiveTask` runs on separate thread
   - ✅ No blocking of UI thread
   - ✅ Events fire as messages arrive

3. **Cancellation:**
   - ✅ `CancellationToken` passed from UI
   - ✅ While loop checks token every 100ms
   - ✅ Background thread also respects cancellation

4. **Cleanup:**
   - ✅ `finally` block ensures disconnect
   - ✅ Comments copied to main data
   - ✅ Dispose pattern handles resources

### The Polling Loop Purpose

```csharp
while (!cancellationToken.IsCancellationRequested && importer.IsConnected)
{
    await Task.Delay(100, cancellationToken);
}
```

**This is NOT polling data** - it's keeping the method alive! 

- Background thread processes data continuously
- Main method just waits for cancellation or disconnect
- 100ms delay prevents CPU spinning

## 🎯 UI Flow

### MainWindow.Importers.cs

```csharp
private async void ImportMesenTraceLiveStreaming()
{
    // Get connection params from dialog
    var (host, port) = ShowMesen2ConnectionDialog();
    
    // Create cancellation UI
    var streamForm = CreateStreamingProgressForm(out Button stopButton);
    var cancellationTokenSource = new CancellationTokenSource();
    
    stopButton.Click += (s, e) => cancellationTokenSource.Cancel();
    
    try
    {
        streamForm.Show();
        
        // THIS is where connection is held
        var bytesModified = await ProjectController.ImportMesenTraceLive(
            host, port, cancellationTokenSource.Token
        );
        
        streamForm.Close();
        ShowInfo($"Streaming completed. Modified {bytesModified:N0} bytes");
    }
    catch (Exception ex)
    {
        streamForm.Close();
        ShowError($"Failed to connect: {ex.Message}");
    }
}
```

### Connection Lifecycle

```
User clicks "Mesen2 Live Streaming"
    ↓
UI shows connection dialog (host/port)
    ↓
UI creates progress form with "Stop" button
    ↓
Calls ProjectController.ImportMesenTraceLive()
    ↓
    Creates MesenTraceLogImporter (using statement)
        ↓
        Calls importer.ConnectAsync()
            ↓
            Creates TcpClient
            Connects to Mesen2 server
            Starts background _receiveTask thread
                ↓
                Background thread continuously:
                - Reads messages from socket
                - Fires events (ExecTraceReceived, etc.)
                - Updates SNES data
            ↓
        Enters while loop (keeps method alive)
            ↓
            Waits for cancellation OR disconnect
            
User clicks "Stop" OR Mesen2 disconnects
    ↓
    CancellationToken signaled OR IsConnected=false
        ↓
        While loop exits
            ↓
            Finally block executes
                ↓
                importer.Disconnect()
                importer.CopyTempGeneratedCommentsIntoMainSnesData()
            ↓
        Using statement disposes importer
    ↓
UI closes progress form
UI shows completion message
```

## 🔍 Debugging Tips

### To Verify Connection is Held

1. **Set breakpoint in while loop:**
   ```csharp
   while (!cancellationToken.IsCancellationRequested && importer.IsConnected)
   {
       await Task.Delay(100, cancellationToken); // <- HERE
   }
   ```

2. **Check background thread:**
   - Debug → Windows → Threads
   - Look for `ReceiveLoopAsync` thread

3. **Watch events firing:**
   - Set breakpoint in `OnExecTraceReceived`
   - Should hit continuously as Mesen2 executes

### To See Mesen2 Server Logs

**PowerShell:**
```powershell
# Run Mesen2 from terminal to see console output
cd c:\Users\me\source\repos\Mesen2\bin\win-x64\Release
.\Mesen.exe

# In Mesen2, load ROM and run Lua:
# emu.startDiztinguishServer(9998)

# Watch for:
# [DiztinGUIsh] Server started successfully on port 9998
# [DiztinGUIsh] Client connected successfully!
```

## 📊 Summary

### Questions Answered

**Q: Where are Mesen debugger logs?**
- A: `std::cout` (console output) + in-memory buffer (last 1000 messages)

**Q: Is GUI connecting only for lifespan of GUI command?**
- A: **No** - connection is held continuously until cancelled or disconnected

**Q: Does it need a worker thread?**
- A: **Already has one** - `_receiveTask` background thread handles all message reception

### Architecture is Solid ✅

- Connection lifetime properly managed via `using` statement
- Background thread processes messages continuously
- Events fire as data arrives (not polling)
- Cancellation token allows graceful shutdown
- Finally block ensures cleanup

**No changes needed to connection architecture!**
