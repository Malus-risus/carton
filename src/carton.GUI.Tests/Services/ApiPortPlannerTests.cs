using carton.Core.Services;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class ApiPortPlannerTests
{
    [Fact]
    public void Resolve_ReturnsDefaultPorts_WhenAutomaticDefaultsAreAvailable()
    {
        var basePort = FindConsecutiveFreePortRange(3);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 1,
            basePort + 2,
            hasConfiguredClashApiPort: false,
            configuredClashApiPort: 0,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort, plan.ClashApiPort);
        Assert.Equal(basePort + 1, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_MovesAutomaticClashPort_WhenDefaultClashPortIsOccupied()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedClash = ReservePort(basePort);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 2,
            basePort + 3,
            hasConfiguredClashApiPort: false,
            configuredClashApiPort: 0,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort + 1, plan.ClashApiPort);
        Assert.Equal(basePort + 2, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_MovesAutomaticNativePort_WhenDefaultNativePortIsOccupied()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedNative = ReservePort(basePort + 1);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 1,
            basePort + 3,
            hasConfiguredClashApiPort: false,
            configuredClashApiPort: 0,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort, plan.ClashApiPort);
        Assert.Equal(basePort + 2, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_AutomaticPortsAvoidBootstrapPort()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedClash = ReservePort(basePort);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 3,
            basePort + 1,
            hasConfiguredClashApiPort: false,
            configuredClashApiPort: 0,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort + 2, plan.ClashApiPort);
        Assert.Equal(basePort + 3, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_KeepsConfiguredClashPort_EvenWhenItIsOccupied()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedConfiguredClash = ReservePort(basePort);

        var plan = ApiPortPlanner.Resolve(
            basePort + 1,
            basePort + 2,
            basePort + 3,
            hasConfiguredClashApiPort: true,
            configuredClashApiPort: basePort,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort, plan.ClashApiPort);
        Assert.Equal(basePort + 2, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_KeepsConfiguredNativePort_EvenWhenItIsOccupied()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedConfiguredNative = ReservePort(basePort);

        var plan = ApiPortPlanner.Resolve(
            basePort + 1,
            basePort + 2,
            basePort + 3,
            hasConfiguredClashApiPort: false,
            configuredClashApiPort: 0,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: true,
            configuredNativeApiPort: basePort);

        Assert.Equal(basePort + 1, plan.ClashApiPort);
        Assert.Equal(basePort, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_KeepsBothConfiguredPorts_EvenWhenTheyAreOccupied()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedConfiguredClash = ReservePort(basePort);
        using var occupiedConfiguredNative = ReservePort(basePort + 1);

        var plan = ApiPortPlanner.Resolve(
            basePort + 2,
            basePort + 3,
            basePort + 3,
            hasConfiguredClashApiPort: true,
            configuredClashApiPort: basePort,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: true,
            configuredNativeApiPort: basePort + 1);

        Assert.Equal(basePort, plan.ClashApiPort);
        Assert.Equal(basePort + 1, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_KeepsEqualConfiguredPorts_WhenBothPortsAreConfigured()
    {
        var basePort = FindConsecutiveFreePortRange(3);

        var plan = ApiPortPlanner.Resolve(
            basePort + 1,
            basePort + 2,
            basePort + 2,
            hasConfiguredClashApiPort: true,
            configuredClashApiPort: basePort,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: true,
            configuredNativeApiPort: basePort);

        Assert.Equal(basePort, plan.ClashApiPort);
        Assert.Equal(basePort, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_MovesAutomaticNativePort_WhenConfiguredClashUsesDefaultNativePort()
    {
        var basePort = FindConsecutiveFreePortRange(4);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 1,
            basePort + 3,
            hasConfiguredClashApiPort: true,
            configuredClashApiPort: basePort + 1,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort + 1, plan.ClashApiPort);
        Assert.Equal(basePort + 2, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_MovesAutomaticClashPort_WhenConfiguredNativeUsesDefaultClashPort()
    {
        var basePort = FindConsecutiveFreePortRange(4);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 2,
            basePort + 3,
            hasConfiguredClashApiPort: false,
            configuredClashApiPort: 0,
            enableNativeApi: true,
            hasConfiguredNativeApiPort: true,
            configuredNativeApiPort: basePort);

        Assert.Equal(basePort + 1, plan.ClashApiPort);
        Assert.Equal(basePort, plan.NativeApiPort);
    }

    private static int FindConsecutiveFreePortRange(int count)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var candidate = FindFreePort();
            if (candidate <= 0 || candidate + count - 1 > 65535)
            {
                continue;
            }

            if (ArePortsAvailable(candidate, count))
            {
                return candidate;
            }
        }

        for (var candidate = 10000; candidate <= 60000; candidate++)
        {
            if (candidate + count - 1 > 65535)
            {
                break;
            }

            if (ArePortsAvailable(candidate, count))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No consecutive loopback ports are available for the test.");
    }

    private static int FindFreePort()
    {
        using var listener = ReservePort(0);
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool ArePortsAvailable(int startPort, int count)
    {
        var listeners = new List<TcpListener>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                listeners.Add(ReservePort(startPort + i));
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            for (var i = 0; i < listeners.Count; i++)
            {
                listeners[i].Stop();
            }
        }
    }

    private static TcpListener ReservePort(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return listener;
    }
}
