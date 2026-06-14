using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using carton.Core.Models;
using carton.Core.Serialization;

namespace carton.Core.Services.SingBoxApi;

internal sealed class ClashHttpApiClient : ISingBoxApiClient
{
    private const string DefaultDelayTestUrl = "https://www.gstatic.com/generate_204";
    private const int MaxConcurrentOutboundDelayTests = 16;
    private const int MaxMonitorMessageBytes = 64 * 1024;
    private readonly Action<string>? _log;
    private HttpClient HttpClient => HttpClientFactory.LocalApi;
    private string ApiAddress => HttpClientFactory.LocalApiAddress;

    public ClashHttpApiClient(Action<string>? log = null)
    {
        _log = log;
    }

    public async Task<bool> IsReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            using var response = await HttpClient.GetAsync($"{ApiAddress}/version", cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            return response.StatusCode is System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.MethodNotAllowed;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ApiModeConfigSnapshot?> GetModeConfigAsync()
    {
        try
        {
            using var response = await HttpClient.GetAsync("configs", HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"[WARN] Failed to fetch Clash config: HTTP {(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var snapshot = new ApiModeConfigSnapshot
            {
                Mode = ReadString(document.RootElement, "mode"),
                ModeList = new List<string>()
            };

            if (document.RootElement.TryGetProperty("mode-list", out var modeList) &&
                modeList.ValueKind == JsonValueKind.Array)
            {
                foreach (var mode in modeList.EnumerateArray())
                {
                    if (mode.ValueKind == JsonValueKind.String && mode.GetString() is { Length: > 0 } value)
                    {
                        snapshot.ModeList.Add(value);
                    }
                }
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[WARN] Failed to fetch Clash config: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SetModeAsync(string mode)
    {
        try
        {
            var body = new JsonObject
            {
                ["mode"] = mode
            };
            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), "configs")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };

            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"[WARN] Failed to change Clash mode: HTTP {(int)response.StatusCode} {response.StatusCode}");
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[WARN] Failed to change Clash mode: {ex.Message}");
            return false;
        }
    }

    public async Task<List<OutboundGroup>> GetOutboundGroupsAsync()
    {
        try
        {
            using var response = await HttpClient.GetAsync($"{ApiAddress}/proxies", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var groups = new List<OutboundGroup>();

            if (document.RootElement.TryGetProperty("proxies", out var proxiesElement) &&
                proxiesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var proxyProperty in proxiesElement.EnumerateObject())
                {
                    if (proxyProperty.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var proxy = proxyProperty.Value;
                    if (!proxy.TryGetProperty("all", out var allElement) ||
                        allElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var group = new OutboundGroup
                    {
                        Tag = proxyProperty.Name,
                        Type = ReadString(proxy, "type"),
                        Selected = ReadString(proxy, "now")
                    };

                    foreach (var item in allElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var itemTag = item.GetString() ?? string.Empty;
                        var itemType = string.Empty;
                        JsonElement itemProxy = default;

                        if (!string.IsNullOrWhiteSpace(itemTag) &&
                            proxiesElement.TryGetProperty(itemTag, out var itemProxyElement) &&
                            itemProxyElement.ValueKind == JsonValueKind.Object)
                        {
                            itemProxy = itemProxyElement;
                            itemType = ReadString(itemProxy, "type");
                        }

                        group.Items.Add(new OutboundItem
                        {
                            Tag = itemTag,
                            Type = itemType,
                            UrlTestDelay = ReadLatestDelay(itemProxy)
                        });
                    }

                    if (group.Items.Count > 0)
                    {
                        groups.Add(group);
                    }
                }
            }

            return groups;
        }
        catch
        {
            return new List<OutboundGroup>();
        }
    }

    public async Task SelectOutboundAsync(string groupTag, string outboundTag)
    {
        var request = new OutboundSelectionRequest { Name = outboundTag };
        var payload = JsonSerializer.Serialize(
            request,
            CartonCoreJsonContext.Default.OutboundSelectionRequest);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await HttpClient.PutAsync($"{ApiAddress}/proxies/{Uri.EscapeDataString(groupTag)}", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Dictionary<string, int>> RunGroupDelayTestAsync(string groupTag, string? testUrl = null, int timeoutMs = 5000)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(groupTag))
        {
            return result;
        }

        var urlParam = Uri.EscapeDataString(string.IsNullOrWhiteSpace(testUrl) ? DefaultDelayTestUrl : testUrl);

        try
        {
            var endpoint = $"{ApiAddress}/group/{Uri.EscapeDataString(groupTag)}/delay?timeout={timeoutMs}&url={urlParam}";
            using var response = await HttpClient.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var delay))
                {
                    result[property.Name] = delay;
                }
            }
        }
        catch
        {
        }

        return result;
    }

