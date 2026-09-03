namespace carton.Core.Services;

public readonly record struct ApiPortPlan(int NativeApiPort);

public static class ApiPortPlanner
{
    /// <summary>
    /// Resolves the listen port for the sing-box native API service.
    /// A port explicitly configured in the profile is kept as-is; otherwise a free
    /// loopback port is allocated, avoiding the dashboard bootstrap port.
    /// </summary>
    public static ApiPortPlan Resolve(
        int defaultNativeApiPort,
        int preferredBootstrapPort,
        bool hasConfiguredNativeApiPort,
        int configuredNativeApiPort)
    {
        var nativeApiPort = hasConfiguredNativeApiPort
            ? configuredNativeApiPort
            : LoopbackPortAllocator.FindAvailablePort(
                defaultNativeApiPort,
                preferredBootstrapPort);

        return new ApiPortPlan(nativeApiPort);
    }
}
