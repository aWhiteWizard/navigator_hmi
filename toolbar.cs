using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FutureTechHMI
{
    public partial class toolbar : Form
    {
        public event Action<Label> LabelCreated;

        public toolbar()
        {
            InitializeComponent();
        }

        public void changeLocation()
        {
            
            int right = Application.OpenForms["main_window"].Right - Application.OpenForms["main_window"].Left - this.Width - 16;
            int top = 25;
            this.Location = new Point(right, top);
        }
        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            
        }

        private void button1_Click(object sender, EventArgs e)
        {
            labelobject labelobject = new labelobject();
            labelobject.Text = $"Label{this.Controls.Count + 1}";
            labelobject.Tag = "customlabel";

            LabelCreated?.Invoke(labelobject);
        }

        private void button2_Click(object sender, EventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {

        }

        private void toolbar_Load(object sender, EventArgs e)
        {
            this.changeLocation();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void tabPage1_Click(object sender, EventArgs e)
        {

        }
    }
}
