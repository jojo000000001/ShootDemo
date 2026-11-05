using UnityEngine;

namespace ShootDemo.Core
{
    /// <summary>Uses real time to resume a short freeze without undoing menu or pause state.</summary>
    [DisallowMultipleComponent]
    public sealed class HitStopController : MonoBehaviour
    {
        public static HitStopController Instance { get; private set; }
        public bool IsActive => remaining > 0f;
        private float remaining;
        private float resumeScale = 1f;
        private GameStateMachine stateMachine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
            BindStateMachine();
        }

        private void BindStateMachine()
        {
            if (stateMachine == GameStateMachine.Instance) return;
            if (stateMachine != null) stateMachine.StateChanged -= OnStateChanged;
            stateMachine = GameStateMachine.Instance;
            if (stateMachine != null) stateMachine.StateChanged += OnStateChanged;
        }

        public bool Request(float seconds)
        {
            BindStateMachine();
            if (!isActiveAndEnabled || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)
                || (stateMachine != null && stateMachine.CurrentState != GameState.Playing)) return false;
            if (!IsActive)
            {
                if (Time.timeScale <= 0f) return false;
                resumeScale = Time.timeScale;
            }
            remaining = Mathf.Max(remaining, seconds);
            Time.timeScale = 0f;
            return true;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        internal void Tick(float unscaledDeltaTime)
        {
            BindStateMachine();
            if (!IsActive) return;
            if (stateMachine != null)
            {
                if (stateMachine.CurrentState == GameState.Paused) return;
                if (stateMachine.CurrentState != GameState.Playing)
                {
                    Cancel();
                    return;
                }
            }
            remaining = Mathf.Max(0f, remaining - Mathf.Max(0f, unscaledDeltaTime));
            Time.timeScale = IsActive ? 0f : resumeScale;
        }

        private void OnStateChanged(GameState previous, GameState next)
        {
            if (!IsActive) return;
            if (next == GameState.Playing || next == GameState.Paused) Time.timeScale = 0f;
            else Cancel();
        }

        public void Cancel()
        {
            if (!IsActive) return;
            remaining = 0f;
            Time.timeScale = stateMachine != null && stateMachine.CurrentState == GameState.Paused
                ? 0f : resumeScale;
        }

        private void OnDisable()
        {
            Cancel();
            if (stateMachine != null) stateMachine.StateChanged -= OnStateChanged;
            stateMachine = null;
            if (Instance == this) Instance = null;
        }
    }
}
