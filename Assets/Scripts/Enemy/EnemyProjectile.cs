using System;
using System.Collections.Generic;
using ShootDemo.Core;
using ShootDemo.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootDemo.Enemy
{
    [DisallowMultipleComponent]
    public sealed class EnemyProjectile : MonoBehaviour, IPoolable
    {
        [SerializeField, Min(0.01f)] private float radius = 0.18f;
        [SerializeField] private LayerMask hitMask = ~0;
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);
        private Pool<EnemyProjectile> pool;
        private Action<Vector2, Vector2> impact;
        private Transform owner;
        private TrailRenderer[] trails;
        private ParticleSystem[] particles;
        private Animator animator;
        private Vector2 direction;
        private float speed;
        private float damage;
        private float remaining;
        public bool IsFlying { get; private set; }

        private void Awake()
        {
            trails = GetComponentsInChildren<TrailRenderer>(true);
            particles = GetComponentsInChildren<ParticleSystem>(true);
            animator = GetComponentInChildren<Animator>();
        }

        public void Launch(Pool<EnemyProjectile> source, Transform instigator, Vector2 heading,
            float travelSpeed, float amount, float lifetime, Action<Vector2, Vector2> hitEffect = null)
        {
            pool = source;
            owner = instigator;
            impact = hitEffect;
            direction = heading.sqrMagnitude > 0.000001f ? heading.normalized : Vector2.right;
            speed = Mathf.Max(0f, travelSpeed);
            damage = Mathf.Max(0f, amount);
            remaining = Mathf.Max(0f, lifetime);
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            IsFlying = true;
            if (animator != null)
            {
                animator.Rebind();
                animator.Play("Base Layer.zidan_outuzhe_fly", 0, 0f);
                animator.Update(0f);
            }
            foreach (var trail in trails) { trail.Clear(); trail.emitting = true; }
        }

        private void Update() { Tick(Time.deltaTime); }

        internal void Tick(float deltaTime)
        {
            var state = GameStateMachine.Instance;
            if (!IsFlying || deltaTime <= 0f || (state != null && state.CurrentState != GameState.Playing)) return;
            float distance = speed * Mathf.Min(deltaTime, remaining);
            Vector2 origin = transform.position;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(hitMask);
            int count = gameObject.scene.GetPhysicsScene2D().CircleCast(origin, radius, direction, distance, filter, hits);
            int nearest = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider == null || (owner != null && (collider.transform == owner || collider.transform.IsChildOf(owner)))
                    || collider.GetComponentInParent<EnemyHealth>() != null) continue;
                if (hits[i].distance < nearestDistance) { nearestDistance = hits[i].distance; nearest = i; }
            }
            if (nearest >= 0)
            {
                var hit = hits[nearest];
                transform.position = origin + direction * hit.distance;
                hit.collider.GetComponentInParent<PlayerHealth>()?.TryTakeDamage(damage);
                impact?.Invoke(hit.point, hit.normal);
                Despawn();
                return;
            }
            transform.position = origin + direction * distance;
            remaining -= deltaTime;
            if (remaining <= 0f) Despawn();
        }

        public void Despawn()
        {
            IsFlying = false;
            if (pool == null || !pool.Release(this)) gameObject.SetActive(false);
        }

        public void OnSpawned()
        {
            pool = null;
            impact = null;
            owner = null;
            IsFlying = false;
            ClearVisuals();
        }

        public void OnDespawned() { IsFlying = false; owner = null; impact = null; ClearVisuals(); }
        private void OnDisable() { IsFlying = false; ClearVisuals(); }

        private void ClearVisuals()
        {
            if (trails != null) foreach (var trail in trails) { trail.emitting = false; trail.Clear(); }
            if (particles != null) foreach (var ps in particles) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
