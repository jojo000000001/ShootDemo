using UnityEngine;

namespace ShootDemo.Combat
{
    public readonly struct DamageInfo
    {
        public float Amount { get; }
        public Vector2 Point { get; }
        public Vector2 Direction { get; }
        public Transform Instigator { get; }

        public DamageInfo(float amount, Vector2 point, Vector2 direction, Transform instigator = null)
        {
            Amount = amount;
            Point = point;
            Direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.zero;
            Instigator = instigator;
        }
    }

    public interface IHitDamageable : IDamageable
    {
        bool TryTakeDamage(DamageInfo hit);
    }
}
