namespace NavigatorHMI.Common
{
    /// <summary>
    /// 绑定变量后控件设计态旧值清理：绑定后控件显示由变量基准值/实时值控制，
    /// 自身的 ImagePath/FrameImagePath/NumericValue/IOFieldContent/ProgressValue 旧值应清除（避免残留）。
    /// </summary>
    public static class WidgetDesignValue
    {
        /// <summary>清除控件的设计态显示旧值。Image/Frame 仅绑列表（ListRef 非空）时清路径——
        /// 仅绑 BoundTag 无 ListRef 时显示仍读 ImagePath（与 DisplayPath 驱动源一致，不可清）。</summary>
        public static void Clear(Widget w)
        {
            switch (w)
            {
                case ImageWidget img when !string.IsNullOrEmpty(img.ListRef): img.ImagePath = ""; break;
                case FrameWidget f when !string.IsNullOrEmpty(f.ListRef): f.ImagePath = ""; break;
                case NumericDisplayWidget nd: nd.Value = 0; break;
                case IOFieldWidget io: io.Content = ""; break;
                case ProgressBarWidget pb: pb.Value = 0; break;
            }
        }

        /// <summary>该控件是否有活跃绑定（BoundTag 或图片/文本列表 ListRef），绑定后旧值应清除。</summary>
        public static bool HasBinding(Widget w)
        {
            if (!string.IsNullOrEmpty(w.BoundTag)) return true;
            return w switch
            {
                ImageWidget img => !string.IsNullOrEmpty(img.ListRef),
                FrameWidget f => !string.IsNullOrEmpty(f.ListRef),
                TextListWidget tl => !string.IsNullOrEmpty(tl.ListRef),
                _ => false,
            };
        }
    }
}
