using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NavigatorHMI.Common
{
    public static class SelectorHelper
    {
        private static Dictionary<FrameworkElement, Adorner> _adornerMap = new Dictionary<FrameworkElement, Adorner>();

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
            var element = d as FrameworkElement;
            if (element == null) return;

            var widget = element.DataContext as Widget;
            if (widget == null) return;

            if ((bool)e.NewValue)
            {
                // 防重复：同一元素已存在 Adorner 时先移除（防止选中状态快速切换导致多个 Adorner 叠加）
                if (_adornerMap.ContainsKey(element))
                {
                    RemoveAdorner(element);
                }

                var adornerLayer = AdornerLayer.GetAdornerLayer(element);
                if (adornerLayer != null)
                {
                    var adorner = new ResizeAdorner(element, widget);
                    adornerLayer.Add(adorner);
                    _adornerMap[element] = adorner;
                    System.Diagnostics.Debug.WriteLine($"✅ 创建 Adorner: {widget.ObjectName}, 元素={element.GetType().Name}, 尺寸={element.RenderSize.Width}x{element.RenderSize.Height}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ AdornerLayer 为 null，无法创建 Adorner: {widget.ObjectName}（视觉树中无 AdornerDecorator？）");
                }
            }
            else
            {
                RemoveAdorner(element);
                System.Diagnostics.Debug.WriteLine($"✅ 移除 Adorner: {widget.ObjectName}");
            }
        }

        private static void RemoveAdorner(FrameworkElement element)
        {
            if (element == null) return;

            if (_adornerMap.TryGetValue(element, out var adorner))
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(element);
                if (adornerLayer != null)
                {
                    adornerLayer.Remove(adorner);
                    _adornerMap.Remove(element);
                    System.Diagnostics.Debug.WriteLine("✅ 从 Dictionary 移除 Adorner");
                }
            }
            else
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(element);
                if (adornerLayer != null)
                {
                    var adorners = adornerLayer.GetAdorners(element);
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

        public static void ClearAllAdorners()
        {
            foreach (var kvp in _adornerMap.ToList())
            {
                var element = kvp.Key;
                var adorner = kvp.Value;
                var adornerLayer = AdornerLayer.GetAdornerLayer(element);
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