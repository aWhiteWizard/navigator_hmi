using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ProtoBuf;

namespace NavigatorHMI.Common
{
    [ProtoContract]
    public class HMIProject : INotifyPropertyChanged
    {
        // 隐式实现事件
        public event PropertyChangedEventHandler? PropertyChanged;

        // 辅助方法，用于触发属性变更通知
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _name;
        [ProtoMember(1)]
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        private DateTime _createTime;
        [ProtoMember(2)]
        public DateTime CreateTime
        {
            get => _createTime;
            set
            {
                if (_createTime != value)
                {
                    _createTime = value;
                    OnPropertyChanged(nameof(CreateTime));
                }
            }
        }

        private DateTime _lastModifiedTime;
        [ProtoMember(3)]
        public DateTime LastModifiedTime
        {
            get => _lastModifiedTime;
            set
            {
                if (_lastModifiedTime != value)
                {
                    _lastModifiedTime = value;
                    OnPropertyChanged(nameof(LastModifiedTime));
                }
            }
        }

        private string _version;
        [ProtoMember(4)]
        public string Version
        {
            get => _version;
            set
            {
                if (_version != value)
                {
                    _version = value;
                    OnPropertyChanged(nameof(Version));
                }
            }
        }

        [ProtoMember(5)]
        public List<Screen> Screens { get; set; } = new List<Screen>();

        private string _projectFilePath;
        [ProtoMember(6)]
        public string ProjectFilePath
        {
            get => _projectFilePath;
            set
            {
                if (_projectFilePath != value)
                {
                    _projectFilePath = value;
                    OnPropertyChanged(nameof(ProjectFilePath));
                }
            }
        }
    }
}
