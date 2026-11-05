using NUnit.Framework;
using System.Reflection;
using ShootDemo.Config;
using ShootDemo.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShootDemo.Tests.EditMode
{
    public sealed class PlayerDashTests
    {
        private Scene scene;
        private GameObject player;
        private GameConfig config;
        private Rigidbody2D body;
        private PlayerMovement movement;
        private PlayerDash dash;
        private float previousFixedDeltaTime;

        [SetUp]
        public void SetUp()
        {
            previousFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 0.02f;
            scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            player = new GameObject("DashTestPlayer");
            player.SetActive(false);
            SceneManager.MoveGameObjectToScene(player, scene);
            body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var collider = player.AddComponent<CapsuleCollider2D>();
            collider.size = new Vector2(0.85f, 1.1f);
            collider.offset = new Vector2(0f, 0.2f);
            movement = player.AddComponent<PlayerMovement>();
            dash = player.AddComponent<PlayerDash>();
            SetConfig(movement);
            SetConfig(dash);
            player.SetActive(true);
            InvokeLifecycle(movement, "Awake");
            InvokeLifecycle(dash, "Awake");
            player.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            body.rotation = 90f;
            Physics2D.SyncTransforms();
        }

        private void SetConfig(Component component)
        {
            var serialized = new SerializedObject(component);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    Object.DestroyImmediate(root);
                }
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
            Object.DestroyImmediate(config);
            Time.fixedDeltaTime = previousFixedDeltaTime;
        }

        private void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                InvokeLifecycle(movement, "FixedUpdate");
                scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
            }
        }

        private static void InvokeLifecycle(MonoBehaviour component, string method)
        {
            // Preview scenes do not dispatch MonoBehaviour lifecycle messages.
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, null);
        }

        private BoxCollider2D CreateWall(Vector2 position, Vector2 size, bool trigger = false)
        {
            var wall = new GameObject("TestWall");
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = position;
            var collider = wall.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = trigger;
            Physics2D.SyncTransforms();
            return collider;
        }

        [Test]
        public void AvoidController_HasAPlaybackSpeedParameterForTheDodgeDuration()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/Arts/zhujue/neo/anim/neo2.controller");
            var parameter = System.Array.Find(controller.parameters, p => p.name == "DashPlaybackSpeed");
            var state = System.Array.Find(controller.layers[0].stateMachine.states,
                s => s.state.name == "neo2_avoid").state;

            Assert.That(parameter, Is.Not.Null);
            Assert.That(parameter.type, Is.EqualTo(AnimatorControllerParameterType.Float));
            Assert.That(parameter.defaultFloat, Is.GreaterThan(0f));
            Assert.That(state.speedParameterActive, Is.True);
            Assert.That(state.speedParameter, Is.EqualTo(parameter.name));
        }

        [Test]
        public void Dash_NormalizesDiagonalAndTravelsConfiguredDistance()
        {
            Assert.That(dash.TryDash(Vector2.one), Is.True);
            Step(16);
            Assert.That(body.position.magnitude, Is.EqualTo(config.dash.dashDistance).Within(0.01f));
            Assert.That(body.position.x, Is.EqualTo(body.position.y).Within(0.001f));
            Assert.That(dash.IsDashing, Is.False);
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Dash_NoInputUsesCurrentLocalForward()
        {
            Assert.That(dash.TryDash(Vector2.zero), Is.True);
            Assert.That(dash.DashDirection.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(dash.DashDirection.y, Is.EqualTo(1f).Within(0.001f));
            Step(16);
            Assert.That(body.position.y, Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void Dash_RejectsReentryAndRespectsCooldown()
        {
            Assert.That(dash.TryDash(Vector2.right), Is.True);
            Assert.That(dash.TryDash(Vector2.left), Is.False);
            Step(16);
            Assert.That(dash.TryDash(Vector2.right), Is.False);
            Step(25);
            Assert.That(dash.TryDash(Vector2.right), Is.True);
        }

        [Test]
        public void Dash_InvulnerabilityEndsBeforeTheActionFinishes()
        {
            dash.TryDash(Vector2.right);
            Assert.That(dash.IsInvulnerable, Is.True);
            Step(5);
            Assert.That(dash.IsInvulnerable, Is.True);
            Step(7);
            Assert.That(dash.IsDashing, Is.True);
            Assert.That(dash.IsInvulnerable, Is.False);
            Step(4);
            Assert.That(dash.IsInvulnerable, Is.False);
        }

        [Test]
        public void Dash_ThinWallStopsTheWholeCapsuleWithoutTunneling()
        {
            var wall = CreateWall(new Vector2(1f, 0f), new Vector2(0.01f, 8f));
            dash.TryDash(Vector2.right);
            Step(16);
            var capsule = player.GetComponent<Collider2D>();
            Assert.That(capsule.bounds.max.x, Is.LessThan(wall.bounds.min.x));
            Assert.That(body.position.x, Is.GreaterThan(0.4f).And.LessThan(0.65f));
            Assert.That(dash.IsDashing, Is.False);
        }

        [Test]
        public void Dash_DiagonalIntoWallCannotCrossIt()
        {
            var wall = CreateWall(new Vector2(1f, 0f), new Vector2(0.01f, 8f));
            dash.TryDash(Vector2.one);
            Step(16);
            Assert.That(player.GetComponent<Collider2D>().bounds.max.x, Is.LessThan(wall.bounds.min.x));
        }

        [Test]
        public void Dash_TriggerDoesNotBlockTravel()
        {
            CreateWall(new Vector2(1f, 0f), new Vector2(0.1f, 8f), true);
            dash.TryDash(Vector2.right);
            Step(16);
            Assert.That(body.position.x, Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void Dash_CanMoveAwayFromAWallAlreadyTouchingTheCapsule()
        {
            CreateWall(new Vector2(0.36f, 0f), new Vector2(0.02f, 8f));
            dash.TryDash(Vector2.left);
            Step(16);
            Assert.That(body.position.x, Is.EqualTo(-5f).Within(0.01f));
        }

        [Test]
        public void Dash_NonIntegralDurationStillTravelsTheExactDistance()
        {
            config.dash.dashDuration = 0.175f;
            dash.TryDash(Vector2.right);
            Step(10);
            Assert.That(body.position.x, Is.EqualTo(5f).Within(0.01f));
            Assert.That(dash.IsDashing, Is.False);
        }

        [Test]
        public void Dash_DisablingCancelsVelocityAndInvulnerability()
        {
            dash.TryDash(Vector2.right);
            Step(1);
            dash.enabled = false;
            InvokeLifecycle(dash, "OnDisable");
            Assert.That(dash.IsDashing, Is.False);
            Assert.That(dash.IsInvulnerable, Is.False);
            Assert.That(body.velocity, Is.EqualTo(Vector2.zero));
            Assert.That(dash.TryDash(Vector2.right), Is.False);
        }
    }
}
