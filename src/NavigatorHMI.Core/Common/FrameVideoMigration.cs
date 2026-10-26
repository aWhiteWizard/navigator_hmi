using System.Linq;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// AD（2026-10-24 用户裁决）：Frame 控件恒为视频控件 —— 工程载入期归一化。
    ///
    /// 背景：P-6 曾用 <see cref="FrameWidget.ShowVideo"/> 区分"视频 Frame / 普通 Frame（图片模式）"，
    /// 用户裁决 Frame 就是视频控件，该 checkbox 语义作废。模型字段保留（Protobuf ProtoMember 11 兼容），
    /// 但载入后一律置 true。
    ///
    /// **单点约束**：所有把 .hmiproj 反序列化成 <see cref="HMIProject"/> 的入口都必须调用本方法
    /// （当前两处：<c>ProjectFileService.LoadCore</c>、<c>App.HandleProjectFileOpen → LoadProjectFromFile</c>），
    /// 否则会出现"生成器恒下发 true、但模型/属性面板仍读到 false"的隐式不一致。
    ///
    /// 注意：图片模式**残留字段**（ImagePath/ListRef）**不在此清理**——按用户裁决由
    /// <c>ProjectGenerator</c> 的编译期校验拦截并逐控件列名（要求用户删除控件）。
    /// </summary>
    public static class FrameVideoMigration
    {
        /// <summary>把所有 FrameWidget 的 ShowVideo 归一化为 true（就地修改，调用方负责随后 ClearDirty）。</summary>
        public static void NormalizeShowVideo(HMIProject? project)
        {
            if (project?.Screens == null) return;
            foreach (var screen in project.Screens)
            {
                if (screen?.Widgets == null) continue;
                foreach (var w in screen.Widgets.OfType<FrameWidget>())
                    if (!w.ShowVideo) w.ShowVideo = true;
            }
        }
    }
}
