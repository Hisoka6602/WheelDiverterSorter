## 性能与逻辑隐患梳理

- **重复包裹编号导致创建失败（逻辑隐患）**  
  位置：`WheelDiverterSorter.Host/Servers/ParcelHostedService.cs` 第35-43行。包裹创建使用 `DateTimeOffset.Now.ToUnixTimeMilliseconds()` 作为 `ParcelId`，在传感器高频触发时，同一毫秒内的多次创建会生成相同 ID。`ParcelManager` 以 `ConcurrentDictionary` 去重，重复 ID 会被拒绝，直接丢弃包裹创建事件，造成漏分拣风险。

- **静态并发字典未清理，长期运行易内存膨胀（性能隐患）**  
  位置：`WheelDiverterSorter.Host/Servers/SortingOrchestrationHostedService.cs` 第32行 `_parcelGates`。为每个 `ParcelId` 创建的 `SemaphoreSlim` 从未移除，包裹生命周期结束后仍常驻，长时间高吞吐运行会让该静态字典无限增长，造成内存占用和字典操作性能下降。

- **取消请求后命令仍被执行（逻辑隐患）**  
  位置：`WheelDiverterSorter.Execution/PositionQueueManager.cs` 第398-421行。`EnqueueBoolAsync` 在收到可取消的 `CancellationToken` 时，仍先把命令写入无界通道；取消只会让调用者的 `Task` 被标记为取消，但队列里的命令仍会被处理，导致“取消”不生效且产生副作用，易引发时序错判或重复操作。

- **传感器事件无背压累积风险（性能隐患）**  
  位置：`WheelDiverterSorter.Host/Servers/PositionQueueHostedService.cs` 第218-243行。每个位置的 `PositionActor` 使用无界通道并通过 `TryWrite` 无条件接收传感器事件，未做节流或容量限制。若传感器抖动或持续高频触发，事件会无限堆积，占用内存并延迟下游处理。
