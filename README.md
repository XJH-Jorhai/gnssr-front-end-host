# GNSSR Front-End Host

基于 `gnssr_windows_host_agent_spec.yaml` 初始化的 GNSS-R 上位机项目，当前采用 `.NET 8 + WPF + WPF UI`。

这套代码现在已经不是最初的纯骨架，而是一版可运行、可截图、可继续接入真实硬件的桌面控制台。界面采用 `总览 / 采集 / 日志` 三分页，适合设备联调、流程演示和论文截图。

## 当前状态

已经完成：

- `.NET 8` 多项目解决方案与分层结构
- `WPF UI` 主题接入
- 自定义分页导航与统一卡片化主界面
- FX3 连接区、前端连接区、FX3 状态区、前端状态区、采集控制区、采集信息区、日志区
- 文件名前缀 + 自动时间戳命名
- Windows 串口实际枚举
- Windows FX3 设备枚举占位接入
- 启动脚本与本机 SDK workaround

当前仍未打通的部分：

- FX3 控制传输与 Bulk IN 数据链
- 前端串口协议 `HELLO / GET_STATUS / START_STREAM / STOP_STREAM`
- 真实 ring buffer 与正式写盘线程
- 真实 metadata 完整字段采集

换句话说，当前版本已经能把“界面、状态流、设备发现、采集命名规则”稳定展示出来，但还没有进入最终硬件链路联调阶段。

## 这版最重要的变化

- 界面从单页骨架整理成了更接近成品的桌面控制台
- 页签头改为自定义 segmented 导航，解决了原生 `TabControl` 头部裁切问题
- 采集输入从“操作员姓名”改成“文件名前缀”
- 输出文件名改为：

```text
fly[2025.07.25 18.00.01].bin
```

- 串口列表不再显示 `Mock Frontend`，而是读取本机真实串口
- FX3 区域不再向前端界面暴露 `Mock Device` 文案

## 运行方式

### 推荐方式 1：双击 `launch-ui.cmd`

根目录的 [launch-ui.cmd](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/launch-ui.cmd:1>) 会：

1. 检查 `.NET 8 SDK`
2. 应用本机 `MSBuildEnableWorkloadResolver=false` workaround
3. 构建解决方案
4. 启动界面

这是当前最稳定的运行方式。

### 推荐方式 2：Visual Studio

1. 打开 [GNSSR.Host.sln](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/GNSSR.Host.sln:1>)
2. 将 `GNSSR.Host.UI` 设为启动项目
3. 按 `F5` 或 `Ctrl + F5`

### 命令行

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet build .\GNSSR.Host.sln -c Debug -m:1 -p:BuildInParallel=false
dotnet run --project .\src\GNSSR.Host.UI\GNSSR.Host.UI.csproj --no-build
```

## 当前界面说明

### 总览

- FX3 设备选择、刷新、连接、断开
- 串口选择、刷新、连接、断开
- 连接状态与关键状态灯
- FX3 与前端关键状态摘要

### 采集

- 文件名前缀输入
- 预计文件名预览
- 输出目录设置
- 开始采集、停止采集、重置流
- 采集统计与 Ring Buffer 占用

### 日志

- 固定行高日志表格
- 时间 / 级别 / 消息三列
- 适合联调时快速扫读

## 文件命名规则

文件命名已经统一改为“前缀 + 时间戳”：

```text
prefix[yyyy.MM.dd HH.mm.ss].bin
prefix[yyyy.MM.dd HH.mm.ss].json
```

示例：

```text
fly[2025.07.25 18.00.01].bin
fly[2025.07.25 18.00.01].json
```

相关实现：

- [FileNamingPolicy.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Core/Services/FileNamingPolicy.cs:1>)
- [ICaptureSessionService.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Core/Abstractions/ICaptureSessionService.cs:1>)
- [CaptureSessionInfo.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Core/Models/CaptureSessionInfo.cs:1>)

## 当前设备发现策略

### 串口

当前使用 [WindowsFrontendSerialService.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Infrastructure.Serial/Services/WindowsFrontendSerialService.cs:1>) 从 Windows 注册表枚举本机串口，因此界面会显示实际 `COM` 口。

### FX3

当前使用 [WindowsFx3UsbService.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Infrastructure.USB/Services/WindowsFx3UsbService.cs:1>) 从 Windows USB 设备注册表中查找 Cypress FX3 设备。

这里的目标是：

- 先让前端展示真实设备发现结果
- 不在 UI 上出现 `Mock` 文案
- 但暂时不强行进入真实数据链路联调

## 仍然保留的模拟部分

为了不阻塞界面与流程展示，当前仍保留：

- [MockCaptureSessionService.cs](</c:/Users/20101/Desktop/bysj/gnssr-front-end-host/src/GNSSR.Host.Infrastructure.Storage/Services/MockCaptureSessionService.cs:1>)

这意味着：

- 采集页的流程可以继续演示
- 文件命名、metadata 写出、统计展示仍然可用
- 但这还不是最终真实采集链路

## 解决方案结构

```text
GNSSR.Host.sln
├─ src/
│  ├─ GNSSR.Host.Core/
│  ├─ GNSSR.Host.Infrastructure.Logging/
│  ├─ GNSSR.Host.Infrastructure.Serial/
│  ├─ GNSSR.Host.Infrastructure.Storage/
│  ├─ GNSSR.Host.Infrastructure.USB/
│  └─ GNSSR.Host.UI/
└─ gnssr_windows_host_agent_spec.yaml
```

职责划分：

- `GNSSR.Host.Core`
  领域模型、接口、命名策略、状态定义

- `GNSSR.Host.Infrastructure.Serial`
  串口枚举与后续前端协议接入

- `GNSSR.Host.Infrastructure.USB`
  FX3 设备发现与后续 USB 控制/数据链接入

- `GNSSR.Host.Infrastructure.Storage`
  采集会话、文件输出、metadata

- `GNSSR.Host.Infrastructure.Logging`
  当前为内存日志服务

- `GNSSR.Host.UI`
  WPF 窗口、ViewModel、命令与页面布局

## 下一步建议

- 接入真实 FX3 控制命令与状态读取
- 接入前端串口真实协议
- 将采集服务从 mock 写盘替换为真实数据写盘
- 增加设备配置持久化
- 增加错误提示与设备详情弹窗

## 参考

- WPF UI GitHub: https://github.com/lepoco/wpfui
- WPF UI Docs: https://wpfui.lepo.co/documentation/getting-started.html
