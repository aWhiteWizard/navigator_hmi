using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FutureTechHMI
{
    public class labelobject : Label
    {
        public labelobject()
        {
            this.MouseDown += DraggableLabel_MouseDown;
            this.MouseMove += DraggableLabel_MouseMove;
            this.MouseUp += DraggableLabel_MouseUp;

            this.BorderStyle = BorderStyle.FixedSingle;
            this.AutoSize = true;
            this.BackColor = Color.LightBlue;
            this.Padding = new Padding(3);
        }


        private void DraggableLabel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStartPoint = e.Location;
                this.DoDragDrop(this, DragDropEffects.Move);
            }
        }

        private void DraggableLabel_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging && e.Button == MouseButtons.Left)
            {
                // 计算移动距离，避免误触
                if (Math.Abs(e.X - dragStartPoint.X) > 3 || Math.Abs(e.Y - dragStartPoint.Y) > 3)
                {
                    CreateDragPreview();
                    this.DoDragDrop(this, DragDropEffects.Move);
                    isDragging = false;
                }
            }
        }

        private void DraggableLabel_MouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
            RemoveDragPreview();
        }

        private void CreateDragPreview()
        {
            // 创建预览窗体
            dragPreviewForm = new Form();
            dragPreviewForm.FormBorderStyle = FormBorderStyle.None;
            dragPreviewForm.ShowInTaskbar = false;
            dragPreviewForm.TopMost = true;
            dragPreviewForm.BackColor = Color.Gray;
            dragPreviewForm.TransparencyKey = Color.Gray; // 设置透明色
            dragPreviewForm.Opacity = 0.7; // 半透明效果

            // 创建预览标签
            previewLabel = new Label();
            previewLabel.Text = this.Text;
            previewLabel.Font = this.Font;
            previewLabel.AutoSize = true;
            previewLabel.BackColor = Color.LightBlue;
            previewLabel.BorderStyle = BorderStyle.FixedSingle;
            previewLabel.Padding = new Padding(5);

            // 设置预览窗体大小
            dragPreviewForm.Size = new Size(previewLabel.Width + 2, previewLabel.Height + 2);
            dragPreviewForm.Controls.Add(previewLabel);
            previewLabel.Location = new Point(1, 1);

            // 显示预览窗体
            Point screenPos = Control.MousePosition;
            dragPreviewForm.Location = new Point(screenPos.X - previewLabel.Width / 2, screenPos.Y - previewLabel.Height / 2);
            dragPreviewForm.Show();
        }

        private void RemoveDragPreview()
        {
            if (dragPreviewForm != null)
            {
                dragPreviewForm.Close();
                dragPreviewForm.Dispose();
                dragPreviewForm = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RemoveDragPreview();
            }
            base.Dispose(disposing);
        }

        private Form dragPreviewForm;
        private Label previewLabel;
        private bool isDragging = false;
        private Point dragStartPoint;

    }
}
