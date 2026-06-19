using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NavigatorHMI.Common
{
    public class ResizeAdorner : Adorner
    {
        private readonly VisualCollection _visualChildren;
        private readonly Thumb[] _thumbs;
        private readonly FrameworkElement _adornedElement;
        private readonly ButtonWidget _widget;

        public ResizeAdorner(FrameworkElement adornedElement, ButtonWidget widget) : base(adornedElement)
        {
            _adornedElement = adornedElement;
            _widget = widget;
            _visualChildren = new VisualCollection(this);
            _thumbs = new Thumb[8];
            for (int i = 0; i < 8; i++)
            {
                var thumb = new Thumb
                {
                    Width = 10,
                    Height = 10,
                    Background = Brushes.White,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Cursor = GetCursor((ResizeDirection)i)
                };
                thumb.DragDelta += Thumb_DragDelta;
                thumb.Tag = (ResizeDirection)i;
                _visualChildren.Add(thumb);
                _thumbs[i] = thumb;
            }
        }

        private enum ResizeDirection { TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight }

        private Cursor GetCursor(ResizeDirection dir)
        {
            switch (dir)
            {
                case ResizeDirection.TopLeft: case ResizeDirection.BottomRight: return Cursors.SizeNWSE;
                case ResizeDirection.TopRight: case ResizeDirection.BottomLeft: return Cursors.SizeNESW;
                case ResizeDirection.Top: case ResizeDirection.Bottom: return Cursors.SizeNS;
                case ResizeDirection.Left: case ResizeDirection.Right: return Cursors.SizeWE;
                default: return Cursors.Arrow;
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double left = 0, top = 0, right = finalSize.Width, bottom = finalSize.Height;
            double half = 5;
            _thumbs[0].Arrange(new Rect(-half, -half, 10, 10)); // TopLeft
            _thumbs[1].Arrange(new Rect((right - 10) / 2, -half, 10, 10)); // Top
            _thumbs[2].Arrange(new Rect(right - half, -half, 10, 10)); // TopRight
            _thumbs[3].Arrange(new Rect(-half, (bottom - 10) / 2, 10, 10)); // Left
            _thumbs[4].Arrange(new Rect(right - half, (bottom - 10) / 2, 10, 10)); // Right
            _thumbs[5].Arrange(new Rect(-half, bottom - half, 10, 10)); // BottomLeft
            _thumbs[6].Arrange(new Rect((right - 10) / 2, bottom - half, 10, 10)); // Bottom
            _thumbs[7].Arrange(new Rect(right - half, bottom - half, 10, 10)); // BottomRight
            return finalSize;
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;
            var dir = (ResizeDirection)thumb.Tag;
            double newX = _widget.X;
            double newY = _widget.Y;
            double newW = _widget.Width;
            double newH = _widget.Height;

            switch (dir)
            {
                case ResizeDirection.TopLeft:
                    newW -= e.HorizontalChange;
                    newH -= e.VerticalChange;
                    newX += e.HorizontalChange;
                    newY += e.VerticalChange;
                    break;
                case ResizeDirection.Top:
                    newH -= e.VerticalChange;
                    newY += e.VerticalChange;
                    break;
                case ResizeDirection.TopRight:
                    newW += e.HorizontalChange;
                    newH -= e.VerticalChange;
                    newY += e.VerticalChange;
                    break;
                case ResizeDirection.Left:
                    newW -= e.HorizontalChange;
                    newX += e.HorizontalChange;
                    break;
                case ResizeDirection.Right:
                    newW += e.HorizontalChange;
                    break;
                case ResizeDirection.BottomLeft:
                    newW -= e.HorizontalChange;
                    newH += e.VerticalChange;
                    newX += e.HorizontalChange;
                    break;
                case ResizeDirection.Bottom:
                    newH += e.VerticalChange;
                    break;
                case ResizeDirection.BottomRight:
                    newW += e.HorizontalChange;
                    newH += e.VerticalChange;
                    break;
            }

            // 限制最小尺寸
            newW = Math.Max(20, newW);
            newH = Math.Max(20, newH);

            _widget.X = newX;
            _widget.Y = newY;
            _widget.Width = newW;
            _widget.Height = newH;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Rect rect = new Rect(0, 0, _adornedElement.ActualWidth, _adornedElement.ActualHeight);
            Pen pen = new Pen(Brushes.Black, 1);
            pen.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
            drawingContext.DrawRectangle(null, pen, rect);
        }

        protected override int VisualChildrenCount => _visualChildren.Count;
        protected override Visual GetVisualChild(int index) => _visualChildren[index];
    }
}
