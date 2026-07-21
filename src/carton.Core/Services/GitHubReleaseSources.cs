using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using carton.Core.Models;

namespace carton.Core.Services;

public interface IGitHubUpdateCheckStrategyProvider
{
    GitHubUpdateCheckStrategy GetStrategy();
}

public sealed class StaticGitHubUpdateCheckStrategyProvider : IGitHubUpdateCheckStrategyProvider
{
    private readonly GitHubUpdateCheckStrategy _strategy;

    public StaticGitHubUpdateCheckStrategyProvider(GitHubUpdateCheckStrategy strategy)
    {
        _strategy = strategy;
    }

    public GitHubUpdateCheckStrategy GetStrategy() => _strategy;
}

public sealed class PreferencesGitHubUpdateCheckStrategyProvider : IGitHubUpdateCheckStrategyProvider
{
    private readonly IPreferencesService _preferencesService;

    public PreferencesGitHubUpdateCheckStrategyProvider(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;
    }

    public GitHubUpdateCheckStrategy GetStrategy()
        => _preferencesService.Load().GitHubUpdateCheckStrategy;
}

public interface IGitHubReleaseSource
{
    Task<GitHubReleaseLookupResult?> GetLatestReleaseAsync(
        bool wantPrerelease,
        CancellationToken cancellationToken = default);
}

public sealed record GitHubReleaseLookupResult(
    string Tag,
    string Version,
    bool IsPrerelease,
    string Name,
    string Body,
    IReadOnlyList<GitHubReleaseAssetLookupResult> Assets,
    DateTimeOffset PublishedAt);

public sealed record GitHubReleaseAssetLookupResult(
    string Name,
    string DownloadUrl,
    long Size);

public sealed class PreferredGitHubReleaseSource : IGitHubReleaseSource
{
    private readonly IGitHubReleaseSource _apiSource;
    private readonly IGitHubReleaseSource _fallbackSource;
    private readonly TimeSpan _apiPreferredWait;
    private readonly TimeSpan _sourceTimeout;
    private readonly IGitHubUpdateCheckStrategyProvider _strategyProvider;
    private readonly Action<string>? _log;

    public PreferredGitHubReleaseSource(
        IGitHubReleaseSource apiSource,
        IGitHubReleaseSource fallbackSource,
        TimeSpan apiPreferredWait,
        TimeSpan sourceTimeout,
        IGitHubUpdateCheckStrategyProvider? strategyProvider = null,
        Action<string>? log = null)
    {
        _apiSource = apiSource;
        _fallbackSource = fallbackSource;
        _apiPreferredWait = apiPreferredWait;
        _sourceTimeout = sourceTimeout;
        _strategyProvider = strategyProvider ??
                            new StaticGitHubUpdateCheckStrategyProvider(GitHubUpdateCheckStrategy.ApiThenAtom);
        _log = log;
    }

    public async Task<GitHubReleaseLookupResult?> GetLatestReleaseAsync(
        bool wantPrerelease,
        CancellationToken cancellationToken = default)
    {
        if (_strategyProvider.GetStrategy() == GitHubUpdateCheckStrategy.ApiOnly)
        {
            return await GetLatestReleaseFromSourceAsync(_apiSource, "GitHub API", wantPrerelease, cancellationToken)
                .ConfigureAwait(false);
        }

        using var apiCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var apiTask = GetLatestReleaseFromSourceAsync(_apiSource, "GitHub API", wantPrerelease, apiCts.Token);
        var fallbackTask = GetLatestReleaseFromSourceAsync(_fallbackSource, "releases.atom", wantPrerelease, fallbackCts.Token);
        var apiPreferredDelayTask = Task.Delay(_apiPreferredWait, cancellationToken);

        if (await Task.WhenAny(apiTask, apiPreferredDelayTask).ConfigureAwait(false) == apiTask)
        {
            var apiRelease = await apiTask.ConfigureAwait(false);
            if (apiRelease != null)
            {
                await CancelAndObserveAsync(fallbackCts, fallbackTask).ConfigureAwait(false);
                return apiRelease;
            }
        }

        if (fallbackTask.IsCompleted)
        {
            var fallbackRelease = await fallbackTask.ConfigureAwait(false);
            if (fallbackRelease != null)
            {
                await CancelAndObserveAsync(apiCts, apiTask).ConfigureAwait(false);
                return fallbackRelease;
            }
        }

        var remainingTasks = new List<Task<GitHubReleaseLookupResult?>>();
        if (!apiTask.IsCompleted)
        {
            remainingTasks.Add(apiTask);
        }
        if (!fallbackTask.IsCompleted)
        {
            remainingTasks.Add(fallbackTask);
        }

        while (remainingTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(remainingTasks).ConfigureAwait(false);
            remainingTasks.Remove(completedTask);
            var release = await completedTask.ConfigureAwait(false);
            if (release != null)
            {
                if (!apiTask.IsCompleted)
                {
                    await CancelAndObserveAsync(apiCts, apiTask).ConfigureAwait(false);
                }
                if (!fallbackTask.IsCompleted)
                {
                    await CancelAndObserveAsync(fallbackCts, fallbackTask).ConfigureAwait(false);
                }
                return release;
            }
        }

        return null;
    }

