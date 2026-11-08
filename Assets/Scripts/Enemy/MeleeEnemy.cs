using System.Collections.Generic;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Player;
using Unity.Profiling;
using UnityEngine;

namespace ShootDemo.Enemy
{
    public enum MeleeEnemyState { Idle, Chasing, Windup, Recovery, Dead }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
    [DefaultExecutionOrder(-20)] // Knockback feedback has the final say over body velocity.
    public sealed class MeleeEnemy : MonoBehaviour, IEnemyBrain
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private string animationPrefix = "jiangshi";
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField, Min(0.05f)] private float navigationRadius = 0.75f;
        public float NavigationClearance => EnemyPathfinder2D.BodyClearance(gameObject, navigationRadius);
        internal int NavigationObstacleMask => obstacleMask.value;
        private readonly List<Vector2> path = new List<Vector2>();
        private Rigidbody2D body;
        private EnemyHealth health;
        private EnemyHitFeedback feedback;
        private Animator animator;
        private EnemyPathfinder2D navigation;
        private PlayerHealth target;
        private float remaining;
        private float cooldown;
        private float acquireRemaining;
        private Vector2 attackDirection;
        private EnemyAnimationPlayer animationPlayer;
        private bool scheduleManaged;
        private bool decisionDue;
        private bool directChase;
        private bool attackVisibilityValid;
        private bool attackVisible;
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("Enemies.Melee.Tick");
        private static readonly ProfilerMarker VisibilityMarker = new ProfilerMarker("Enemies.Melee.AttackVisibility");
        private static readonly ProfilerMarker HitVisibilityMarker = new ProfilerMarker("Enemies.Melee.HitVisibility");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Benchmark-only A/B switch; ordinary gameplay always uses the scheduled cache.
        internal static bool BenchmarkUncachedAttackVisibility;
