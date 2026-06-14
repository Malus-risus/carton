using carton.GUI.Serialization;
using carton.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace carton.GUI.Services;

internal sealed class SingBoxDashboardBootstrapService : IDisposable
{
    private const string DashboardOrigin = "https://sing-box-dashboard.sagernet.org";
    private const string DashboardServerId = "carton";
    internal const int PreferredPort = 9092;
    private const int MaxRequestHeaderBytes = 32 * 1024;
    private const int MaxConcurrentConnections = 32;
    private const int MaxOfficialRedirects = 5;
    private const int MaxCachedOfficialAssetBytes = 4 * 1024 * 1024;
    private const int MaxOfficialAssetCacheBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan OfficialIndexCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OfficialAssetCacheDuration = TimeSpan.FromHours(12);
    private static readonly Uri DashboardOriginUri = new(DashboardOrigin);
    private static readonly object SyncRoot = new();
    private static SingBoxDashboardBootstrapService? _instance;

    private readonly TcpListener _listener;
    private readonly object _cacheSyncRoot = new();
    private readonly Dictionary<string, OfficialAssetCacheEntry> _assetCache = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionSlots = new(MaxConcurrentConnections, MaxConcurrentConnections);
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        BaseAddress = DashboardOriginUri,
        Timeout = TimeSpan.FromSeconds(15)
    };
    private Action<string>? _log;
    private Task? _acceptLoop;
    private string? _officialIndexHtml;
    private DateTimeOffset _officialIndexExpiresAtUtc;
    private int _assetCacheBytes;
    private DashboardApiServerSnapshot _apiServer = new(0, string.Empty);
    private bool _disposed;

    private SingBoxDashboardBootstrapService(Action<string>? log, int[] excludedPorts)
    {
        _log = log;
        _listener = LoopbackPortAllocator.StartAvailableListener(PreferredPort, excludedPorts);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Origin = $"http://127.0.0.1:{Port}";
        Url = $"{Origin}/";
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }
    public string Origin { get; }
    public string Url { get; }

    public static SingBoxDashboardBootstrapEndpoint Configure(
        int apiPort,
        string? apiSecret,
        Action<string>? log = null,
        params int[] excludedPorts)
    {
        lock (SyncRoot)
        {
            if (_instance is not { _disposed: false } ||
                ContainsPort(excludedPorts, _instance.Port))
            {
                _instance?.Dispose();
                _instance = new SingBoxDashboardBootstrapService(log, excludedPorts);
            }

            _instance.UpdateLogger(log);
            _instance.UpdateServer(apiPort, apiSecret);
            return new SingBoxDashboardBootstrapEndpoint(_instance.Origin, _instance.Url);
        }
    }

    private void UpdateLogger(Action<string>? log)
    {
        if (log != null)
        {
            _log = log;
        }
    }

    private void UpdateServer(int apiPort, string? apiSecret)
    {
        var normalizedSecret = string.IsNullOrWhiteSpace(apiSecret) ? string.Empty : apiSecret;
        Interlocked.Exchange(ref _apiServer, new DashboardApiServerSnapshot(apiPort, normalizedSecret));
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                try
                {
                    await _connectionSlots.WaitAsync(_cts.Token);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }

                _ = HandleClientWithSlotAsync(client, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            LogWarning($"[WARN] sing-box dashboard bootstrap stopped: {ex.Message}");
        }
    }

    private async Task HandleClientWithSlotAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(client, cancellationToken);
        }
        finally
        {
            _connectionSlots.Release();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientScope = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        NetworkStream? stream = null;
        try
        {
            stream = client.GetStream();
            var request = await ReadRequestAsync(stream, timeout.Token);
            if (request == null)
            {
                return;
            }

            if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextResponseAsync(stream, HttpStatusCode.MethodNotAllowed, "Method Not Allowed", timeout.Token);
                return;
            }

            if (IsIndexPath(request.Target))
            {
                var body = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase)
                    ? Array.Empty<byte>()
                    : await BuildIndexAsync(timeout.Token);
                await WriteResponseAsync(
                    stream,
                    HttpStatusCode.OK,
                    "text/html; charset=utf-8",
                    body,
                    noStore: true,
                    timeout.Token);
                return;
            }

            await ProxyOfficialAssetAsync(stream, request, timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try
            {
                LogWarning($"[WARN] sing-box dashboard bootstrap request failed: {ex.Message}");
                if (stream is { CanWrite: true })
                {
                    await WriteTextResponseAsync(stream, HttpStatusCode.BadGateway, $"Dashboard bootstrap failed: {ex.Message}", timeout.Token);
                }
            }
            catch
            {
            }
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private async Task<DashboardRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        await using var requestBytes = new MemoryStream();
        var headerEnd = -1;
        while (requestBytes.Length < MaxRequestHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                return null;
            }

            requestBytes.Write(buffer, 0, read);
            headerEnd = FindHeaderTerminator(requestBytes.GetBuffer(), checked((int)requestBytes.Length));
            if (headerEnd >= 0)
            {
                break;
            }
        }

        if (headerEnd < 0 || headerEnd + 4 > MaxRequestHeaderBytes)
        {
            return null;
        }

        var headerText = Encoding.ASCII.GetString(requestBytes.GetBuffer(), 0, headerEnd);
        var firstLineEnd = headerText.IndexOf("\r\n", StringComparison.Ordinal);
        if (firstLineEnd <= 0)
        {
            return null;
        }

        var parts = headerText[..firstLineEnd].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var target = NormalizeTarget(parts[1]);
        if (string.IsNullOrEmpty(target))
        {
            return null;
        }

        return new DashboardRequest(parts[0], target);
    }

    private async Task<byte[]> BuildIndexAsync(CancellationToken cancellationToken)
    {
        var html = await GetOfficialIndexHtmlAsync(cancellationToken);
        var injection = BuildStorageInjection();
        var moduleIndex = html.IndexOf("<script type=\"module\"", StringComparison.OrdinalIgnoreCase);
        if (moduleIndex >= 0)
        {
            html = html.Insert(moduleIndex, injection);
        }
        else
        {
            var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            html = headEnd >= 0 ? html.Insert(headEnd, injection) : injection + html;
        }

        return Encoding.UTF8.GetBytes(html);
    }

    private async Task<string> GetOfficialIndexHtmlAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_cacheSyncRoot)
        {
            if (_officialIndexHtml != null && _officialIndexExpiresAtUtc > now)
            {
                return _officialIndexHtml;
            }
        }

        using var response = await GetOfficialAsync("/", cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        lock (_cacheSyncRoot)
        {
            _officialIndexHtml = html;
            _officialIndexExpiresAtUtc = DateTimeOffset.UtcNow.Add(OfficialIndexCacheDuration);
        }

        return html;
    }

    private string BuildStorageInjection()
    {
        var apiServer = Volatile.Read(ref _apiServer);
        var state = new SingBoxDashboardServersState
        {
            Servers =
            [
                new SingBoxDashboardServer
                {
                    Id = DashboardServerId,
                    Name = "carton",
                    Url = $"127.0.0.1:{apiServer.ApiPort}",
                    Secret = apiServer.ApiSecret
                }
            ],
            ActiveId = DashboardServerId
        };
        var stateJson = JsonSerializer.Serialize(
            state,
            CartonGuiJsonContext.Default.SingBoxDashboardServersState).Replace("</", "<\\/", StringComparison.Ordinal);

        return $$"""
<script>
(function () {
  try {
    var key = "sing-box-dashboard.servers";
    var next = {{stateJson}};
    localStorage.setItem(key, JSON.stringify(next));
    localStorage.removeItem("sing-box-dashboard.server");
  } catch (e) {}
})();
</script>
""";
    }

    private async Task ProxyOfficialAssetAsync(Stream stream, DashboardRequest request, CancellationToken cancellationToken)
    {
        if (TryGetOfficialAssetFromCache(request.Target, out var cachedAsset))
        {
            if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseHeadersAsync(
                    stream,
                    HttpStatusCode.OK,
                    cachedAsset.ContentType,
                    cachedAsset.Body.Length,
                    noStore: false,
                    cancellationToken);
                return;
            }

            await WriteResponseAsync(
                stream,
                HttpStatusCode.OK,
                cachedAsset.ContentType,
                cachedAsset.Body,
                noStore: false,
                cancellationToken);
            return;
        }

        using var response = await GetOfficialAsync(request.Target, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await WriteTextResponseAsync(stream, response.StatusCode, response.ReasonPhrase ?? "Not Found", cancellationToken);
            return;
        }

        var contentType = response.Content.Headers.ContentType?.ToString() ?? GuessContentType(request.Target);
        var contentLength = response.Content.Headers.ContentLength;
        if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseHeadersAsync(
                stream,
                response.StatusCode,
                contentType,
                contentLength.GetValueOrDefault(),
                noStore: false,
                cancellationToken);
            return;
        }

        if (ShouldCacheOfficialAsset(request.Target, contentLength))
        {
            var cacheableBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            StoreOfficialAssetInCache(request.Target, contentType, cacheableBody);
            await WriteResponseAsync(stream, response.StatusCode, contentType, cacheableBody, noStore: false, cancellationToken);
            return;
        }

        if (contentLength.HasValue)
        {
            await WriteResponseHeadersAsync(
                stream,
                response.StatusCode,
                contentType,
                contentLength.Value,
                noStore: false,
                cancellationToken);
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await responseStream.CopyToAsync(stream, cancellationToken);
            return;
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        await WriteResponseAsync(stream, response.StatusCode, contentType, body, noStore: false, cancellationToken);
    }

    private bool TryGetOfficialAssetFromCache(string target, out OfficialAssetCacheEntry asset)
    {
        asset = null!;
        if (!IsCacheableOfficialAssetTarget(target))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        lock (_cacheSyncRoot)
        {
            if (!_assetCache.TryGetValue(target, out var cachedAsset))
            {
                return false;
            }

            if (cachedAsset.ExpiresAtUtc <= now)
            {
                RemoveOfficialAssetFromCache(target);
                return false;
            }

            cachedAsset.LastAccessUtc = now;
            asset = cachedAsset;
            return true;
        }
    }

    private void StoreOfficialAssetInCache(string target, string contentType, byte[] body)
    {
        if (!IsCacheableOfficialAssetTarget(target) || body.Length > MaxCachedOfficialAssetBytes)
        {
            return;
        }

        lock (_cacheSyncRoot)
        {
            RemoveOfficialAssetFromCache(target);
            var now = DateTimeOffset.UtcNow;
            _assetCache[target] = new OfficialAssetCacheEntry(
                body,
                contentType,
                now.Add(OfficialAssetCacheDuration),
                now);
            _assetCacheBytes += body.Length;
            TrimOfficialAssetCache(now);
        }
    }

    private void TrimOfficialAssetCache(DateTimeOffset now)
    {
        var expiredKeys = new List<string>();
        foreach (var pair in _assetCache)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                expiredKeys.Add(pair.Key);
            }
        }

        for (var i = 0; i < expiredKeys.Count; i++)
        {
            RemoveOfficialAssetFromCache(expiredKeys[i]);
        }

        while (_assetCacheBytes > MaxOfficialAssetCacheBytes && _assetCache.Count > 0)
        {
            string? oldestKey = null;
            var oldestAccess = DateTimeOffset.MaxValue;
            foreach (var pair in _assetCache)
            {
                if (pair.Value.LastAccessUtc < oldestAccess)
                {
                    oldestKey = pair.Key;
                    oldestAccess = pair.Value.LastAccessUtc;
                }
            }

            if (oldestKey == null)
            {
                break;
            }

            RemoveOfficialAssetFromCache(oldestKey);
        }
    }

    private void RemoveOfficialAssetFromCache(string target)
    {
        if (!_assetCache.TryGetValue(target, out var asset))
        {
            return;
        }

        _assetCache.Remove(target);
        _assetCacheBytes -= asset.Body.Length;
        if (_assetCacheBytes < 0)
        {
            _assetCacheBytes = 0;
        }
    }

    private static bool ShouldCacheOfficialAsset(string target, long? contentLength)
    {
        if (!IsCacheableOfficialAssetTarget(target))
        {
            return false;
        }

        return !contentLength.HasValue || contentLength.Value <= MaxCachedOfficialAssetBytes;
    }

    private static bool IsCacheableOfficialAssetTarget(string target)
    {
        var path = StripQuery(target);
        return path.StartsWith("/assets/", StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage> GetOfficialAsync(string target, CancellationToken cancellationToken)
    {
        var uri = BuildOfficialUri(target);
        for (var redirectCount = 0; redirectCount <= MaxOfficialRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirectStatusCode(response.StatusCode))
            {
                return response;
            }

            if (response.Headers.Location == null)
            {
                return response;
            }

            var nextUri = ResolveOfficialRedirectUri(uri, response.Headers.Location);
            if (nextUri == null)
            {
                return response;
            }

            response.Dispose();
            uri = nextUri;
        }

        throw new InvalidOperationException("Official dashboard redirected too many times.");
    }

    private static Uri BuildOfficialUri(string target)
    {
        if (Uri.TryCreate(target, UriKind.Relative, out var relativeUri))
        {
            return new Uri(DashboardOriginUri, relativeUri);
        }

        return DashboardOriginUri;
    }

    private static Uri? ResolveOfficialRedirectUri(Uri currentUri, Uri location)
    {
        var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        return IsAllowedOfficialUri(nextUri) ? nextUri : null;
    }

    private static bool IsAllowedOfficialUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsAllowedOfficialHost(uri.Host);
    }

    private static bool IsAllowedOfficialHost(string host)
    {
        return string.Equals(host, "sing-box-dashboard.sagernet.org", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "dash.sing-box.app", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
    }

    private static bool IsIndexPath(string target)
    {
        var path = StripQuery(target);
        return path is "/" or "/index.html";
    }

    private static string NormalizeTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return "/";
        }

        if (!target.StartsWith("/", StringComparison.Ordinal) || IsProtocolRelativeTarget(target))
        {
            return string.Empty;
        }

        return target;
    }

    private static bool IsProtocolRelativeTarget(string target)
    {
        return target.Length >= 2 &&
               target[0] == '/' &&
               (target[1] == '/' || target[1] == '\\');
    }

    private static int FindHeaderTerminator(byte[] buffer, int length)
    {
        for (var i = 0; i <= length - 4; i++)
        {
            if (buffer[i] == '\r' &&
                buffer[i + 1] == '\n' &&
                buffer[i + 2] == '\r' &&
                buffer[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private void LogWarning(string message)
    {
        var log = _log;
        log?.Invoke(message);
    }

    private static bool ContainsPort(int[] ports, int port)
    {
        for (var i = 0; i < ports.Length; i++)
        {
            if (ports[i] == port)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteTextResponseAsync(
        Stream stream,
        HttpStatusCode statusCode,
        string text,
        CancellationToken cancellationToken)
    {
        await WriteResponseAsync(
            stream,
            statusCode,
            "text/plain; charset=utf-8",
            Encoding.UTF8.GetBytes(text),
            noStore: true,
            cancellationToken);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        HttpStatusCode statusCode,
        string contentType,
        byte[] body,
        bool noStore,
        CancellationToken cancellationToken)
    {
        await WriteResponseHeadersAsync(stream, statusCode, contentType, body.Length, noStore, cancellationToken);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, cancellationToken);
        }
    }

    private static async Task WriteResponseHeadersAsync(
        Stream stream,
        HttpStatusCode statusCode,
        string contentType,
        long contentLength,
        bool noStore,
        CancellationToken cancellationToken)
    {
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ")
            .Append((int)statusCode)
            .Append(' ')
            .Append(statusCode)
            .Append("\r\n")
            .Append("Content-Type: ")
            .Append(contentType)
            .Append("\r\n")
            .Append("Content-Length: ")
            .Append(Math.Max(0, contentLength))
            .Append("\r\n")
            .Append(noStore ? "Cache-Control: no-store\r\n" : "Cache-Control: public, max-age=300\r\n")
            .Append("Connection: close\r\n")
            .Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
        await stream.WriteAsync(headerBytes, cancellationToken);
    }

    private static string GuessContentType(string target)
    {
        var path = StripQuery(target);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".json" => "application/json; charset=utf-8",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }

    private static string StripQuery(string target)
    {
        var separator = target.IndexOf('?');
        return separator >= 0 ? target[..separator] : target;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _listener.Stop();
        _httpClient.Dispose();
        _cts.Dispose();
    }

    private sealed record DashboardRequest(string Method, string Target);
    private sealed record DashboardApiServerSnapshot(int ApiPort, string ApiSecret);
}

internal sealed record SingBoxDashboardBootstrapEndpoint(string Origin, string Url);

internal sealed class SingBoxDashboardServersState
{
    public List<SingBoxDashboardServer> Servers { get; set; } = [];
    public string? ActiveId { get; set; }
}

internal sealed class SingBoxDashboardServer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

internal sealed class OfficialAssetCacheEntry
{
    public OfficialAssetCacheEntry(
        byte[] body,
        string contentType,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset lastAccessUtc)
    {
        Body = body;
        ContentType = contentType;
        ExpiresAtUtc = expiresAtUtc;
        LastAccessUtc = lastAccessUtc;
    }

    public byte[] Body { get; }
    public string ContentType { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public DateTimeOffset LastAccessUtc { get; set; }
}
