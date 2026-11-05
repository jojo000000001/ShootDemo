using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShootDemo.Tests.EditMode
{
    public sealed class EnemyCrowdTests
    {
        private Scene scene;
        private GameConfig config;
        private EnemyRegistry registry;
        private readonly List<EnemyLifecycle> actors = new List<EnemyLifecycle>();
        private float savedTime;

        [SetUp]
        public void SetUp()
        {
            savedTime = Time.timeScale;
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            registry = Create("Registry", Vector2.zero).AddComponent<EnemyRegistry>();
            Invoke(registry, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var actor in actors) if (actor != null) Invoke(actor, "OnDisable");
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

        private static object Invoke(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static).Invoke(target, args);

        private void SetConfig(Component component)
        {
            var data = new SerializedObject(component);
            data.FindProperty("config").objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private EnemyLifecycle Actor(Vector2 position)
        {
            var go = Create("CrowdActor", position);
            go.layer = LayerMask.NameToLayer("Enemy");
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<CapsuleCollider2D>().size = new Vector2(1.2f, 1.2f);
            var health = go.AddComponent<EnemyHealth>();
            Invoke(health, "Awake");
            var lifecycle = go.AddComponent<EnemyLifecycle>();
            SetConfig(lifecycle);
            Invoke(lifecycle, "Awake");
            Invoke(lifecycle, "OnEnable");
            var movement = go.AddComponent<EnemyCrowdMovement>();
            SetConfig(movement);
            Invoke(movement, "Awake");
            Invoke(movement, "OnEnable");
            actors.Add(lifecycle);
            return lifecycle;
        }

        private void Step(Vector2 intent)
        {
            registry.SynchronizeGrid();
            foreach (var actor in actors)
            {
                actor.GetComponent<Rigidbody2D>().velocity = intent;
                Invoke(actor.GetComponent<EnemyCrowdMovement>(), "Tick", 0.02f);
            }
            scene.GetPhysicsScene2D().Simulate(0.02f);
        }

        [Test]
        public void GridTracksMovementDeathDisableAndReuseWithoutStaleEntries()
        {
            var actor = Actor(Vector2.zero);
            Assert.AreEqual(1, registry.Grid.Count);
            actor.GetComponent<Rigidbody2D>().position = Vector2.right * 5f;
            registry.SynchronizeGrid();
            Assert.IsTrue(registry.Grid.TryGetPosition(actor, out var point));
            Assert.AreEqual(Vector2.right * 5f, point);
            actor.Health.TryTakeDamage(100f);
            Assert.AreEqual(0, registry.Grid.Count);
            Invoke(actor, "OnDisable");
            actor.Health.ResetForSpawn();
            Invoke(actor, "OnEnable");
            Assert.AreEqual(1, registry.Grid.Count);
            actor.Despawn();
            Assert.AreEqual(0, registry.Grid.Count);
        }

        [Test]
        public void OverlappingIdleActorsSeparateWithoutPhysicsPushing()
        {
            var a = Actor(Vector2.zero);
            var b = Actor(Vector2.right * 0.3f);
            Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(a.gameObject.layer, b.gameObject.layer));
            for (int i = 0; i < 100; i++) Step(Vector2.zero);
            Assert.Greater(Vector2.Distance(a.Position, b.Position), 0.95f);
        }

        [Test]
        public void ExactlyCoincidentAgentsHaveFiniteOppositeSeparation()
        {
            var a = Actor(Vector2.zero);
            var b = Actor(Vector2.zero);
            for (int i = 0; i < 100; i++) Step(Vector2.zero);
            Assert.Greater(Vector2.Distance(a.Position, b.Position), 0.9f);
            Assert.Less((a.Position + b.Position).magnitude, 0.01f);
        }

        [Test]
        public void DenseGroupKeepsAdvancingInsteadOfFormingPhysicalQueue()
        {
            for (int i = 0; i < 24; i++) Actor(new Vector2(i % 6 * 0.4f, i / 6 * 0.4f));
            float before = 0f;
            foreach (var actor in actors) before += actor.Position.x;
            for (int i = 0; i < 100; i++) Step(Vector2.right * 3f);
            float after = 0f;
            foreach (var actor in actors) after += actor.Position.x;
            Assert.Greater((after - before) / actors.Count, 3f);
            foreach (var actor in actors)
                Assert.LessOrEqual(actor.GetComponent<EnemyCrowdMovement>().LastCandidateChecks, config.crowd.maxCandidates);
        }

        [Test]
        public void HighSpeedSweepStopsAtThinWallEvenWhenSeparationPushesTowardIt()
        {
            var actor = Actor(Vector2.zero);
            Create("ThinWall", new Vector2(1f, 0f)).AddComponent<BoxCollider2D>().size = new Vector2(0.025f, 5f);
            var movement = actor.GetComponent<EnemyCrowdMovement>();
            Vector2 safe = (Vector2)Invoke(movement, "ConstrainVelocity", Vector2.right * 100f, 0.02f);
            actor.GetComponent<Rigidbody2D>().velocity = safe;
            scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.Less(actor.GetComponent<Collider2D>().bounds.max.x, 0.99f);
        }

        [Test]
        public void ForwardObstacleProbeSlidesAlongWallWithoutPenetrating()
        {
            var actor = Actor(Vector2.zero);
            Create("Wall", new Vector2(1.5f, 0f)).AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 20f);
            for (int i = 0; i < 100; i++) Step(Vector2.right * 3f);
            Assert.Greater(Mathf.Abs(actor.Position.y), 2f);
            Assert.Less(actor.GetComponent<Collider2D>().bounds.max.x, 1.41f);
        }

        [Test]
        public void SeparationCannotPushActorIntoWallAndCanMoveAwayFromContact()
        {
            var actor = Actor(Vector2.zero);
            Actor(Vector2.left * 0.3f);
            Create("Wall", new Vector2(0.75f, 0f)).AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 10f);
            for (int i = 0; i < 80; i++) Step(Vector2.zero);
            Assert.Less(actor.GetComponent<Collider2D>().bounds.max.x, 0.66f);
            Vector2 away = (Vector2)Invoke(actor.GetComponent<EnemyCrowdMovement>(), "ConstrainVelocity", Vector2.left * 2f, 0.02f);
            Assert.Less(away.x, -1f);
        }

        [Test]
        public void AttackWindupKeepsBodyStillDespiteNeighborSeparation()
        {
            var actor = Actor(Vector2.zero);
            Actor(Vector2.left * 0.3f);
            var melee = actor.gameObject.AddComponent<MeleeEnemy>();
            SetConfig(melee);
            Invoke(melee, "Awake");
            Invoke(melee, "OnEnable");
            typeof(MeleeEnemy).GetField("<State>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(melee, MeleeEnemyState.Windup);
            var crowd = actor.GetComponent<EnemyCrowdMovement>();
            Invoke(crowd, "Awake");
            registry.SynchronizeGrid();
            Invoke(crowd, "Tick", 0.02f);
            Assert.AreEqual(Vector2.zero, actor.GetComponent<Rigidbody2D>().velocity);
            Invoke(melee, "OnDisable");
        }

        [Test]
        public void KnockbackRetainsDirectionAndIsNotReplacedBySeparation()
        {
            var actor = Actor(Vector2.zero);
            Actor(Vector2.left * 0.3f);
            var feedback = actor.gameObject.AddComponent<EnemyHitFeedback>();
            SetConfig(feedback);
            config.combatFeedback.hitStopDuration = 0f;
            Invoke(feedback, "Awake");
            Invoke(feedback, "OnEnable");
            var crowd = actor.GetComponent<EnemyCrowdMovement>();
            Invoke(crowd, "Awake");
            actor.Health.TryTakeDamage(new DamageInfo(1f, Vector2.zero, Vector2.left));
            Vector2 knockback = actor.GetComponent<Rigidbody2D>().velocity;
            registry.SynchronizeGrid();
            Invoke(crowd, "Tick", 0.02f);
            Assert.AreEqual(knockback, actor.GetComponent<Rigidbody2D>().velocity);
            Invoke(feedback, "OnDisable");
        }

        [Test]
        public void PauseAndDeathNeverMoveFromNeighborForces()
        {
            var actor = Actor(Vector2.zero);
            Actor(Vector2.right * 0.3f);
            var state = Create("State", Vector2.zero).AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            state.TryChangeState(GameState.Paused);
            Step(Vector2.zero);
            Assert.AreEqual(Vector2.zero, actor.GetComponent<Rigidbody2D>().velocity);
            state.TryChangeState(GameState.Playing);
            actor.Health.TryTakeDamage(100f);
            Invoke(actor.GetComponent<EnemyCrowdMovement>(), "Tick", 0.02f);
            Assert.AreEqual(Vector2.zero, actor.GetComponent<Rigidbody2D>().velocity);
        }
    }
}
