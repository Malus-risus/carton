using carton.Core.Services;
using Daemon;
using Xunit;

namespace carton.GUI.Tests.Services;

/// <summary>
/// Tests for the production connection-events merge engine
/// (SingBoxManager.ApplyConnectionEvents). The merge rules mirror the official
/// sing-box dashboard (src/api/daemon.ts): reset=true rebuilds, NEW upserts
/// (skipping closed connections reported by the initial snapshot), UPDATE adds
/// deltas, CLOSED drops. These tests call the production code directly - deleting
/// or breaking the real merge fails here.
/// </summary>
public sealed class SingBoxManagerStreamingTests
{
    private static Daemon.Connection NewConnection(string id, long uplink = 100, long downlink = 200, long closedAt = 0) => new()
    {
        Id = id,
        Inbound = "mixed-in",
        InboundType = "Mixed",
        Network = "tcp",
        Source = "127.0.0.1:50000",
        Destination = "example.com:443",
        Domain = "example.com",
        Outbound = "direct",
        CreatedAt = 1_700_000_000_000,
        ClosedAt = closedAt,
        UplinkTotal = uplink,
        DownlinkTotal = downlink
    };

    private static Dictionary<string, SingBoxManager.ConnectionSnapshotRow> Merge(
        Dictionary<string, SingBoxManager.ConnectionSnapshotRow> rows,
        params ConnectionEvent[] events) =>
        SingBoxManager.ApplyConnectionEvents(
            rows,
            new ConnectionEvents { Events = { events } });

    private static Dictionary<string, SingBoxManager.ConnectionSnapshotRow> ResetMerge(
        params ConnectionEvent[] events) =>
        SingBoxManager.ApplyConnectionEvents(
            new Dictionary<string, SingBoxManager.ConnectionSnapshotRow>(StringComparer.Ordinal),
            new ConnectionEvents { Reset = true, Events = { events } });

    [Fact]
    public void Apply_InitialSnapshotBuildsRows_AndSkipsClosedConnections()
    {
        var rows = ResetMerge(
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "a", Connection = NewConnection("a") },
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "closed", Connection = NewConnection("closed", closedAt: 12345) });

        Assert.Single(rows);
        Assert.True(rows.ContainsKey("a"));
    }

    [Fact]
    public void Apply_UpdateEventsAccumulateDeltas()
    {
        var rows = ResetMerge(
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "a", Connection = NewConnection("a", 100, 200) });

        rows = Merge(rows,
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventUpdate, Id = "a", UplinkDelta = 50, DownlinkDelta = 80 },
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventUpdate, Id = "a", UplinkDelta = 25, DownlinkDelta = 40 });

        var row = Assert.Single(rows.Values);
        Assert.Equal(175, row.UplinkTotal);
        Assert.Equal(320, row.DownlinkTotal);
    }

    [Fact]
    public void Apply_ClosedEventRemovesRow()
    {
        var rows = ResetMerge(
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "a", Connection = NewConnection("a") });

        rows = Merge(rows,
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventClosed, Id = "a", ClosedAt = 999 });

        Assert.Empty(rows);
    }

    [Fact]
    public void Apply_UpdateForUnknownIdIsIgnored()
    {
        var rows = new Dictionary<string, SingBoxManager.ConnectionSnapshotRow>(StringComparer.Ordinal);

        rows = Merge(rows,
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventUpdate, Id = "ghost", UplinkDelta = 10 });

        Assert.Empty(rows);
    }

    [Fact]
    public void Apply_ResetDropsPreviousRows()
    {
        var rows = ResetMerge(
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "a", Connection = NewConnection("a") });

        rows = SingBoxManager.ApplyConnectionEvents(
            rows,
            new ConnectionEvents
            {
                Reset = true,
                Events = { new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "b", Connection = NewConnection("b") } }
            });

        Assert.Single(rows);
        Assert.True(rows.ContainsKey("b"));
    }

    [Fact]
    public void Apply_NonResetMessageKeepsUnrelatedRows()
    {
        var rows = ResetMerge(
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "a", Connection = NewConnection("a") },
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventNew, Id = "b", Connection = NewConnection("b") });

        rows = Merge(rows,
            new ConnectionEvent { Type = ConnectionEventType.ConnectionEventUpdate, Id = "b", UplinkDelta = 1, DownlinkDelta = 1 });

        Assert.Equal(2, rows.Count);
        Assert.Equal(101, rows["b"].UplinkTotal);
        Assert.Equal(100, rows["a"].UplinkTotal);
    }
}
