using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Enemy;
using ShootDemo.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShootDemo.Tests.EditMode
{
    public sealed class EnemyAISchedulerTests
    {
        private Scene scene;
        private GameConfig config;
        private EnemyRegistry registry;
        private EnemyAIScheduler scheduler;
        private PlayerHealth player;
        private readonly List<EnemyLifecycle> actors = new List<EnemyLifecycle>();
        private static readonly FieldInfo DueField = typeof(MeleeEnemy).GetField("decisionDue", BindingFlags.Instance | BindingFlags.NonPublic);
        private float savedTime;

        [SetUp]
        public void SetUp()
        {
            savedTime = Time.timeScale;
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.playerHealth.hurtInvulnerability = 0f;
            var service = Create("Registry", Vector2.zero);
            registry = service.AddComponent<EnemyRegistry>();
            scheduler = service.AddComponent<EnemyAIScheduler>();
            SetConfig(registry);
            SetConfig(scheduler);
            Invoke(registry, "Awake");
            Invoke(scheduler, "Awake");
            Invoke(scheduler, "OnEnable");
            player = Create("Player", Vector2.zero).AddComponent<PlayerHealth>();
            SetConfig(player);
            Invoke(player, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var actor in actors)
            {
                Invoke(actor, "OnDisable");
                Invoke(actor.Brain, "OnDisable");
            }
            Invoke(scheduler, "OnDisable");
            Invoke(registry, "OnDestroy");
            foreach (var root in scene.GetRootGameObjects())
            {
                var state = root.GetComponent<GameStateMachine>();
                if (state != null) Invoke(state, "OnDestroy");
            }
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(config);
            actors.Clear();
            Time.timeScale = savedTime;
        }

        private GameObject Create(string name, Vector2 position)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            return go;
        }

        private void SetConfig(Component component)
        {
            var data = new SerializedObject(component);
            data.FindProperty("config").objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static object Invoke(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        private EnemyLifecycle Actor(float distance, bool ranged = false, bool crowd = false)
        {
            var go = Create("ScheduledActor", Vector2.left * distance);
            go.layer = LayerMask.NameToLayer("Enemy");
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            go.AddComponent<CapsuleCollider2D>().size = Vector2.one;
            var health = go.AddComponent<EnemyHealth>();
            Invoke(health, "Awake");
            Component brain = ranged ? (Component)go.AddComponent<RangedEnemy>() : go.AddComponent<MeleeEnemy>();
            SetConfig(brain);
            Invoke(brain, "Awake");
            Invoke(brain, "OnEnable");
            if (ranged) ((RangedEnemy)brain).SetTarget(player);
            else ((MeleeEnemy)brain).SetTarget(player);
            var actor = go.AddComponent<EnemyLifecycle>();
            SetConfig(actor);
            if (crowd)
            {
                var movement = go.AddComponent<EnemyCrowdMovement>();
                SetConfig(movement);
                Invoke(movement, "Awake");
                Invoke(movement, "OnEnable");
            }
            Invoke(actor, "Awake");
            Invoke(actor, "OnEnable");
            actors.Add(actor);
            return actor;
        }

        private void StepSchedule() { Invoke(scheduler, "Tick", 0.02f); }
        private static bool Due(EnemyLifecycle actor) => (bool)DueField.GetValue(actor.Brain);

        [Test]
        public void ThousandNearEnemiesUseTheHighPopulationDecisionRate()
        {
            for (int i = 0; i < 1000; i++) Actor(10f);
            long before = scheduler.TotalDecisions;
            for (int step = 0; step < 50; step++)
            {
                StepSchedule();
                Assert.LessOrEqual(scheduler.LastDecisionCount, 256);
                Assert.That(scheduler.LastDecisionCount, Is.InRange(30, 50));
            }
            Assert.That(scheduler.TotalDecisions - before, Is.InRange(1900, 2100));
            Assert.AreEqual(1000, scheduler.RegisteredCount);
        }

        [Test]
        public void FarEnemiesThinkAtOneThirdOfNearRate()
        {
            var near = Actor(10f);
            var far = Actor(30f);
            int nearCount = 0, farCount = 0;
            for (int step = 0; step < 150; step++)
            {
                StepSchedule();
                if (Due(near)) nearCount++;
                if (Due(far)) farCount++;
            }
            Assert.That(nearCount, Is.InRange(29, 31));
            Assert.That(farCount, Is.InRange(9, 11));
            Assert.AreEqual(1, scheduler.LastFarCount);
        }

        [Test]
        public void DecisionBudgetDoesNotStarveEnemies()
        {
            config.performance.maxDecisionsPerFixedStep = 13;
            for (int i = 0; i < 150; i++) Actor(10f);
            var seen = new HashSet<EnemyLifecycle>();
            for (int step = 0; step < 100; step++)
            {
                StepSchedule();
                Assert.LessOrEqual(scheduler.LastDecisionCount, 13);
                foreach (var actor in actors) if (Due(actor)) seen.Add(actor);
            }
            Assert.AreEqual(150, seen.Count);
        }

        [Test]
        public void PathBudgetDefersRequestsAndEventuallyServesEveryEnemy()
        {
            for (int i = 0; i < 150; i++) Actor(i < 75 ? 10f : 30f);
            var served = new HashSet<EnemyLifecycle>();
            for (int step = 0; step < 300 && served.Count < actors.Count; step++)
            {
                StepSchedule();
                foreach (var actor in actors)
                    if (Due(actor) && !served.Contains(actor)
                        && (bool)Invoke(scheduler, "TryReservePathSearch", actor.Brain)) served.Add(actor);
                Assert.LessOrEqual(scheduler.LastPathSearchCount, config.performance.maxPathSearchesPerFixedStep);
            }
            Assert.AreEqual(150, served.Count);
        }

        [Test]
        public void LodHysteresisAvoidsBoundaryFlappingAndPromotesNearImmediately()
        {
            var actor = Actor(16f);
            StepSchedule();
            Assert.AreEqual(1, scheduler.LastFarCount);
            actor.GetComponent<Rigidbody2D>().position = Vector2.left * 14f;
            StepSchedule();
            Assert.AreEqual(1, scheduler.LastFarCount);
            actor.GetComponent<Rigidbody2D>().position = Vector2.left * 12f;
            StepSchedule();
            Assert.AreEqual(0, scheduler.LastFarCount);
            Assert.IsTrue(Due(actor));
            actor.GetComponent<Rigidbody2D>().position = Vector2.left * 16f;
            StepSchedule();
            Assert.AreEqual(0, scheduler.LastFarCount);
        }

        [Test]
        public void PauseDoesNotAccumulateCatchUpAndDisabledSchedulerFallsBack()
        {
            var actor = Actor(10f);
            var state = Create("State", Vector2.zero).AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            StepSchedule();
            long before = scheduler.TotalDecisions;
            state.TryChangeState(GameState.Paused);
            for (int i = 0; i < 100; i++) StepSchedule();
            Assert.AreEqual(before, scheduler.TotalDecisions);
            state.TryChangeState(GameState.Playing);
            StepSchedule();
            Assert.AreEqual(0, scheduler.LastDecisionCount);
            Invoke(scheduler, "OnDisable");
            Invoke(actor.Brain, "Tick", 0.02f);
            Assert.Greater(actor.GetComponent<Rigidbody2D>().velocity.x, 1f);
        }

        [Test]
        public void DeathAndReuseNeverDuplicateOrRetainSchedulerEntries()
        {
            var actor = Actor(10f);
            StepSchedule();
            actor.Health.TryTakeDamage(1000f);
            Assert.AreEqual(0, scheduler.RegisteredCount);
            Invoke(actor, "OnDisable");
            Invoke(actor.Brain, "OnDisable");
            actor.Health.ResetForSpawn();
            Invoke(actor.Brain, "OnEnable");
            Invoke(actor, "OnEnable");
            Invoke(scheduler, "Register", actor);
            Assert.AreEqual(1, scheduler.RegisteredCount);
            long before = scheduler.TotalDecisions;
            for (int i = 0; i < 5; i++) StepSchedule();
            Assert.AreEqual(1, scheduler.TotalDecisions - before);
        }

        [Test]
        public void MovementContinuesEveryStepAndDoesNotAccumulateCrowdVelocity()
        {
            var actor = Actor(10f);
            StepSchedule();
            Invoke(actor.Brain, "Tick", 0.02f);
            var body = actor.GetComponent<Rigidbody2D>();
            Vector2 expected = body.velocity;
            for (int i = 0; i < 3; i++)
            {
                body.velocity = Vector2.up * 99f;
                StepSchedule();
                Assert.IsFalse(Due(actor));
                Invoke(actor.Brain, "Tick", 0.02f);
                Assert.AreEqual(expected, body.velocity);
            }
        }

        [Test]
        public void ManagedMeleeWindupStillResolvesOnTheCorrectPhysicsStep()
        {
            var actor = Actor(1.4f);
            StepSchedule();
            Invoke(actor.Brain, "Tick", 0.02f);
            Assert.IsTrue(actor.Brain.IsAttacking);
            int frames = Mathf.CeilToInt(config.enemy.meleeWindup / 0.02f);
            for (int i = 0; i < frames - 1; i++)
            {
                StepSchedule();
                Invoke(actor.Brain, "Tick", 0.02f);
                Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
            }
            StepSchedule();
            Invoke(actor.Brain, "Tick", 0.02f);
            Assert.Less(player.CurrentHealth, player.MaxHealth);
        }

        [Test]
        public void ManagedRangedRecoveryCompletesWithoutWaitingForAnotherDecision()
        {
            var actor = Actor(7f, true);
            StepSchedule();
            Invoke(actor.Brain, "Tick", 0.02f);
            Assert.AreEqual(RangedEnemyState.Windup, ((RangedEnemy)actor.Brain).State);
            for (int i = 0; i < 50; i++)
            {
                StepSchedule();
                Invoke(actor.Brain, "Tick", 0.02f);
            }
            Assert.IsFalse(actor.Brain.IsAttacking);
        }

        [Test]
        public void ManagedCrowdSkipsNeighborQueriesButStillStopsAtThinWall()
        {
            var actor = Actor(10f, false, true);
            Actor(10.3f);
            var wall = Create("ThinWall", new Vector2(-9f, 0f));
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.025f, 5f);
            registry.SynchronizeGrid();
            StepSchedule();
            var body = actor.GetComponent<Rigidbody2D>();
            body.velocity = Vector2.right * 3f;
            Invoke(actor.Crowd, "Tick", 0.02f);
            Assert.Greater(actor.Crowd.LastCandidateChecks, 0);
            StepSchedule();
            body.velocity = Vector2.right * 100f;
            Invoke(actor.Crowd, "Tick", 0.02f);
            Assert.AreEqual(0, actor.Crowd.LastCandidateChecks);
            scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.Less(actor.GetComponent<Collider2D>().bounds.max.x, -9.01f);
        }
    }
}
