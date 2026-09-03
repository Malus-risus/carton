using Daemon;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace carton.GUI.Tests.Services;

/// <summary>
/// In-process mock of the sing-box daemon StartedService gRPC API. It reproduces the
/// server side of the wire contract that carton's SingBoxGrpcApiClient relies on:
///  - Bearer secret auth on every RPC,
///  - GetVersion returns (version, apiVersion) without requiring a started service,
///  - SubscribeGroups sends an initial full snapshot, then pushes again on demand
///    (url test history updates / selection changes),
///  - SubscribeConnections sends an initial full snapshot with reset=true where
///    recently closed connections are reported as NEW events with ClosedAt set,
///    then UPDATE deltas and CLOSED events,
///  - URLTest is asynchronous: it completes later via the groups snapshot stream,
///  - SubscribeStatus ticks with the requested interval where the interval unit is
///    NANOSECONDS (Go time.Duration) - a wrong unit here reproduces the historic bug.
/// </summary>
public sealed class MockSingBoxServer : StartedService.StartedServiceBase
{
    public const string Secret = "test-secret";
    public const string ServerVersion = "1.14.0";
    public const int ServerApiVersion = 3;

    private readonly object _syncRoot = new();
    private readonly List<Group> _groups = new();
    private readonly List<Connection> _connections = new();
    private readonly Dictionary<string, Connection> _closed = new();
    private readonly List<string> _logLines = new();
    private readonly List<(string GroupTag, bool IsExpand)> _groupExpandCalls = new();
    private readonly List<string> _urlTestCalls = new();
    private readonly List<(string GroupTag, string OutboundTag)> _selectCalls = new();
    private readonly List<(string Version, int ApiVersion)> _versionRequests = new();
    private readonly List<long> _statusIntervalsNs = new();
    private readonly List<long> _connectionIntervalsNs = new();

    private int _urlTestCounter;

    public bool RequireSecret { get; set; } = true;

