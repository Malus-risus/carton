using carton.Core.Models;
using carton.Core.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class ProfileManagerTests
{
    [Fact]
    public async Task CreateAsync_RuntimeOptionsPreferMixedInboundPort()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "carton-profile-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var configManager = new ConfigManager(baseDirectory);
            var profileManager = new ProfileManager(baseDirectory, configManager);
            var config = """
            {
              "log": {
                "level": "warn"
              },
              "inbounds": [
                {
                  "type": "socks",
                  "listen": "127.0.0.1",
                  "listen_port": 2080
                },
                {
                  "type": "mixed",
                  "listen": "0.0.0.0",
                  "listen_port": 7890,
                  "set_system_proxy": true
                }
              ]
            }
            """;

            var profile = await profileManager.CreateAsync(new Profile
            {
                Name = "mixed",
                Type = ProfileType.Local
            }, config);

            var options = await profileManager.GetRuntimeOptionsAsync(profile.Id);

            Assert.Equal(7890, options.InboundPort);
            Assert.True(options.AllowLanConnections);
            Assert.True(options.EnableSystemProxy);
            Assert.Equal("warn", options.LogLevel);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("::", true)]
    [InlineData("[::]", true)]
    [InlineData("192.168.1.8", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("localhost", false)]
    public async Task CreateAsync_RuntimeOptionsReadLanScopeFromListenAddress(string listen, bool expectedAllowLan)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "carton-profile-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var configManager = new ConfigManager(baseDirectory);
            var profileManager = new ProfileManager(baseDirectory, configManager);
            var config = $$"""
            {
              "inbounds": [
                {
                  "type": "mixed",
                  "listen": "{{listen}}",
                  "listen_port": 7890
                }
              ]
            }
            """;

            var profile = await profileManager.CreateAsync(new Profile
            {
                Name = "lan",
                Type = ProfileType.Local
            }, config);

            var options = await profileManager.GetRuntimeOptionsAsync(profile.Id);

            Assert.Equal(expectedAllowLan, options.AllowLanConnections);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }
}
