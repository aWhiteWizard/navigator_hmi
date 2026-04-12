using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace NavigatorHMI.ViewModels
{
    public class WelComeViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RecentOpenedFileItems
    {
        public String Name {  get; set; }
        public String Path { get; set; }
        public DateTime LastOpenedTime { get; set; }

        [JsonIgnore]
        public ICommand OpenCommand { get; set; }

        [JsonIgnore]
        public bool IsFileExists => System.IO.File.Exists(Path);

    }

    public class RecentProjectManager
    {
        private static RecentProjectManager _instance;
        public static RecentProjectManager Instance => _instance ??= new RecentProjectManager();

        private RecentProjectManager()
        {
            _recent_opened_files = new List<RecentOpenedFileItems>();
            LoadFromFile();
        }

        private const string recent_opened_file = @"D:\workspace\test_project\RecentOpenedFile.json";
        private List<RecentOpenedFileItems> _recent_opened_files = new List<RecentOpenedFileItems>();

        public List<RecentOpenedFileItems> RecentOpenedProject => _recent_opened_files;
        public event Action<string> OnProjectSelected;

        // 添加一个工程到最近打开列表
        public void AddRecentProject(string filePath)
        {
            // 检查是否已存在
            var existing = _recent_opened_files.FirstOrDefault(p => p.Path == filePath);
            if (existing != null)
            {
                // 已存在，移到最前面
                _recent_opened_files.Remove(existing);
                existing.LastOpenedTime = DateTime.Now;
                _recent_opened_files.Insert(0, existing);
            }
            else
            {
                // 新增
                var newItem = new RecentOpenedFileItems
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Path = filePath,
                    LastOpenedTime = DateTime.Now,
                    OpenCommand = new RelayCommand(() => OnProjectSelected?.Invoke(filePath))
                };
                _recent_opened_files.Insert(0, newItem);
            }
            // 保存到文件
            SaveToFile();
        }

        public void LoadFromFile()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, recent_opened_file);

            // 确保目录存在
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            
            if (File.Exists(filePath))
            {
                try
                {
                    // 文件存在：读取并反序列化
                    string json = File.ReadAllText(filePath);
                    _recent_opened_files = JsonSerializer.Deserialize<List<RecentOpenedFileItems>>(json)
                                      ?? new List<RecentOpenedFileItems>();
                }
                catch (Exception ex)
                {
                    // 打印完整错误信息
                    MessageBox.Show($"反序列化失败：{ex.Message}\n\n堆栈：{ex.StackTrace}");
                    // 如果 ex.InnerException 不为空，也要显示
                    if (ex.InnerException != null)
                        MessageBox.Show($"内部异常：{ex.InnerException.Message}");
                }

            }
            else
            {
                // 文件不存在：创建空文件并初始化空列表
                _recent_opened_files = new List<RecentOpenedFileItems>();
                SaveToFile(); // 调用保存方法，写入空列表
            }
        }

        public void SaveToFile()
        {
            string file_path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, recent_opened_file);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.CjkUnifiedIdeographs)
            };
            string json = JsonSerializer.Serialize(_recent_opened_files, options);
            File.WriteAllText(file_path, json);
        }

        public void RemoveRecentProject(string filePath)
        {
            var item = _recent_opened_files.FirstOrDefault(p => p.Path == filePath);
            if (item != null)
            {
                _recent_opened_files.Remove(item);
                SaveToFile();
            }
        }

        public void RemoveAllInvalidProjects()
        {
            // 移除所有文件不存在的项
            int removedCount = _recent_opened_files.RemoveAll(p => !File.Exists(p.Path));
            if (removedCount > 0)
                SaveToFile();
        }
    }
}

