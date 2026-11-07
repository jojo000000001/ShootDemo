using UnityEngine;
using ShootDemo.Core;
using UnityEngine.EventSystems;

namespace ShootDemo.Player
{
    /// <summary>
    /// Keeps legacy Input Manager access in one place so gameplay code does not
    /// depend on concrete axis names.
    /// </summary>
    public static class InputService
    {
        private const string HorizontalAxis = "Horizontal";
        private const string VerticalAxis = "Vertical";

        public static bool GameplayEnabled => Application.isFocused
            && (GameStateMachine.Instance == null || GameStateMachine.Instance.CurrentState == GameState.Playing);

        public static bool ReadFireHeld()
        {
            return GameplayEnabled && Input.GetButton("Fire1")
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
        }

        public static bool ReadDashPressed()
        {
            return GameplayEnabled && Input.GetKeyDown(KeyCode.Space);
        }

        public static bool ReadRunHeld()
        {
            return GameplayEnabled && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        public static Vector2 ReadMovement()
        {
            if (!GameplayEnabled) return Vector2.zero;
            Vector2 input = new Vector2(
                Input.GetAxisRaw(HorizontalAxis),
                Input.GetAxisRaw(VerticalAxis));

            return Vector2.ClampMagnitude(input, 1f);
        }
    }
}
