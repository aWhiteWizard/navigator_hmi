using System.IO;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// 撤销/重做管理器。基于 ProtoBuf 序列化快照实现画面 Widget 列表的撤销和重做。
    /// </summary>
    public class UndoManager
    {
        private const int MaxSteps = 20;
        private readonly Dictionary<Screen, Stack<byte[]>> _undoStacks = new();
        private readonly Dictionary<Screen, Stack<byte[]>> _redoStacks = new();

        public int UndoCount => _undoStacks.Values.Sum(s => s.Count);
        public int RedoCount => _redoStacks.Values.Sum(s => s.Count);

        /// <summary>保存快照（修改前调用）。</summary>
        public void PushSnapshot(Screen screen)
        {
            if (screen == null) return;
            PushToStack(GetUndoStack(screen), screen);
            // 新操作清空 redo
            if (_redoStacks.ContainsKey(screen)) _redoStacks[screen].Clear();
        }

        /// <summary>撤销。</summary>
        public List<Widget>? Undo(Screen screen)
        {
            if (screen == null || GetUndoStack(screen).Count == 0) return null;
            // 当前状态推入 redo
            PushToStack(GetRedoStack(screen), screen);
            // 从 undo 弹出恢复
            return Deserialize(GetUndoStack(screen).Pop());
        }

        /// <summary>重做。</summary>
        public List<Widget>? Redo(Screen screen)
        {
            if (screen == null || GetRedoStack(screen).Count == 0) return null;
            // 当前状态推入 undo
            PushToStack(GetUndoStack(screen), screen);
            // 从 redo 弹出恢复
            return Deserialize(GetRedoStack(screen).Pop());
        }

        public void Clear(Screen screen)
        {
            if (screen == null) return;
            _undoStacks.Remove(screen);
            _redoStacks.Remove(screen);
        }

        /// <summary>按画面名称清理 undo/redo 栈（删除画面后调用，防内存泄漏）。</summary>
        public void ClearByName(string screenName)
        {
            if (string.IsNullOrEmpty(screenName)) return;
            foreach (var key in _undoStacks.Keys.Where(k => k.Name == screenName).ToList()) _undoStacks.Remove(key);
            foreach (var key in _redoStacks.Keys.Where(k => k.Name == screenName).ToList()) _redoStacks.Remove(key);
        }

        private Stack<byte[]> GetUndoStack(Screen s) { if (!_undoStacks.ContainsKey(s)) _undoStacks[s] = new(); return _undoStacks[s]; }
        private Stack<byte[]> GetRedoStack(Screen s) { if (!_redoStacks.ContainsKey(s)) _redoStacks[s] = new(); return _redoStacks[s]; }

        private static void PushToStack(Stack<byte[]> stack, Screen screen)
        {
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, screen.Widgets.ToList());
            stack.Push(ms.ToArray());
            if (stack.Count > MaxSteps) { var items = stack.ToArray(); stack.Clear(); for (int i = Math.Min(items.Length - 1, MaxSteps - 1); i >= 0; i--) stack.Push(items[i]); }
        }

        private static List<Widget>? Deserialize(byte[] data) { using var ms = new MemoryStream(data); return Serializer.Deserialize<List<Widget>>(ms); }
    }
}
