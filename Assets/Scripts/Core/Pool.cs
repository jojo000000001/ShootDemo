using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShootDemo.Core
{
    public sealed class Pool<T> where T : Component
    {
        readonly T _prefab;
        readonly Transform _root;
        readonly Stack<T> _inactive = new Stack<T>();
        readonly HashSet<T> _active = new HashSet<T>();
        readonly Action<T> _onGet;
        readonly Action<T> _onRelease;
        readonly bool _ownsRoot;

        public int InactiveCount => _inactive.Count;

        public int ActiveCount => _active.Count;

        public int TotalCount => _active.Count + _inactive.Count;

        public Pool(
            T prefab,
            int prewarmCount = 0,
            Transform root = null,
            Action<T> onGet = null,
            Action<T> onRelease = null)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            _prefab = prefab;
            _onGet = onGet;
            _onRelease = onRelease;

            if (root == null)
            {
                var rootObject = new GameObject($"{prefab.name}_Pool");
                rootObject.SetActive(false);
                _root = rootObject.transform;
                _ownsRoot = true;
            }
            else
            {
                _root = root;
                _ownsRoot = false;
            }

            prewarmCount = Math.Max(0, prewarmCount);
            for (int i = 0; i < prewarmCount; i++)
            {
                _inactive.Push(CreateInstance());
            }
        }

        public T Get()
        {
            return Get(Vector3.zero, Quaternion.identity);
        }

        public T Get(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            T instance = _inactive.Count > 0 ? _inactive.Pop() : CreateInstance();
            Transform transform = instance.transform;
            transform.SetPositionAndRotation(position, rotation);

            if (parent != null)
            {
                transform.SetParent(parent, worldPositionStays: true);
            }
            else
            {
                transform.SetParent(null, worldPositionStays: true);
            }

            instance.gameObject.SetActive(true);
            _active.Add(instance);
            _onGet?.Invoke(instance);

            if (instance is IPoolable poolable)
            {
                poolable.OnSpawned();
            }

            return instance;
        }

        public bool Release(T instance)
        {
            if (instance == null || !_active.Remove(instance))
            {
                return false;
            }

            _onRelease?.Invoke(instance);

            if (instance is IPoolable poolable)
            {
                poolable.OnDespawned();
            }

            instance.gameObject.SetActive(false);
            instance.transform.SetParent(_root, false);
            _inactive.Push(instance);
            return true;
        }

        public void Clear(bool destroyInstances = true)
        {
            if (destroyInstances)
            {
                foreach (T instance in _active)
                {
                    Object.DestroyImmediate(instance.gameObject);
                }

                foreach (T instance in _inactive)
                {
                    Object.DestroyImmediate(instance.gameObject);
                }

                if (_ownsRoot && _root != null)
                {
                    Object.DestroyImmediate(_root.gameObject);
                }
            }

            _active.Clear();
            _inactive.Clear();
        }

        T CreateInstance()
        {
            T instance = Object.Instantiate(_prefab, _root);
            instance.gameObject.SetActive(false);
            return instance;
        }
    }
}
