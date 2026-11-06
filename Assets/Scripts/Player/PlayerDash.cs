using System;
using System.Collections.Generic;
using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Player
{
    /// <summary>Space dodge with a fixed direction and a swept physics body.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement), typeof(Rigidbody2D))]
    public sealed class PlayerDash : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField, Min(0f)] private float collisionSkin = 0.02f;

        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);
        private Rigidbody2D body;
        private PlayerMovement movement;
        private float elapsed;
        private float duration;
        private float distance;
        private float invincibleDuration;
        private bool blocked;

        public bool IsDashing { get; private set; }
        // Damage receivers in B6 must reject damage while this is true.
        public bool IsInvulnerable => isActiveAndEnabled && IsDashing && elapsed < invincibleDuration;
        public float CooldownRemaining { get; private set; }
        public float CooldownDuration => config != null ? Mathf.Max(0f, config.dash.dashCooldown) : 0f;
        public bool CanDash => isActiveAndEnabled && config != null && !IsDashing
            && CooldownRemaining <= 0f && CanAct();
        public Vector2 DashDirection { get; private set; }
        public float Duration => duration;
        public event Action DashStarted;
        public event Action DashEnded;
        public event Action DashAnimationCue;
        public event Action<float, float> CooldownChanged;
        private int reportedCooldown = -1;

        private void ReportCooldown()
        {
            int value = Mathf.CeilToInt(CooldownRemaining * 10f);
            if (value == reportedCooldown) return;
            reportedCooldown = value;
            CooldownChanged?.Invoke(CooldownRemaining, config != null ? config.dash.dashCooldown : 0f);
        }

        // neo2_avoid contains this cue for sound/effect synchronization.
        public void Event_Trigger()
        {
            if (IsDashing)
            {
                DashAnimationCue?.Invoke();
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            movement = GetComponent<PlayerMovement>();
            if (config == null)
            {
                Debug.LogError("PlayerDash needs a GameConfig reference.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (InputService.ReadDashPressed())
            {
                TryDash(InputService.ReadMovement());
            }

            if (IsDashing && !CanAct())
            {
                // Pause freezes the action. Other disabled states cancel it.
                var state = GameStateMachine.Instance;
                if (state == null || state.CurrentState != GameState.Paused)
                {
                    CancelDash();
                }
            }
        }

        public bool TryDash(Vector2 inputDirection)
        {
            if (!CanDash || config.dash.dashDistance <= 0f)
            {
                return false;
            }

            DashDirection = inputDirection.sqrMagnitude > 0.000001f
                ? inputDirection.normalized : movement.Forward;
            duration = Mathf.Max(0.01f, config.dash.dashDuration);
            distance = Mathf.Max(0f, config.dash.dashDistance);
            invincibleDuration = Mathf.Clamp(config.dash.iFrameDuration, 0f, duration);
            CooldownRemaining = Mathf.Max(0f, config.dash.dashCooldown);
            ReportCooldown();
            elapsed = 0f;
            blocked = false;
            body.velocity = Vector2.zero;
            IsDashing = true;
            DashStarted?.Invoke();
            return true;
        }

        // Called by PlayerMovement so normal acceleration cannot overwrite the dodge.
        internal bool Tick(float deltaTime)
        {
            var state = GameStateMachine.Instance;
            if (state != null && state.CurrentState == GameState.Paused)
            {
                return IsDashing;
            }

            deltaTime = Mathf.Max(0f, deltaTime);
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaTime);
            ReportCooldown();
            if (!IsDashing)
            {
                return false;
            }

            if (!CanAct() || elapsed >= duration - 0.000001f)
            {
                CancelDash();
                return false;
            }

            if (deltaTime <= 0f)
            {
                return true;
            }

            float stepTime = Mathf.Min(deltaTime, duration - elapsed);
            float travel = blocked ? 0f : distance / duration * stepTime;
            if (travel > 0f)
            {
                var filter = new ContactFilter2D();
                filter.SetLayerMask(obstacleLayers.value & Physics2D.GetLayerCollisionMask(gameObject.layer));
                filter.useTriggers = false;
                int count = body.Cast(DashDirection, filter, hits, travel + collisionSkin);
                for (int i = 0; i < count; i++)
                {
                    var hit = hits[i];
                    // Allow motion away from or parallel to an existing contact.
                    if (Vector2.Dot(DashDirection, hit.normal) >= -0.0001f)
                    {
                        continue;
                    }

                    float safeDistance = Mathf.Max(0f, hit.distance - collisionSkin);
                    if (safeDistance < travel)
                    {
                        travel = safeDistance;
                        blocked = true;
                    }
                }
            }

            body.velocity = DashDirection * (travel / deltaTime);
            elapsed = Mathf.Min(duration, elapsed + stepTime);
            return true;
        }

        public void CancelDash()
        {
            if (!IsDashing)
            {
                return;
            }

            IsDashing = false;
            if (body != null)
            {
                body.velocity = Vector2.zero;
            }
            DashEnded?.Invoke();
        }

        private bool CanAct()
        {
            if (movement == null || !movement.isActiveAndEnabled || !movement.MovementEnabled)
            {
                return false;
            }

            var state = GameStateMachine.Instance;
            return state == null || state.CurrentState == GameState.Playing;
        }

        private void OnDisable()
        {
            CancelDash();
        }
    }
}
