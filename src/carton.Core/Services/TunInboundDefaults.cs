using System.Text.Json.Nodes;

namespace carton.Core.Services;

public static class TunInboundDefaults
{
    public static void Apply(JsonObject tunInbound, bool supportsIpv6)
    {
        ArgumentNullException.ThrowIfNull(tunInbound);

        if (tunInbound["address"] is null)
        {
            var addresses = new JsonArray((JsonNode)"172.18.0.1/30");
            if (supportsIpv6)
            {
                addresses.Add((JsonNode)"fdfe:dcba:9876::1/126");
            }

            tunInbound["address"] = addresses;
        }

        tunInbound["auto_route"] = true;
        tunInbound["strict_route"] = true;
    }
}
