using NUnit.Framework;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class ObjectPoolTests
    {
        sealed class PoolProbe : MonoBehaviour, IPoolable
        {
            public int SpawnCount;
            public int DespawnCount;

            public void OnSpawned()
            {
                SpawnCount++;
            }

            public void OnDespawned()
            {
                DespawnCount++;
            }
        }

        GameObject prefabRoot;
        PoolProbe prefab;
        Transform poolRoot;
        Pool<PoolProbe> pool;

        [SetUp]
        public void SetUp()
        {
            prefabRoot = new GameObject("PoolProbePrefab");
            prefab = prefabRoot.AddComponent<PoolProbe>();
            prefabRoot.SetActive(false);

            poolRoot = new GameObject("PoolRoot").transform;
            poolRoot.gameObject.SetActive(false);
            pool = new Pool<PoolProbe>(prefab, prewarmCount: 2, root: poolRoot);
        }

        [TearDown]
        public void TearDown()
        {
            pool?.Clear();
            Object.DestroyImmediate(prefabRoot);
            if (poolRoot != null)
            {
                Object.DestroyImmediate(poolRoot.gameObject);
            }
        }

        [Test]
        public void Get_UsesPrewarmedInstancesBeforeAllocatingNewOnes()
        {
            PoolProbe first = pool.Get();
            PoolProbe second = pool.Get();

            Assert.That(pool.ActiveCount, Is.EqualTo(2));
            Assert.That(pool.InactiveCount, Is.Zero);
            Assert.That(first, Is.Not.SameAs(second));
        }

        [Test]
        public void ReleaseAndGet_ReusesSameInstance()
        {
            PoolProbe instance = pool.Get();
            bool released = pool.Release(instance);
            PoolProbe reused = pool.Get();

            Assert.That(released, Is.True);
            Assert.That(reused, Is.SameAs(instance));
            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.InactiveCount, Is.EqualTo(1));
            Assert.That(pool.TotalCount, Is.EqualTo(2));
        }

        [Test]
        public void Release_InvokesPoolableCallbacks()
        {
            PoolProbe instance = pool.Get();
            pool.Release(instance);

            Assert.That(instance.SpawnCount, Is.EqualTo(1));
            Assert.That(instance.DespawnCount, Is.EqualTo(1));
            Assert.That(instance.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void Release_ReturnsFalseForUnknownInstance()
        {
            var outsider = new GameObject("Outsider").AddComponent<PoolProbe>();

            try
            {
                Assert.That(pool.Release(outsider), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(outsider.gameObject);
            }
        }
    }
}
