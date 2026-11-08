#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ShootDemo.Enemy;
using ShootDemo.Player;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace ShootDemo.Core
{
    /// <summary>Opt-in, repeatable player/editor capture. Absent from release builds.</summary>
    [DefaultExecutionOrder(30000)]
    public sealed class StressBenchmark : MonoBehaviour
    {
        private static readonly string[] CounterNames = {
            "Main Thread", "Render Thread", "PlayerLoop", "Camera.Render", "Gfx.PresentFrame",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "WaitForTargetFPS",
            "Canvas.BuildBatch", "Rendering.Batch", "GC.Collect", "GC Allocated In Frame",
            "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Physics2D.Simulate",
            "BehaviourUpdate", "FixedBehaviourUpdate", "Animation.Update", "Enemies.AI.Schedule",
            "Enemies.SpatialGrid.Update", "Enemies.SpatialGrid.Neighbors", "Enemies.FlowField.Integrate",
            "Enemies.FlowField.Sample", "Enemies.Melee.Tick", "Enemies.Melee.AttackVisibility",
            "Enemies.Melee.HitVisibility"
        };
        private const int Capacity = 131072;
        private const int MetricColumns = 15;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
        private ProfilerRecorder[] recorders;
        private double[][] rows;
        private string directory;
        private int[] tiers;
        private int repeats, trial, phase, count, fixedSteps, settlingFrames;
        private double phaseStart, fullSince = -1, sampleStart, previousTime, nextProgress;
        private float warmup, duration;
        private StressTestController controller;
        private PlayerHealth player;
        private int savedVsync, savedTarget;
        private bool savedBackground, savedProfiler, finished;
        private int TrialCount => repeats * tiers.Length * 2;
        private bool Uncached => (trial & 1) == (Repeat & 1);
        private int Tier => tiers[(trial / 2) % tiers.Length];
        private int Repeat => trial / (tiers.Length * 2);
        public string Progress => finished ? "complete" : "trial=" + trial + "/" + TrialCount + ", tier=" + Tier
            + ", uncached=" + Uncached + ", phase=" + phase + ", samples=" + count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void FromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-arena-benchmark")
                {
                    StartCapture(args[i + 1], ReadInt(args, "-arena-repeats", 2),
                        ReadInt(args, "-arena-seconds", 20), ReadInt(args, "-arena-warmup", 10));
                    break;
                }
        }

        private static int ReadInt(string[] args, string key, int fallback)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == key && int.TryParse(args[i + 1], out int value)) return Mathf.Max(1, value);
            return fallback;
        }

        public static StressBenchmark StartCapture(string outputDirectory, int repetitions = 2,
            float sampleSeconds = 20, float warmupSeconds = 10, int[] populations = null)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
            if (FindObjectOfType<StressBenchmark>() != null) throw new InvalidOperationException("Capture already running.");
            var go = new GameObject("Stress Benchmark (opt-in)");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<StressBenchmark>();
            capture.directory = Path.GetFullPath(outputDirectory);
            capture.repeats = Mathf.Max(1, repetitions);
            capture.warmup = Mathf.Max(1, warmupSeconds);
            capture.duration = Mathf.Max(1, sampleSeconds);
            capture.tiers = populations ?? new[] { 500, 1000 };
            foreach (int tier in capture.tiers)
                if (!StressTestController.IsValidTier(tier)) throw new ArgumentException("Invalid population.");
            capture.Initialize();
            return capture;
        }

        private void Initialize()
        {
            Directory.CreateDirectory(directory);
            savedVsync = QualitySettings.vSyncCount;
            savedTarget = Application.targetFrameRate;
            savedBackground = Application.runInBackground;
            savedProfiler = UnityEngine.Profiling.Profiler.enabled;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            // Same profiling overhead in A/B and editor/player captures. No deep profiling.
            UnityEngine.Profiling.Profiler.enabled = true;
            rows = new double[MetricColumns + CounterNames.Length * 2][];
            for (int i = 0; i < rows.Length; i++) rows[i] = new double[Capacity];
            File.WriteAllText(Path.Combine(directory, "environment.txt"),
                "StartedUTC=" + DateTime.UtcNow.ToString("O") + "\nUnity=" + Application.unityVersion
                + "\nPlatform=" + Application.platform + "\nDevelopment=" + Debug.isDebugBuild
                + "\nResolution=" + Screen.width + "x" + Screen.height + "\nGPU=" + SystemInfo.graphicsDeviceName
                + "\nGraphicsAPI=" + SystemInfo.graphicsDeviceType + "\nBatchMode=" + Application.isBatchMode + "\nCPU=" + SystemInfo.processorType
                + "\nQuality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]
                + "\nVSync=0\nTargetFrameRate=-1\nProfilerEnabled=" + UnityEngine.Profiling.Profiler.enabled
                + "\nFixedDeltaTime=" + Time.fixedDeltaTime + "\nMaximumDeltaTime=" + Time.maximumDeltaTime
                + "\nWarmupSeconds=" + warmup + "\nSampleSeconds=" + duration + "\nRepeats=" + repeats
                + "\nScenario=stationary, no input/shooting, invulnerable, normal brains, fresh scene per trial"
                + "\nOrder=paired A/B, reversed on odd repeats; identical seed 42000+repeat per pair"
                + "\nFrameMs=realtime Update-to-Update; recorder values describe previous completed frame"
                + "\nMarker counts are profiler samples (not necessarily physics-query counts); nested times overlap"
                + "\nMissing markers/timings are NaN, not zero. Counters may be unavailable on some platforms.");
            File.WriteAllText(Path.Combine(directory, "trials.txt"), "");
            BeginTrial();
        }

        private void BeginTrial()
        {
            phase = 0;
            phaseStart = Time.realtimeSinceStartupAsDouble;
            fullSince = -1;
            count = 0;
            controller = null;
            player = null;
            MeleeEnemy.BenchmarkUncachedAttackVisibility = Uncached;
            GameStateMachine.ReloadInto(GameState.Menu);
        }

        private void FixedUpdate() { fixedSteps++; }

        private void Update()
        {
            if (finished || rows == null) return;
            try { CaptureTick(); }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(directory, "error.txt"), error.ToString());
                Complete(false);
            }
        }

        private void CaptureTick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            FrameTimingManager.CaptureFrameTimings();
            if (phase < 2 && now >= nextProgress)
            {
                nextProgress = now + 2;
                File.WriteAllText(Path.Combine(directory, "progress.txt"), Progress + ", alive="
                    + (controller != null ? controller.AliveCount : -1) + ", state="
                    + (GameStateMachine.Instance != null ? GameStateMachine.Instance.CurrentState.ToString() : "none"));
            }
            if (phase < 2 && now - phaseStart > 180) throw new TimeoutException("Population/warmup timeout: " + Progress);
            if (phase == 0)
            {
                controller = FindObjectOfType<StressTestController>();
                player = FindObjectOfType<PlayerHealth>();
                if (controller == null || player == null) return;
                if (controller.TargetCount != Tier)
                {
                    // Start() on the freshly loaded controller must have completed.
                    UnityEngine.Random.InitState(42000 + Repeat);
                    Debug.Log("Benchmark selecting " + Tier);
                    controller.SelectTier(Tier);
                    Debug.Log("Benchmark selected " + controller.TargetCount);
                    if (controller.TargetCount != Tier) return;
                    // Remove interactive input as a source of benchmark variability.
                    var movement = player.GetComponent<PlayerMovement>();
                    if (movement != null)
                    {
                        movement.SetMovementEnabled(false, true);
                        movement.SetFacingEnabled(false);
                    }
                    foreach (var behaviour in player.GetComponents<MonoBehaviour>())
                        if (behaviour is PlayerWeapon || behaviour is PlayerDash || behaviour is PlayerAim) behaviour.enabled = false;
                }
                phase = 1;
            }
            if (phase == 1)
            {
                if (controller.AliveCount != Tier) { fullSince = -1; return; }
                if (fullSince < 0) fullSince = now;
                if (now - fullSince < warmup) return;
                StartRecorders();
                // Write discovery metadata and let that frame finish before sampling.
                phase = 2; settlingFrames = 2;
                Debug.Log("Benchmark sampling " + TrialName());
                return;
            }
            if (phase == 2)
            {
                if (--settlingFrames > 0) return;
                phase = 3; previousTime = sampleStart = now; fixedSteps = 0;
                return;
            }
            if (controller.AliveCount != Tier || !player.IsInvulnerable || Time.timeScale != 1f)
                throw new InvalidOperationException("Capture scenario changed: " + Progress);
            if (count >= Capacity) throw new InvalidOperationException("Sample capacity exceeded.");
            rows[0][count] = Time.frameCount;
            rows[1][count] = now - sampleStart;
            rows[2][count] = (now - previousTime) * 1000;
            rows[3][count] = controller.AliveCount;
            rows[4][count] = player.IsInvulnerable ? 1 : 0;
            rows[5][count] = fixedSteps;
            rows[6][count] = Time.timeScale;
            rows[14][count] = UnityEngine.Profiling.Profiler.enabled ? 1 : 0;
            for (int i = 7; i < 14; i++) rows[i][count] = double.NaN;
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                var t = timings[0];
                rows[7][count] = t.cpuFrameTime > 0 ? t.cpuFrameTime : double.NaN;
                rows[8][count] = t.cpuMainThreadFrameTime > 0 ? t.cpuMainThreadFrameTime : double.NaN;
                rows[9][count] = t.cpuRenderThreadFrameTime > 0 ? t.cpuRenderThreadFrameTime : double.NaN;
                rows[10][count] = t.gpuFrameTime > 0 ? t.gpuFrameTime : double.NaN;
                rows[11][count] = t.cpuMainThreadPresentWaitTime;
                rows[12][count] = t.frameStartTimestamp;
                rows[13][count] = t.syncInterval;
            }
            for (int i = 0; i < recorders.Length; i++)
            {
                var recorder = recorders[i];
                rows[MetricColumns + i * 2][count] = recorder.Valid ? recorder.LastValueAsDouble : double.NaN;
                rows[MetricColumns + i * 2 + 1][count] = recorder.Valid && recorder.Count > 0 ? recorder.GetSample(0).Count : double.NaN;
            }
            count++; previousTime = now; fixedSteps = 0;
            if (now - sampleStart < duration) return;
            WriteTrial(now - sampleStart);
            DisposeRecorders();
            trial++;
            if (trial >= TrialCount) Complete(true);
            else BeginTrial();
        }

        private void StartRecorders()
        {
            handles.Clear();
            ProfilerRecorderHandle.GetAvailable(handles);
            recorders = new ProfilerRecorder[CounterNames.Length];
            var metadata = new StringBuilder("name,category,unit,available\n");
            for (int i = 0; i < CounterNames.Length; i++)
            {
                bool found = false;
                foreach (var handle in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(handle);
                    if (d.Name != CounterNames[i]) continue;
                    recorders[i] = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.StartImmediately
                        | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame);
                    metadata.AppendLine(d.Name + "," + d.Category.Name + "," + d.UnitType + "," + recorders[i].Valid);
                    found = true;
                    break;
                }
                if (!found) metadata.AppendLine(CounterNames[i] + ",,,False");
            }
            File.WriteAllText(Path.Combine(directory, TrialName() + "_counters.csv"), metadata.ToString());
        }

        private string TrialName() => "tier" + Tier + "_" + (Uncached ? "uncached" : "cached") + "_run" + (Repeat + 1);

        private void WriteTrial(double elapsed)
        {
            string name = TrialName();
            using (var writer = new StreamWriter(Path.Combine(directory, name + ".csv"), false, new UTF8Encoding(false)))
            {
                writer.Write("frame,elapsed_s,frame_ms,alive,invulnerable,fixed_steps,time_scale,cpu_frame_ms,cpu_main_ms,cpu_render_ms,gpu_ms,present_wait_ms,timing_timestamp,sync_interval,profiler_enabled");
                foreach (string counter in CounterNames) writer.Write("," + counter + "," + counter + " samples");
                writer.WriteLine();
                for (int i = 0; i < count; i++)
                {
                    for (int j = 0; j < rows.Length; j++)
                    {
                        if (j > 0) writer.Write(',');
                        writer.Write(rows[j][i].ToString("R", CultureInfo.InvariantCulture));
                    }
                    writer.WriteLine();
                }
            }
            var world = FindObjectOfType<EnemyFlowFieldWorld>();
            File.AppendAllText(Path.Combine(directory, "trials.txt"), name + "," + count + ","
                + elapsed.ToString("R", CultureInfo.InvariantCulture) + ",graphs=" + (world != null ? world.GraphBuilds : 0)
                + ",fields=" + (world != null ? world.CompletedBuilds : 0) + "\n");
        }

        private void DisposeRecorders()
        {
            if (recorders == null) return;
            for (int i = 0; i < recorders.Length; i++) recorders[i].Dispose();
            recorders = null;
        }

        private void Complete(bool success)
        {
            finished = true;
            Restore();
            File.WriteAllText(Path.Combine(directory, "complete.txt"), success ? "success" : "failed");
            if (!Application.isEditor) Application.Quit(success ? 0 : 1);
        }

        private void Restore()
        {
            DisposeRecorders();
            MeleeEnemy.BenchmarkUncachedAttackVisibility = false;
            QualitySettings.vSyncCount = savedVsync;
            Application.targetFrameRate = savedTarget;
            Application.runInBackground = savedBackground;
            UnityEngine.Profiling.Profiler.enabled = savedProfiler;
        }

        private void OnDestroy() { if (rows != null) Restore(); }
    }
}
#endif
