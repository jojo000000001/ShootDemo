namespace ShootDemo.Core
{
    public interface IPoolable
    {
        void OnSpawned();

        void OnDespawned();
    }
}
