using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Enemy
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    [DefaultExecutionOrder(-10)]
    public sealed class EnemyLifecycle : MonoBehaviour, IPoolable
    {
        [SerializeField] private GameConfig config;
        private Pool<EnemyLifecycle> pool;
        private EnemyRegistry registry;
        private MeleeEnemy melee;
        private RangedEnemy ranged;
        private bool useMelee;
        private bool useRanged;
        private bool dying;
        private float remaining;
        public EnemyHealth Health { get; private set; }
        public IEnemyBrain Brain { get; private set; }
        public EnemyCrowdMovement Crowd { get; private set; }
        private Rigidbody2D body;
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        public float Radius { get; private set; } = 0.65f;

        private void Awake()
        {
            Health = GetComponent<EnemyHealth>();
            body = GetComponent<Rigidbody2D>();
            var capsule = GetComponent<CapsuleCollider2D>();
            if (capsule != null) Radius = Mathf.Max(capsule.size.x * Mathf.Abs(transform.lossyScale.x),
                capsule.size.y * Mathf.Abs(transform.lossyScale.y)) * 0.5f;
            melee = GetComponent<MeleeEnemy>();
            ranged = GetComponent<RangedEnemy>();
            Brain = melee != null ? (IEnemyBrain)melee : ranged;
            Crowd = GetComponent<EnemyCrowdMovement>();
            useMelee = melee != null && melee.enabled;
            useRanged = ranged != null && ranged.enabled;
        }

        private void OnEnable()
        {
            // Interface references are not serialized across an editor domain reload.
            melee = GetComponent<MeleeEnemy>();
            ranged = GetComponent<RangedEnemy>();
            Brain = melee != null ? (IEnemyBrain)melee : ranged;
            Crowd = GetComponent<EnemyCrowdMovement>();
            dying = false;
            remaining = 0f;
            if (melee != null) melee.enabled = useMelee;
            if (ranged != null) ranged.enabled = useRanged;
            Health.Died += OnDied;
            registry = EnemyRegistry.Instance;
            registry?.Register(this);
        }

        public void BindPool(Pool<EnemyLifecycle> source) { pool = source; }
        public void OnSpawned() { }
        public void OnDespawned() { registry?.Unregister(this); }

        private void OnDied()
        {
            if (dying) return;
            dying = true;
            registry?.Unregister(this);
            if (melee != null) melee.enabled = false;
            if (ranged != null) ranged.enabled = false;
            remaining = Health.DeathAnimationDuration + (config != null ? config.enemy.deathHoldDuration : 0.5f);
        }

        private void Update() { Tick(Time.deltaTime); }

        internal void Tick(float deltaTime)
        {
            var state = GameStateMachine.Instance;
            if (!dying || deltaTime <= 0f || (state != null && state.CurrentState == GameState.Paused)) return;
            remaining -= deltaTime;
            if (remaining <= 0f) Despawn();
        }

        public void Despawn()
        {
            if (!gameObject.activeSelf) return;
            registry?.Unregister(this);
            // Scene-placed enemies have no source pool and remain disabled after death.
            if (pool == null || !pool.Release(this)) gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (Health != null) Health.Died -= OnDied;
            registry?.Unregister(this);
            dying = false;
        }
    }
}
