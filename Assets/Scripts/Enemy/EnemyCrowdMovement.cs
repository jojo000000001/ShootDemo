using System.Collections.Generic;
using ShootDemo.Config;
using ShootDemo.Core;
using Unity.Profiling;
using UnityEngine;

namespace ShootDemo.Enemy
{
    /// <summary>Combines AI intent with bounded soft separation, then sweeps the real body against walls.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(EnemyLifecycle))]
    [DefaultExecutionOrder(20)] // Runs after AI and knockback, before the physics step.
    public sealed class EnemyCrowdMovement : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        private static readonly ProfilerMarker QueryMarker = new ProfilerMarker("Enemies.SpatialGrid.Neighbors");
        private readonly List<EnemyLifecycle> neighbors = new List<EnemyLifecycle>(64);
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);
        private Rigidbody2D body;
        private EnemyLifecycle lifecycle;
        private EnemyHitFeedback feedback;
        private MeleeEnemy melee;
        private RangedEnemy ranged;
        private ContactFilter2D filter;
        private Vector2 separation;
        private Vector2 desiredSeparation;
        private bool scheduleManaged;
        private bool decisionDue;
        private bool obstacleCached;
        private Vector2 probePosition;
        private Vector2 obstacleNormal;
        private Vector2 obstacleSlide;
        private float obstacleDistance;
        private float probeLength;
        private float wallSide;
        private float entranceForwardLimit = float.PositiveInfinity;
        private Vector2 entranceForwardAxis;
        public int LastNeighborCount { get; private set; }
        public int LastCandidateChecks { get; private set; }

        public void SetDecisionSchedule(bool managed, bool due)
        {
            scheduleManaged = managed;
            decisionDue = due;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            lifecycle = GetComponent<EnemyLifecycle>();
            feedback = GetComponent<EnemyHitFeedback>();
            melee = GetComponent<MeleeEnemy>();
            ranged = GetComponent<RangedEnemy>();
            filter = new ContactFilter2D { useTriggers = false };
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            filter.SetLayerMask(enemyLayer >= 0 ? ~(1 << enemyLayer) : ~0);
            if (config == null)
            {
                Debug.LogError("EnemyCrowdMovement needs a GameConfig reference.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            separation = Vector2.zero;
            desiredSeparation = Vector2.zero;
            obstacleCached = scheduleManaged = decisionDue = false;
            wallSide = 0f;
            entranceForwardLimit = float.PositiveInfinity;
            LastNeighborCount = LastCandidateChecks = 0;
        }

        private void FixedUpdate() { Tick(Time.fixedDeltaTime); }

        internal void Tick(float deltaTime)
        {
            if (config == null || deltaTime <= 0f || body == null || !body.simulated || lifecycle.Health.IsDead) return;
            var state = GameStateMachine.Instance;
            if (state != null && state.CurrentState != GameState.Playing) { body.velocity = Vector2.zero; return; }
            bool knockedBack = feedback != null && feedback.IsKnockedBack;
            bool attacking = (melee != null && melee.enabled
                    && (melee.State == MeleeEnemyState.Windup || melee.State == MeleeEnemyState.Recovery))
                || (ranged != null && ranged.enabled
                    && (ranged.State == RangedEnemyState.Windup || ranged.State == RangedEnemyState.Firing));
            Vector2 intent = body.velocity;
            bool entering = (melee != null && melee.UsingPassageEntrance) || (ranged != null && ranged.UsingPassageEntrance);
            bool think = !scheduleManaged || decisionDue;
            decisionDue = false;
            LastNeighborCount = LastCandidateChecks = 0;
            if (!knockedBack && entering)
            {
                // Entrance guidance does not reserve space: retain the bounded neighbor
                // separation used elsewhere, or every actor converges on the same point.
                if (think)
                {
                    desiredSeparation = CalculateSeparation(true);
                    entranceForwardLimit = EntranceSpeedLimit(intent);
                }
                separation = Vector2.MoveTowards(separation, desiredSeparation, 10f * deltaTime);
                Vector2 spacing = EnemyNarrowPassage.ConstrainEntranceSeparation(gameObject.scene, body.position, separation);
                // Do not force minimum forward motion into a crowded doorway or add wall
                // steering that pulls the actor away from the authored entrance route.
                Vector2 combined = intent + spacing;
                Vector2 forward = entranceForwardAxis;
                float progress = Vector2.Dot(combined, forward);
                if (progress > entranceForwardLimit) combined -= forward * (progress - entranceForwardLimit);
                body.velocity = ConstrainVelocity(Vector2.ClampMagnitude(combined,
                    Mathf.Max(intent.magnitude, config.crowd.maxSeparationSpeed)), deltaTime);
                ResolveEntranceOverlap(deltaTime);
                return;
            }
            entranceForwardLimit = float.PositiveInfinity;
            if (!knockedBack && !attacking)
            {
                if (think) desiredSeparation = CalculateSeparation();
                separation = Vector2.MoveTowards(separation, desiredSeparation, 10f * deltaTime);
                Vector2 combined = intent + separation;
                if (intent.sqrMagnitude > 0.0001f)
                {
                    // Slow down and fan around a crowded front instead of driving through it.
                    Vector2 forward = intent.normalized;
                    float progress = Vector2.Dot(combined, forward);
                    Vector2 side = new Vector2(-forward.y, forward.x);
                    float obstruction = Mathf.Clamp01(1f - progress / intent.magnitude);
                    float lateral = Vector2.Dot(combined, side);
                    float minimumLateral = intent.magnitude * 0.65f * obstruction;
                    if (Mathf.Abs(lateral) < minimumLateral)
                    {
                        float sign = Mathf.Abs(lateral) > 0.05f ? Mathf.Sign(lateral) : ((GetInstanceID() & 1) == 0 ? 1f : -1f);
                        combined += side * (sign * minimumLateral - lateral);
                    }
                    float minimum = intent.magnitude * 0.1f;
                    if (progress < minimum) combined += forward * (minimum - progress);
                    combined = Vector2.ClampMagnitude(combined, intent.magnitude);
                }
                // A* already selected a side of the obstacle. Local wall steering must
                // not reverse that route at a corner and send the agent back and forth.
                if (melee != null && melee.FollowingPath)
                {
                    obstacleCached = false;
                    intent = combined;
                }
                else intent = AvoidObstacle(combined, deltaTime, think);
            }
            else
            {
                separation = Vector2.zero;
                desiredSeparation = Vector2.zero;
                obstacleCached = false;
                LastNeighborCount = LastCandidateChecks = 0;
            }
            // This sweep also protects knockback and stationary attackers from unintended wall penetration.
            body.velocity = ConstrainVelocity(intent, deltaTime);
        }

        private Vector2 CalculateSeparation(bool entranceFlow = false)
        {
            LastNeighborCount = LastCandidateChecks = 0;
            var registry = EnemyRegistry.Instance;
            if (registry == null || registry.gameObject.scene != gameObject.scene || registry.Grid == null) return Vector2.zero;
            var grid = registry.Grid;
            if (!grid.TryGetPosition(lifecycle, out Vector2 position)) return Vector2.zero;
            var settings = config.crowd;
            using (QueryMarker.Auto())
            {
                grid.Query(position, settings.neighborRadius, neighbors, lifecycle, settings.maxNeighbors, settings.maxCandidates);
                LastCandidateChecks = grid.LastCandidateChecks;
            }
            Vector2 force = Vector2.zero;
            for (int i = 0; i < neighbors.Count; i++)
            {
                var other = neighbors[i];
                if (other == null || other.Health.IsDead || !other.isActiveAndEnabled
                    || !grid.TryGetPosition(other, out Vector2 otherPosition)) continue;
                if (entranceFlow && HasClearedEntrance(other)) continue;
                float desiredDistance = (lifecycle.Radius + other.Radius) * settings.spacing;
                force += PairSeparation(position - otherPosition, desiredDistance,
                    lifecycle.GetInstanceID(), other.GetInstanceID());
                LastNeighborCount++;
            }
            return Vector2.ClampMagnitude(force * settings.separationStrength, settings.maxSeparationSpeed);
        }

        private bool HasClearedEntrance(EnemyLifecycle other)
        {
            var target = melee != null ? melee.Target : ranged != null ? ranged.Target : null;
            return target != null && other.Brain != null
                && EnemyNarrowPassage.HasClearedEntrance(gameObject.scene, other.Position, target.transform.position);
        }

        private float EntranceSpeedLimit(Vector2 intent)
        {
            entranceForwardAxis = intent.normalized;
            if (intent.sqrMagnitude < 0.0001f) return float.PositiveInfinity;
            var target = melee != null ? melee.Target : ranged != null ? ranged.Target : null;
            Vector2 outside = body.position, entrance = body.position;
            bool passage = target != null && EnemyNarrowPassage.TryGetEntranceRoute(gameObject.scene, target.transform.position,
                out outside, out entrance);
            // Initialize for callers without an authored passage (including isolated crowd tests).
            if (!passage) outside = entrance = body.position;
            else entranceForwardAxis = (entrance - outside).normalized;
            // Merge traffic follows the passage axis. Using each actor's diagonal
            // direction makes two side-by-side actors consider each other the leader.
            Vector2 forward = entranceForwardAxis;
            Vector2 side = new Vector2(-forward.y, forward.x);
            if (passage && Mathf.Abs(Vector2.Dot(body.position - outside, side)) > 0.4f)
                return float.PositiveInfinity;
            float limit = float.PositiveInfinity;
            foreach (var other in neighbors)
            {
                if (other == null || other.Health.IsDead || !other.isActiveAndEnabled) continue;
                if (HasClearedEntrance(other)) continue;
                if (passage && Mathf.Abs(Vector2.Dot(other.Position - outside, side)) > 0.4f) continue;
                Vector2 delta = other.Position - body.position;
                float ahead = Vector2.Dot(delta, forward);
                if (ahead <= 0.001f) continue;
                float desired = (lifecycle.Radius + other.Radius) * config.crowd.spacing;
                float sideSquared = Mathf.Max(0f, delta.sqrMagnitude - ahead * ahead);
                if (sideSquared >= desired * desired) continue;
                float gap = ahead - Mathf.Sqrt(desired * desired - sideSquared);
                var otherBody = other.GetComponent<Rigidbody2D>();
                float leaderSpeed = otherBody != null ? Mathf.Max(0f, Vector2.Dot(otherBody.velocity, forward)) : 0f;
                limit = Mathf.Min(limit, Mathf.Max(0f, leaderSpeed + gap / 0.2f));
            }
            return limit;
        }

        private void ResolveEntranceOverlap(float deltaTime)
        {
            // Summed repulsion cancels in the middle of a dense line. Resolve the
            // deepest actual overlap as well, using only the already bounded query.
            // Sweep each correction against walls instead of enabling enemy physics pairs.
            for (int pass = 0; pass < 2; pass++)
            {
                float deepest = 0.04f;
                Vector2 correction = Vector2.zero;
                foreach (var other in neighbors)
                {
                    if (other == null || other.Health.IsDead || !other.isActiveAndEnabled) continue;
                    if (HasClearedEntrance(other)) continue;
                    Vector2 delta = body.position - other.Position;
                    float distance = delta.magnitude;
                    float penetration = (lifecycle.Radius + other.Radius) * config.crowd.spacing - distance;
                    if (penetration <= deepest) continue;
                    deepest = penetration;
                    Vector2 direction = distance > 0.0001f ? delta / distance
                        : PairSeparation(delta, 1f, lifecycle.GetInstanceID(), other.GetInstanceID());
                    correction = direction * Mathf.Min(penetration * 0.5f, 0.1f);
                }
                if (correction.sqrMagnitude < 0.000001f) break;
                body.position += ConstrainVelocity(correction / deltaTime, deltaTime) * deltaTime;
            }
        }

        internal static Vector2 PairSeparation(Vector2 delta, float desiredDistance, int self, int other)
        {
            float distance = delta.magnitude;
            if (desiredDistance <= 0f || distance >= desiredDistance) return Vector2.zero;
            if (distance > 0.0001f) return delta / distance * (1f - distance / desiredDistance);
            // Antisymmetric, stable direction even when pooled agents start at exactly the same position.
            uint low = (uint)Mathf.Min(self, other);
            uint high = (uint)Mathf.Max(self, other);
            float angle = ((low * 73856093u ^ high * 19349663u) % 1024) * (Mathf.PI * 2f / 1024f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (self < other ? 1f : -1f);
        }

        private Vector2 AvoidObstacle(Vector2 velocity, float deltaTime, bool think)
        {
            float speed = velocity.magnitude;
            if (speed < 0.0001f) return velocity;
            Vector2 direction = velocity / speed;
            float lookAhead = Mathf.Max(speed * deltaTime + config.crowd.collisionSkin, config.crowd.obstacleLookAhead);
            if (think)
            {
                obstacleCached = TryBlockingHit(direction, lookAhead, out var hit);
                if (!obstacleCached) { wallSide = 0f; return velocity; }
                probePosition = body.position;
                probeLength = lookAhead;
                obstacleNormal = hit.normal;
                obstacleDistance = hit.distance;
                Vector2 tangent = new Vector2(-hit.normal.y, hit.normal.x);
                float alignment = Vector2.Dot(tangent, direction);
                if (Mathf.Abs(alignment) > 0.15f) wallSide = Mathf.Sign(alignment);
                else if (wallSide == 0f) wallSide = (GetInstanceID() & 1) == 0 ? 1f : -1f;
                Vector2 slide = tangent * wallSide;
                if (TryBlockingHit(slide, lookAhead, out var slideHit)
                    && slideHit.distance < speed * deltaTime + config.crowd.collisionSkin)
                {
                    Vector2 alternate = -slide;
                    if (!TryBlockingHit(alternate, lookAhead, out var alternateHit)
                        || alternateHit.distance > slideHit.distance + 0.1f)
                    { wallSide = -wallSide; slide = alternate; }
                }
                obstacleSlide = slide;
            }
            if (!obstacleCached || Vector2.Dot(direction, obstacleNormal) >= -0.001f) return velocity;
            // Blend into wall sliding as the obstacle approaches, instead of snapping on the probe boundary.
            float distance = obstacleDistance + Vector2.Dot(body.position - probePosition, obstacleNormal);
            float urgency = 1f - Mathf.Clamp01((distance - config.crowd.collisionSkin) / probeLength);
            Vector2 steered = Vector2.Lerp(direction, obstacleSlide, Mathf.Clamp01(urgency * 1.5f));
            return steered.sqrMagnitude > 0.0001f ? steered.normalized * speed : Vector2.zero;
        }

        internal Vector2 ConstrainVelocity(Vector2 velocity, float deltaTime)
        {
            float speed = velocity.magnitude;
            if (speed < 0.0001f || deltaTime <= 0f) return Vector2.zero;
            Vector2 direction = velocity / speed;
            float distance = speed * deltaTime;
            if (!TryBlockingHit(direction, distance + config.crowd.collisionSkin, out var hit)) return velocity;
            float safe = Mathf.Max(0f, hit.distance - config.crowd.collisionSkin);
            Vector2 forward = direction * Mathf.Min(speed, safe / deltaTime);
            // Keep the tangential component when a diagonal step touches a wall.
            // Scaling the entire vector to zero also removed all motion along the wall.
            Vector2 slide = velocity - hit.normal * Mathf.Min(0f, Vector2.Dot(velocity, hit.normal));
            float slideSpeed = slide.magnitude;
            if (slideSpeed < 0.0001f) return forward;
            Vector2 slideDirection = slide / slideSpeed;
            if (TryBlockingHit(slideDirection, slideSpeed * deltaTime + config.crowd.collisionSkin, out var slideHit))
                slide *= Mathf.Clamp01(Mathf.Max(0f, slideHit.distance - config.crowd.collisionSkin) / (slideSpeed * deltaTime));
            return forward.sqrMagnitude > slide.sqrMagnitude ? forward : slide;
        }

        private bool TryBlockingHit(Vector2 direction, float distance, out RaycastHit2D nearest)
        {
            int count = body.Cast(direction, filter, hits, distance);
            nearest = default;
            float nearestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody == body
                    || hit.collider.GetComponentInParent<EnemyHealth>() != null) continue;
                // A contact while moving away or along a surface must not trap the actor against it.
                if (Vector2.Dot(direction, hit.normal) >= -0.001f) continue;
                if (hit.distance < nearestDistance)
                { nearest = hit; nearestDistance = hit.distance; found = true; }
            }
            return found;
        }

        private void OnDisable()
        {
            separation = Vector2.zero;
            desiredSeparation = Vector2.zero;
            obstacleCached = scheduleManaged = decisionDue = false;
            wallSide = 0f;
            neighbors.Clear();
            LastNeighborCount = LastCandidateChecks = 0;
        }
    }
}
