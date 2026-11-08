using System.Collections.Generic;
using ShootDemo.Config;
using ShootDemo.Core;
using Unity.Profiling;
using UnityEngine;

namespace ShootDemo.Enemy
{
    /// <summary>Staggers expensive decisions; brains and collision safety still tick every physics step.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyRegistry))]
    [DefaultExecutionOrder(-40)]
    public sealed class EnemyAIScheduler : MonoBehaviour
    {
        private sealed class Entry
        {
            public EnemyLifecycle Actor;
            public IEnemyBrain Brain;
            public Behaviour Behaviour;
            public EnemyCrowdMovement Crowd;
            public float NextDecision;
            public bool Far;
            public bool Initialized;
            public int Index;
            public int Sequence;
            public bool WaitingForPath;
            public bool PathPermit;
        }

        [SerializeField] private GameConfig config;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Enemies.AI.Schedule");
        private readonly List<Entry> entries = new List<Entry>(1024);
        private readonly Dictionary<EnemyLifecycle, Entry> lookup = new Dictionary<EnemyLifecycle, Entry>();
        private readonly Dictionary<IEnemyBrain, Entry> brains = new Dictionary<IEnemyBrain, Entry>();
        private EnemyRegistry registry;
        private float clock;
        private int sequence;
        private int cursor;
        private int allocatedPathSearches;
        private int pathFrame = -1;
        private int framePathSearches;
        private int PathBudget => Application.isPlaying
            ? Mathf.Min(config.performance.maxPathSearchesPerFixedStep,
                Mathf.Max(0, config.performance.maxPathSearchesPerFrame - framePathSearches))
            : config.performance.maxPathSearchesPerFixedStep;
        public int RegisteredCount => entries.Count;
        public int LastDecisionCount { get; private set; }
        public int LastFarCount { get; private set; }
        public int LastPathSearchCount { get; private set; }
        public int FramePathSearchCount => framePathSearches;
        public long TotalDecisions { get; private set; }

        private void Awake() { registry = GetComponent<EnemyRegistry>(); }

        private void OnEnable()
        {
            if (registry == null) registry = GetComponent<EnemyRegistry>();
            foreach (var actor in registry.Alive) Register(actor);
        }

        internal void Register(EnemyLifecycle actor)
        {
            if (actor == null || actor.Brain == null || lookup.ContainsKey(actor)) return;
            var entry = new Entry
            {
                Actor = actor, Brain = actor.Brain, Behaviour = actor.Brain as Behaviour,
                Crowd = actor.Crowd, Index = entries.Count, Sequence = sequence++
            };
            lookup.Add(actor, entry);
            brains.Add(entry.Brain, entry);
            entries.Add(entry);
        }

        internal void Unregister(EnemyLifecycle actor)
        {
            if (!lookup.TryGetValue(actor, out var entry)) return;
            entry.Brain.SetDecisionSchedule(false, true);
            if (entry.Crowd != null) entry.Crowd.SetDecisionSchedule(false, true);
            int last = entries.Count - 1;
            var moved = entries[last];
            entries[entry.Index] = moved;
            moved.Index = entry.Index;
            entries.RemoveAt(last);
            lookup.Remove(actor);
            brains.Remove(entry.Brain);
        }

        private void FixedUpdate() { Tick(Time.fixedDeltaTime); }

        internal void Tick(float deltaTime)
        {
            LastDecisionCount = LastFarCount = LastPathSearchCount = 0;
            allocatedPathSearches = 0;
            if (pathFrame != Time.frameCount) { pathFrame = Time.frameCount; framePathSearches = 0; }
            if (deltaTime <= 0f || config == null || entries.Count == 0) return;
            var state = GameStateMachine.Instance;
            if (state != null && state.CurrentState != GameState.Playing) return;
            clock += deltaTime;
            var settings = config.performance;
            bool managed = settings.aiSchedulingEnabled;
            int count = entries.Count;
            float baseInterval = count >= settings.highPopulationThreshold
                ? settings.highPopulationAiTickInterval : settings.aiTickInterval;
            using (Marker.Auto())
            {
                for (int offset = 0; offset < count; offset++)
                {
                    var entry = entries[(cursor + offset) % count];
                    entry.PathPermit = false;
                    bool active = entry.Behaviour != null && entry.Behaviour.isActiveAndEnabled;
                    var target = entry.Brain.Target;
                    float distanceSquared = target != null
                        ? ((Vector2)target.transform.position - entry.Actor.Position).sqrMagnitude : float.PositiveInfinity;
                    bool previousFar = entry.Far;
                    float threshold = settings.farDistanceThreshold;
                    float boundary = entry.Initialized
                        ? Mathf.Max(0f, threshold + (entry.Far ? -settings.lodHysteresis : settings.lodHysteresis)) : threshold;
                    entry.Far = distanceSquared > boundary * boundary;
                    float interval = Mathf.Max(deltaTime, baseInterval * (entry.Far ? settings.farTickMultiplier : 1f));
                    if (!entry.Initialized)
                    {
                        int phases = Mathf.Max(1, Mathf.CeilToInt(interval / deltaTime));
                        entry.NextDecision = clock + (entry.Sequence % phases) * deltaTime;
                        entry.Initialized = true;
                    }
                    // A far enemy entering the near band should react without waiting out its old interval.
                    else if (previousFar && !entry.Far) entry.NextDecision = Mathf.Min(entry.NextDecision, clock);
                    if (entry.Far) LastFarCount++;
                    bool due = !managed || (clock + 0.00001f >= entry.NextDecision
                        && LastDecisionCount < settings.maxDecisionsPerFixedStep);
                    bool decision = active && !entry.Brain.IsAttacking && due;
                    if (decision)
                    {
                        entry.NextDecision = clock + interval; // No accumulated catch-up work after a stall.
                        LastDecisionCount++;
                        TotalDecisions++;
                        // Previously deferred requests take priority over fresh requests in brain FixedUpdate.
                        if (entry.WaitingForPath && allocatedPathSearches < PathBudget)
                        {
                            entry.PathPermit = true;
                            entry.WaitingForPath = false;
                            allocatedPathSearches++;
                        }
                    }
                    entry.Brain.SetDecisionSchedule(managed, decision);
                    // Crowd decisions share the phase; attack poses remain stationary in the crowd component.
                    if (entry.Crowd != null) entry.Crowd.SetDecisionSchedule(managed, decision);
                }
            }
            cursor = (cursor + Mathf.Max(1, LastDecisionCount) + 1) % count;
        }

        internal bool TryReservePathSearch(IEnemyBrain brain)
        {
            if (!isActiveAndEnabled || config == null || !config.performance.aiSchedulingEnabled) return true;
            if (!brains.TryGetValue(brain, out var entry)) return true;
            if (entry.PathPermit) entry.PathPermit = false;
            else if (allocatedPathSearches < PathBudget) allocatedPathSearches++;
            else { entry.WaitingForPath = true; return false; }
            framePathSearches++;
            entry.WaitingForPath = false;
            LastPathSearchCount++;
            return true;
        }

        private void OnDisable()
        {
            foreach (var entry in entries)
            {
                if (entry.Behaviour != null) entry.Brain.SetDecisionSchedule(false, true);
                if (entry.Crowd != null) entry.Crowd.SetDecisionSchedule(false, true);
                entry.Initialized = false;
                entry.PathPermit = entry.WaitingForPath = false;
            }
        }
    }
}
