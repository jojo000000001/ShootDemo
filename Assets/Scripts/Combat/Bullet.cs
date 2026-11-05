using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Combat
{
    /// <summary>
    /// Kinematic projectile. Movement is a circle cast, so the bullet has no rigidbody.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Bullet : MonoBehaviour, IPoolable
    {
        [SerializeField] private float radius = 0.06f;
        [SerializeField] private LayerMask hitMask = ~0;

        private readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[8];
        private Pool<Bullet> pool;
        private Transform owner;
        private Vector2 direction = Vector2.right;
        private float speed;
        private float lifetime;
        private bool launched;

        public float Damage { get; private set; }
        public Vector2 Direction => direction;

        public void Launch(
            Pool<Bullet> sourcePool,
            Transform ownerTransform,
            Vector2 travelDirection,
            float travelSpeed,
            float damage,
            float travelLifetime)
        {
            pool = sourcePool;
            owner = ownerTransform;
            direction = travelDirection.sqrMagnitude <= 0.000001f ? Vector2.right : travelDirection.normalized;
            speed = Mathf.Max(0f, travelSpeed);
            Damage = Mathf.Max(0f, damage);
            lifetime = Mathf.Max(0f, travelLifetime);
            launched = true;
            transform.rotation = Quaternion.Euler(
                0f,
                0f,
                Player.PlayerMovement.CalculateFacingAngle(Vector2.right, direction));
        }

        public void OnSpawned()
        {
            launched = false;
            lifetime = 0f;
            speed = 0f;
            Damage = 0f;
            direction = Vector2.right;
            owner = null;
        }

        public void OnDespawned()
        {
            launched = false;
            pool = null;
            owner = null;
        }

        private void Update()
        {
            if (!launched)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            lifetime -= deltaTime;
            if (lifetime <= 0f)
            {
                Release();
                return;
            }

            Vector2 position = transform.position;
            Vector2 delta = direction * speed * Mathf.Max(0f, deltaTime);
            float distance = delta.magnitude;
            if (distance > 0f && TryGetBlockingHit(position, direction, distance, out RaycastHit2D hit))
            {
                transform.position = hit.centroid.sqrMagnitude > 0f
                    ? (Vector3)hit.centroid
                    : (Vector3)(position + direction * hit.distance);
                Release();
                return;
            }

            transform.position = position + delta;
        }

        public static Vector2 NextPosition(Vector2 position, Vector2 travelDirection, float travelSpeed, float deltaTime)
        {
            Vector2 direction = travelDirection.sqrMagnitude <= 0.000001f
                ? Vector2.right
                : travelDirection.normalized;
            return position + direction * Mathf.Max(0f, travelSpeed) * Mathf.Max(0f, deltaTime);
        }

        private bool TryGetBlockingHit(Vector2 origin, Vector2 castDirection, float distance, out RaycastHit2D blockingHit)
        {
            int count = Physics2D.CircleCastNonAlloc(origin, radius, castDirection, hitBuffer, distance, hitMask);
            float nearest = float.MaxValue;
            blockingHit = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = hitBuffer[i].collider;
                if (collider == null || IsOwnerCollider(collider))
                {
                    continue;
                }

                if (hitBuffer[i].distance < nearest)
                {
                    nearest = hitBuffer[i].distance;
                    blockingHit = hitBuffer[i];
                    found = true;
                }
            }

            return found;
        }

        private bool IsOwnerCollider(Collider2D collider)
        {
            if (owner == null)
            {
                return false;
            }

            Transform hitTransform = collider.transform;
            return hitTransform == owner || hitTransform.IsChildOf(owner);
        }

        private void Release()
        {
            launched = false;
            if (pool != null)
            {
                pool.Release(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
