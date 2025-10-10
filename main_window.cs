using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;

namespace FutureTechHMI
{
    public partial class main_window : Form
    {
        private toolbar toolbarForm;
        private Form propertyForm;
        private Form projectTreeForm;
        private Form outputForm;


        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new main_window());
        }
        public main_window()
        {
            InitializeComponent();
        }


        private void mainWindow_load(object sender, EventArgs e)
        {

        }

        private void 保存SToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void 查找FToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void 属性窗PToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.Controls.ContainsKey("propertybar")){
                propertyForm.Close();
            }
            else{
                propertyForm = new propertybar();
                propertyForm.TopLevel = false;
                propertyForm.Parent = this;
                propertyForm.Dock = DockStyle.Bottom;
                propertyForm.Show();
                propertyForm.BringToFront();
            }
        }

        private void Background_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(labelobject)))
            {
                e.Effect = DragDropEffects.Move;

                // 实时更新预览位置（如果需要更复杂的效果）
                //UpdateDragPreviewPosition(e);
            }
        }

        private void Background_DragLeave(object sender, EventArgs e)
        {
            // 恢复Panel外观
            this.background.BackColor = Color.White;
        }

        private void Background_DragEnter(object sender, DragEventArgs e)
        {
            // 检查拖动的数据是否是Label类型
            if (e.Data.GetDataPresent(typeof(labelobject)))
            {
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }
        private void Background_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(labelobject)))
            {
                // 获取被拖动的Label
                labelobject draggedLabel = (labelobject)e.Data.GetData(typeof(labelobject));
                //labelobject newLabel = CreateLabelFromOriginal(draggedLabel);
                // 计算放置位置（相对于Panel）
                Point dropPoint = this.background.PointToClient(new Point(e.X , e.Y));

                // 调整位置，让鼠标在Label中心.
                dropPoint.X -= draggedLabel.Width / 2;
                dropPoint.Y -= draggedLabel.Height / 2;

                // 确保位置在Panel范围内
                dropPoint.X = Math.Max(0, Math.Min(dropPoint.X, this.Width));
                dropPoint.Y = Math.Max(0, Math.Min(dropPoint.Y, this.Height));

                // 如果Label已经有父控件，先移除
                if (draggedLabel.Parent != null)
                {
                    draggedLabel.Parent.Controls.Remove(draggedLabel);
                }

                // 添加到Panel
                draggedLabel.Location = dropPoint;
                this.background.Controls.Add(draggedLabel);

                // 确保Label在最前面
                draggedLabel.BringToFront();
            }
        }

        private labelobject CreateLabelFromOriginal(labelobject original)
        {
            // 创建新的标签实例
            labelobject newLabel = new labelobject();
            newLabel.Text = original.Text;
            newLabel.BackColor = original.BackColor;
            newLabel.Font = original.Font;
            newLabel.ForeColor = original.ForeColor;
            newLabel.Tag = "dropped_label";

            return newLabel;
        }

        private void menuStripTop_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void 工具箱XToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(this.Controls.ContainsKey("toolbar")){
                toolbarForm.Close();
            }
            else{
                toolbarForm = new toolbar();

                // 订阅事件
                toolbarForm.LabelCreated += (label) =>
                {
                    // 这里接收到从子窗口传递过来的labelobject
                    // 可以将其添加到Panel中或进行其他处理
                    this.background.Controls.Add(label);
                };

                toolbarForm.TopLevel = false;
                toolbarForm.Parent = this;
                toolbarForm.Dock = DockStyle.Right;
                toolbarForm.FormBorderStyle = FormBorderStyle.None; // 去掉边框
                toolbarForm.Show();
                toolbarForm.BringToFront();
            }
        }

        private void 项目树NToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(this.Controls.ContainsKey("projectbar")){
                projectTreeForm.Close();
            }
            else{
                projectTreeForm = new projectbar();
                projectTreeForm.TopLevel = false;
                projectTreeForm.Parent = this;
                projectTreeForm.Dock = DockStyle.Left;
                projectTreeForm.FormBorderStyle = FormBorderStyle.FixedToolWindow; // 去掉边框
                projectTreeForm.Show();
                projectTreeForm.BringToFront();
            }
        }


        private void AddLabelToPanel(Label label, Point location)
        {
            // 如果Label已经有父控件，先移除
            if (label.Parent != null)
            {
                label.Parent.Controls.Remove(label);
            }

            // 添加到Panel
            label.Location = location;
            this.Controls.Add(label);

            // 确保Label可以接收鼠标事件
            label.BringToFront();
        }

    }
}
