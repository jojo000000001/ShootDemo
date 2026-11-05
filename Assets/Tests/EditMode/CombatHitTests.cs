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
    public sealed class CombatHitTests
    {
        private Scene scene;
        private GameObject enemy;
        private EnemyHealth health;
        private EnemyHitFeedback feedback;
        private GameConfig config;
        private float timeScale;

        [SetUp]
        public void SetUp()
        {
            timeScale = Time.timeScale;
            Time.timeScale = 1f;
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            enemy = new GameObject("HitTestEnemy");
            SceneManager.MoveGameObjectToScene(enemy, scene);
            enemy.AddComponent<Rigidbody2D>().gravityScale = 0f;
            enemy.AddComponent<BoxCollider2D>().size = Vector2.one;
            health = enemy.AddComponent<EnemyHealth>();
            feedback = enemy.AddComponent<EnemyHitFeedback>();
            var data = new SerializedObject(feedback);
            data.FindProperty("config").objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
            Invoke(health, "Awake");
            Invoke(feedback, "Awake");
            Invoke(feedback, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            Invoke(feedback, "OnDisable");
            foreach (var root in scene.GetRootGameObjects())
            {
                var stop = root.GetComponent<HitStopController>();
                if (stop != null) Invoke(stop, "OnDisable");
                var state = root.GetComponent<GameStateMachine>();
                if (state != null) Invoke(state, "OnDestroy");
                Object.DestroyImmediate(root);
            }
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(config);
            Time.timeScale = timeScale;
        }

        private static object Invoke(MonoBehaviour component, string method, params object[] args)
        {
            return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, args);
        }

        private GameObject CreateObject(string name, Vector2 position)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            return go;
        }

        private Bullet Fire(Vector2 origin, Transform owner = null)
        {
            var go = CreateObject("TestBullet", origin);
            var bullet = go.AddComponent<Bullet>();
            Invoke(bullet, "Awake");
            bullet.Launch(null, owner, Vector2.right, 200f, 10f, 2f);
            Physics2D.SyncTransforms();
            Invoke(bullet, "Tick", 0.02f);
            return bullet;
        }

        [Test]
        public void Projectile_SweepsFastTravelAppliesOneDirectionalHitAndReleases()
        {
            DamageInfo received = default;
            int events = 0;
            health.Damaged += hit => { received = hit; events++; };
            var bullet = Fire(new Vector2(-2f, 0f));
            Assert.That(health.CurrentHealth, Is.EqualTo(20f));
            Assert.That(received.Amount, Is.EqualTo(10f));
            Assert.That(received.Direction, Is.EqualTo(Vector2.right));
            Assert.That(received.Point.x, Is.EqualTo(-0.5f).Within(0.02f));
            Assert.That(enemy.GetComponent<Rigidbody2D>().velocity.x, Is.GreaterThan(0f));
            Assert.That(bullet.gameObject.activeSelf, Is.False);
            Invoke(bullet, "Tick", 0.02f);
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void Projectile_ThinWallBeforeEnemyBlocksDamage()
        {
            var wall = CreateObject("ThinWall", new Vector2(-1f, 0f));
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.01f, 5f);
            var bullet = Fire(new Vector2(-2f, 0f));
            Assert.That(health.CurrentHealth, Is.EqualTo(30f));
            Assert.That(bullet.gameObject.activeSelf, Is.False);
            Assert.That(bullet.transform.position.x, Is.LessThan(-0.9f));
        }

        [Test]
        public void Projectile_IgnoresTriggersAndManyOwnerColliders()
        {
            var owner = CreateObject("Shooter", new Vector2(-2f, 0f));
            for (int i = 0; i < 20; i++) owner.AddComponent<CircleCollider2D>().radius = 0.2f;
            var trigger = CreateObject("Sensor", new Vector2(-1f, 0f));
            trigger.AddComponent<BoxCollider2D>().isTrigger = true;
            Fire(new Vector2(-2f, 0f), owner.transform);
            Assert.That(health.CurrentHealth, Is.EqualTo(20f));
        }

        [Test]
        public void Projectile_HitsNearestEnemyOnly()
        {
            var second = CreateObject("SecondEnemy", new Vector2(2f, 0f));
            second.AddComponent<BoxCollider2D>();
            var secondHealth = second.AddComponent<EnemyHealth>();
            Invoke(secondHealth, "Awake");
            Fire(new Vector2(-2f, 0f));
            Assert.That(health.CurrentHealth, Is.EqualTo(20f));
            Assert.That(secondHealth.CurrentHealth, Is.EqualTo(30f));
        }

        [Test]
        public void Knockback_RespectsWallAndStopsAfterItsDuration()
        {
            enemy.GetComponent<Rigidbody2D>().collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var wall = CreateObject("KnockbackWall", new Vector2(0.8f, 0f));
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.05f, 5f);
            health.TryTakeDamage(new DamageInfo(10f, Vector2.zero, Vector2.right));
            Physics2D.SyncTransforms();
            for (int i = 0; i < 15; i++)
            {
                Invoke(feedback, "FixedUpdate");
                scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
            }
            Assert.That(enemy.GetComponent<Collider2D>().bounds.max.x, Is.LessThan(0.79f));
            Assert.That(enemy.GetComponent<Rigidbody2D>().velocity, Is.EqualTo(Vector2.zero));
            Assert.That(feedback.IsKnockedBack, Is.False);
        }

        [Test]
        public void Enemy_OverkillReportsActualDamageAndDeathOnce()
        {
            int deaths = 0;
            float damage = 0f;
            health.Damaged += hit => damage = hit.Amount;
            health.Died += () => deaths++;
            Assert.That(health.TryTakeDamage(999f), Is.True);
            Assert.That(damage, Is.EqualTo(30f));
            Assert.That(health.CurrentHealth, Is.Zero);
            Assert.That(health.TryTakeDamage(10f), Is.False);
            Assert.That(deaths, Is.EqualTo(1));
        }

        [TestCase("bianyi_ganranzhe_sanjieduan")]
        [TestCase("outuzhe")]
        [TestCase("chushouguai")]
        public void EnemyPrefabs_ContainHealthPhysicsAndWhiteFlashMaterial(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyHealth>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyHitFeedback>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Collider2D>().isTrigger, Is.False);
            Assert.That(prefab.GetComponent<Rigidbody2D>().gravityScale, Is.Zero);
            Assert.That(prefab.GetComponentInChildren<Animator>().applyRootMotion, Is.False);
            var renderers = prefab.GetComponentsInChildren<SpriteRenderer>();
            Assert.That(System.Array.Exists(renderers, r => r.sharedMaterial.HasProperty("_FlashAmount")), Is.True);
        }

        [Test]
        public void Flash_UsesShaderWhiteThenRestoresPropertyBlock()
        {
            var visual = CreateObject("FlashSprite", Vector2.zero);
            visual.transform.SetParent(enemy.transform, false);
            var sprite = visual.AddComponent<SpriteRenderer>();
            sprite.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EnemyHitFlash.mat");
            Invoke(feedback, "OnDisable");
            Invoke(feedback, "Awake");
            Invoke(feedback, "OnEnable");
            health.TryTakeDamage(10f);
            var block = new MaterialPropertyBlock();
            sprite.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_FlashAmount"), Is.EqualTo(1f));
            Invoke(feedback, "OnDisable");
            sprite.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_FlashAmount"), Is.Zero);
        }

        private HitStopController CreateHitStop(out GameStateMachine state)
        {
            state = CreateObject("HitStopState", Vector2.zero).AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            var stop = CreateObject("HitStop", Vector2.zero).AddComponent<HitStopController>();
            Invoke(stop, "Awake");
            Invoke(stop, "OnEnable");
            return stop;
        }

        [Test]
        public void HitStop_UsesRealTimeAndExtendsWithoutStackingOrRestoringOverPause()
        {
            var stop = CreateHitStop(out var state);
            Assert.That(stop.Request(0.04f), Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Invoke(stop, "Tick", 0.02f);
            stop.Request(0.04f);
            Invoke(stop, "Tick", 0.02f);
            Assert.That(stop.IsActive, Is.True);
            state.TryChangeState(GameState.Paused);
            Invoke(stop, "Tick", 1f);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(stop.IsActive, Is.True);
            state.TryChangeState(GameState.Playing);
            Assert.That(Time.timeScale, Is.Zero);
            Invoke(stop, "Tick", 0.03f);
            Assert.That(stop.IsActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void HitStop_GameOverCancelsAndNeverLeavesTimeFrozen()
        {
            var stop = CreateHitStop(out var state);
            stop.Request(0.04f);
            state.TryChangeState(GameState.GameOver);
            Assert.That(stop.IsActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(stop.Request(0.04f), Is.False);
        }
    }
}
