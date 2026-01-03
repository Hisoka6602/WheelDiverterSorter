# WheelDiverterSorter (摆轮分拣系统)

## 项目简介

WheelDiverterSorter 是一套基于 .NET 的高性能摆轮分拣控制系统，用于物流分拣场景中的包裹自动化分拣作业。系统通过控制多个摆轮设备，将传送带上的包裹精准地分拣到指定的格口中，实现高效的自动化物流分拣。

## 系统架构

### 架构分层

本项目采用清晰的分层架构设计，各层职责明确：

```
┌─────────────────────────────────────────┐
│   WheelDiverterSorter.Host (宿主层)     │  ← Windows Service / 控制台应用
├─────────────────────────────────────────┤
│ WheelDiverterSorter.Execution (执行层)  │  ← 业务逻辑编排
├─────────────────────────────────────────┤
│  WheelDiverterSorter.Ingress (入口层)   │  ← 数据接入、传感器监控
├─────────────────────────────────────────┤
│  WheelDiverterSorter.Drivers (驱动层)   │  ← 硬件设备通信驱动
├─────────────────────────────────────────┤
│    WheelDiverterSorter.Core (核心层)    │  ← 接口定义、数据模型
├─────────────────────────────────────────┤
│ WheelDiverterSorter.Infrastructure (基础设施层) │
└─────────────────────────────────────────┘
```

#### 1. **Core (核心层)**
- 定义系统核心接口契约
- 包含数据模型、事件定义、枚举类型
- 主要接口：
  - `IWheelDiverter`: 摆轮设备控制接口（左转/右转/直通）
  - `IUpstreamRouting`: 上游路由通信接口（与 WMS/WCS 对接）
  - `IEmcController`: EMC 运动控制器接口
  - `IIoPanel`: IO 面板控制接口
  - `ISortingOrchestrator`: 分拣编排接口
  - `IParcelManager`: 包裹管理接口
  - `IPositionQueueManager`: 位置队列管理接口

#### 2. **Drivers (驱动层)**
- 实现与硬件设备的通信协议
- 支持厂商：
  - **雷赛 (Leadshine)**: EMC 运动控制器、IO 面板
  - **数点鸟 (ShuDiNiao)**: 摆轮分拣设备
- 基于 TCP/IP 通信，支持客户端/服务端模式

#### 3. **Ingress (入口层)**
- 传感器数据采集与监控
- 上游系统路由通信实现
- 包裹检测事件触发

#### 4. **Execution (执行层)**
- 包裹生命周期管理 (`ParcelManager`)
- 位置队列调度 (`PositionQueueManager`)
- 摆轮设备管理 (`WheelDiverterManager`)
- 系统状态管理 (`SystemStateManager`)

#### 5. **Host (宿主层)**
- 应用程序入口
- 依赖注入配置
- 可运行为 Windows 服务或控制台应用
- 配置文件管理（appsettings.json）

#### 6. **Infrastructure (基础设施层)**
- 通用基础设施组件

## 核心功能

### 1. 包裹追踪与管理
- **包裹生命周期管理**: 从创建、传输、分拣到落格的全流程追踪
- **高并发处理**: 基于 `ConcurrentDictionary` 的高性能包裹信息存储
- **实时状态更新**: 包裹位置、目标格口、实际落格格口等信息实时维护

### 2. 摆轮分拣控制
- **多摆轮协同**: 支持多台摆轮设备同时工作
- **三方向分拣**: 支持左转、右转、直通三种分拣方向
- **速度控制**: 可动态调整摆轮速度（毫米/秒）
- **自动重连**: 设备断线后自动重连机制

### 3. 位置队列调度
- **无锁队列管理**: 每个位置点独立 Actor 模型，基于 Channel 实现高并发无竞态
- **时序控制**: 基于预估时间、最早时间、最晚时间的精确时序控制
- **队列任务管理**: 支持入队、出队、查看、更新等操作

### 4. 传感器监控
- **实时 IO 监控**: 高频轮询（5-10ms）传感器状态
- **防抖处理**: 配置化防抖窗口，过滤干扰信号
- **多类型传感器**:
  - 包裹创建传感器
  - 摆轮位置传感器
  - 面板按钮（启动/停止/急停）

