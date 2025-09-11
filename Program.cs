using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NavigatorHMI
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
        /*static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }*/
    }
    // 假设的主窗体类
    public partial class MainForm : Form
    {
        // 源面板 - 从这里拖拽创建Label
        private Panel sourcePanel;
        private bool isDragging = false;
        private Point dragStartPoint;
        private Label previewLabel; // 拖拽时的预览Label

        // 修复 CS0103: 当前上下文中不存在名称“InitializeComponent”
        // 由于 MainForm 没有设计器文件，直接移除 InitializeComponent() 调用即可。

        public MainForm()
        {
            // InitializeComponent(); // 移除此行
            SetupUI();
        }

        private void SetupUI()
        {
            // 设置窗口属性
            this.Text = "拖拽创建Label示例";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.AllowDrop = true;

            // 创建源面板 - 从这里拖拽创建Label
            sourcePanel = new Panel();
            sourcePanel.BackColor = Color.LightBlue;
            sourcePanel.BorderStyle = BorderStyle.FixedSingle;
            sourcePanel.Size = new Size(150, 100);
            sourcePanel.Location = new Point(20, 20);
            sourcePanel.MouseDown += SourcePanel_MouseDown;
            sourcePanel.MouseMove += SourcePanel_MouseMove;
            sourcePanel.MouseUp += SourcePanel_MouseUp;

            // 在源面板中添加说明文本
            Label sourceLabel = new Label();
            sourceLabel.Text = "label";
            sourceLabel.AutoSize = true;
            sourceLabel.Location = new Point(30, 40);
            sourceLabel.Font = new Font("微软雅黑", 10, FontStyle.Bold);
            sourcePanel.Controls.Add(sourceLabel);

            CreateAglinButton();
            // 添加到窗口
            this.Controls.Add(sourcePanel);

            // 注册窗体的鼠标事件，以便在整个窗口范围内跟踪拖拽
            this.MouseMove += MainForm_MouseMove;
            this.MouseUp += MainForm_MouseUp;
        }

        private void CreateAglinButton()
        {
            Button alignLeftButton = new Button();
            alignLeftButton.Text = "左对齐";
            alignLeftButton.Size = new Size(80, 30);
            alignLeftButton.Location = new Point(20, 140);
            alignLeftButton.Click += (s, e) => AlignLeft();

            Button alignVirtialButton = new Button();
            alignVirtialButton.Text = "垂直分布";
            alignVirtialButton.Size = new Size(80, 30);
            alignVirtialButton.Location = new Point(120, 140);
            alignVirtialButton.Click += (s, e) => DistributeVertically();

            Button alignRightButton = new Button();
            alignRightButton.Text = "右对齐";
            alignRightButton.Size = new Size(80, 30);
            alignRightButton.Location = new Point(220, 140);
            alignRightButton.Click += (s, e) => AlignRight();

            Button alignCenterButton = new Button();
            alignCenterButton.Text = "水平居中";
            alignCenterButton.Size = new Size(80, 30);
            alignCenterButton.Location = new Point(320, 140);
            alignCenterButton.Click += (s, e) => AlignCenter();

            Button alignTopButton = new Button();
            alignTopButton.Text = "顶对齐";
            alignTopButton.Size = new Size(80, 30);
            alignTopButton.Location = new Point(420, 140);
            alignTopButton.Click += (s, e) => AlignTop();

            Button alignBottomButton = new Button();
            alignBottomButton.Text = "底对齐";
            alignBottomButton.Size = new Size(80, 30);
            alignBottomButton.Location = new Point(520, 140);
            alignBottomButton.Click += (s, e) => AlignBottom();

            Button alignHorizontalDistributeButton = new Button();
            alignHorizontalDistributeButton.Text = "水平分布";
            alignHorizontalDistributeButton.Size = new Size(80, 30);
            alignHorizontalDistributeButton.Location = new Point(620, 140);
            alignHorizontalDistributeButton.Click += (s, e) => HorizontalDistribute();

            this.Controls.Add(alignRightButton);
            this.Controls.Add(alignBottomButton);
            this.Controls.Add(alignTopButton);
            this.Controls.Add(alignHorizontalDistributeButton);
            this.Controls.Add(alignLeftButton);
            this.Controls.Add(alignVirtialButton);
            this.Controls.Add(alignCenterButton);
        }

        private void SourcePanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStartPoint = e.Location;

                // 创建预览Label
                previewLabel = new Label();
                previewLabel.Text = "新Label";
                previewLabel.AutoSize = true;
                previewLabel.BackColor = Color.FromArgb(128, Color.Yellow); // 半透明黄色
                previewLabel.BorderStyle = BorderStyle.FixedSingle;
                previewLabel.Padding = new Padding(5);
                previewLabel.Location = sourcePanel.PointToScreen(e.Location);
                previewLabel.Parent = this;
                previewLabel.BringToFront();
            }
        }

        private void SourcePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging && previewLabel != null)
            {
                // 更新预览Label的位置
                Point screenPos = sourcePanel.PointToScreen(e.Location);
                Point clientPos = this.PointToClient(screenPos);
                previewLabel.Location = clientPos;
            }
        }

        private void SourcePanel_MouseUp(object sender, MouseEventArgs e)
        {
            CompleteDragOperation(e.Location);
        }

        private void MainForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging && previewLabel != null)
            {
                // 更新预览Label的位置
                previewLabel.Location = e.Location;
            }
        }

        private void MainForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                CompleteDragOperation(e.Location);
            }
        }

        private void CompleteDragOperation(Point location)
        {
            if (isDragging && previewLabel != null)
            {
                // 移除预览Label
                previewLabel.Dispose();
                previewLabel = null;

                // 创建实际的Label
                CreateNewLabel(location);

                isDragging = false;
            }
        }

        private void CreateNewLabel(Point location)
        {
            // 确保Label不会创建在源面板内部
            if (sourcePanel.Bounds.Contains(location))
                return;

            // 创建新的Label
            Label newLabel = new Label();
            newLabel.Text = $"Label {this.Controls.Count - 2}"; // 减去源面板和说明标签
            newLabel.AutoSize = true;
            newLabel.BackColor = Color.LightYellow;
            newLabel.BorderStyle = BorderStyle.FixedSingle;
            newLabel.Padding = new Padding(5);
            newLabel.Location = location;

            // 添加鼠标事件，使新Label也可拖动
            newLabel.MouseDown += Label_MouseDown;
            newLabel.MouseMove += Label_MouseMove;
            newLabel.MouseUp += Label_MouseUp;
            newLabel.Cursor = Cursors.Hand;

            // 添加到窗口
            this.Controls.Add(newLabel);
            newLabel.BringToFront();

            // 添加双击删除功能
            newLabel.DoubleClick += (s, e) => {
                if (MessageBox.Show("删除这个Label?", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    this.Controls.Remove(newLabel);
                    newLabel.Dispose();
                }
            };
        }

        // 以下代码使新创建的Label也可以被拖动
        private bool labelDragging = false;
        private Point labelDragStartPoint;
        private Label draggedLabel;

        private void Label_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                labelDragging = true;
                draggedLabel = (Label)sender;
                labelDragStartPoint = e.Location;
                draggedLabel.BackColor = Color.LightCoral; // 拖动时改变颜色
            }
        }

        private void Label_MouseMove(object sender, MouseEventArgs e)
        {
            if (labelDragging && draggedLabel != null)
            {
                // 计算新位置
                Point newLocation = new Point(
                    draggedLabel.Location.X + e.X - labelDragStartPoint.X,
                    draggedLabel.Location.Y + e.Y - labelDragStartPoint.Y
                );

                // 确保Label不会移出窗口边界
                newLocation.X = Math.Max(0, Math.Min(newLocation.X, this.ClientSize.Width - draggedLabel.Width));
                newLocation.Y = Math.Max(0, Math.Min(newLocation.Y, this.ClientSize.Height - draggedLabel.Height));

                // 更新Label位置
                draggedLabel.Location = newLocation;
            }
        }

        private void Label_MouseUp(object sender, MouseEventArgs e)
        {
            if (labelDragging && draggedLabel != null)
            {
                labelDragging = false;
                draggedLabel.BackColor = Color.LightYellow; // 恢复原颜色
                draggedLabel = null;
            }
        }

        private void AlignLeft()
        {
            // 修复 CS0019: 运算符“!=”无法应用于“Label”和“Panel”类型的操作数
            // 原代码：var labels = this.Controls.OfType<Label>().Where(l => l != sourcePanel);
            // 修正为：只获取类型为 Label 的控件，不需要与 Panel 比较
            var labels = this.Controls.OfType<Label>().ToList();

            if (labels.Any())
            {
                int minX = labels.Min(l => l.Left);
                foreach (var label in labels)
                {
                    label.Left = minX;
                }
            }
        }

        private void AlignRight()
        {
            var labels = this.Controls.OfType<Label>().ToList();
            if (labels.Any())
            {
                int maxRight = labels.Max(l => l.Right);
                foreach (var label in labels)
                {
                    label.Left = maxRight - label.Width;
                }
            }
        }

        private void AlignCenter()
        {
            var labels = this.Controls.OfType<Label>().ToList();
            if (labels.Any())
            {
                int centerX = this.ClientSize.Width / 2;
                foreach (var label in labels)
                {
                    label.Left = centerX - label.Width / 2;
                }
            }
        }

        private void AlignTop()
        {
            var labels = this.Controls.OfType<Label>().ToList();
            if (labels.Any())
            {
                int minY = labels.Min(l => l.Top);
                foreach (var label in labels)
                {
                    label.Top = minY;
                }
            }
        }

        private void AlignBottom()
        {
            var labels = this.Controls.OfType<Label>().ToList();
            if (labels.Any())
            {
                int maxBottom = labels.Max(l => l.Bottom);
                foreach (var label in labels)
                {
                    label.Top = maxBottom - label.Height;
                }
            }
        }

        private void HorizontalDistribute()
        {
            // 获取所有可拖动的Label并按X坐标排序
            var labels = this.Controls.OfType<Label>()
                           .Where(l => !sourcePanel.Controls.Contains(l))
                           .OrderBy(l => l.Left).ToList();
            if (labels.Count > 2)
            {
                int totalWidth = labels.Last().Right - labels.First().Left;
                int spacing = totalWidth / (labels.Count - 1);
                for (int i = 1; i < labels.Count - 1; i++)
                {
                    labels[i].Left = labels.First().Left + i * spacing - labels[i].Width / 2;
                }
            }
        }
        private void DistributeVertically()
        {
            // 获取所有可拖动的Label并按Y坐标排序
            var labels = this.Controls.OfType<Label>()
                           .Where(l => !sourcePanel.Controls.Contains(l))
                           .OrderBy(l => l.Top).ToList();

            if (labels.Count > 2)
            {
                int totalHeight = labels.Last().Bottom - labels.First().Top;
                int spacing = totalHeight / (labels.Count - 1);

                for (int i = 1; i < labels.Count - 1; i++)
                {
                    labels[i].Top = labels.First().Top + i * spacing - labels[i].Height / 2;
                }
            }
        }
    }
}
