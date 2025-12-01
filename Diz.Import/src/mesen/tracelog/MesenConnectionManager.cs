using System;
using System.Threading;
using System.Threading.Tasks;
using Diz.Core.Interfaces;
using Diz.Cpu._65816;
using Diz.Import.Mesen;

namespace Diz.Import.mesen.tracelog;

/// <summary>
/// STATIC singleton manager to ensure Mesen2 TCP connection stays alive.
/// This keeps the connection in scope even when async void methods complete.
/// </summary>
public static class MesenConnectionManager
{
    private static readonly object _lock = new object();
    
    // STATIC field - stays alive for entire application lifetime
    private static MesenTraceLogImporter? _activeImporter;
    private static Task? _monitoringTask;
    private static CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Get the currently active connection, or null if not connected.
    /// </summary>
    public static MesenTraceLogImporter? ActiveConnection
    {
        get
        {
            lock (_lock)
            {
                return _activeImporter;
            }
        }
    }

    /// <summary>
    /// Check if there is an active connection.
    /// </summary>
    public static bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _activeImporter?.IsConnected ?? false;
            }
        }
    }

    /// <summary>
    /// Connect to Mesen2 and store connection in STATIC field to keep it alive.
    /// </summary>
    public static async Task<bool> ConnectAsync(ISnesData snesData, string host = "localhost", int port = 9998)
    {
        lock (_lock)
        {
            // Disconnect existing connection first
            if (_activeImporter != null)
            {
                MesenConnectionLogger.Log("CONNECTION_MANAGER", "Disconnecting existing connection before creating new one");
                DisconnectInternal();
            }

            MesenConnectionLogger.Log("CONNECTION_MANAGER", $"*** CREATING NEW IMPORTER (STATIC) FOR {host}:{port} ***");
            
            // Create NEW importer and store in STATIC field
            _activeImporter = new MesenTraceLogImporter(snesData);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        MesenConnectionLogger.Log("CONNECTION_MANAGER", $"Calling ConnectAsync({host}, {port})...");
        var connected = await _activeImporter.ConnectAsync(host, port);
        MesenConnectionLogger.Log("CONNECTION_MANAGER", $"ConnectAsync returned: {connected}");

        if (!connected)
        {
            MesenConnectionLogger.Log("CONNECTION_MANAGER", "*** CONNECTION FAILED - CLEANING UP ***");
            lock (_lock)
            {
                DisconnectInternal();
            }
            return false;
        }

        // Start background monitoring task to keep connection alive
        MesenConnectionLogger.Log("CONNECTION_MANAGER", "Starting background monitoring task (STATIC)...");
        _monitoringTask = Task.Run(async () =>
        {
            MesenConnectionLogger.Log("CONNECTION_MANAGER", "*** BACKGROUND TASK STARTED (STATIC) ***");
            try
            {
                var token = _cancellationTokenSource.Token;
                int loopCount = 0;

                while (!token.IsCancellationRequested && _activeImporter.IsConnected)
                {
                    loopCount++;
                    if (loopCount % 10 == 0) // Log every second
                    {
                        MesenConnectionLogger.Log("CONNECTION_MANAGER", $"Background task alive - loop #{loopCount}, IsConnected={_activeImporter.IsConnected}");
                    }
                    await Task.Delay(100, token);
                }

                MesenConnectionLogger.Log("CONNECTION_MANAGER", $"*** BACKGROUND TASK EXITING: Cancelled={token.IsCancellationRequested}, IsConnected={_activeImporter?.IsConnected} ***");
            }
            catch (OperationCanceledException)
            {
                MesenConnectionLogger.Log("CONNECTION_MANAGER", "Background task cancelled (normal shutdown)");
            }
            catch (Exception ex)
            {
                MesenConnectionLogger.Log("CONNECTION_MANAGER", $"*** BACKGROUND TASK ERROR: {ex.Message} ***");
                MesenConnectionLogger.Log("CONNECTION_MANAGER", ex.StackTrace ?? "No stack trace");
            }
        }, _cancellationTokenSource.Token);

        MesenConnectionLogger.Log("CONNECTION_MANAGER", "*** CONNECTION ESTABLISHED AND STORED IN STATIC FIELD ***");
        return true;
    }

    /// <summary>
    /// Disconnect from Mesen2 and cleanup resources.
    /// </summary>
    public static async Task<long> DisconnectAsync()
    {
        MesenConnectionLogger.Log("CONNECTION_MANAGER", "*** DisconnectAsync CALLED ***");
        
        long bytesModified = 0;
        
        lock (_lock)
        {
            if (_activeImporter == null)
            {
                MesenConnectionLogger.Log("CONNECTION_MANAGER", "No active importer - returning 0");
                return 0;
            }

            try
            {
                // Signal cancellation
                MesenConnectionLogger.Log("CONNECTION_MANAGER", "Cancelling background task...");
                _cancellationTokenSource?.Cancel();
            }
            catch (Exception ex)
            {
                MesenConnectionLogger.Log("CONNECTION_MANAGER", $"Error cancelling: {ex.Message}");
            }
        }

        // Wait for background task outside lock
        if (_monitoringTask != null)
        {
            MesenConnectionLogger.Log("CONNECTION_MANAGER", "Waiting for background task to complete (5s timeout)...");
            await Task.WhenAny(_monitoringTask, Task.Delay(5000));
            MesenConnectionLogger.Log("CONNECTION_MANAGER", "Background task wait completed");
        }

        lock (_lock)
        {
            try
            {
                if (_activeImporter.IsConnected)
                {
                    MesenConnectionLogger.Log("CONNECTION_MANAGER", "Disconnecting importer...");
                    _activeImporter.Disconnect();
                }

                MesenConnectionLogger.Log("CONNECTION_MANAGER", "Copying temp comments to main data...");
                _activeImporter.CopyTempGeneratedCommentsIntoMainSnesData();

                bytesModified = _activeImporter.CurrentStats.NumRomBytesModified;
                MesenConnectionLogger.Log("CONNECTION_MANAGER", $"Final stats: {bytesModified} ROM bytes modified");
            }
            catch (Exception ex)
            {
                MesenConnectionLogger.Log("CONNECTION_MANAGER", $"Error during disconnect: {ex.Message}");
            }
            finally
            {
                DisconnectInternal();
            }
        }

        return bytesModified;
    }

    /// <summary>
    /// Internal cleanup - must be called inside lock.
    /// </summary>
    private static void DisconnectInternal()
    {
        MesenConnectionLogger.Log("CONNECTION_MANAGER", "Cleaning up connection resources...");
        
        _activeImporter?.Dispose();
        _activeImporter = null;

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _monitoringTask = null;
        
        MesenConnectionLogger.Log("CONNECTION_MANAGER", "*** CLEANUP COMPLETE ***");
    }
}
