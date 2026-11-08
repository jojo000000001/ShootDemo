using NUnit.Framework;
using ShootDemo.Enemy;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootDemo.Tests.EditMode
{
    public sealed class EnemyFlowFieldTests
    {
        private Scene scene;
        private EnemyFlowFieldWorld world;
        private EnemyPathfinder2D navigation;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            world = EnemyFlowFieldWorld.ForScene(scene);
            navigation = new EnemyPathfinder2D(scene, 0.76f, ~0);
        }

        [TearDown]
        public void TearDown() { EditorSceneManager.ClosePreviewScene(scene); }

        private void Wall(Vector2 position, Vector2 size)
        {
            var wall = new GameObject("Static wall");
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = size;
            Physics2D.SyncTransforms();
        }

        private bool Sample(Vector2 from, Vector2 goal, out Vector2 next, bool entrance = false, float radius = 0.76f)
            => world.TryGetWaypoint(from, goal, radius, ~0, entrance, navigation, out next);

        [Test]
        public void ThousandRequestsShareOneGraphAndOneIntegration()
        {
            Wall(Vector2.zero, new Vector2(1f, 4f));
            for (int i = 0; i < 1000; i++)
                Assert.IsTrue(Sample(new Vector2(-4f, (i % 5) * 0.1f), Vector2.right * 4f, out _));
            Assert.AreEqual(1, world.GraphBuilds);
            Assert.AreEqual(1, world.CompletedBuilds);
            Assert.AreEqual(1000, world.SampleCount);
        }

        [Test]
        public void FlowEscapesUTrapWithClearBodySegments()
        {
            Wall(new Vector2(-2f, 0f), new Vector2(0.5f, 6f));
            Wall(new Vector2(0.5f, 3f), new Vector2(5.5f, 0.5f));
            Wall(new Vector2(0.5f, -3f), new Vector2(5.5f, 0.5f));
            Vector2 position = Vector2.zero, goal = new Vector2(-5f, 0f);
            for (int i = 0; i < 120 && !navigation.SegmentClear(position, goal); i++)
            {
                Assert.IsTrue(Sample(position, goal, out Vector2 next), "No direction at " + position);
                Assert.IsTrue(navigation.SegmentClear(position, next));
                Assert.Greater(Vector2.Distance(position, next), 0.01f);
                position = next;
            }
            Assert.IsTrue(navigation.SegmentClear(position, goal), "Must go out of the U opening and around its back.");
        }

        [Test]
        public void EnclosedGoalDoesNotProduceDirectionsThroughWalls()
        {
            Wall(Vector2.zero, new Vector2(3f, 3f));
            Assert.IsFalse(Sample(Vector2.left * 5f, Vector2.zero, out _));
        }

        [Test]
        public void MovingGoalRebuildsIntegrationButNotStaticGraph()
        {
            Wall(Vector2.zero, new Vector2(1f, 4f));
            Assert.IsTrue(Sample(Vector2.left * 4f, new Vector2(4f, 1f), out _));
            Assert.IsTrue(Sample(Vector2.left * 4f, new Vector2(4f, -1f), out _));
            Assert.AreEqual(1, world.GraphBuilds);
            Assert.AreEqual(2, world.CompletedBuilds);
        }

        [Test]
        public void EntranceFieldDoesNotOverwritePlayerField()
        {
            Wall(Vector2.zero, new Vector2(1f, 4f));
            Assert.IsTrue(Sample(Vector2.left * 4f, new Vector2(4f, 2f), out Vector2 first));
            Assert.IsTrue(Sample(Vector2.left * 4f, new Vector2(4f, -2f), out _, true));
            Assert.IsTrue(Sample(Vector2.left * 4f, new Vector2(4f, 2f), out Vector2 unchanged));
            Assert.AreEqual(first, unchanged);
            Assert.AreEqual(2, world.CompletedBuilds);
            Assert.AreEqual(1, world.GraphBuilds);
        }

        [Test]
        public void CurrentPrefabSizesShareLayerWhileLargeBodiesUseSeparateLayer()
        {
            Sample(Vector2.left * 4f, Vector2.right * 4f, out _, false, 0.71f);
            Sample(Vector2.left * 4f, Vector2.right * 4f, out _, false, 0.76f);
            Assert.AreEqual(1, world.LayerCount);
            Sample(Vector2.left * 4f, Vector2.right * 4f, out _, false, 1f);
            Assert.AreEqual(2, world.LayerCount);
        }
    }
}
