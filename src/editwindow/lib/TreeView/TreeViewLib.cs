using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace editwindow.lib.TreeView
{
    public class SceneInfo
    {
        #region 属性

        /// <summary>
        /// 场景名称
        /// </summary>
        public string SceneName { get; set; }

        /// <summary>
        /// 画布名称
        /// </summary>
        public string CanvasName { get; set; }

        /// <summary>
        /// 对应的画布控件
        /// </summary>
        public Canvas CanvasControl { get; set; }

        /// <summary>
        /// 对应的TreeView项
        /// </summary>
        public TreeViewItem TreeItem { get; set; }

        /// <summary>
        /// 场景中的所有控件列表
        /// </summary>
        public List<ControlInfo> Controls { get; private set; }

        /// <summary>
        /// 父场景（用于层级结构）
        /// </summary>
        public SceneInfo ParentScene { get; set; }

        /// <summary>
        /// 子场景列表
        /// </summary>
        public List<SceneInfo> ChildScenes { get; private set; }

        /// <summary>
        /// 场景背景颜色
        /// </summary>
        public Brush BackgroundColor { get; set; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 基本构造函数
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <param name="canvasName">画布名称</param>
        public SceneInfo(string sceneName, string canvasName)
        {
            SceneName = sceneName;
            CanvasName = canvasName;
            Controls = new List<ControlInfo>();
            ChildScenes = new List<SceneInfo>();
            BackgroundColor = Brushes.White;
        }

        /// <summary>
        /// 完整构造函数
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <param name="canvasName">画布名称</param>
        /// <param name="canvasControl">画布控件</param>
        /// <param name="treeItem">TreeView项</param>
        public SceneInfo(string sceneName, string canvasName, Canvas canvasControl, TreeViewItem treeItem)
            : this(sceneName, canvasName)
        {
            CanvasControl = canvasControl;
            TreeItem = treeItem;
        }

        /// <summary>
        /// 复制构造函数
        /// </summary>
        /// <param name="source">源场景</param>
        public SceneInfo(SceneInfo source)
        {
            SceneName = source.SceneName + " - 副本";
            CanvasName = source.CanvasName + "_Copy";
            Controls = new List<ControlInfo>();
            ChildScenes = new List<SceneInfo>();
            BackgroundColor = source.BackgroundColor?.Clone();

            // 深度复制控件列表
            foreach (var control in source.Controls)
            {
                Controls.Add(new ControlInfo
                {
                    Type = control.Type,
                    Content = control.Content,
                    X = control.X,
                    Y = control.Y,
                    SceneName = SceneName,
                    Control = null // 不复制实际控件引用
                });
            }

            // 深度复制子场景
            foreach (var childScene in source.ChildScenes)
            {
                ChildScenes.Add(new SceneInfo(childScene)
                {
                    ParentScene = this
                });
            }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 添加控件到场景
        /// </summary>
        /// <param name="controlInfo">控件信息</param>
        public void AddControl(ControlInfo controlInfo)
        {
            if (controlInfo != null)
            {
                controlInfo.SceneName = SceneName;
                Controls.Add(controlInfo);
            }
        }

        /// <summary>
        /// 从场景中移除控件
        /// </summary>
        /// <param name="controlInfo">控件信息</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveControl(ControlInfo controlInfo)
        {
            return Controls.Remove(controlInfo);
        }

        /// <summary>
        /// 根据控件对象查找控件信息
        /// </summary>
        /// <param name="control">控件对象</param>
        /// <returns>控件信息</returns>
        public ControlInfo FindControlInfo(FrameworkElement control)
        {
            return Controls.FirstOrDefault(c => c.Control == control);
        }

        /// <summary>
        /// 获取场景中特定类型的控件
        /// </summary>
        /// <param name="controlType">控件类型</param>
        /// <returns>控件列表</returns>
        public List<ControlInfo> GetControlsByType(string controlType)
        {
            return Controls.Where(c => c.Type == controlType).ToList();
        }

        /// <summary>
        /// 添加子场景
        /// </summary>
        /// <param name="childScene">子场景</param>
        public void AddChildScene(SceneInfo childScene)
        {
            if (childScene != null && !ChildScenes.Contains(childScene))
            {
                childScene.ParentScene = this;
                ChildScenes.Add(childScene);
            }
        }

        /// <summary>
        /// 移除子场景
        /// </summary>
        /// <param name="childScene">子场景</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveChildScene(SceneInfo childScene)
        {
            if (ChildScenes.Remove(childScene))
            {
                childScene.ParentScene = null;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检查场景是否包含特定名称的子场景
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <returns>是否包含</returns>
        public bool HasChildScene(string sceneName)
        {
            return ChildScenes.Any(scene => scene.SceneName == sceneName);
        }

        /// <summary>
        /// 获取场景的完整路径（包含父场景）
        /// </summary>
        /// <returns>完整路径</returns>
        public string GetFullPath()
        {
            var path = SceneName;
            var current = ParentScene;

            while (current != null)
            {
                path = current.SceneName + " / " + path;
                current = current.ParentScene;
            }

            return path;
        }

        /// <summary>
        /// 清空场景中的所有控件
        /// </summary>
        public void ClearControls()
        {
            Controls.Clear();
        }

        /// <summary>
        /// 获取场景中控件的数量
        /// </summary>
        /// <returns>控件数量</returns>
        public int GetControlCount()
        {
            return Controls.Count;
        }

        /// <summary>
        /// 获取场景层级深度
        /// </summary>
        /// <returns>深度值</returns>
        public int GetDepth()
        {
            int depth = 0;
            var current = ParentScene;

            while (current != null)
            {
                depth++;
                current = current.ParentScene;
            }

            return depth;
        }

        #endregion

        #region 重写方法

        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>场景名称</returns>
        public override string ToString()
        {
            return SceneName;
        }

        /// <summary>
        /// 重写Equals方法
        /// </summary>
        /// <param name="obj">比较对象</param>
        /// <returns>是否相等</returns>
        public override bool Equals(object obj)
        {
            if (obj is SceneInfo other)
            {
                return SceneName == other.SceneName && CanvasName == other.CanvasName;
            }
            return false;
        }

        /// <summary>
        /// 重写GetHashCode方法
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
        {
            return (SceneName?.GetHashCode() ?? 0) ^ (CanvasName?.GetHashCode() ?? 0);
        }

        #endregion
    }

    public class ControlInfo
    {
        #region 属性

        /// <summary>
        /// 控件类型（Label、Button、TextBox等）
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 控件显示的内容
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 控件在画布上的X坐标
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// 控件在画布上的Y坐标
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 所属场景名称
        /// </summary>
        public string SceneName { get; set; }

        /// <summary>
        /// 实际的WPF控件对象
        /// </summary>
        public FrameworkElement Control { get; set; }

        /// <summary>
        /// 控件宽度
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 控件高度
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// 控件背景颜色
        /// </summary>
        public Brush Background { get; set; }

        /// <summary>
        /// 控件前景颜色
        /// </summary>
        public Brush Foreground { get; set; }

        /// <summary>
        /// 字体大小
        /// </summary>
        public double FontSize { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public System.DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public System.DateTime ModifiedTime { get; set; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public ControlInfo()
        {
            CreatedTime = System.DateTime.Now;
            ModifiedTime = System.DateTime.Now;
            FontSize = 12;
            Width = double.NaN; // 自动大小
            Height = double.NaN; // 自动大小
        }

        /// <summary>
        /// 带参数的构造函数
        /// </summary>
        /// <param name="type">控件类型</param>
        /// <param name="content">控件内容</param>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="sceneName">场景名称</param>
        public ControlInfo(string type, string content, double x, double y, string sceneName)
            : this()
        {
            Type = type;
            Content = content;
            X = x;
            Y = y;
            SceneName = sceneName;
        }

        #endregion

        #region 方法

        /// <summary>
        /// 更新修改时间
        /// </summary>
        public void UpdateModifiedTime()
        {
            ModifiedTime = System.DateTime.Now;
        }

        /// <summary>
        /// 获取控件的位置信息
        /// </summary>
        /// <returns>位置字符串</returns>
        public string GetPositionString()
        {
            return $"({X:F1}, {Y:F1})";
        }

        /// <summary>
        /// 获取控件的基本信息
        /// </summary>
        /// <returns>信息字符串</returns>
        public string GetInfoString()
        {
            return $"{Type}: {Content} at {GetPositionString()}";
        }

        #endregion

        #region 重写方法

        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>控件信息</returns>
        public override string ToString()
        {
            return GetInfoString();
        }

        #endregion
    }
}