#endif
        public bool FollowingPath => !directChase && path.Count > 0;
        public bool UsingPassageEntrance { get; private set; }
        public MeleeEnemyState State { get; private set; }
        public PlayerHealth Target => target;
        public bool IsAttacking => State == MeleeEnemyState.Windup || State == MeleeEnemyState.Recovery;

        public void SetDecisionSchedule(bool managed, bool due)
        {
            scheduleManaged = managed;
            decisionDue = due;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<EnemyHealth>();
            feedback = GetComponent<EnemyHitFeedback>();
            animator = GetComponentInChildren<Animator>();
            animationPlayer = new EnemyAnimationPlayer(animator, animationPrefix);
            // Enemy root motion is disabled; fully stop evaluating sprite-part clips off camera.
            if (animator != null) animator.cullingMode = AnimatorCullingMode.CullCompletely;
            navigation = new EnemyPathfinder2D(gameObject.scene, EnemyPathfinder2D.BodyClearance(gameObject, navigationRadius), obstacleMask);
            if (config == null)
            {
                Debug.LogError("MeleeEnemy needs a GameConfig reference.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            // Plain C# search state is not restored by an editor domain reload.
            if (navigation == null)
                navigation = new EnemyPathfinder2D(gameObject.scene, NavigationClearance, obstacleMask);
            if (animationPlayer == null)
                animationPlayer = new EnemyAnimationPlayer(animator, animationPrefix);
            if (health != null) health.Damaged += OnDamaged;
            State = MeleeEnemyState.Idle;
            remaining = cooldown = acquireRemaining = 0f;
            path.Clear();
            directChase = scheduleManaged = decisionDue = false;
            attackVisibilityValid = false;
            UsingPassageEntrance = false;
        }

        public void SetTarget(PlayerHealth value)
        {
            target = value;
            path.Clear();
            directChase = false;
            attackVisibilityValid = false;
            if (State == MeleeEnemyState.Windup) Interrupt();
        }

        private void FixedUpdate() { Tick(Time.fixedDeltaTime); }

        internal void Tick(float deltaTime)
        {
            using (TickMarker.Auto()) TickBrain(deltaTime);
        }

        private void TickBrain(float deltaTime)
        {
            if (config == null || deltaTime <= 0f) return;
            if (health.IsDead) { State = MeleeEnemyState.Dead; Stop(); return; }
            var game = GameStateMachine.Instance;
            if (game != null && game.CurrentState != GameState.Playing) { attackVisibilityValid = false; Stop(); return; }
            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            bool think = !scheduleManaged || decisionDue;
            decisionDue = false;
            if (feedback != null && feedback.IsKnockedBack) return;
            if (target == null || !target.isActiveAndEnabled)
            {
                attackVisibilityValid = false;
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
                State = MeleeEnemyState.Idle;
                Stop();
                animationPlayer?.Play(EnemyAnimation.Idle);
                return;
            }

            Vector2 destination = target.transform.position;
            bool guided = EnemyNarrowPassage.Guide(health, destination, out Vector2 passagePoint);
            UsingPassageEntrance = guided;
            if (guided) destination = passagePoint;

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
                    animationPlayer?.Play(EnemyAnimation.AttackDo, settings.meleeRecovery);
                    // A single strike: running away, dodging or taking cover can make it miss.
                    if (delta.magnitude <= settings.meleeRange
                        && Vector2.Angle(attackDirection, delta) <= settings.meleeHalfAngle
                        && HasAttackVisibility(true))
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
            bool inAttackRange = !guided && delta.sqrMagnitude <= settings.meleeRange * settings.meleeRange;
            if (!inAttackRange) attackVisibilityValid = false;
            bool refreshVisibility = think || !attackVisibilityValid;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            refreshVisibility |= BenchmarkUncachedAttackVisibility;
#endif
            if (inAttackRange && refreshVisibility)
            {
                attackVisible = HasAttackVisibility(false);
                attackVisibilityValid = true;
            }
            if (inAttackRange && attackVisible)
            {
                Face(delta, deltaTime);
                Stop();
                if (cooldown <= 0f && Vector2.Angle(transform.right, delta) <= 12f)
                {
                    State = MeleeEnemyState.Windup;
                    remaining = settings.meleeWindup;
                    attackDirection = transform.right;
                    animationPlayer?.Play(EnemyAnimation.AttackPre, Mathf.Max(0.01f, remaining));
                }
                else { State = MeleeEnemyState.Idle; animationPlayer?.Play(EnemyAnimation.Idle); }
                return;
            }

            State = MeleeEnemyState.Chasing;
            if (think)
            {
                directChase = navigation.SegmentClear(body.position, destination);
            }
            while (path.Count > 0 && Vector2.Distance(body.position, path[0]) < (guided ? 0.03f : 0.15f)) path.RemoveAt(0);
            if (directChase)
            {
                path.Clear();
                path.Add(destination);
            }
            else if (think || path.Count == 0)
            {
                path.Clear();
                Vector2 goal = destination;
                if (guided && EnemyNarrowPassage.TryGetEntranceRoute(gameObject.scene, target.transform.position,
                    out Vector2 outside, out _)) goal = outside;
                if (EnemyFlowFieldWorld.ForScene(gameObject.scene).TryGetWaypoint(body.position, goal,
                    navigation.Clearance, obstacleMask, guided, navigation, out Vector2 next)) path.Add(next);
            }
            if (path.Count == 0) { Stop(); animationPlayer?.Play(EnemyAnimation.Idle); return; }
            Vector2 move = path[0] - body.position;
            Face(move, deltaTime);
            body.velocity = move.normalized * Mathf.Min(settings.meleeSpeed, move.magnitude / deltaTime);
            animationPlayer?.Play(EnemyAnimation.Run);
        }

        private bool HasAttackVisibility(bool committingHit)
        {
            using ((committingHit ? HitVisibilityMarker : VisibilityMarker).Auto())
                return navigation.SegmentClear(body.position, target.transform.position, false);
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
            directChase = false;
            attackVisibilityValid = false;
            animationPlayer?.Play(EnemyAnimation.Idle);
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= OnDamaged;
            Stop();
            if (animator != null) animator.speed = 1f;
            animationPlayer?.Reset();
            path.Clear();
        }
    }
}
