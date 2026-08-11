using System.IO;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// 撤销/重做管理器。基于 ProtoBuf 序列化快照实现画面 Widget 列表的撤销和重做。
    /// WorldMap 配置（作业点/范围点等）独立于 Widgets 双栈快照——Widgets 列表不含 WorldMap 数据（C12-2）。
    /// </summary>
    public class UndoManager
    {
        private const int MaxSteps = 20;
        private readonly Dictionary<Screen, Stack<byte[]>> _undoStacks = new();
        private readonly Dictionary<Screen, Stack<byte[]>> _redoStacks = new();
        private readonly Dictionary<Screen, Stack<byte[]>> _wmUndoStacks = new();
        private readonly Dictionary<Screen, Stack<byte[]>> _wmRedoStacks = new();

        public int UndoCount => _undoStacks.Values.Sum(s => s.Count);
        public int RedoCount => _redoStacks.Values.Sum(s => s.Count);

        private int _version;
        /// <summary>单调递增版本号：PushSnapshot/Undo/Redo 每次操作 +1（含裁剪），供 UI 判断快照会话变更。</summary>
        public int Version => _version;

        /// <summary>保存快照（修改前调用）。</summary>
        public void PushSnapshot(Screen screen)
        {
            if (screen == null) return;
            PushToStack(GetUndoStack(screen), screen);
            // 新操作清空 redo
            if (_redoStacks.ContainsKey(screen)) _redoStacks[screen].Clear();
            _version++;
        }

        /// <summary>弹出最近一个撤销快照（命令失败时调用——BeforeModify 已推但模型未变，防空快照污染撤销栈）。</summary>
        public void PopLastSnapshot(Screen screen)
        {
            if (screen == null) return;
            if (_undoStacks.TryGetValue(screen, out var stack) && stack.Count > 0)
            {
                stack.Pop();
                _version++;   // 任何栈变更推进版本（保持快照事件计数不变量，Push/Undo/Redo/Pop 一致）
            }
        }

        /// <summary>撤销。</summary>
        public List<Widget>? Undo(Screen screen)
        {
            if (screen == null || GetUndoStack(screen).Count == 0) return null;
            // 当前状态推入 redo
            PushToStack(GetRedoStack(screen), screen);
            // 从 undo 弹出恢复
            _version++;
            return Deserialize(GetUndoStack(screen).Pop());
        }

        /// <summary>重做。</summary>
        public List<Widget>? Redo(Screen screen)
        {
            if (screen == null || GetRedoStack(screen).Count == 0) return null;
            // 当前状态推入 undo
            PushToStack(GetUndoStack(screen), screen);
            // 从 redo 弹出恢复
            _version++;
            return Deserialize(GetRedoStack(screen).Pop());
        }

        public void Clear(Screen screen)
        {
            if (screen == null) return;
            _undoStacks.Remove(screen);
            _redoStacks.Remove(screen);
            _wmUndoStacks.Remove(screen);
            _wmRedoStacks.Remove(screen);
            _version++;   // 清栈也推进版本（微移快照会话判断用）
        }

        /// <summary>按画面名称清理 undo/redo 栈（删除画面后调用，防内存泄漏）。</summary>
        public void ClearByName(string screenName)
        {
            if (string.IsNullOrEmpty(screenName)) return;
            foreach (var key in _undoStacks.Keys.Where(k => k.Name == screenName).ToList()) _undoStacks.Remove(key);
            foreach (var key in _redoStacks.Keys.Where(k => k.Name == screenName).ToList()) _redoStacks.Remove(key);
            foreach (var key in _wmUndoStacks.Keys.Where(k => k.Name == screenName).ToList()) _wmUndoStacks.Remove(key);
            foreach (var key in _wmRedoStacks.Keys.Where(k => k.Name == screenName).ToList()) _wmRedoStacks.Remove(key);
            _version++;   // 清栈也推进版本（微移快照会话判断用）
        }

        // ── WorldMap 配置快照（C12-2：作业点/范围点等 WorldMap 数据撤销——Widgets 快照不含 WorldMap）──

        /// <summary>WorldMap 撤销栈是否有可撤销操作。</summary>
        public bool HasWorldMapUndo(Screen screen) => screen != null && _wmUndoStacks.TryGetValue(screen, out var s) && s.Count > 0;

        /// <summary>WorldMap 重做栈是否有可重做操作。</summary>
        public bool HasWorldMapRedo(Screen screen) => screen != null && _wmRedoStacks.TryGetValue(screen, out var s) && s.Count > 0;

        /// <summary>保存 WorldMap 配置快照（作业点/范围点等修改前调用；与 Widgets 快照独立）。</summary>
        public void PushWorldMapSnapshot(Screen screen, WorldMapConfig wm)
        {
            if (screen == null || wm == null) return;
            PushWmToStack(GetWmUndoStack(screen), wm);
            // 新操作清空 redo
            if (_wmRedoStacks.ContainsKey(screen)) _wmRedoStacks[screen].Clear();
            _version++;
        }

        /// <summary>撤销 WorldMap 配置：反序列化旧快照写回 wm 实例（保留引用）；无栈返回 false。</summary>
        public bool UndoWorldMap(Screen screen, WorldMapConfig wm)
        {
            if (screen == null || wm == null || !HasWorldMapUndo(screen)) return false;
            // 当前状态推入 redo
            PushWmToStack(GetWmRedoStack(screen), wm);
            RestoreWorldMap(wm, GetWmUndoStack(screen).Pop());
            _version++;
            return true;
        }

        /// <summary>重做 WorldMap 配置。</summary>
        public bool RedoWorldMap(Screen screen, WorldMapConfig wm)
        {
            if (screen == null || wm == null || !HasWorldMapRedo(screen)) return false;
            // 当前状态推入 undo
            PushWmToStack(GetWmUndoStack(screen), wm);
            RestoreWorldMap(wm, GetWmRedoStack(screen).Pop());
            _version++;
            return true;
        }

        private Stack<byte[]> GetWmUndoStack(Screen s) { if (!_wmUndoStacks.ContainsKey(s)) _wmUndoStacks[s] = new(); return _wmUndoStacks[s]; }
        private Stack<byte[]> GetWmRedoStack(Screen s) { if (!_wmRedoStacks.ContainsKey(s)) _wmRedoStacks[s] = new(); return _wmRedoStacks[s]; }

        private static void PushWmToStack(Stack<byte[]> stack, WorldMapConfig wm)
        {
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, wm);
            stack.Push(ms.ToArray());
            if (stack.Count > MaxSteps) { var items = stack.ToArray(); stack.Clear(); for (int i = Math.Min(items.Length - 1, MaxSteps - 1); i >= 0; i--) stack.Push(items[i]); }
        }

        /// <summary>反序列化快照并写回 wm 实例（全字段复制——wm 是 Project.WorldMap 引用，调用方保留引用）。</summary>
        private static void RestoreWorldMap(WorldMapConfig wm, byte[] data)
        {
            using var ms = new MemoryStream(data);
            var restored = Serializer.Deserialize<WorldMapConfig>(ms);
            wm.LatMin = restored.LatMin;
            wm.LatMax = restored.LatMax;
            wm.LngMin = restored.LngMin;
            wm.LngMax = restored.LngMax;
            wm.TileSource = restored.TileSource;
            wm.ZoomLevel = restored.ZoomLevel;
            wm.ShowGlobalOverlay = restored.ShowGlobalOverlay;
            wm.ViewLocked = restored.ViewLocked;
            wm.WorkPoints.Clear();
            foreach (var p in restored.WorkPoints) wm.WorkPoints.Add(p);
            wm.WorkRangePoints.Clear();
            foreach (var p in restored.WorkRangePoints) wm.WorkRangePoints.Add(p);
            wm.Events.Clear();
            foreach (var ev in restored.Events) wm.Events.Add(ev);
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
