using ShootDemo.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ShootDemo.UI
{
    public sealed class StressTestScreen : MonoBehaviour
    {
        [SerializeField] private Button entry;
        [SerializeField] private Button tier100;
        [SerializeField] private Button tier500;
        [SerializeField] private Button tier1000;
        [SerializeField] private Button exit;
        [SerializeField] private GameObject controls;
        [SerializeField] private Text status;
        private StressTestController controller;
        private GameStateMachine state;

        private void Start()
        {
            controller = FindObjectOfType<StressTestController>();
            state = GameStateMachine.Instance;
            entry.onClick.AddListener(Select100);
            tier100.onClick.AddListener(Select100);
            tier500.onClick.AddListener(Select500);
            tier1000.onClick.AddListener(Select1000);
            exit.onClick.AddListener(Exit);
            controller.Changed += Refresh;
            state.StateChanged += OnState;
            Refresh();
        }
        private void Select100() { controller.SelectTier(100); }
        private void Select500() { controller.SelectTier(500); }
        private void Select1000() { controller.SelectTier(1000); }
        private void Exit() { GameStateMachine.ReloadInto(GameState.Menu); }
        private void OnState(GameState previous, GameState next) { Refresh(); }
        private void Refresh()
        {
            controls.SetActive(controller.IsActive && (state.CurrentState == GameState.Playing || state.CurrentState == GameState.Paused));
            status.text = $"压测：{controller.AliveCount} / {controller.TargetCount}";
            tier100.interactable = controller.TargetCount != 100;
            tier500.interactable = controller.TargetCount != 500;
            tier1000.interactable = controller.TargetCount != 1000;
        }
        private void OnDestroy()
        {
            if (controller != null) controller.Changed -= Refresh;
            if (state != null) state.StateChanged -= OnState;
            entry.onClick.RemoveListener(Select100);
            tier100.onClick.RemoveListener(Select100);
            tier500.onClick.RemoveListener(Select500);
            tier1000.onClick.RemoveListener(Select1000);
            exit.onClick.RemoveListener(Exit);
        }
    }
}
