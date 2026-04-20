# GNSSR Front-End Host

基于 `gnssr_windows_host_agent_spec.yaml` 初始化的 GNSS-R 上位机仓库。

当前仓库已经落下第一版可运行的 `.NET 8 + WPF` 项目骨架，并使用 [WPF UI](https://github.com/lepoco/wpfui) 作为主题与主要视觉风格。整体设计遵循规格文件中的方向：`Windows + WPF + MVVM + 分层架构 + 面向设备会话`，同时把真实硬件依赖先隔离在基础设施层，便于先稳定 UI、状态机与采集工作流。

## 当前实现状态

目前仓库包含的是“可启动的上位机骨架”，不是最终硬件联调版本。

已经完成：

- `.NET 8` 多项目解决方案骨架
- `WPF UI` 主题接入
- 主窗口 MVP 布局
- FX3 连接区、前端状态区、采集控制区、采集信息区、日志区
- `Core / Infrastructure / UI` 职责拆分
- mock FX3、mock 串口、mock 采集会话服务
- 模拟 metadata 输出逻辑

当前仍是 mock 的部分：

- FX3 USB 枚举与 EP0 控制
- 前端串口 `HELLO / GET_STATUS` 二进制协议
- Bulk IN 异步接收
- ring buffer 与真实写盘线程
- Serilog 与正式日志落盘

这套代码当前的价值是：

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
  后续承接前端串口、CRC16、`HELLO / GET_STATUS` 协议编解码。当前是 `MockFrontendSerialService`。

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

## 第一次运行

如果你是第一次接触 C# / .NET，我最推荐下面这三种启动方式。

### 方式 1：双击 `launch-ui.cmd`

这是最省心的方式。

根目录已经新增了：

- [launch-ui.cmd](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/launch-ui.cmd:1>)

它会自动做两件事：

1. 检查你机器上有没有 `.NET 8 SDK`
2. 自动编译并启动界面

使用方法：

1. 打开仓库根目录
2. 直接双击 `launch-ui.cmd`
3. 等它编译完成后，界面会自动弹出

如果有错误，窗口不会立刻关闭，而是会停在错误信息页面，方便你看问题。

### 方式 2：Visual Studio 里点绿色运行按钮

如果你装了 `Visual Studio 2022`，这是最适合新手的方式。

操作步骤：

1. 双击打开 [GNSSR.Host.sln](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/GNSSR.Host.sln:1>)
2. 在右侧“解决方案资源管理器”里找到 `GNSSR.Host.UI`
3. 右键它，选择“设为启动项目”
4. 点击顶部绿色三角按钮，或者按 `F5`

如果你只想运行，不想调试，也可以按 `Ctrl + F5`。

### 方式 3：直接运行生成好的 EXE

先编译一次：

```powershell
dotnet build .\GNSSR.Host.sln
```

然后直接双击这个文件：

- [GNSSR.Host.UI.exe](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.UI/bin/Debug/net8.0-windows/GNSSR.Host.UI.exe:1>)

这是最接近“普通桌面软件”的启动方式。

## 命令行运行方式

如果你仍然想用命令行，可以使用：

```powershell
dotnet run --project .\src\GNSSR.Host.UI\GNSSR.Host.UI.csproj
```

补充说明：

- 对于 WPF 这种桌面程序，终端窗口本身不会显示太多输出
- 终端看起来像“没反应”，很多时候其实是应用正在运行
- 正常情况下，你应该会看到桌面窗口弹出
- 如果终端一直占着不返回，那通常意味着程序还在运行，关闭界面后终端才会结束

如果你发现“命令执行了但没看到窗口”，优先改用上面的 `launch-ui.cmd` 或直接双击 EXE。

## 我这次确认过的事情

我已经在当前机器上验证过：

- 系统 `dotnet` 已经是 `.NET 8 SDK 8.0.420`
- 不再需要仓库里的临时本地 `.NET` SDK
- 解决方案可以正常构建通过
- `GNSSR.Host.UI.exe` 进程可以正常启动

也就是说，当前问题更像是“启动方式不直观”，不是项目本身不能运行。

## 快速开始

### 1. 检查 .NET 8 SDK

```powershell
dotnet --info
```

如果输出里能看到：

- `.NET SDKs installed: 8.x`

那就说明环境没问题。

### 2. 还原依赖

```powershell
dotnet restore .\GNSSR.Host.sln
```

### 3. 构建

```powershell
dotnet build .\GNSSR.Host.sln
```

### 4. 启动

推荐：

- 双击 `launch-ui.cmd`

或者：

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
- [launch-ui.cmd](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/launch-ui.cmd:1>)
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

- 当前重点是“先把上位机骨架搭稳”，不是一次性做完所有硬件逻辑
- mock 服务是刻意保留的过渡层，不是临时代码堆积
- 后续真实实现应尽量保持 `Core` 接口不变，只替换 `Infrastructure` 层
