using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using carton.Core.Models;
using carton.Core.Utilities;

namespace carton.Core.Services;

public partial class SingBoxManager
{
    private const int MessageBufferTrimThreshold = 64 * 1024;
    private const int MessageBufferInitialCapacity = 4 * 1024;
    private const int MaxMonitorMessageBytes = 64 * 1024;
    private static readonly JsonReaderOptions MonitorJsonReaderOptions = new()
    {
        MaxDepth = 64
    };

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
        if (_logMonitorTask is { IsCompleted: false })
        {
        }
        else
        {
            _logMonitorTask = Task.Run(StartLogMonitorAsync);
        }

        if (_trafficMonitorTask is { IsCompleted: false })
        {
        }
        else
        {
            _trafficMonitorTask = Task.Run(StartTrafficMonitorAsync);
        }

        if (_memoryMonitorTask is { IsCompleted: false })
        {
            return;
        }

        _memoryMonitorTask = Task.Run(StartMemoryMonitorAsync);
    }

    private async Task StartLogMonitorAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        var stream = new MemoryStream(MessageBufferInitialCapacity);
        ClientWebSocket? webSocket = null;
        var skippingOversizedMessage = false;
        var consecutiveFailures = 0;
        var monitorLevel = _logMonitorLevel;
        var wsUri = BuildWebSocketUri($"logs?level={Uri.EscapeDataString(monitorLevel)}");
        LogManager($"[INFO] Log monitor subscribed at level: {monitorLevel}");

        try
        {
            while (_state.Status == ServiceStatus.Running)
            {
                try
                {
                    if (webSocket == null || webSocket.State != WebSocketState.Open)
                    {
                        webSocket?.Dispose();
                        webSocket = new ClientWebSocket();
                        webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                        var wsSecret = HttpClientFactory.LocalApiSecret;
                        if (!string.IsNullOrWhiteSpace(wsSecret))
                        {
                            webSocket.Options.SetRequestHeader("Authorization", $"Bearer {wsSecret}");
                        }

                        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await webSocket.ConnectAsync(wsUri, connectCts.Token);
                        StopStartupLogCapture();
                        consecutiveFailures = 0;
                        ResetMessageBuffer(stream);
                    }

                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseSocketSilentlyAsync(webSocket, "Carton log monitor reconnect");
                        webSocket.Dispose();
                        webSocket = null;
                        await Task.Delay(500);
                        continue;
                    }

                    if (result.Count > 0)
                    {
                        if (skippingOversizedMessage)
                        {
                            if (result.EndOfMessage)
                            {
                                skippingOversizedMessage = false;
                                ResetMessageBuffer(stream);
                            }

                            continue;
                        }

                        if (stream.Length + result.Count > MaxMonitorMessageBytes)
                        {
                            LogManager($"[WARN] Log monitor payload exceeded {MaxMonitorMessageBytes} bytes and was discarded");
                            skippingOversizedMessage = !result.EndOfMessage;
                            ResetMessageBuffer(stream);
                            continue;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        ResetMessageBuffer(stream);
                        continue;
                    }

                    if (stream.Length == 0)
                    {
                        continue;
                    }

                    var payload = stream.GetBuffer().AsMemory(0, (int)stream.Length);
                    var logEntry = TryParseClashLogMessage(payload);
                    ResetMessageBuffer(stream);
                    if (logEntry is { Message.Length: > 0 } entry)
                    {
                        LogKernel(entry);
                    }
                }
                catch (Exception e)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures == 1 || consecutiveFailures % 10 == 0)
                    {
                        LogManager($"[WARN] Log monitor error: {e.Message}");
                    }

                    if (webSocket != null)
                    {
                        await CloseSocketSilentlyAsync(webSocket, "Carton log monitor error");
                        webSocket.Dispose();
                        webSocket = null;
                    }

                    ResetMessageBuffer(stream);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, Math.Max(1, consecutiveFailures))));
                }
            }
        }
        finally
        {
            if (webSocket != null)
            {
                await CloseSocketSilentlyAsync(webSocket, "Carton log monitor stopped");
                webSocket.Dispose();
            }

            stream.Dispose();
            ArrayPool<byte>.Shared.Return(buffer);
            _logMonitorTask = null;
        }
    }

    private async Task StartTrafficMonitorAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        var stream = new MemoryStream();
        ClientWebSocket? webSocket = null;
        var skippingOversizedMessage = false;
        var wsUri = BuildWebSocketUri("traffic");

        try
        {
            while (_state.Status == ServiceStatus.Running)
            {
                try
                {
                    if (webSocket == null || webSocket.State != WebSocketState.Open)
                    {
                        webSocket?.Dispose();
                        webSocket = new ClientWebSocket();
                        webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                        var wsSecret = HttpClientFactory.LocalApiSecret;
                        if (!string.IsNullOrWhiteSpace(wsSecret))
                        {
                            webSocket.Options.SetRequestHeader("Authorization", $"Bearer {wsSecret}");
                        }
                        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await webSocket.ConnectAsync(wsUri, connectCts.Token);
                        ResetMessageBuffer(stream);
                    }

                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseSocketSilentlyAsync(webSocket, "Carton traffic monitor reconnect");
                        webSocket.Dispose();
                        webSocket = null;
                        await Task.Delay(500);
                        continue;
                    }

                    if (result.Count > 0)
                    {
                        if (skippingOversizedMessage)
                        {
                            if (result.EndOfMessage)
                            {
                                skippingOversizedMessage = false;
                                ResetMessageBuffer(stream);
                            }

                            continue;
                        }

                        if (stream.Length + result.Count > MaxMonitorMessageBytes)
                        {
                            LogManager($"[WARN] Traffic monitor payload exceeded {MaxMonitorMessageBytes} bytes and was discarded");
                            skippingOversizedMessage = !result.EndOfMessage;
                            ResetMessageBuffer(stream);
                            continue;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        ResetMessageBuffer(stream);
                        continue;
                    }

                    if (stream.Length == 0)
                    {
                        continue;
                    }

                    var payload = stream.GetBuffer().AsMemory(0, (int)stream.Length);
                    var traffic = TryParseTrafficSnapshot(payload);
                    ResetMessageBuffer(stream);
                    if (traffic == null)
                    {
                        continue;
                    }

                    _state.UploadSpeed = traffic.Uplink;
                    _state.DownloadSpeed = traffic.Downlink;
                    _state.TotalUpload += traffic.Uplink;
                    _state.TotalDownload += traffic.Downlink;
                    TrafficUpdated?.Invoke(this, traffic);
                }
                catch (Exception e)
                {
                    LogManager($"[WARN] Traffic monitor error: {e.Message}");
                    if (webSocket != null)
                    {
                        await CloseSocketSilentlyAsync(webSocket, "Carton traffic monitor error");
                        webSocket.Dispose();
                        webSocket = null;
                    }

                    ResetMessageBuffer(stream);
                    await Task.Delay(1000);
                }
            }
        }
        finally
        {
            if (webSocket != null)
            {
                await CloseSocketSilentlyAsync(webSocket, "Carton traffic monitor stopped");
                webSocket.Dispose();
            }

            stream.Dispose();
            ArrayPool<byte>.Shared.Return(buffer);
            _trafficMonitorTask = null;
        }
    }

    private async Task StartMemoryMonitorAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        var stream = new MemoryStream();
        ClientWebSocket? webSocket = null;
        var skippingOversizedMessage = false;
        var wsUri = BuildWebSocketUri("memory");

        try
        {
            while (_state.Status == ServiceStatus.Running)
            {
                try
                {
                    if (webSocket == null || webSocket.State != WebSocketState.Open)
                    {
                        webSocket?.Dispose();
                        webSocket = new ClientWebSocket();
                        webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                        var wsSecret = HttpClientFactory.LocalApiSecret;
                        if (!string.IsNullOrWhiteSpace(wsSecret))
                        {
                            webSocket.Options.SetRequestHeader("Authorization", $"Bearer {wsSecret}");
                        }

                        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await webSocket.ConnectAsync(wsUri, connectCts.Token);
                        ResetMessageBuffer(stream);
                    }

                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseSocketSilentlyAsync(webSocket, "Carton memory monitor reconnect");
                        webSocket.Dispose();
                        webSocket = null;
                        await Task.Delay(500);
                        continue;
                    }

                    if (result.Count > 0)
                    {
                        if (skippingOversizedMessage)
                        {
                            if (result.EndOfMessage)
                            {
                                skippingOversizedMessage = false;
                                ResetMessageBuffer(stream);
                            }

                            continue;
                        }

                        if (stream.Length + result.Count > MaxMonitorMessageBytes)
                        {
                            LogManager($"[WARN] Memory monitor payload exceeded {MaxMonitorMessageBytes} bytes and was discarded");
                            skippingOversizedMessage = !result.EndOfMessage;
                            ResetMessageBuffer(stream);
                            continue;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        ResetMessageBuffer(stream);
                        continue;
                    }

                    if (stream.Length == 0)
                    {
                        continue;
                    }

                    var payload = stream.GetBuffer().AsMemory(0, (int)stream.Length);
                    var memoryInUse = TryParseMemorySnapshot(payload);
                    ResetMessageBuffer(stream);
                    if (!memoryInUse.HasValue)
                    {
                        continue;
                    }

                    _state.MemoryInUse = memoryInUse.Value;
                    MemoryUpdated?.Invoke(this, memoryInUse.Value);
                }
                catch (Exception e)
                {
                    LogManager($"[WARN] Memory monitor error: {e.Message}");
                    if (webSocket != null)
                    {
                        await CloseSocketSilentlyAsync(webSocket, "Carton memory monitor error");
                        webSocket.Dispose();
                        webSocket = null;
                    }

                    ResetMessageBuffer(stream);
                    await Task.Delay(1000);
                }
            }
        }
        finally
        {
            if (webSocket != null)
            {
                await CloseSocketSilentlyAsync(webSocket, "Carton memory monitor stopped");
                webSocket.Dispose();
            }

            stream.Dispose();
            ArrayPool<byte>.Shared.Return(buffer);
            _memoryMonitorTask = null;
        }
    }

    private TrafficInfo? TryParseTrafficSnapshot(ReadOnlyMemory<byte> payload)
    {
        if (IsEmptyOrJsonWhitespace(payload.Span))
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(payload.Span, MonitorJsonReaderOptions);
            long? rootUplink = null;
            long? rootDownlink = null;
            long? nowUplink = null;
            long? nowDownlink = null;

            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                ReadTrafficObject(ref reader, out rootUplink, out rootDownlink, out nowUplink, out nowDownlink);
            }

            return new TrafficInfo
            {
                Uplink = rootUplink ?? nowUplink ?? 0,
                Downlink = rootDownlink ?? nowDownlink ?? 0
            };
        }
        catch (JsonException ex)
        {
            LogManager($"[WARN] Failed to parse traffic snapshot: {ex.Message}");
            return null;
        }
    }

    private long? TryParseMemorySnapshot(ReadOnlyMemory<byte> payload)
    {
        if (IsEmptyOrJsonWhitespace(payload.Span))
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(payload.Span, MonitorJsonReaderOptions);
            long? rootMemory = null;
            long? nowMemory = null;

            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                ReadMemoryObject(ref reader, out rootMemory, out nowMemory);
            }

            return rootMemory ?? nowMemory;
        }
        catch (JsonException ex)
        {
            LogManager($"[WARN] Failed to parse memory snapshot: {ex.Message}");
            return null;
        }
    }

    private KernelLogEntry? TryParseClashLogMessage(ReadOnlyMemory<byte> payload)
    {
        if (IsEmptyOrJsonWhitespace(payload.Span))
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(payload.Span, MonitorJsonReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            var level = "Info";
            string? message = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                if (reader.ValueTextEquals("type"u8))
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    level = ReadClashLogLevel(ref reader);
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        reader.Skip();
                    }
                    continue;
                }

                if (reader.ValueTextEquals("payload"u8))
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        message = reader.GetString();
                    }
                    else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        reader.Skip();
                    }

                    continue;
                }

                if (!reader.Read())
                {
                    return null;
                }

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            return new KernelLogEntry(level, message);
        }
        catch (JsonException ex)
        {
            LogManager($"[WARN] Failed to parse log monitor payload: {ex.Message}");
            return null;
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

    private static bool IsEmptyOrJsonWhitespace(ReadOnlySpan<byte> payload)
    {
        foreach (var value in payload)
        {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }

        return true;
    }

    private static void ReadTrafficObject(
        ref Utf8JsonReader reader,
        out long? rootUplink,
        out long? rootDownlink,
        out long? nowUplink,
        out long? nowDownlink)
    {
        rootUplink = null;
        rootDownlink = null;
        nowUplink = null;
        nowDownlink = null;
        var rootUplinkPriority = int.MaxValue;
        var rootDownlinkPriority = int.MaxValue;
        var nowUplinkPriority = int.MaxValue;
        var nowDownlinkPriority = int.MaxValue;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var uplinkPriority = GetTrafficUplinkPriority(ref reader);
            var downlinkPriority = GetTrafficDownlinkPriority(ref reader);
            var isNow = reader.ValueTextEquals("now"u8);

            if (!reader.Read())
            {
                return;
            }

            if (uplinkPriority >= 0)
            {
                if (uplinkPriority < rootUplinkPriority && TryGetLongValue(ref reader, out var value))
                {
                    rootUplink = value;
                    rootUplinkPriority = uplinkPriority;
                }
                else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
            else if (downlinkPriority >= 0)
            {
                if (downlinkPriority < rootDownlinkPriority && TryGetLongValue(ref reader, out var value))
                {
                    rootDownlink = value;
                    rootDownlinkPriority = downlinkPriority;
                }
                else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
            else if (isNow && reader.TokenType == JsonTokenType.StartObject)
            {
                ReadTrafficProperties(ref reader, ref nowUplink, ref nowDownlink, ref nowUplinkPriority, ref nowDownlinkPriority);
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
    }

    private static void ReadTrafficProperties(
        ref Utf8JsonReader reader,
        ref long? uplink,
        ref long? downlink,
        ref int uplinkPriority,
        ref int downlinkPriority)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var currentUplinkPriority = GetTrafficUplinkPriority(ref reader);
            var currentDownlinkPriority = GetTrafficDownlinkPriority(ref reader);
            if (!reader.Read())
            {
                return;
            }

            if (currentUplinkPriority >= 0 && currentUplinkPriority < uplinkPriority && TryGetLongValue(ref reader, out var uplinkValue))
            {
                uplink = uplinkValue;
                uplinkPriority = currentUplinkPriority;
            }
            else if (currentDownlinkPriority >= 0 && currentDownlinkPriority < downlinkPriority && TryGetLongValue(ref reader, out var downlinkValue))
            {
                downlink = downlinkValue;
                downlinkPriority = currentDownlinkPriority;
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
    }

    private static void ReadMemoryObject(ref Utf8JsonReader reader, out long? rootMemory, out long? nowMemory)
    {
        rootMemory = null;
        nowMemory = null;
        var rootMemoryPriority = int.MaxValue;
        var nowMemoryPriority = int.MaxValue;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var memoryPriority = GetMemoryPriority(ref reader);
            var isNow = reader.ValueTextEquals("now"u8);

            if (!reader.Read())
            {
                return;
            }

            if (memoryPriority >= 0)
            {
                if (memoryPriority < rootMemoryPriority && TryGetLongValue(ref reader, out var value))
                {
                    rootMemory = value;
                    rootMemoryPriority = memoryPriority;
                }
                else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
            else if (isNow && reader.TokenType == JsonTokenType.StartObject)
            {
                ReadMemoryProperties(ref reader, ref nowMemory, ref nowMemoryPriority);
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
    }

    private static void ReadMemoryProperties(ref Utf8JsonReader reader, ref long? memory, ref int memoryPriority)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var currentMemoryPriority = GetMemoryPriority(ref reader);
            if (!reader.Read())
            {
                return;
            }

            if (currentMemoryPriority >= 0 && currentMemoryPriority < memoryPriority && TryGetLongValue(ref reader, out var value))
            {
                memory = value;
                memoryPriority = currentMemoryPriority;
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
    }

    private static int GetTrafficUplinkPriority(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("uplink"u8)) return 0;
        if (reader.ValueTextEquals("up"u8)) return 1;
        if (reader.ValueTextEquals("upload"u8)) return 2;
        return -1;
    }

    private static int GetTrafficDownlinkPriority(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("downlink"u8)) return 0;
        if (reader.ValueTextEquals("down"u8)) return 1;
        if (reader.ValueTextEquals("download"u8)) return 2;
        return -1;
    }

    private static int GetMemoryPriority(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("inuse"u8)) return 0;
        if (reader.ValueTextEquals("inUse"u8)) return 1;
        if (reader.ValueTextEquals("memory"u8)) return 2;
        if (reader.ValueTextEquals("value"u8)) return 3;
        return -1;
    }

    private static string ReadClashLogLevel(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return "Info";
        }

        if (reader.ValueTextEquals("trace"u8) || reader.ValueTextEquals("debug"u8))
        {
            return "Debug";
        }

        if (reader.ValueTextEquals("warn"u8) || reader.ValueTextEquals("warning"u8))
        {
            return "Warn";
        }

        if (reader.ValueTextEquals("error"u8))
        {
            return "Error";
        }

        if (reader.ValueTextEquals("fatal"u8) || reader.ValueTextEquals("panic"u8))
        {
            return "Fatal";
        }

        return "Info";
    }

    private static bool TryGetLongValue(ref Utf8JsonReader reader, out long value)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out value))
            {
                return true;
            }

            if (reader.TryGetDouble(out var doubleValue))
            {
                value = (long)doubleValue;
                return true;
            }
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var span = reader.ValueSpan;
            if (Utf8Parser.TryParse(span, out long parsed, out var consumed) && consumed == span.Length)
            {
                value = parsed;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static void ResetMessageBuffer(MemoryStream stream)
    {
        stream.SetLength(0);
        if (stream.Capacity <= MessageBufferTrimThreshold)
        {
            return;
        }

        stream.Capacity = MessageBufferInitialCapacity;
    }
}
