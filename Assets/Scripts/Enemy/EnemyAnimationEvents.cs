using UnityEngine;

namespace ShootDemo.Enemy
{
    /// <summary>Receives the imported animation's callbacks. Combat timing is owned by the fixed-step AI.</summary>
    public sealed class EnemyAnimationEvents : MonoBehaviour
    {
        public void Event_Trigger() { }
        public void Event_End() { }
    }
}