### 5. 上游系统对接
- **双向通信**: 
  - 上行：发送包裹创建、落格完成、异常消息
  - 下行：接收格口分配（路由）结果
- **自动重连**: 支持指数退避重连策略
- **超时控制**: 连接、接收、发送超时独立配置

### 6. IO 联动控制
- **状态联动**: 根据系统状态（运行/暂停/急停）自动控制 IO 输出
- **延迟与持续时间**: 支持 IO 延迟触发和持续时间配置
- **信号塔控制**: 红绿黄灯、蜂鸣器等指示设备控制

## 系统逻辑流程

### 包裹分拣完整流程

```
1. 【包裹检测】
   传感器触发 → ParcelCreateSensor 检测到包裹
   ↓
2. 【包裹创建】
   创建 ParcelInfo → 发送创建消息到上游系统
   ↓
3. 【路由分配】
   上游系统 → 下发格口分配（ChuteAssignmentInfo）
   ↓
4. 【包裹入队】
   ParcelInfo 更新目标格口 → 加入位置队列（PositionQueueManager）
   ↓
5. 【位置追踪】
   包裹到达各位置点 → 传感器触发 → 队列出队 → 包裹前移到下一位置
   ↓
6. 【摆轮动作】
   包裹到达摆轮位置 → 判断目标格口 → 执行左转/右转/直通
   ↓
7. 【落格完成】
   包裹落入格口 → 标记落格时间和实际格口 → 发送完成消息到上游
   ↓
8. 【包裹移除】
   包裹完成分拣 → 从 ParcelManager 中移除
```

### 时序控制逻辑

系统采用基于时间窗口的精确时序控制：

- **预估出站时间 (EstimatedExitAtMs)**: 根据线段长度和速度计算的理论出站时间
- **最早出站时间 (EarliestExitAtMs)**: 预估时间 - 时间容差
- **最晚出站时间 (LatestExitAtMs)**: 预估时间 + 时间容差

如果包裹在时间窗口外触发传感器：
- **提前触发**: 触发 `ParcelEarlyTriggered` 事件
- **超时触发**: 触发 `ParcelPositionTimedOut` 事件

### 状态机管理

系统状态转换：

```
Initialized → Running → Paused → Running → Stopped
                ↓
           EmergencyStop → Stopped
```

- **Initialized**: 初始化完成，等待启动
- **Running**: 正常运行，执行分拣作业
- **Paused**: 暂停，可恢复
- **EmergencyStop**: 急停，需要人工介入
- **Stopped**: 停止，系统退出

## 对接说明

### 上游系统对接 (WMS/WCS)

#### 1. 通信协议
- **协议**: TCP/IP
- **模式**: Server 或 Client（可配置）
- **编码**: 文本协议（具体格式由实现类定义）

#### 2. 接口契约

**上行消息（本系统 → 上游）**:

```csharp
// 1. 包裹创建通知
public class UpstreamCreateParcelRequest {
    public long ParcelId { get; set; }
    public string BarCode { get; set; }
    public DateTime DetectedTime { get; set; }
    // 其他字段...
}

// 2. 分拣完成通知
public class SortingCompletedMessage {
    public long ParcelId { get; set; }
    public long ActualChuteId { get; set; }
    public DateTime DroppedTime { get; set; }
    // 其他字段...
}

// 3. 包裹异常通知
public class ParcelExceptionMessage {
    public long ParcelId { get; set; }
    public string ExceptionType { get; set; }  // 超时/丢失/其他
    public string Reason { get; set; }
    // 其他字段...
}
```

**下行消息（上游 → 本系统）**:

```csharp
// 格口分配（路由）结果
public class ChuteAssignmentInfo {
    public long ParcelId { get; set; }
    public long TargetChuteId { get; set; }
    public DateTime AssignedTime { get; set; }
    // 其他字段...
}
```

#### 3. 配置示例

```json
{
  "UpstreamRoutingConnectionOptions": {
    "Endpoint": "192.168.5.200",
    "Port": 2003,
    "Mode": "Server",
    "ConnectTimeoutMs": 3000,
    "ReceiveTimeoutMs": 5000,
    "SendTimeoutMs": 5000,
    "IsAutoReconnectEnabled": true,
    "ReconnectMinDelayMs": 100,
    "ReconnectMaxDelayMs": 2000,
    "ReconnectBackoffFactor": 1.2
  }
}
```

