using System;
using ShootDemo.Combat;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Enemy
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class EnemyHealth : MonoBehaviour, IHitDamageable
    {
        [SerializeField, Min(1f)] private float maxHealth = 30f;
        [SerializeField] private string deathState;
        private Rigidbody2D body;
        private Animator animator;
        public float CurrentHealth { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead => CurrentHealth <= 0f;
        public float DeathAnimationDuration { get; private set; }
        public event Action<float, float> HealthChanged;
        public event Action<DamageInfo> Damaged;
        public event Action Died;

        private void Awake()
        {
            CurrentHealth = Mathf.Max(1f, maxHealth);
            body = GetComponent<Rigidbody2D>();
            animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable() { ResetForSpawn(); }

        public void ResetForSpawn()
        {
            CurrentHealth = Mathf.Max(1f, maxHealth);
            DeathAnimationDuration = 0f;
            if (body != null)
            {
                body.simulated = true;
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.speed = 1f;
                animator.Rebind();
                animator.Update(0f);
            }
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        public bool TryTakeDamage(float amount)
        {
            return TryTakeDamage(new DamageInfo(amount, transform.position, Vector2.zero));
        }

        public bool TryTakeDamage(DamageInfo hit)
        {
            var state = GameStateMachine.Instance;
            if (!isActiveAndEnabled || IsDead || (state != null && state.CurrentState != GameState.Playing)
                || hit.Amount <= 0f || float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount)) return false;
            float amount = Mathf.Min(CurrentHealth, hit.Amount);
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            bool killed = IsDead;
            if (killed)
            {
                if (body != null)
                {
                    body.velocity = Vector2.zero;
                    body.simulated = false;
                }
                if (animator != null && !string.IsNullOrEmpty(deathState))
                {
                    int hash = Animator.StringToHash(deathState);
                    if (animator.HasState(0, hash))
                    {
                        animator.speed = 1f;
                        animator.Play(hash, 0, 0f);
                        animator.Update(0f);
                        DeathAnimationDuration = animator.GetCurrentAnimatorStateInfo(0).length;
                    }
                }
            }
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
            Damaged?.Invoke(new DamageInfo(amount, hit.Point, hit.Direction, hit.Instigator));
            if (killed) Died?.Invoke();
            return true;
        }
    }
}
