using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhotonGameSample.Infrastructure
{
    /// <summary>
    /// シンプルなサービス登録レイヤ (フェーズ1)。
    /// 目的: 後続段階での依存取得統一と、Find/Poll を不要化する受け皿。
    /// </summary>
    [DefaultExecutionOrder(-800)]
    public static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> _map = new();
        public static event Action<Type, object> OnAnyRegistered;

        public static void Register<T>(T instance) where T : class
        {
            if (instance == null)
            {
                Debug.LogWarning($"[ServiceRegistry] Try register null for {typeof(T).Name}");
                return;
            }
            var type = typeof(T);
            if (_map.TryGetValue(type, out var existing) && !ReferenceEquals(existing, instance))
            {
                Debug.LogWarning($"[ServiceRegistry] Duplicate registration for {type.Name}. Overwriting old instance.");
            }
            _map[type] = instance;
            Debug.Log($"[ServiceRegistry] Registered {type.Name} -> {instance}");
            OnAnyRegistered?.Invoke(type, instance);
        }

        public static bool TryGet<T>(out T value) where T : class
        {
            if (_map.TryGetValue(typeof(T), out var obj) && obj is T typed)
            {
                value = typed;
                return true;
            }
            value = null;
            return false;
        }

        public static T GetOrNull<T>() where T : class
        {
            TryGet<T>(out var v);
            return v;
        }

        /// <summary>
        /// 全サービス登録をクリア（ハードリセット用）。
        /// </summary>
        public static void Clear()
        {
            _map.Clear();
            Debug.Log("[ServiceRegistry] Cleared all registrations");
        }
    }
}
