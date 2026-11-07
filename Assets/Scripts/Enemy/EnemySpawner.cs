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
        private readonly List<SpriteRenderer> obstacleVisuals = new List<SpriteRenderer>();
        private readonly Dictionary<EnemyLifecycle, EnemyPathfinder2D> spawnNavigation = new Dictionary<EnemyLifecycle, EnemyPathfinder2D>();
        private IEnumerator<int> pendingPlacement;
        private EnemyLifecycle pendingPrefab;
        private Vector2? placementResult;
        private Vector2 placementTarget;
        private readonly System.Diagnostics.Stopwatch placementTimer = new System.Diagnostics.Stopwatch();
        public double LastPlacementSliceMilliseconds { get; private set; }
        public int LastPlacementSteps { get; private set; }
        private bool visualsCached;
        private GameStateMachine state;
        private float remaining;
        private float acquireRemaining;
        public float ElapsedSeconds { get; private set; }
        public int TotalSpawned { get; private set; }
        public int FailedAttempts { get; private set; }
        // Zero keeps the spawner unlimited for stress tests; match rules set a cumulative wave budget.
        public int SpawnLimit { get; set; }
        public bool StressControlled { get; set; }
        public Rect ArenaBounds => arena;
        public float StressRadius
        {
            get
            {
                float radius = 0.5f;
                if (enemies != null) foreach (var entry in enemies)
                {
                    if (entry?.prefab == null) continue;
                    var capsule = entry.prefab.GetComponent<CapsuleCollider2D>();
                    if (capsule == null) continue;
                    Vector3 scale = entry.prefab.transform.lossyScale;
                    radius = Mathf.Max(radius, Mathf.Max(capsule.size.x * Mathf.Abs(scale.x), capsule.size.y * Mathf.Abs(scale.y)) * 0.5f
                        + capsule.offset.magnitude * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
                }
                return radius + (config != null ? config.spawn.clearance : 0.15f);
            }
        }

        public bool TryStressSpawn(Vector2 position)
        {
            if (!StressControlled || !isActiveAndEnabled || config == null || registry == null || player == null
                || player.IsDead || GameStateMachine.Instance?.CurrentState != GameState.Playing) return false;
            CacheObstacleVisuals();
            if (!PositionClear(position, StressRadius)) return false;
            var prefab = SelectPrefab();
            if (prefab == null) return false;
            Vector2 delta = (Vector2)player.transform.position - position;
            var actor = registry.Spawn(prefab, position, Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), player);
            owned.Add(actor);
            TotalSpawned++;
            Spawned?.Invoke(actor);
            return true;
        }
        public bool HasFinishedSpawning => SpawnLimit > 0 && TotalSpawned >= SpawnLimit;
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
            if (StressControlled || HasFinishedSpawning) { CancelPlacement(); return; }
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
            if (registry.AliveCount >= config.spawn.maxAlive) { CancelPlacement(); remaining = CurrentInterval; return; }
            remaining -= deltaTime;
            if (remaining > 0f) return;
            if (pendingPlacement != null && Vector2.Distance(player.transform.position, placementTarget) > 0.75f)
                CancelPlacement();
            if (pendingPlacement == null)
            {
                pendingPrefab = SelectPrefab();
                if (pendingPrefab == null) { FailedAttempts++; remaining = Mathf.Max(0.25f, CurrentInterval); return; }
                placementTarget = player.transform.position;
                pendingPlacement = FindSpawnPositionIncremental(pendingPrefab).GetEnumerator();
            }
            LastPlacementSteps = 0;
            placementTimer.Restart();
            bool more = true;
            // A time budget handles expensive physics queries; the node cap bounds fast machines too.
            while (LastPlacementSteps < 64 && placementTimer.Elapsed.TotalMilliseconds < 1.0)
            {
                LastPlacementSteps++;
                if (!(more = pendingPlacement.MoveNext())) break;
            }
            placementTimer.Stop();
            LastPlacementSliceMilliseconds = placementTimer.Elapsed.TotalMilliseconds;
            if (more) return;
            var selected = pendingPrefab;
            var result = placementResult;
            CancelPlacement();
            // Other actors may have moved into a candidate while its search was suspended.
            if (result.HasValue && PositionClear(result.Value, SpawnClearance(selected)))
            {
                SpawnAt(selected, result.Value);
                remaining = CurrentInterval;
            }
            else { FailedAttempts++; remaining = Mathf.Max(0.25f, CurrentInterval); }
        }

        public bool TrySpawn(out EnemyLifecycle spawned)
        {
            spawned = null;
            var game = GameStateMachine.Instance;
            if (HasFinishedSpawning || !isActiveAndEnabled || config == null || registry == null || player == null || player.IsDead
                || !player.isActiveAndEnabled || registry.AliveCount >= config.spawn.maxAlive
                || (game != null && game.CurrentState != GameState.Playing)) return false;
            var prefab = SelectPrefab();
            if (prefab == null || !TryFindSpawnPosition(prefab, out Vector2 position)) return false;
            spawned = SpawnAt(prefab, position);
            return true;
        }

        private EnemyLifecycle SpawnAt(EnemyLifecycle prefab, Vector2 position)
        {
            Vector2 direction = (Vector2)player.transform.position - position;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            var actor = registry.Spawn(prefab, position, Quaternion.Euler(0f, 0f, angle), player);
            owned.Add(actor);
            TotalSpawned++;
            Spawned?.Invoke(actor);
            return actor;
        }

        private void CancelPlacement()
        {
            pendingPlacement?.Dispose();
            pendingPlacement = null;
            pendingPrefab = null;
            placementResult = null;
        }

        private void OnDisable() { CancelPlacement(); }

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
            CancelPlacement();
            using (var search = FindSpawnPositionIncremental(prefab).GetEnumerator())
                while (search.MoveNext()) { }
            position = placementResult.GetValueOrDefault();
            return placementResult.HasValue;
        }

        private float SpawnClearance(EnemyLifecycle prefab)
        {
            var collider = prefab.GetComponent<CapsuleCollider2D>();
            Vector3 scale = prefab.transform.lossyScale;
            return Mathf.Max(collider.size.x * Mathf.Abs(scale.x), collider.size.y * Mathf.Abs(scale.y)) * 0.5f
                + collider.offset.magnitude * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) + config.spawn.clearance;
        }

        private IEnumerable<int> FindSpawnPositionIncremental(EnemyLifecycle prefab)
        {
            placementResult = null;
            if (config == null || player == null || prefab == null || prefab.GetComponent<CapsuleCollider2D>() == null) yield break;
            Physics2D.SyncTransforms();
            CacheObstacleVisuals();
            float radius = SpawnClearance(prefab);
            EnemyPathfinder2D navigation;
            if (!spawnNavigation.TryGetValue(prefab, out navigation))
            {
                // Spawn padding keeps art clear; movement uses the same clearance as the live actor.
                var melee = prefab.GetComponent<MeleeEnemy>();
                var ranged = prefab.GetComponent<RangedEnemy>();
                float clearance = melee != null ? melee.NavigationClearance : ranged != null ? ranged.NavigationClearance
                    : EnemyPathfinder2D.BodyClearance(prefab.gameObject, 0.05f);
                navigation = new EnemyPathfinder2D(gameObject.scene, clearance, ~0);
                spawnNavigation.Add(prefab, navigation);
            }
            Vector2 center = player.transform.position;
            Vector2 outside, entrance;
            bool passage = EnemyNarrowPassage.TryGetEntranceRoute(gameObject.scene, center, out outside, out entrance);
            int pointStart = spawnPoints != null && spawnPoints.Length > 0 ? UnityEngine.Random.Range(0, spawnPoints.Length) : 0;
            int attempts = Mathf.Clamp(config.spawn.positionAttempts, 1, 64);
            for (int i = 0; i < attempts; i++)
            {
                yield return i;
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
                Vector2 from = candidate;
                bool reachable = true;
                int legs = passage ? 3 : 1;
                for (int leg = 0; leg < legs; leg++)
                {
                    Vector2 target = passage ? (leg == 0 ? outside : leg == 1 ? entrance : center) : center;
                    float stop = leg == legs - 1 ? Mathf.Max(1f, config.enemy.meleeRange * 0.85f) : 0.2f;
                    foreach (int node in navigation.FindPathIncremental(from, target, stop, route)) yield return node;
                    if (route.Count == 0) { reachable = false; break; }
                    from = target;
                }
                if (!reachable) continue;
                placementResult = candidate;
                yield break;
            }
        }

        private bool PositionClear(Vector2 position, float radius)
        {
            if (EnemyNarrowPassage.BlocksSpawn(gameObject.scene, position)) return false;
            if (position.x - radius < arena.xMin || position.x + radius > arena.xMax
                || position.y - radius < arena.yMin || position.y + radius > arena.yMax
                || Vector2.Distance(position, player.transform.position) < config.spawn.minPlayerDistance + radius) return false;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(~0);
            int count = gameObject.scene.GetPhysicsScene2D().OverlapCircle(position, radius, filter, overlaps);
            for (int i = 0; i < count; i++) if (overlaps[i] != null && !overlaps[i].isTrigger) return false;
            // Thin collision footprints do not cover the full wall/rock art. Avoid spawning
            // behind that art as well, without widening colliders and closing narrow passages.
            for (int i = 0; i < obstacleVisuals.Count; i++)
            {
                var visual = obstacleVisuals[i];
                if (visual == null || !visual.enabled || !visual.gameObject.activeInHierarchy) continue;
                Bounds bounds = visual.bounds;
                float x = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
                float y = Mathf.Clamp(position.y, bounds.min.y, bounds.max.y);
                if ((position - new Vector2(x, y)).sqrMagnitude <= radius * radius) return false;
            }
            return true;
        }

        private void CacheObstacleVisuals()
        {
            if (visualsCached) return;
            visualsCached = true;
            var seen = new HashSet<SpriteRenderer>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var solid in root.GetComponentsInChildren<Collider2D>(true))
            {
                if (solid.isTrigger || (solid.attachedRigidbody != null
                    && solid.attachedRigidbody.bodyType == RigidbodyType2D.Dynamic)) continue;
                var visual = solid.GetComponent<SpriteRenderer>();
                if (visual != null && seen.Add(visual)) obstacleVisuals.Add(visual);
            }
        }

        public void ResetSession(bool despawnOwned = true)
        {
            CancelPlacement();
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
