using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NavigatorHMI.Common
{
    [Serializable]
    public class ProjectData
    {
        // 基本属性
        public string ProjectName { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public string ModelType { get; set; } = "";
        public string Size { get; set; } = "";

        // 文件相关信息
        [NonSerialized]
        public string FilePath = "";  // 工程文件的完整路径

        public DateTime CreateTime { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;

        // 其他配置数据...
        // public List<DeviceConfig> Devices { get; set; } = new();
        // public ProjectSettings Settings { get; set; } = new();

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(ProjectName) &&
                   !string.IsNullOrWhiteSpace(ProjectPath);
        }
    }
}
