using System.Reflection;
using carton.Core.Models;
using carton.Core.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class KernelManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carton-kernel-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Leftover temp files must not fail the test run.
        }
    }

    [Theory]
    [InlineData("sing-box version 1.14.0", "1.14.0")]
    [InlineData("sing-box reF1nd 1.14.0", "1.14.0")]
    [InlineData("sing-box version 1.14.0-re", "1.14.0-re")]
    [InlineData("sing-box version 1.13.13-reF1nd", "1.13.13-reF1nd")]
    [InlineData("sing-box version 1.14.0-alpha.31-reF1nd", "1.14.0-alpha.31-reF1nd")]
    [InlineData("reF1nd sing-box 1.13.9", "1.13.9")]
    public void ParseInstalledVersion_ExtractsSemanticVersion(string output, string expected)
    {
        var method = typeof(KernelManager).GetMethod(
            "ParseInstalledVersion",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var actual = method.Invoke(null, [new[] { output }]);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task EnsureWritableKernelAsync_PromotesBundledKernelIntoDataDirectory()
    {
        var kernel = CreateManager();
        await File.WriteAllTextAsync(kernel.BuiltinPath, "bundled-v1");

        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        Assert.Equal("bundled-v1", await File.ReadAllTextAsync(kernel.DataPath));
        Assert.Equal(kernel.DataPath, kernel.Manager.KernelPath);
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(kernel.DataPath).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task EnsureWritableKernelAsync_LeavesAKernelTheUserInstalledAlone()
    {
        var kernel = CreateManager();
        await File.WriteAllTextAsync(kernel.BuiltinPath, "bundled-v1");
        await File.WriteAllTextAsync(kernel.DataPath, "user-installed");

        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        Assert.Equal("user-installed", await File.ReadAllTextAsync(kernel.DataPath));
    }

    [Fact]
    public async Task EnsureWritableKernelAsync_RefreshesThePromotedCopyAfterAnAppUpdate()
    {
        var kernel = CreateManager();
        await File.WriteAllTextAsync(kernel.BuiltinPath, "bundled-v1");
        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        await File.WriteAllTextAsync(kernel.BuiltinPath, "bundled-v2-is-longer");
        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        Assert.Equal("bundled-v2-is-longer", await File.ReadAllTextAsync(kernel.DataPath));
    }

    [Fact]
    public async Task EnsureWritableKernelAsync_KeepsThePromotedCopyWhenTheBundledKernelIsUnchanged()
    {
        var kernel = CreateManager();
        await File.WriteAllTextAsync(kernel.BuiltinPath, "bundled-v1");
        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        // Stands in for the copy that was chowned to root and given the setuid bit: a second
        // call must not throw that authorization away.
        await File.WriteAllTextAsync(kernel.DataPath, "authorized-copy");
        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        Assert.Equal("authorized-copy", await File.ReadAllTextAsync(kernel.DataPath));
    }

    [Fact]
    public async Task EnsureWritableKernelAsync_KeepsACustomKernelInstalledAfterAPromotion()
    {
        var kernel = CreateManager();
        await File.WriteAllTextAsync(kernel.BuiltinPath, "bundled-v1");
        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        var customKernelPath = Path.Combine(_root, $"custom{PlatformInfo.Current.Suffix}");
        await File.WriteAllTextAsync(customKernelPath, "custom-kernel");
        Assert.True(await kernel.Manager.InstallCustomKernelAsync(customKernelPath));

        // An app update must not silently take the kernel the user picked back out.
        await File.WriteAllTextAsync(kernel.BuiltinPath, "bundled-v2-is-longer");
        Assert.True(await kernel.Manager.EnsureWritableKernelAsync());

        Assert.Equal("custom-kernel", await File.ReadAllTextAsync(kernel.DataPath));
    }

    [Fact]
    public async Task EnsureWritableKernelAsync_FailsWhenNoKernelIsAvailable()
    {
        var kernel = CreateManager();

        Assert.False(await kernel.Manager.EnsureWritableKernelAsync());
    }

    /// <summary>
    /// Builds a manager whose bundled kernel lives in a temporary directory instead of next to
    /// the test host, which is what <see cref="AppContext.BaseDirectory"/> would resolve to.
    /// </summary>
    private ManagedKernel CreateManager()
    {
        var fileName = $"sing-box{PlatformInfo.Current.Suffix}";
        var builtinDirectory = Path.Combine(_root, "app");
        var dataDirectory = Path.Combine(_root, "data");
        Directory.CreateDirectory(builtinDirectory);

        var manager = new KernelManager(dataDirectory);
        var builtinPath = Path.Combine(builtinDirectory, fileName);
        typeof(KernelManager)
            .GetField("_builtinKernelPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(manager, builtinPath);

        return new ManagedKernel(manager, Path.Combine(dataDirectory, "bin", fileName), builtinPath);
    }

    private sealed record ManagedKernel(KernelManager Manager, string DataPath, string BuiltinPath);
}