    private async Task<GitHubReleaseLookupResult?> GetLatestReleaseFromSourceAsync(
        IGitHubReleaseSource source,
        string sourceName,
        bool wantPrerelease,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_sourceTimeout);
            return await source.GetLatestReleaseAsync(wantPrerelease, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _log?.Invoke($"{sourceName} lookup failed: {ex.Message}");
            return null;
        }
    }

    private static async Task CancelAndObserveAsync(
        CancellationTokenSource cancellationTokenSource,
        Task<GitHubReleaseLookupResult?> task)
    {
        cancellationTokenSource.Cancel();

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }
}

public sealed class GitHubApiReleaseSource : IGitHubReleaseSource
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;

    public GitHubApiReleaseSource(HttpClient httpClient, string owner, string repository)
    {
        _httpClient = httpClient;
        _owner = owner;
        _repository = repository;
    }

    public async Task<GitHubReleaseLookupResult?> GetLatestReleaseAsync(
        bool wantPrerelease,
        CancellationToken cancellationToken = default)
    {
        return wantPrerelease
            ? await GetLatestPrereleaseAsync(cancellationToken).ConfigureAwait(false)
            : await GetLatestStableReleaseAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitHubReleaseLookupResult?> GetReleaseByTagAsync(
        string tag,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{_owner}/{_repository}/releases/tags/{Uri.EscapeDataString(tag)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseRelease(document.RootElement);
    }

    private async Task<GitHubReleaseLookupResult?> GetLatestStableReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{_owner}/{_repository}/releases/latest");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var release = ParseRelease(document.RootElement);
        return release?.IsPrerelease == false ? release : null;
    }

    private async Task<GitHubReleaseLookupResult?> GetLatestPrereleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{_owner}/{_repository}/releases?per_page=30");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var release = ParseRelease(item);
            if (release?.IsPrerelease == true)
            {
                return release;
            }
        }

        return null;
    }

    private static GitHubReleaseLookupResult? ParseRelease(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var assets = new List<GitHubReleaseAssetLookupResult>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = ReadString(asset, "name") ?? string.Empty;
                var url = ReadString(asset, "browser_download_url") ?? string.Empty;
                var size = asset.TryGetProperty("size", out var sizeProperty) && sizeProperty.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0;

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                {
                    assets.Add(new GitHubReleaseAssetLookupResult(name, url, size));
                }
            }
        }

        var releaseName = ReadString(root, "name") ?? tag;
        var body = ReadString(root, "body") ?? string.Empty;
        var isPrerelease = ReadBool(root, "prerelease") == true;
        var publishedAt = ParseDateTime(ReadString(root, "published_at"));

        return new GitHubReleaseLookupResult(
            tag,
            GitHubReleaseLookup.NormalizeVersion(tag),
            isPrerelease,
            string.IsNullOrWhiteSpace(releaseName) ? tag : releaseName,
            body,
            assets,
            publishedAt);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;
    }

    private static DateTimeOffset ParseDateTime(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : DateTimeOffset.MinValue;
    }
}

public sealed class GitHubAtomReleaseSource : IGitHubReleaseSource
{
    private readonly HttpClient _httpClient;
    private readonly string _atomFeedUrl;

    public GitHubAtomReleaseSource(HttpClient httpClient, string atomFeedUrl)
    {
        _httpClient = httpClient;
        _atomFeedUrl = atomFeedUrl;
    }

    public async Task<GitHubReleaseLookupResult?> GetLatestReleaseAsync(
        bool wantPrerelease,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _atomFeedUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var atom = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        foreach (var release in ParseReleaseAtomFeed(atom, _atomFeedUrl))
        {
            if (wantPrerelease == release.IsPrerelease)
            {
                return release;
            }
        }

        return null;
    }

