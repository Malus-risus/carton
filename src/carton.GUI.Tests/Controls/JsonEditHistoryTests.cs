using carton.GUI.Controls;
using Xunit;

namespace carton.GUI.Tests.Controls;

public class JsonEditHistoryTests
{
    private static readonly CaretState S0 = new(0, -1, 0, 0);

    private static JsonEditHistory New() => new(maxEntries: 200, maxChars: 32L * 1024 * 1024);

    // 模拟编辑器调用方：记录正向编辑（Restore = 编辑前状态）。
    private static string Apply(JsonEditHistory h, string text, int offset, int removeLen, string insert, CaretState before)
    {
        var removed = removeLen > 0 ? text.Substring(offset, removeLen) : string.Empty;
        h.Record(new TextEdit(offset, removed, insert, before));
        return text.Remove(offset, removeLen).Insert(offset, insert);
    }

    [Fact]
    public void Undo_RevertsInsertion()
    {
        var h = New();
        var text = "ab";
        text = Apply(h, text, 2, 0, "c", S0); // "abc"
        Assert.Equal("abc", text);

        Assert.True(h.TryUndo(text, new CaretState(3, 3, 0, 0), out text, out var state));
        Assert.Equal("ab", text);
        Assert.Equal(0, state.Caret); // 恢复到编辑前
    }

    [Fact]
    public void Undo_RevertsDeletion()
    {
        var h = New();
        var text = "abc";
        text = Apply(h, text, 2, 1, string.Empty, new CaretState(3, 3, 0, 0)); // 删除 'c' -> "ab"
        Assert.Equal("ab", text);

        Assert.True(h.TryUndo(text, new CaretState(2, 2, 0, 0), out text, out var state));
        Assert.Equal("abc", text); // 删除被补回
        Assert.Equal(3, state.Caret);
    }

    [Fact]
    public void Redo_ReappliesEdit()
    {
        var h = New();
        var text = Apply(h, "ab", 2, 0, "c", S0); // "abc"
        Assert.True(h.TryUndo(text, new CaretState(3, 3, 0, 0), out text, out _)); // "ab"

        Assert.True(h.TryRedo(text, new CaretState(0, 0, 0, 0), out text, out var state));
        Assert.Equal("abc", text);
        Assert.Equal(3, state.Caret); // 重做恢复到编辑后
    }

    [Fact]
    public void NewEdit_ClearsRedoStack()
    {
        var h = New();
        var text = Apply(h, "ab", 2, 0, "c", S0); // "abc"
        Assert.True(h.TryUndo(text, new CaretState(3, 3, 0, 0), out text, out _)); // "ab"
        Assert.True(h.CanRedo);

        text = Apply(h, text, 2, 0, "X", new CaretState(2, 2, 0, 0)); // "abX"
        Assert.False(h.CanRedo); // 新编辑后重做栈被清空
    }

    [Fact]
    public void MultiStep_UndoRedoRoundTrips()
    {
        var h = New();
        var text = "";
        text = Apply(h, text, 0, 0, "hello", S0);
        text = Apply(h, text, 5, 0, " world", new CaretState(5, 5, 0, 0));
        text = Apply(h, text, 0, 5, "HELLO", new CaretState(11, 11, 0, 0)); // 替换 hello
        Assert.Equal("HELLO world", text);

        // 全部撤销
        Assert.True(h.TryUndo(text, S0, out text, out _));
        Assert.Equal("hello world", text);
        Assert.True(h.TryUndo(text, S0, out text, out _));
        Assert.Equal("hello", text);
        Assert.True(h.TryUndo(text, S0, out text, out _));
        Assert.Equal("", text);
        Assert.False(h.CanUndo);

        // 全部重做
        Assert.True(h.TryRedo(text, S0, out text, out _));
        Assert.Equal("hello", text);
        Assert.True(h.TryRedo(text, S0, out text, out _));
        Assert.Equal("hello world", text);
        Assert.True(h.TryRedo(text, S0, out text, out _));
        Assert.Equal("HELLO world", text);
    }

    [Fact]
    public void Undo_OnEmptyStack_ReturnsFalse()
    {
        var h = New();
        Assert.False(h.TryUndo("abc", S0, out var text, out _));
        Assert.Equal("abc", text);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var h = New();
        var text = Apply(h, "ab", 2, 0, "c", S0);
        h.TryUndo(text, S0, out _, out _);
        h.Clear();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void Trim_DropsOldestWhenCharBudgetExceeded()
    {
        // 每条编辑插入 10 个字符，字符预算 25，最多保留最新 2 条（20 字符）。
        var h = new JsonEditHistory(maxEntries: 200, maxChars: 25);
        var text = "";
        for (var i = 0; i < 5; i++)
        {
            text = Apply(h, text, text.Length, 0, new string('x', 10), S0);
        }

        // 只能撤销最新的 2 条。
        Assert.True(h.TryUndo(text, S0, out text, out _));
        Assert.True(h.TryUndo(text, S0, out text, out _));
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Trim_KeepsAtLeastOneEntryEvenIfOverBudget()
    {
        // 单条编辑就超预算，仍须保留这一条（否则该步无法撤销）。
        var h = new JsonEditHistory(maxEntries: 200, maxChars: 5);
        var text = Apply(h, "", 0, 0, new string('x', 100), S0);
        Assert.True(h.CanUndo);
        Assert.True(h.TryUndo(text, S0, out text, out _));
        Assert.Equal("", text);
    }
}
