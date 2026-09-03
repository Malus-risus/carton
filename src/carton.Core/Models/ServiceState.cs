namespace carton.Core.Models;

public enum ServiceStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error
}

public class ServiceState
{
    public ServiceStatus Status { get; set; } = ServiceStatus.Stopped;
    public string StatusText => Status switch
    {
        ServiceStatus.Stopped => "Stopped",
        ServiceStatus.Starting => "Starting...",
        ServiceStatus.Running => "Running",
        ServiceStatus.Stopping => "Stopping...",
        ServiceStatus.Error => "Error",
        _ => "Unknown"
    };
    public long UploadSpeed { get; set; }
    public long DownloadSpeed { get; set; }
    public long TotalUpload { get; set; }
    public long TotalDownload { get; set; }
    public int ConnectionCount { get; set; }
    public long MemoryInUse { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartTime { get; set; }
}

/// <summary>
/// Immutable snapshot of the tracked connection list, updated from the
/// sing-box gRPC SubscribeConnections incremental stream.
/// </summary>
public sealed class ConnectionsSnapshot
{
    public static readonly ConnectionsSnapshot Empty = new(
        Array.Empty<ConnectionInfo>(),
        activeCount: 0,
        totalUplink: 0,
        totalDownlink: 0);

    public ConnectionsSnapshot(IReadOnlyList<ConnectionInfo> activeConnections, int activeCount, long totalUplink, long totalDownlink)
    {
        ActiveConnections = activeConnections;
        ActiveCount = activeCount;
        TotalUplink = totalUplink;
        TotalDownlink = totalDownlink;
    }

    /// <summary>Connections that are currently open (closed ones excluded).</summary>
    public IReadOnlyList<ConnectionInfo> ActiveConnections { get; }

    public int ActiveCount { get; }

    /// <summary>Cumulative uplink bytes across the tracked active connections.</summary>
    public long TotalUplink { get; }

    /// <summary>Cumulative downlink bytes across the tracked active connections.</summary>
    public long TotalDownlink { get; }
}

/// <summary>
/// A configuration deprecation warning reported by the kernel
/// (GetDeprecatedWarnings): what is deprecated, since when, when it will be
/// removed and where to migrate.
/// </summary>
public sealed class DeprecatedConfigWarning
{
    public string Message { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DeprecatedVersion { get; init; } = string.Empty;
    public string ScheduledVersion { get; init; } = string.Empty;
    public string MigrationLink { get; init; } = string.Empty;
    public bool Impending { get; init; }
}

/// <summary>
/// Immutable snapshot of the outbound groups, updated from the sing-box gRPC
/// SubscribeGroups stream (initial full snapshot + pushes on every url test
/// history / selection change).
/// </summary>
public sealed class GroupsSnapshot
{
    public static readonly GroupsSnapshot Empty = new(Array.Empty<OutboundGroup>());

    public GroupsSnapshot(IReadOnlyList<OutboundGroup> groups)
    {
        Groups = groups;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<OutboundGroup> Groups { get; }

    /// <summary>When this snapshot was pushed by the kernel (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>True when the snapshot is recent enough to serve reads directly.</summary>
    /// <remarks>Empty (never-delivered) snapshots report as stale by construction.</remarks>
    public bool IsFresh(TimeSpan maxAge) => Groups.Count > 0 && CreatedAt + maxAge > DateTimeOffset.UtcNow;
}

public class TrafficInfo
{
    public long Uplink { get; set; }
    public long Downlink { get; set; }
}

public class ConnectionInfo
{
    public string Id { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string Inbound { get; set; } = string.Empty;
    public string InboundType { get; set; } = string.Empty;
    public string Process { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Network { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Outbound { get; set; } = string.Empty;
    public List<string> Chains { get; set; } = new();
    public long Upload { get; set; }
    public long Download { get; set; }
}

public class OutboundGroup
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Selected { get; set; } = string.Empty;
    public List<OutboundItem> Items { get; set; } = new();
}

public class OutboundItem
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    /// <summary>Unix seconds of the last successful URL test; 0 when never tested.</summary>
    public long UrlTestTime { get; set; }
    public int UrlTestDelay { get; set; }
}
