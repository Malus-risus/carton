using System.Net;
using System.Net.Http.Headers;
using carton.Core.Utilities;

namespace carton.Core.Services;

/// <summary>
/// Shared HttpClient instances to avoid socket exhaustion and reduce memory overhead.
/// </summary>
public static class HttpClientFactory
{
    private const string UserAgentHeaderName = "User-Agent";
    private static readonly TimeSpan LocalApiClientDisposeDelay = TimeSpan.FromSeconds(30);
    private static readonly object LocalApiSyncRoot = new();
    private static HttpClient _localApi = null!;
    private static string _appVersion = "1.0";
    public static string LocalApiAddress { get; private set; } = string.Empty;
    public static int LocalApiPort { get; private set; }
    public static string? LocalApiSecret { get; private set; }
    public static string LocalClashApiAddress { get; private set; } = string.Empty;
    public static int LocalClashApiPort { get; private set; }
    public static string? LocalClashApiSecret { get; private set; }
    public static string LocalNativeApiAddress { get; private set; } = string.Empty;
    public static int LocalNativeApiPort { get; private set; }
    public static string? LocalNativeApiSecret { get; private set; }

    static HttpClientFactory()
    {
        UpdateLocalApi("127.0.0.1", 9090, null);
        CartonApplicationInfo.SingBoxVersionChanged += OnSingBoxVersionChanged;
    }

    /// <summary>
    /// Call once at application startup to set the app version used in User-Agent.
    /// Must be called before External is first accessed.
    /// </summary>
    public static void Initialize(string appVersion)
    {
        if (!string.IsNullOrWhiteSpace(appVersion))
        {
            _appVersion = appVersion;
        }

        RefreshExternalUserAgent();
    }

    /// <summary>
    /// Client for the local sing-box / Clash API.
    /// </summary>
    public static HttpClient LocalApi => _localApi;

    public static void UpdateLocalApi(
        string host,
        int port,
        string? secret,
        int? clashApiPort = null,
        string? clashApiSecret = null)
    {
        var client = CreateLoopbackClient(TimeSpan.FromSeconds(5));
        client.BaseAddress = new Uri($"http://{host}:{port}/");

        if (!string.IsNullOrWhiteSpace(secret))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        var effectiveClashPort = clashApiPort.GetValueOrDefault(port);
        var effectiveClashSecret = clashApiSecret ?? secret;
        HttpClient? oldClient;
        lock (LocalApiSyncRoot)
        {
            oldClient = _localApi;
            LocalApiAddress = $"http://{host}:{port}";
            LocalApiPort = port;
            LocalApiSecret = string.IsNullOrWhiteSpace(secret) ? null : secret;
            LocalClashApiAddress = $"http://{host}:{effectiveClashPort}";
            LocalClashApiPort = effectiveClashPort;
            LocalClashApiSecret = string.IsNullOrWhiteSpace(effectiveClashSecret) ? null : effectiveClashSecret;
            _localApi = client;
        }

        if (oldClient != null)
        {
            _ = DisposeLocalApiClientLaterAsync(oldClient);
        }
    }

    private static async Task DisposeLocalApiClientLaterAsync(HttpClient client)
    {
        try
        {
            await Task.Delay(LocalApiClientDisposeDelay).ConfigureAwait(false);
            client.Dispose();
        }
        catch
        {
        }
    }

    public static void UpdateLocalNativeApi(string host, int port, string? secret)
    {
        if (port is <= 0 or > 65535)
        {
            ClearLocalNativeApi();
            return;
        }

        LocalNativeApiAddress = $"http://{host}:{port}";
        LocalNativeApiPort = port;
        LocalNativeApiSecret = string.IsNullOrWhiteSpace(secret) ? null : secret;
    }

    public static void ClearLocalNativeApi()
    {
        LocalNativeApiAddress = string.Empty;
        LocalNativeApiPort = 0;
        LocalNativeApiSecret = null;
    }

    public static HttpClient CreateLoopbackClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout
        };
    }

    /// <summary>
    /// Client for external requests (GitHub, remote config downloads, etc.).
    /// Lazy-created so that Initialize(appVersion) takes effect before first use.
    /// </summary>
    private static HttpClient? _external;
    public static HttpClient External => _external ??= CreateExternalClient();

    private static HttpClient CreateExternalClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        ConfigureExternalClient(client);
        return client;
    }

    public static HttpClient CreateExternalProxyClient(string host, int port)
    {
        // This is the proxy endpoint scheme, not the destination scheme:
        // HTTPS URLs still work here because HttpClient tunnels them via CONNECT
        // through the local mixed-port HTTP proxy.
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = new WebProxy($"http://{host}:{port}")
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        ConfigureExternalClient(client);
        return client;
    }

    private static void ConfigureExternalClient(HttpClient client)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(UserAgentHeaderName, BuildExternalUserAgent());
    }

    public static string DefaultUserAgent => BuildExternalUserAgent();

    private static string BuildExternalUserAgent()
    {
        var kernelVersion = CartonApplicationInfo.EffectiveSingBoxVersion;
        return $"carton/{_appVersion} (sing-box {kernelVersion}; sing-box/{kernelVersion})";
    }

    private static void OnSingBoxVersionChanged(string? _)
    {
        RefreshExternalUserAgent();
    }

    private static void RefreshExternalUserAgent()
    {
        var client = _external;
        if (client == null)
        {
            return;
        }

        client.DefaultRequestHeaders.Remove(UserAgentHeaderName);
        client.DefaultRequestHeaders.TryAddWithoutValidation(UserAgentHeaderName, BuildExternalUserAgent());
    }
}
