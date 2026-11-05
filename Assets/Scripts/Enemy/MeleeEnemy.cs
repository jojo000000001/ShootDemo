using System.Collections.Generic;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Player;
using UnityEngine;

namespace ShootDemo.Enemy
{
    public enum MeleeEnemyState { Idle, Chasing, Windup, Recovery, Dead }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
    [DefaultExecutionOrder(-20)] // Knockback feedback has the final say over body velocity.
    public sealed class MeleeEnemy : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private string animationPrefix = "jiangshi";
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField, Min(0.05f)] private float navigationRadius = 0.75f;
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
        private Vector2 pathTarget;
        private Vector2 attackDirection;
        private int animationHash;
        public MeleeEnemyState State { get; private set; }
        public PlayerHealth Target => target;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<EnemyHealth>();
            feedback = GetComponent<EnemyHitFeedback>();
            animator = GetComponentInChildren<Animator>();
            navigation = new EnemyPathfinder2D(gameObject.scene, navigationRadius, obstacleMask);
            if (config == null)
            {
                Debug.LogError("MeleeEnemy needs a GameConfig reference.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (health != null) health.Damaged += OnDamaged;
            State = MeleeEnemyState.Idle;
            remaining = cooldown = repathRemaining = acquireRemaining = 0f;
            path.Clear();
        }

        public void SetTarget(PlayerHealth value)
        {
            target = value;
            path.Clear();
            repathRemaining = 0f;
            if (State == MeleeEnemyState.Windup) Interrupt();
        }

        private void FixedUpdate() { Tick(Time.fixedDeltaTime); }

        internal void Tick(float deltaTime)
        {
            if (config == null || deltaTime <= 0f) return;
            if (health.IsDead) { State = MeleeEnemyState.Dead; Stop(); return; }
            var game = GameStateMachine.Instance;
            if (game != null && game.CurrentState != GameState.Playing) { Stop(); return; }
            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            if (feedback != null && feedback.IsKnockedBack) return;
            if (target == null || !target.isActiveAndEnabled)
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
                State = MeleeEnemyState.Idle;
                Stop();
                Play("idle");
                return;
            }

            var settings = config.enemy;
            Vector2 delta = (Vector2)target.transform.position - body.position;
            if (State == MeleeEnemyState.Windup)
            {
                Stop();
                remaining -= deltaTime;
                if (remaining <= 0f)
                {
                    State = MeleeEnemyState.Recovery;
                    remaining = settings.meleeRecovery;
                    cooldown = settings.meleeInterval;
                    Play("attack_do", settings.meleeRecovery);
                    // A single strike: running away, dodging or taking cover can make it miss.
                    if (delta.magnitude <= settings.meleeRange
                        && Vector2.Angle(attackDirection, delta) <= settings.meleeHalfAngle
                        && navigation.SegmentClear(body.position, target.transform.position, false))
                        target.TryTakeDamage(settings.meleeDamage);
                }
                return;
            }
            if (State == MeleeEnemyState.Recovery)
            {
                Stop();
                remaining -= deltaTime;
                if (remaining > 0f) return;
                State = MeleeEnemyState.Chasing;
            }
            if (delta.magnitude <= settings.meleeRange
                && navigation.SegmentClear(body.position, target.transform.position, false))
            {
                Face(delta, deltaTime);
                Stop();
                if (cooldown <= 0f && Vector2.Angle(transform.right, delta) <= 12f)
                {
                    State = MeleeEnemyState.Windup;
                    remaining = settings.meleeWindup;
                    attackDirection = transform.right;
                    Play("attack_pre", Mathf.Max(0.01f, remaining));
                }
                else { State = MeleeEnemyState.Idle; Play("idle"); }
                return;
            }

            State = MeleeEnemyState.Chasing;
            Vector2 destination = target.transform.position;
            repathRemaining -= deltaTime;
            if (navigation.SegmentClear(body.position, destination))
            {
                path.Clear();
                path.Add(destination);
            }
            else if (repathRemaining <= 0f && (path.Count == 0 || Vector2.Distance(pathTarget, destination) > 0.75f
                || !navigation.SegmentClear(body.position, path[0])))
            {
                navigation.FindPath(body.position, destination, settings.meleeRange * 0.85f, path);
                pathTarget = destination;
                repathRemaining = settings.meleeRepathInterval;
            }
            while (path.Count > 0 && Vector2.Distance(body.position, path[0]) < 0.15f) path.RemoveAt(0);
            // Skip corners only when the full body fits through the shortcut.
            for (int i = path.Count - 1; i > 0; i--)
                if (navigation.SegmentClear(body.position, path[i])) { path.RemoveRange(0, i); break; }
            if (path.Count == 0) { Stop(); Play("idle"); return; }
            Vector2 move = path[0] - body.position;
            Face(move, deltaTime);
            body.velocity = move.normalized * Mathf.Min(settings.meleeSpeed, move.magnitude / deltaTime);
            Play("run");
        }

        private void Face(Vector2 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            // Rotate the actor's native local +X forward smoothly in the XY plane.
            body.rotation = Mathf.MoveTowardsAngle(body.rotation, angle, config.enemy.meleeTurnSpeed * deltaTime);
        }

        private void Stop() { if (body != null) body.velocity = Vector2.zero; }

        private void OnDamaged(DamageInfo hit)
        {
            // EnemyHealth already selected the death animation before raising Damaged.
            if (health.IsDead)
            {
                State = MeleeEnemyState.Dead;
                if (animator != null) animator.speed = 1f;
                return;
            }
            Interrupt();
        }

        private void Interrupt()
        {
            State = MeleeEnemyState.Idle;
            remaining = 0f;
            cooldown = Mathf.Max(cooldown, config.enemy.meleeRecovery);
            path.Clear();
            repathRemaining = 0f;
            Play("idle");
        }

        private void Play(string suffix, float duration = 0f)
        {
            if (animator == null) return;
            int hash = Animator.StringToHash("Base Layer." + animationPrefix + "_" + suffix);
            if (hash == animationHash) return;
            if (!animator.HasState(0, hash)) return;
            animationHash = hash;
            animator.speed = 1f;
            animator.Play(hash, 0, 0f);
            animator.Update(0f);
            float length = animator.GetCurrentAnimatorStateInfo(0).length;
            if (duration > 0f) animator.speed = length / duration;
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= OnDamaged;
            Stop();
            if (animator != null) animator.speed = 1f;
            animationHash = 0;
            path.Clear();
        }
    }
}
