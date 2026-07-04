using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// 撤销管理器。基于 ProtoBuf 序列化快照实现画面 Widget 列表的撤销。
    /// 每个 Screen 维护独立的快照栈，最多保留 20 步。
    /// </summary>
    public class UndoManager
    {
        private const int MaxSteps = 20;

        /// <summary>按 Screen 维度存储快照栈（key = Screen 引用）</summary>
        private readonly Dictionary<Screen, Stack<byte[]>> _snapshots = new();

        /// <summary>当前可撤销步数</summary>
        public int UndoCount => _snapshots.Values.Sum(s => s.Count);

        /// <summary>
        /// 保存当前 Screen 的 Widgets 快照。在修改操作之前调用。
        /// </summary>
        /// <param name="screen">要保存快照的画面</param>
        public void PushSnapshot(Screen screen)
        {
            if (screen == null) return;

            if (!_snapshots.ContainsKey(screen))
                _snapshots[screen] = new Stack<byte[]>();

            var stack = _snapshots[screen];

            // 序列化 Widgets 集合为字节数组
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, screen.Widgets.ToList());
            stack.Push(ms.ToArray());

            // 超过上限时丢弃最旧的快照
            while (stack.Count > MaxSteps)
            {
                // Stack 底部是最旧的，需要重建栈
                var temp = stack.Reverse().Skip(1).Reverse().ToList();
                stack.Clear();
                foreach (var item in temp)
                    stack.Push(item);
            }
        }

        /// <summary>
        /// 撤销当前 Screen 的最后一次操作，恢复 Widgets 列表。
        /// </summary>
        /// <param name="screen">要恢复的画面</param>
        /// <returns>恢复后的 Widgets 列表，如果无快照则返回 null</returns>
        public List<Widget>? Undo(Screen screen)
        {
            if (screen == null) return null;
            if (!_snapshots.ContainsKey(screen) || _snapshots[screen].Count == 0)
                return null;

            var stack = _snapshots[screen];
            var data = stack.Pop();

            // 反序列化恢复 Widgets
            using var ms = new MemoryStream(data);
            return Serializer.Deserialize<List<Widget>>(ms);
        }

        /// <summary>
        /// 清除指定 Screen 的所有快照（切换画面时调用）。
        /// </summary>
        public void Clear(Screen screen)
        {
            if (screen != null && _snapshots.ContainsKey(screen))
                _snapshots.Remove(screen);
        }
    }
}
