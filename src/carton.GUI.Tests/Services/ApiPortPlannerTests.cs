using carton.Core.Services;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class ApiPortPlannerTests
{
    [Fact]
    public void Resolve_ReturnsDefaultPort_WhenItIsAvailable()
    {
        var basePort = FindConsecutiveFreePortRange(2);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 1,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_MovesNativePort_WhenDefaultPortIsOccupied()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedDefault = ReservePort(basePort);
        using var occupiedNext = ReservePort(basePort + 1);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 2,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        // Ports 0 and 1 are held and port 2 is the excluded bootstrap port:
        // the planner must skip all three and return the next candidate. Holding the
        // ports ourselves keeps the candidate space fully deterministic.
        Assert.Equal(basePort + 3, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_AutomaticPortAvoidsBootstrapPort()
    {
        var basePort = FindConsecutiveFreePortRange(4);
        using var occupiedDefault = ReservePort(basePort);
        // Deterministic: bootstrap port is excluded AND its neighbour is held, so the
        // only valid automatic candidate is basePort + 3.
        using var occupiedAfterBootstrap = ReservePort(basePort + 2);

        var plan = ApiPortPlanner.Resolve(
            basePort,
            basePort + 1,
            hasConfiguredNativeApiPort: false,
            configuredNativeApiPort: 0);

        Assert.Equal(basePort + 3, plan.NativeApiPort);
    }

    [Fact]
    public void Resolve_KeepsConfiguredPort_EvenWhenItIsOccupied()
    {
        var basePort = FindConsecutiveFreePortRange(3);
        using var occupiedConfigured = ReservePort(basePort);

        var plan = ApiPortPlanner.Resolve(
            basePort + 1,
            basePort + 2,
            hasConfiguredNativeApiPort: true,
            configuredNativeApiPort: basePort);

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
