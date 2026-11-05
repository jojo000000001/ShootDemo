using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ShootDemo.Combat;
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
    public sealed class MeleeEnemyTests
    {
        private Scene scene;
        private GameConfig config;
        private MeleeEnemy enemy;
        private EnemyHealth health;
        private EnemyHitFeedback feedback;
        private PlayerHealth player;
        private Rigidbody2D body;
        private float savedTimeScale;

        [SetUp]
        public void SetUp()
        {
            savedTimeScale = Time.timeScale;
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.combatFeedback.hitStopDuration = 0f;
            config.playerHealth.hurtInvulnerability = 0f;
            var actor = Create("Melee");
            body = actor.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            actor.AddComponent<CircleCollider2D>().radius = 0.5f;
            health = actor.AddComponent<EnemyHealth>();
            feedback = actor.AddComponent<EnemyHitFeedback>();
            SetConfig(feedback);
            enemy = actor.AddComponent<MeleeEnemy>();
            SetConfig(enemy);
            Invoke(health, "Awake");
            Invoke(feedback, "Awake");
            Invoke(feedback, "OnEnable");
            Invoke(enemy, "Awake");
            Invoke(enemy, "OnEnable");
            var target = Create("Player");
            target.transform.position = new Vector2(1.4f, 0f);
            target.AddComponent<Rigidbody2D>().gravityScale = 0f;
            target.AddComponent<CircleCollider2D>().radius = 0.3f;
            player = target.AddComponent<PlayerHealth>();
            SetConfig(player);
            Invoke(player, "Awake");
            enemy.SetTarget(player);
        }

        [TearDown]
        public void TearDown()
        {
            Invoke(enemy, "OnDisable");
            Invoke(feedback, "OnDisable");
            foreach (var root in scene.GetRootGameObjects())
            {
                var state = root.GetComponent<GameStateMachine>();
                if (state != null) Invoke(state, "OnDestroy");
                Object.DestroyImmediate(root);
            }
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(config);
            Time.timeScale = savedTimeScale;
        }

        private GameObject Create(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private void SetConfig(Component component)
        {
            var data = new SerializedObject(component);
            data.FindProperty("config").objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static object Invoke(object component, string method, params object[] args)
        {
            return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, args);
        }

        private void Tick(float seconds) { Invoke(enemy, "Tick", seconds); }
        private void Strike() { Tick(0.02f); Tick(config.enemy.meleeWindup + 0.01f); }

        private void Wall(Vector2 position, Vector2 size)
        {
            var wall = Create("Wall");
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = size;
        }

        [Test]
        public void WindupStopsMovementAndDoesNotDealEarlyDamage()
        {
            Tick(0.02f);
            Assert.AreEqual(MeleeEnemyState.Windup, enemy.State);
            Tick(config.enemy.meleeWindup * 0.8f);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
            Assert.AreEqual(Vector2.zero, body.velocity);
        }

        [Test]
        public void StrikeDealsDamageOnlyOnceAndEnforcesInterval()
        {
            var times = new List<float>();
            float elapsed = 0f;
            player.Damaged += amount => times.Add(elapsed);
            for (int i = 0; i < 250; i++) { elapsed += 0.02f; Tick(0.02f); }
            Assert.GreaterOrEqual(times.Count, 3);
            Assert.LessOrEqual(times.Count, 5);
            for (int i = 1; i < times.Count; i++)
                Assert.GreaterOrEqual(times[i] - times[i - 1], config.enemy.meleeInterval - 0.001f);
        }

        [Test]
        public void LeavingRangeDuringWindupMakesStrikeMiss()
        {
            Tick(0.02f);
            player.transform.position = new Vector2(4f, 0f);
            Tick(config.enemy.meleeWindup + 0.01f);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
            Assert.AreEqual(MeleeEnemyState.Recovery, enemy.State);
        }

        [Test]
        public void CrossingBehindDuringWindupMakesStrikeMiss()
        {
            Tick(0.02f);
            player.transform.position = new Vector2(-1.4f, 0f);
            Tick(config.enemy.meleeWindup + 0.01f);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
        }

        [Test]
        public void WallAddedDuringWindupBlocksStrike()
        {
            Tick(0.02f);
            Wall(new Vector2(0.7f, 0f), new Vector2(0.1f, 3f));
            Tick(config.enemy.meleeWindup + 0.01f);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
        }

        [Test]
        public void DamageInterruptsWindupAndPreservesKnockback()
        {
            Tick(0.02f);
            health.TryTakeDamage(new DamageInfo(1f, body.position, Vector2.left));
            Assert.AreNotEqual(MeleeEnemyState.Windup, enemy.State);
            Vector2 knockback = body.velocity;
            Tick(0.02f);
            Assert.AreEqual(knockback, body.velocity);
            Assert.Less(body.velocity.x, 0f);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
        }

        [Test]
        public void DeathDuringWindupPreventsStrikeAndMovement()
        {
            Tick(0.02f);
            health.TryTakeDamage(100f);
            Tick(2f);
            Assert.AreEqual(MeleeEnemyState.Dead, enemy.State);
            Assert.AreEqual(Vector2.zero, body.velocity);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
        }

        [Test]
        public void PauseFreezesWindupUntilPlayingResumes()
        {
            var state = Create("State").AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            Tick(0.02f);
            state.TryChangeState(GameState.Paused);
            Tick(5f);
            Assert.AreEqual(MeleeEnemyState.Windup, enemy.State);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
            state.TryChangeState(GameState.Playing);
            Tick(config.enemy.meleeWindup + 0.01f);
            Assert.AreEqual(player.MaxHealth - config.enemy.meleeDamage, player.CurrentHealth);
        }

        [Test]
        public void DeadTargetIsNotAttacked()
        {
            Tick(0.02f);
            player.TryTakeDamage(100f);
            Tick(1f);
            Assert.AreEqual(MeleeEnemyState.Idle, enemy.State);
            Assert.AreEqual(Vector2.zero, body.velocity);
        }

        [Test]
        public void PlayerInvulnerabilityRejectsStrike()
        {
            config.playerHealth.hurtInvulnerability = 2f;
            player.TryTakeDamage(1f);
            Strike();
            Assert.AreEqual(player.MaxHealth - 1f, player.CurrentHealth);
        }

        [Test]
        public void PathRoutesAroundWallWithBodyClearance()
        {
            Wall(Vector2.zero, new Vector2(1f, 4f));
            var navigation = new EnemyPathfinder2D(scene, 0.6f, ~0);
            var path = new List<Vector2>();
            Vector2 start = new Vector2(-4f, 0f);
            Assert.IsFalse(navigation.SegmentClear(start, new Vector2(4f, 0f)));
            Assert.IsTrue(navigation.FindPath(start, new Vector2(4f, 0f), 1.3f, path));
            float maxY = 0f;
            foreach (var point in path)
            {
                Assert.IsTrue(navigation.SegmentClear(start, point));
                start = point;
                maxY = Mathf.Max(maxY, Mathf.Abs(point.y));
            }
            Assert.Greater(maxY, 2.6f);
        }

        [Test]
        public void PathEscapesUShapedTrapInsteadOfOscillating()
        {
            Wall(new Vector2(-2f, 0f), new Vector2(0.5f, 6f));
            Wall(new Vector2(0.5f, 3f), new Vector2(5.5f, 0.5f));
            Wall(new Vector2(0.5f, -3f), new Vector2(5.5f, 0.5f));
            var navigation = new EnemyPathfinder2D(scene, 0.6f, ~0);
            var path = new List<Vector2>();
            Assert.IsTrue(navigation.FindPath(Vector2.zero, new Vector2(-5f, 0f), 1.3f, path));
            Assert.IsTrue(path.Exists(p => p.x > 3.8f), "Must exit the opening before going around the U.");
        }

        [Test]
        public void UnreachableEnclosedTargetReturnsNoPath()
        {
            Wall(Vector2.zero, Vector2.one * 3f);
            var navigation = new EnemyPathfinder2D(scene, 0.6f, ~0);
            var path = new List<Vector2>();
            Assert.IsFalse(navigation.FindPath(new Vector2(-5f, 0f), Vector2.zero, 1f, path));
            Assert.IsEmpty(path);
        }

        [Test]
        public void NavigationEscapesClearanceMarginAfterKnockbackAgainstWall()
        {
            Wall(Vector2.zero, new Vector2(1f, 4f));
            var navigation = new EnemyPathfinder2D(scene, 0.75f, ~0);
            Vector2 flush = new Vector2(-1.05f, 0f);
            Assert.IsTrue(navigation.SegmentClear(flush, new Vector2(-3f, 0f)));
            Assert.IsFalse(navigation.SegmentClear(flush, new Vector2(3f, 0f)));
            var path = new List<Vector2>();
            Assert.IsTrue(navigation.FindPath(flush, new Vector2(4f, 0f), 1.3f, path));
        }

        [Test]
        public void ChaserReachesPlayerOnOtherSideOfObstacle()
        {
            body.position = new Vector2(-4f, 0f);
            player.GetComponent<Rigidbody2D>().position = new Vector2(4f, 0f);
            Wall(Vector2.zero, new Vector2(1f, 4f));
            for (int i = 0; i < 400 && player.CurrentHealth == player.MaxHealth; i++)
            {
                Tick(0.02f);
                scene.GetPhysicsScene2D().Simulate(0.02f);
            }
            Assert.Less(player.CurrentHealth, player.MaxHealth, "Must route around the wall, reach the player and strike.");
            Assert.Greater(body.position.x, 1f);
        }
    }
}
