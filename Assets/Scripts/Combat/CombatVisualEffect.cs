using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Combat
{
    public sealed class CombatVisualEffect : MonoBehaviour, IPoolable
    {
        private Pool<CombatVisualEffect> pool;
        private ParticleSystem[] particles;
        private SpriteRenderer sprite;
        private Color baseColor;
        private float remaining;
        private float lifetime;

        private void Awake()
        {
            particles = GetComponentsInChildren<ParticleSystem>(true);
            sprite = GetComponent<SpriteRenderer>();
            if (sprite != null) baseColor = sprite.color;
        }

        public void Play(Pool<CombatVisualEffect> source, float duration)
        {
            pool = source;
            remaining = lifetime = Mathf.Max(0.01f, duration);
            foreach (var ps in particles) ps.Play(false);
        }

        public void OnSpawned()
        {
            pool = null;
            if (sprite != null) sprite.color = baseColor;
            foreach (var ps in particles) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void OnDespawned()
        {
            pool = null;
            foreach (var ps in particles) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Update()
        {
            if (pool == null) return;
            remaining -= Time.deltaTime;
            if (sprite != null)
            {
                Color c = baseColor;
                c.a *= Mathf.Clamp01(remaining / lifetime);
                sprite.color = c;
            }
            if (remaining <= 0f) pool.Release(this);
        }
    }
}
