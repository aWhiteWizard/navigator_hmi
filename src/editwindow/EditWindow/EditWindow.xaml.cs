using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Linq.Expressions;

namespace NavigatorHMI.EditWindow
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class EditWindow : Window
    {

        public EditWindow()
        {
            InitializeComponent();
        }

        public void ScreenTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
        }

        private void CutScene_Click(object sender, RoutedEventArgs e)
        {
            // 暂不实现
        }

        private void CopyScene_Click(object sender, RoutedEventArgs e)
        {
            // 暂不实现
        }

        private void PasteScene_Click(object sender, RoutedEventArgs e)
        {
            // 暂不实现
        }

    }
}