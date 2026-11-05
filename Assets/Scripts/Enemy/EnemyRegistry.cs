using System;
using System.Collections.Generic;
using ShootDemo.Core;
using ShootDemo.Player;
using ShootDemo.Config;
using Unity.Profiling;
using UnityEngine;

namespace ShootDemo.Enemy
{
    /// <summary>Live enemies and reusable instances are tracked separately; corpses are never alive.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class EnemyRegistry : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        private static readonly ProfilerMarker GridMarker = new ProfilerMarker("Enemies.SpatialGrid.Update");
        public SpatialGrid<EnemyLifecycle> Grid { get; private set; }
        public EnemyAIScheduler Scheduler { get; private set; }
        private readonly HashSet<EnemyLifecycle> alive = new HashSet<EnemyLifecycle>();
        private readonly Dictionary<EnemyLifecycle, Pool<EnemyLifecycle>> pools = new Dictionary<EnemyLifecycle, Pool<EnemyLifecycle>>();
        public static EnemyRegistry Instance { get; private set; }
        public int AliveCount => alive.Count;
        public IReadOnlyCollection<EnemyLifecycle> Alive => alive;
        public event Action<int> AliveCountChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
            Grid = new SpatialGrid<EnemyLifecycle>(config != null ? config.crowd.cellSize : 2.5f);
            Scheduler = GetComponent<EnemyAIScheduler>();
        }

        internal void Register(EnemyLifecycle enemy)
        {
            if (enemy != null && enemy.gameObject.scene == gameObject.scene && !enemy.Health.IsDead && alive.Add(enemy))
            {
                Grid?.Update(enemy, enemy.Position);
                Scheduler?.Register(enemy);
                AliveCountChanged?.Invoke(alive.Count);
            }
        }

        internal void Unregister(EnemyLifecycle enemy)
        {
            Grid?.Remove(enemy);
            Scheduler?.Unregister(enemy);
            if (alive.Remove(enemy)) AliveCountChanged?.Invoke(alive.Count);
        }

        private void FixedUpdate() { SynchronizeGrid(); }

        public void SynchronizeGrid()
        {
            if (Grid == null) return;
            using (GridMarker.Auto())
                foreach (var enemy in alive) Grid.Update(enemy, enemy.Position);
        }

        public EnemyLifecycle Spawn(EnemyLifecycle prefab, Vector2 position, Quaternion rotation, PlayerHealth target = null)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (!pools.TryGetValue(prefab, out var pool))
            {
                var root = new GameObject(prefab.name + "_Pool");
                root.transform.SetParent(transform, false);
                root.SetActive(false);
                pool = new Pool<EnemyLifecycle>(prefab, 0, root.transform);
                pools.Add(prefab, pool);
            }
            var enemy = pool.Get(position, rotation);
            enemy.BindPool(pool);
            enemy.GetComponent<MeleeEnemy>()?.SetTarget(target);
            enemy.GetComponent<RangedEnemy>()?.SetTarget(target);
            return enemy;
        }

        public int PooledActiveCount(EnemyLifecycle prefab) => pools.TryGetValue(prefab, out var pool) ? pool.ActiveCount : 0;
        public int PooledInactiveCount(EnemyLifecycle prefab) => pools.TryGetValue(prefab, out var pool) ? pool.InactiveCount : 0;

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            alive.Clear();
            Grid?.Clear();
            foreach (var pool in pools.Values) pool.Clear();
            pools.Clear();
        }
    }
}
