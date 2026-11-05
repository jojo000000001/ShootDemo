using UnityEngine;

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

        public static bool ReadRunHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        public static Vector2 ReadMovement()
        {
            Vector2 input = new Vector2(
                Input.GetAxisRaw(HorizontalAxis),
                Input.GetAxisRaw(VerticalAxis));

            return Vector2.ClampMagnitude(input, 1f);
        }
    }
}
