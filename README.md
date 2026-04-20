# GNSSR Front-End Host

基于 `gnssr_windows_host_agent_spec.yaml` 初始化的 GNSS-R 上位机仓库。

当前仓库已经落下第一版可运行的 `.NET 8 + WPF` 项目骨架，并使用 [WPF UI](https://github.com/lepoco/wpfui) 作为主题与主要视觉风格。整体设计遵循规格文件中给出的方向：`Windows + WPF + MVVM + 分层架构 + 面向设备会话`，同时把真实硬件依赖先隔离在基础设施层，方便先稳定 UI、状态机与采集工作流。

## 当前实现状态

目前仓库包含的是“可启动的上位机骨架”，不是最终硬件联调版本。

已经完成：

- `.NET 8` 多项目解决方案骨架
- `WPF UI` 主题接入
- 主窗口 MVP 布局
- FX3 连接区、前端状态区、采集控制区、采集信息区、日志区
- Core / Infrastructure / UI 的职责拆分
- mock FX3、mock 串口、mock 采集会话服务
- 模拟 metadata 输出逻辑

当前仍是 mock 的部分：

- FX3 USB 枚举与 EP0 控制
- 前端串口 HELLO / GET_STATUS 二进制协议
- Bulk IN 异步接收
- ring buffer 与真实写盘线程
- Serilog 与正式日志落盘

换句话说，现在这套代码的价值是：

- 可以直接作为真实项目的启动骨架继续开发
- 可以先演练 UI 流程、状态流和目录结构
- 可以明确后续真实驱动应该替换哪些类

## 技术栈

- `.NET 8`
- `WPF`
- `WPF UI 4.2.0`
- `C#`
- `MVVM`

WPF UI 官方资源：

- GitHub: https://github.com/lepoco/wpfui
- Documentation: https://wpfui.lepo.co/documentation/getting-started.html

## 解决方案结构

```text
GNSSR.Host.sln
├─ src/
│  ├─ GNSSR.Host.Core/
│  │  ├─ Abstractions/
│  │  ├─ Enums/
│  │  ├─ Models/
│  │  └─ Services/
│  ├─ GNSSR.Host.Infrastructure.Logging/
│  ├─ GNSSR.Host.Infrastructure.Serial/
│  ├─ GNSSR.Host.Infrastructure.Storage/
│  ├─ GNSSR.Host.Infrastructure.USB/
│  └─ GNSSR.Host.UI/
└─ gnssr_windows_host_agent_spec.yaml
```

各项目职责：

- `GNSSR.Host.Core`
  放领域模型、接口定义、状态枚举、文件命名策略等与具体技术无关的核心代码。

- `GNSSR.Host.Infrastructure.USB`
  后续承接 FX3 枚举、EP0 命令、Bulk IN 接收与状态解析。当前是 `MockFx3UsbService`。

- `GNSSR.Host.Infrastructure.Serial`
  后续承接前端串口、CRC16、HELLO / GET_STATUS 协议编解码。当前是 `MockFrontendSerialService`。

- `GNSSR.Host.Infrastructure.Storage`
  后续承接 ring buffer、写盘线程、metadata 输出。当前是 `MockCaptureSessionService`。

- `GNSSR.Host.Infrastructure.Logging`
  当前提供内存日志流。后续可替换为 Serilog 初始化与统一日志落盘。

- `GNSSR.Host.UI`
  WPF 界面、ViewModel、命令和主窗口布局。

## 与 YAML 规格的对应关系

这版骨架直接映射了规格文件里的关键要求：

- 推荐栈：`.NET 8 + WPF + MVVM`
- 状态机：`Idle -> DeviceReady -> FrontendReady -> ReadyToCapture -> Capturing`
- 主窗口区块：
  - 设备连接区
  - 前端状态区
  - FX3 状态区
  - 采集控制区
  - 采集信息区
  - 日志区
- 采集文件策略：
  - `operatorName_yyyyMMdd_HHmmss.bin`
  - 同名 `.json` metadata

当前 UI 中展示的吞吐、buffer 占用、采集计数，都是 mock 服务根据规格建议值模拟出来的，用来验证展示逻辑和交互流程。

## 快速开始

### 1. 安装 .NET 8 SDK

请先确认本机已安装 `.NET 8 SDK`，而不是只有 runtime。

```powershell
dotnet --info
```

如果输出中没有 `.NET SDKs installed: 8.x`，请先安装 SDK。

### 2. 还原依赖

```powershell
dotnet restore .\GNSSR.Host.sln
```

### 3. 构建

```powershell
dotnet build .\GNSSR.Host.sln
```

### 4. 运行

```powershell
dotnet run --project .\src\GNSSR.Host.UI\GNSSR.Host.UI.csproj
```

## 运行后的预期

应用启动后会看到：

- 顶部状态横幅
- FX3 设备枚举与连接卡片
- 串口前端连接与状态卡片
- 采集控制与输出目录设置
- 模拟的采集速率、写入量和 ring buffer 信息
- 日志流

即使没有真实硬件，也能把界面跑起来，因为当前使用的是 mock 服务。

## 真实硬件接入时应替换的入口

如果要进入下一阶段联调，优先替换这些类：

- [MockFx3UsbService](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Infrastructure.USB/Services/MockFx3UsbService.cs:1>)
- [MockFrontendSerialService](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Infrastructure.Serial/Services/MockFrontendSerialService.cs:1>)
- [MockCaptureSessionService](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Infrastructure.Storage/Services/MockCaptureSessionService.cs:1>)

推荐替换顺序：

1. 先接入真实 FX3 枚举与 `GET_STATUS`
2. 再接入串口 `HELLO` 与 `GET_STATUS`
3. 然后接入 `START_STREAM / STOP_STREAM`
4. 最后补 ring buffer、独立写盘线程和 metadata 完整字段

## 已创建的关键文件

- [GNSSR.Host.sln](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/GNSSR.Host.sln:1>)
- [App.xaml](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.UI/App.xaml:1>)
- [MainWindow.xaml](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.UI/MainWindow.xaml:1>)
- [MainViewModel.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.UI/ViewModels/MainViewModel.cs:1>)
- [FileNamingPolicy.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Core/Services/FileNamingPolicy.cs:1>)

## 下一步建议

- 接入真实 FX3 主机库或 WinUSB 封装
- 按规格补齐串口二进制协议与 CRC16
- 引入 `System.Threading.Channels` 实现有界 ring buffer
- 引入 Serilog 滚动日志
- 增加 `GNSSR.Host.Tests` 测试项目
- 增加设置持久化与最近采集记录

## 备注

- 当前重点是“先把上位机骨架搭稳”，不是一次性做完所有硬件逻辑。
- mock 服务是刻意保留的过渡层，不是临时代码堆积。
- 后续真实实现应尽量保持 Core 接口不变，只替换 Infrastructure 层。
