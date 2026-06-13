namespace carton.Core.Models;

internal sealed class WindowsHelperStartRequest
{
    public string SingBoxPath { get; set; } = string.Empty;
    public string ConfigPath { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public string ResultFilePath { get; set; } = string.Empty;
    public string ApiAddress { get; set; } = string.Empty;
    public string? ApiSecret { get; set; }
}

internal sealed class WindowsHelperActionResponse
{
    public bool Success { get; set; }
    public int? Pid { get; set; }
    public string? Error { get; set; }
}

internal sealed class WindowsHelperProcessStatusResponse
{
    public bool HasProcess { get; set; }
    public bool IsRunning { get; set; }
    public bool ApiReady { get; set; }
    public int? Pid { get; set; }
    public int? ExitCode { get; set; }
    public string? Error { get; set; }
    public bool StartupLogGap { get; set; }
    public List<WindowsHelperStartupLogLine>? StartupLogs { get; set; }
}

internal sealed class WindowsHelperStartupLogLine
{
    public long Sequence { get; set; }
    public string Message { get; set; } = string.Empty;
}

internal sealed class WindowsHelperLaunchRequest
{
    public int Port { get; set; }
    public string Token { get; set; } = string.Empty;
    public int ParentPid { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
