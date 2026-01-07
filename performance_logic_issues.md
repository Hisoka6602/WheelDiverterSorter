## 性能与逻辑隐患梳理

**Summary (EN)**:  
- Duplicate parcel IDs when sensor triggers within the same millisecond cause dropped creations because `ParcelId` uses a millisecond timestamp.  
- `_parcelGates` in sorting orchestration never removes per-parcel semaphores, causing unbounded growth.  
- `EnqueueBoolAsync` still executes queued commands after cancellation, so canceled callers still trigger side effects.  
- Unbounded channels for sensor events can grow without backpressure during sensor chatter.

- **重复包裹编号导致创建失败（逻辑隐患）**  
  位置：`WheelDiverterSorter.Host/Servers/ParcelHostedService.cs` 第35-42行。包裹创建使用 `DateTimeOffset.Now.ToUnixTimeMilliseconds()` 作为 `ParcelId`，同一毫秒内的多次触发会生成相同 ID，属于竞态。  
  影响：`ParcelManager` 以 `ConcurrentDictionary` 去重，重复 ID 会被拒绝，直接丢弃包裹创建事件，造成漏分拣。建议改用全局递增或 Guid 等单调唯一值。

- **静态并发字典未清理，长期运行易内存膨胀（性能隐患）**  
  位置：`WheelDiverterSorter.Host/Servers/SortingOrchestrationHostedService.cs` 第32行 `_parcelGates`。为每个 `ParcelId` 创建的 `SemaphoreSlim` 从未移除，生命周期结束后仍常驻。  
  影响：长时间高吞吐运行时静态字典会无限增长，占用内存并拖慢字典操作。

- **取消请求后命令仍被执行（逻辑隐患）**  
  位置：`WheelDiverterSorter.Execution/PositionQueueManager.cs` 第398-415行。`EnqueueBoolAsync` 在收到可取消的 `CancellationToken` 时，仍先把命令写入无界通道。  
  影响：取消只会让调用者的 `Task` 被标记为取消，但队列里的命令仍会被处理，导致“取消”不生效且产生副作用，易引发时序错判或重复操作。

- **传感器事件无背压累积风险（性能隐患）**  
  位置：`WheelDiverterSorter.Host/Servers/PositionQueueHostedService.cs` 第239-241行。每个位置的 `PositionActor` 使用无界通道并通过 `TryWrite` 无条件接收传感器事件，未做节流或容量限制。  
  影响：传感器抖动或持续高频触发时事件会无限堆积，占用内存并延迟下游处理。
