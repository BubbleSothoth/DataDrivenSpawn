# DataDrivenSpawn 软件用户手册

**文档编号：** DDS-SUM-001
**文档版本：** V1.1
**适用软件：** DataDrivenSpawn（指令集 V1.1）
**文档状态：** 正式版
**编制日期：** 2026-10-03
**保密标识：** 公开

## 修改记录

| 版本 | 日期 | 修改内容 | 修改者 |
|---|---|---|---|
| V1.0 | 2026-10-02 | 首次发布。依据 GJB 438C-2021 附录 Q 组织正文，覆盖安装、配置、操作、消息及故障恢复。 | DataDrivenSpawn 项目组 |
| V1.1 | 2026-10-03 | 采用 DESTORY 分组等待语法，增加重设时间、移除分组、绝对时间等待和相对时长等待指令。 | DataDrivenSpawn 项目组 |

## 目录

1. 范围  
2. 引用文件  
3. 软件综述  
4. 软件入门  
5. 使用指南  
6. 注释  
附录 A 完整配置示例  
附录 B 快速参考  
附录 C 验收检查表

# 1 范围

## 1.1 标识

本文档适用于开源软件组件 **DataDrivenSpawn**，软件名称、版本和发布标识见表 1。

| 项目 | 标识 |
|---|---|
| 软件名称 | DataDrivenSpawn 数据驱动导演系统 |
| 软件缩略名 | DDS |
| 软件配置项 | `Scripts/Spawn` 目录内的 C# 源文件 |
| 适用版本 | Git `main` 分支，指令集 V1.1 |
| 配置格式版本 | 七列刷新行、兼容六列刷新行；`$WAIT`、`$RESET`、`$REMOVE` 指令 |
| 发布渠道 | GitHub 仓库 `BubbleSothoth/DataDrivenSpawn` |

## 1.2 系统概述

DataDrivenSpawn 是面向 Unity 项目的数据驱动导演组件。用户以文本配置规定 GameObject 的创建时间或前置条件、Prefab 名称、三维位置、分组、待挂载的 Component 以及 Component 的 public 字段值；运行时由 `SpawnController` 顺序解析并执行。软件可用于弹幕射击、关卡波次、剧情触发、测试场景和其他需要按时间或条件组织对象的场合。

软件具有以下一般特性：

- 以毫秒时间轴和可扩展条件驱动对象创建；
- 通过 Inspector 显式登记 Prefab，不依赖 `Resources.Load`；
- 通过反射查找或添加 Component，并按字段真实类型写入 public field；
- 内置常用基础类型和 Unity 类型转换器，并支持自定义扩展；
- 逐行隔离配置错误，使单条错误不阻断后续有效指令；
- 通过缓存和单帧指令限额控制运行开销。

该软件不提供图形化编排器、网络同步、存档系统、对象池、Prefab 自动寻址或业务对象销毁逻辑。上述能力由集成项目负责。

## 1.3 文档概述

本文档用于指导 Unity 开发人员、关卡配置人员和测试人员安装、配置、启动、使用、停止及排查 DataDrivenSpawn。第 3 章说明软件能力与运行条件；第 4 章给出首次安装和启停规程；第 5 章给出全部用户操作、输入约定、数据备份、消息及恢复规程；附录提供可直接复用的示例、快速参考和验收检查表。

本软件及本文档均按公开开源资料管理。用户应遵守仓库 `LICENSE`；不得在公开配置、日志、截图或问题报告中写入涉密信息、访问令牌、私钥或个人敏感信息。

# 2 引用文件

| 编号或标识 | 文件名称 | 版本/日期 | 来源 |
|---|---|---|---|
| GJB 438C-2021 | 军用软件开发文档通用要求 | 2021-12-30 发布 | 标准公开发行渠道 |
| DDS-README | DataDrivenSpawn README | 与本文档同一发布提交 | 本仓库 `README.md` |
| DDS-REQ | DataDrivenSpawn 需求文档 | 仓库当前版本 | 本仓库 `需求文档.markdown` |
| UNITY-MANUAL | Unity Manual / Scripting API | 与集成工程所用 Unity 版本一致 | Unity 官方文档 |

注：本文档依据 GJB 438C-2021 第 5.17 条及附录 Q 的《软件用户手册》内容和正文结构编制。Unity 版本由宿主工程确定；发生接口差异时，以宿主工程所用版本的官方文档为准。

