using System.Globalization;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Combat
{
    [RequireComponent(typeof(TextMesh))]
    public sealed class DamageNumber : MonoBehaviour, IPoolable
    {
        private Pool<DamageNumber> pool;
        private TextMesh[] labels;
        private Color[] colors;
        private float elapsed;
        private float lifetime;
        private float rise;
        private Vector3 origin;

        private void Awake()
        {
            labels = GetComponentsInChildren<TextMesh>(true);
            colors = new Color[labels.Length];
            for (int i = 0; i < labels.Length; i++) colors[i] = labels[i].color;
        }

        public void Show(Pool<DamageNumber> source, float amount, float duration, float riseDistance)
        {
            pool = source;
            lifetime = Mathf.Max(0.01f, duration);
            rise = riseDistance;
            origin = transform.position;
            foreach (var label in labels) label.text = amount.ToString("0.#", CultureInfo.InvariantCulture);
        }

        public void OnSpawned()
        {
            elapsed = 0f;
            pool = null;
            for (int i = 0; i < labels.Length; i++) labels[i].color = colors[i];
        }

        public void OnDespawned()
        {
            pool = null;
        }

        private void Update()
        {
            if (pool == null) return;
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / lifetime);
            transform.position = origin + Vector3.up * (rise * (1f - Mathf.Pow(1f - progress, 2f)));
            for (int i = 0; i < labels.Length; i++)
            {
                Color c = colors[i];
                c.a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, progress));
                labels[i].color = c;
            }
            if (elapsed >= lifetime) pool.Release(this);
        }
    }
}
