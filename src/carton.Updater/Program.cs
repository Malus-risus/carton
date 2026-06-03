using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace carton.Updater;

internal static class Program
{
    private const int DefaultWaitSeconds = 30;

    public static int Main(string[] args)
    {
        try
        {
            var options = UpdaterOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            ValidateOptions(options);
            WaitForProcessExit(options.ProcessId, options.WaitSeconds);

            var stagingDir = Path.Combine(
                Path.GetTempPath(),
                "carton-update-stage-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(stagingDir);
                ExtractArchive(options.ArchivePath!, stagingDir);

                var sourceRoot = ResolveArchiveRoot(stagingDir);
                CopyDirectory(sourceRoot, options.TargetDirectory!);
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
            }

            if (!string.IsNullOrWhiteSpace(options.RestartExecutable))
            {
                var exePath = Path.Combine(options.TargetDirectory!, options.RestartExecutable);
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = options.TargetDirectory!,
                        UseShellExecute = false
                    });
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "carton-updater-error.log"),
                    ex.ToString());
            }
            catch
            {
            }

            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Carton_Updater --pid <pid> --archive <portable.zip|portable.tar.gz> --target <appDir> [--restart carton] [--wait-seconds 30]");
    }

    private static void ValidateOptions(UpdaterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ArchivePath) || !File.Exists(options.ArchivePath))
        {
            throw new ArgumentException("Archive path is missing or does not exist.");
        }

        if (string.IsNullOrWhiteSpace(options.TargetDirectory) || !Directory.Exists(options.TargetDirectory))
        {
            throw new ArgumentException("Target directory is missing or does not exist.");
        }
    }

    private static void WaitForProcessExit(int? processId, int waitSeconds)
    {
        if (processId is not > 0)
        {
            return;
        }

        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId.Value);
        }
        catch
        {
            return;
        }

        using (process)
        {
            var waitMilliseconds = Math.Max(1, waitSeconds) * 1000;
            if (process.WaitForExit(waitMilliseconds))
            {
                return;
            }

            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
            }
        }
    }

    private static string ResolveArchiveRoot(string stagingDir)
    {
        var entries = Directory.GetFileSystemEntries(stagingDir);
        if (entries.Length == 1 && Directory.Exists(entries[0]))
        {
            return entries[0];
        }

        return stagingDir;
    }

    private static void ExtractArchive(string archivePath, string stagingDir)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, stagingDir, overwriteFiles: true);
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var fileStream = File.OpenRead(archivePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzipStream, stagingDir, overwriteFiles: true);
            return;
        }

        throw new NotSupportedException($"Unsupported archive format: {archivePath}");
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(targetDir, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destination, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                TryCopyUnixFileMode(file, destination);
            }
        }
    }

    private static void TryCopyUnixFileMode(string source, string destination)
    {
        try
        {
#pragma warning disable CA1416
            var mode = File.GetUnixFileMode(source);
            File.SetUnixFileMode(destination, mode);
#pragma warning restore CA1416
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class UpdaterOptions
{
    public bool ShowHelp { get; private set; }
    public int? ProcessId { get; private set; }
    public string? ArchivePath { get; private set; }
    public string? TargetDirectory { get; private set; }
    public string? RestartExecutable { get; private set; }
    public int WaitSeconds { get; private set; } = 30;

    public static UpdaterOptions Parse(string[] args)
    {
        var options = new UpdaterOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--pid":
                    options.ProcessId = int.Parse(ReadValue(args, ref i, arg));
                    break;
                case "--archive":
                    options.ArchivePath = ReadValue(args, ref i, arg);
                    break;
                case "--target":
                    options.TargetDirectory = ReadValue(args, ref i, arg);
                    break;
                case "--restart":
                    options.RestartExecutable = ReadValue(args, ref i, arg);
                    break;
                case "--wait-seconds":
                    options.WaitSeconds = int.Parse(ReadValue(args, ref i, arg));
                    break;
            }
        }

        return options;
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }
}
