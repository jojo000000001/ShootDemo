using System.Collections.Generic;
using NUnit.Framework;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class SpatialGridTests
    {
        [Test]
        public void NegativeCoordinatesAndBoundaryCrossingsMatchBruteForce()
        {
            var grid = new SpatialGrid<int>(2.5f);
            var points = new List<Vector2>();
            var results = new List<int>();
            for (int i = 0; i < 1000; i++)
            {
                var position = new Vector2((i % 40) * 1.3f - 26f, (i / 40) * 1.3f - 16f);
                points.Add(position);
                grid.Update(i, position);
            }
            for (int step = 0; step < 12; step++)
            {
                var center = new Vector2(-12.5f + step * 2.5f, -2.5f);
                grid.Query(center, 3.75f, results, -1);
                var expected = new List<int>();
                for (int i = 0; i < points.Count; i++)
                    if ((points[i] - center).sqrMagnitude <= 3.75f * 3.75f) expected.Add(i);
                CollectionAssert.AreEquivalent(expected, results);
            }
        }

        [Test]
        public void MovingAndSwapRemovingNeverLeaveStaleOrDuplicateEntries()
        {
            var grid = new SpatialGrid<int>(2f);
            var results = new List<int>();
            grid.Update(1, Vector2.zero);
            grid.Update(2, Vector2.right);
            grid.Update(3, Vector2.up);
            Assert.IsTrue(grid.Remove(1));
            grid.Update(3, new Vector2(-2.01f, 0f));
            grid.Update(2, Vector2.right);
            grid.Query(Vector2.zero, 5f, results, -1);
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, results);
            Assert.IsFalse(grid.Remove(1));
            grid.Update(3, new Vector2(20f, 0f));
            grid.Query(Vector2.zero, 5f, results, -1);
            CollectionAssert.AreEqual(new[] { 2 }, results);
            Assert.AreEqual(2, grid.Count);
        }

        [Test]
        public void QueryExcludesSelfAndSquareCornersAndReusesOutputBuffer()
        {
            var grid = new SpatialGrid<int>(1f);
            grid.Update(1, Vector2.zero);
            grid.Update(2, new Vector2(0.9f, 0.9f));
            grid.Update(3, Vector2.right);
            var results = new List<int> { 999 };
            grid.Query(Vector2.zero, 1f, results, 1);
            CollectionAssert.AreEqual(new[] { 3 }, results);
            grid.Query(Vector2.one * 100f, 1f, results, 1);
            Assert.IsEmpty(results);
        }

        [Test]
        public void DenseThousandAgentQueryObeysCandidateAndResultBudgets()
        {
            var grid = new SpatialGrid<int>(2.5f);
            for (int i = 0; i < 1000; i++) grid.Update(i, Vector2.zero);
            var results = new List<int>(24);
            grid.Query(Vector2.zero, 2f, results, 0, 24, 128);
            Assert.AreEqual(24, results.Count);
            Assert.LessOrEqual(grid.LastCandidateChecks, 128);
            CollectionAssert.DoesNotContain(results, 0);
            grid.Query(new Vector2(2.4f, 2.4f), 0.1f, results, -1, 24, 64);
            Assert.LessOrEqual(grid.LastCandidateChecks, 64);
        }

        [Test]
        public void ThousandAgentsOnlyCheckNearbyBuckets()
        {
            var grid = new SpatialGrid<int>(2.5f);
            for (int i = 0; i < 1000; i++) grid.Update(i, new Vector2(i % 40 * 2f, i / 40 * 2f));
            var results = new List<int>(24);
            int checks = 0;
            for (int i = 0; i < 1000; i++)
            {
                grid.Query(new Vector2(i % 40 * 2f, i / 40 * 2f), 2f, results, i, 24, 128);
                checks += grid.LastCandidateChecks;
            }
            Assert.Less(checks, 20000, "Nearby queries should not scan a million agent pairs.");
        }

        [Test]
        public void WarmQueriesAndCrossCellUpdatesAllocateNoManagedMemory()
        {
            var grid = new SpatialGrid<int>(2.5f);
            var results = new List<int>(1024);
            for (int i = 0; i < 1000; i++) grid.Update(i, new Vector2(i % 40 * 2f, i / 40 * 2f));
            for (int i = 0; i < 1000; i++) grid.Query(Vector2.zero, 2f, results, i, 24, 128);
            // Warm both sides of cell boundaries before measuring bucket reuse.
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < 1000; i++) grid.Update(i, new Vector2(i % 40 * 2f + pass * 0.5f, i / 40 * 2f));
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < 4; pass++)
            for (int i = 0; i < 1000; i++)
            {
                var position = new Vector2(i % 40 * 2f + (pass % 2) * 0.5f, i / 40 * 2f);
                grid.Update(i, position);
                grid.Query(position, 2f, results, i, 24, 128);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
        }

        [Test]
        public void ClearReleasesMembershipAndAllowsReusingGrid()
        {
            var grid = new SpatialGrid<int>(2f);
            grid.Update(1, Vector2.zero);
            grid.Clear();
            Assert.AreEqual(0, grid.Count);
            Assert.AreEqual(0, grid.OccupiedCellCount);
            Assert.IsFalse(grid.TryGetPosition(1, out _));
            grid.Update(1, Vector2.left);
            Assert.IsTrue(grid.TryGetPosition(1, out var position));
            Assert.AreEqual(Vector2.left, position);
        }
    }
}