    public async Task<Dictionary<string, int>> RunOutboundDelayTestsAsync(IEnumerable<string> outboundTags, string? testUrl = null, int timeoutMs = 5000)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<string>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in outboundTags)
        {
            if (string.IsNullOrWhiteSpace(tag) || !seenTargets.Add(tag))
            {
                continue;
            }

            targets.Add(tag);
        }

        if (targets.Count == 0)
        {
            return result;
        }

        var workerCount = Math.Min(MaxConcurrentOutboundDelayTests, targets.Count);
        var tasks = new List<Task>(workerCount);
        var nextIndex = -1;
        var resultSyncRoot = new object();
        for (var i = 0; i < workerCount; i++)
        {
            tasks.Add(RunDelayTestWorkerAsync());
        }

        await Task.WhenAll(tasks);

        return result;

        async Task RunDelayTestWorkerAsync()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= targets.Count)
                {
                    return;
                }

                var tag = targets[index];
                var delay = await RunOutboundDelayTestAsync(tag, testUrl, timeoutMs);
                lock (resultSyncRoot)
                {
                    result[tag] = delay;
                }
            }
        }
    }

    public async Task<List<ConnectionInfo>> GetConnectionsAsync()
    {
        try
        {
            var connections = new List<ConnectionInfo>();
            using var response = await HttpClient.GetAsync($"{ApiAddress}/connections", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            if (!document.RootElement.TryGetProperty("connections", out var connectionsElement) ||
                connectionsElement.ValueKind != JsonValueKind.Array)
            {
                return connections;
            }

            foreach (var conn in connectionsElement.EnumerateArray())
            {
                if (conn.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var metadata = conn.TryGetProperty("metadata", out var metadataElement) &&
                               metadataElement.ValueKind == JsonValueKind.Object
                    ? metadataElement
                    : default;
                var hasMetadata = metadata.ValueKind == JsonValueKind.Object;
                var chains = conn.TryGetProperty("chains", out var chainsElement) ? chainsElement : default;

                var sourceIp = hasMetadata ? ReadString(metadata, "sourceIP") : string.Empty;
                var sourcePort = hasMetadata ? ReadString(metadata, "sourcePort") : string.Empty;
                var destinationIp = hasMetadata ? ReadString(metadata, "destinationIP") : string.Empty;
                var destinationPort = hasMetadata ? ReadString(metadata, "destinationPort") : string.Empty;
                var host = hasMetadata ? ReadString(metadata, "host") : string.Empty;

                connections.Add(new ConnectionInfo
                {
                    Id = ReadString(conn, "id"),
                    StartTime = ReadDateTime(conn, "start"),
                    Inbound = ReadString(conn, "inbound"),
                    Process = hasMetadata ? ReadString(metadata, "process") : string.Empty,
                    Ip = sourceIp,
                    Source = ComposeEndpoint(sourceIp, sourcePort),
                    Destination = ComposeDestination(host, destinationIp, destinationPort),
                    Domain = host,
                    Protocol = ResolveProtocol(conn, metadata),
                    Chains = ReadChains(chains),
                    Outbound = ResolveOutbound(conn, chains),
                    Upload = ReadInt64(conn, "upload"),
                    Download = ReadInt64(conn, "download")
                });
            }

            return connections;
        }
        catch
        {
            return new List<ConnectionInfo>();
        }
    }

    public async Task CloseConnectionAsync(string connectionId)
    {
        try
        {
            await HttpClient.DeleteAsync($"{ApiAddress}/connections/{Uri.EscapeDataString(connectionId)}");
        }
        catch
        {
        }
    }

    public async Task CloseAllConnectionsAsync()
    {
        try
        {
            await HttpClient.DeleteAsync($"{ApiAddress}/connections");
        }
        catch
        {
        }
    }

    public async IAsyncEnumerable<KernelLogEntry> SubscribeLogsAsync(
        string level,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var relativePath = $"logs?level={Uri.EscapeDataString(level)}";
        await foreach (var entry in ReadWebSocketLogEntriesAsync(relativePath, cancellationToken))
        {
            yield return entry;
        }
    }

    public async IAsyncEnumerable<TrafficInfo> SubscribeTrafficAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var document in ReadWebSocketJsonDocumentsAsync("traffic", cancellationToken))
        {
            using var _ = document;
            if (ParseTraffic(document.RootElement) is { } traffic)
            {
                yield return traffic;
            }
        }
    }

    public async IAsyncEnumerable<long> SubscribeMemoryAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var document in ReadWebSocketJsonDocumentsAsync("memory", cancellationToken))
        {
            using var _ = document;
            if (ParseMemory(document.RootElement) is { } memory)
            {
                yield return memory;
            }
        }
    }

    private async Task<int> RunOutboundDelayTestAsync(string tag, string? testUrl = null, int timeoutMs = 5000)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return -1;
        }

        var urlParam = Uri.EscapeDataString(string.IsNullOrWhiteSpace(testUrl) ? DefaultDelayTestUrl : testUrl);

        try
        {
            var endpoint = $"{ApiAddress}/proxies/{Uri.EscapeDataString(tag)}/delay?timeout={timeoutMs}&url={urlParam}";
            using var response = await HttpClient.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                return -1;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (document.RootElement.TryGetProperty("delay", out var delayElement) &&
                delayElement.ValueKind == JsonValueKind.Number &&
                delayElement.TryGetInt32(out var delay))
            {
                return delay;
            }
        }
        catch
        {
        }

        return -1;
    }

    private async IAsyncEnumerable<JsonDocument> ReadWebSocketJsonDocumentsAsync(
        string relativePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        await using var stream = new MemoryStream();
        using var webSocket = new ClientWebSocket();
        var skippingOversizedMessage = false;
        try
        {
            ConfigureMonitorWebSocket(webSocket);
            await webSocket.ConnectAsync(BuildWebSocketUri(relativePath), cancellationToken);
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                if (skippingOversizedMessage)
                {
                    stream.SetLength(0);
                    if (result.EndOfMessage)
                    {
                        skippingOversizedMessage = false;
                    }

                    continue;
                }

                if (result.Count > 0)
                {
                    if (stream.Length + result.Count > MaxMonitorMessageBytes)
                    {
                        stream.SetLength(0);
                        skippingOversizedMessage = !result.EndOfMessage;
                        continue;
                    }

                    stream.Write(buffer, 0, result.Count);
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text && stream.Length > 0)
                {
                    var document = TryParseJsonDocument(stream);
                    stream.SetLength(0);
                    if (document != null)
                    {
                        yield return document;
                    }
                }
                else
                {
                    stream.SetLength(0);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await CloseMonitorWebSocketAsync(webSocket);
        }
    }

    private async IAsyncEnumerable<KernelLogEntry> ReadWebSocketLogEntriesAsync(
        string relativePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        await using var stream = new MemoryStream();
        using var webSocket = new ClientWebSocket();
        var skippingOversizedMessage = false;
        try
        {
            ConfigureMonitorWebSocket(webSocket);
            await webSocket.ConnectAsync(BuildWebSocketUri(relativePath), cancellationToken);
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                if (skippingOversizedMessage)
                {
                    stream.SetLength(0);
                    if (result.EndOfMessage)
                    {
                        skippingOversizedMessage = false;
                    }

                    continue;
                }

                if (result.Count > 0)
                {
                    if (stream.Length + result.Count > MaxMonitorMessageBytes)
                    {
                        stream.SetLength(0);
                        skippingOversizedMessage = !result.EndOfMessage;
                        continue;
                    }

                    stream.Write(buffer, 0, result.Count);
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text &&
                    stream.Length > 0 &&
                    TryParseLogEntry(stream, out var entry))
                {
                    stream.SetLength(0);
                    yield return entry;
                }
                else
                {
                    stream.SetLength(0);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await CloseMonitorWebSocketAsync(webSocket);
        }
    }

    private static void ConfigureMonitorWebSocket(ClientWebSocket webSocket)
    {
        webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        if (!string.IsNullOrWhiteSpace(HttpClientFactory.LocalApiSecret))
        {
            webSocket.Options.SetRequestHeader("Authorization", $"Bearer {HttpClientFactory.LocalApiSecret}");
        }
    }

    private static async Task CloseMonitorWebSocketAsync(ClientWebSocket webSocket)
    {
        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Carton monitor stopped", CancellationToken.None);
            }
            catch
            {
            }
        }
    }

    private static JsonDocument? TryParseJsonDocument(MemoryStream stream)
    {
        try
        {
            return JsonDocument.Parse(stream.GetBuffer().AsMemory(0, checked((int)stream.Length)));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseLogEntry(MemoryStream stream, out KernelLogEntry entry)
    {
        entry = default;
        try
        {
            var reader = new Utf8JsonReader(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
            var level = "Info";
            string? message = null;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                if (reader.ValueTextEquals("type"u8))
                {
                    if (!reader.Read())
                    {
                        break;
                    }

                    level = ReadLogLevel(ref reader);
                    continue;
                }

                if (reader.ValueTextEquals("payload"u8))
                {
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        message = reader.GetString();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            entry = new KernelLogEntry(level, message);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Uri BuildWebSocketUri(string relativePath)
    {
        var pathAndQuery = relativePath.TrimStart('/');
        var query = string.Empty;
        var queryIndex = pathAndQuery.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = pathAndQuery[(queryIndex + 1)..];
            pathAndQuery = pathAndQuery[..queryIndex];
        }

        var builder = new UriBuilder(ApiAddress)
        {
            Scheme = ApiAddress.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = pathAndQuery,
            Query = query
        };
        return builder.Uri;
    }

    private static string ReadLogLevel(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return "Info";
        }

        return reader.HasValueSequence
            ? MapLogLevel(reader.GetString() ?? string.Empty)
            : MapLogLevel(reader.ValueSpan);
    }

    private static string MapLogLevel(ReadOnlySpan<byte> level)
    {
        return level.Length switch
        {
            5 when AsciiEqualsIgnoreCase(level, "trace"u8) ||
                   AsciiEqualsIgnoreCase(level, "debug"u8) => "Debug",
            4 when AsciiEqualsIgnoreCase(level, "warn"u8) => "Warn",
            7 when AsciiEqualsIgnoreCase(level, "warning"u8) => "Warn",
            5 when AsciiEqualsIgnoreCase(level, "error"u8) => "Error",
            5 when AsciiEqualsIgnoreCase(level, "fatal"u8) ||
                   AsciiEqualsIgnoreCase(level, "panic"u8) => "Fatal",
            _ => "Info"
        };
    }

    private static bool AsciiEqualsIgnoreCase(ReadOnlySpan<byte> value, ReadOnlySpan<byte> expected)
    {
        if (value.Length != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var actual = value[i];
            if (actual >= 'A' && actual <= 'Z')
            {
                actual = (byte)(actual + ('a' - 'A'));
            }

            if (actual != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    private static TrafficInfo? ParseTraffic(JsonElement root)
    {
        try
        {
            return new TrafficInfo
            {
                Uplink = ReadBestInt64(root, "uplink", "up", "upload"),
                Downlink = ReadBestInt64(root, "downlink", "down", "download")
            };
        }
        catch
        {
            return null;
        }
    }

    private static long? ParseMemory(JsonElement root)
    {
        try
        {
            return ReadBestInt64(root, "inuse", "inUse", "memory", "value");
        }
        catch
        {
            return null;
        }
    }

    private static string MapLogLevel(string level)
    {
        return level.Length switch
        {
            5 when string.Equals(level, "trace", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(level, "debug", StringComparison.OrdinalIgnoreCase) => "Debug",
            4 when string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) => "Warn",
            7 when string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase) => "Warn",
            5 when string.Equals(level, "error", StringComparison.OrdinalIgnoreCase) => "Error",
            5 when string.Equals(level, "fatal", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(level, "panic", StringComparison.OrdinalIgnoreCase) => "Fatal",
            _ => "Info"
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.ToString(),
            JsonValueKind.Object when property.TryGetProperty("name", out var nameProperty) &&
                                     nameProperty.ValueKind == JsonValueKind.String
                => nameProperty.GetString() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static int ReadLatestDelay(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("history", out var historyElement) ||
            historyElement.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var latestDelay = 0;
        foreach (var historyItem in historyElement.EnumerateArray())
        {
            if (historyItem.ValueKind == JsonValueKind.Object &&
                historyItem.TryGetProperty("delay", out var delayElement) &&
                delayElement.ValueKind == JsonValueKind.Number &&
                delayElement.TryGetInt32(out var delay))
            {
                latestDelay = delay > 0 ? delay : 0;
            }
        }

        return latestDelay;
    }

    private static long ReadBestInt64(JsonElement element, params string[] propertyNames)
    {
        if (TryReadBestInt64(element, propertyNames, out var value))
        {
            return value;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("now", out var nowElement) &&
            nowElement.ValueKind == JsonValueKind.Object)
        {
            if (TryReadBestInt64(nowElement, propertyNames, out value))
            {
                return value;
            }
        }

        return 0;
    }

    private static bool TryReadBestInt64(JsonElement element, string[] propertyNames, out long value)
    {
        for (var i = 0; i < propertyNames.Length; i++)
        {
            if (TryReadInt64(element, propertyNames[i], out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static long ReadInt64(JsonElement element, string propertyName)
    {
        return TryReadInt64(element, propertyName, out var value) ? value : 0;
    }

    private static bool TryReadInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var integerValue))
        {
            value = integerValue;
            return true;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var floatingValue))
        {
            value = (long)floatingValue;
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), out var parsedValue))
        {
            value = parsedValue;
            return true;
        }

        return false;
    }

    private static DateTime ReadDateTime(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return DateTime.Now;
        }

        if (property.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(property.GetString(), out var timestamp))
        {
            return timestamp;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var unixMilliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).LocalDateTime;
        }

        return DateTime.Now;
    }

    private static string ResolveProtocol(JsonElement connection, JsonElement metadata)
    {
        var protocol = ReadString(connection, "protocol");
        return string.IsNullOrWhiteSpace(protocol) ? ReadString(metadata, "network") : protocol;
    }

    private static string ResolveOutbound(JsonElement connection, JsonElement chains)
    {
        var outbound = ReadString(connection, "outbound");
        if (!string.IsNullOrWhiteSpace(outbound))
        {
            return outbound;
        }

        var tags = ReadChains(chains);
        return tags.Count > 0 ? string.Join(" -> ", tags) : string.Empty;
    }

    private static List<string> ReadChains(JsonElement chains)
    {
        var tags = new List<string>();
        if (chains.ValueKind != JsonValueKind.Array)
        {
            return tags;
        }

        foreach (var chain in chains.EnumerateArray())
        {
            if (chain.ValueKind == JsonValueKind.String && chain.GetString() is { Length: > 0 } tag)
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static string ComposeEndpoint(string address, string port)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(port) ? address : $"{address}:{port}";
    }

    private static string ComposeDestination(string host, string destinationIp, string port)
    {
        var target = string.IsNullOrWhiteSpace(host) ? destinationIp : host;
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(port) ? target : $"{target}:{port}";
    }
}
