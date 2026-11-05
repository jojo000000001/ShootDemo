using System.Collections.Generic;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Enemy
{
    [DefaultExecutionOrder(-150)]
    public sealed class EnemyProjectiles : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private EnemyProjectile projectilePrefab;
        [SerializeField] private CombatVisualEffect impactPrefab;
        private readonly HashSet<EnemyProjectile> active = new HashSet<EnemyProjectile>();
        private Pool<EnemyProjectile> projectiles;
        private Pool<CombatVisualEffect> impacts;
        private GameStateMachine state;
        public static EnemyProjectiles Instance { get; private set; }
        public int ActiveCount => active.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            if (config == null || projectilePrefab == null)
            {
                Debug.LogError("EnemyProjectiles needs config and projectile prefab.", this);
                enabled = false;
                return;
            }
            Instance = this;
            projectiles = new Pool<EnemyProjectile>(projectilePrefab, 16, onGet: p => active.Add(p), onRelease: p => active.Remove(p));
            if (impactPrefab != null) impacts = new Pool<CombatVisualEffect>(impactPrefab, 8);
        }

        private void Start()
        {
            state = GameStateMachine.Instance;
            if (state != null) state.StateChanged += OnStateChanged;
        }

        public EnemyProjectile Fire(Transform owner, Vector2 position, Vector2 direction)
        {
            var game = GameStateMachine.Instance;
            if (projectiles == null || (game != null && game.CurrentState != GameState.Playing)) return null;
            var shot = projectiles.Get(position, Quaternion.identity);
            var settings = config.enemy;
            shot.Launch(projectiles, owner, direction, settings.rangedProjectileSpeed,
                settings.rangedDamage, settings.rangedProjectileLifetime, ShowImpact);
            return shot;
        }

        private void ShowImpact(Vector2 point, Vector2 normal)
        {
            impacts?.Get(point, Quaternion.Euler(0f, 0f, Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg)).Play(impacts, 0.3f);
        }

        private void OnStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.GameOver || next == GameState.Menu) ClearShots();
        }

        public void ClearShots()
        {
            var snapshot = new List<EnemyProjectile>(active);
            foreach (var shot in snapshot) if (shot != null) shot.Despawn();
            active.Clear();
        }

        private void OnDestroy()
        {
            if (state != null) state.StateChanged -= OnStateChanged;
            projectiles?.Clear();
            impacts?.Clear();
            active.Clear();
            if (Instance == this) Instance = null;
        }
    }
}
