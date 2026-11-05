using System.Reflection;
using NUnit.Framework;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShootDemo.Tests.EditMode
{
    public sealed class PlayerHealthTests
    {
        private Scene scene;
        private GameObject player;
        private GameConfig config;
        private PlayerMovement movement;
        private PlayerDash dash;
        private PlayerHealth health;
        private PlayerHealthFeedback feedback;
        private SpriteRenderer sprite;
        private Material material;
        private float originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            originalTimeScale = Time.timeScale;
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            player = new GameObject("HealthTestPlayer");
            player.SetActive(false);
            SceneManager.MoveGameObjectToScene(player, scene);
            player.AddComponent<Rigidbody2D>().gravityScale = 0f;
            movement = player.AddComponent<PlayerMovement>();
            dash = player.AddComponent<PlayerDash>();
            health = player.AddComponent<PlayerHealth>();
            feedback = player.AddComponent<PlayerHealthFeedback>();
            var child = new GameObject("Body");
            child.transform.SetParent(player.transform, false);
            sprite = child.AddComponent<SpriteRenderer>();
            sprite.color = new Color(0.5f, 0.7f, 0.9f, 0.6f);
            material = new Material(Shader.Find("Sprites/Default"));
            sprite.sharedMaterial = material;
            foreach (var component in new Component[] { movement, dash, health, feedback })
            {
                var serialized = new SerializedObject(component);
                serialized.FindProperty("config").objectReferenceValue = config;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            player.SetActive(true);
            Invoke(movement, "Awake");
            Invoke(dash, "Awake");
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
                var state = root.GetComponent<GameStateMachine>();
                if (state != null) Invoke(state, "OnDestroy");
                Object.DestroyImmediate(root);
            }
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(config);
            Time.timeScale = originalTimeScale;
        }

        private static object Invoke(MonoBehaviour component, string method, params object[] arguments)
        {
            return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, arguments);
        }

        private GameStateMachine CreateStateMachine()
        {
            var go = new GameObject("HealthTestState");
            SceneManager.MoveGameObjectToScene(go, scene);
            var state = go.AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            return state;
        }

        [Test]
        public void Damage_AppliesOnceAndRaisesHealthEvents()
        {
            int changed = 0;
            float reportedDamage = 0f;
            health.HealthChanged += (current, maximum) =>
            {
                changed++;
                Assert.That(current, Is.EqualTo(90f));
                Assert.That(maximum, Is.EqualTo(100f));
            };
            health.Damaged += amount => reportedDamage = amount;

            Assert.That(health.TryTakeDamage(10f), Is.True);
            Assert.That(health.TryTakeDamage(10f), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(90f));
            Assert.That(health.IsInvulnerable, Is.True);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(reportedDamage, Is.EqualTo(10f));
        }

        [Test]
        public void Damage_IsAcceptedAfterHurtProtectionExpires()
        {
            health.TryTakeDamage(10f);
            Invoke(health, "Tick", 0.49f);
            Assert.That(health.TryTakeDamage(10f), Is.False);
            Invoke(health, "Tick", 0.02f);
            Assert.That(health.TryTakeDamage(10f), Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(80f));
        }

        [Test]
        public void Damage_DodgeProtectionRejectsDamageWithoutRaisingEvents()
        {
            int events = 0;
            health.Damaged += _ => events++;
            dash.TryDash(Vector2.right);

            Assert.That(health.IsInvulnerable, Is.True);
            Assert.That(health.TryTakeDamage(999f), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
            Assert.That(events, Is.Zero);

            for (int i = 0; i < 12; i++) Invoke(movement, "FixedUpdate");
            Assert.That(dash.IsDashing, Is.True);
            Assert.That(health.IsInvulnerable, Is.False);
            Assert.That(health.TryTakeDamage(10f), Is.True);
        }

        [TestCase(0f)]
        [TestCase(-10f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Damage_InvalidAmountDoesNotChangeHealthOrProtection(float amount)
        {
            Assert.That(health.TryTakeDamage(amount), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
            Assert.That(health.IsInvulnerable, Is.False);
        }

        [Test]
        public void Death_ClampsHealthAndStopsActionsAndPhysicsExactlyOnce()
        {
            var state = CreateStateMachine();
            int deaths = 0;
            health.Died += () => deaths++;
            player.GetComponent<Rigidbody2D>().velocity = Vector2.right * 6f;

            Assert.That(health.TryTakeDamage(1000f), Is.True);
            Assert.That(health.CurrentHealth, Is.Zero);
            Assert.That(health.IsDead, Is.True);
            Assert.That(health.IsInvulnerable, Is.False);
            Assert.That(movement.MovementEnabled, Is.False);
            Assert.That(movement.enabled, Is.False);
            Assert.That(dash.enabled, Is.False);
            Assert.That(dash.TryDash(Vector2.up), Is.False);
            Assert.That(player.GetComponent<Rigidbody2D>().velocity, Is.EqualTo(Vector2.zero));
            Assert.That(player.GetComponent<Rigidbody2D>().simulated, Is.False);
            Assert.That(state.CurrentState, Is.EqualTo(GameState.GameOver));
            Assert.That(health.TryTakeDamage(1000f), Is.False);
            Assert.That(deaths, Is.EqualTo(1));
        }

        [Test]
        public void Damage_PauseRejectsHitsAndFreezesProtection()
        {
            var state = CreateStateMachine();
            health.TryTakeDamage(10f);
            state.TryChangeState(GameState.Paused);
            Invoke(health, "Tick", 10f);
            Assert.That(health.IsInvulnerable, Is.True);
            Assert.That(health.TryTakeDamage(10f), Is.False);
            state.TryChangeState(GameState.Playing);
            Invoke(health, "Tick", 0.51f);
            Assert.That(health.TryTakeDamage(10f), Is.True);
        }

        [Test]
        public void Feedback_HurtTintPreservesAnimatedSpriteColorAndRestoresPropertyBlock()
        {
            Color before = sprite.color;
            var originalBlock = new MaterialPropertyBlock();
            sprite.GetPropertyBlock(originalBlock);
            health.TryTakeDamage(10f);
            var block = new MaterialPropertyBlock();
            sprite.GetPropertyBlock(block);

            Assert.That(block.GetColor("_Color").g, Is.LessThan(1f));
            Assert.That(sprite.color, Is.EqualTo(before));
            Assert.That(sprite.enabled, Is.True);

            Invoke(feedback, "OnDisable");
            sprite.GetPropertyBlock(block);
            Assert.That(block.HasColor("_Color"), Is.EqualTo(originalBlock.HasColor("_Color")));
            Assert.That(sprite.color, Is.EqualTo(before));
        }

        [Test]
        public void Feedback_DodgeBlinksAndClearsAfterProtectionEnds()
        {
            dash.TryDash(Vector2.right);
            Invoke(feedback, "ApplyTint");
            var block = new MaterialPropertyBlock();
            sprite.GetPropertyBlock(block);
            Assert.That(block.GetColor("_Color").a, Is.EqualTo(config.playerHealth.blinkAlpha).Within(0.001f));

            typeof(PlayerHealthFeedback).GetField("blinkElapsed", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(feedback, config.playerHealth.blinkInterval + 0.01f);
            Invoke(feedback, "ApplyTint");
            sprite.GetPropertyBlock(block);
            Assert.That(block.GetColor("_Color").a, Is.EqualTo(1f).Within(0.001f));

            dash.CancelDash();
            Invoke(feedback, "ApplyTint");
            sprite.GetPropertyBlock(block);
            Assert.That(block.HasColor("_Color"), Is.False);
        }

        [Test]
        public void Feedback_DeathRestoresTintAndStaysVisible()
        {
            health.TryTakeDamage(10f);
            Invoke(health, "Tick", 0.51f);
            health.TryTakeDamage(1000f);
            var block = new MaterialPropertyBlock();
            sprite.GetPropertyBlock(block);
            Assert.That(block.HasColor("_Color"), Is.False);
            Assert.That(sprite.enabled, Is.True);
            Assert.That(sprite.color.a, Is.EqualTo(0.6f));
        }
    }
}
