using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace QHotUpdateSystem.EventsSystem
{
    /// <summary>
    /// 主线程派发器：
    /// 1. 运行期：使用隐藏常驻 GameObject，在 Update 中执行队列。
    /// 2. 非运行(编辑器构建等)：直接同步执行（不进入队列）避免依赖场景。
    /// 3. 线程安全：ConcurrentQueue。
    /// </summary>
    [DisallowMultipleComponent]
    internal class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private static bool _applicationQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        private void Update()
        {
            // 将等待的委托逐个执行（限制每帧最大数量可在后续扩展）
            while (_queue.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception e)
                {
                    Debug.LogError("[QHotUpdate] MainThreadDispatcher 执行回调异常: " + e);
                }
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private static void EnsureInstance()
        {
            if (_applicationQuitting) return;
            if (_instance != null) return;

            var go = new GameObject("QHotUpdateDispatcher");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThreadDispatcher>();
        }

        /// <summary>
        /// 提交到主线程执行。若当前不在运行期（例如编辑器构建脚本阶段），直接执行。
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;

            // 编辑器下，若未进入播放模式，直接同步执行，避免隐藏对象未创建
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                try { action(); } catch (Exception e) { Debug.LogError("[QHotUpdate] (Editor Sync Call) " + e); }
                return;
            }
#endif
            EnsureInstance();
            _queue.Enqueue(action);
        }
    }
}
