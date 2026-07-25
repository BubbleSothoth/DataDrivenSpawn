/******************************************************************************
 * 文件名称：SpawnController.cs
 *
 * 功能描述：
 *     建立 Prefab 注册表，并按刷新时间创建和初始化对象。
 *
 * 设计原则：
 *     1. 仅负责编排文本读取、Prefab 创建和初始化入口调用；
 *     2. 不承担 JSON 解析、Component 添加、反射、字段赋值或类型转换；
 *     3. 使用 currentIndex 单向推进，避免每帧遍历全部刷新记录。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataDrivenSpawn
{
    /// <summary>
    /// 按刷新文本时间轴创建 Prefab，并调用通用组件初始化器。
    /// </summary>
    public sealed class SpawnController : MonoBehaviour
    {
        [Tooltip("每行格式：createTime、createName、posX、posY、posZ、json；各固定列使用 Tab 分隔。")]
        [SerializeField]
        private TextAsset spawnText;

        [Tooltip("可被刷新配置或 GameObject 字段引用的 Prefab；注册键使用 prefab.name。")]
        [SerializeField]
        private List<GameObject> prefabTable = new List<GameObject>();

        private readonly Dictionary<string, GameObject> prefabDictionary =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private IReadOnlyList<SpawnRecord> records = new List<SpawnRecord>();
        private int currentIndex;
        private float startTime;
        private bool scheduleStarted;

        /// <summary>
        /// 获取当前控制器建立的只读 Prefab 注册表。
        /// </summary>
        public IReadOnlyDictionary<string, GameObject> PrefabDictionary
        {
            get { return prefabDictionary; }
        }

        /// <summary>
        /// 获取最近初始化的 SpawnController 注册表，供同一运行环境中的其它系统查询。
        /// </summary>
        public static IReadOnlyDictionary<string, GameObject> SharedPrefabDictionary { get; private set; }

        /// <summary>
        /// 从共享注册表查询 Prefab。
        /// </summary>
        /// <param name="prefabName">Prefab 名称。</param>
        /// <param name="prefab">查询成功时返回 Prefab。</param>
        /// <returns>注册表存在指定有效 Prefab 时返回 true。</returns>
        public static bool TryGetSharedPrefab(string prefabName, out GameObject prefab)
        {
            prefab = null;
            IReadOnlyDictionary<string, GameObject> registry = SharedPrefabDictionary;
            return registry != null && !string.IsNullOrWhiteSpace(prefabName) &&
                   registry.TryGetValue(prefabName, out prefab) && prefab != null;
        }

        private void Awake()
        {
            BuildPrefabDictionary();
            SharedPrefabDictionary = prefabDictionary;

            // 文本格式与 JSON 解析均封装在独立解析器中，控制器只接收可直接调度的记录。
            records = spawnText == null
                ? new List<SpawnRecord>()
                : SpawnConfigParser.Parse(spawnText.text);

            if (spawnText == null)
            {
                Debug.LogError("SpawnController 未配置刷新文本，本控制器不会创建对象。", this);
                enabled = false;
            }
        }

        private void Start()
        {
            startTime = Time.time;
            scheduleStarted = true;
        }

        private void Update()
        {
            if (!scheduleStarted || currentIndex >= records.Count) return;

            long elapsedMilliseconds = (long)Math.Floor((Time.time - startTime) * 1000d);

            // currentIndex 只前进不回退，总调度复杂度为 O(n)，可支撑大量刷新记录。
            while (currentIndex < records.Count && records[currentIndex].CreateTime <= elapsedMilliseconds)
            {
                Spawn(records[currentIndex]);
                currentIndex++;
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
                Debug.LogWarning(
                    "刷新文本第 " + record.SourceLine + " 行引用了未注册 Prefab“" + record.CreateName + "”，已跳过该对象。",
                    this);
                return;
            }

            GameObject instance = Instantiate(prefab, record.Position, prefab.transform.rotation);

            // JSON 错误已经由解析层记录；此处仅放弃当前对象初始化，后续刷新记录继续执行。
            if (record.Root != null)
            {
                ComponentInitializer.Apply(instance, record.Root, prefabDictionary);
            }
        }
    }
}