# 3 软件综述

## 3.1 软件应用

软件接收一份 UTF-8 文本资产和一张 Prefab 注册表，输出按配置创建的 GameObject 实例。每条刷新记录可以指定创建时间、位置、分组和 JSON 初始化数据；等待记录可以阻塞后续流程，直至时间、外部信号、分组清空或自定义条件成立。

典型能力如下：

1. 在导演启动后的指定毫秒创建指定 Prefab；
2. 在控制器局部空间或世界空间设置出生位置；
3. 将实例挂为控制器子对象或放置于场景根级；
4. 自动复用已有 Component，或在缺失时执行 `AddComponent`；
5. 为 Component 的可写 public 实例字段赋值；
6. 按组跟踪由当前控制器创建且尚未销毁的对象；
7. 接收外部代码设置的布尔信号；
8. 重设导演时间，或按绝对时间和相对时长等待；
9. 解除既有对象的分组归属而不销毁对象；
10. 自动发现和注册自定义条件、自定义类型转换器。

受益情况：关卡流程与业务脚本解耦，常规波次和参数变更可通过文本完成。运行改进体现在配置只解析一次、指令索引单向前进、反射结果缓存和单帧执行数受限。使用收益取决于配置规模、Prefab 复杂度和宿主工程对象生命周期设计。

## 3.2 软件清单

安装和运行本软件所需文件见表 2。文件均应来自同一 Git 基线，不应混用不同提交的文件。

| 文件 | 用途 | 必需性 |
|---|---|---|
| `SpawnController.cs` | 调度、Prefab 注册、实例化、信号和组计数 | 必需 |
| `SpawnConfigParser.cs` | 解析刷新行和 `$WAIT` 指令 | 必需 |
| `DirectorConditions.cs` | 条件接口、注册表和内置条件 | 必需 |
| `SpawnedObjectTracker.cs` | 对象销毁时回报分组计数 | 必需 |
| `JsonParser.cs`、`JsonDataDefine.cs` | JSON 解析和只读数据模型 | 必需 |
| `ComponentInitializer.cs` | 添加 Component 并初始化 public 字段 | 必需 |
| `ReflectionCache.cs` | 缓存 Component 类型及字段信息 | 必需 |
| `TypeConverter.cs` | 类型转换接口、发现、注册和路由 | 必需 |
| `BuiltInTypeConverters.cs` | 内置字段类型转换器 | 必需 |
| `Examples/spawn-config.txt` | 示例配置 | 可选 |
| 用户导演文本 | 关卡或场景的实际指令 | 运行必需 |
| 业务 Prefab 和 Component 脚本 | 被创建和初始化的项目资源 | 按项目必需 |

紧急恢复所需标识至少包括：已验证的 Git 提交号、导演文本、Prefab 注册表截图或场景/Prefab 资产、集成工程的 Unity 版本。建议使用 Git 提交和标签固化上述配置。

## 3.3 软件环境

### 3.3.1 硬件环境

软件不直接要求专用硬件。计算机应满足宿主 Unity 编辑器和目标游戏的最低要求，并留有足够内存容纳场景、Prefab 实例及业务资源。大量同时实例化会产生 CPU、内存和渲染负载，容量指标应由集成项目通过目标平台测试确定。

### 3.3.2 软件环境

- Unity 工程能够编译仓库中的 C# 语法和所引用的 `UnityEngine` API；
- 业务脚本已加入工程且不存在编译错误；
- 导演配置以 Unity `TextAsset` 可导入的文本文件保存，建议 UTF-8 编码；
- 不需要数据库、网络连接、后台服务或额外 Unity Package；
- 若采用 IL2CPP，自动发现的扩展类型建议标记 `[UnityEngine.Scripting.Preserve]`，并按工程裁剪策略进行真机验证。

### 3.3.3 其他资源

配置人员需要文本编辑器；为避免空格冒充 Tab，编辑器应能够显示不可见字符。测试人员需要 Unity Console 读取消息，并能够进入 Play Mode、销毁分组对象和触发业务信号。

## 3.4 软件组织和操作概述

运行链路如下：

