using System;
using UnityEngine;

namespace ShootDemo.Core
{
    [DisallowMultipleComponent]
    public sealed class GameStateMachine : MonoBehaviour
    {
        private const float RunningTimeScale = 1f;
        private const float PausedTimeScale = 0f;

        [SerializeField] private GameState initialState = GameState.Playing;

        private static GameState? queuedInitialState;

        public static GameStateMachine Instance { get; private set; }

        public static void ReloadInto(GameState state)
        {
            queuedInitialState = state;
            if (Application.isPlaying)
            {
                Time.timeScale = 1f;
                UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                UnityEngine.SceneManagement.SceneManager.LoadScene(active.name);
            }
        }

        public GameState CurrentState { get; private set; }

        public event Action<GameState, GameState> StateChanging;
        public event Action<GameState, GameState> StateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (queuedInitialState.HasValue)
            {
                initialState = queuedInitialState.Value;
                queuedInitialState = null;
            }

            CurrentState = initialState;
            ApplyTimeScale(CurrentState);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;

            if (Application.isPlaying)
            {
                Time.timeScale = RunningTimeScale;
            }
        }

        public bool TryChangeState(GameState nextState)
        {
            if (nextState == CurrentState || !CanTransition(CurrentState, nextState))
            {
                return false;
            }

            GameState previousState = CurrentState;
            StateChanging?.Invoke(previousState, nextState);

            CurrentState = nextState;
            ApplyTimeScale(CurrentState);

            StateChanged?.Invoke(previousState, CurrentState);
            return true;
        }

        public bool CanChangeState(GameState nextState)
        {
            return nextState != CurrentState && CanTransition(CurrentState, nextState);
        }

        private static bool CanTransition(GameState from, GameState to)
        {
            switch (from)
            {
                case GameState.Menu:
                    return to == GameState.Playing;

                case GameState.Playing:
                    return to == GameState.Paused || to == GameState.GameOver || to == GameState.Menu;

                case GameState.Paused:
                    return to == GameState.Playing || to == GameState.GameOver || to == GameState.Menu;

                case GameState.GameOver:
                    return to == GameState.Playing || to == GameState.Menu;

                default:
                    return false;
            }
        }

        private static void ApplyTimeScale(GameState state)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Time.timeScale = state == GameState.Playing ? RunningTimeScale : PausedTimeScale;
        }
    }
}
