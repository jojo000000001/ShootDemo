namespace ShootDemo.Combat
{
    public interface IDamageable
    {
        /// <returns>True when damage was accepted, false when it was rejected.</returns>
        bool TryTakeDamage(float amount);
    }
}
