using System;
using System.Windows;
using System.Windows.Controls;

namespace NavigatorHMI.Views
{
    public partial class GridArrayDialog : Window
    {
        /// <summary>矩形阵列起始 X 坐标。</summary>
        public double StartX { get; private set; }
        /// <summary>矩形阵列起始 Y 坐标。</summary>
        public double StartY { get; private set; }
        /// <summary>列间距。</summary>
        public double SpacingX { get; private set; }
        /// <summary>行间距。</summary>
        public double SpacingY { get; private set; }
        /// <summary>列数。</summary>
        public int Cols { get; private set; }
        /// <summary>行数。</summary>
        public int Rows { get; private set; }
        /// <summary>圆形阵列圆心 X。</summary>
        public double CenterX { get; private set; }
        /// <summary>圆形阵列圆心 Y。</summary>
        public double CenterY { get; private set; }
        /// <summary>圆形阵列半径。</summary>
        public double Radius { get; private set; }
        /// <summary>起始角度(度)。</summary>
        public double StartAngle { get; private set; }
        /// <summary>终止角度(度)。</summary>
        public double EndAngle { get; private set; }

        private readonly bool _isCircle;
        private readonly TextBox _sx, _sy, _cols, _rows, _startX, _startY, _cx, _cy, _rad, _start, _end;
        private readonly Action? _preview;

        public GridArrayDialog(bool isCircle, int itemCount, double defCx, double defCy, Action? preview = null)
        {
            _isCircle = isCircle; _preview = preview;
            Width = 280; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = isCircle ? "圆形阵列" : "矩形阵列"; ResizeMode = ResizeMode.NoResize;
            var root = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };

            if (!isCircle)
            {
                root.Children.Add(MakeRow("起始 X:", out _startX, "50")); root.Children.Add(MakeRow("起始 Y:", out _startY, "50"));
                root.Children.Add(MakeRow("列数:", out _cols, "3")); root.Children.Add(MakeRow("行数:", out _rows, "2"));
                root.Children.Add(MakeRow("列距:", out _sx, "120")); root.Children.Add(MakeRow("行距:", out _sy, "80"));
                _cols.TextChanged += (_, _) => InvokePreview(); _rows.TextChanged += (_, _) => InvokePreview();
                _startX.TextChanged += (_, _) => InvokePreview(); _startY.TextChanged += (_, _) => InvokePreview();
                _sx.TextChanged += (_, _) => InvokePreview(); _sy.TextChanged += (_, _) => InvokePreview();
            }
            else
            {
                root.Children.Add(MakeRow("圆心 X:", out _cx, defCx.ToString("F0")));
                root.Children.Add(MakeRow("圆心 Y:", out _cy, defCy.ToString("F0")));
                root.Children.Add(MakeRow("半径:", out _rad, "150"));
                root.Children.Add(MakeRow("起始角度:", out _start, "0"));
                root.Children.Add(MakeRow("终止角度:", out _end, "360"));
                _cx.TextChanged += (_, _) => InvokePreview(); _cy.TextChanged += (_, _) => InvokePreview();
                _rad.TextChanged += (_, _) => InvokePreview(); _start.TextChanged += (_, _) => InvokePreview(); _end.TextChanged += (_, _) => InvokePreview();
            }
            root.Children.Add(new Label { Content = $"共 {itemCount} 个控件", FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var okBtn = new Button { Content = "确定", Width = 80, Height = 26, Margin = new Thickness(0, 0, 10, 0) };
            var cancelBtn = new Button { Content = "取消", Width = 80, Height = 26 };
            okBtn.Click += (_, _) => { ParseAndClose(); };
            cancelBtn.Click += (_, _) => Close();
            btnPanel.Children.Add(okBtn); btnPanel.Children.Add(cancelBtn);
            root.Children.Add(btnPanel);
            Content = root;
            ParseParams();
        }

        public void InvokePreview() { if (ParseParams()) _preview?.Invoke(); }

        private bool ParseParams()
        {
            if (!_isCircle)
            {
                if (!int.TryParse(_cols.Text, out int c) || !int.TryParse(_rows.Text, out int r)) return false;
                // 列/行数范围校验（≥1，非法标红）
                bool colOk = c >= 1, rowOk = r >= 1;
                _cols.Background = colOk ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.LightPink;
                _rows.Background = rowOk ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.LightPink;
                if (!colOk || !rowOk) return false;
                if (!double.TryParse(_startX.Text, out double sx0) || !double.TryParse(_startY.Text, out double sy0)) return false;
                if (!double.TryParse(_sx.Text, out double sx) || !double.TryParse(_sy.Text, out double sy)) return false;
                Cols = c; Rows = r; StartX = sx0; StartY = sy0; SpacingX = sx; SpacingY = sy;
            }
            else
            {
                if (!double.TryParse(_cx.Text, out double cx) || !double.TryParse(_cy.Text, out double cy)) return false;
                if (!double.TryParse(_rad.Text, out double rad) || !double.TryParse(_start.Text, out double s) || !double.TryParse(_end.Text, out double e)) return false;
                // 角度验证
                bool startOk = s >= 0 && s <= 360, endOk = e >= 0 && e <= 360;
                _start.Background = startOk ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.LightPink;
                _end.Background = endOk ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.LightPink;
                if (!startOk || !endOk) return false;
                CenterX = cx; CenterY = cy; Radius = rad; StartAngle = s; EndAngle = e;
            }
            return true;
        }

        private void ParseAndClose() { if (ParseParams()) { DialogResult = true; Close(); } }

        private static StackPanel MakeRow(string label, out TextBox textBox, string defaultValue)
        {
            textBox = new TextBox { Text = defaultValue, Width = 80, Height = 22 };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new Label { Content = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(textBox);
            return row;
        }
    }
}
