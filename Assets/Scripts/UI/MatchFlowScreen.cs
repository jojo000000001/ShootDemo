using System;
using System.Reflection;
using ShootDemo.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ShootDemo.UI
{
    public enum MatchFlowAction
    {
        Start,
        Pause,
        Resume,
        End,
        Restart,
        BackToMenu
    }

    /// <summary>
    /// Start, pause, end, and restart screens. Buttons only request transitions the state machine allows.
    /// Restart and return-to-menu reload the scene so the next match does not keep a dead player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchFlowScreen : MonoBehaviour
    {
        [SerializeField] private GameStateMachine stateMachine;
        [SerializeField] private GameObject overlay;
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private GameObject pauseButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button pauseControl;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button endButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button menuButton;

        private void Awake()
        {
            if (stateMachine == null)
            {
                stateMachine = GameStateMachine.Instance != null
                    ? GameStateMachine.Instance
                    : FindObjectOfType<GameStateMachine>();
            }

        }

        private void OnEnable()
        {
            if (startButton != null) startButton.onClick.AddListener(StartMatch);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
            if (pauseControl != null) pauseControl.onClick.AddListener(PauseMatch);
            if (resumeButton != null) resumeButton.onClick.AddListener(ResumeMatch);
            if (endButton != null) endButton.onClick.AddListener(EndMatch);
            if (restartButton != null) restartButton.onClick.AddListener(RestartMatch);
            if (menuButton != null) menuButton.onClick.AddListener(ReturnToMenu);
            if (stateMachine != null)
            {
                stateMachine.StateChanged += OnStateChanged;
            }
        }

        private void Start()
        {
            if (stateMachine != null)
            {
                Show(stateMachine.CurrentState);
            }
        }

        private void OnDisable()
        {
            if (startButton != null) startButton.onClick.RemoveListener(StartMatch);
            if (quitButton != null) quitButton.onClick.RemoveListener(QuitGame);
            if (pauseControl != null) pauseControl.onClick.RemoveListener(PauseMatch);
            if (resumeButton != null) resumeButton.onClick.RemoveListener(ResumeMatch);
            if (endButton != null) endButton.onClick.RemoveListener(EndMatch);
            if (restartButton != null) restartButton.onClick.RemoveListener(RestartMatch);
            if (menuButton != null) menuButton.onClick.RemoveListener(ReturnToMenu);
            if (stateMachine != null)
            {
                stateMachine.StateChanged -= OnStateChanged;
            }
        }

        private void Update()
        {
            if (stateMachine == null || !Input.GetKeyDown(KeyCode.Escape))
            {
                return;
            }

            if (stateMachine.CurrentState == GameState.Playing)
            {
                PauseMatch();
            }
            else if (stateMachine.CurrentState == GameState.Paused)
            {
                ResumeMatch();
            }
        }

        public void StartMatch()
        {
            Perform(MatchFlowAction.Start);
        }

        public void QuitGame()
        {
            if (stateMachine != null && stateMachine.CurrentState != GameState.Menu)
            {
                return;
            }

            Type editor = Type.GetType("UnityEditor.EditorApplication, UnityEditor");
            if (editor != null)
            {
                PropertyInfo playing = editor.GetProperty("isPlaying");
                if (playing != null)
                {
                    playing.SetValue(null, false, null);
                    return;
                }
            }

            Application.Quit();
        }

        public void PauseMatch()
        {
            Perform(MatchFlowAction.Pause);
        }

        public void ResumeMatch()
        {
            Perform(MatchFlowAction.Resume);
        }

        public void EndMatch()
        {
            Perform(MatchFlowAction.End);
        }

        public void RestartMatch()
        {
            Perform(MatchFlowAction.Restart);
        }

        public void ReturnToMenu()
        {
            Perform(MatchFlowAction.BackToMenu);
        }

        public static bool ReloadsScene(MatchFlowAction action)
        {
            return action == MatchFlowAction.Restart || action == MatchFlowAction.BackToMenu;
        }

        public static bool IsActionLegal(GameState from, MatchFlowAction action)
        {
            switch (action)
            {
                case MatchFlowAction.Start:
                    return from == GameState.Menu;
                case MatchFlowAction.Pause:
                    return from == GameState.Playing;
                case MatchFlowAction.Resume:
                    return from == GameState.Paused;
                case MatchFlowAction.End:
                    return from == GameState.Paused;
                case MatchFlowAction.Restart:
                case MatchFlowAction.BackToMenu:
                    return from == GameState.GameOver;
                default:
                    return false;
            }
        }

        public static bool TryGetTarget(MatchFlowAction action, out GameState target)
        {
            switch (action)
            {
                case MatchFlowAction.Start:
                case MatchFlowAction.Resume:
                case MatchFlowAction.Restart:
                    target = GameState.Playing;
                    return true;
                case MatchFlowAction.Pause:
                    target = GameState.Paused;
                    return true;
                case MatchFlowAction.End:
                    target = GameState.GameOver;
                    return true;
                case MatchFlowAction.BackToMenu:
                    target = GameState.Menu;
                    return true;
                default:
                    target = GameState.Menu;
                    return false;
            }
        }

        private void Perform(MatchFlowAction action)
        {
            if (stateMachine == null || !IsActionLegal(stateMachine.CurrentState, action))
            {
                return;
            }

            if (!TryGetTarget(action, out GameState target) || !stateMachine.CanChangeState(target))
            {
                return;
            }

            if (ReloadsScene(action))
            {
                GameStateMachine.ReloadInto(target);
                return;
            }

            stateMachine.TryChangeState(target);
        }

        private void OnStateChanged(GameState previous, GameState next)
        {
            Show(next);
        }

        private void Show(GameState state)
        {
            bool playing = state == GameState.Playing;
            if (overlay != null) overlay.SetActive(!playing);
            if (pauseButton != null) pauseButton.SetActive(playing);
            if (menuPanel != null) menuPanel.SetActive(state == GameState.Menu);
            if (pausePanel != null) pausePanel.SetActive(state == GameState.Paused);
            if (gameOverPanel != null) gameOverPanel.SetActive(state == GameState.GameOver);
        }

    }
}
