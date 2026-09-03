namespace carton.Core.Services.SingBoxApi;

internal static class SingBoxApiClientFactory
{
    private static readonly object SyncRoot = new();
    private static SingBoxGrpcApiClient? _sharedClient;
    private static Action<string>? _log;

    /// <summary>
    /// Returns the shared gRPC API client. A single client is reused for every call so
    /// the underlying HTTP/2 channel (and its connection pool) is multiplexed instead of
    /// being re-created per API call / monitor reconnect; the client itself transparently
    /// rebuilds the channel when the API address changes.
    /// </summary>
    public static ISingBoxApiClient Create(Action<string>? log = null)
    {
        lock (SyncRoot)
        {
            if (log != null)
            {
                _log = log;
            }

            if (_sharedClient is not { } client)
            {
                _sharedClient = client = new SingBoxGrpcApiClient(_log);
            }

            return client;
        }
    }

    /// <summary>
    /// Returns the shared client without creating one; null when none exists yet.
    /// </summary>
    public static ISingBoxApiClient? Peek()
    {
        lock (SyncRoot)
        {
            return _sharedClient;
        }
    }

    /// <summary>
    /// Disposes the shared client (e.g. on manager shutdown). The next Create call
    /// lazily creates a fresh instance.
    /// </summary>
    public static void Reset()
    {
        lock (SyncRoot)
        {
            _sharedClient?.Dispose();
            _sharedClient = null;
            // Drop the log delegate with the client: it belongs to the (old) manager
            // instance and holding it would pin that manager's closure tree alive.
            _log = null;
        }
    }
}
