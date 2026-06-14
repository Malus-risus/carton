using carton.Core.Models;

namespace carton.Core.Services.SingBoxApi;

internal interface ISingBoxApiClient
{
    Task<bool> IsReachableAsync();
    Task<ApiModeConfigSnapshot?> GetModeConfigAsync();
    Task<bool> SetModeAsync(string mode);
    Task<List<OutboundGroup>> GetOutboundGroupsAsync();
    Task SelectOutboundAsync(string groupTag, string outboundTag);
    Task<Dictionary<string, int>> RunGroupDelayTestAsync(string groupTag, string? testUrl = null, int timeoutMs = 5000);
    Task<Dictionary<string, int>> RunOutboundDelayTestsAsync(IEnumerable<string> outboundTags, string? testUrl = null, int timeoutMs = 5000);
    Task<List<ConnectionInfo>> GetConnectionsAsync();
    Task CloseConnectionAsync(string connectionId);
    Task CloseAllConnectionsAsync();
    IAsyncEnumerable<KernelLogEntry> SubscribeLogsAsync(string level, CancellationToken cancellationToken);
    IAsyncEnumerable<TrafficInfo> SubscribeTrafficAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<long> SubscribeMemoryAsync(CancellationToken cancellationToken);
}
