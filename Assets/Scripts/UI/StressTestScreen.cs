using System;
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

        private const int MetricWindowSize = 600;
        private const float MetricWindowSeconds = 5f;
        private const float HudRefreshInterval = 0.25f;
        private readonly float[] frameTimes = new float[MetricWindowSize];
        private readonly float[] frameTimestamps = new float[MetricWindowSize];
        private readonly float[] sortedFrameTimes = new float[MetricWindowSize];
        private int frameReadIndex;
        private int frameWriteIndex;
        private int frameSampleCount;
        private float sampledSeconds;
        private float nextHudRefresh;
        private long previousAllocatedBytes;
        private long allocatedBytesThisFrame;

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
            previousAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            Refresh();
        }

        private void Update()
        {
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            allocatedBytesThisFrame = Math.Max(0L, allocatedBytes - previousAllocatedBytes);
            previousAllocatedBytes = allocatedBytes;

            float frameTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            while (frameSampleCount > 0 && (frameSampleCount == MetricWindowSize ||
                   Time.unscaledTime - frameTimestamps[frameReadIndex] > MetricWindowSeconds))
            {
                sampledSeconds -= frameTimes[frameReadIndex];
                frameReadIndex = (frameReadIndex + 1) % MetricWindowSize;
                frameSampleCount--;
            }

            frameTimes[frameWriteIndex] = frameTime;
            frameTimestamps[frameWriteIndex] = Time.unscaledTime;
            frameWriteIndex = (frameWriteIndex + 1) % MetricWindowSize;
            frameSampleCount++;
            sampledSeconds += frameTime;

            if (controller == null || !controller.IsActive || Time.unscaledTime < nextHudRefresh) return;
            nextHudRefresh = Time.unscaledTime + HudRefreshInterval;
            UpdateStatus();
        }
        private void Select100() { controller.SelectTier(100); }
        private void Select500() { controller.SelectTier(500); }
        private void Select1000() { controller.SelectTier(1000); }
        private void Exit() { GameStateMachine.ReloadInto(GameState.Menu); }
        private void OnState(GameState previous, GameState next) { Refresh(); }
        private void Refresh()
        {
            controls.SetActive(controller.IsActive && (state.CurrentState == GameState.Playing || state.CurrentState == GameState.Paused));
            if (!controller.IsActive || Time.unscaledTime >= nextHudRefresh)
            {
                nextHudRefresh = Time.unscaledTime + HudRefreshInterval;
                UpdateStatus();
            }
            tier100.interactable = controller.TargetCount != 100;
            tier500.interactable = controller.TargetCount != 500;
            tier1000.interactable = controller.TargetCount != 1000;
        }

        private void UpdateStatus()
        {
            status.text = controller.IsActive
                ? BuildPerformanceStatus()
                : $"压测：{controller.AliveCount} / {controller.TargetCount}";
        }

        private string BuildPerformanceStatus()
        {
            if (frameSampleCount == 0) return $"压测：{controller.AliveCount} / {controller.TargetCount}\n性能采样中…";

            for (int i = 0; i < frameSampleCount; i++)
                sortedFrameTimes[i] = frameTimes[(frameReadIndex + i) % MetricWindowSize];
            Array.Sort(sortedFrameTimes, 0, frameSampleCount);
            int slowFrameCount = Mathf.Max(1, Mathf.CeilToInt(frameSampleCount * 0.01f));
            float slowSeconds = 0f;
            for (int i = frameSampleCount - slowFrameCount; i < frameSampleCount; i++)
                slowSeconds += sortedFrameTimes[i];

            float averageFps = frameSampleCount / Mathf.Max(sampledSeconds, 0.0001f);
            float onePercentLow = slowFrameCount / Mathf.Max(slowSeconds, 0.0001f);
            return $"压测：{controller.AliveCount} / {controller.TargetCount}\n" +
                   $"FPS 均值 {averageFps:0.0}  |  1% low {onePercentLow:0.0}\n" +
                   $"GC Alloc {allocatedBytesThisFrame:N0} B/frame";
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
