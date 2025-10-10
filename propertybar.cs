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
    public partial class propertybar : Form
    {
        public propertybar()
        {
            InitializeComponent();
        }
        public void changeLocation()
        {

            int right = 16;
            int top = Application.OpenForms["main_window"].Height - this.Height;
            this.Location = new Point(right, top);
        }
        private void propertybar_Load(object sender, EventArgs e)
        {
            this.changeLocation();
        }
    }
}
