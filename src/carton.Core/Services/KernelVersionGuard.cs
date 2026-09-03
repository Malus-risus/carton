using carton.Core.Utilities;

namespace carton.Core.Services;

/// <summary>
/// Hard gate for the minimum supported sing-box kernel version.
///
/// carton's runtime config injects `services: [{type: api}]` and
/// `cache_file.store_dns`, which only exist since sing-box 1.14.0; the whole API
/// layer speaks the 1.14 daemon gRPC contract. Older kernels fail with a cryptic
/// FATAL "unknown field" at startup, so every kernel entry point (start, online
/// download, custom install) must block them with a clear message instead.
/// </summary>
public static class KernelVersionGuard
{
    public const string MinimumRequiredVersion = "1.14.0";

    /// <summary>
    /// Returns true when the given raw version string (e.g. "sing-box version 1.14.0 ..."
    /// or "1.13.9") meets the minimum supported kernel version.
    /// A null/empty version means "unknown" and is rejected by default unless
    /// <paramref name="allowUnknown"/> is set (used by pre-install checks where
    /// the version has not been probed yet).
    /// </summary>
    public static bool IsSupported(string? rawVersion, bool allowUnknown = false)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return allowUnknown;
        }

        var normalized = CartonApplicationInfo.NormalizeSingBoxVersion(ExtractVersionNumber(rawVersion));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            // A non-empty version we cannot parse is an indeterminate result: honour
            // allowUnknown the same way as a missing one (never a deliberate rejection).
            return allowUnknown;
        }

        return CartonApplicationInfo.SupportsNativeApi(normalized);
    }

    /// <summary>
    /// The localized-ready rejection message for an unsupported version.
    /// </summary>
    public static string BuildUnsupportedMessage(string? rawVersion)
    {
        var display = string.IsNullOrWhiteSpace(rawVersion)
            ? CartonApplicationInfo.UnknownSingBoxVersion
            : CartonApplicationInfo.FormatSingBoxVersion(ExtractVersionNumber(rawVersion));
        return $"sing-box {display} is not supported. carton requires sing-box {MinimumRequiredVersion} or newer; please download or install a newer kernel.";
    }

    /// <summary>
    /// Extracts the first semver-looking token from raw `sing-box version` output
    /// ("sing-box version 1.14.0\n..." -> "1.14.0").
    /// </summary>
    public static string? ExtractVersionNumber(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            rawVersion,
            @"\bv?(?<version>\d+\.\d+(?:\.\d+)?(?:[-+][\w.-]+)?)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["version"].Value : null;
    }
}