1. `SpawnController.Awake` 建立 Prefab 字典并解析完整导演文本；
2. `Start` 记录导演起始时间；
3. 每帧 `Update` 从当前索引开始顺序处理；
4. 遇到未到时的刷新行或未满足的等待条件时暂停在当前行；
5. 遇到 `$RESET` 或 `$REMOVE` 时立即修改导演状态并继续；
6. 刷新行就绪后实例化 Prefab、登记分组并应用 JSON；
7. `SpawnedObjectTracker` 在对象销毁时解除跟踪；
8. 索引到达末尾时 `IsCompleted` 为 true，控制器停止调度。

用户可通过 `CurrentInstructionIndex`、`ElapsedMilliseconds`、`IsCompleted`、`HasSignal` 和 `GetLivingGroupCount` 查询状态。控制器每帧最多执行 `Maximum Instructions Per Frame` 条就绪指令；达到限额后，其余就绪指令延至下一帧。

## 3.5 意外事故及运行的备用状态和方式

- `Spawn Text` 未配置：控制器在 `Awake` 输出错误并禁用，不创建对象；应补齐资产后重新进入 Play Mode。
- 单行、JSON、Component、字段或类型转换错误：仅跳过对应行、组件或字段；其余有效配置继续执行。
- 等待条件不存在或求值抛出异常：输出错误并跳过该等待行，以避免流程永久锁死。
- 控制器 GameObject 被禁用：Unity 不再调用 `Update`，流程实际挂起；重新启用后继续，时间是否推进取决于 `Use Unscaled Time` 和全局时间状态。
- 调用 `Restart()`：索引和信号复位，起始时间重置；**既有实例不会被销毁，分组计数也不会清空**。需要全量复位时，集成项目应先安全销毁旧实例，再重启或重载场景。
- 执行 `$REMOVE GROUP`：对象仍存活，但不再影响对应 `DESTORY` 等待。后续创建的同名分组对象会重新建立独立跟踪。
- 配置破坏或误修改：从已验证 Git 提交恢复导演文本、场景和 Prefab；不要在运行中覆盖正在使用的 `TextAsset` 作为恢复手段。

## 3.6 保密性

软件不实现身份鉴别、访问控制、加密或审计。导演文本、Prefab 名称、脚本类型名和 Console 日志可能暴露工程结构；用户负责按项目密级在受控环境中保存和传输。公开问题报告应移除令牌、仓库凭据、涉密需求、个人信息和内部路径。Git 凭据不得写入配置文件、源码或本文档。

## 3.7 帮助和问题报告

优先查阅本手册第 5.7 条和仓库 `README.md`。无法解决时，通过仓库 Issues 或项目约定渠道报告，并提供：

- DataDrivenSpawn 提交号和宿主 Unity 版本；
- 最小可复现导演文本（敏感数据脱敏）；
- `SpawnController` Inspector 配置；
- 完整 Console 消息和调用栈；
- 复现步骤、期望结果、实际结果及发生频率；
- 目标平台、脚本后端（Mono/IL2CPP）和裁剪级别。

# 4 软件入门

## 4.1 软件的首次用户

本软件没有独立电源、显示器、光标或键盘控制。设备上电、关机、显示和输入规程均按宿主开发计算机、Unity 编辑器和目标平台规程执行。首次用户应具备 Unity 场景、Prefab、MonoBehaviour、Inspector、JSON 和 C# public field 的基础知识。

首次使用前确认：工程可正常编译；`Scripts/Spawn` 文件齐全；目标 Prefab 名称唯一；业务字段为 public 实例字段；导演文本使用真实 Tab；敏感数据未写入配置。

## 4.2 访问控制

软件本身没有用户账户和口令。对源码、场景、Prefab、导演文本、构建产物和日志的访问由操作系统、版本控制平台及项目权限策略控制。授权变更应由仓库或工程管理员执行；用户不得在问题报告中附带访问令牌。

## 4.3 安装和设置

1. 从已批准的 Git 提交取得仓库内容，核对提交号。
2. 将 `Scripts/Spawn` 整个目录复制到 Unity 工程的 `Assets/Scripts/Spawn`；保持文件集合完整。
3. 等待 Unity 导入并编译，消除 Console 中的编译错误。
4. 在场景中新建 GameObject，挂载 `DataDrivenSpawn.SpawnController`。
5. 新建或导入导演文本文件，将其拖入 `Spawn Text`。
6. 将所有会被创建、或会被 `GameObject` 字段引用的 Prefab 拖入 `Prefab Table`。
7. 设置 `Spawn Space`、`Parent Spawned Objects`、`Use Unscaled Time` 和 `Maximum Instructions Per Frame`。
8. 保存场景，在版本控制中提交源码、导演文本、场景和 Prefab 变更。

