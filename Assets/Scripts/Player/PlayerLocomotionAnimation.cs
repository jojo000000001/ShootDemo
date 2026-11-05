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
        private static readonly int AvoidState = Animator.StringToHash("Base Layer.neo2_avoid");
        private static readonly int DashPlaybackSpeed = Animator.StringToHash("DashPlaybackSpeed");

        private PlayerMovement movement;
        private Animator animator;
        private int currentState;
        private float attackLock;
        private PlayerDash dash;
        private float avoidClipLength;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            animator = GetComponent<Animator>();
            dash = GetComponent<PlayerDash>();
            animator.applyRootMotion = false;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == "neo2_avoid")
                {
                    avoidClipLength = clip.length;
                    break;
                }
            }

            if (!animator.HasState(0, IdleState) || !animator.HasState(0, WalkState)
                || !animator.HasState(0, RunState)
                || (dash != null && (!animator.HasState(0, AvoidState) || avoidClipLength <= 0f)))
            {
                Debug.LogError("Player locomotion needs idle, walk, run and (with PlayerDash) avoid states.", this);
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
            if (dash != null && dash.IsDashing)
            {
                attackLock = 0f;
                if (currentState != AvoidState)
                {
                    animator.SetFloat(DashPlaybackSpeed, avoidClipLength / dash.Duration);
                    animator.CrossFadeInFixedTime(AvoidState, 0.03f, 0);
                    currentState = AvoidState;
                }

                return;
            }

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
