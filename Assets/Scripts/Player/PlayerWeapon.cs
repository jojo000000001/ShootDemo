using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Player
{
    /// <summary>
    /// Fires pooled bullets along the current aim direction.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class PlayerWeapon : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private PlayerAim aim;
        [SerializeField] private Bullet bulletPrefab;
        [SerializeField] private int prewarmCount = 24;
        [SerializeField] private string muzzleName = "weapon_huoqiang_huoguang";
        [SerializeField] private float attackFireTime = 1.0833334f;
        [SerializeField] private float attackPoseHold = 0.12f;

        private static readonly int AttackState = Animator.StringToHash("Base Layer.neo2_attack");

        private Pool<Bullet> pool;
        private float cooldown;
        private Transform muzzle;
        private Animator animator;
        private PlayerLocomotionAnimation locomotion;
        private bool pendingShot;
        private Vector2 pendingDirection;

        private void Awake()
        {
            if (aim == null)
            {
                aim = GetComponent<PlayerAim>();
            }

            if (config == null)
            {
                Debug.LogError("PlayerWeapon needs a GameConfig reference.", this);
                enabled = false;
                return;
            }

            if (bulletPrefab == null)
            {
                Debug.LogError("PlayerWeapon needs a bullet prefab.", this);
                enabled = false;
                return;
            }

            muzzle = FindActiveNamed(transform, muzzleName);
            animator = GetComponent<Animator>();
            locomotion = GetComponent<PlayerLocomotionAnimation>();
            pool = new Pool<Bullet>(bulletPrefab, prewarmCount);
        }

        private void OnDestroy()
        {
            pool?.Clear();
        }

        private void Update()
        {
            if (!CanFire() || pool == null || aim == null)
            {
                return;
            }

            float interval = ShotInterval(config.shooting.fireRate);
            if (!TryFire(Input.GetButton("Fire1"), ref cooldown, interval, Time.deltaTime))
            {
                return;
            }

            PlayAttackPose();
            pendingDirection = aim.AimDirection;
            pendingShot = true;
        }

        private void LateUpdate()
        {
            if (!pendingShot)
            {
                return;
            }

            pendingShot = false;
            Vector2 direction = BarrelDirection();
            Vector3 origin = muzzle != null ? muzzle.position : (Vector3)MuzzlePosition(transform.position, direction, config.shooting.muzzleOffset);
            Bullet bullet = pool.Get(new Vector3(origin.x, origin.y, 0f), Quaternion.identity);
            bullet.Launch(
                pool,
                transform,
                direction,
                config.shooting.bulletSpeed,
                config.shooting.bulletDamage,
                config.shooting.bulletLifetime);
        }

        private Vector2 BarrelDirection()
        {
            if (muzzle != null && muzzle.parent != null)
            {
                Vector3 barrel = muzzle.parent.TransformDirection(Vector3.right);
                if (barrel.sqrMagnitude > 0.000001f)
                {
                    return new Vector2(barrel.x, barrel.y).normalized;
                }
            }

            return pendingDirection.sqrMagnitude <= 0.000001f ? aim.AimDirection : pendingDirection;
        }

        private void PlayAttackPose()
        {
            if (animator == null || !animator.HasState(0, AttackState))
            {
                return;
            }

            float length = AttackClipLength();
            float normalized = length <= 0.0001f ? 0f : Mathf.Clamp01(attackFireTime / length);
            animator.Play(AttackState, 0, normalized);
            animator.Update(0f);
            if (locomotion != null)
            {
                float interval = config != null ? ShotInterval(config.shooting.fireRate) : attackPoseHold;
                locomotion.SuppressLocomotion(Mathf.Max(attackPoseHold, interval));
            }
        }

        private float AttackClipLength()
        {
            var controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                return 0f;
            }

            AnimationClip[] clips = controller.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == "neo2_attack")
                {
                    return clips[i].length;
                }
            }

            return 0f;
        }

        public static float ShotInterval(float fireRate)
        {
            return 1f / Mathf.Max(0.01f, fireRate);
        }

        public static bool TryFire(bool triggerHeld, ref float cooldown, float interval, float deltaTime)
        {
            cooldown = Mathf.Max(0f, cooldown - Mathf.Max(0f, deltaTime));
            if (!triggerHeld || cooldown > 0f)
            {
                return false;
            }

            cooldown = Mathf.Max(0f, interval);
            return true;
        }

        public static Vector2 MuzzlePosition(Vector2 origin, Vector2 direction, float offset)
        {
            Vector2 aim = direction.sqrMagnitude <= 0.000001f ? Vector2.right : direction.normalized;
            return origin + aim * Mathf.Max(0f, offset);
        }

        private bool CanFire()
        {
            GameStateMachine stateMachine = GameStateMachine.Instance;
            return stateMachine == null || stateMachine.CurrentState == GameState.Playing;
        }

        private static Transform FindActiveNamed(Transform root, string childName)
        {
            Transform fallback = null;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name != childName)
                {
                    continue;
                }

                if (children[i].gameObject.activeInHierarchy)
                {
                    return children[i];
                }

                if (fallback == null)
                {
                    fallback = children[i];
                }
            }

            return fallback;
        }
    }
}
