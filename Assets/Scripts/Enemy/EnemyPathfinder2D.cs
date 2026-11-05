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

        private sealed class Node
        {
            public Vector2Int Cell;
            public float Cost;
            public float Score;
            public Node Parent;
            public bool Closed;
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
            float targetClearance = 0.01f)
        {
            path.Clear();
            if (SegmentClear(start, target)) { path.Add(target); return true; }
            nodes.Clear();
            open.Clear();
            var first = new Node { Cell = Vector2Int.zero, Score = Vector2.Distance(start, target) };
            nodes.Add(first.Cell, first);
            open.Add(first);
            Vector2 targetCell = (target - start) / CellSize;
            int minX = Mathf.FloorToInt(Mathf.Min(0f, targetCell.x)) - 16;
            int maxX = Mathf.CeilToInt(Mathf.Max(0f, targetCell.x)) + 16;
            int minY = Mathf.FloorToInt(Mathf.Min(0f, targetCell.y)) - 16;
            int maxY = Mathf.CeilToInt(Mathf.Max(0f, targetCell.y)) + 16;
            int visited = 0;
            while (open.Count > 0 && visited++ < MaxVisited)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++) if (open[i].Score < open[best].Score) best = i;
                Node current = open[best];
                open.RemoveAt(best);
                current.Closed = true;
                Vector2 point = start + (Vector2)current.Cell * CellSize;
                if (Vector2.Distance(point, target) <= stoppingDistance && SegmentClear(point, target, targetClearance))
                {
                    for (Node node = current; node.Parent != null; node = node.Parent)
                        path.Add(start + (Vector2)node.Cell * CellSize);
                    path.Reverse();
                    return path.Count > 0;
                }
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    Vector2Int cell = current.Cell + new Vector2Int(x, y);
                    if (cell.x < minX || cell.x > maxX || cell.y < minY || cell.y > maxY) continue;
                    nodes.TryGetValue(cell, out Node next);
                    if (next != null && next.Closed) continue;
                    float cost = current.Cost + (x != 0 && y != 0 ? CellSize * 1.414214f : CellSize);
                    if (next != null && cost >= next.Cost) continue;
                    Vector2 destination = start + (Vector2)cell * CellSize;
                    if (!PositionClear(destination) || !SegmentClear(point, destination)) continue;
                    if (next == null)
                    {
                        next = new Node { Cell = cell };
                        nodes.Add(cell, next);
                        open.Add(next);
                    }
                    next.Cost = cost;
                    next.Score = cost + Mathf.Max(0f, Vector2.Distance(destination, target) - stoppingDistance);
                    next.Parent = current;
                }
            }
            return false;
        }
    }
}