    /// <summary>
    /// Starts the mock daemon on a free loopback port over HTTP/2 (h2c), returning
    /// the port and the underlying web host.
    /// </summary>
    /// <summary>
    /// Starts the mock daemon on a free loopback port over HTTP/2 (h2c), returning
    /// the port and the underlying web host. Binding races with other tests grabbing
    /// ports, so a handful of retries with fresh ports is built in (an "Address in
    /// use" failure here would otherwise surface as a confusing gRPC Unavailable).
    /// Start() is synchronous and rethrows, so startup errors are loud, not swallowed.
    /// </summary>
    public static (int Port, WebApplication Host) Start(MockSingBoxServer service)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = GetFreePort();
            try
            {
                var host = BuildHost(service, port);
                // Await startup so bind failures surface HERE (loud, retryable) instead
                // of as a deferred "Address in use" on the first gRPC call.
                host.StartAsync().GetAwaiter().GetResult();
                return (port, host);
            }
            catch (Exception e)
            {
                lastError = e;
            }
        }

        throw new InvalidOperationException($"Failed to start mock sing-box daemon: {lastError?.Message}", lastError);
    }

    private static WebApplication BuildHost(MockSingBoxServer service, int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(port, listen => listen.Protocols = HttpProtocols.Http2);
        });
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(service);

        var host = builder.Build();
        host.MapGrpcService<MockSingBoxServer>();
        return host;
    }

    public void AddGroup(string tag, string type, string selected, params (string Tag, string Type, int Delay)[] items)
    {
        lock (_syncRoot)
        {
            var group = new Group { Tag = tag, Type = type, Selected = selected };
            foreach (var (itemTag, itemType, delay) in items)
            {
                group.Items.Add(new GroupItem { Tag = itemTag, Type = itemType, UrlTestDelay = delay, UrlTestTime = 1000 });
            }

            _groups.Add(group);
        }
    }

    public void AddConnection(string id, string outbound = "direct", long uplinkTotal = 0, long downlinkTotal = 0)
    {
        lock (_syncRoot)
        {
            _connections.Add(new Connection
            {
                Id = id,
                Inbound = "mixed-in",
                InboundType = "Mixed",
                Network = "tcp",
                Source = "127.0.0.1:50000",
                Destination = "example.com:443",
                Domain = "example.com",
                Outbound = outbound,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UplinkTotal = uplinkTotal,
                DownlinkTotal = downlinkTotal
            });
        }
    }

    public void CloseConnectionLocally(string id, long uplinkTotal)
    {
        lock (_syncRoot)
        {
            var connection = _connections.FirstOrDefault(c => c.Id == id);
            if (connection == null)
            {
                return;
            }

            connection.UplinkTotal = uplinkTotal;
            connection.ClosedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _connections.Remove(connection);
            _closed[id] = connection;
        }
    }

    public IReadOnlyList<(string GroupTag, string OutboundTag)> SelectCalls
    {
        get
        {
            lock (_syncRoot)
            {
                return _selectCalls.ToList();
            }
        }
    }

    public IReadOnlyList<(string GroupTag, bool IsExpand)> GroupExpandCalls
    {
        get
        {
            lock (_syncRoot)
            {
                return _groupExpandCalls.ToList();
            }
        }
    }

    public IReadOnlyList<string> UrlTestCalls
    {
        get
        {
            lock (_syncRoot)
            {
                return _urlTestCalls.ToList();
            }
        }
    }

    public IReadOnlyList<long> StatusIntervalsNs
    {
        get
        {
            lock (_syncRoot)
            {
                return _statusIntervalsNs.ToList();
            }
        }
    }

    public IReadOnlyList<long> ConnectionIntervalsNs
    {
        get
        {
            lock (_syncRoot)
            {
                return _connectionIntervalsNs.ToList();
            }
        }
    }

    public IReadOnlyList<(string Version, int ApiVersion)> VersionRequests
    {
        get
        {
            lock (_syncRoot)
            {
                return _versionRequests.ToList();
            }
        }
    }

    public override Task<Daemon.Version> GetVersion(Empty request, ServerCallContext context)
    {
        if (!IsAuthenticated(context))
        {
            lock (_syncRoot)
            {
                // Record the rejection so tests can assert the RPC reached the server
                // (vs. a client-side connection failure) and was rejected there.
                _versionRequests.Add(("unauthenticated", 0));
            }

            throw new RpcException(new Grpc.Core.Status(StatusCode.Unauthenticated, "invalid authorization"));
        }

        lock (_syncRoot)
        {
            _versionRequests.Add((ServerVersion, ServerApiVersion));
        }

        return Task.FromResult(new Daemon.Version { Version_ = ServerVersion, ApiVersion = ServerApiVersion });
    }

    private bool IsAuthenticated(ServerCallContext context)
    {
        if (!RequireSecret)
        {
            return true;
        }

        var header = context.RequestHeaders.FirstOrDefault(h =>
            string.Equals(h.Key, "authorization", StringComparison.OrdinalIgnoreCase));
        return header != null && string.Equals(header.Value, $"Bearer {Secret}", StringComparison.Ordinal);
    }

    public override async Task SubscribeGroups(Empty request, IServerStreamWriter<Groups> responseStream, ServerCallContext context)
    {
        AssertAuthenticated(context);
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await responseStream.WriteAsync(BuildSnapshot());
            // Mirror the kernel: a fresh snapshot is pushed after every url test
            // history update / selection change; idle periods push nothing.
            await Task.Delay(50, context.CancellationToken);
        }
    }

    public override Task<Empty> URLTest(URLTestRequest request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        lock (_syncRoot)
        {
            _urlTestCalls.Add(request.OutboundTag);
            // Mark fresh results; the client must observe them via a newer snapshot.
            foreach (var group in _groups)
            {
                if (!string.Equals(group.Tag, request.OutboundTag, StringComparison.OrdinalIgnoreCase) &&
                    group.Items.All(i => !string.Equals(i.Tag, request.OutboundTag, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var item in group.Items)
                {
                    item.UrlTestDelay = 55 + _urlTestCounter++;
                    item.UrlTestTime = 2000;
                }
            }

        }

        return Task.FromResult(new Empty());
    }

    public override Task<Empty> SelectOutbound(SelectOutboundRequest request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        lock (_syncRoot)
        {
            _selectCalls.Add((request.GroupTag, request.OutboundTag));
            var group = _groups.FirstOrDefault(g => string.Equals(g.Tag, request.GroupTag, StringComparison.OrdinalIgnoreCase));
            if (group != null)
            {
                group.Selected = request.OutboundTag;
            }
        }

        return Task.FromResult(new Empty());
    }

    public override Task<Empty> SetGroupExpand(SetGroupExpandRequest request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        lock (_syncRoot)
        {
            _groupExpandCalls.Add((request.GroupTag, request.IsExpand));
        }

        return Task.FromResult(new Empty());
    }

    public override async Task SubscribeConnections(SubscribeConnectionsRequest request, IServerStreamWriter<ConnectionEvents> responseStream, ServerCallContext context)
    {
        AssertAuthenticated(context);
        lock (_syncRoot)
        {
            _connectionIntervalsNs.Add(request.Interval);
        }

        var snapshotIds = new HashSet<string>();
        var initial = new ConnectionEvents { Reset = true };
        lock (_syncRoot)
        {
            foreach (var connection in _connections)
            {
                initial.Events.Add(new ConnectionEvent
                {
                    Type = ConnectionEventType.ConnectionEventNew,
                    Id = connection.Id,
                    Connection = connection.Clone()
                });
                snapshotIds.Add(connection.Id);
            }

            foreach (var closed in _closed.Values)
            {
                initial.Events.Add(new ConnectionEvent
                {
                    Type = ConnectionEventType.ConnectionEventNew,
                    Id = closed.Id,
                    Connection = closed.Clone()
                });
            }
        }

        await responseStream.WriteAsync(initial);

        var knownUplink = new Dictionary<string, long>();
        lock (_syncRoot)
        {
            foreach (var connection in _connections)
            {
                knownUplink[connection.Id] = connection.UplinkTotal;
            }
        }

        while (!context.CancellationToken.IsCancellationRequested)
        {
            await Task.Delay(50, context.CancellationToken);
            var events = new ConnectionEvents();
            lock (_syncRoot)
            {
                foreach (var connection in _connections)
                {
                    if (!knownUplink.TryGetValue(connection.Id, out var previous))
                    {
                        events.Events.Add(new ConnectionEvent
                        {
                            Type = ConnectionEventType.ConnectionEventNew,
                            Id = connection.Id,
                            Connection = connection.Clone()
                        });
                    }
                    else if (connection.UplinkTotal != previous)
                    {
                        events.Events.Add(new ConnectionEvent
                        {
                            Type = ConnectionEventType.ConnectionEventUpdate,
                            Id = connection.Id,
                            UplinkDelta = connection.UplinkTotal - previous
                        });
                    }

                    knownUplink[connection.Id] = connection.UplinkTotal;
                }

                foreach (var closedId in _closed.Keys)
                {
                    if (knownUplink.Remove(closedId))
                    {
                        events.Events.Add(new ConnectionEvent
                        {
                            Type = ConnectionEventType.ConnectionEventClosed,
                            Id = closedId,
                            ClosedAt = _closed[closedId].ClosedAt
                        });
                    }
                }
            }

            if (events.Events.Count > 0)
            {
                await responseStream.WriteAsync(events);
            }
        }
    }

    public override async Task SubscribeStatus(SubscribeStatusRequest request, IServerStreamWriter<Daemon.Status> responseStream, ServerCallContext context)
    {
        AssertAuthenticated(context);
        lock (_syncRoot)
        {
            _statusIntervalsNs.Add(request.Interval);
        }

        var tick = 0;
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await responseStream.WriteAsync(new Daemon.Status
            {
                Memory = 4096,
                TrafficAvailable = true,
                Uplink = 10,
                Downlink = 20,
                UplinkTotal = 10 * (tick + 1),
                DownlinkTotal = 20 * (tick + 1)
            });
            tick++;
            await Task.Delay(50, context.CancellationToken);
        }
    }

    public override async Task SubscribeLog(Empty request, IServerStreamWriter<Log> responseStream, ServerCallContext context)
    {
        AssertAuthenticated(context);
        var replay = new Log { Reset = true };
        lock (_syncRoot)
        {
            foreach (var line in _logLines)
            {
                replay.Messages.Add(new Log.Types.Message { Level = LogLevel.Info, Message_ = line });
            }
        }

        await responseStream.WriteAsync(replay);
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await Task.Delay(100, context.CancellationToken);
            await responseStream.WriteAsync(new Log
            {
                Messages = { new Log.Types.Message { Level = LogLevel.Info, Message_ = "live-line" } }
            });
        }
    }

    private string _currentClashMode = "rule";

    public override Task<ClashModeStatus> GetClashModeStatus(Empty request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        return Task.FromResult(new ClashModeStatus
        {
            CurrentMode = _currentClashMode,
            ModeList = { "rule", "global", "direct" }
        });
    }

    public override Task<Empty> SetClashMode(ClashMode request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        lock (_syncRoot)
        {
            _currentClashMode = request.Mode;
            _clashModeGate?.TrySetResult(true);
        }

        return Task.FromResult(new Empty());
    }

    private TaskCompletionSource<bool>? _clashModeGate;

    public override async Task SubscribeClashMode(Empty request, IServerStreamWriter<Daemon.ClashMode> responseStream, ServerCallContext context)
    {
        AssertAuthenticated(context);
        // Initial push carries the current mode; every subsequent change (including
        // SetClashMode from any client) pushes the new mode - mirroring the kernel.
        await responseStream.WriteAsync(new Daemon.ClashMode { Mode = _currentClashMode });
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var gate = _clashModeGate;
            if (gate == null)
            {
                lock (_syncRoot)
                {
                    _clashModeGate ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    gate = _clashModeGate;
                }
            }

            var completed = await Task.WhenAny(gate.Task, Task.Delay(500, context.CancellationToken));
            if (completed == gate.Task)
            {
                lock (_syncRoot)
                {
                    _clashModeGate = null;
                }

                await responseStream.WriteAsync(new Daemon.ClashMode { Mode = _currentClashMode });
            }
        }
    }

    public override Task<Empty> CloseConnection(CloseConnectionRequest request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        CloseConnectionLocally(request.Id, uplinkTotal: 0);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> CloseAllConnections(Empty request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        lock (_syncRoot)
        {
            foreach (var connection in _connections.ToList())
            {
                CloseConnectionLocally(connection.Id, uplinkTotal: connection.UplinkTotal);
            }
        }

        return Task.FromResult(new Empty());
    }

    public override Task<DeprecatedWarnings> GetDeprecatedWarnings(Empty request, ServerCallContext context)
    {
        AssertAuthenticated(context);
        return Task.FromResult(new DeprecatedWarnings
        {
            Warnings =
            {
                new DeprecatedWarning
                {
                    Message = "experimental.clash_api is deprecated",
                    Impending = true,
                    DeprecatedVersion = "1.14.0",
                    ScheduledVersion = "1.16.0",
                    MigrationLink = "https://sing-box.sagernet.org/deprecated/",
                    Description = "Migrate to the sing-box api service."
                }
            }
        });
    }

    private Groups BuildSnapshot()
    {
        lock (_syncRoot)
        {
            var snapshot = new Groups();
            foreach (var group in _groups)
            {
                snapshot.Group.Add(group.Clone());
            }

            return snapshot;
        }
    }

    private void AssertAuthenticated(ServerCallContext context)
    {
        if (!RequireSecret)
        {
            return;
        }

        var header = context.RequestHeaders.FirstOrDefault(h =>
            string.Equals(h.Key, "authorization", StringComparison.OrdinalIgnoreCase));
        if (header == null || !string.Equals(header.Value, $"Bearer {Secret}", StringComparison.Ordinal))
        {
            throw new RpcException(new Grpc.Core.Status(StatusCode.Unauthenticated, "invalid authorization"));
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListenerSingleton();
        return listener.Port;
    }
}

internal sealed class TcpListenerSingleton : IDisposable
{
    private readonly System.Net.Sockets.TcpListener _listener;

    public TcpListenerSingleton()
    {
        _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Server.Dispose();
    }
}
