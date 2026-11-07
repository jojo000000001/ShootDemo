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
    public sealed class EnemySpawnerTests
    {
        private Scene scene;
        private GameConfig config;
        private EnemySpawner spawner;
        private EnemyRegistry registry;
        private PlayerHealth player;
        private EnemyLifecycle prefab;
        private float savedTimeScale;
        private Random.State savedRandom;

        [SetUp]
        public void SetUp()
        {
            savedTimeScale = Time.timeScale;
            savedRandom = Random.state;
            Random.InitState(521);
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.spawn.positionAttempts = 4;
            var root = Create("Spawner", Vector2.zero);
            registry = root.AddComponent<EnemyRegistry>();
            Invoke(registry, "Awake");
            spawner = root.AddComponent<EnemySpawner>();
            var target = Create("Player", Vector2.zero);
            target.AddComponent<Rigidbody2D>().gravityScale = 0f;
            target.AddComponent<CircleCollider2D>().radius = 0.4f;
            player = target.AddComponent<PlayerHealth>();
            SetObject(player, "config", config);
            Invoke(player, "Awake");
            SetObject(spawner, "config", config);
            SetObject(spawner, "registry", registry);
            SetObject(spawner, "player", player);
            Invoke(spawner, "Awake");
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/chushouguai.prefab")
                .GetComponent<EnemyLifecycle>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var state = root.GetComponent<GameStateMachine>();
                if (state != null) Invoke(state, "OnDestroy");
            }
            Invoke(registry, "OnDestroy");
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(config);
            Time.timeScale = savedTimeScale;
            Random.state = savedRandom;
        }

        private GameObject Create(string name, Vector2 position)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            return go;
        }

        private static object Invoke(object component, string method, params object[] args)
        {
            return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, args);
        }

        private static void SetObject(Component component, string field, Object value)
        {
            var data = new SerializedObject(component);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private void Points(params Vector2[] positions)
        {
            var data = new SerializedObject(spawner);
            var points = data.FindProperty("spawnPoints");
            points.arraySize = positions.Length;
            for (int i = 0; i < positions.Length; i++)
                points.GetArrayElementAtIndex(i).objectReferenceValue = Create("Point", positions[i]).transform;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private bool Find(out Vector2 position) => spawner.TryFindSpawnPosition(prefab, out position);

        [Test]
        public void IncrementalSearchSuspendsAndEventuallyFindsDetour()
        {
            var wall = Create("DetourWall", new Vector2(5f, 0f)).AddComponent<BoxCollider2D>();
            wall.size = new Vector2(1f, 5f);
            Physics2D.SyncTransforms();
            var navigation = new EnemyPathfinder2D(scene, 0.75f, ~0);
            var path = new System.Collections.Generic.List<Vector2>();
            int steps = 0;
            using (var search = navigation.FindPathIncremental(Vector2.zero, Vector2.right * 10f, 1f, path).GetEnumerator())
            {
                Assert.IsTrue(search.MoveNext(), "A blocked direct route must suspend before expanding the grid.");
                Assert.IsEmpty(path);
                while (search.MoveNext()) Assert.Less(++steps, 25000);
            }
            Assert.IsNotEmpty(path);
            Vector2 previous = Vector2.zero;
            foreach (var point in path)
            {
                Assert.IsTrue(navigation.SegmentClear(previous, point));
                previous = point;
            }
            Assert.LessOrEqual(Vector2.Distance(previous, Vector2.right * 10f), 1.5f);
        }

        [Test]
        public void NormalSpawnSearchIsBoundedAndResetCancelsPendingWork()
        {
            Points(new Vector2(10f, 0f));
            foreach (var offset in new[] { new Vector2(2f, 0f), new Vector2(-2f, 0f), new Vector2(0f, 2f), new Vector2(0f, -2f) })
            {
                var wall = Create("PocketWall", new Vector2(10f, 0f) + offset).AddComponent<BoxCollider2D>();
                wall.size = offset.x != 0f ? new Vector2(0.3f, 4.5f) : new Vector2(4.5f, 0.3f);
            }
            var data = new SerializedObject(spawner);
            var entries = data.FindProperty("enemies");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("prefab").objectReferenceValue = prefab;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("weight").floatValue = 1f;
            data.ApplyModifiedPropertiesWithoutUndo();
            config.spawn.initialDelay = 0f;
            Invoke(spawner, "Awake");
            Invoke(spawner, "Tick", 0.02f);
            Assert.That(spawner.LastPlacementSteps, Is.InRange(1, 64));
            Assert.AreEqual(0, spawner.TotalSpawned);
            var field = typeof(EnemySpawner).GetField("pendingPlacement", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field.GetValue(spawner));
            spawner.ResetSession();
            Assert.IsNull(field.GetValue(spawner));
            Assert.AreEqual(0, spawner.FailedAttempts);
        }

        [Test]
        public void SpawnPaddingDoesNotMakePassableEntranceUnreachable()
        {
            Points(new Vector2(0f, -12f));
            var guide = Create("EntranceGuide", Vector2.zero).AddComponent<EnemyNarrowPassage>();
            var data = new SerializedObject(guide);
            data.FindProperty("interior").rectValue = new Rect(-1.195f, -5.09f, 2.39f, 28.18f);
            data.FindProperty("outside").vector2Value = new Vector2(0f, -6.4f);
            data.FindProperty("entrance").vector2Value = new Vector2(0f, -4.1f);
            data.ApplyModifiedPropertiesWithoutUndo();
            Invoke(guide, "OnEnable");
            try
            {
                foreach (float sign in new[] { -1f, 1f })
                {
                    var side = Create("SideWall", new Vector2(sign * 1.335f, 9f)).AddComponent<BoxCollider2D>();
                    side.size = new Vector2(0.28f, 28.74f);
                    var post = Create("EntrancePost", new Vector2(sign * 1.13f, -5.23f)).AddComponent<BoxCollider2D>();
                    post.size = new Vector2(0.68f, 0.28f);
                }
                Physics2D.SyncTransforms();
                Assert.IsTrue(Find(out var point), "0.85m spawn padding must not block a doorway passable with the live actor's 0.76m clearance.");
                Assert.AreEqual(new Vector2(0f, -12f), point);
            }
            finally { Invoke(guide, "OnDisable"); }
        }

        [Test]
        public void SpawnRejectsVisibleRockBeyondItsSmallCollider()
        {
            Points(new Vector2(10f, 0f));
            var rock = Create("Rock", new Vector2(11.5f, 0f));
            rock.AddComponent<BoxCollider2D>().size = Vector2.one * 0.2f;
            var texture = new Texture2D(4, 4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f, 1f);
            rock.AddComponent<SpriteRenderer>().sprite = sprite;
            try { Assert.IsFalse(Find(out _), "The body is clear of the collider but hidden inside the rock art."); }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void SpawnSeesWallMovedBeforePhysicsStep()
        {
            Points(new Vector2(10f, 0f));
            var wall = Create("MovedWall", new Vector2(30f, 0f));
            wall.AddComponent<BoxCollider2D>().size = Vector2.one * 3f;
            Physics2D.SyncTransforms();
            wall.transform.position = new Vector2(10f, 0f);
            Assert.IsFalse(Find(out _));
        }

        [Test]
        public void RingCandidatesRemainInsideArenaAndOutsidePlayerSafetyDistance()
        {
            for (int i = 0; i < 30; i++)
            {
                Assert.IsTrue(Find(out var point));
                Assert.That(point.magnitude, Is.InRange(10f, 14f));
                Assert.Greater(point.magnitude, config.spawn.minPlayerDistance + 0.8f);
            }
        }

        [Test]
        public void RingMovesWithPlayerNearArenaEdge()
        {
            player.transform.position = new Vector2(38f, 31f);
            config.spawn.positionAttempts = 64;
            Assert.IsTrue(Find(out var point));
            Assert.Less(point.x + 0.85f, 39.5f);
            Assert.Less(point.y + 0.85f, 32.9f);
            Assert.Greater(Vector2.Distance(point, player.transform.position), config.spawn.minPlayerDistance);
        }

        [Test]
        public void PointInsideWallOrPlayerOrOutsideArenaIsRejected()
        {
            Points(new Vector2(10f, 0f), Vector2.zero, new Vector2(50f, 0f));
            Create("Wall", new Vector2(10f, 0f)).AddComponent<BoxCollider2D>().size = Vector2.one * 3f;
            Assert.IsFalse(Find(out _));
        }

        [Test]
        public void WholeBodyClearanceRejectsPointNearWall()
        {
            Points(new Vector2(10f, 0f));
            Create("Wall", new Vector2(11f, 0f)).AddComponent<BoxCollider2D>().size = Vector2.one;
            Assert.IsFalse(Find(out _), "The center is free but the enemy would overlap the wall.");
        }

        [Test]
        public void AnotherActorBlocksPointButTriggerDoesNot()
        {
            Points(new Vector2(10f, 0f));
            var collider = Create("Actor", new Vector2(10f, 0f)).AddComponent<CircleCollider2D>();
            collider.gameObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            Assert.IsFalse(Find(out _));
            collider.isTrigger = true;
            Assert.IsTrue(Find(out _));
        }

        [Test]
        public void IsolatedEmptyPocketIsRejected()
        {
            Points(new Vector2(10f, 0f));
            foreach (var offset in new[] { new Vector2(2f, 0f), new Vector2(-2f, 0f), new Vector2(0f, 2f), new Vector2(0f, -2f) })
            {
                var wall = Create("PocketWall", new Vector2(10f, 0f) + offset).AddComponent<BoxCollider2D>();
                wall.size = offset.x != 0f ? new Vector2(0.3f, 4.5f) : new Vector2(4.5f, 0.3f);
            }
            Assert.IsFalse(Find(out _));
        }

        [Test]
        public void InvalidAuthoredPointsDoNotFallBackToUncheckedRing()
        {
            Points(Vector2.zero);
            Assert.IsFalse(Find(out _));
            Assert.AreEqual(0, registry.AliveCount);
        }

        [Test]
        public void InactiveAuthoredPointIsIgnored()
        {
            Points(Vector2.right * 10f);
            var data = new SerializedObject(spawner);
            var point = (Transform)data.FindProperty("spawnPoints").GetArrayElementAtIndex(0).objectReferenceValue;
            point.gameObject.SetActive(false);
            Assert.IsFalse(Find(out _));
        }

        [Test]
        public void DifficultyCurveIncreasesRateAndClampsAfterDuration()
        {
            config.spawn.waveCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
            Assert.AreEqual(0.5f, EnemySpawner.EvaluateInterval(config.spawn, 0f));
            Assert.AreEqual(0.25f, EnemySpawner.EvaluateInterval(config.spawn, 120f));
            Assert.AreEqual(0.25f, EnemySpawner.EvaluateInterval(config.spawn, 1000f));
        }

        [Test]
        public void PauseAndGameOverFreezeSpawnerClock()
        {
            var state = Create("State", Vector2.zero).AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            Invoke(spawner, "Tick", 0.5f);
            Assert.AreEqual(0.5f, spawner.ElapsedSeconds);
            state.TryChangeState(GameState.Paused);
            Invoke(spawner, "Tick", 5f);
            Assert.AreEqual(0.5f, spawner.ElapsedSeconds);
            state.TryChangeState(GameState.GameOver);
            Invoke(spawner, "Tick", 5f);
            Assert.AreEqual(0.5f, spawner.ElapsedSeconds);
            Assert.IsFalse(spawner.TrySpawn(out _));
        }

        [Test]
        public void LiveCapIncludesEnemiesOutsideSpawnerAndPreventsManualSpawn()
        {
            config.spawn.maxAlive = 1;
            var actor = Create("ExistingEnemy", Vector2.up * 5f);
            var health = actor.AddComponent<EnemyHealth>();
            Invoke(health, "Awake");
            var life = actor.AddComponent<EnemyLifecycle>();
            Invoke(life, "Awake");
            Invoke(life, "OnEnable");
            Assert.AreEqual(1, registry.AliveCount);
            for (int i = 0; i < 100; i++) Invoke(spawner, "Tick", 0.1f);
            Assert.AreEqual(0, spawner.TotalSpawned);
            Assert.AreEqual(0, spawner.FailedAttempts, "Hitting the cap is not a failed placement.");
            Assert.IsFalse(spawner.TrySpawn(out _));
            Invoke(life, "OnDisable");
        }

        [Test]
        public void FailedPlacementCannotSpawnFromAnUnsafeAuthoredPoint()
        {
            Points(Vector2.zero);
            var data = new SerializedObject(spawner);
            var entries = data.FindProperty("enemies");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("prefab").objectReferenceValue = prefab;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("weight").floatValue = 1f;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsFalse(spawner.TrySpawn(out var result));
            Assert.IsNull(result);
            Assert.AreEqual(0, spawner.TotalSpawned);
            Assert.AreEqual(0, registry.PooledActiveCount(prefab));
        }

        [Test]
        public void NoAvailablePositionRetriesAtBoundedRateAndResetClearsCounters()
        {
            config.spawn.initialDelay = 0f;
            Invoke(spawner, "Awake");
            for (int i = 0; i < 50; i++) Invoke(spawner, "Tick", 0.02f);
            Assert.That(spawner.FailedAttempts, Is.InRange(1, 3));
            Assert.AreEqual(0, spawner.TotalSpawned);
            spawner.ResetSession();
            Assert.AreEqual(0, spawner.ElapsedSeconds);
            Assert.AreEqual(0, spawner.FailedAttempts);
        }
    }
}
