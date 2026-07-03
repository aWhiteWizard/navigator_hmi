# 架构约定

## 设计模式
- **MVVM**: 使用 CommunityToolkit.Mvvm v8.4.2 实现
- **依赖注入**: 使用 Microsoft.Extensions.DependencyInjection v10.0.5
- 视图与 ViewModel 一一对应，通过 DI 容器管理生命周期

## UI 框架
- **SunnyUI** v3.9.7 系列作为主要 UI 组件库（含 Common、COM、FrameDecoder、Serialization 子包）
- 自定义 Behaviors 和 Helpers 放在 Views 子目录下

## 序列化
- **protobuf-net** v3.2.56 用于 Protocol Buffers 序列化

## COM 互操作
- 项目引用了 COM 组件（GUID: 215d64d2-031c-33c7-96e3-61794cd1ee61），用于底层硬件/串口通信

## 命名约定
- Views: `XxxWindow.xaml` / `XxxDialog.xaml`
- ViewModels: `XxxViewModel.cs`
- Models: `XxxModel.cs`
