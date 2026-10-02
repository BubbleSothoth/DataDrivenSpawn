# 数据驱动导演系统

这是一套可直接接入 Unity 的通用导演系统。它按时间或条件创建 Prefab，通过反射自动发现/添加任意 `Component`，再根据目标字段的真实类型转换 JSON 数据并写入 `public field`。核心流程不包含具体业务组件分支，也不使用 `Resources.Load`。

## 接入

1. 将 `Scripts/Spawn` 整个目录复制到 Unity 工程的 `Assets/Scripts/Spawn`。
2. 在场景中新建对象并挂载 `DataDrivenSpawn.SpawnController`。
3. 在 `Spawn Text` 中放入导演文本 `TextAsset`。
4. 将可创建或可被字段引用的全部 Prefab 拖入 `Prefab Table`。注册键严格使用 `prefab.name`，名称区分大小写。
5. 业务组件的可配置成员声明为 `public field`；JSON 的 `Name` 必须与字段名称完全一致。

控制器还可以配置：

- `Spawn Space`：坐标使用控制器局部空间或世界空间；
- `Parent Spawned Objects`：是否把新对象挂到控制器对象下；
- `Use Unscaled Time`：是否使用不受 `Time.timeScale` 影响的时间；
- `Maximum Instructions Per Frame`：单帧最多执行的就绪指令数。

## 刷新行

推荐的七列格式如下，各列之间必须使用真实 Tab：

```text
createTime<Tab>createName<Tab>posX<Tab>posY<Tab>posZ<Tab>group<Tab>json
```

`createTime` 是导演启动后的绝对毫秒数。`group` 用于追踪同组对象，写 `-` 表示不分组。系统也兼容不含 `group` 的旧六列格式，JSON 内部可以包含 Tab。

所有指令严格按文件顺序执行。这样等待指令可以形成流程屏障；位于等待指令后的刷新行，必须同时满足等待条件和自身时间条件。

## 条件指令

```text
$WAIT TIME 5000
$WAIT GROUP_EMPTY WaveA
$WAIT SIGNAL BossIntroFinished
```

- `TIME`：等待导演时间达到指定毫秒数；
- `GROUP_EMPTY`：等待指定分组内由该控制器创建的对象全部销毁；
- `SIGNAL`：等待外部代码调用 `controller.SetSignal("BossIntroFinished")`。

为兼容旧配置，`DESTROY`、`GROUP_DESTROYED` 和历史拼写 `DESTORY` 均等价于 `GROUP_EMPTY`。

例如先创建一波敌人，全部击破后再创建 Boss：

```text
500	Enemy01	-2	5	0	WaveA	{"ComponentVar":[]}
500	Enemy01	2	5	0	WaveA	{"ComponentVar":[]}
$WAIT GROUP_EMPTY WaveA
3000	Boss01	0	6	0	Boss	{"ComponentVar":[]}
```

## JSON 与 public 字段

```json
{
  "ComponentVar": [
    {
      "Component": "Game.Enemy01Ctrl",
      "Variables": [
        { "Name": "targetPosition", "Data": { "x": 0, "y": 0, "z": 0 } },
        { "Name": "moveSpeed", "Data": 5 },
        { "Name": "bulletPrefab", "Data": "Bullet-001" }
      ]
    }
  ]
}
```

`Component` 推荐填写包含命名空间的完整类型名。简单类型名也可使用，但当不同命名空间存在同名组件时，框架会拒绝歧义并输出警告。对象没有该组件时会自动 `AddComponent`，已有时直接复用。

内置支持 `bool`、`int`、`long`、`float`、`double`、`string`、`Vector2`、`Vector3`、`Quaternion`、`Color`、`GameObject` 和所有 `Enum`。`Quaternion` 的 `x/y/z` 按欧拉角解释；`Color.a` 可省略，默认值为 1；`GameObject` 的字符串值从同一 Prefab 注册表查找。

## 扩展自定义条件

新增一个公开、非抽象、带无参构造函数的 `ISpawnConditionEvaluator` 实现即可自动发现，无需修改控制器：

```csharp
[UnityEngine.Scripting.Preserve]
public sealed class ScoreCondition : DataDrivenSpawn.ISpawnConditionEvaluator
{
    public string Keyword { get { return "SCORE"; } }

    public bool IsSatisfied(
        DataDrivenSpawn.DirectorContext context,
        System.Collections.Generic.IReadOnlyList<string> arguments)
    {
        int required = int.Parse(arguments[0]);
        return GameScore.Current >= required;
    }
}
```

导演文本中即可写 `$WAIT SCORE 10000`。也可以调用 `DirectorConditionRegistry.Register` 显式注册或覆盖条件。

## 扩展新类型

新增一个实现 `ITypeConverter` 的公开、非抽象、无参构造类即可被自动发现。也可以调用 `TypeConverter.Register` 显式注册；显式注册的转换器优先，可覆盖默认规则。反射发现的扩展类建议添加 `[UnityEngine.Scripting.Preserve]`，避免 IL2CPP 代码裁剪。

## 生命周期注意事项

Prefab 实例化时，Prefab 上已有组件的 `Awake/OnEnable` 会先于反射赋值执行；动态 `AddComponent` 也会立即触发该组件的 `Awake/OnEnable`。需要读取配置字段的业务逻辑应放在 `Start` 或更晚阶段。

## 错误隔离与性能

- 单行、JSON、Component、字段或类型转换错误不会阻断后续有效指令；
- 调度索引只前进不回退，不会每帧遍历全部配置；
- 每条 JSON 仅在加载文本时解析一次；
- Component 类型、字段、类型转换器和条件求值器均会缓存；
- Prefab 创建和 `GameObject` 字段引用使用字典查询；
- `Maximum Instructions Per Frame` 防止极端配置造成单帧长时间阻塞。

## 文件职责

- `SpawnController.cs`：注册 Prefab、顺序调度、创建对象和管理信号；
- `SpawnConfigParser.cs`：解析刷新行和 `$WAIT` 指令；
- `DirectorConditions.cs`：条件接口、注册表和内置条件；
- `SpawnedObjectTracker.cs`：跟踪分组对象销毁；
- `JsonParser.cs` / `JsonDataDefine.cs`：解析 JSON 并建立只读数据模型；
- `ComponentInitializer.cs`：添加组件并初始化公开字段；
- `ReflectionCache.cs`：缓存组件类型及 `FieldInfo`；
- `TypeConverter.cs` / `BuiltInTypeConverters.cs`：类型转换扩展点与内置实现。
