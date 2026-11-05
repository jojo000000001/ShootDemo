using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private Vector2 localForward = Vector2.right;

        private Rigidbody2D body;
        private PlayerDash dash;
        private Vector2 moveInput;
        private bool movementEnabled = true;
        private bool facingEnabled = true;
        private bool isRunning;

        public Vector2 MoveInput => moveInput;
        public Vector2 CurrentVelocity => body == null ? Vector2.zero : body.velocity;
        public Vector2 LastMoveDirection { get; private set; } = Vector2.up;
        public bool MovementEnabled => movementEnabled;
        public bool FacingEnabled => facingEnabled;
        public float TurnSpeed => config == null ? 0f : config.movement.turnSpeed;
        public float WalkSpeed => config == null ? 0f : config.movement.walkSpeed;
        public bool IsRunning => isRunning;
        public Vector2 Forward => transform.TransformDirection(localForward).normalized;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            dash = GetComponent<PlayerDash>();

            if (config == null)
            {
                Debug.LogError("PlayerMovement needs a GameConfig reference.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            moveInput = CanReadMovementInput() ? InputService.ReadMovement() : Vector2.zero;
            isRunning = moveInput.sqrMagnitude > 0f && InputService.ReadRunHeld();

            if (moveInput.sqrMagnitude > 0f)
            {
                LastMoveDirection = moveInput.normalized;
            }
        }

        private void FixedUpdate()
        {
            // Only one system writes translation during a physics step.
            if (dash != null && dash.isActiveAndEnabled && dash.Tick(Time.fixedDeltaTime))
            {
                return;
            }

            body.velocity = CalculateNextVelocity(
                body.velocity,
                moveInput,
                config.movement,
                Time.fixedDeltaTime,
                isRunning);

            if (facingEnabled && moveInput.sqrMagnitude > 0f)
            {
                float targetAngle = CalculateFacingAngle(localForward, moveInput);
                body.SetRotation(CalculateNextFacingAngle(
                    body.rotation,
                    targetAngle,
                    TurnSpeed,
                    Time.fixedDeltaTime));
            }
        }

        public void SetMovementEnabled(bool value, bool stopImmediately = false)
        {
            movementEnabled = value;

            if (!value)
            {
                moveInput = Vector2.zero;
                isRunning = false;
            }

            if (stopImmediately && body != null)
            {
                body.velocity = Vector2.zero;
            }
        }

        /// <summary>
        /// Allows the aiming system to take ownership of rotation without
        /// disabling movement.
        /// </summary>
        public void SetFacingEnabled(bool value)
        {
            facingEnabled = value;
        }

        public static Vector2 CalculateNextVelocity(
            Vector2 currentVelocity,
            Vector2 rawInput,
            MovementSettings settings,
            float deltaTime,
            bool isRunning = true)
        {
            Vector2 input = Vector2.ClampMagnitude(rawInput, 1f);
            float speed = isRunning ? settings.moveSpeed : Mathf.Min(settings.walkSpeed, settings.moveSpeed);
            Vector2 targetVelocity = input * speed;

            float rate;
            if (input.sqrMagnitude <= 0f)
            {
                rate = settings.decel;
            }
            else if (Vector2.Dot(currentVelocity, targetVelocity) < 0f)
            {
                // Braking and acceleration act together while reversing. This
                // prevents the character from floating in the old direction.
                rate = settings.accel + settings.decel;
            }
            else
            {
                rate = currentVelocity.sqrMagnitude > targetVelocity.sqrMagnitude
                    ? settings.decel
                    : settings.accel;
            }

            return Vector2.MoveTowards(
                currentVelocity,
                targetVelocity,
                Mathf.Max(0f, rate) * Mathf.Max(0f, deltaTime));
        }

        public static float CalculateFacingAngle(Vector2 localForward, Vector2 worldDirection)
        {
            if (localForward.sqrMagnitude <= 0f || worldDirection.sqrMagnitude <= 0f)
            {
                return 0f;
            }

            float localAngle = Mathf.Atan2(localForward.y, localForward.x) * Mathf.Rad2Deg;
            float worldAngle = Mathf.Atan2(worldDirection.y, worldDirection.x) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, worldAngle - localAngle);
        }

        public static float CalculateNextFacingAngle(
            float currentAngle,
            float targetAngle,
            float turnSpeed,
            float deltaTime)
        {
            return Mathf.MoveTowardsAngle(
                currentAngle,
                targetAngle,
                Mathf.Max(0f, turnSpeed) * Mathf.Max(0f, deltaTime));
        }

        private bool CanReadMovementInput()
        {
            if (!movementEnabled)
            {
                return false;
            }

            GameStateMachine stateMachine = GameStateMachine.Instance;
            return stateMachine == null || stateMachine.CurrentState == GameState.Playing;
        }
    }
}
