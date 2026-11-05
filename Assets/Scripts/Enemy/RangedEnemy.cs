using System.Collections.Generic;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Player;
using UnityEngine;

namespace ShootDemo.Enemy
{
    public enum RangedEnemyState { Idle, Approaching, Retreating, Windup, Firing, Dead }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth), typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-20)]
    public sealed class RangedEnemy : MonoBehaviour
    {
        // Saliva radius plus a margin keeps decisions stable at obstacle edges.
        private const float FiringClearance = 0.26f;
        [SerializeField] private GameConfig config;
        [SerializeField] private Transform muzzle;
        [SerializeField] private LineRenderer warning;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField, Min(0.05f)] private float navigationRadius = 0.71f;
        private readonly List<Vector2> path = new List<Vector2>();
        private Rigidbody2D body;
        private EnemyHealth health;
        private EnemyHitFeedback feedback;
        private Animator animator;
        private EnemyPathfinder2D navigation;
        private PlayerHealth target;
        private float remaining;
        private float cooldown;
        private float repathRemaining;
        private float acquireRemaining;
        private bool released;
        private Vector2 attackDirection;
        private float warningLength;
        private int animationHash;
        public RangedEnemyState State { get; private set; }
        public PlayerHealth Target => target;
        public int ShotsFired { get; private set; }
        public bool IsWarningVisible => warning != null && warning.enabled;
        private Vector2 MuzzlePosition => muzzle != null ? (Vector2)muzzle.position : body.position + attackDirection * 0.8f;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<EnemyHealth>();
            feedback = GetComponent<EnemyHitFeedback>();
            animator = GetComponentInChildren<Animator>();
            navigation = new EnemyPathfinder2D(gameObject.scene, navigationRadius, obstacleMask);
            if (config == null)
            {
                Debug.LogError("RangedEnemy needs a GameConfig reference.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (health != null) health.Damaged += OnDamaged;
            State = RangedEnemyState.Idle;
            remaining = cooldown = repathRemaining = acquireRemaining = 0f;
            ShotsFired = 0;
            released = false;
            path.Clear();
            HideWarning();
        }

        public void SetTarget(PlayerHealth value)
        {
            target = value;
            path.Clear();
            repathRemaining = 0f;
            if (State == RangedEnemyState.Windup || State == RangedEnemyState.Firing) Interrupt();
        }

        private void FixedUpdate() { Tick(Time.fixedDeltaTime); }

        internal void Tick(float deltaTime)
        {
            if (config == null || deltaTime <= 0f) return;
            if (health.IsDead) { State = RangedEnemyState.Dead; Stop(); HideWarning(); return; }
            var game = GameStateMachine.Instance;
            if (game != null && game.CurrentState != GameState.Playing) { Stop(); return; }
            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            if (feedback != null && feedback.IsKnockedBack) return;
            if (target == null || !target.isActiveAndEnabled || target.IsDead)
            {
                acquireRemaining -= deltaTime;
                if (acquireRemaining <= 0f)
                {
                    acquireRemaining = 1f;
                    foreach (var player in FindObjectsOfType<PlayerHealth>())
                        if (player.gameObject.scene == gameObject.scene && !player.IsDead) { target = player; break; }
                }
            }
            if (target == null || !target.isActiveAndEnabled || target.IsDead)
            {
                State = RangedEnemyState.Idle;
                Stop();
                HideWarning();
                Play("idle");
                return;
            }

            var settings = config.enemy;
            Vector2 delta = (Vector2)target.transform.position - body.position;
            if (State == RangedEnemyState.Windup)
            {
                Stop();
                remaining -= deltaTime;
                DrawWarning();
                if (remaining <= 0f)
                {
                    State = RangedEnemyState.Firing;
                    remaining = settings.rangedRecovery;
                    released = false;
                    Play("attack_do", settings.rangedRecovery);
                }
                return;
            }
            if (State == RangedEnemyState.Firing)
            {
                Stop();
                remaining -= deltaTime;
                // The authored spit event is one third into outuzhe_attack_do.
                if (!released && remaining <= settings.rangedRecovery * (2f / 3f))
                {
                    released = true;
                    HideWarning();
                    cooldown = 1f / Mathf.Max(0.01f, settings.rangedFireRate);
                    Vector2 origin = MuzzlePosition;
                    // Check the full body-to-mouth segment so an animated mouth cannot spawn a shot through a wall.
                    if (navigation.SegmentClear(body.position, origin, false)
                        && EnemyProjectiles.Instance != null
                        && EnemyProjectiles.Instance.gameObject.scene == gameObject.scene
                        && EnemyProjectiles.Instance.Fire(transform, origin, attackDirection) != null) ShotsFired++;
                }
                if (remaining > 0f) return;
                State = RangedEnemyState.Idle;
            }

            float distance = delta.magnitude;
            // Finding a firing lane takes priority over keeping distance. Otherwise routing
            // toward a corner and retreating from the player undo each other every fixed step.
            bool sight = navigation.SegmentClear(body.position, target.transform.position, FiringClearance);
            if (!sight)
            {
                State = RangedEnemyState.Approaching;
                Approach(target.transform.position, deltaTime);
                return;
            }
            float retreatUntil = State == RangedEnemyState.Retreating
                ? settings.rangedKeepDistance
                : settings.rangedKeepDistance - settings.rangedDistanceTolerance;
            if (distance < retreatUntil
                && TryRetreat(-delta, deltaTime))
            {
                State = RangedEnemyState.Retreating;
                return;
            }
            float approachUntil = State == RangedEnemyState.Approaching
                ? settings.rangedKeepDistance
                : settings.rangedKeepDistance + settings.rangedDistanceTolerance;
            if (distance > approachUntil)
            {
                State = RangedEnemyState.Approaching;
                Approach(target.transform.position, deltaTime);
                return;
            }
            Stop();
            Face(delta, deltaTime);
            if (distance <= settings.rangedMaxDistance && cooldown <= 0f
                && Vector2.Angle(transform.right, delta) <= 12f)
            {
                State = RangedEnemyState.Windup;
                remaining = settings.rangedWindup;
                attackDirection = delta.sqrMagnitude > 0.0001f ? delta.normalized : (Vector2)transform.right;
                warningLength = Mathf.Min(settings.rangedMaxDistance, distance);
                Play("attack_pre", Mathf.Max(0.01f, remaining));
                DrawWarning();
            }
            else { State = RangedEnemyState.Idle; Play("idle"); }
        }

        private bool TryRetreat(Vector2 away, float deltaTime)
        {
            if (away.sqrMagnitude < 0.0001f) away = -transform.right;
            away.Normalize();
            float best = float.NegativeInfinity;
            Vector2 direction = Vector2.zero;
            // Try the retreat direction, then safe side steps. Do not walk toward the player when cornered.
            for (int i = -4; i <= 4; i++)
            {
                Vector2 candidate = Quaternion.Euler(0f, 0f, i * 22.5f) * away;
                float score = Vector2.Dot(candidate, away);
                Vector2 next = body.position + candidate * (config.enemy.rangedSpeed * deltaTime + 0.25f);
                if (score < -0.001f || score <= best || !navigation.SegmentClear(body.position, next)
                    || !navigation.SegmentClear(next, target.transform.position, FiringClearance)) continue;
                best = score;
                direction = candidate;
            }
            if (direction == Vector2.zero) return false;
            body.velocity = direction * config.enemy.rangedSpeed;
            Face((Vector2)target.transform.position - body.position, deltaTime);
            Play("walk");
            return true;
        }

        private void Approach(Vector2 destination, float deltaTime)
        {
            repathRemaining -= deltaTime;
            if (navigation.SegmentClear(body.position, destination)) { path.Clear(); path.Add(destination); }
            else if (repathRemaining <= 0f)
            {
                navigation.FindPath(body.position, destination,
                    Mathf.Max(1f, config.enemy.rangedKeepDistance - config.enemy.rangedDistanceTolerance), path,
                    FiringClearance);
                repathRemaining = config.enemy.meleeRepathInterval;
            }
            while (path.Count > 0 && Vector2.Distance(body.position, path[0]) < 0.15f) path.RemoveAt(0);
            for (int i = path.Count - 1; i > 0; i--)
                if (navigation.SegmentClear(body.position, path[i])) { path.RemoveRange(0, i); break; }
            if (path.Count == 0) { Stop(); Play("idle"); return; }
            Vector2 move = path[0] - body.position;
            Face(move, deltaTime);
            body.velocity = move.normalized * Mathf.Min(config.enemy.rangedSpeed, move.magnitude / deltaTime);
            Play("run");
        }

        private void Face(Vector2 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            body.rotation = Mathf.MoveTowardsAngle(body.rotation, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg,
                config.enemy.meleeTurnSpeed * deltaTime);
        }

        private void DrawWarning()
        {
            if (warning == null) return;
            warning.enabled = true;
            Vector2 origin = MuzzlePosition;
            warning.SetPosition(0, new Vector3(origin.x, origin.y, 0f));
            Vector2 end = body.position + attackDirection * warningLength;
            warning.SetPosition(1, new Vector3(end.x, end.y, 0f));
        }

        private void HideWarning() { if (warning != null) warning.enabled = false; }
        private void Stop() { if (body != null) body.velocity = Vector2.zero; }

        private void OnDamaged(DamageInfo hit)
        {
            if (health.IsDead)
            {
                State = RangedEnemyState.Dead;
                HideWarning();
                if (animator != null) animator.speed = 1f;
                return;
            }
            Interrupt();
        }

        private void Interrupt()
        {
            State = RangedEnemyState.Idle;
            remaining = 0f;
            cooldown = Mathf.Max(cooldown, config.enemy.rangedRecovery);
            path.Clear();
            repathRemaining = 0f;
            HideWarning();
            Play("idle");
        }

        private void Play(string suffix, float duration = 0f)
        {
            if (animator == null) return;
            int hash = Animator.StringToHash("Base Layer.outuzhe_" + suffix);
            if (hash == animationHash || !animator.HasState(0, hash)) return;
            animationHash = hash;
            animator.speed = 1f;
            animator.Play(hash, 0, 0f);
            animator.Update(0f);
            if (duration > 0f) animator.speed = animator.GetCurrentAnimatorStateInfo(0).length / duration;
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= OnDamaged;
            Stop();
            HideWarning();
            if (animator != null) animator.speed = 1f;
            animationHash = 0;
            path.Clear();
            target = null;
        }
    }
}
