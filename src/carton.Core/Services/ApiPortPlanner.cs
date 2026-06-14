namespace carton.Core.Services;

public readonly record struct ApiPortPlan(int ClashApiPort, int NativeApiPort);

public static class ApiPortPlanner
{
    public static ApiPortPlan Resolve(
        int defaultClashApiPort,
        int defaultNativeApiPort,
        int preferredBootstrapPort,
        bool hasConfiguredClashApiPort,
        int configuredClashApiPort,
        bool enableNativeApi,
        bool hasConfiguredNativeApiPort,
        int configuredNativeApiPort)
    {
        var clashApiPort = hasConfiguredClashApiPort
            ? configuredClashApiPort
            : ResolveAutomaticClashPort(
                defaultClashApiPort,
                defaultNativeApiPort,
                preferredBootstrapPort,
                enableNativeApi,
                hasConfiguredNativeApiPort,
                configuredNativeApiPort);

        if (!enableNativeApi)
        {
            return new ApiPortPlan(clashApiPort, 0);
        }

        var nativeApiPort = hasConfiguredNativeApiPort
            ? configuredNativeApiPort
            : LoopbackPortAllocator.FindAvailablePort(
                defaultNativeApiPort,
                clashApiPort,
                preferredBootstrapPort);

        return new ApiPortPlan(clashApiPort, nativeApiPort);
    }

    private static int ResolveAutomaticClashPort(
        int defaultClashApiPort,
        int defaultNativeApiPort,
        int preferredBootstrapPort,
        bool enableNativeApi,
        bool hasConfiguredNativeApiPort,
        int configuredNativeApiPort)
    {
        if (!enableNativeApi)
        {
            return LoopbackPortAllocator.FindAvailablePort(defaultClashApiPort);
        }

        var excludedNativePort = hasConfiguredNativeApiPort
            ? configuredNativeApiPort
            : defaultNativeApiPort;
        return LoopbackPortAllocator.FindAvailablePort(
            defaultClashApiPort,
            excludedNativePort,
            preferredBootstrapPort);
    }
}
