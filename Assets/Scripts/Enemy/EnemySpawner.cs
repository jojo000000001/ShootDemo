using System;
using System.Collections.Generic;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootDemo.Enemy
{
    [Serializable]
    public sealed class EnemySpawnEntry
    {
        public EnemyLifecycle prefab;
        [Min(0f)] public float weight = 1f;
    }

    [DisallowMultipleComponent]
    public sealed class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private EnemyRegistry registry;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private EnemySpawnEntry[] enemies;
        [Tooltip("If assigned, only active authored points are used. An empty list uses the ring around the player.")]
        [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();
        [SerializeField] private Rect arena = new Rect(-39.5f, -32.9f, 79f, 65.8f);
        private readonly List<Collider2D> overlaps = new List<Collider2D>(32);
        private readonly List<Vector2> route = new List<Vector2>();
        private readonly HashSet<EnemyLifecycle> owned = new HashSet<EnemyLifecycle>();
        private GameStateMachine state;
        private float remaining;
        private float acquireRemaining;
        public float ElapsedSeconds { get; private set; }
        public int TotalSpawned { get; private set; }
        public int FailedAttempts { get; private set; }
        public event Action<EnemyLifecycle> Spawned;
        public float CurrentInterval => EvaluateInterval(config != null ? config.spawn : null, ElapsedSeconds);

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("EnemySpawner needs a GameConfig reference.", this);
                enabled = false;
                return;
            }
            remaining = config.spawn.initialDelay;
        }

        private void Start()
        {
            if (registry == null) registry = GetComponent<EnemyRegistry>();
            state = GameStateMachine.Instance;
            if (state != null) state.StateChanged += OnStateChanged;
        }

        public static float EvaluateInterval(SpawnSettings settings, float elapsed)
        {
            if (settings == null) return 1f;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(1f, settings.difficultyDuration));
            float multiplier = settings.waveCurve != null && settings.waveCurve.length > 0
                ? settings.waveCurve.Evaluate(progress) : 1f;
            return Mathf.Max(0.05f, settings.spawnInterval / Mathf.Max(0.05f, multiplier));
        }

        private void Update() { Tick(Time.deltaTime); }

        internal void Tick(float deltaTime)
        {
            if (config == null || deltaTime <= 0f) return;
            var game = GameStateMachine.Instance;
            if (game != null && game.CurrentState != GameState.Playing) return;
            if (registry == null || !registry.isActiveAndEnabled) return;
            if (player == null || !player.isActiveAndEnabled)
            {
                acquireRemaining -= deltaTime;
                if (acquireRemaining <= 0f)
                {
                    acquireRemaining = 1f;
                    foreach (var candidate in FindObjectsOfType<PlayerHealth>())
                        if (candidate.gameObject.scene == gameObject.scene && !candidate.IsDead) { player = candidate; break; }
                }
            }
            if (player == null || !player.isActiveAndEnabled || player.IsDead) return;
            ElapsedSeconds += deltaTime;
            // No accumulated burst after staying at the cap, pausing or failing to find room.
            if (registry.AliveCount >= config.spawn.maxAlive) { remaining = CurrentInterval; return; }
            remaining -= deltaTime;
            if (remaining > 0f) return;
            if (TrySpawn(out _)) remaining = CurrentInterval;
            else { FailedAttempts++; remaining = Mathf.Max(0.25f, CurrentInterval); }
        }

        public bool TrySpawn(out EnemyLifecycle spawned)
        {
            spawned = null;
            var game = GameStateMachine.Instance;
            if (!isActiveAndEnabled || config == null || registry == null || player == null || player.IsDead
                || !player.isActiveAndEnabled || registry.AliveCount >= config.spawn.maxAlive
                || (game != null && game.CurrentState != GameState.Playing)) return false;
            var prefab = SelectPrefab();
            if (prefab == null || !TryFindSpawnPosition(prefab, out Vector2 position)) return false;
            Vector2 direction = (Vector2)player.transform.position - position;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            spawned = registry.Spawn(prefab, position, Quaternion.Euler(0f, 0f, angle), player);
            owned.Add(spawned);
            TotalSpawned++;
            Spawned?.Invoke(spawned);
            return true;
        }

        private EnemyLifecycle SelectPrefab()
        {
            if (enemies == null) return null;
            float total = 0f;
            foreach (var entry in enemies) if (entry != null && entry.prefab != null) total += Mathf.Max(0f, entry.weight);
            if (total <= 0f) return null;
            float roll = UnityEngine.Random.value * total;
            EnemyLifecycle last = null;
            foreach (var entry in enemies)
            {
                if (entry == null || entry.prefab == null || entry.weight <= 0f) continue;
                last = entry.prefab;
                roll -= entry.weight;
                if (roll <= 0f) return last;
            }
            return last;
        }

        public bool TryFindSpawnPosition(EnemyLifecycle prefab, out Vector2 position)
        {
            position = default;
            if (config == null || player == null || prefab == null) return false;
            var collider = prefab.GetComponent<CapsuleCollider2D>();
            if (collider == null) return false;
            Vector3 scale = prefab.transform.lossyScale;
            float radius = (Mathf.Max(collider.size.x * Mathf.Abs(scale.x), collider.size.y * Mathf.Abs(scale.y)) * 0.5f
                + collider.offset.magnitude * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y))) + config.spawn.clearance;
            var navigation = new EnemyPathfinder2D(gameObject.scene, radius, ~0);
            Vector2 center = player.transform.position;
            int pointStart = spawnPoints != null && spawnPoints.Length > 0 ? UnityEngine.Random.Range(0, spawnPoints.Length) : 0;
            int attempts = Mathf.Clamp(config.spawn.positionAttempts, 1, 64);
            for (int i = 0; i < attempts; i++)
            {
                Vector2 candidate;
                if (spawnPoints != null && spawnPoints.Length > 0)
                {
                    var point = spawnPoints[(pointStart + i) % spawnPoints.Length];
                    if (point == null || !point.gameObject.activeInHierarchy) continue;
                    candidate = point.position;
                }
                else
                {
                    float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                    float distance = Mathf.Max(0f, config.spawn.spawnRadius
                        + UnityEngine.Random.Range(-config.spawn.radiusVariation, config.spawn.radiusVariation));
                    candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                }
                if (!PositionClear(candidate, radius)) continue;
                // Reject isolated pockets, even when a point itself is empty.
                if (!navigation.FindPath(candidate, center, Mathf.Max(1f, config.enemy.meleeRange * 0.85f), route)) continue;
                position = candidate;
                return true;
            }
            return false;
        }

        private bool PositionClear(Vector2 position, float radius)
        {
            if (position.x - radius < arena.xMin || position.x + radius > arena.xMax
                || position.y - radius < arena.yMin || position.y + radius > arena.yMax
                || Vector2.Distance(position, player.transform.position) < config.spawn.minPlayerDistance + radius) return false;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(~0);
            int count = gameObject.scene.GetPhysicsScene2D().OverlapCircle(position, radius, filter, overlaps);
            for (int i = 0; i < count; i++) if (overlaps[i] != null && !overlaps[i].isTrigger) return false;
            return true;
        }

        public void ResetSession(bool despawnOwned = true)
        {
            if (despawnOwned)
            {
                foreach (var enemy in owned) if (enemy != null) enemy.Despawn();
                owned.Clear();
            }
            ElapsedSeconds = 0f;
            TotalSpawned = FailedAttempts = 0;
            remaining = config != null ? config.spawn.initialDelay : 0f;
            acquireRemaining = 0f;
        }

        private void OnStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.Menu || (next == GameState.Playing && previous == GameState.GameOver)) ResetSession();
        }

        private void OnDestroy()
        {
            if (state != null) state.StateChanged -= OnStateChanged;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(arena.center, arena.size);
            if (spawnPoints != null) foreach (var point in spawnPoints)
                if (point != null) Gizmos.DrawWireSphere(point.position, 0.9f);
            if (player != null && config != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(player.transform.position, config.spawn.spawnRadius);
            }
        }
    }
}
