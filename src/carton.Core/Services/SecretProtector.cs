using System.Security.Cryptography;
using System.Text;

namespace carton.Core.Services;

/// <summary>
/// Protects the persisted native API secret at rest. On Windows the secret can fully
/// control the proxy (SetClashMode / SelectOutbound / CloseAllConnections) and read all
/// connection metadata, so it is encrypted with DPAPI (CurrentUser scope) before
/// landing in the preferences JSON; other platforms keep the previous plain
/// behaviour (the API is loopback-bound and the file inherits OS user isolation - see
/// docs/SINGBOX_API_MIGRATION.md threat model note).
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "dpapi:";

    /// <summary>Encrypts the secret for storage (Windows: DPAPI; otherwise passthrough).</summary>
    public static string Protect(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsWindows())
        {
            return secret;
        }

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// True when the stored value is already in protected (dpapi-prefixed) form.
 /// Callers gate re-encryption on this: DPAPI encryption is NON-DETERMINISTIC, so
    /// comparing a freshly protected value against the stored one would always differ
    /// and trigger pointless (and non-atomic) file rewrites on every startup.
    /// </summary>
    public static bool IsProtected(string? stored)
        => !string.IsNullOrWhiteSpace(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Decrypts a stored secret; returns null when absent or unreadable.</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        if (stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // DPAPI-prefixed values are only decryptable on the machine+user that wrote
            // them: a preferences file copied from Windows to another platform (or from
            // another machine) cannot yield a usable secret. Returning the raw ciphertext
            // would make it the literal API secret and fail auth with no hint at the
            // cause - degrade to "no stored secret" like any unreadable blob instead.
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(stored[Prefix.Length..]);
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                // DPAPI blobs are machine+user bound: unreadable means "no stored secret"
                // (e.g. profile copied from another machine) - the kernel's own config
                // still carries the plain secret for this session.
                return null;
            }
        }

        // No prefix: legacy plain value (pre-DPAPI or non-Windows writer) - use as-is.
        return stored;
    }
}
