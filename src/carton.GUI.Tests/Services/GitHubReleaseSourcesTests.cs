using System.Net;
using System.Text.Json;
using carton.Core.Models;
using carton.Core.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class GitHubReleaseSourcesTests
{
    [Fact]
    public async Task PreferredSource_UsesApiWhenApiCompletesWithinPreferredWait()
    {
        var apiRelease = CreateRelease("v1.2.0");
        var atomRelease = CreateRelease("v1.1.0");
        var source = new PreferredGitHubReleaseSource(
            new FakeReleaseSource(apiRelease, TimeSpan.FromMilliseconds(5)),
            new FakeReleaseSource(atomRelease, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(1));

        var release = await source.GetLatestReleaseAsync(wantPrerelease: false);

        Assert.Equal("v1.2.0", release?.Tag);
    }

    [Fact]
    public async Task PreferredSource_UsesFallbackWhenApiDoesNotCompleteWithinPreferredWait()
    {
        var apiRelease = CreateRelease("v1.2.0");
        var atomRelease = CreateRelease("v1.1.0");
        var source = new PreferredGitHubReleaseSource(
            new FakeReleaseSource(apiRelease, TimeSpan.FromMilliseconds(100)),
            new FakeReleaseSource(atomRelease, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(1));

        var release = await source.GetLatestReleaseAsync(wantPrerelease: false);

        Assert.Equal("v1.1.0", release?.Tag);
    }

    [Fact]
    public async Task PreferredSource_ApiOnlyStrategyDoesNotUseFallback()
    {
        var apiRelease = CreateRelease("v1.2.0");
        var fallbackSource = new CountingReleaseSource(CreateRelease("v1.1.0"), TimeSpan.Zero);
        var source = new PreferredGitHubReleaseSource(
            new FakeReleaseSource(apiRelease, TimeSpan.Zero),
            fallbackSource,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(1),
            new StaticGitHubUpdateCheckStrategyProvider(GitHubUpdateCheckStrategy.ApiOnly));

        var release = await source.GetLatestReleaseAsync(wantPrerelease: false);

        Assert.Equal("v1.2.0", release?.Tag);
        Assert.Equal(0, fallbackSource.CallCount);
    }

    [Fact]
    public async Task PreferredSource_CancelsFallbackWhenApiWins()
    {
        var fallbackSource = new CancellableReleaseSource(CreateRelease("v1.1.0"));
        var source = new PreferredGitHubReleaseSource(
            new FakeReleaseSource(CreateRelease("v1.2.0"), TimeSpan.Zero),
            fallbackSource,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(1));

        var release = await source.GetLatestReleaseAsync(wantPrerelease: false);

        Assert.Equal("v1.2.0", release?.Tag);
        Assert.True(fallbackSource.WasCanceled);
    }

    [Fact]
    public void Preferences_SerializesGitHubUpdateCheckStrategyAsNumber()
    {
        var preferences = new AppPreferences
        {
            GitHubUpdateCheckStrategy = GitHubUpdateCheckStrategy.ApiOnly
        };

        var json = JsonSerializer.Serialize(
            preferences,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        using var document = JsonDocument.Parse(json);
        var strategyProperty = document.RootElement.EnumerateObject()
            .Single(property => string.Equals(
                property.Name,
                nameof(AppPreferences.GitHubUpdateCheckStrategy),
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(JsonValueKind.Number, strategyProperty.Value.ValueKind);
        Assert.Equal(1, strategyProperty.Value.GetInt32());
    }

    [Fact]
    public async Task AtomSource_ReturnsFirstReleaseMatchingChannel()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler("""
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>v2.0.0-rc.1</title>
                <id>tag:github.com,2008:Repository/1.0.0/v2.0.0-rc.1</id>
                <link href="https://github.com/example/project/releases/tag/v2.0.0-rc.1" />
                <content type="html">&lt;p&gt;Preview&lt;br/&gt;notes&lt;/p&gt;</content>
                <published>2026-01-02T03:04:05Z</published>
              </entry>
              <entry>
                <title>v1.9.0</title>
                <id>tag:github.com,2008:Repository/1.0.0/v1.9.0</id>
                <link href="https://github.com/example/project/releases/tag/v1.9.0" />
                <summary>Stable notes</summary>
                <published>2026-01-01T03:04:05Z</published>
              </entry>
            </feed>
            """));
        var source = new GitHubAtomReleaseSource(httpClient, "https://github.com/example/project/releases.atom");

        var stable = await source.GetLatestReleaseAsync(wantPrerelease: false);
        var prerelease = await source.GetLatestReleaseAsync(wantPrerelease: true);

        Assert.Equal("v1.9.0", stable?.Tag);
        Assert.Equal("1.9.0", stable?.Version);
        Assert.Equal("Stable notes", stable?.Body);
        Assert.Equal("v2.0.0-rc.1", prerelease?.Tag);
        Assert.Contains("Preview", prerelease?.Body);
    }

    private static GitHubReleaseLookupResult CreateRelease(string tag)
        => new(
            tag,
            GitHubReleaseLookup.NormalizeVersion(tag),
            GitHubReleaseLookup.IsLikelyPrereleaseTag(tag),
            tag,
            string.Empty,
            [],
            DateTimeOffset.MinValue);

    private sealed class FakeReleaseSource : IGitHubReleaseSource
    {
        private readonly GitHubReleaseLookupResult? _release;
        private readonly TimeSpan _delay;

        public FakeReleaseSource(GitHubReleaseLookupResult? release, TimeSpan delay)
        {
            _release = release;
            _delay = delay;
        }

        public async Task<GitHubReleaseLookupResult?> GetLatestReleaseAsync(
            bool wantPrerelease,
            CancellationToken cancellationToken = default)
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return _release;
        }
    }

    private sealed class CountingReleaseSource : IGitHubReleaseSource
    {
        private readonly GitHubReleaseLookupResult? _release;
        private readonly TimeSpan _delay;

        public CountingReleaseSource(GitHubReleaseLookupResult? release, TimeSpan delay)
        {
            _release = release;
            _delay = delay;
        }

        public int CallCount { get; private set; }

        public async Task<GitHubReleaseLookupResult?> GetLatestReleaseAsync(
            bool wantPrerelease,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return _release;
        }
    }

    private sealed class CancellableReleaseSource : IGitHubReleaseSource
    {
        private readonly GitHubReleaseLookupResult? _release;

        public CancellableReleaseSource(GitHubReleaseLookupResult? release)
        {
            _release = release;
        }

        public bool WasCanceled { get; private set; }

        public async Task<GitHubReleaseLookupResult?> GetLatestReleaseAsync(
            bool wantPrerelease,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return _release;
            }
            catch (OperationCanceledException)
            {
                WasCanceled = true;
                throw;
            }
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly string _content;

        public StaticResponseHandler(string content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content)
            });
        }
    }
}
