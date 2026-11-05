using ShootDemo.Player;

namespace ShootDemo.Enemy
{
    public interface IEnemyBrain
    {
        PlayerHealth Target { get; }
        bool IsAttacking { get; }
        void SetDecisionSchedule(bool managed, bool due);
    }
}
