/******************************************************************************
 * 文件名称：DirectorConditions.cs
 *
 * 功能描述：
 *     定义可扩展的导演条件接口、自动注册表与内置条件。
 *
 * 设计原则：
 *     1. 新条件只需新增 ISpawnConditionEvaluator 实现，不修改控制器；
 *     2. 条件实例与查找结果均缓存，避免每帧重复反射；
 *     3. 条件异常被隔离到单条等待指令，不阻断整个关卡。
 ******************************************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

namespace DataDrivenSpawn
{
    // 条件模块保持独立，使新增关卡条件无需改动刷新控制器。
    /// <summary>内置导演条件的标准关键字。</summary>
    public static class DirectorConditionNames
    {
        /// <summary>等待指定时间。</summary>
        public const string Time = "TIME";
        /// <summary>等待外部信号。</summary>
        public const string Signal = "SIGNAL";
        /// <summary>等待指定刷新分组中没有存活对象。</summary>
        public const string GroupEmpty = "GROUP_EMPTY";
    }

    /// <summary>向条件求值器提供只读的导演运行状态。</summary>
    public sealed class DirectorContext
    {
        private readonly SpawnController controller;

        /// <summary>获取发起本次求值的控制器。</summary>
        public SpawnController Controller { get { return controller; } }
        /// <summary>获取导演启动后经过的毫秒数。</summary>
        public long ElapsedMilliseconds { get { return controller.ElapsedMilliseconds; } }

        /// <summary>创建条件求值上下文。</summary>
        public DirectorContext(SpawnController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        /// <summary>查询导演信号是否已经置位。</summary>
        public bool HasSignal(string signalName) { return controller.HasSignal(signalName); }
        /// <summary>获取指定分组的存活对象数。</summary>
        public int GetLivingGroupCount(string group) { return controller.GetLivingGroupCount(group); }
    }

    /// <summary>定义一个可以通过“$WAIT 关键字 参数”使用的导演条件。</summary>
    public interface ISpawnConditionEvaluator
    {
        /// <summary>获取条件关键字，建议使用全大写下划线命名。</summary>
        string Keyword { get; }

        /// <summary>判断当前条件是否已经成立。</summary>
        bool IsSatisfied(DirectorContext context, IReadOnlyList<string> arguments);
    }

    /// <summary>自动发现、缓存并调用导演条件求值器。</summary>
    public static class DirectorConditionRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, ISpawnConditionEvaluator> Evaluators =
            new Dictionary<string, ISpawnConditionEvaluator>(StringComparer.OrdinalIgnoreCase);
        private static bool initialized;

        /// <summary>显式注册或覆盖一个条件求值器。</summary>
        public static void Register(ISpawnConditionEvaluator evaluator)
        {
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            if (string.IsNullOrWhiteSpace(evaluator.Keyword)) throw new ArgumentException("条件关键字不能为空。", nameof(evaluator));
            EnsureInitialized();
            lock (SyncRoot) Evaluators[evaluator.Keyword.Trim()] = evaluator;
        }

        /// <summary>尝试计算一条等待指令。</summary>
        public static bool TryEvaluate(WaitRecord wait, DirectorContext context, out bool satisfied, out string error)
        {
            satisfied = false;
            error = null;
            EnsureInitialized();

            ISpawnConditionEvaluator evaluator;
            lock (SyncRoot) Evaluators.TryGetValue(wait.Condition, out evaluator);
            if (evaluator == null)
            {
                error = "没有注册关键字为“" + wait.Condition + "”的 ISpawnConditionEvaluator。";
                return false;
            }

            try
            {
                satisfied = evaluator.IsSatisfied(context, wait.Arguments);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void EnsureInitialized()
        {
            lock (SyncRoot)
            {
                if (initialized) return;
                Type contract = typeof(ISpawnConditionEvaluator);
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
                {
                    Type[] types = GetLoadableTypes(assemblies[assemblyIndex]);
                    for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
                    {
                        Type candidate = types[typeIndex];
                        if (candidate == null || candidate.IsAbstract || candidate.IsInterface ||
                            candidate.ContainsGenericParameters || !contract.IsAssignableFrom(candidate) ||
                            candidate.GetConstructor(Type.EmptyTypes) == null)
                        {
                            continue;
                        }

                        try
                        {
                            ISpawnConditionEvaluator evaluator = (ISpawnConditionEvaluator)Activator.CreateInstance(candidate);
                            if (string.IsNullOrWhiteSpace(evaluator.Keyword)) continue;
                            string keyword = evaluator.Keyword.Trim();
                            if (!Evaluators.ContainsKey(keyword)) Evaluators.Add(keyword, evaluator);
                            else Debug.LogWarning("导演条件关键字“" + keyword + "”重复，已保留首次发现的实现。");
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning("导演条件“" + candidate.FullName + "”创建失败，已跳过。原因：" + exception.Message);
                        }
                    }
                }

                initialized = true;
            }
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception)
            {
                List<Type> result = new List<Type>();
                for (int index = 0; index < exception.Types.Length; index++)
                {
                    if (exception.Types[index] != null) result.Add(exception.Types[index]);
                }

                return result.ToArray();
            }
            catch { return new Type[0]; }
        }
    }

    /// <summary>等待导演时间达到指定毫秒数。</summary>
    [Preserve]
    public sealed class TimeConditionEvaluator : ISpawnConditionEvaluator
    {
        /// <inheritdoc />
        public string Keyword { get { return DirectorConditionNames.Time; } }

        /// <inheritdoc />
        public bool IsSatisfied(DirectorContext context, IReadOnlyList<string> arguments)
        {
            RequireArgumentCount(arguments, 1, Keyword);
            long milliseconds;
            if (!long.TryParse(arguments[0], out milliseconds) || milliseconds < 0)
            {
                throw new FormatException("TIME 的参数必须是非负毫秒整数。");
            }

            return context.ElapsedMilliseconds >= milliseconds;
        }

        internal static void RequireArgumentCount(IReadOnlyList<string> arguments, int count, string keyword)
        {
            if (arguments == null || arguments.Count != count)
            {
                throw new FormatException(keyword + " 需要 " + count + " 个参数。");
            }
        }
    }

    /// <summary>等待外部代码通过 SpawnController.SetSignal 置位指定信号。</summary>
    [Preserve]
    public sealed class SignalConditionEvaluator : ISpawnConditionEvaluator
    {
        /// <inheritdoc />
        public string Keyword { get { return DirectorConditionNames.Signal; } }

        /// <inheritdoc />
        public bool IsSatisfied(DirectorContext context, IReadOnlyList<string> arguments)
        {
            TimeConditionEvaluator.RequireArgumentCount(arguments, 1, Keyword);
            return context.HasSignal(arguments[0]);
        }
    }

    /// <summary>等待指定刷新分组中的所有对象被销毁。</summary>
    [Preserve]
    public sealed class GroupEmptyConditionEvaluator : ISpawnConditionEvaluator
    {
        /// <inheritdoc />
        public string Keyword { get { return DirectorConditionNames.GroupEmpty; } }

        /// <inheritdoc />
        public bool IsSatisfied(DirectorContext context, IReadOnlyList<string> arguments)
        {
            TimeConditionEvaluator.RequireArgumentCount(arguments, 1, Keyword);
            return context.GetLivingGroupCount(arguments[0]) == 0;
        }
    }
}
