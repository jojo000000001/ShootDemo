using System;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private GameConfig config;
        private PlayerDash dash;
        private float hurtProtectionRemaining;

        public float CurrentHealth { get; private set; }
        public float MaxHealth { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsInvulnerable => !IsDead && (hurtProtectionRemaining > 0f
            || (dash != null && dash.IsInvulnerable));
        public event Action<float, float> HealthChanged;
        public event Action<float> Damaged;
        public event Action Died;

        private void Awake()
        {
            dash = GetComponent<PlayerDash>();
            if (config == null)
            {
                Debug.LogError("PlayerHealth needs a GameConfig reference.", this);
                enabled = false;
                return;
            }

            MaxHealth = Mathf.Max(1f, config.playerHealth.maxHealth);
            CurrentHealth = MaxHealth;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        internal void Tick(float deltaTime)
        {
            if (IsPlaying())
            {
                hurtProtectionRemaining = Mathf.Max(0f, hurtProtectionRemaining - Mathf.Max(0f, deltaTime));
            }
        }

        public bool TryTakeDamage(float amount)
        {
            if (!isActiveAndEnabled || config == null || IsDead || IsInvulnerable || !IsPlaying()
                || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            {
                return false;
            }

            float damage = Mathf.Min(CurrentHealth, amount);
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            hurtProtectionRemaining = Mathf.Max(0f, config.playerHealth.hurtInvulnerability);
            bool diedThisHit = CurrentHealth <= 0f;
            if (diedThisHit)
            {
                IsDead = true;
                hurtProtectionRemaining = 0f;
                StopPlayer();
                GameStateMachine.Instance?.TryChangeState(GameState.GameOver);
            }

            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
            CombatAudio.Instance?.PlayerHit(diedThisHit);
            Damaged?.Invoke(damage);
            if (diedThisHit)
            {
                Died?.Invoke();
            }
            return true;
        }

        private void StopPlayer()
        {
            var movement = GetComponent<PlayerMovement>();
            if (movement != null)
            {
                movement.SetMovementEnabled(false, true);
                movement.enabled = false;
            }
            if (dash != null)
            {
                dash.CancelDash();
                dash.enabled = false;
            }
            var body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }
            var aim = GetComponent<PlayerAim>();
            if (aim != null) aim.enabled = false;
            var weapon = GetComponent<PlayerWeapon>();
            if (weapon != null) weapon.enabled = false;
            var locomotion = GetComponent<PlayerLocomotionAnimation>();
            if (locomotion != null) locomotion.enabled = false;
        }

        private static bool IsPlaying()
        {
            var state = GameStateMachine.Instance;
            return state == null || state.CurrentState == GameState.Playing;
        }

        [ContextMenu("Debug/Take 10 Damage (Play Mode)")]
        private void DebugTakeDamage()
        {
            if (!Application.isPlaying) return;
            bool accepted = TryTakeDamage(10f);
            Debug.Log("Player HP: " + CurrentHealth + "/" + MaxHealth
                + (accepted ? " (damage applied)" : " (damage blocked)"), this);
        }
    }
}