安装验证：进入 Play Mode，确认 Console 无 `SpawnController 未配置导演文本` 错误；使用最小配置在 0 ms 创建一个已登记 Prefab；检查位置、父对象和 public 字段值是否符合配置。

卸载时，先从场景和 Prefab 中移除 `SpawnController` 及对 `DataDrivenSpawn` 类型的引用，再删除 `Assets/Scripts/Spawn`。删除前应提交或备份工程；否则 Unity 场景可能保留 Missing Script 引用。

## 4.4 启动

1. 打开包含 `SpawnController` 的场景。
2. 在 Inspector 复核 `Spawn Text` 和 `Prefab Table`。
3. 进入 Play Mode 或启动包含该场景的构建。
4. 观察 Console；`Start` 执行后导演时间从 0 ms 起算。
5. 通过生成结果或状态属性确认流程启动。

启动故障检查顺序：工程是否编译成功；控制器及其 GameObject 是否启用；`Spawn Text` 是否赋值；文本是否含有效行；Prefab 名称是否与 `createName` 大小写完全一致；Console 是否报告解析错误。

## 4.5 停止和挂起

正常停止由以下任一条件形成：退出 Play Mode、卸载场景、销毁控制器、禁用控制器，或执行完全部指令。`IsCompleted == true` 表示正常到达文本末尾；它不表示已生成对象全部销毁。

临时挂起可禁用 `SpawnController` 组件或其 GameObject。恢复时重新启用。注意：默认 `Use Unscaled Time == false` 时，若仅禁用组件而游戏时间仍推进，恢复后可能一次执行多条已到时指令；如需真正冻结导演时钟，应由宿主工程同步冻结所选时间源或实现自定义暂停策略。

# 5 使用指南

## 5.1 能力

### 5.1.1 配置 Inspector

| 属性 | 含义 | 操作要求 |
|---|---|---|
| Spawn Text | 导演文本 `TextAsset` | 必填；运行时在 `Awake` 一次性解析 |
| Prefab Table | Prefab 注册表 | 名称区分大小写；重复名称保留首次项 |
| Spawn Space | `Local` 或 `World` | Local 使用 `transform.TransformPoint` |
| Parent Spawned Objects | 是否挂到控制器下 | 勾选后实例成为控制器子对象 |
| Use Unscaled Time | 是否使用 `Time.unscaledTime` | 需要忽略 `Time.timeScale` 时启用 |
| Maximum Instructions Per Frame | 单帧就绪指令限额 | 必须大于等于 1；默认 10000 |

### 5.1.2 编排流程

导演文本中的有效记录严格按文件顺序执行。刷新记录的 `createTime` 是当前导演时间轴上的绝对毫秒数，不是相对上一条记录的延时。`$RESET TIMER` 会改变后续刷新记录和 `TIMER UNTIL` 使用的时间基准。等待指令形成屏障：其后的刷新记录必须同时满足屏障和当前导演时间条件。

## 5.2 约定

- 空行和以 `#` 开头的行视为注释；
- 刷新列之间必须使用 U+0009 Tab，不能使用连续空格；
- 数值使用不受系统区域影响的格式，小数点使用 `.`；
- Prefab 键为 `prefab.name`，区分大小写；分组和信号不区分大小写；
- JSON 属性名 `ComponentVar`、`Component`、`Variables`、`Name` 和 `Data` 应按示例拼写；
- `Component` 推荐填写包含命名空间的完整类型名；简单名存在歧义时拒绝解析；
- `Name` 必须与 public field 名称完全一致；属性（property）、private/protected 字段、静态字段和只读字段不属于支持的配置对象；
- JSON 采用双引号，不能写注释或尾随逗号；
- Prefab 上既有组件和动态添加组件的 `Awake/OnEnable` 均可能早于字段赋值；依赖配置值的业务逻辑应放在 `Start` 或更晚阶段。

## 5.3 处理规程

### 5.3.1 编写刷新行

推荐七列格式：

```text
createTime<Tab>createName<Tab>posX<Tab>posY<Tab>posZ<Tab>group<Tab>json
```

