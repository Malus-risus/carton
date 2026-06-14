using System.Net;
using System.Net.Sockets;

namespace carton.Core.Services;

public static class LoopbackPortAllocator
{
    private const int MaxSequentialProbeCount = 100;
    private const int MaxEphemeralAttempts = 8;

    public static int FindAvailablePort(int preferredPort, params int[] excludedPorts)
    {
        var firstCandidate = Math.Max(1, preferredPort);
        var lastCandidate = Math.Min(65535, firstCandidate + MaxSequentialProbeCount - 1);

        for (var candidate = firstCandidate; candidate <= lastCandidate; candidate++)
        {
            if (ContainsPort(excludedPorts, candidate))
            {
                continue;
            }

            if (IsPortAvailable(candidate))
            {
                return candidate;
            }
        }

        for (var attempt = 0; attempt < MaxEphemeralAttempts; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                if (listener.LocalEndpoint is IPEndPoint endpoint && !ContainsPort(excludedPorts, endpoint.Port))
                {
                    return endpoint.Port;
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        return ContainsPort(excludedPorts, preferredPort) ? firstCandidate : preferredPort;
    }

    public static TcpListener StartAvailableListener(int preferredPort, params int[] excludedPorts)
    {
        var firstCandidate = Math.Max(1, preferredPort);
        var lastCandidate = Math.Min(65535, firstCandidate + MaxSequentialProbeCount - 1);

        for (var candidate = firstCandidate; candidate <= lastCandidate; candidate++)
        {
            if (ContainsPort(excludedPorts, candidate))
            {
                continue;
            }

            var listener = new TcpListener(IPAddress.Loopback, candidate);
            try
            {
                listener.Start();
                return listener;
            }
            catch
            {
                listener.Stop();
            }
        }

        for (var attempt = 0; attempt < MaxEphemeralAttempts; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                if (listener.LocalEndpoint is IPEndPoint endpoint && !ContainsPort(excludedPorts, endpoint.Port))
                {
                    return listener;
                }
            }
            catch
            {
                listener.Stop();
                throw;
            }

            listener.Stop();
        }

        throw new InvalidOperationException("No available loopback port found.");
    }

    public static bool IsPortAvailable(int port)
    {
        if (port is <= 0 or > 65535)
        {
            return false;
        }

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsPort(int[] ports, int port)
    {
        if (port is <= 0 or > 65535)
        {
            return false;
        }

        for (var i = 0; i < ports.Length; i++)
        {
            if (ports[i] == port)
            {
                return true;
            }
        }

        return false;
    }
}
