using System.Reflection;
using NUnit.Framework;
using ShootDemo.Core;
using ShootDemo.Enemy;
using ShootDemo.Player;
using ShootDemo.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootDemo.Tests.EditMode
{
    public sealed class MatchSessionTests
    {
        private Scene scene;
        private GameStateMachine state;
        private EnemyRegistry registry;
        private EnemySpawner spawner;
        private PlayerHealth player;
        private MatchSession session;
        private GameConfig config;

        private GameObject Create(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static void Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            config = ScriptableObject.CreateInstance<GameConfig>();
            state = Create("State").AddComponent<GameStateMachine>();
            Invoke(state, "Awake");
            registry = Create("Registry").AddComponent<EnemyRegistry>();
            Invoke(registry, "Awake");
            player = Create("Player").AddComponent<PlayerHealth>();
            Set(player, "config", config);
            Invoke(player, "Awake");
            spawner = Create("Spawner").AddComponent<EnemySpawner>();
            Set(spawner, "config", config);
            Invoke(spawner, "Awake");
            session = Create("Session").AddComponent<MatchSession>();
            Set(session, "state", state);
            Set(session, "registry", registry);
            Set(session, "spawner", spawner);
            Set(session, "player", player);
            var data = new SerializedObject(session);
            var waves = data.FindProperty("waveCounts");
            waves.arraySize = 2;
            waves.GetArrayElementAtIndex(0).intValue = 1;
            waves.GetArrayElementAtIndex(1).intValue = 1;
            data.ApplyModifiedPropertiesWithoutUndo();
            Invoke(session, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            Invoke(session, "OnDestroy");
            Invoke(registry, "OnDestroy");
            Invoke(state, "OnDestroy");
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(config);
        }

        private void Spawned(int count) => typeof(EnemySpawner)
            .GetField("<TotalSpawned>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(spawner, count);

        [Test]
        public void EmptyArenaBeforeWaveFinishesDoesNotWin()
        {
            Invoke(session, "OnAliveChanged", 0);
            Assert.That(session.Outcome, Is.EqualTo(MatchOutcome.None));
            Assert.That(state.CurrentState, Is.EqualTo(GameState.Playing));
            Assert.That(session.WaveIndex, Is.Zero);
        }

        [Test]
        public void ClearingEachBudgetAdvancesThenWinsExactlyOnce()
        {
            Spawned(1);
            Invoke(session, "OnAliveChanged", 0);
            Assert.That(session.WaveIndex, Is.EqualTo(1));
            Assert.That(spawner.SpawnLimit, Is.EqualTo(2));
            Assert.That(session.Outcome, Is.EqualTo(MatchOutcome.None));
            Spawned(2);
            Invoke(session, "OnAliveChanged", 0);
            Invoke(session, "OnAliveChanged", 0);
            Assert.That(session.Outcome, Is.EqualTo(MatchOutcome.Victory));
            Assert.That(state.CurrentState, Is.EqualTo(GameState.GameOver));
        }

        [Test]
        public void PlayerDeathRecordsDefeatBeforeGameOverScreen()
        {
            bool observed = false;
            state.StateChanged += (_, next) =>
            {
                if (next == GameState.GameOver) observed = session.Outcome == MatchOutcome.Defeat;
            };
            Assert.That(player.TryTakeDamage(player.MaxHealth), Is.True);
            Assert.That(observed, Is.True);
            Assert.That(session.Outcome, Is.EqualTo(MatchOutcome.Defeat));
        }

        [Test]
        public void ManualEndDoesNotAwardVictory()
        {
            state.TryChangeState(GameState.Paused);
            state.TryChangeState(GameState.GameOver);
            Assert.That(session.Outcome, Is.EqualTo(MatchOutcome.Ended));
        }

        [Test]
        public void KillsCountOnceOnDeathAndNotOnOrdinaryDespawn()
        {
            var go = Create("Enemy");
            var health = go.AddComponent<EnemyHealth>();
            Invoke(health, "Awake");
            var life = go.AddComponent<EnemyLifecycle>();
            Invoke(life, "Awake");
            Invoke(life, "OnEnable");
            life.Despawn();
            Assert.That(session.DefeatedCount, Is.Zero);
            go.SetActive(true);
            Invoke(life, "OnEnable");
            Assert.That(health.TryTakeDamage(health.MaxHealth), Is.True);
            life.Despawn();
            Assert.That(session.DefeatedCount, Is.EqualTo(1));
        }

        [Test]
        public void PauseDoesNotAdvanceAnAlreadySpawnedWave()
        {
            Spawned(1);
            state.TryChangeState(GameState.Paused);
            Invoke(session, "OnAliveChanged", 0);
            Assert.That(session.WaveIndex, Is.Zero);
            Assert.That(session.Outcome, Is.EqualTo(MatchOutcome.None));
        }
    }
}
