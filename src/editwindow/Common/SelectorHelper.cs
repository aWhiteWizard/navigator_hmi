using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace NavigatorHMI.Common
{
    public static class SelectorHelper
    {
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.RegisterAttached(
                "IsSelected",
                typeof(bool),
                typeof(SelectorHelper),
                new PropertyMetadata(false, OnIsSelectedChanged));

        public static void SetIsSelected(DependencyObject obj, bool value) => obj.SetValue(IsSelectedProperty, value);
        public static bool GetIsSelected(DependencyObject obj) => (bool)obj.GetValue(IsSelectedProperty);

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = d as Button;
            if (button == null) return;

            var widget = button.DataContext as ButtonWidget;
            if (widget == null) return;

            var adornerLayer = AdornerLayer.GetAdornerLayer(button);
            if (adornerLayer == null) return;

            if ((bool)e.NewValue)
            {
                var adorner = new ResizeAdorner(button, widget);
                adornerLayer.Add(adorner);
                // 存储在 button.Tag 中，便于移除
                button.Tag = adorner;
            }
            else
            {
                if (button.Tag is ResizeAdorner oldAdorner)
                {
                    adornerLayer.Remove(oldAdorner);
                    button.Tag = null;
                }
            }
        }
    }
}
