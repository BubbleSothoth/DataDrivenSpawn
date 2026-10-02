/******************************************************************************
 * 文件名称：SpawnController.cs
 *
 * 功能描述：
 *     顺序执行导演文本，在指定时间或条件成立后创建、分组并初始化对象。
 *
 * 设计原则：
 *     1. 控制器只负责编排，解析、条件求值和反射初始化均由独立模块完成；
 *     2. 指令索引只前进不回退，避免每帧遍历全部配置；
 *     3. 对外提供信号接口，使任意游戏逻辑都能驱动条件刷新。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataDrivenSpawn
{
    /// <summary>指定导演文本中的出生坐标使用哪一种空间。</summary>
    public enum DirectorSpawnSpace
    {
        /// <summary>坐标相对于 SpawnController 所在对象。</summary>
        Local,
        /// <summary>坐标直接作为世界坐标。</summary>
        World
    }

    /// <summary>按导演文本执行时间与条件驱动的对象刷新流程。</summary>
    public sealed class SpawnController : MonoBehaviour
    {
        [Tooltip("导演文本：支持刷新行、# 注释及 $WAIT 条件指令。")]
        [SerializeField] private TextAsset spawnText;

        [Tooltip("可被创建或被 GameObject 字段引用的 Prefab；键为 prefab.name。")]
        [SerializeField] private List<GameObject> prefabTable = new List<GameObject>();

        [Tooltip("出生坐标使用控制器局部空间还是世界空间。")]
        [SerializeField] private DirectorSpawnSpace spawnSpace = DirectorSpawnSpace.Local;

        [Tooltip("是否把新对象挂到当前控制器对象下。")]
        [SerializeField] private bool parentSpawnedObjects = true;

        [Tooltip("是否使用不受 Time.timeScale 影响的时间轴。")]
        [SerializeField] private bool useUnscaledTime;

        [Tooltip("单帧最多执行的已就绪指令数，用于避免极端配置造成长帧。")]
        [SerializeField, Min(1)] private int maximumInstructionsPerFrame = 10000;

        private readonly Dictionary<string, GameObject> prefabDictionary =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> livingGroupCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<DirectorInstruction> instructions = new List<DirectorInstruction>();
        private int currentIndex;
        private float startTime;
        private bool scheduleStarted;

        /// <summary>获取当前控制器建立的 Prefab 注册表。</summary>
        public IReadOnlyDictionary<string, GameObject> PrefabDictionary { get { return prefabDictionary; } }
        /// <summary>获取当前已执行到的指令索引。</summary>
        public int CurrentInstructionIndex { get { return currentIndex; } }
        /// <summary>获取导演流程是否已执行完毕。</summary>
        public bool IsCompleted { get { return currentIndex >= instructions.Count; } }

        /// <summary>获取导演启动后经过的毫秒数。</summary>
        public long ElapsedMilliseconds
        {
            get
            {
                if (!scheduleStarted) return 0;
                return (long)Math.Floor((CurrentTime - startTime) * 1000d);
            }
        }

        /// <summary>获取最近初始化的控制器注册表，供同一运行环境中的其它系统查询。</summary>
        public static IReadOnlyDictionary<string, GameObject> SharedPrefabDictionary { get; private set; }

        private float CurrentTime { get { return useUnscaledTime ? Time.unscaledTime : Time.time; } }

        /// <summary>从最近初始化的共享注册表查询 Prefab。</summary>
        public static bool TryGetSharedPrefab(string prefabName, out GameObject prefab)
        {
            prefab = null;
            IReadOnlyDictionary<string, GameObject> registry = SharedPrefabDictionary;
            return registry != null && !string.IsNullOrWhiteSpace(prefabName) &&
                   registry.TryGetValue(prefabName, out prefab) && prefab != null;
        }

        /// <summary>设置或清除一个条件信号；“$WAIT SIGNAL 名称”会等待该信号为 true。</summary>
        public void SetSignal(string signalName, bool value = true)
        {
            if (string.IsNullOrWhiteSpace(signalName))
            {
                Debug.LogWarning("导演信号名称不能为空。", this);
                return;
            }

            string normalized = signalName.Trim();
            if (value) signals.Add(normalized);
            else signals.Remove(normalized);
        }

        /// <summary>查询一个导演信号是否已经置位。</summary>
        public bool HasSignal(string signalName)
        {
            return !string.IsNullOrWhiteSpace(signalName) && signals.Contains(signalName.Trim());
        }

        /// <summary>获取指定刷新分组中仍然存活的对象数。</summary>
        public int GetLivingGroupCount(string group)
        {
            if (string.IsNullOrWhiteSpace(group)) return 0;
            int count;
            return livingGroupCounts.TryGetValue(group.Trim(), out count) ? count : 0;
        }

        /// <summary>从头重新开始当前流程，但不会销毁此前创建的对象。</summary>
        public void Restart()
        {
            currentIndex = 0;
            signals.Clear();
            startTime = CurrentTime;
            scheduleStarted = true;
        }

        private void Awake()
        {
            BuildPrefabDictionary();
            SharedPrefabDictionary = prefabDictionary;
            instructions = spawnText == null ? new List<DirectorInstruction>() : SpawnConfigParser.Parse(spawnText.text);
            if (spawnText == null)
            {
                Debug.LogError("SpawnController 未配置导演文本，本控制器不会创建对象。", this);
                enabled = false;
            }
        }

        private void Start()
        {
            startTime = CurrentTime;
            scheduleStarted = true;
        }

        private void Update()
        {
            if (!scheduleStarted || IsCompleted) return;

            int executed = 0;
            while (!IsCompleted && executed < maximumInstructionsPerFrame)
            {
                DirectorInstruction instruction = instructions[currentIndex];
                SpawnRecord spawn = instruction as SpawnRecord;
                if (spawn != null)
                {
                    if (spawn.CreateTime > ElapsedMilliseconds) break;
                    Spawn(spawn);
                    currentIndex++;
                    executed++;
                    continue;
                }

                WaitRecord wait = instruction as WaitRecord;
                if (wait != null)
                {
                    bool satisfied;
                    string error;
                    if (!DirectorConditionRegistry.TryEvaluate(wait, new DirectorContext(this), out satisfied, out error))
                    {
                        Debug.LogError("导演文本第 " + wait.SourceLine + " 行条件无法执行，已跳过。原因：" + error, this);
                        currentIndex++;
                        executed++;
                        continue;
                    }

                    if (!satisfied) break;
                    currentIndex++;
                    executed++;
                    continue;
                }

                Debug.LogError("遇到未知导演指令，已跳过第 " + instruction.SourceLine + " 行。", this);
                currentIndex++;
                executed++;
            }
        }

        private void BuildPrefabDictionary()
        {
            prefabDictionary.Clear();
            for (int index = 0; index < prefabTable.Count; index++)
            {
                GameObject prefab = prefabTable[index];
                if (prefab == null)
                {
                    Debug.LogWarning("PrefabTable 第 " + index + " 项为空，已跳过。", this);
                    continue;
                }

                if (prefabDictionary.ContainsKey(prefab.name))
                {
                    Debug.LogWarning("PrefabTable 存在重复名称“" + prefab.name + "”，已保留首次注册项。", this);
                    continue;
                }

                prefabDictionary.Add(prefab.name, prefab);
            }
        }

        private void Spawn(SpawnRecord record)
        {
            GameObject prefab;
            if (!prefabDictionary.TryGetValue(record.CreateName, out prefab) || prefab == null)
            {
                Debug.LogWarning("导演文本第 " + record.SourceLine + " 行引用了未注册 Prefab“" + record.CreateName + "”，已跳过。", this);
                return;
            }

            Vector3 worldPosition = spawnSpace == DirectorSpawnSpace.Local
                ? transform.TransformPoint(record.Position)
                : record.Position;
            Transform parent = parentSpawnedObjects ? transform : null;
            GameObject instance = Instantiate(prefab, worldPosition, prefab.transform.rotation, parent);

            if (!string.IsNullOrEmpty(record.Group)) RegisterGroupObject(instance, record.Group);
            if (record.Root != null) ComponentInitializer.Apply(instance, record.Root, prefabDictionary);
        }

        private void RegisterGroupObject(GameObject instance, string group)
        {
            int count;
            livingGroupCounts.TryGetValue(group, out count);
            livingGroupCounts[group] = count + 1;
            SpawnedObjectTracker tracker = instance.GetComponent<SpawnedObjectTracker>();
            if (tracker == null) tracker = instance.AddComponent<SpawnedObjectTracker>();
            tracker.Initialize(this, group);
        }

        internal void NotifyDestroyed(string group)
        {
            int count;
            if (!livingGroupCounts.TryGetValue(group, out count)) return;
            if (count <= 1) livingGroupCounts.Remove(group);
            else livingGroupCounts[group] = count - 1;
        }
    }
}
