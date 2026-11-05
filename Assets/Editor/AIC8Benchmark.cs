using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Enemy;
using ShootDemo.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShootDemo.Editor
{
    /// <summary>Repeatable main-thread CPU comparison without model rendering or animation costs.</summary>
    public static class AIC8Benchmark
    {
        [Serializable]
        public sealed class Sample
        {
            public bool scheduled;
            public float scriptsMs;
            public float scriptsAndPhysicsMs;
            public float p95ScriptsAndPhysicsMs;
            public float decisionsPerStep;
            public long allocatedBytes;
        }

        [Serializable]
        public sealed class Report
        {
            public string measurement = "Editor main-thread CPU, 80% melee / 20% ranged, real 2D bodies and crowd, fixed positions, no models/rendering/animation; excludes pose reset overhead";
            public int enemies;
            public int samplesPerRound;
            public int farEnemies;
            public Sample[] rounds;
        }

        private sealed class Actor
        {
            public EnemyLifecycle Life;
            public Rigidbody2D Body;
            public EnemyCrowdMovement Crowd;
            public Vector2 Position;
            public Action<float> BrainTick;
            public Action<float> CrowdTick;
        }

        [MenuItem("Shoot Demo/Benchmark C8 AI (1000)")]
        public static void RunMenu() { UnityEngine.Debug.Log(JsonUtility.ToJson(Run(), true)); }

        public static Report Run(int count = 1000, int samples = 100)
        {
            if (Application.isPlaying || EnemyRegistry.Instance != null || GameStateMachine.Instance != null)
                throw new InvalidOperationException("Run the benchmark in Edit Mode with no active runtime singletons.");
            var scene = EditorSceneManager.NewPreviewScene();
            var config = ScriptableObject.CreateInstance<GameConfig>();
            var actors = new List<Actor>(count);
            EnemyRegistry registry = null;
            EnemyAIScheduler scheduler = null;
            try
            {
                var service = Create(scene, "BenchmarkRegistry", Vector2.zero);
                registry = service.AddComponent<EnemyRegistry>();
                scheduler = service.AddComponent<EnemyAIScheduler>();
                SetConfig(registry, config);
                SetConfig(scheduler, config);
                Call(registry, "Awake");
                Call(scheduler, "Awake");
                Call(scheduler, "OnEnable");
                var target = Create(scene, "BenchmarkPlayer", Vector2.zero).AddComponent<PlayerHealth>();
                SetConfig(target, config);
                Call(target, "Awake");
                for (int i = 0; i < count; i++)
                {
                    Vector2 position = new Vector2(-40f + (i % 40) * 0.85f, (i / 40 - 12) * 0.85f);
                    var go = Create(scene, "BenchmarkEnemy", position);
                    go.layer = LayerMask.NameToLayer("Enemy");
                    var body = go.AddComponent<Rigidbody2D>();
                    body.gravityScale = 0f;
                    body.freezeRotation = true;
                    body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                    go.AddComponent<CapsuleCollider2D>().size = Vector2.one;
                    var health = go.AddComponent<EnemyHealth>();
                    Call(health, "Awake");
                    Component brain = i % 5 == 0 ? (Component)go.AddComponent<RangedEnemy>() : go.AddComponent<MeleeEnemy>();
                    SetConfig(brain, config);
                    Call(brain, "Awake");
                    Call(brain, "OnEnable");
                    if (brain is RangedEnemy) ((RangedEnemy)brain).SetTarget(target);
                    else ((MeleeEnemy)brain).SetTarget(target);
                    var life = go.AddComponent<EnemyLifecycle>();
                    SetConfig(life, config);
                    var crowd = go.AddComponent<EnemyCrowdMovement>();
                    SetConfig(crowd, config);
                    Call(crowd, "Awake");
                    Call(crowd, "OnEnable");
                    Call(life, "Awake");
                    Call(life, "OnEnable");
                    actors.Add(new Actor { Life = life, Body = body, Crowd = crowd, Position = position,
                        BrainTick = TickDelegate(brain), CrowdTick = TickDelegate(crowd) });
                }
                var schedule = TickDelegate(scheduler);
                var physics = scene.GetPhysicsScene2D();
                var report = new Report { enemies = count, samplesPerRound = samples, rounds = new Sample[4] };
                for (int round = 0; round < 4; round++)
                {
                    // ABBA order reduces bias from JIT warmup, temperature and editor background work.
                    bool enabled = round == 1 || round == 2;
                    config.performance.aiSchedulingEnabled = enabled;
                    foreach (var actor in actors)
                    {
                        Call(actor.Life.Brain, "OnDisable");
                        Call(actor.Crowd, "OnDisable");
                        Call(actor.Life, "OnDisable");
                        actor.Body.position = actor.Position;
                        actor.Body.rotation = 0f;
                        actor.Life.Health.ResetForSpawn();
                        Call(actor.Life.Brain, "OnEnable");
                        Call(actor.Crowd, "OnEnable");
                        Call(actor.Life, "OnEnable");
                    }
                    double scripts = 0d, total = 0d;
                    var totals = new double[samples];
                    long decisions = 0, bytes = 0;
                    for (int step = -30; step < samples; step++)
                    {
                        // Identical poses keep broadphase density, distances and navigation workload comparable.
                        foreach (var actor in actors) { actor.Body.position = actor.Position; actor.Body.rotation = 0f; }
                        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                        long started = Stopwatch.GetTimestamp();
                        registry.SynchronizeGrid();
                        schedule(0.02f);
                        foreach (var actor in actors) actor.BrainTick(0.02f);
                        foreach (var actor in actors) actor.CrowdTick(0.02f);
                        long scriptEnd = Stopwatch.GetTimestamp();
                        physics.Simulate(0.02f);
                        long ended = Stopwatch.GetTimestamp();
                        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                        if (step < 0) continue;
                        double scriptMs = (scriptEnd - started) * 1000d / Stopwatch.Frequency;
                        double totalMs = (ended - started) * 1000d / Stopwatch.Frequency;
                        scripts += scriptMs;
                        total += totalMs;
                        totals[step] = totalMs;
                        decisions += scheduler.LastDecisionCount;
                        bytes += allocated;
                    }
                    Array.Sort(totals);
                    report.rounds[round] = new Sample { scheduled = enabled,
                        scriptsMs = (float)(scripts / samples), scriptsAndPhysicsMs = (float)(total / samples),
                        p95ScriptsAndPhysicsMs = (float)totals[Mathf.Min(samples - 1, Mathf.FloorToInt(samples * 0.95f))],
                        decisionsPerStep = (float)decisions / samples, allocatedBytes = bytes };
                    report.farEnemies = scheduler.LastFarCount;
                }
                Directory.CreateDirectory("Temp/C8Verification");
                File.WriteAllText("Temp/C8Verification/ai-benchmark-" + count + ".json", JsonUtility.ToJson(report, true));
                return report;
            }
            finally
            {
                foreach (var actor in actors)
                {
                    Call(actor.Life, "OnDisable");
                    Call(actor.Life.Brain, "OnDisable");
                    Call(actor.Crowd, "OnDisable");
                }
                if (scheduler != null) Call(scheduler, "OnDisable");
                if (registry != null) Call(registry, "OnDestroy");
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(config);
            }
        }

        private static GameObject Create(Scene scene, string name, Vector2 position)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            return go;
        }

        private static void SetConfig(Component component, GameConfig config)
        {
            var data = new SerializedObject(component);
            data.FindProperty("config").objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Call(object target, string method) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static Action<float> TickDelegate(object target) => (Action<float>)Delegate.CreateDelegate(
            typeof(Action<float>), target, target.GetType().GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
