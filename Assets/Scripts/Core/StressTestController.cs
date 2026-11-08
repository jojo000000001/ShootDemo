using System;
using System.Collections.Generic;
using ShootDemo.Enemy;
using ShootDemo.Player;
using UnityEngine;

namespace ShootDemo.Core
{
    /// <summary>Maintains real combat actors at the selected tier, with bounded work per frame.</summary>
    public sealed class StressTestController : MonoBehaviour
    {
        [SerializeField] private int spawnBatch = 16;
        [SerializeField] private int placementAttemptsPerFrame = 160;
        private GameStateMachine state;
        private MatchSession session;
        private EnemyRegistry registry;
        private EnemySpawner spawner;
        private PlayerHealth player;
        private readonly List<Vector2> positions = new List<Vector2>();
        private readonly List<EnemyLifecycle> remove = new List<EnemyLifecycle>();
        private int cursor;
        public int TargetCount { get; private set; }
        public bool IsActive => TargetCount > 0;
        public int AliveCount => registry != null ? registry.AliveCount : 0;
        public event Action Changed;

        private void Start()
        {
            state = GameStateMachine.Instance;
            session = GetComponent<MatchSession>();
            registry = EnemyRegistry.Instance;
            spawner = FindObjectOfType<EnemySpawner>();
            player = FindObjectOfType<PlayerHealth>();
            if (registry != null) registry.AliveCountChanged += OnCount;
            if (state != null) state.StateChanged += OnState;
        }

        public static bool IsValidTier(int count) => count == 100 || count == 500 || count == 1000;

        public void SelectTier(int count)
        {
            if (!IsValidTier(count) || state == null || session == null || registry == null || spawner == null || player == null
                || (state.CurrentState != GameState.Menu && state.CurrentState != GameState.Playing && state.CurrentState != GameState.Paused)) return;
            bool entering = !IsActive;
            player.SetStressTestInvulnerable(true);
            session.SetStressMode(true);
            spawner.StressControlled = true;
            TargetCount = count;
            if (entering || positions.Count == 0) BuildReachablePositions();
            if (state.CurrentState == GameState.Menu) state.TryChangeState(GameState.Playing);
            Changed?.Invoke();
        }

        private void BuildReachablePositions()
        {
            positions.Clear();
            cursor = 0;
            Physics2D.SyncTransforms();
            float step = Mathf.Max(1.5f, spawner.StressRadius * 2f + 0.05f);
            Rect arena = spawner.ArenaBounds;
            Vector2 origin = player.transform.position;
            var navigation = new EnemyPathfinder2D(gameObject.scene, spawner.StressRadius, ~0);
            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue(Vector2Int.zero);
            visited.Add(Vector2Int.zero);
            Vector2Int[] offsets = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (queue.Count > 0 && visited.Count < 12000)
            {
                Vector2Int cell = queue.Dequeue();
                Vector2 point = origin + (Vector2)cell * step;
                positions.Add(point);
                foreach (var offset in offsets)
                {
                    Vector2Int next = cell + offset;
                    if (visited.Contains(next)) continue;
                    Vector2 candidate = origin + (Vector2)next * step;
                    if (!arena.Contains(candidate) || !navigation.SegmentClear(point, candidate)) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
            // Scatter the initial population instead of filling only the closest rows.
            for (int i = positions.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                Vector2 temp = positions[i]; positions[i] = positions[j]; positions[j] = temp;
            }
        }

        private void Update()
        {
            if (!IsActive || state == null || state.CurrentState != GameState.Playing || player == null || player.IsDead) return;
            if (registry.AliveCount > TargetCount)
            {
                remove.Clear();
                foreach (var actor in registry.Alive)
                {
                    remove.Add(actor);
                    if (remove.Count >= Mathf.Min(64, registry.AliveCount - TargetCount)) break;
                }
                foreach (var actor in remove) if (actor != null) actor.Despawn();
                return;
            }
            if (registry.AliveCount >= TargetCount || positions.Count == 0) return;
            Physics2D.SyncTransforms();
            int added = 0;
            for (int attempt = 0; attempt < placementAttemptsPerFrame && added < spawnBatch && registry.AliveCount < TargetCount; attempt++)
            {
                Vector2 point = positions[cursor++ % positions.Count];
                if (spawner.TryStressSpawn(point)) { added++; Physics2D.SyncTransforms(); }
            }
        }

        private void OnCount(int count) { Changed?.Invoke(); }
        private void OnState(GameState previous, GameState next)
        {
            if (next != GameState.Menu) return;
            TargetCount = 0;
            if (player != null) player.SetStressTestInvulnerable(false);
            if (session != null) session.SetStressMode(false);
            if (spawner != null) spawner.StressControlled = false;
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (registry != null) registry.AliveCountChanged -= OnCount;
            if (state != null) state.StateChanged -= OnState;
            if (player != null) player.SetStressTestInvulnerable(false);
        }
    }
}
