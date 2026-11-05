using UnityEngine;

namespace ShootDemo.Player
{
    /// <summary>Drives the existing neo locomotion states from physics velocity.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement), typeof(Animator))]
    public sealed class PlayerLocomotionAnimation : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float transitionDuration = 0.12f;
        [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.1f;

        private static readonly int IdleState = Animator.StringToHash("Base Layer.neo2_idle");
        private static readonly int WalkState = Animator.StringToHash("Base Layer.neo2_walk");
        private static readonly int RunState = Animator.StringToHash("Base Layer.neo2_run");

        private PlayerMovement movement;
        private Animator animator;
        private int currentState;
        private float attackLock;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;

            if (!animator.HasState(0, IdleState) || !animator.HasState(0, WalkState)
                || !animator.HasState(0, RunState))
            {
                Debug.LogError("Player locomotion needs neo2_idle, neo2_walk and neo2_run states.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            currentState = 0;
        }

        /// <summary>
        /// Keeps a fired attack pose from being replaced by idle, walk, or run.
        /// </summary>
        public void SuppressLocomotion(float duration)
        {
            attackLock = Mathf.Max(attackLock, Mathf.Max(0f, duration));
        }

        private void LateUpdate()
        {
            if (attackLock > 0f)
            {
                attackLock -= Time.deltaTime;
                if (attackLock <= 0f)
                {
                    currentState = 0;
                }

                return;
            }

            float speed = movement.CurrentVelocity.magnitude;
            int nextState = speed <= idleSpeedThreshold ? IdleState
                : speed > movement.WalkSpeed + idleSpeedThreshold ? RunState : WalkState;

            if (nextState == currentState)
            {
                return;
            }

            if (currentState == 0)
            {
                animator.Play(nextState, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(nextState, transitionDuration, 0);
            }

            currentState = nextState;
        }
    }
}
