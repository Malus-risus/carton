using System.Diagnostics;
using System.Runtime.InteropServices;
using carton.Core.Models;

namespace carton.Core.Services;

public partial class SingBoxManager
{
    public Task<ApiModeConfigSnapshot?> GetModeConfigAsync()
        => CreateApiClient().GetModeConfigAsync();

    public Task<bool> SetModeAsync(string mode)
        => CreateApiClient().SetModeAsync(mode);

    public async Task<List<OutboundGroup>> GetOutboundGroupsAsync()
    {
        return await CreateApiClient().GetOutboundGroupsAsync();
    }

    public async Task SelectOutboundAsync(string groupTag, string outboundTag)
    {
        await CreateApiClient().SelectOutboundAsync(groupTag, outboundTag);
    }

    public async Task<Dictionary<string, int>> RunGroupDelayTestAsync(string groupTag, string? testUrl = null, int timeoutMs = 5000)
    {
        return await CreateApiClient().RunGroupDelayTestAsync(groupTag, testUrl, timeoutMs);
    }

    public async Task<Dictionary<string, int>> RunOutboundDelayTestsAsync(IEnumerable<string> outboundTags, string? testUrl = null, int timeoutMs = 5000)
    {
        return await CreateApiClient().RunOutboundDelayTestsAsync(outboundTags, testUrl, timeoutMs);
    }

    public async Task<List<ConnectionInfo>> GetConnectionsAsync()
    {
        return await CreateApiClient().GetConnectionsAsync();
    }

    public async Task CloseConnectionAsync(string connectionId)
    {
        await CreateApiClient().CloseConnectionAsync(connectionId);
    }

    public async Task CloseAllConnectionsAsync()
    {
        await CreateApiClient().CloseAllConnectionsAsync();
    }

    private async Task<bool> IsApiReachableAsync()
    {
        return await CreateApiClient().IsReachableAsync();
    }

    private async Task<int?> TryFindProcessPidByApiPortAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = "-ano -p tcp",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = raw.Trim();
                    if (!line.Contains($":{_apiPort}", StringComparison.Ordinal) ||
                        !line.Contains("LISTEN", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[^1], out var pid) && pid > 0)
                    {
                        return pid;
                    }
                }

                return null;
            }

            using var unixProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lsof",
                    Arguments = $"-nP -iTCP:{_apiPort} -sTCP:LISTEN -t",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            unixProcess.Start();
            var pidOutput = (await unixProcess.StandardOutput.ReadToEndAsync()).Trim();
            await unixProcess.WaitForExitAsync();

            var firstLine = pidOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (int.TryParse(firstLine, out var unixPid) && unixPid > 0)
            {
                return unixPid;
            }
        }
        catch
        {
        }

        return null;
    }
}