| 列 | 类型 | 规则 |
|---|---|---|
| createTime | 非负 long | 导演启动后的绝对毫秒数 |
| createName | 非空字符串 | 必须命中 Prefab Table，区分大小写 |
| posX/posY/posZ | 有限 float | 不接受 NaN 或 Infinity |
| group | 字符串 | `-` 表示不分组；其他值用于组存活计数 |
| json | JSON 对象或空 | 控制 Component 和 public field 初始化 |

兼容六列旧格式：省略 `group`，第五个 Tab 后全部内容作为 JSON。JSON 内部允许包含 Tab，因为解析器只切分固定数量的前置列。

### 5.3.2 编写等待和控制指令

```text
$WAIT DESTORY WaveA
$WAIT SIGNAL BossIntroFinished
$WAIT TIMER UNTIL 5000
$WAIT TIMER SLEEP 1000
$RESET TIMER
$RESET TIMER 2500
$REMOVE GROUP WaveA
```

| 指令 | 参数 | 处理规则 |
|---|---|---|
| `$WAIT DESTORY` | 1 个分组名 | 当前控制器追踪的该组存活数为 0 时放行 |
| `$WAIT SIGNAL` | 1 个信号名 | `SetSignal(name, true)` 已调用时放行 |
| `$WAIT TIMER UNTIL` | 1 个非负毫秒整数 | 导演时间达到参数时放行 |
| `$WAIT TIMER SLEEP` | 1 个非负毫秒整数 | 从首次到达该指令起经过参数指定时长后放行 |
| `$RESET TIMER` | 可选的非负毫秒整数 | 立即把导演时间重设为参数；省略时重设为 0 |
| `$REMOVE GROUP` | 1 个非空分组名 | 立即解除此前已登记到该组的全部对象，不销毁对象 |

`TIMER UNTIL` 使用绝对时间。例如当前导演时间为 3000 ms，`$WAIT TIMER UNTIL 5000` 最多再等待 2000 ms。`TIMER SLEEP` 使用相对时长；无论首次到达时导演时间是多少，`$WAIT TIMER SLEEP 5000` 都从该时刻继续等待 5000 ms。

`$REMOVE GROUP WaveA` 只影响执行该指令之前已登记到 WaveA 的对象。这些对象变为无分组状态，销毁时不再改变 WaveA 的存活数。该指令之后新创建的 WaveA 对象仍正常登记。

为兼容旧配置，`GROUP_EMPTY`、`GROUP_DESTROYED` 和 `DESTROY` 仍按 `DESTORY` 处理；旧的 `$WAIT TIME ms` 仍按 `$WAIT TIMER UNTIL ms` 处理。新版配置应使用 V1.1 规范写法。条件不存在、参数错误或求值异常时，该等待行记录错误后被跳过；格式错误的控制指令在解析阶段被跳过。

### 5.3.3 配置 Component 和字段

```json
{
  "ComponentVar": [
    {
      "Component": "Game.EnemyController",
      "Variables": [
        { "Name": "moveSpeed", "Data": 5.0 },
        { "Name": "targetPosition", "Data": { "x": 0, "y": 2, "z": 0 } },
        { "Name": "bulletPrefab", "Data": "Bullet-001" }
      ]
    }
  ]
}
```

执行时先查找指定 Component；对象已有该组件则复用，否则执行 `AddComponent`。随后逐字段读取真实 `FieldType`、转换 `Data` 并赋值。单个组件或字段失败不影响同一对象的其余项。

### 5.3.4 内置数据类型

| C# 字段类型 | Data 格式 | 示例/说明 |
|---|---|---|
| bool | JSON 布尔或可解析字符串 | `true` |
| int、long、float、double | JSON 数值 | `5`、`3.5` |
| string | JSON 字符串或 null | `"Boss"` |
| Vector2 | 含 x、y 的对象 | `{"x":1,"y":2}` |
| Vector3 | 含 x、y、z 的对象 | `{"x":1,"y":2,"z":3}` |
| Quaternion | 含 x、y、z 的对象 | 三值按欧拉角传入 `Quaternion.Euler` |
| Color | 含 r、g、b，可选 a | a 省略时为 1 |
| GameObject | Prefab 名称字符串 | 从同一控制器的 Prefab Table 查询 |
| 任意 Enum | 枚举成员名称字符串 | 不区分大小写 |

