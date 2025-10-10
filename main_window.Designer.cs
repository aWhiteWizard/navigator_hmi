using System.Windows.Forms;

namespace FutureTechHMI
{
    partial class main_window
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            
            this.menuStripTop = new System.Windows.Forms.MenuStrip();
            this.文件FToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.新建NToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.保存SToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.另存为RToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.关闭CToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.编辑EToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.查找FToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.视图VToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.重新布局控件ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.工具箱XToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.项目树NToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.属性窗PToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.生成BToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.biToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            background = new System.Windows.Forms.Panel();
            this.menuStripTop.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStripTop
            // 
            this.menuStripTop.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.文件FToolStripMenuItem,
            this.编辑EToolStripMenuItem,
            this.视图VToolStripMenuItem,
            this.生成BToolStripMenuItem});
            this.menuStripTop.Location = new System.Drawing.Point(0, 0);
            this.menuStripTop.Name = "menuStripTop";
            this.menuStripTop.Size = new System.Drawing.Size(884, 25);
            this.menuStripTop.TabIndex = 1;
            this.menuStripTop.Text = "menuStripTop";
            this.menuStripTop.ItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this.menuStripTop_ItemClicked);
            // 
            // 文件FToolStripMenuItem
            // 
            this.文件FToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.新建NToolStripMenuItem,
            this.保存SToolStripMenuItem,
            this.另存为RToolStripMenuItem,
            this.关闭CToolStripMenuItem});
            this.文件FToolStripMenuItem.Name = "文件FToolStripMenuItem";
            this.文件FToolStripMenuItem.Size = new System.Drawing.Size(58, 21);
            this.文件FToolStripMenuItem.Text = "文件(F)";
            // 
            // 新建NToolStripMenuItem
            // 
            this.新建NToolStripMenuItem.Name = "新建NToolStripMenuItem";
            this.新建NToolStripMenuItem.Size = new System.Drawing.Size(128, 22);
            this.新建NToolStripMenuItem.Text = "新建(N)";
            // 
            // 保存SToolStripMenuItem
            // 
            this.保存SToolStripMenuItem.Name = "保存SToolStripMenuItem";
            this.保存SToolStripMenuItem.Size = new System.Drawing.Size(128, 22);
            this.保存SToolStripMenuItem.Text = "保存(S)";
            // 
            // 另存为RToolStripMenuItem
            // 
            this.另存为RToolStripMenuItem.Name = "另存为RToolStripMenuItem";
            this.另存为RToolStripMenuItem.Size = new System.Drawing.Size(128, 22);
            this.另存为RToolStripMenuItem.Text = "另存为(R)";
            // 
            // 关闭CToolStripMenuItem
            // 
            this.关闭CToolStripMenuItem.Name = "关闭CToolStripMenuItem";
            this.关闭CToolStripMenuItem.Size = new System.Drawing.Size(128, 22);
            this.关闭CToolStripMenuItem.Text = "关闭(C)";
            // 
            // 编辑EToolStripMenuItem
            // 
            this.编辑EToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.查找FToolStripMenuItem});
            this.编辑EToolStripMenuItem.Name = "编辑EToolStripMenuItem";
            this.编辑EToolStripMenuItem.Size = new System.Drawing.Size(59, 21);
            this.编辑EToolStripMenuItem.Text = "编辑(E)";
            // 
            // 查找FToolStripMenuItem
            // 
            this.查找FToolStripMenuItem.Name = "查找FToolStripMenuItem";
            this.查找FToolStripMenuItem.Size = new System.Drawing.Size(150, 22);
            this.查找FToolStripMenuItem.Text = "查找与替换(F)";
            // 
            // 视图VToolStripMenuItem
            // 
            this.视图VToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.重新布局控件ToolStripMenuItem,
            this.工具箱XToolStripMenuItem,
            this.项目树NToolStripMenuItem,
            this.属性窗PToolStripMenuItem});
            this.视图VToolStripMenuItem.Name = "视图VToolStripMenuItem";
            this.视图VToolStripMenuItem.Size = new System.Drawing.Size(60, 21);
            this.视图VToolStripMenuItem.Text = "视图(V)";
            // 
            // 重新布局控件ToolStripMenuItem
            // 
            this.重新布局控件ToolStripMenuItem.Name = "重新布局控件ToolStripMenuItem";
            this.重新布局控件ToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.重新布局控件ToolStripMenuItem.Text = "重新布局控件";
            // 
            // 工具箱XToolStripMenuItem
            // 
            this.工具箱XToolStripMenuItem.Checked = true;
            this.工具箱XToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            this.工具箱XToolStripMenuItem.Name = "工具箱XToolStripMenuItem";
            this.工具箱XToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.工具箱XToolStripMenuItem.Text = "工具箱(X)";
            this.工具箱XToolStripMenuItem.Click += new System.EventHandler(this.工具箱XToolStripMenuItem_Click);
            // 
            // 项目树NToolStripMenuItem
            // 
            this.项目树NToolStripMenuItem.Checked = true;
            this.项目树NToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            this.项目树NToolStripMenuItem.Name = "项目树NToolStripMenuItem";
            this.项目树NToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.项目树NToolStripMenuItem.Text = "项目树(N)";
            this.项目树NToolStripMenuItem.Click += new System.EventHandler(this.项目树NToolStripMenuItem_Click);
            // 
            // 属性窗PToolStripMenuItem
            // 
            this.属性窗PToolStripMenuItem.Checked = true;
            this.属性窗PToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            this.属性窗PToolStripMenuItem.Name = "属性窗PToolStripMenuItem";
            this.属性窗PToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
            this.属性窗PToolStripMenuItem.Text = "属性窗(P)";
            this.属性窗PToolStripMenuItem.Click += new System.EventHandler(this.属性窗PToolStripMenuItem_Click);
            // 
            // 生成BToolStripMenuItem
            // 
            this.生成BToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.biToolStripMenuItem});
            this.生成BToolStripMenuItem.Name = "生成BToolStripMenuItem";
            this.生成BToolStripMenuItem.Size = new System.Drawing.Size(60, 21);
            this.生成BToolStripMenuItem.Text = "生成(B)";
            // 
            // biToolStripMenuItem
            // 
            this.biToolStripMenuItem.Name = "biToolStripMenuItem";
            this.biToolStripMenuItem.Size = new System.Drawing.Size(121, 22);
            this.biToolStripMenuItem.Text = "编译(F5)";
            // 
            // background
            // 
            background.AllowDrop = true;
            background.DragDrop += new System.Windows.Forms.DragEventHandler(this.Background_DragDrop);
            background.DragEnter += new System.Windows.Forms.DragEventHandler(this.Background_DragEnter);

            background.DragOver += new System.Windows.Forms.DragEventHandler(this.Background_DragOver);
            background.DragLeave += new System.EventHandler(this.Background_DragLeave);

            background.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            background.AutoScroll = true;
            background.BackColor = System.Drawing.SystemColors.AppWorkspace;
            background.Location = new System.Drawing.Point(0, 25);
            background.Name = "background";
            background.Size = new System.Drawing.Size(885, 540);
            background.TabIndex = 2;
            // 
            // main_window
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 561);
            this.Controls.Add(background);
            this.Controls.Add(this.menuStripTop);
            this.Name = "main_window";
            this.Text = "NavigatorHMI";
            this.Load += new System.EventHandler(this.mainWindow_load);
            this.menuStripTop.ResumeLayout(false);
            this.menuStripTop.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStripTop;
        private System.Windows.Forms.Panel background;

        private System.Windows.Forms.ToolStripMenuItem 文件FToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 新建NToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 保存SToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 另存为RToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 关闭CToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 编辑EToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 查找FToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 视图VToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 重新布局控件ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 工具箱XToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 项目树NToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 属性窗PToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 生成BToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem biToolStripMenuItem;
    }
}

