using Daemon;
using carton.Core.Models;

namespace carton.Core.Services.SingBoxApi;

internal interface ISingBoxApiClient
{
    /// <summary>
    /// Raised when the kernel resets its log buffer. Consumers should clear their
    /// local log buffer to avoid duplicated history replays on reconnect.
    /// </summary>
    event EventHandler? LogsReset;

    Task<bool> IsReachableAsync();

    /// <summary>
    /// Returns (version, apiVersion) from the kernel's GetVersion RPC, or null when
    /// unreachable. apiVersion gates optional features (official dashboard gates:
    /// taildrop >= 4, OpenVPN/OpenConnect >= 3).
    /// </summary>
    Task<(string Version, int ApiVersion)?> GetServerVersionAsync();

    /// <summary>Last known daemon apiVersion (0 when unknown); refreshed by GetServerVersionAsync.</summary>
    int ApiVersion { get; }

    /// <summary>
    /// Raw deprecation warnings from the kernel (null when unavailable);
    /// see GetServerVersionAsync for the mapped model used by the UI layer.
    /// </summary>
    Task<DeprecatedWarnings?> GetDeprecatedWarningsAsync();
    Task<ApiModeConfigSnapshot?> GetModeConfigAsync();
    Task<bool> SetModeAsync(string mode);
    Task<List<OutboundGroup>> GetOutboundGroupsAsync();
    Task SelectOutboundAsync(string groupTag, string outboundTag);
    Task SetGroupExpandAsync(string groupTag, bool isExpand);

    /// <summary>
    /// Triggers a URL test on the kernel (fire-and-forget: results are pushed via the
    /// groups stream, not through this RPC's response).
    /// </summary>
    Task URLTestAsync(string outboundTag);

    /// <summary>
    /// Streams clash mode changes: an initial message with the current mode, then a
    /// push on every change (including changes made by other control clients).
    /// </summary>
    IAsyncEnumerable<Daemon.ClashMode> SubscribeClashModeStreamAsync(CancellationToken cancellationToken);
    /// <summary>
    /// Tests every node of a group and returns fresh delays keyed by item tag.
    /// NOTE: testUrl is accepted for signature compatibility only - the sing-box
    /// daemon URLTest RPC always uses the url configured on the group/outbound.
    /// </summary>
    Task<Dictionary<string, int>> RunGroupDelayTestAsync(string groupTag, string? testUrl = null, int timeoutMs = 5000);

    /// <summary>
    /// Tests the given outbounds and returns fresh delays keyed by tag.
    /// NOTE: testUrl is accepted for signature compatibility only - the sing-box
    /// daemon URLTest RPC always uses the url configured on the outbound.
    /// </summary>
    Task<Dictionary<string, int>> RunOutboundDelayTestsAsync(IEnumerable<string> outboundTags, string? testUrl = null, int timeoutMs = 5000);
    Task<List<ConnectionInfo>> GetConnectionsAsync();
    Task CloseConnectionAsync(string connectionId);
    Task CloseAllConnectionsAsync();
    IAsyncEnumerable<KernelLogEntry> SubscribeLogsAsync(string level, CancellationToken cancellationToken);

    /// <summary>
    /// Streams aggregated daemon status: one multiplexed stream feeding both the
    /// traffic (uplink/downlink rates + kernel-tracked totals) and memory updates.
    /// </summary>
    IAsyncEnumerable<Daemon.Status> SubscribeAggregatedStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Streams connection events (NEW/UPDATE/CLOSED with deltas). The first
    /// message is the full snapshot (reset=true, including recently closed connections).
    /// </summary>
    IAsyncEnumerable<ConnectionEvents> SubscribeConnectionEventsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Streams outbound group snapshots: an initial full snapshot followed by a fresh
    /// snapshot on every url test history update or selection change.
    /// </summary>
    IAsyncEnumerable<Groups> SubscribeGroupSnapshotsAsync(CancellationToken cancellationToken);
}
