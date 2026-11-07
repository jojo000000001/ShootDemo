using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootDemo.Enemy
{
    /// <summary>Small, bounded A* search against the scene's solid environment colliders.</summary>
    public sealed class EnemyPathfinder2D
    {
        private const float CellSize = 0.75f;
        private const int MaxVisited = 4096;
        private readonly PhysicsScene2D physics;
        private readonly float radius;
        private readonly ContactFilter2D filter;
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);
        private readonly List<Collider2D> overlaps = new List<Collider2D>(16);
        private readonly Dictionary<Vector2Int, Node> nodes = new Dictionary<Vector2Int, Node>();
        private readonly List<Node> open = new List<Node>();
        private readonly List<Node> nodePool = new List<Node>();
        private int usedNodes;

        private Node RentNode(Vector2Int cell, Vector2 position)
        {
            if (usedNodes == nodePool.Count) nodePool.Add(new Node());
            Node node = nodePool[usedNodes++];
            node.Cell = cell;
            node.Position = position;
            node.Cost = node.Score = 0f;
            node.Parent = null;
            node.Closed = false;
            node.HeapIndex = 0;
            return node;
        }

        private sealed class Node
        {
            public Vector2Int Cell;
            public Vector2 Position;
            public float Cost;
            public float Score;
            public Node Parent;
            public bool Closed;
            public int HeapIndex;
        }

        private static bool Before(Node a, Node b) => a.Score < b.Score || (a.Score == b.Score && a.Cost > b.Cost);

        private void Raise(Node node)
        {
            int index = node.HeapIndex;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Before(node, open[parent])) break;
                open[index] = open[parent];
                open[index].HeapIndex = index;
                index = parent;
            }
            open[index] = node;
            node.HeapIndex = index;
        }

        private void Push(Node node)
        {
            node.HeapIndex = open.Count;
            open.Add(node);
            Raise(node);
        }

        private Node Pop()
        {
            Node result = open[0];
            Node last = open[open.Count - 1];
            open.RemoveAt(open.Count - 1);
            if (open.Count == 0) return result;
            int index = 0;
            while (index * 2 + 1 < open.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < open.Count && Before(open[child + 1], open[child])) child++;
                if (!Before(open[child], last)) break;
                open[index] = open[child];
                open[index].HeapIndex = index;
                index = child;
            }
            open[index] = last;
            last.HeapIndex = index;
            return result;
        }

        public static float BodyClearance(GameObject actor, float minimum)
        {
            var capsule = actor.GetComponent<CapsuleCollider2D>();
            if (capsule == null) return minimum;
            Vector3 scale = actor.transform.lossyScale;
            Vector2 size = Vector2.Scale(capsule.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            Vector2 offset = Vector2.Scale(capsule.offset, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            return Mathf.Max(minimum, Mathf.Max(size.x, size.y) * 0.5f + offset.magnitude + 0.04f);
        }

        public EnemyPathfinder2D(Scene scene, float clearance, LayerMask obstacleMask)
        {
            physics = scene.GetPhysicsScene2D();
            radius = Mathf.Max(0.05f, clearance);
            filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(obstacleMask);
        }

        // Actors are dynamic bodies. Static/kinematic solids form the navigable environment.
        private static bool IsObstacle(Collider2D collider)
        {
            return collider != null && !collider.isTrigger
                && (collider.attachedRigidbody == null || collider.attachedRigidbody.bodyType != RigidbodyType2D.Dynamic);
        }

        public bool SegmentClear(Vector2 from, Vector2 to, bool useClearance = true)
        {
            return CastClear(from, to, useClearance ? radius : 0.01f, useClearance);
        }

        public bool SegmentClear(Vector2 from, Vector2 to, float clearance)
        {
            return CastClear(from, to, Mathf.Max(0.01f, clearance), false);
        }

        private bool CastClear(Vector2 from, Vector2 to, float clearance, bool allowMarginEscape)
        {
            Vector2 delta = to - from;
            int count = physics.CircleCast(from, clearance,
                delta.normalized, delta.magnitude, filter, hits);
            for (int i = 0; i < count; i++)
            {
                if (!IsObstacle(hits[i].collider)) continue;
                // Knockback may leave the body flush against a wall, inside the extra
                // navigation margin. Permit a step out of that margin, never into the wall.
                Vector2 outward = from - hits[i].collider.ClosestPoint(from);
                if (allowMarginEscape && hits[i].distance <= 0.0001f && outward.sqrMagnitude > 0.000001f
                    && Vector2.Dot(delta, outward) > 0.0001f) continue;
                return false;
            }
            return true;
        }

        private bool PositionClear(Vector2 point)
        {
            int count = physics.OverlapCircle(point, radius, filter, overlaps);
            for (int i = 0; i < count; i++) if (IsObstacle(overlaps[i])) return false;
            return true;
        }

        /// <summary>Returns a path to an unobstructed approach point within stoppingDistance of the target.</summary>
        public bool FindPath(Vector2 start, Vector2 target, float stoppingDistance, List<Vector2> path,
            float targetClearance = 0.01f, bool allowFineSearch = true)
        {
            using (var search = FindPathIncremental(start, target, stoppingDistance, path, targetClearance, allowFineSearch).GetEnumerator())
                while (search.MoveNext()) { }
            return path.Count > 0;
        }

        // One yield per expanded node lets callers impose both time and node budgets.
        // Each pathfinder owns its search buffers: do not interleave searches on one instance.
        public IEnumerable<int> FindPathIncremental(Vector2 start, Vector2 target, float stoppingDistance,
            List<Vector2> path, float targetClearance = 0.01f, bool allowFineSearch = true)
        {
            path.Clear();
            if (SegmentClear(start, target)) { path.Add(target); yield break; }
            foreach (int node in FindGridPath(start, target, stoppingDistance, path, targetClearance, CellSize)) yield return node;
            if (path.Count > 0 || !allowFineSearch) yield break;
            foreach (int node in FindGridPath(start, target, stoppingDistance, path, targetClearance, 0.25f)) yield return node;
        }

        private IEnumerable<int> FindGridPath(Vector2 start, Vector2 target, float stoppingDistance,
            List<Vector2> path, float targetClearance, float step)
        {
            path.Clear();
            nodes.Clear();
            open.Clear();
            usedNodes = 0;
            Vector2Int startCell = Vector2Int.RoundToInt(start / step);
            Node first = RentNode(startCell, start);
            first.Score = Vector2.Distance(start, target);
            nodes.Add(first.Cell, first);
            Push(first);
            int minX = Mathf.FloorToInt((Mathf.Min(start.x, target.x) - 12f) / step);
            int maxX = Mathf.CeilToInt((Mathf.Max(start.x, target.x) + 12f) / step);
            int minY = Mathf.FloorToInt((Mathf.Min(start.y, target.y) - 12f) / step);
            int maxY = Mathf.CeilToInt((Mathf.Max(start.y, target.y) + 12f) / step);
            // A long wall may require a detour beyond the old fixed 12m window.
            // Include the ends of solids directly separating the actor and target.
            Vector2 delta = target - start;
            int blockingCount = physics.CircleCast(start, radius, delta.normalized, delta.magnitude, filter, hits);
            for (int i = 0; i < blockingCount; i++)
            {
                var solid = hits[i].collider;
                if (!IsObstacle(solid)) continue;
                Bounds bounds = solid.bounds;
                float padding = radius + step * 2f;
                minX = Mathf.Min(minX, Mathf.FloorToInt((bounds.min.x - padding) / step));
                maxX = Mathf.Max(maxX, Mathf.CeilToInt((bounds.max.x + padding) / step));
                minY = Mathf.Min(minY, Mathf.FloorToInt((bounds.min.y - padding) / step));
                maxY = Mathf.Max(maxY, Mathf.CeilToInt((bounds.max.y + padding) / step));
            }
            int visited = 0;
            int budget = step < CellSize ? MaxVisited * 4 : MaxVisited;
            while (open.Count > 0 && visited++ < budget)
            {
                yield return visited;
                Node current = Pop();
                current.Closed = true;
                Vector2 point = current.Position;
                float targetDistance = Vector2.Distance(point, target);
                bool finalConnection = targetDistance <= step * 1.5f && SegmentClear(point, target);
                if (finalConnection || (targetDistance <= stoppingDistance && SegmentClear(point, target, targetClearance)))
                {
                    for (Node node = current; node.Parent != null; node = node.Parent)
                        path.Add(node.Position);
                    path.Reverse();
                    if (finalConnection) path.Add(target);
                    yield break;
                }
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    Vector2Int cell = current.Cell + new Vector2Int(x, y);
                    if (cell.x < minX || cell.x > maxX || cell.y < minY || cell.y > maxY) continue;
                    nodes.TryGetValue(cell, out Node next);
                    if (next != null && next.Closed) continue;
                    Vector2 destination = (Vector2)cell * step;
                    float cost = current.Cost + Vector2.Distance(point, destination);
                    if (next != null && cost >= next.Cost) continue;
                    if (!PositionClear(destination) || !SegmentClear(point, destination)) continue;
                    bool created = next == null;
                    if (created)
                    {
                        next = RentNode(cell, destination);
                        nodes.Add(cell, next);
                    }
                    next.Cost = cost;
                    next.Score = cost + Mathf.Max(0f, Vector2.Distance(destination, target) - stoppingDistance);
                    next.Parent = current;
                    if (created) Push(next);
                    else Raise(next);
                }
            }
        }
    }
}
