/******************************************************************************
 * 文件名称：SpawnedObjectTracker.cs
 *
 * 功能描述：
 *     跟踪分组对象的销毁，并把存活数量变化通知导演控制器。
 ******************************************************************************/

using UnityEngine;

namespace DataDrivenSpawn
{
    // 跟踪器只负责生命周期通知，不承载任何业务逻辑。
    /// <summary>由导演系统自动挂载的轻量分组生命周期跟踪器。</summary>
    [DisallowMultipleComponent]
    public sealed class SpawnedObjectTracker : MonoBehaviour
    {
        private SpawnController owner;
        private string group;
        private bool initialized;

        /// <summary>将当前对象绑定到指定控制器与刷新分组。</summary>
        public void Initialize(SpawnController controller, string groupName)
        {
            owner = controller;
            group = groupName;
            initialized = true;
        }

        /// <summary>解除当前对象与导演分组的关联，但不销毁对象。</summary>
        internal void RemoveGroup()
        {
            initialized = false;
            owner = null;
            group = null;
        }

        private void OnDestroy()
        {
            if (initialized && owner != null) owner.NotifyDestroyed(this, group);
        }
    }
}
