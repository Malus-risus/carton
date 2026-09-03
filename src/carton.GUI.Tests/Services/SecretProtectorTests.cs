using carton.Core.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class SecretProtectorTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTripsOnWindows()
    {
        const string secret = "s3cr3t-native-api";

        var stored = SecretProtector.Protect(secret);

        if (OperatingSystem.IsWindows())
        {
            // DPAPI round-trip: stored value must be obfuscated AND recoverable.
            Assert.NotEqual(secret, stored);
            Assert.StartsWith("dpapi:", stored);
            Assert.Equal(secret, SecretProtector.Unprotect(stored));
        }
        else
        {
            // Non-Windows: passthrough by design (documented threat model).
            Assert.Equal(secret, stored);
            Assert.Equal(secret, SecretProtector.Unprotect(stored));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Protect_EmptySecretsStoredAsEmpty(string? empty)
    {
        Assert.Equal(string.Empty, SecretProtector.Protect(empty));
        Assert.Null(SecretProtector.Unprotect(SecretProtector.Protect(empty)));
    }

    [Fact]
    public void Unprotect_PlainLegacyValuePassesThrough()
    {
        // Values written before DPAPI (or on non-Windows) have no prefix.
        Assert.Equal("legacy-secret", SecretProtector.Unprotect("legacy-secret"));
    }

    [Fact]
    public void Unprotect_UnreadableBlobYieldsNull()
    {
        // A blob that does not decrypt (wrong machine/user, or a preferences file
        // copied across platforms) must resolve to null - never the literal ciphertext
        // as the "secret" - and never crash the startup path. True on every platform.
        Assert.Null(SecretProtector.Unprotect("dpapi:not-a-valid-blob=="));
    }

    [Fact]
    public void Unprotect_DpapiPrefixedValueNeverBecomesLiteralSecret()
    {
        // Regression (review #2): a dpapi-prefixed blob must never be returned as the
        // literal secret. On Windows the malformed blob fails to decrypt; on every
        // other platform it cannot be decrypted by construction. Both paths yield
        // null - asserted identically on every platform so a Linux CI run verifies
        // the same invariant instead of skipping.
        var storedByWindows = "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAInvalidButPrefixed==";

        Assert.Null(SecretProtector.Unprotect(storedByWindows));
    }

    [Fact]
    public void IsProtected_ReportsTheStoredForm()
    {
        // The re-encryption gate keys off the INPUT form: a stored dpapi blob must
        // already read as protected, a legacy plain value must not (see the
        // non-determinism test below for why comparing ciphertexts cannot gate it).
        Assert.True(SecretProtector.IsProtected("dpapi:AAAA"));
        Assert.False(SecretProtector.IsProtected("legacy-secret"));
        Assert.False(SecretProtector.IsProtected(null));
        Assert.False(SecretProtector.IsProtected(""));
    }

    [Fact]
    public void Protect_IsNonDeterministicOnWindows_SoReprotectMustNotGateOnCiphertext()
    {
        // Regression (review): DPAPI encryption is non-deterministic - the same
        // plaintext yields a different blob every call. Gating the upgrade
        // re-encryption on "re-protected != stored" would therefore rewrite
        // preferences.json on EVERY startup; the gate must use IsProtected instead.
        if (!OperatingSystem.IsWindows())
        {
            // On passthrough platforms Protect is deterministic and this hazard does
            // not exist; the Windows CI path is where this regression is guarded.
            return;
        }

        const string secret = "determinism-probe";
        var first = SecretProtector.Protect(secret);
        var second = SecretProtector.Protect(secret);

        // Both decrypt to the same secret, but the blobs differ.
        Assert.Equal(secret, SecretProtector.Unprotect(first));
        Assert.Equal(secret, SecretProtector.Unprotect(second));
        Assert.NotEqual(first, second);
    }
}
