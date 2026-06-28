using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NavigatorHMI.Common
{
    public static class SelectorHelper
    {
        private static Dictionary<Button, Adorner> _adornerMap = new Dictionary<Button, Adorner>();

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.RegisterAttached(
                "IsSelected",
                typeof(bool),
                typeof(SelectorHelper),
                new PropertyMetadata(false, OnIsSelectedChanged));

        public static void SetIsSelected(DependencyObject element, bool value)
        {
            element.SetValue(IsSelectedProperty, value);
        }

        public static bool GetIsSelected(DependencyObject element)
        {
            return (bool)element.GetValue(IsSelectedProperty);
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = d as Button;
            if (button == null) return;

            var widget = button.DataContext as ButtonWidget;
            if (widget == null) return;

            if ((bool)e.NewValue)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(button);
                if (adornerLayer != null)
                {
                    // 先创建一个简单的 Adorner 测试
                    var adorner = new ResizeAdorner(button, widget);
                    adornerLayer.Add(adorner);
                }
            }
            else
            {
                // 取消选中：移除 Adorner
                RemoveAdorner(button);
                System.Diagnostics.Debug.WriteLine($"✅ 移除 Adorner 从按钮: {widget.Text}");
            }
        }

        /// <summary>
        /// 移除按钮上的 Adorner
        /// </summary>
        private static void RemoveAdorner(Button button)
        {
            if (button == null) return;

            // 从 Dictionary 中移除
            if (_adornerMap.TryGetValue(button, out var adorner))
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(button);
                if (adornerLayer != null)
                {
                    adornerLayer.Remove(adorner);
                    _adornerMap.Remove(button);
                    System.Diagnostics.Debug.WriteLine("✅ 从 Dictionary 移除 Adorner");
                }
            }
            else
            {
                // 备用方法：遍历 AdornerLayer
                var adornerLayer = AdornerLayer.GetAdornerLayer(button);
                if (adornerLayer != null)
                {
                    var adorners = adornerLayer.GetAdorners(button);
                    if (adorners != null)
                    {
                        foreach (var a in adorners)
                        {
                            if (a is ResizeAdorner)
                            {
                                adornerLayer.Remove(a);
                                System.Diagnostics.Debug.WriteLine("✅ 通过遍历移除 Adorner");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 清除所有按钮的 Adorner（用于切换画面时）
        /// </summary>
        public static void ClearAllAdorners()
        {
            foreach (var kvp in _adornerMap.ToList())
            {
                var button = kvp.Key;
                var adorner = kvp.Value;
                var adornerLayer = AdornerLayer.GetAdornerLayer(button);
                if (adornerLayer != null)
                {
                    adornerLayer.Remove(adorner);
                }
            }
            _adornerMap.Clear();
            System.Diagnostics.Debug.WriteLine("✅ 已清除所有 Adorner");
        }

    }
}