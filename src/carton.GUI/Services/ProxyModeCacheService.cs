using System;
using carton.Core.Models;

namespace carton.GUI.Services;

public sealed class ProxyModeCacheService
{
    private static readonly Lazy<ProxyModeCacheService> _instance = new(() => new ProxyModeCacheService());
    public static ProxyModeCacheService Instance => _instance.Value;

    private ProxyModeCacheService()
    {
    }

    public ApiModeConfigSnapshot? Current { get; private set; }
    public bool IsDirty { get; private set; }
    public DateTimeOffset LastUpdatedUtc { get; private set; }

    public void Update(ApiModeConfigSnapshot? config, bool isDirty = false)
    {
        Current = config;
        IsDirty = isDirty;
        LastUpdatedUtc = config == null ? default : DateTimeOffset.UtcNow;
    }

    public bool TryGetFresh(TimeSpan maxAge, out ApiModeConfigSnapshot? config)
    {
        config = Current;
        if (config == null || IsDirty)
        {
            return false;
        }

        return LastUpdatedUtc != default &&
               LastUpdatedUtc.Add(maxAge) > DateTimeOffset.UtcNow;
    }

    public void Clear()
    {
        Current = null;
        IsDirty = false;
        LastUpdatedUtc = default;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }
}