### 5.3.5 从外部发送信号

```csharp
using UnityEngine;
using DataDrivenSpawn;

public sealed class BossIntro : MonoBehaviour
{
    public SpawnController director;

    public void FinishIntro()
    {
        director.SetSignal("BossIntroFinished");
    }
}
```

清除信号时调用 `SetSignal("BossIntroFinished", false)`。同一信号置位后会保持为 true，直至显式清除或调用 `Restart()`。

### 5.3.6 扩展自定义条件

```csharp
using System.Collections.Generic;
using UnityEngine.Scripting;
using DataDrivenSpawn;

[Preserve]
public sealed class ScoreCondition : ISpawnConditionEvaluator
{
    public string Keyword { get { return "SCORE"; } }

    public bool IsSatisfied(DirectorContext context, IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 1) throw new System.FormatException("SCORE 需要 1 个参数。");
        int required = int.Parse(arguments[0]);
        return GameScore.Current >= required;
    }
}
```

实现类必须公开、非抽象、无泛型参数并具有公开无参构造函数。框架会自动发现；也可调用 `DirectorConditionRegistry.Register` 显式注册或覆盖关键字。导演文本写法为 `$WAIT SCORE 10000`。

### 5.3.7 扩展自定义字段类型

实现 `ITypeConverter` 的公开、非抽象、具有无参构造函数的类即可自动发现。`CanConvert(Type)` 声明支持范围，`Convert` 返回可直接写入目标字段的值。也可调用 `TypeConverter.Register` 显式注册；后注册项优先并清空路由缓存。IL2CPP 工程应使用 `[Preserve]` 或等效 `link.xml` 保护扩展类。

### 5.3.8 时间和分组控制

```text
$RESET TIMER
$WAIT TIMER SLEEP 500
$REMOVE GROUP Escort
$WAIT DESTORY Escort
```

上述流程先把导演时间重设为 0，再相对等待 500 ms，然后解除此前 Escort 组对象的分组。紧随其后的 `DESTORY Escort` 会立即成立，除非在移除分组后、等待指令前又创建了新的 Escort 组对象。

代码也可调用 `ResetTimer(long)` 和 `RemoveGroup(string)`。传给 `ResetTimer` 的值必须为非负数；`RemoveGroup` 对不存在的分组不产生错误。

### 5.3.9 重启和状态查询

```csharp
if (director.IsCompleted)
{
    Debug.Log("导演流程结束");
}

long elapsed = director.ElapsedMilliseconds;
int alive = director.GetLivingGroupCount("WaveA");
director.Restart();
```

`Restart()` 不销毁旧对象、不重置组计数，只重置当前索引、信号和起始时间。不要在未处理旧对象时用它实现“重新开始关卡”。

## 5.4 有关的处理

下列处理由软件自动完成，用户不直接调用：读取并缓存文本、解析 JSON、扫描程序集、缓存 Component 类型和字段、跟踪组对象销毁、限制单帧指令数。用户责任包括保证输入可信、处理对象生命周期、决定错误是否允许继续、评估大量实例化的帧耗时，以及在发布构建上验证代码裁剪结果。

## 5.5 数据备份

1. 将 `Scripts/Spawn`、导演文本、场景、Prefab 和业务脚本纳入 Git。
2. 在可发布状态创建提交或标签，并记录 Unity 版本及 DataDrivenSpawn 提交号。
3. 修改导演文本前建立分支或提交；不要只保留 Unity Library 中的导入副本。
4. 评审时保留 Inspector 配置截图或采用工程内自动化检查，确保 Prefab Table 可追溯。
5. 发生误改时，从已验证提交恢复相关文本和资产，再重新导入、编译和执行验收检查。

## 5.6 错误、故障和紧急情况下的恢复

