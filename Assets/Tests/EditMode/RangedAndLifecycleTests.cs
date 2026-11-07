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
    public sealed class RangedAndLifecycleTests
    {
        private Scene scene;
        private GameConfig config;
        private RangedEnemy enemy;
        private EnemyHealth health;
        private EnemyHitFeedback feedback;
        private EnemyLifecycle lifecycle;
        private EnemyRegistry registry;
        private PlayerHealth player;
        private Rigidbody2D body;
        private float savedTimeScale;
        private Sprite testSprite;

        [SetUp]
        public void SetUp()
        {
            savedTimeScale = Time.timeScale;
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.combatFeedback.hitStopDuration = 0f;
            config.playerHealth.hurtInvulnerability = 0f;
            registry = Create("Registry", Vector2.zero).AddComponent<EnemyRegistry>();
            Invoke(registry, "Awake");
            var actor = Create("Ranged", Vector2.zero);
            body = actor.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            actor.AddComponent<CircleCollider2D>().radius = 0.5f;
            health = actor.AddComponent<EnemyHealth>();
            feedback = actor.AddComponent<EnemyHitFeedback>();
            SetObject(feedback, "config", config);
            enemy = actor.AddComponent<RangedEnemy>();
            SetObject(enemy, "config", config);
            var warning = new GameObject("Warning");
            warning.transform.SetParent(actor.transform, false);
            var line = warning.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            SetObject(enemy, "warning", line);
            lifecycle = actor.AddComponent<EnemyLifecycle>();
            SetObject(lifecycle, "config", config);
            Initialize(actor);
            var target = Create("Player", Vector2.right * 7f);
            target.AddComponent<Rigidbody2D>().gravityScale = 0f;
            target.AddComponent<CircleCollider2D>().radius = 0.3f;
            player = target.AddComponent<PlayerHealth>();
            SetObject(player, "config", config);
            Invoke(player, "Awake");
            enemy.SetTarget(player);
        }

        private void Initialize(GameObject actor)
        {
            Invoke(actor.GetComponent<EnemyHealth>(), "Awake");
            Invoke(actor.GetComponent<EnemyHealth>(), "OnEnable");
            var hitFeedback = actor.GetComponent<EnemyHitFeedback>();
            Invoke(hitFeedback, "Awake");
            Invoke(hitFeedback, "OnEnable");
            var ai = actor.GetComponent<RangedEnemy>();
            Invoke(ai, "Awake");
            Invoke(ai, "OnEnable");
            var life = actor.GetComponent<EnemyLifecycle>();
            Invoke(life, "Awake");
            Invoke(life, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var life in root.GetComponentsInChildren<EnemyLifecycle>(true)) Invoke(life, "OnDisable");
                foreach (var ai in root.GetComponentsInChildren<RangedEnemy>(true)) Invoke(ai, "OnDisable");
                foreach (var hit in root.GetComponentsInChildren<EnemyHitFeedback>(true)) Invoke(hit, "OnDisable");
                var state = root.GetComponent<GameStateMachine>();
                if (state != null) Invoke(state, "OnDestroy");
            }
            Invoke(registry, "OnDestroy");
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(config);
            if (testSprite != null) Object.DestroyImmediate(testSprite);
            testSprite = null;
            Time.timeScale = savedTimeScale;
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

        private void Tick(float seconds) { Invoke(enemy, "Tick", seconds); }

        private void Wall(Vector2 position, Vector2 size)
        {
            Create("Wall", position).AddComponent<BoxCollider2D>().size = size;
        }

        private GameStateMachine StateMachine()
        {
            var state = Create("State", Vector2.zero).AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            return state;
        }

        private EnemyAttackVisibility Presentation(out Camera camera, out SpriteRenderer sprite)
        {
            var visual = new GameObject("outuzhe_body");
            visual.transform.SetParent(enemy.transform, false);
            sprite = visual.AddComponent<SpriteRenderer>();
            testSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            sprite.sprite = testSprite;
            camera = Create("GameplayCamera", Vector2.zero).AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 1f;
            var gate = enemy.GetComponent<EnemyAttackVisibility>();
            SetObject(gate, "bodyRenderer", sprite);
            SetObject(gate, "viewCamera", camera);
            Invoke(gate, "Awake");
            Invoke(gate, "OnEnable");
            return gate;
        }

        private void ArmVisibility(EnemyAttackVisibility gate)
        {
            for (int frame = 0; frame < 12; frame++) Invoke(gate, "TickVisibility", 0.02f, frame);
            Assert.IsTrue(gate.CanAttack);
        }

        [Test]
        public void CameraRapidlyApproachingOffscreenEnemyRequiresBodyVisibleBeforeWarning()
        {
            var gate = Presentation(out var camera, out _);
            // Player and the future warning endpoint are inside the picture; the enemy body is outside.
            camera.transform.position = new Vector3(7f, 0f, -10f);
            for (int frame = 0; frame < 30; frame++)
            {
                Invoke(gate, "TickVisibility", 0.02f, frame);
                Tick(0.02f);
                Assert.IsFalse(enemy.IsWarningVisible);
                Assert.AreEqual(0, enemy.ShotsFired);
            }
            camera.transform.position = new Vector3(0f, 0f, -10f);
            Invoke(gate, "TickVisibility", 0.5f, 30);
            Tick(0.02f);
            Assert.IsFalse(gate.CanAttack, "Time spent outside the picture must not arm an attack.");
            Assert.IsFalse(enemy.IsWarningVisible);
            for (int frame = 31; frame < 40; frame++)
            {
                Invoke(gate, "TickVisibility", 0.02f, frame);
                Tick(0.02f);
                Assert.IsFalse(enemy.IsWarningVisible);
            }
            Invoke(gate, "TickVisibility", 0.02f, 40);
            Invoke(gate, "TickVisibility", 0.02f, 41);
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Windup, enemy.State);
            Assert.IsTrue(enemy.IsWarningVisible);
            Tick(config.enemy.rangedWindup * 0.8f);
            Assert.AreEqual(RangedEnemyState.Windup, enemy.State, "Newly visible enemies still give the complete windup.");
        }

        [Test]
        public void LeavingGameplayCameraCancelsWarningEvenWithoutAnotherAiDecision()
        {
            var gate = Presentation(out var camera, out _);
            ArmVisibility(gate);
            Tick(0.02f);
            Assert.IsTrue(enemy.IsWarningVisible);
            enemy.SetDecisionSchedule(true, false);
            camera.transform.position = new Vector3(7f, 0f, -10f);
            Invoke(gate, "TickVisibility", 0.02f, 12);
            Assert.IsFalse(enemy.IsWarningVisible);
            Assert.IsFalse(enemy.IsAttacking);
            Assert.AreEqual(0, enemy.ShotsFired);
        }

        [TestCase("disabled")]
        [TestCase("transparent")]
        [TestCase("layer")]
        [TestCase("forceHidden")]
        [TestCase("cameraDisabled")]
        public void HiddenBodyCannotBeArmedByVisibleWarningRenderer(string reason)
        {
            var gate = Presentation(out var camera, out var sprite);
            ArmVisibility(gate);
            Tick(0.02f);
            Assert.IsTrue(enemy.IsWarningVisible);
            if (reason == "disabled") sprite.enabled = false;
            if (reason == "transparent") sprite.color = Color.clear;
            if (reason == "layer") camera.cullingMask = 0;
            if (reason == "forceHidden") sprite.forceRenderingOff = true;
            if (reason == "cameraDisabled") camera.enabled = false;
            Assert.IsFalse(gate.CanAttack);
            Tick(0.02f);
            Assert.IsFalse(enemy.IsWarningVisible);
            Assert.IsFalse(enemy.IsAttacking);
        }

        [Test]
        public void VisibilityNeedsDistinctFramesAndPoolReuseResetsGracePeriod()
        {
            var gate = Presentation(out _, out _);
            for (int i = 0; i < 100; i++) Invoke(gate, "TickVisibility", 1f, 0);
            Assert.IsFalse(gate.CanAttack, "Multiple fixed steps before a rendered frame cannot arm attacks.");
            for (int i = 1; i < 12; i++) Invoke(gate, "TickVisibility", 0.02f, i);
            Assert.IsTrue(gate.CanAttack);
            Invoke(gate, "OnDisable");
            Invoke(gate, "OnEnable");
            Assert.IsFalse(gate.CanAttack);
        }

        [Test]
        public void PauseDoesNotCountTowardVisibilityGraceOrCancelAnArmedAttack()
        {
            var gate = Presentation(out _, out _);
            var state = StateMachine();
            Invoke(gate, "TickVisibility", 0.02f, 0);
            state.TryChangeState(GameState.Paused);
            for (int frame = 1; frame < 100; frame++) Invoke(gate, "TickVisibility", 1f, frame);
            Assert.IsFalse(gate.CanAttack);
            state.TryChangeState(GameState.Playing);
            for (int frame = 100; frame < 111; frame++) Invoke(gate, "TickVisibility", 0.02f, frame);
            Tick(0.02f);
            Assert.IsTrue(enemy.IsWarningVisible);
            state.TryChangeState(GameState.Paused);
            Invoke(gate, "TickVisibility", 1f, 111);
            Assert.AreEqual(RangedEnemyState.Windup, enemy.State);
        }

        [Test]
        public void ReenableRebuildsLostNavigationAndBrainReference()
        {
            Invoke(enemy, "OnDisable");
            typeof(RangedEnemy).GetField("navigation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(enemy, null);
            typeof(EnemyLifecycle).GetField("<Brain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(lifecycle, null);
            Invoke(enemy, "OnEnable");
            Invoke(lifecycle, "OnEnable");
            enemy.SetTarget(player);
            Assert.AreSame(enemy, lifecycle.Brain);
            Assert.DoesNotThrow(() => Tick(0.02f));
            Assert.AreEqual(RangedEnemyState.Windup, enemy.State);
        }

        [Test]
        public void InDistanceBandShowsWarningAndStopsDuringWindup()
        {
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Windup, enemy.State);
            Assert.IsTrue(enemy.IsWarningVisible);
            Assert.AreEqual(Vector2.zero, body.velocity);
            Tick(config.enemy.rangedWindup * 0.8f);
            Assert.AreEqual(0, enemy.ShotsFired);
            Assert.AreEqual(RangedEnemyState.Windup, enemy.State);
        }

        [Test]
        public void EntranceRetreatDoesNotCancelEveryAttackCycle()
        {
            var passage = Create("Passage", Vector2.zero).AddComponent<EnemyNarrowPassage>();
            var data = new SerializedObject(passage);
            data.FindProperty("interior").rectValue = new Rect(-1.2f, -1f, 2.4f, 20f);
            data.FindProperty("outside").vector2Value = new Vector2(0f, -2.3f);
            data.FindProperty("entrance").vector2Value = Vector2.zero;
            data.ApplyModifiedPropertiesWithoutUndo();
            Invoke(passage, "OnEnable");
            body.position = new Vector2(0f, 0.5f);
            body.rotation = 90f;
            player.transform.position = Vector2.up * 4f;
            Wall(new Vector2(-1.35f, 8f), new Vector2(0.3f, 18f));
            Wall(new Vector2(1.35f, 8f), new Vector2(0.3f, 18f));
            Physics2D.SyncTransforms();
            int windups = 0;
            var previous = enemy.State;
            try
            {
                for (int i = 0; i < 600; i++)
                {
                    enemy.SetDecisionSchedule(true, i % 5 == 0);
                    Tick(0.02f);
                    scene.GetPhysicsScene2D().Simulate(0.02f);
                    if (previous != RangedEnemyState.Windup && enemy.State == RangedEnemyState.Windup) windups++;
                    previous = enemy.State;
                }
                Assert.GreaterOrEqual(windups, 2, "A short passage must permit repeated attacks even when the preferred 7m range is unavailable.");
                Assert.GreaterOrEqual(body.position.y, -0.2f, "Retreat must not send the actor back into entrance guidance.");
                Assert.IsFalse(enemy.UsingPassageEntrance);
            }
            finally { Invoke(passage, "OnDisable"); }
        }

        [Test]
        public void TooCloseRetreatsAndTooFarApproaches()
        {
            player.transform.position = Vector2.right * 3f;
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Retreating, enemy.State);
            Assert.Less(body.velocity.x, 0f);
            player.transform.position = Vector2.right * 10f;
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Approaching, enemy.State);
            Assert.Greater(body.velocity.x, 0f);
        }

        [Test]
        public void BlockedSightRoutesAroundWallInsteadOfStartingAttack()
        {
            Wall(Vector2.right * 3f, new Vector2(0.1f, 4f));
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Approaching, enemy.State);
            Assert.IsFalse(enemy.IsWarningVisible);
            Assert.AreNotEqual(Vector2.zero, body.velocity);
        }

        [Test]
        public void DistanceThresholdsDoNotImmediatelyReverseMovement()
        {
            player.transform.position = Vector2.right * 6.2f;
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Retreating, enemy.State);
            player.transform.position = Vector2.right * 6.4f;
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Retreating, enemy.State, "Finish backing up to the preferred distance.");
            player.transform.position = Vector2.right * 10f;
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Approaching, enemy.State);
            player.transform.position = Vector2.right * 7.5f;
            Tick(0.02f);
            Assert.AreEqual(RangedEnemyState.Approaching, enemy.State, "Finish approaching before stopping to attack.");
        }

        [TestCase(1f, -2f, 8f, 3f, false)]
        [TestCase(2.5f, 0f, 4f, 7f, false)]
        [TestCase(1f, -2f, 8f, 3f, true)]
        [TestCase(2.5f, 0f, 4f, 7f, true)]
        public void RoutingAroundObstacleDoesNotOscillateBetweenApproachingAndRetreating(
            float wallX, float wallY, float wallHeight, float playerX, bool scheduled)
        {
            player.transform.position = Vector2.right * playerX;
            Wall(new Vector2(wallX, wallY), new Vector2(0.3f, wallHeight));
            var previous = enemy.State;
            int reversals = 0;
            int windups = 0;
            for (int i = 0; i < 600; i++)
            {
                if (scheduled) enemy.SetDecisionSchedule(true, i % 5 == 0);
                Tick(0.02f);
                scene.GetPhysicsScene2D().Simulate(0.02f);
                if ((previous == RangedEnemyState.Approaching && enemy.State == RangedEnemyState.Retreating)
                    || (previous == RangedEnemyState.Retreating && enemy.State == RangedEnemyState.Approaching)) reversals++;
                if (previous != RangedEnemyState.Windup && enemy.State == RangedEnemyState.Windup) windups++;
                previous = enemy.State;
            }
            Assert.LessOrEqual(reversals, 2, "Routing and retreating must not undo each other every fixed step.");
            Assert.GreaterOrEqual(windups, 2, "Must find a firing position and resume repeated attacks.");
        }

        [Test]
        public void WindupLocksDirectionAndDamageCancelsWarning()
        {
            Tick(0.02f);
            var field = typeof(RangedEnemy).GetField("attackDirection", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector2 locked = (Vector2)field.GetValue(enemy);
            player.transform.position = Vector2.up * 7f;
            Tick(0.1f);
            Assert.AreEqual(locked, field.GetValue(enemy));
            health.TryTakeDamage(new DamageInfo(1f, body.position, Vector2.left));
            Assert.AreEqual(RangedEnemyState.Idle, enemy.State);
            Assert.IsFalse(enemy.IsWarningVisible);
            Vector2 knockback = body.velocity;
            Tick(1f);
            Assert.AreEqual(knockback, body.velocity);
            Assert.AreEqual(0, enemy.ShotsFired);
        }

        [Test]
        public void PauseFreezesAttackAndGameOverCannotFire()
        {
            var state = StateMachine();
            Tick(0.02f);
            state.TryChangeState(GameState.Paused);
            Tick(5f);
            Assert.AreEqual(RangedEnemyState.Windup, enemy.State);
            state.TryChangeState(GameState.GameOver);
            Tick(5f);
            Assert.AreEqual(0, enemy.ShotsFired);
        }

        [Test]
        public void DeathRemovesLiveEntryImmediatelyAndCancelsAttack()
        {
            int changes = 0;
            registry.AliveCountChanged += count => changes++;
            Tick(0.02f);
            Assert.AreEqual(1, registry.AliveCount);
            Assert.IsTrue(health.TryTakeDamage(100f));
            Assert.AreEqual(0, registry.AliveCount);
            Assert.IsFalse(enemy.enabled);
            Assert.IsFalse(enemy.IsWarningVisible);
            Assert.IsFalse(body.simulated);
            Assert.IsFalse(health.TryTakeDamage(1f));
            Assert.AreEqual(1, changes);
            Assert.IsTrue(lifecycle.gameObject.activeSelf, "Death pose must remain visible until cleanup.");
        }

        [Test]
        public void DeathCleanupWaitsForDelayAndPause()
        {
            var state = StateMachine();
            health.TryTakeDamage(100f);
            state.TryChangeState(GameState.Paused);
            Invoke(lifecycle, "Tick", 10f);
            Assert.IsTrue(lifecycle.gameObject.activeSelf);
            state.TryChangeState(GameState.Playing);
            Invoke(lifecycle, "Tick", config.enemy.deathHoldDuration * 0.8f);
            Assert.IsTrue(lifecycle.gameObject.activeSelf);
            Invoke(lifecycle, "Tick", config.enemy.deathHoldDuration);
            Assert.IsFalse(lifecycle.gameObject.activeSelf);
            Assert.AreEqual(0, registry.AliveCount);
        }

        [Test]
        public void DespawnLiveEnemyUnregistersOnlyOnce()
        {
            int changes = 0;
            registry.AliveCountChanged += count => changes++;
            lifecycle.Despawn();
            lifecycle.Despawn();
            Assert.AreEqual(0, registry.AliveCount);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void PoolReusesInstanceWithFullHealthAndCleanFeedback()
        {
            // Preview scenes need explicit lifecycle invocation; Play Mode verifies Unity's automatic ordering too.
            Invoke(lifecycle, "OnDisable");
            Invoke(enemy, "OnDisable");
            Invoke(feedback, "OnDisable");
            enemy.gameObject.SetActive(false);
            var first = registry.Spawn(lifecycle, new Vector2(-3f, 0f), Quaternion.identity, player);
            Initialize(first.gameObject);
            first.Health.TryTakeDamage(100f);
            Invoke(first, "Tick", 10f);
            Invoke(first, "OnDisable");
            Invoke(first.GetComponent<RangedEnemy>(), "OnDisable");
            Invoke(first.GetComponent<EnemyHitFeedback>(), "OnDisable");
            Assert.AreEqual(0, registry.PooledActiveCount(lifecycle));
            Assert.AreEqual(1, registry.PooledInactiveCount(lifecycle));
            var reused = registry.Spawn(lifecycle, new Vector2(-5f, 0f), Quaternion.identity, player);
            // Awake only runs once per instance, as in Play Mode.
            Invoke(reused.Health, "OnEnable");
            Invoke(reused.GetComponent<RangedEnemy>(), "OnEnable");
            Invoke(reused.GetComponent<EnemyHitFeedback>(), "OnEnable");
            Invoke(reused, "OnEnable");
            Assert.AreSame(first, reused);
            Assert.AreEqual(reused.Health.MaxHealth, reused.Health.CurrentHealth);
            Assert.IsTrue(reused.GetComponent<Rigidbody2D>().simulated);
            Assert.IsFalse(reused.GetComponent<EnemyHitFeedback>().IsKnockedBack);
            Assert.IsTrue(reused.GetComponent<RangedEnemy>().enabled);
            Assert.AreEqual(1, registry.AliveCount);
            Assert.AreEqual(1, registry.PooledActiveCount(lifecycle));
        }

        private EnemyProjectile Shot(Vector2 position, float speed = 400f, float lifetime = 3f)
        {
            var shot = Create("Shot", position).AddComponent<EnemyProjectile>();
            Invoke(shot, "Awake");
            shot.Launch(null, enemy.transform, Vector2.right, speed, 10f, lifetime);
            return shot;
        }

        [Test]
        public void FastProjectileCannotTunnelThroughThinWallToPlayer()
        {
            Wall(Vector2.right * 3f, new Vector2(0.025f, 5f));
            var shot = Shot(Vector2.zero);
            Invoke(shot, "Tick", 0.02f);
            Assert.IsFalse(shot.IsFlying);
            Assert.IsFalse(shot.gameObject.activeSelf);
            Assert.AreEqual(player.MaxHealth, player.CurrentHealth);
            Assert.Less(shot.transform.position.x, 3f);
        }

        [Test]
        public void ProjectileIgnoresOwnerOtherEnemiesAndTriggersThenDamagesPlayerOnce()
        {
            var ally = Create("Ally", Vector2.right * 2f);
            ally.AddComponent<BoxCollider2D>();
            var allyHealth = ally.AddComponent<EnemyHealth>();
            Invoke(allyHealth, "Awake");
            Create("Trigger", Vector2.right * 4f).AddComponent<BoxCollider2D>().isTrigger = true;
            var shot = Shot(Vector2.zero);
            Invoke(shot, "Tick", 0.02f);
            Invoke(shot, "Tick", 0.02f);
            Assert.AreEqual(player.MaxHealth - 10f, player.CurrentHealth);
            Assert.AreEqual(allyHealth.MaxHealth, allyHealth.CurrentHealth);
            Assert.IsFalse(shot.gameObject.activeSelf);
        }

        [Test]
        public void ProjectileCannotDamageInvulnerablePlayer()
        {
            config.playerHealth.hurtInvulnerability = 2f;
            player.TryTakeDamage(1f);
            var shot = Shot(Vector2.zero);
            Invoke(shot, "Tick", 0.02f);
            Assert.AreEqual(player.MaxHealth - 1f, player.CurrentHealth);
            Assert.IsFalse(shot.IsFlying);
        }

        [Test]
        public void ProjectilePauseFreezesPositionAndLifetime()
        {
            var state = StateMachine();
            var shot = Shot(Vector2.zero, 1f, 0.1f);
            state.TryChangeState(GameState.Paused);
            Invoke(shot, "Tick", 2f);
            Assert.IsTrue(shot.IsFlying);
            Assert.AreEqual(Vector3.zero, shot.transform.position);
            state.TryChangeState(GameState.Playing);
            Invoke(shot, "Tick", 0.2f);
            Assert.IsFalse(shot.IsFlying);
        }

        [Test]
        public void ProjectileDespawnAndReuseClearOldTrail()
        {
            var shot = Shot(Vector2.zero, 1f);
            var trail = shot.gameObject.AddComponent<TrailRenderer>();
            Invoke(shot, "Awake");
            trail.AddPosition(Vector3.zero);
            trail.AddPosition(Vector3.right);
            shot.OnDespawned();
            Assert.AreEqual(0, trail.positionCount);
            shot.OnSpawned();
            shot.Launch(null, enemy.transform, Vector2.up, 1f, 10f, 3f);
            Assert.AreEqual(0, trail.positionCount);
            Assert.IsTrue(trail.emitting);
        }
    }
}
