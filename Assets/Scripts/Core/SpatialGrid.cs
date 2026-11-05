using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShootDemo.Core
{
    /// <summary>Incremental uniform grid. Queries reuse the caller's buffer; empty buckets are recycled.</summary>
    public sealed class SpatialGrid<T>
    {
        private sealed class Entry
        {
            public T Item;
            public Vector2 Position;
            public Vector2Int Cell;
            public int Index;
        }

        private readonly Dictionary<T, Entry> entries = new Dictionary<T, Entry>();
        private readonly Dictionary<Vector2Int, List<Entry>> cells = new Dictionary<Vector2Int, List<Entry>>();
        private readonly Stack<List<Entry>> freeBuckets = new Stack<List<Entry>>();
        private readonly EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        public float CellSize { get; }
        public int Count => entries.Count;
        public int OccupiedCellCount => cells.Count;
        public int LastCandidateChecks { get; private set; }

        public SpatialGrid(float cellSize)
        {
            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            CellSize = cellSize;
        }

        private Vector2Int Cell(Vector2 position) => new Vector2Int(
            Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.y / CellSize));

        public void Update(T item, Vector2 position)
        {
            Vector2Int cell = Cell(position);
            if (entries.TryGetValue(item, out Entry entry))
            {
                entry.Position = position;
                if (entry.Cell == cell) return;
                RemoveFromCell(entry);
            }
            else
            {
                entry = new Entry { Item = item, Position = position };
                entries.Add(item, entry);
            }
            if (!cells.TryGetValue(cell, out var bucket))
            {
                bucket = freeBuckets.Count > 0 ? freeBuckets.Pop() : new List<Entry>(8);
                cells.Add(cell, bucket);
            }
            entry.Cell = cell;
            entry.Index = bucket.Count;
            bucket.Add(entry);
        }

        public bool Remove(T item)
        {
            if (!entries.TryGetValue(item, out Entry entry)) return false;
            RemoveFromCell(entry);
            entries.Remove(item);
            return true;
        }

        private void RemoveFromCell(Entry entry)
        {
            var bucket = cells[entry.Cell];
            int last = bucket.Count - 1;
            var moved = bucket[last];
            bucket[entry.Index] = moved;
            moved.Index = entry.Index;
            bucket.RemoveAt(last);
            if (bucket.Count != 0) return;
            cells.Remove(entry.Cell);
            freeBuckets.Push(bucket);
        }

        public bool TryGetPosition(T item, out Vector2 position)
        {
            if (entries.TryGetValue(item, out Entry entry)) { position = entry.Position; return true; }
            position = default;
            return false;
        }

        /// <summary>Exact circle query by default. Optional budgets bound steering work in very dense cells.</summary>
        public int Query(Vector2 position, float radius, List<T> results, T exclude,
            int maxResults = int.MaxValue, int maxCandidates = int.MaxValue)
        {
            results.Clear();
            LastCandidateChecks = 0;
            if (radius < 0f || maxResults <= 0 || maxCandidates <= 0) return 0;
            var center = Cell(position);
            var min = Cell(position - Vector2.one * radius);
            var max = Cell(position + Vector2.one * radius);
            float squared = radius * radius;
            // Start with the caller's own cell so distant buckets cannot exhaust a bounded query first.
            if (Scan(center, position, squared, results, exclude, maxResults, maxCandidates)) return results.Count;
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                var cell = new Vector2Int(x, y);
                if (cell == center) continue;
                if (Scan(cell, position, squared, results, exclude, maxResults, maxCandidates)) return results.Count;
            }
            return results.Count;
        }

        private bool Scan(Vector2Int cell, Vector2 position, float squared, List<T> results, T exclude,
            int maxResults, int maxCandidates)
        {
            if (!cells.TryGetValue(cell, out var bucket)) return false;
            // Rotate sampling per caller so a packed bucket does not always separate against its first few entries.
            int offset = (comparer.GetHashCode(exclude) & int.MaxValue) % bucket.Count;
            for (int i = 0; i < bucket.Count; i++)
            {
                var entry = bucket[(offset + i) % bucket.Count];
                LastCandidateChecks++;
                if (!comparer.Equals(entry.Item, exclude) && (entry.Position - position).sqrMagnitude <= squared)
                    results.Add(entry.Item);
                if (results.Count >= maxResults || LastCandidateChecks >= maxCandidates) return true;
            }
            return false;
        }

        public void Clear()
        {
            foreach (var bucket in cells.Values) { bucket.Clear(); freeBuckets.Push(bucket); }
            cells.Clear();
            entries.Clear();
        }
    }
}
