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
    private readonly object _timeSyncRoot = new();
    private int _pendingEntriesChanged;
    private long _nextSequence;
    private long _cachedTimeSecond = -1;
    private string _cachedTimeText = string.Empty;

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
            entry = entry with { Sequence = ++_nextSequence };
            _entries.Add(entry);
        }

        RaiseEntriesChanged();
    }

    public void CopySnapshotTo(List<LogEntryRecord> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        lock (_syncRoot)
        {
            _entries.CopyTo(destination);
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
        }

        RaiseEntriesChanged();
    }

    private LogEntryRecord CreateEntry(string message, LogSource source)
    {
        var now = GetCurrentTimeText();
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

        return new LogEntryRecord(0, time, source, level, parsedMessage);
    }

    private LogEntryRecord CreateSingBoxEntry(KernelLogEntry log)
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
            0,
            GetCurrentTimeText(),
            LogSource.SingBox,
            log.Level,
            message);
    }

    private string GetCurrentTimeText()
    {
        var now = DateTime.Now;
        var second = now.Ticks / TimeSpan.TicksPerSecond;
        if (Volatile.Read(ref _cachedTimeSecond) == second)
        {
            return Volatile.Read(ref _cachedTimeText);
        }

        lock (_timeSyncRoot)
        {
            if (_cachedTimeSecond == second)
            {
                return _cachedTimeText;
            }

            _cachedTimeText = now.ToString("HH:mm:ss");
            Volatile.Write(ref _cachedTimeSecond, second);
            return _cachedTimeText;
        }
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

public readonly record struct LogEntryRecord(long Sequence, string Time, LogSource Source, string Level, string Message);

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

    public void CopyTo(List<LogEntryRecord> destination)
    {
        destination.Clear();
        destination.EnsureCapacity(_count);
        for (var i = 0; i < _count; i++)
        {
            destination.Add(_buffer[(_start + i) % _buffer.Length]);
        }
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _start = 0;
        _count = 0;
    }
}
