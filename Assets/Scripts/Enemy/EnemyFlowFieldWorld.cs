using System;
using System.Collections.Generic;
using ShootDemo.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootDemo.Enemy
{
    /// <summary>Scene-owned static clearance graphs and shared, double-buffered reverse Dijkstra fields.</summary>
    [DefaultExecutionOrder(-60)]
    public sealed class EnemyFlowFieldWorld : MonoBehaviour
    {
        private static readonly Dictionary<int, EnemyFlowFieldWorld> worlds = new Dictionary<int, EnemyFlowFieldWorld>();
        private static readonly ProfilerMarker BakeMarker = new ProfilerMarker("Enemies.FlowField.Bake");
        private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("Enemies.FlowField.Integrate");
        private static readonly ProfilerMarker SampleMarker = new ProfilerMarker("Enemies.FlowField.Sample");
        private readonly List<Layer> layers = new List<Layer>(3);
        private readonly System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
        private Rect bounds;
        private bool boundsSet;
        private int cursor;
        public float RefreshInterval { get; set; } = 0.3f;
        public float BuildMillisecondsPerFrame { get; set; } = 1f;
        public int LayerCount => layers.Count;
        public int CompletedBuilds { get; private set; }
        public int GraphBuilds { get; private set; }
        public int SampleCount { get; private set; }
        public double LastBuildMilliseconds { get; private set; }

        public static EnemyFlowFieldWorld ForScene(Scene scene)
        {
            if (worlds.TryGetValue(scene.handle, out var world) && world != null) return world;
            var root = new GameObject("Shared Enemy Navigation");
            SceneManager.MoveGameObjectToScene(root, scene);
            world = root.AddComponent<EnemyFlowFieldWorld>();
            worlds[scene.handle] = world;
            return world;
        }

        // Current prefabs (0.71–0.76m) share one conservative graph. Larger bodies share coarser classes.
        internal static float ClearanceClass(float radius) => radius <= 0.76f + 0.00001f ? 0.76f
            : radius <= 1f ? 1f : radius <= 1.5f ? 1.5f : Mathf.Ceil(radius * 2f) * 0.5f;

        public void Prepare(float radius, int obstacleMask, Rect arena)
        {
            if (!boundsSet) SetBounds(arena.min, arena.max);
            GetLayer(radius, obstacleMask, arena.min, arena.max);
        }

        private void SetBounds(Vector2 from, Vector2 target)
        {
            Vector2 min = Vector2.Min(from, target), max = Vector2.Max(from, target);
            // Include wall ends, not just the actor/target window, so long detours remain reachable.
            var colliders = new List<Collider2D>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                colliders.Clear();
                root.GetComponentsInChildren(false, colliders);
                foreach (var solid in colliders)
                {
                    if (!EnemyPathfinder2D.IsObstacle(solid) || !solid.enabled) continue;
                    min = Vector2.Min(min, solid.bounds.min);
                    max = Vector2.Max(max, solid.bounds.max);
                }
            }
            min -= Vector2.one * 4f;
            max += Vector2.one * 4f;
            bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            boundsSet = true;
        }

        private Layer GetLayer(float radius, int mask, Vector2 from, Vector2 target)
        {
            radius = ClearanceClass(radius);
            foreach (var existing in layers)
                if (existing.Radius == radius && existing.Mask == mask) return existing;
            if (!boundsSet) SetBounds(from, target);
            Layer layer;
            using (BakeMarker.Auto()) layer = new Layer(gameObject.scene, bounds, radius, mask);
            layers.Add(layer);
            GraphBuilds++;
            return layer;
        }

        public bool TryGetWaypoint(Vector2 from, Vector2 target, float radius, int obstacleMask,
            bool entranceRoute, EnemyPathfinder2D navigation, out Vector2 waypoint)
        {
            using (SampleMarker.Auto())
            {
                SampleCount++;
                var layer = GetLayer(radius, obstacleMask, from, target);
                var field = layer.Fields[entranceRoute ? 1 : 0];
                field.Request(target, Time.time, RefreshInterval);
                // EditMode simulations have no player loop to service the shared job.
                if (!Application.isPlaying) while (field.Building) CompleteStep(field);
                return layer.Sample(field, from, navigation, out waypoint);
            }
        }

        private void CompleteStep(Field field)
        {
            if (field.Step()) CompletedBuilds++;
        }

        private void Update()
        {
            var state = GameStateMachine.Instance;
            if (state != null && state.CurrentState != GameState.Playing) return;
            using (BuildMarker.Auto())
            {
                timer.Restart();
                int idle = 0, count = layers.Count * 2;
                while (count > 0 && idle < count && timer.Elapsed.TotalMilliseconds < BuildMillisecondsPerFrame)
                {
                    int index = cursor++ % count;
                    // Keep the cursor bounded even over long play sessions.
                    cursor %= count;
                    var field = layers[index / 2].Fields[index % 2];
                    field.Refresh(Time.time, RefreshInterval);
                    if (!field.Building) { idle++; continue; }
                    idle = 0;
                    for (int i = 0; i < 32 && field.Building; i++) CompleteStep(field);
                }
                timer.Stop();
                LastBuildMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
        }

        private void OnDestroy()
        {
            if (worlds.TryGetValue(gameObject.scene.handle, out var world) && world == this)
                worlds.Remove(gameObject.scene.handle);
        }

        private sealed class Layer
        {
            internal const float CellSize = 0.5f;
            private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
            private static readonly int[] DY = { 0, 1, 1, 1, 0, -1, -1, -1 };
            internal readonly float Radius;
            internal readonly int Mask;
            internal readonly int Width, Height;
            internal readonly Vector2 Origin;
            internal readonly byte[] Edges;
            internal readonly bool[] Walkable;
            internal readonly Field[] Fields;
            internal readonly EnemyPathfinder2D Navigation;

            internal Layer(Scene scene, Rect bounds, float radius, int mask)
            {
                Radius = radius;
                Mask = mask;
                Origin = new Vector2(Mathf.Floor(bounds.xMin / CellSize), Mathf.Floor(bounds.yMin / CellSize)) * CellSize
                    + Vector2.one * (CellSize * 0.5f);
                Width = Mathf.CeilToInt((bounds.xMax - Origin.x) / CellSize) + 1;
                Height = Mathf.CeilToInt((bounds.yMax - Origin.y) / CellSize) + 1;
                if ((long)Width * Height > 262144) throw new InvalidOperationException("Enemy navigation bounds exceed 262144 cells.");
                int count = Width * Height;
                Walkable = new bool[count];
                Edges = new byte[count];
                Navigation = new EnemyPathfinder2D(scene, radius, mask);
                for (int i = 0; i < count; i++) Walkable[i] = Navigation.PointClear(Position(i));
                for (int i = 0; i < count; i++)
                {
                    if (!Walkable[i]) continue;
                    // Bake each undirected edge once; diagonal sweeps forbid cutting wall corners.
                    for (int d = 0; d < 4; d++)
                    {
                        int next = Neighbor(i, d);
                        if (next < 0 || !Walkable[next]) continue;
                        if ((d & 1) != 0 && (!Walkable[i + DX[d]] || !Walkable[i + DY[d] * Width])) continue;
                        if (!Navigation.SegmentClear(Position(i), Position(next))) continue;
                        Edges[i] |= (byte)(1 << d);
                        Edges[next] |= (byte)(1 << ((d + 4) % 8));
                    }
                }
                Fields = new[] { new Field(this), new Field(this) };
            }

            internal Vector2 Position(int i) => Origin + new Vector2(i % Width, i / Width) * CellSize;
            internal int Cell(Vector2 p)
            {
                int x = Mathf.RoundToInt((p.x - Origin.x) / CellSize), y = Mathf.RoundToInt((p.y - Origin.y) / CellSize);
                return x < 0 || x >= Width || y < 0 || y >= Height ? -1 : y * Width + x;
            }
            internal int Neighbor(int i, int d)
            {
                int x = i % Width + DX[d], y = i / Width + DY[d];
                return x < 0 || x >= Width || y < 0 || y >= Height ? -1 : y * Width + x;
            }
            internal static float EdgeCost(int d) => (d & 1) == 0 ? CellSize : CellSize * 1.41421356f;

            internal bool Sample(Field field, Vector2 from, EnemyPathfinder2D navigation, out Vector2 point)
            {
                point = from;
                int cell = Cell(from);
                if (!field.Ready || cell < 0) return false;
                // The usual case is a constant-size lookup. Validate the off-center connection too.
                int next = field.Next[cell];
                if (next >= 0 && next != cell && navigation.SegmentClear(from, Position(next)))
                { point = Position(next); return true; }
                float best = float.PositiveInfinity;
                int chosen = -1;
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    int cx = cell % Width + x, cy = cell / Width + y;
                    if (cx < 0 || cx >= Width || cy < 0 || cy >= Height) continue;
                    int i = cy * Width + cx;
                    float distance = Vector2.Distance(from, Position(i));
                    float score = field.Cost[i] + distance;
                    if (distance < 0.04f || score >= best || float.IsPositiveInfinity(score)) continue;
                    if (!navigation.SegmentClear(from, Position(i))) continue;
                    best = score;
                    chosen = i;
                }
                if (chosen < 0) return false;
                point = Position(chosen);
                return true;
            }
        }

        private sealed class Field
        {
            private readonly Layer layer;
            internal float[] Cost;
            internal int[] Next;
            private float[] workingCost;
            private int[] workingNext;
            private readonly int[] heap, heapIndex;
            private int heapCount;
            private Vector2 requestedTarget;
            private int requestedCell = -1, builtCell = -1, buildingCell = -1;
            private bool requested;
            private float nextRefresh;
            internal bool Ready { get; private set; }
            internal bool Building { get; private set; }

            internal Field(Layer graph)
            {
                layer = graph;
                int count = graph.Edges.Length;
                Cost = new float[count]; Next = new int[count];
                workingCost = new float[count]; workingNext = new int[count];
                heap = new int[count]; heapIndex = new int[count];
            }

            internal void Request(Vector2 target, float now, float interval)
            {
                requestedTarget = target;
                requestedCell = layer.Cell(target);
                requested = true;
                Refresh(now, interval);
            }

            internal void Refresh(float now, float interval)
            {
                if (!requested || Building || (Ready && (requestedCell == builtCell || (Application.isPlaying && now < nextRefresh)))) return;
                nextRefresh = now + Mathf.Max(0.2f, interval);
                Begin();
            }

            private void Begin()
            {
                Building = true;
                buildingCell = requestedCell;
                heapCount = 0;
                for (int i = 0; i < workingCost.Length; i++)
                { workingCost[i] = float.PositiveInfinity; workingNext[i] = -1; heapIndex[i] = -1; }
                if (buildingCell < 0) return;
                // Multi-source seeds let a player stand closer to a wall than an enemy body can.
                // A seed must see the actual goal, so enclosed targets do not leak through walls.
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    int cx = buildingCell % layer.Width + x, cy = buildingCell / layer.Width + y;
                    if (cx < 0 || cx >= layer.Width || cy < 0 || cy >= layer.Height) continue;
                    int i = cy * layer.Width + cx;
                    Vector2 p = layer.Position(i);
                    float distance = Vector2.Distance(p, requestedTarget);
                    if (!layer.Walkable[i] || distance > 1f || !layer.Navigation.SegmentClear(p, requestedTarget, 0.01f)) continue;
                    workingCost[i] = distance;
                    workingNext[i] = i;
                    PushOrRaise(i);
                }
            }

            internal bool Step()
            {
                if (!Building) return false;
                if (heapCount == 0)
                {
                    var costs = Cost; Cost = workingCost; workingCost = costs;
                    var next = Next; Next = workingNext; workingNext = next;
                    builtCell = buildingCell;
                    Ready = true;
                    Building = false;
                    return true;
                }
                int current = Pop();
                for (int d = 0; d < 8; d++)
                {
                    if ((layer.Edges[current] & (1 << d)) == 0) continue;
                    int next = layer.Neighbor(current, d);
                    float cost = workingCost[current] + Layer.EdgeCost(d);
                    if (cost >= workingCost[next]) continue;
                    workingCost[next] = cost;
                    workingNext[next] = current;
                    PushOrRaise(next);
                }
                return false;
            }

            private void PushOrRaise(int cell)
            {
                int i = heapIndex[cell];
                if (i < 0) i = heapCount++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (workingCost[heap[parent]] <= workingCost[cell]) break;
                    heap[i] = heap[parent]; heapIndex[heap[i]] = i; i = parent;
                }
                heap[i] = cell; heapIndex[cell] = i;
            }

            private int Pop()
            {
                int result = heap[0], last = heap[--heapCount];
                heapIndex[result] = -1;
                if (heapCount == 0) return result;
                int i = 0;
                while (i * 2 + 1 < heapCount)
                {
                    int child = i * 2 + 1;
                    if (child + 1 < heapCount && workingCost[heap[child + 1]] < workingCost[heap[child]]) child++;
                    if (workingCost[last] <= workingCost[heap[child]]) break;
                    heap[i] = heap[child]; heapIndex[heap[i]] = i; i = child;
                }
                heap[i] = last; heapIndex[last] = i;
                return result;
            }
        }
    }
}
