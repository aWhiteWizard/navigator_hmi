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
        // X-1c：变量基准值快照（单栈——Tags 全局非画面级；拖拽绑变量点更新 BaseValue 前调用，Ctrl+Z 还原变量值）
        private readonly Stack<byte[]> _tagUndoStack = new();
        private readonly Stack<byte[]> _tagRedoStack = new();

        public int UndoCount => _undoStacks.Values.Sum(s => s.Count);
        public int RedoCount => _redoStacks.Values.Sum(s => s.Count);
        /// <summary>X-1c：Widgets 撤销栈是否有可撤销操作。</summary>
        public bool HasUndo(Screen screen) => screen != null && GetUndoStack(screen).Count > 0;
        /// <summary>X-1c：Widgets 重做栈是否有可重做操作。</summary>
        public bool HasRedo(Screen screen) => screen != null && GetRedoStack(screen).Count > 0;

        private int _version;
        /// <summary>单调递增版本号：PushSnapshot/Undo/Redo 每次操作 +1（含裁剪），供 UI 判断快照会话变更。</summary>
        public int Version => _version;

        // X-1c should-fix：各撤销栈最近 push 版本——Ctrl+Z 按 LIFO 全局顺序取最近操作（防固定优先级撤销错序：
        // 先拖绑点（Tag 栈）再拖非绑点（WorldMap 栈）时，应撤后者而非按固定 Tag 优先撤更早的）
        public int WidgetsLatestVersion { get; private set; }
        public int WorldMapLatestVersion { get; private set; }
        public int TagLatestVersion { get; private set; }
        // X-1c 复审：redo 路由须用"最近撤销"版本（非 push 版本）——撤销顺序 WM→Tag 时，重做应 Tag→WM（后撤先重做）
        public int WidgetsRedoLatestVersion { get; private set; }
        public int WorldMapRedoLatestVersion { get; private set; }
        public int TagRedoLatestVersion { get; private set; }

        /// <summary>保存快照（修改前调用）。</summary>
        public void PushSnapshot(Screen screen)
        {
            if (screen == null) return;
            PushToStack(GetUndoStack(screen), screen);
            // 新操作清空 redo
            if (_redoStacks.ContainsKey(screen)) _redoStacks[screen].Clear();
            _version++;
            WidgetsLatestVersion = _version;   // X-1c：记录最近版本供 LIFO 路由
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
            WidgetsRedoLatestVersion = _version;   // X-1c 复审：记录最近撤销版本供 redo 路由
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
            WorldMapLatestVersion = _version;   // X-1c：记录最近版本供 LIFO 路由
        }

        /// <summary>撤销 WorldMap 配置：反序列化旧快照写回 wm 实例（保留引用）；无栈返回 false。</summary>
        public bool UndoWorldMap(Screen screen, WorldMapConfig wm)
        {
            if (screen == null || wm == null || !HasWorldMapUndo(screen)) return false;
            // 当前状态推入 redo
            PushWmToStack(GetWmRedoStack(screen), wm);
            RestoreWorldMap(wm, GetWmUndoStack(screen).Pop());
            _version++;
            WorldMapRedoLatestVersion = _version;   // X-1c 复审
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

        // ── X-1c：变量基准值快照（单栈——Tags 全局非画面级；拖拽绑变量点更新 BaseValue 前调用）──

        /// <summary>变量撤销栈是否有可撤销操作。</summary>
        public bool HasTagUndo => _tagUndoStack.Count > 0;
        /// <summary>变量重做栈是否有可重做操作。</summary>
        public bool HasTagRedo => _tagRedoStack.Count > 0;

        /// <summary>保存变量列表快照（修改 BaseValue 前调用；单栈全局）。</summary>
        public void PushTagSnapshot(List<Tag> tags)
        {
            if (tags == null) return;
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, tags);
            _tagUndoStack.Push(ms.ToArray());
            if (_tagUndoStack.Count > MaxSteps) { var items = _tagUndoStack.ToArray(); _tagUndoStack.Clear(); for (int i = Math.Min(items.Length - 1, MaxSteps - 1); i >= 0; i--) _tagUndoStack.Push(items[i]); }
            _tagRedoStack.Clear();   // 新操作清空 redo
            _version++;
            TagLatestVersion = _version;   // X-1c：记录最近版本供 LIFO 路由
        }

        /// <summary>X-1c：外部（非拖拽）修改变量基准值后清 Tag redo——防 Redo 用旧快照覆盖手动编辑的新值。</summary>
        public void ClearTagRedo() => _tagRedoStack.Clear();

        /// <summary>撤销变量基准值：反序列化旧快照写回 tags 列表（按 Name 匹配还原 BaseValue）；无栈返回 false。</summary>
        public bool UndoTags(List<Tag> tags)
        {
            if (tags == null || _tagUndoStack.Count == 0) return false;
            // 当前状态推入 redo
            using var msCur = new MemoryStream();
            Serializer.Serialize(msCur, tags);
            _tagRedoStack.Push(msCur.ToArray());
            RestoreTagBaseValues(tags, _tagUndoStack.Pop());
            _version++;
            TagRedoLatestVersion = _version;   // X-1c 复审
            return true;
        }

        /// <summary>重做变量基准值。</summary>
        public bool RedoTags(List<Tag> tags)
        {
            if (tags == null || _tagRedoStack.Count == 0) return false;
            using var msCur = new MemoryStream();
            Serializer.Serialize(msCur, tags);
            _tagUndoStack.Push(msCur.ToArray());
            RestoreTagBaseValues(tags, _tagRedoStack.Pop());
            _version++;
            return true;
        }

        /// <summary>反序列化快照并按 Name 还原 BaseValue（快照里的变量若已删除则忽略；当前新增变量不动）。</summary>
        private static void RestoreTagBaseValues(List<Tag> tags, byte[] data)
        {
            using var ms = new MemoryStream(data);
            var snap = Serializer.Deserialize<List<Tag>>(ms);
            foreach (var st in snap)
            {
                var cur = tags.FirstOrDefault(t => t.Name == st.Name);
                if (cur != null) cur.BaseValue = st.BaseValue;
            }
        }

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