| 现象 | 可能原因 | 恢复步骤 |
|---|---|---|
| 完全不创建对象 | Spawn Text 为空、控制器禁用或无有效行 | 赋值 TextAsset；启用对象；查看 Console；重新启动场景 |
| 某行不创建 | 时间未到、前置等待未满足、Prefab 未注册或行格式错误 | 查询当前索引；检查等待条件；核对名称和 Tab；修正后重启 |
| 对象创建但字段未赋值 | JSON 错误、字段非 public、字段名不匹配或无转换器 | 按 Console 指定行/字段修正；必要时实现转换器 |
| Component 未添加 | 类型不存在、简单名歧义或 `AddComponent` 失败 | 使用完整类型名；确认脚本可编译且允许动态添加 |
| DESTORY 永久等待 | 对象仍存活、组名错误或期望对象未解除分组 | 检查组存活数；确认对象销毁或执行 REMOVE GROUP |
| TIMER UNTIL 长时间等待 | 导演时间被 RESET 到较小值或所选时间源未推进 | 查询 ElapsedMilliseconds；检查 RESET 顺序和时间源 |
| TIMER SLEEP 重新计时 | 等待期间由外部代码调用了 ResetTimer | 避免在活动的 SLEEP 上外部重设时间，或接受重新计时语义 |
| SIGNAL 永久等待 | 未调用 SetSignal、控制器引用错误或名称不一致 | 检查调用路径；读取 `HasSignal`；统一名称 |
| 恢复后瞬间批量刷新 | 挂起期间所选时间源继续推进 | 同步冻结时间源，或重载场景/在清理后 Restart |
| 发布构建缺少扩展 | IL2CPP 裁剪了反射发现类型 | 增加 `[Preserve]`/`link.xml`，重新构建并真机验证 |
| 单帧卡顿 | 同帧实例化过多或 Prefab 初始化昂贵 | 降低单帧限额、错开 createTime、在业务层采用对象池 |

紧急恢复规程：停止 Play Mode 或退出受影响场景；保存 Console 日志；记录提交号和当前配置；从已验证提交恢复源码、文本和资产；清除编译错误；执行第 4.3 条安装验证和附录 C；确认后再恢复正常使用。软件不会自动回滚用户配置。

## 5.7 消息

消息通过 Unity Console 输出，文本可能随版本演进。用户应按语义、来源行号和对象上下文定位，不应依赖完整字符串做自动化协议。

| 级别/消息语义 | 含义 | 用户动作 |
|---|---|---|
| Error：未配置导演文本 | 控制器已禁用 | 配置 Spawn Text 后重新启动 |
| Warning：PrefabTable 项为空/重名 | 空项被跳过；重名保留首次项 | 清理列表并确保名称唯一 |
| Warning：行列数、时间、坐标非法 | 当前刷新行被跳过 | 修正 Tab、数值和列数 |
| Error：JSON 解析失败 | 对象仍创建，但跳过该对象参数初始化 | 修复 JSON 语法 |
| Warning：未注册 Prefab | 当前刷新行被跳过 | 将 Prefab 加入表并核对大小写 |
| Warning：找不到 Component/字段 | 当前组件或字段被跳过 | 使用完整类型名和 public field |
| Error：字段初始化失败 | 转换或赋值失败，继续处理其他字段 | 按根因消息修正 Data 或转换器 |
| Error：条件无法执行 | 等待行被跳过 | 注册求值器、修正参数或异常 |
| Warning：RESET、REMOVE 或 TIMER 格式非法 | 当前控制指令被跳过 | 按第 5.3.2 条修正关键字、参数数量和非负毫秒值 |
| Warning：转换器/条件创建失败 | 自动发现的扩展无法实例化 | 提供公开无参构造并检查异常 |

## 5.8 快速参考指南

| 任务 | 最短操作 |
|---|---|
| 定时创建 | `1000<Tab>Enemy<Tab>0<Tab>0<Tab>0<Tab>-<Tab>{"ComponentVar":[]}` |
| 等到波次清空 | 对刷新行设置同一 group；随后写 `$WAIT DESTORY WaveA` |
| 等外部事件 | 写 `$WAIT SIGNAL Ready`；代码调用 `SetSignal("Ready")` |
| 等到绝对时刻 | 写 `$WAIT TIMER UNTIL 5000` |
| 等待一段时长 | 写 `$WAIT TIMER SLEEP 1000` |
| 重设导演时间 | 写 `$RESET TIMER` 或 `$RESET TIMER 2500` |
| 解除既有分组 | 写 `$REMOVE GROUP WaveA`；对象不会被销毁 |
| 设置字段 | JSON 中指定 Component、Variables、Name、Data |
| 观察完成 | 读取 `IsCompleted` |
| 重新开始调度 | 清理旧对象后调用 `Restart()`，或重载场景 |
| 定位错误 | 在 Console 按“导演文本第 N 行”回查配置 |

# 6 注释

