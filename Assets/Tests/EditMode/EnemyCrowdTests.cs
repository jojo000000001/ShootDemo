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
        public void SavedArenaDoesNotPutEnvironmentSolidsOnNonCollidingEnemyLayer()
        {
            var arena = SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
            bool opened = !arena.IsValid() || !arena.isLoaded;
            if (opened) arena = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            try
            {
                int checkedSolids = 0;
                int enemyLayer = LayerMask.NameToLayer("Enemy");
                foreach (var root in arena.GetRootGameObjects())
                foreach (var solid in root.GetComponentsInChildren<Collider2D>(true))
                {
                    if (solid.isTrigger || solid.GetComponentInParent<EnemyHealth>() != null
                        || (solid.attachedRigidbody != null && solid.attachedRigidbody.bodyType == RigidbodyType2D.Dynamic)) continue;
                    checkedSolids++;
                    Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(enemyLayer, solid.gameObject.layer),
                        solid.name + " must physically block enemies.");
                }
                Assert.Greater(checkedSolids, 0);
            }
            finally { if (opened) EditorSceneManager.CloseScene(arena, true); }
        }

        [Test]
        public void DiagonalContactPreservesSlidingAlongWall()
        {
            var actor = Actor(new Vector2(-1.11f, 0f));
            var wall = Create("Wall", Vector2.zero).AddComponent<BoxCollider2D>();
            wall.size = new Vector2(1f, 10f);
            Physics2D.SyncTransforms();
            Vector2 velocity = (Vector2)Invoke(actor.GetComponent<EnemyCrowdMovement>(),
                "ConstrainVelocity", new Vector2(3f, 2f), 0.02f);
            Assert.That(velocity.y, Is.GreaterThan(1.5f), "Contact must not erase tangential movement.");
            Assert.That(velocity.x, Is.LessThan(0.6f));
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
        public void EntranceFollowersSlowForStationaryNeighborAndStillSeparate()
        {
            var follower = Actor(Vector2.zero);
            var leader = Actor(Vector2.up * 0.5f);
            var melee = follower.gameObject.AddComponent<MeleeEnemy>();
            SetConfig(melee);
            Invoke(melee, "Awake");
            Invoke(melee, "OnEnable");
            typeof(MeleeEnemy).GetField("<UsingPassageEntrance>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(melee, true);
            var movement = follower.GetComponent<EnemyCrowdMovement>();
            Invoke(movement, "Awake");
            registry.SynchronizeGrid();
            for (int i = 0; i < 10; i++)
            {
                follower.GetComponent<Rigidbody2D>().velocity = Vector2.up * 3.5f;
                Invoke(movement, "Tick", 0.02f);
                Assert.LessOrEqual(follower.GetComponent<Rigidbody2D>().velocity.y, 0.001f,
                    "Entrance followers must not continue advancing inside a stationary neighbor.");
            }
            Assert.Greater(movement.LastNeighborCount, 0);
            Assert.LessOrEqual(movement.LastCandidateChecks, config.crowd.maxCandidates);
            Invoke(melee, "OnDisable");
        }

        [Test]
        public void SideTrafficDoesNotStopAnActorAlignedWithTheEntrance()
        {
            var guide = Create("Passage", Vector2.zero).AddComponent<EnemyNarrowPassage>();
            var data = new SerializedObject(guide);
            data.FindProperty("interior").rectValue = new Rect(-1.2f, 0f, 2.4f, 20f);
            data.FindProperty("outside").vector2Value = new Vector2(0f, -1.3f);
            data.FindProperty("entrance").vector2Value = Vector2.up;
            data.ApplyModifiedPropertiesWithoutUndo();
            Invoke(guide, "OnEnable");
            var player = Create("Player", Vector2.up * 10f).AddComponent<ShootDemo.Player.PlayerHealth>();
            SetConfig(player);
            Invoke(player, "Awake");
            var follower = Actor(new Vector2(0f, -1.3f));
            Actor(new Vector2(0.8f, -0.9f));
            var brain = follower.gameObject.AddComponent<MeleeEnemy>();
            SetConfig(brain);
            Invoke(brain, "Awake");
            Invoke(brain, "OnEnable");
            brain.SetTarget(player);
            typeof(MeleeEnemy).GetField("<UsingPassageEntrance>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(brain, true);
            var movement = follower.GetComponent<EnemyCrowdMovement>();
            Invoke(movement, "Awake");
            try
            {
                registry.SynchronizeGrid();
                follower.GetComponent<Rigidbody2D>().velocity = Vector2.up * 3.5f;
                Invoke(movement, "Tick", 0.02f);
                Assert.Greater(follower.GetComponent<Rigidbody2D>().velocity.y, 1f,
                    "A side actor still approaching the staging point must not be treated as a stopped leader in the entrance lane.");
            }
            finally { Invoke(brain, "OnDisable"); Invoke(guide, "OnDisable"); }
        }

        [Test]
        public void CombatantInsidePassageDoesNotReserveTheEntranceForItself()
        {
            var guide = Create("Passage", Vector2.zero).AddComponent<EnemyNarrowPassage>();
            var data = new SerializedObject(guide);
            data.FindProperty("interior").rectValue = new Rect(-1.2f, 0f, 2.4f, 20f);
            data.FindProperty("outside").vector2Value = new Vector2(0f, -1.3f);
            data.FindProperty("entrance").vector2Value = Vector2.up;
            data.ApplyModifiedPropertiesWithoutUndo();
            Invoke(guide, "OnEnable");
            var player = Create("Player", Vector2.up * 4f).AddComponent<ShootDemo.Player.PlayerHealth>();
            SetConfig(player);
            Invoke(player, "Awake");
            var follower = Actor(new Vector2(0f, 0.7f));
            var holder = Actor(Vector2.up);
            var ranged = holder.gameObject.AddComponent<RangedEnemy>();
            SetConfig(ranged);
            Invoke(ranged, "Awake");
            Invoke(ranged, "OnEnable");
            ranged.SetTarget(player);
            typeof(RangedEnemy).GetField("<State>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ranged, RangedEnemyState.Firing);
            Invoke(holder, "Awake");
            var brain = follower.gameObject.AddComponent<MeleeEnemy>();
            SetConfig(brain);
            Invoke(brain, "Awake");
            Invoke(brain, "OnEnable");
            brain.SetTarget(player);
            typeof(MeleeEnemy).GetField("<UsingPassageEntrance>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(brain, true);
            var movement = follower.GetComponent<EnemyCrowdMovement>();
            Invoke(movement, "Awake");
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    registry.SynchronizeGrid();
                    holder.GetComponent<Rigidbody2D>().velocity = Vector2.zero;
                    follower.GetComponent<Rigidbody2D>().velocity = Vector2.up * 3.5f;
                    Invoke(movement, "Tick", 0.02f);
                    scene.GetPhysicsScene2D().Simulate(0.02f);
                }
                Assert.Greater(follower.Position.y, holder.Position.y + 0.5f,
                    "A ranged actor already inside must not permanently stop the incoming queue.");
                Assert.AreEqual(Vector2.up, holder.Position, "The front actor stays in the test instead of being removed.");
            }
            finally { Invoke(brain, "OnDisable"); Invoke(ranged, "OnDisable"); Invoke(guide, "OnDisable"); }
        }

        [Test]
        public void OverlappingEntranceActorsRecoverSpacingWhileContinuingForward()
        {
            var a = Actor(Vector2.zero);
            var b = Actor(Vector2.up * 0.1f);
            var brains = new List<MeleeEnemy>();
            foreach (var actor in new[] { a, b })
            {
                var brain = actor.gameObject.AddComponent<MeleeEnemy>();
                SetConfig(brain);
                Invoke(brain, "Awake");
                Invoke(brain, "OnEnable");
                typeof(MeleeEnemy).GetField("<UsingPassageEntrance>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(brain, true);
                Invoke(actor.GetComponent<EnemyCrowdMovement>(), "Awake");
                brains.Add(brain);
            }
            try
            {
                for (int i = 0; i < 100; i++) Step(Vector2.up * 3.5f);
                Assert.Greater(Vector2.Distance(a.Position, b.Position), 0.9f);
                Assert.Greater(Mathf.Min(a.Position.y, b.Position.y), 2f,
                    "Restoring spacing must not stop all entrance traffic.");
            }
            finally { foreach (var brain in brains) Invoke(brain, "OnDisable"); }
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
