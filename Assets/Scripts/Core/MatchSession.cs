using System;
using ShootDemo.Enemy;
using ShootDemo.Player;
using UnityEngine;

namespace ShootDemo.Core
{
    public enum MatchOutcome { None, Victory, Defeat, Ended }

    /// <summary>Finite waves share the existing spawner; completion requires all spawned foes to be gone.</summary>
    public sealed class MatchSession : MonoBehaviour
    {
        [SerializeField] private GameStateMachine state;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private EnemyRegistry registry;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private int[] waveCounts = { 100, 140, 180 };
        [Tooltip("Disable finite waves when using the unlimited stress-test spawner.")]
        [SerializeField] private bool finiteWaves = true;

        public int DefeatedCount { get; private set; }
        public int WaveIndex { get; private set; }
        public int WaveCount => waveCounts.Length;
        public MatchOutcome Outcome { get; private set; }
        public event Action Changed;

        private void Start()
        {
            if (state == null) state = GameStateMachine.Instance;
            if (registry == null) registry = EnemyRegistry.Instance;
            if (spawner == null) spawner = FindObjectOfType<EnemySpawner>();
            if (player == null) player = FindObjectOfType<PlayerHealth>();
            if (waveCounts == null || waveCounts.Length == 0) waveCounts = new[] { 100, 140, 180 };
            if (state == null || registry == null || spawner == null || player == null)
            {
                Debug.LogError("MatchSession needs state, registry, spawner and player references.", this);
                enabled = false;
                return;
            }
            state.StateChanging += OnStateChanging;
            state.StateChanged += OnStateChanged;
            registry.EnemyDefeated += OnDefeated;
            registry.AliveCountChanged += OnAliveChanged;
            spawner.Spawned += OnSpawned;
            Begin();
        }

        private void Begin()
        {
            DefeatedCount = 0;
            WaveIndex = 0;
            Outcome = MatchOutcome.None;
            spawner.SpawnLimit = finiteWaves ? Mathf.Max(1, waveCounts[0]) : 0;
            Changed?.Invoke();
        }

        private void OnStateChanging(GameState previous, GameState next)
        {
            if (next != GameState.GameOver || Outcome != MatchOutcome.None) return;
            Outcome = player.IsDead ? MatchOutcome.Defeat : MatchOutcome.Ended;
            Changed?.Invoke();
        }

        private void OnStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.Menu || (previous == GameState.GameOver && next == GameState.Playing)) Begin();
        }

        private void OnDefeated()
        {
            if (state.CurrentState != GameState.Playing || Outcome != MatchOutcome.None) return;
            DefeatedCount++;
            Changed?.Invoke();
        }

        private void OnSpawned(EnemyLifecycle enemy) { EvaluateWave(); }
        private void OnAliveChanged(int count) { EvaluateWave(); }

        private void EvaluateWave()
        {
            if (!finiteWaves || Outcome != MatchOutcome.None || state.CurrentState != GameState.Playing
                || !spawner.HasFinishedSpawning || registry.AliveCount != 0 || player.IsDead) return;
            if (WaveIndex + 1 < waveCounts.Length)
            {
                WaveIndex++;
                spawner.SpawnLimit += Mathf.Max(1, waveCounts[WaveIndex]);
                Changed?.Invoke();
                return;
            }
            Outcome = MatchOutcome.Victory;
            Changed?.Invoke();
            state.TryChangeState(GameState.GameOver);
        }

        private void OnDestroy()
        {
            if (state != null) { state.StateChanging -= OnStateChanging; state.StateChanged -= OnStateChanged; }
            if (registry != null) { registry.EnemyDefeated -= OnDefeated; registry.AliveCountChanged -= OnAliveChanged; }
            if (spawner != null) spawner.Spawned -= OnSpawned;
        }
    }
}
