using System.Diagnostics;
using System.Text.Json;
using carton.Core.Models;
using carton.Core.Utilities;

namespace carton.Core.Services;

public partial class SingBoxManager
{
    public long? GetRunningProcessMemoryBytes()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Refresh();
                return _process.WorkingSet64;
            }

            if (_elevatedPid.HasValue && _elevatedPid.Value > 0)
            {
                using var process = Process.GetProcessById(_elevatedPid.Value);
                process.Refresh();
                return process.WorkingSet64;
            }
        }
        catch
        {
        }

        return null;
    }

    private void EnsureRuntimeMonitorsRunning()
    {
        var replacingCanceledMonitors = _monitorCancellation?.IsCancellationRequested == true;
        var cancellationToken = EnsureRuntimeMonitorCancellationToken();
        if (replacingCanceledMonitors || _logMonitorTask is not { IsCompleted: false })
        {
            _logMonitorTask = Task.Run(() => StartLogMonitorAsync(cancellationToken));
        }

        if (replacingCanceledMonitors || _trafficMonitorTask is not { IsCompleted: false })
        {
            _trafficMonitorTask = Task.Run(() => StartTrafficMonitorAsync(cancellationToken));
        }

        if (!replacingCanceledMonitors && _memoryMonitorTask is { IsCompleted: false })
        {
            return;
        }

        _memoryMonitorTask = Task.Run(() => StartMemoryMonitorAsync(cancellationToken));
    }

    private CancellationToken EnsureRuntimeMonitorCancellationToken()
    {
        if (_monitorCancellation is { IsCancellationRequested: false } current)
        {
            return current.Token;
        }

        _monitorCancellation = new CancellationTokenSource();
        return _monitorCancellation.Token;
    }

    private void CancelRuntimeMonitors()
    {
        try
        {
            _monitorCancellation?.Cancel();
        }
        catch
        {
        }
    }

    private async Task StartLogMonitorAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        var monitorLevel = _logMonitorLevel;
        LogManager($"[INFO] Log monitor subscribed at level: {monitorLevel}");

        while (_state.Status == ServiceStatus.Running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var entry in CreateApiClient().SubscribeLogsAsync(monitorLevel, cancellationToken))
                {
                    if (_state.Status != ServiceStatus.Running)
                    {
                        break;
                    }

                    StopStartupLogCapture();
                    consecutiveFailures = 0;
                    LogKernel(entry);
                }

                if (_state.Status == ServiceStatus.Running)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                consecutiveFailures++;
                if (consecutiveFailures == 1 || consecutiveFailures % 10 == 0)
                {
                    LogManager($"[WARN] Log monitor error: {e.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, consecutiveFailures))), cancellationToken);
            }
        }
    }

    private async Task StartTrafficMonitorAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        while (_state.Status == ServiceStatus.Running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var traffic in CreateApiClient().SubscribeTrafficAsync(cancellationToken))
                {
                    if (_state.Status != ServiceStatus.Running)
                    {
                        break;
                    }

                    consecutiveFailures = 0;
                    _state.UploadSpeed = traffic.Uplink;
                    _state.DownloadSpeed = traffic.Downlink;
                    _state.TotalUpload += traffic.Uplink;
                    _state.TotalDownload += traffic.Downlink;
                    TrafficUpdated?.Invoke(this, traffic);
                }

                if (_state.Status == ServiceStatus.Running)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                consecutiveFailures++;
                if (consecutiveFailures == 1 || consecutiveFailures % 10 == 0)
                {
                    LogManager($"[WARN] Traffic monitor error: {e.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, consecutiveFailures))), cancellationToken);
            }
        }
    }

    private async Task StartMemoryMonitorAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        while (_state.Status == ServiceStatus.Running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var memoryInUse in CreateApiClient().SubscribeMemoryAsync(cancellationToken))
                {
                    if (_state.Status != ServiceStatus.Running)
                    {
                        break;
                    }

                    consecutiveFailures = 0;
                    _state.MemoryInUse = memoryInUse;
                    MemoryUpdated?.Invoke(this, memoryInUse);
                }

                if (_state.Status == ServiceStatus.Running)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                consecutiveFailures++;
                if (consecutiveFailures == 1 || consecutiveFailures % 10 == 0)
                {
                    LogManager($"[WARN] Memory monitor error: {e.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, consecutiveFailures))), cancellationToken);
            }
        }
    }

    private string ReadLogMonitorLevel(string configPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(configPath));
            if (document.RootElement.TryGetProperty("log", out var logElement) &&
                logElement.ValueKind == JsonValueKind.Object &&
                logElement.TryGetProperty("level", out var levelElement) &&
                levelElement.ValueKind == JsonValueKind.String)
            {
                return SingBoxLogLevelHelper.Normalize(levelElement.GetString());
            }
        }
        catch (Exception ex)
        {
            LogManager($"[WARN] Failed to inspect config log level: {ex.Message}");
        }

        return LogMonitorFallbackLevel;
    }
}
