using editwindow.lib.TreeView;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NavigatorHMI.EditWindow.ProjectWindow
{
    public class ProjectBarDrawer : UserControl
    {
        public SceneInfo screenInfo;
        private bool isAddLabelMode = false;
        private int sceneCount = 1;
        private int labelCount = 0;
        private SceneInfo currentScene;
        private TreeViewItem ProjectbarTreeView;

        // 剪贴板相关
        private SceneInfo copiedScene;
        private bool isCutOperation = false;

        // 存储所有场景
        private Dictionary<string, SceneInfo> scenes = new Dictionary<string, SceneInfo>();

        public ProjectBarDrawer(SceneInfo screceinfo, TreeViewItem treeViewItem)
        {
            screenInfo = screceinfo;
            ProjectbarTreeView = treeViewItem;
        }
        
        
        // 开始重命名场景 - 双击或右键菜单调用
        public void StartRenameScene()
        {
            // 创建编辑框
            TextBox editTextBox = new TextBox
            {
                Text = screenInfo.SceneName,
                FontSize = 12,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Background = Brushes.White
            };

            // 替换TreeViewItem的内容
            screenInfo.TreeItem.Header = editTextBox;
            editTextBox.Focus();
            editTextBox.SelectAll();

            // 处理编辑完成
            editTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    FinishRenameScene(screenInfo, editTextBox.Text);
                }
                else if (e.Key == Key.Escape)
                {
                    CancelRenameScene(screenInfo);
                }
            };

            editTextBox.LostFocus += (s, e) =>
            {
                if (screenInfo.TreeItem.Header is TextBox)
                {
                    FinishRenameScene(screenInfo, editTextBox.Text);
                }
            };
        }

        // 完成重命名
        private void FinishRenameScene(SceneInfo sceneInfo, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                CancelRenameScene(sceneInfo);
                return;
            }

            // 检查名称是否重复
            if (scenes.ContainsKey(newName) && newName != sceneInfo.SceneName)
            {
                MessageBox.Show("场景名称不能重复！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                CancelRenameScene(sceneInfo);
                return;
            }

            string oldName = sceneInfo.SceneName;

            // 更新场景信息
            scenes.Remove(oldName);
            sceneInfo.SceneName = newName;
            scenes[newName] = sceneInfo;

            // 更新TreeViewItem
            sceneInfo.TreeItem.Header = newName;

            // 更新画布中的默认文本
            if (sceneInfo.CanvasControl.Children.Count > 0 &&
                sceneInfo.CanvasControl.Children[0] is TextBlock textBlock)
            {
                textBlock.Text = newName;
            }

            // 更新控件中的场景名称
            foreach (var control in sceneInfo.Controls)
            {
                control.SceneName = newName;
            }

            // UpdateStatusText();
        }

        // 取消重命名
        private void CancelRenameScene(SceneInfo sceneInfo)
        {
            sceneInfo.TreeItem.Header = sceneInfo.SceneName;
        }
    }
}