#### 4. 事件订阅

```csharp
// 订阅格口分配事件
upstreamRouting.ChuteAssignedReceived += (sender, chuteInfo) => {
    // 处理路由分配结果
    var parcelId = chuteInfo.ParcelId;
    var targetChute = chuteInfo.TargetChuteId;
    // 更新包裹目标格口...
};

// 订阅连接状态事件
upstreamRouting.Connected += (sender, e) => { /* 连接成功 */ };
upstreamRouting.Disconnected += (sender, e) => { /* 连接断开 */ };
upstreamRouting.Faulted += (sender, e) => { /* 连接异常 */ };
```

### 摆轮设备对接

#### 1. 设备通信

支持数点鸟摆轮设备，通过 TCP 通信：

```json
{
  "WheelDiverterConnectionOptions": [
    {
      "DiverterId": 1,
      "Endpoint": "192.168.5.101",
      "Port": 2000,
      "Mode": "Client",
      "ConnectTimeoutMs": 3000,
      "ReceiveTimeoutMs": 500,
      "SendTimeoutMs": 500,
      "IsAutoReconnectEnabled": true
    }
  ]
}
```

#### 2. 控制接口

```csharp
// 左转
await wheelDiverter.TurnLeftAsync();

// 右转
await wheelDiverter.TurnRightAsync();

// 直通
await wheelDiverter.StraightThroughAsync();

// 设置速度（毫米/秒）
await wheelDiverter.SetSpeedMmpsAsync(1500);
```

### EMC 控制器对接

支持雷赛 EMC 运动控制器：

- **IO 点读写**: 支持读取和写入 IO 电平状态
- **IO 监控**: 批量监控 IO 点状态变化
- **初始化**: 系统启动时自动初始化连接

## 应用场景

### 1. 电商物流分拣中心
- **场景**: 大型电商仓库包裹出库分拣
- **规模**: 支持 5-20 个格口，处理能力 2000-5000 件/小时
- **优势**: 高速、准确、自动化程度高

### 2. 快递转运中心
- **场景**: 快递公司省级或市级转运中心
- **规模**: 多线并行，每线 10-30 个格口
- **优势**: 降低人工成本，提高分拣效率

### 3. 邮政分拣系统
- **场景**: 邮政包裹、信件自动化分拣
- **规模**: 中小型分拣线，灵活配置
- **优势**: 适应多种包裹尺寸，分拣准确率高

### 4. 生产制造物流
- **场景**: 工厂内部物料、成品分拣配送
- **规模**: 定制化线路设计
- **优势**: 与生产系统集成，实现智能物流

## 配置说明

### 线段配置 (ConveyorSegmentOptions)

定义传送带线段的物理参数：

```json
{
  "ConveyorSegmentOptions": [
    {
      "SegmentId": 1,
      "SegmentName": "入口到摆轮D1",
      "LengthMm": 4700,          // 线段长度（毫米）
      "SpeedMmps": 1600,         // 传送速度（毫米/秒）
      "TimeToleranceMs": 600     // 时间容差（毫秒）
    }
  ]
}
```

### 位置配置 (PositionOptions)

定义每个摆轮位置的路由信息：

```json
{
  "PositionOptions": [
    {
      "PositionIndex": 1,        // 位置索引
      "FrontSensorId": 9,        // 前置传感器 IO 点
      "DiverterId": 1,           // 摆轮设备 ID
      "SegmentId": 1,            // 所属线段
      "LeftChuteIds": [1],       // 左转格口列表
      "RightChuteIds": [],       // 右转格口列表（空表示直通）
      "Remarks": "摆轮1"
    }
  ]
}
```

### 传感器配置 (SensorOptions)

配置传感器 IO 点：

```json
{
  "SensorOptions": [
    {
      "Point": 8,                      // IO 点位
      "Type": "ParcelCreateSensor",    // 传感器类型
      "TriggerState": "Low",           // 触发电平（Low/High）
      "SensorName": "创建包裹",
      "PollIntervalMs": 10,            // 轮询间隔（毫秒）
      "DebounceWindowMs": 30           // 防抖窗口（毫秒）
    }
  ]
}
```

### IO 联动配置 (IoLinkagePointOptions)

配置系统状态与 IO 输出的联动关系：

