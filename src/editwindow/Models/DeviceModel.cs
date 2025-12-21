using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NavigatorHMI.Models
{
    public class DeviceModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        private ObservableCollection<string> _supportedSizes;
        public ObservableCollection<string> SupportedSizes
        {
            get => _supportedSizes;
            set
            {
                _supportedSizes = value;
                OnPropertyChanged("SupportedSizes");
            }
        }
    }
}
