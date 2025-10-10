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
    public partial class projectbar : Form
    {
        public projectbar()
        {
            InitializeComponent();
        }

        public void changeLocation()
        {

            int right = Application.OpenForms["main_window"].Left + 16;
            int top = 25;
            this.Location = new Point(right, top);
        }

        private void projectbar_Load(object sender, EventArgs e)
        {
            this.changeLocation();
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {

        }
    }
}