```json
{
  "IoLinkagePointOptions": [
    {
      "Point": 1,                      // IO 点位
      "RelatedSystemState": "Running", // 关联系统状态
      "Name": "单件分离",
      "TriggerState": "Low",           // 触发电平
      "DurationMs": 0,                 // 持续时间（0表示持续）
      "DelayMs": 4000,                 // 延迟时间（毫秒）
      "Note": "单件分离"
    }
  ]
}
```

## 技术栈

- **.NET 8.0**: 现代化的 .NET 平台
- **C# 12**: 最新语言特性
- **Microsoft.Extensions.Hosting**: 通用主机，支持依赖注入
- **Microsoft.Extensions.Logging**: 统一日志接口
- **NLog**: 高性能日志框架
- **System.Threading.Channels**: 高性能异步队列
- **ConcurrentDictionary**: 高并发数据结构

## 运行要求

### 系统要求
- **操作系统**: Windows 10/11, Windows Server 2016+
- **.NET Runtime**: .NET 8.0 或更高版本
- **内存**: 建议 4GB 以上
- **CPU**: 建议 4 核以上

### 网络要求
- 与摆轮设备通信：局域网，低延迟（< 10ms）
- 与上游系统通信：稳定网络连接
- 与 EMC 控制器通信：工业以太网或串口转网口

## 部署说明

### 1. 作为 Windows 服务运行

```batch
REM 安装服务（使用提供的脚本）
install.bat

REM 卸载服务
uninstall.bat
```

### 2. 作为控制台应用运行（调试模式）

```batch
dotnet run --project WheelDiverterSorter.Host
```

### 3. 配置文件

主配置文件：`WheelDiverterSorter.Host/appsettings.json`

日志配置：`WheelDiverterSorter.Host/nlog.config`

## 日志管理

- **日志位置**: `logs/` 目录
- **自动清理**: 可配置保留天数（默认 2 天）
- **日志级别**: Trace, Debug, Info, Warn, Error, Fatal

## 性能特点

1. **高并发**: 基于 Actor 模型的位置队列，无锁设计
2. **低延迟**: 传感器 5-10ms 高频轮询
3. **高可靠**: 自动重连、异常隔离、事件驱动
4. **可扩展**: 清晰的分层架构，易于扩展新设备、新协议

## 安全性

1. **异常隔离**: 使用事件机制隔离异常，不影响主流程
2. **超时控制**: 所有网络操作均有超时保护
3. **状态机**: 严格的状态转换控制，防止非法操作
4. **防抖处理**: 传感器信号防抖，避免误触发

## 扩展开发

### 添加新摆轮设备驱动

1. 实现 `IWheelDiverter` 接口
2. 在 `WheelDiverterSorter.Drivers/Vendors/` 创建新厂商目录
3. 在 `Program.cs` 中注册新驱动

### 添加新上游协议

1. 实现 `IUpstreamRouting` 接口
2. 在 `WheelDiverterSorter.Ingress/` 添加实现类
3. 更新依赖注入配置

### 添加新 EMC 控制器

1. 实现 `IEmcController` 接口
2. 在 `WheelDiverterSorter.Drivers/Vendors/` 创建新厂商目录
3. 在 `Program.cs` 中注册新驱动

## 常见问题

### Q1: 摆轮设备连接不上？
**A**: 检查网络连接、IP 地址配置、防火墙设置，查看日志中的连接错误信息。

### Q2: 包裹分拣到错误的格口？
**A**: 检查路由分配逻辑、位置配置（LeftChuteIds/RightChuteIds）、传感器触发时序。

### Q3: 系统性能不足？
**A**: 调整传感器轮询间隔、增加 Actor 并发度、优化网络延迟、升级硬件配置。

### Q4: 日志文件占用空间太大？
**A**: 调整日志清理配置（RetentionDays、CheckIntervalHours），降低日志级别。

## 许可证

本项目使用的许可证信息请查看 LICENSE 文件。

## 联系方式

如有问题或建议，请通过 GitHub Issues 提交。

---

**注意**: 本系统涉及硬件设备控制，部署前请确保：
1. 所有设备已正确连接和配置
2. 网络环境稳定可靠
3. 已进行充分的测试和调试
4. 操作人员已接受培训

**警告**: 急停按钮必须正确配置并可随时触发，确保人员和设备安全。
