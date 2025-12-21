using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Linq;

namespace NavigatorHMI.Common
{
    public class HMIProject : INotifyPropertyChanged
    {
        private string _name;
        private string _filePath;
        private DateTime _lastModified;
        private bool _isDirty;

        #region 基本属性
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { 
            get => _name;
            set 
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                    IsDirty = true;
                }
            }
        }
        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(IsTemporary));
                OnPropertyChanged(nameof(FileName));
            }
        }

        #endregion
        /// <summary>
        /// 是否为临时工程
        /// </summary>
        public bool IsTemporary => string.IsNullOrEmpty(FilePath);

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 文件名称（不包含路径）
        /// </summary>
        public string FileName => !string.IsNullOrEmpty(FilePath)
            ? System.IO.Path.GetFileName(FilePath)
            : $"{Name}.hmiproj";
        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModified
        {
            get => _lastModified;
            set
            {
                _lastModified = value;
                OnPropertyChanged(nameof(LastModified));
            }
        }
            /// <summary>
            /// 是否已修改（需要保存）
            /// </summary>
            public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged(nameof(IsDirty));
                    if (value) LastModified = DateTime.Now;
                }
            }
        }

            public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        
    }
}