    private static IEnumerable<GitHubReleaseLookupResult> ParseReleaseAtomFeed(
        string atom,
        string atomFeedUrl)
    {
        var document = XDocument.Parse(atom);
        XNamespace atomNamespace = "http://www.w3.org/2005/Atom";
        var atomFeedUri = Uri.TryCreate(atomFeedUrl, UriKind.Absolute, out var parsedAtomFeedUri)
            ? parsedAtomFeedUri
            : null;

        foreach (var entry in document.Descendants(atomNamespace + "entry"))
        {
            var title = entry.Element(atomNamespace + "title")?.Value?.Trim() ?? string.Empty;
            var tag = TryExtractReleaseTag(entry.Element(atomNamespace + "id")?.Value);
            foreach (var link in entry.Elements(atomNamespace + "link"))
            {
                tag ??= TryExtractReleaseTag(link.Attribute("href")?.Value);
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var htmlContent = entry.Element(atomNamespace + "content")?.Value;
            var body = NormalizeReleaseBody(
                htmlContent,
                entry.Element(atomNamespace + "summary")?.Value);
            var assets = ExtractReleaseAssets(htmlContent, tag, atomFeedUri);
            var isPrerelease = GitHubReleaseLookup.IsLikelyPrereleaseTag(tag) ||
                               GitHubReleaseLookup.IsLikelyPrereleaseTag(title);
            var publishedAt = ParseAtomDateTime(
                entry.Element(atomNamespace + "published")?.Value,
                entry.Element(atomNamespace + "updated")?.Value);

            yield return new GitHubReleaseLookupResult(
                tag,
                GitHubReleaseLookup.NormalizeVersion(tag),
                isPrerelease,
                string.IsNullOrWhiteSpace(title) ? tag : title,
                body,
                assets,
                publishedAt);
        }
    }

    private static IReadOnlyList<GitHubReleaseAssetLookupResult> ExtractReleaseAssets(
        string? htmlContent,
        string tag,
        Uri? atomFeedUri)
    {
        if (string.IsNullOrWhiteSpace(htmlContent) || atomFeedUri == null)
        {
            return [];
        }

        const string atomSuffix = "/releases.atom";
        var atomPath = atomFeedUri.AbsolutePath.TrimEnd('/');
        if (!atomPath.EndsWith(atomSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var repositoryPath = atomPath[..^atomSuffix.Length];
        var downloadPathPrefix = $"{repositoryPath}/releases/download/";
        var assets = new List<GitHubReleaseAssetLookupResult>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(
            htmlContent,
            @"href\s*=\s*(?:""(?<double>[^""]+)""|'(?<single>[^']+)')",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            var rawUrl = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value;
            var decodedUrl = WebUtility.HtmlDecode(rawUrl).Trim();
            if (!Uri.TryCreate(atomFeedUri, decodedUrl, out var downloadUri) ||
                (downloadUri.Scheme != Uri.UriSchemeHttp && downloadUri.Scheme != Uri.UriSchemeHttps) ||
                !string.Equals(downloadUri.Host, atomFeedUri.Host, StringComparison.OrdinalIgnoreCase) ||
                !downloadUri.AbsolutePath.StartsWith(downloadPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativeDownloadPath = downloadUri.AbsolutePath[downloadPathPrefix.Length..];
            var separatorIndex = relativeDownloadPath.IndexOf('/');
            if (separatorIndex <= 0 || separatorIndex == relativeDownloadPath.Length - 1)
            {
                continue;
            }

            var linkedTag = Uri.UnescapeDataString(relativeDownloadPath[..separatorIndex]);
            var encodedAssetName = relativeDownloadPath[(separatorIndex + 1)..];
            if (!string.Equals(linkedTag, tag, StringComparison.OrdinalIgnoreCase) ||
                encodedAssetName.Contains('/'))
            {
                continue;
            }

            var assetName = Uri.UnescapeDataString(encodedAssetName);
            var absoluteUrl = downloadUri.AbsoluteUri;
            if (string.IsNullOrWhiteSpace(assetName) || !seenUrls.Add(absoluteUrl))
            {
                continue;
            }

            assets.Add(new GitHubReleaseAssetLookupResult(assetName, absoluteUrl, 0));
        }

        return assets;
    }

    private static string? TryExtractReleaseTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(
            value,
            @"/releases/tag/(?<tag>[^""#?&/]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? Uri.UnescapeDataString(match.Groups["tag"].Value)
            : null;
    }

    private static string NormalizeReleaseBody(string? htmlContent, string? summary)
    {
        var content = string.IsNullOrWhiteSpace(htmlContent) ? summary : htmlContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(content, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        return WebUtility.HtmlDecode(normalized).Trim();
    }

    private static DateTimeOffset ParseAtomDateTime(params string?[] values)
    {
        foreach (var value in values)
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            {
                return dto;
            }
        }

        return DateTimeOffset.MinValue;
    }
}

public static class GitHubReleaseLookup
{
    public static string NormalizeVersion(string tag)
    {
        var normalized = tag.Trim();
        if (normalized.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["refs/tags/".Length..];
        }

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }

    public static bool IsLikelyPrereleaseTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"(?:^|[.\-_])(?:alpha|beta|preview|pre|rc)(?:[.\-_]?\d+)?(?:$|[.\-_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
