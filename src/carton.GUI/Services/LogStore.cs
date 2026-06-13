using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using carton.Core.Models;
using carton.GUI.Models;

namespace carton.GUI.Services;

public sealed class LogStore
{
    private const int MaxEntries = 800;
    private const int MaxMessageLength = 2048;
    private readonly LogRingBuffer _entries = new(MaxEntries);
    private readonly object _syncRoot = new();
    private LogEntryRecord[] _snapshotCache = Array.Empty<LogEntryRecord>();
    private bool _snapshotDirty = true;
    private int _pendingEntriesChanged;

    public event EventHandler? EntriesChanged;

    public void AddLog(string message)
    {
        AddLog(message, LogSource.Carton);
    }

    public void AddLog(string message, LogSource source)
    {
        var entry = CreateEntry(message, source);
        AddEntry(entry);
    }

    public void AddSingBoxLog(KernelLogEntry log)
    {
        var entry = CreateSingBoxEntry(log);
        AddEntry(entry);
    }

    private void AddEntry(LogEntryRecord entry)
    {
        lock (_syncRoot)
        {
            _entries.Add(entry);
            _snapshotDirty = true;
        }

        RaiseEntriesChanged();
    }

    public IReadOnlyList<LogEntryRecord> GetSnapshot()
    {
        lock (_syncRoot)
        {
            if (_snapshotDirty)
            {
                _snapshotCache = _entries.ToArray();
                _snapshotDirty = false;
            }

            return _snapshotCache;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
            _snapshotDirty = true;
            _snapshotCache = Array.Empty<LogEntryRecord>();
        }

        RaiseEntriesChanged();
    }

    private static LogEntryRecord CreateEntry(string message, LogSource source)
    {
        var now = DateTime.Now.ToString("HH:mm:ss");
        var (time, level, parsedMessage) = source switch
        {
            LogSource.Carton => LogParser.ParseCartonLog(message, now),
            LogSource.SingBox => LogParser.ParseSingBoxLog(message, now),
            _ => (now, "Info", message)
        };

        if (parsedMessage.Length > MaxMessageLength)
        {
            parsedMessage = parsedMessage[..MaxMessageLength] + "...";
        }

        return new LogEntryRecord(time, source, level, parsedMessage);
    }

    private static LogEntryRecord CreateSingBoxEntry(KernelLogEntry log)
    {
        if (string.IsNullOrWhiteSpace(log.Level))
        {
            return CreateEntry(log.Message, LogSource.SingBox);
        }

        var message = log.Message;
        if (message.Length > MaxMessageLength)
        {
            message = message[..MaxMessageLength] + "...";
        }

        return new LogEntryRecord(
            DateTime.Now.ToString("HH:mm:ss"),
            LogSource.SingBox,
            log.Level,
            message);
    }

    private void RaiseEntriesChanged()
    {
        if (EntriesChanged == null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            EntriesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Interlocked.Exchange(ref _pendingEntriesChanged, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _pendingEntriesChanged, 0);
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}

public readonly record struct LogEntryRecord(string Time, LogSource Source, string Level, string Message);

internal sealed class LogRingBuffer
{
    private readonly LogEntryRecord[] _buffer;
    private int _start;
    private int _count;

    public LogRingBuffer(int capacity)
    {
        _buffer = new LogEntryRecord[Math.Max(1, capacity)];
    }

    public void Add(LogEntryRecord entry)
    {
        if (_count < _buffer.Length)
        {
            _buffer[(_start + _count) % _buffer.Length] = entry;
            _count++;
            return;
        }

        _buffer[_start] = entry;
        _start = (_start + 1) % _buffer.Length;
    }

    public LogEntryRecord[] ToArray()
    {
        var result = new LogEntryRecord[_count];
        for (var i = 0; i < _count; i++)
        {
            result[i] = _buffer[(_start + i) % _buffer.Length];
        }

        return result;
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _start = 0;
        _count = 0;
    }
}
