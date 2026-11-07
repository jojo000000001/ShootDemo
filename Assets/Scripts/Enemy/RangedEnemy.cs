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
    [RequireComponent(typeof(EnemyAttackVisibility))]
    [DefaultExecutionOrder(-20)]
    public sealed class RangedEnemy : MonoBehaviour, IEnemyBrain
    {
        // Saliva radius plus a margin keeps decisions stable at obstacle edges.
        private const float FiringClearance = 0.26f;
        [SerializeField] private GameConfig config;
        [SerializeField] private Transform muzzle;
        [SerializeField] private LineRenderer warning;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField, Min(0.05f)] private float navigationRadius = 0.71f;
        public float NavigationClearance => EnemyPathfinder2D.BodyClearance(gameObject, navigationRadius);
        private readonly List<Vector2> path = new List<Vector2>();
        private Rigidbody2D body;
        private EnemyHealth health;
        private EnemyHitFeedback feedback;
        private Animator animator;
        private EnemyAttackVisibility attackVisibility;
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
        private bool scheduleManaged;
        private bool decisionDue;
        private bool directApproach;
        private bool hasSight;
        private Vector2 retreatDirection;
        public RangedEnemyState State { get; private set; }
        public PlayerHealth Target => target;
        public bool IsAttacking => State == RangedEnemyState.Windup || State == RangedEnemyState.Firing;
        public int ShotsFired { get; private set; }
        public bool IsWarningVisible => warning != null && warning.enabled;
        public bool UsingPassageEntrance { get; private set; }
        private Vector2 MuzzlePosition => muzzle != null ? (Vector2)muzzle.position : body.position + attackDirection * 0.8f;

        public void SetDecisionSchedule(bool managed, bool due)
        {
            scheduleManaged = managed;
            decisionDue = due;
        }

        private bool ReservePathSearch()
        {
            var registry = EnemyRegistry.Instance;
            return registry == null || registry.gameObject.scene != gameObject.scene
                || registry.Scheduler == null || registry.Scheduler.TryReservePathSearch(this);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<EnemyHealth>();
            feedback = GetComponent<EnemyHitFeedback>();
            animator = GetComponentInChildren<Animator>();
            attackVisibility = GetComponent<EnemyAttackVisibility>();
            navigation = new EnemyPathfinder2D(gameObject.scene, EnemyPathfinder2D.BodyClearance(gameObject, navigationRadius), obstacleMask);
            if (config == null)
            {
                Debug.LogError("RangedEnemy needs a GameConfig reference.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (navigation == null)
                navigation = new EnemyPathfinder2D(gameObject.scene, NavigationClearance, obstacleMask);
            if (health != null) health.Damaged += OnDamaged;
            State = RangedEnemyState.Idle;
            remaining = cooldown = repathRemaining = acquireRemaining = 0f;
            ShotsFired = 0;
            released = false;
            path.Clear();
            directApproach = hasSight = scheduleManaged = decisionDue = false;
            UsingPassageEntrance = false;
            retreatDirection = Vector2.zero;
            HideWarning();
        }

        public void SetTarget(PlayerHealth value)
        {
            if (IsAttacking) Interrupt();
            target = value;
            path.Clear();
            directApproach = hasSight = false;
            retreatDirection = Vector2.zero;
            State = RangedEnemyState.Idle;
            repathRemaining = 0f;
            HideWarning();
        }

        private void FixedUpdate() { Tick(Time.fixedDeltaTime); }

        internal void Tick(float deltaTime)
        {
            if (config == null || deltaTime <= 0f) return;
            if (health.IsDead) { State = RangedEnemyState.Dead; Stop(); HideWarning(); return; }
            var game = GameStateMachine.Instance;
            if (game != null && game.CurrentState != GameState.Playing) { Stop(); return; }
            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            repathRemaining -= deltaTime;
            bool think = !scheduleManaged || decisionDue;
            decisionDue = false;
            if (feedback != null && feedback.IsKnockedBack) return;
            if (target == null || !target.isActiveAndEnabled || target.IsDead)
            {
                acquireRemaining -= deltaTime;
                if (acquireRemaining <= 0f && think)
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

            if (attackVisibility != null && !attackVisibility.CanAttack) CancelUnreleasedAttack();

            bool guided = EnemyNarrowPassage.Guide(health, target.transform.position, out Vector2 passagePoint);
            UsingPassageEntrance = guided;
            if (guided)
            {
                CancelUnreleasedAttack();
                State = RangedEnemyState.Approaching;
                Approach(passagePoint, deltaTime, think, 0.2f);
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
            if (think) DecideMovement(delta, distance, deltaTime);
            if (State == RangedEnemyState.Approaching)
            {
                Approach(target.transform.position, deltaTime, think);
                return;
            }
            if (State == RangedEnemyState.Retreating)
            {
                // Reapply raw intent each step; crowd steering must never feed back into the AI direction.
                body.velocity = retreatDirection * settings.rangedSpeed;
                Face(delta, deltaTime);
                Play("walk");
                return;
            }
            Stop();
            Face(delta, deltaTime);
            if ((attackVisibility == null || attackVisibility.CanAttack)
                && hasSight && distance <= settings.rangedMaxDistance && cooldown <= 0f
                && Vector2.Angle(transform.right, delta) <= 12f
                && navigation.SegmentClear(body.position, target.transform.position, FiringClearance))
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

        private void DecideMovement(Vector2 delta, float distance, float deltaTime)
        {
            var settings = config.enemy;
            hasSight = navigation.SegmentClear(body.position, target.transform.position, FiringClearance);
            if (!hasSight) { State = RangedEnemyState.Approaching; return; }
            float retreatUntil = State == RangedEnemyState.Retreating
                ? settings.rangedKeepDistance : settings.rangedKeepDistance - settings.rangedDistanceTolerance;
            if (distance < retreatUntil && TryRetreat(-delta, deltaTime))
            { State = RangedEnemyState.Retreating; return; }
            float approachUntil = State == RangedEnemyState.Approaching
                ? settings.rangedKeepDistance : settings.rangedKeepDistance + settings.rangedDistanceTolerance;
            State = distance > approachUntil ? RangedEnemyState.Approaching : RangedEnemyState.Idle;
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
                if (score < 0.1f || score <= best
                    || !EnemyNarrowPassage.AllowsRetreat(gameObject.scene, body.position, next, target.transform.position)
                    || !navigation.SegmentClear(body.position, next)
                    || !navigation.SegmentClear(next, target.transform.position, FiringClearance)) continue;
                best = score;
                direction = candidate;
            }
            if (direction == Vector2.zero) return false;
            retreatDirection = direction;
            return true;
        }

        private void Approach(Vector2 destination, float deltaTime, bool think, float stopDistance = -1f)
        {
            if (think)
            {
                directApproach = navigation.SegmentClear(body.position, destination);
                if (directApproach) path.Clear();
                else if (repathRemaining <= 0f && ReservePathSearch())
                {
                    bool found = navigation.FindPath(body.position, destination,
                        stopDistance >= 0f ? stopDistance : Mathf.Max(1f, config.enemy.rangedKeepDistance - config.enemy.rangedDistanceTolerance), path,
                        FiringClearance, stopDistance < 0f);
                    repathRemaining = found ? config.enemy.meleeRepathInterval : Mathf.Max(2f, config.enemy.meleeRepathInterval);
                }
                for (int i = Mathf.Min(path.Count - 1, 8); i > 0; i--)
                    if (navigation.SegmentClear(body.position, path[i])) { path.RemoveRange(0, i); break; }
            }
            if (directApproach) { path.Clear(); path.Add(destination); }
            while (path.Count > 0 && Vector2.Distance(body.position, path[0]) < (stopDistance >= 0f ? 0.03f : 0.15f)) path.RemoveAt(0);
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

        internal void CancelUnreleasedAttack()
        {
            if (State == RangedEnemyState.Windup || (State == RangedEnemyState.Firing && !released)) Interrupt();
        }

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
            directApproach = hasSight = false;
            retreatDirection = Vector2.zero;
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