- **导演文本**：由刷新行、等待指令、注释和空行组成的 `TextAsset` 文本。
- **刷新行**：规定 GameObject 创建时间、名称、位置、分组和 JSON 的记录。
- **流程屏障**：未满足时阻止后续指令执行的 `$WAIT` 记录。
- **Prefab 注册表**：`SpawnController` Inspector 中的 `Prefab Table`，键为 `prefab.name`。
- **分组存活数**：由当前控制器创建、登记到指定组且尚未销毁的对象数量。
- **public field**：C# 公开实例字段；不包括属性、非公开字段、静态字段和只读字段。
- **固定区域格式**：使用与设备语言无关的数值表示，小数点为 `.`。
- **基线**：经确认可重现的软件、配置和文档版本集合。

# 附录 A 完整配置示例

下例在 1000 ms 创建两个敌人；两者全部销毁后把导演时间重设为 2000 ms，再相对等待 500 ms。此时导演时间达到 2500 ms，Boss 刷新行立即执行。

```text
# 时间、Prefab、X、Y、Z、分组、JSON；列之间为真实 Tab
1000	Enemy01	-2	5	0	WaveA	{"ComponentVar":[{"Component":"Game.Enemy01Ctrl","Variables":[{"Name":"targetPosition","Data":{"x":0,"y":0,"z":0}},{"Name":"moveSpeed","Data":5},{"Name":"bulletPrefab","Data":"Bullet-001"}]}]}
1000	Enemy01	2	5	0	WaveA	{"ComponentVar":[{"Component":"Game.Enemy01Ctrl","Variables":[{"Name":"targetPosition","Data":{"x":0,"y":0,"z":0}},{"Name":"moveSpeed","Data":5},{"Name":"bulletPrefab","Data":"Bullet-001"}]}]}
$WAIT DESTORY WaveA
$RESET TIMER 2000
$WAIT TIMER SLEEP 500
2500	Boss01	0	6	0	Boss	{"ComponentVar":[{"Component":"Game.Boss01Ctrl","Variables":[{"Name":"HP","Data":5000},{"Name":"MoveSpeed","Data":3}]}]}
```

# 附录 B 快速参考

```text
# 注释
$WAIT DESTORY <分组>
$WAIT SIGNAL <信号>
$WAIT TIMER UNTIL <非负毫秒>
$WAIT TIMER SLEEP <非负毫秒>
$RESET TIMER [非负毫秒]
$REMOVE GROUP <分组>

SpawnController.SetSignal(name, true|false)
SpawnController.HasSignal(name)
SpawnController.GetLivingGroupCount(group)
SpawnController.ResetTimer(milliseconds)
SpawnController.RemoveGroup(group)
SpawnController.Restart()
SpawnController.ElapsedMilliseconds
SpawnController.CurrentInstructionIndex
SpawnController.IsCompleted
```

# 附录 C 验收检查表

| 序号 | 检查项 | 通过准则 |
|---|---|---|
| 1 | 文件完整性 | 第 3.2 条全部必需文件处于同一基线 |
| 2 | 编译 | Unity Console 无编译错误 |
| 3 | 最小刷新 | 0 ms 配置能创建登记的 Prefab |
| 4 | 空间与父子关系 | Local/World 和 Parent 结果符合 Inspector |
| 5 | Component | 缺失组件被添加，已有组件被复用 |
| 6 | 字段类型 | 各项目实际使用类型均正确赋值 |
| 7 | 绝对时间屏障 | TIMER UNTIL 在阈值前阻塞、阈值后放行 |
| 8 | 相对时长屏障 | TIMER SLEEP 从首次到达起等待指定时长 |
| 9 | 分组屏障 | DESTORY 在组对象全销毁或移除分组后放行 |
| 10 | 重设时间 | RESET TIMER 默认归零，并支持指定非负时间 |
| 11 | 移除分组 | 既有对象不再被跟踪，后续同名组可重新登记 |
| 12 | 信号屏障 | SIGNAL 在 SetSignal 后放行 |
| 13 | 错误隔离 | 故障行被记录，后续有效行仍执行 |
| 14 | 挂起恢复 | 结果符合项目选定的时间源策略 |
| 15 | 发布构建 | IL2CPP/目标平台上扩展类型未被裁剪 |
| 16 | 追溯性 | 记录软件提交号、Unity 版本和配置提交号 |

