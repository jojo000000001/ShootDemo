using ShootDemo.Config;
using UnityEngine;

namespace ShootDemo.Player
{
    /// <summary>
    /// Smoothly follows a target, keeps the orthographic view inside the arena, and applies a decaying shake.
    /// Aiming should use <see cref="ScreenToWorldOnPlane"/> so shake does not move the shot.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ArenaCamera : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private Transform target;
        [SerializeField] private Transform boundsRoot;

        private Camera _camera;
        private Bounds _bounds;
        private bool _hasBounds;
        private Vector3 _focus;
        private float _shakeTimeLeft;
        private float _shakeDuration;
        private float _shakeAmplitude;
        private float _shakeSeed;

        public Vector3 FocusPosition => _focus;

        public bool IsShaking => _shakeTimeLeft > 0f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _focus = transform.position;

            if (target == null)
            {
                GameObject player = GameObject.Find("neo");
                if (player != null)
                {
                    target = player.transform;
                }
            }

            if (boundsRoot == null)
            {
                GameObject walls = GameObject.Find("OuterWalls");
                if (walls != null)
                {
                    boundsRoot = walls.transform;
                }
            }
        }

        private void Start()
        {
            _hasBounds = TryMeasureBounds(boundsRoot, out _bounds);
            _focus = ClampFocus(TargetPlanarPosition());
            ApplyTransform(Vector2.zero);
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            float follow = config != null ? config.camera.followLerp : 10f;
            float blend = follow <= 0f ? 0f : 1f - Mathf.Exp(-follow * deltaTime);
            Vector3 desired = TargetPlanarPosition();
            Vector2 smoothed = Vector2.Lerp(new Vector2(_focus.x, _focus.y), new Vector2(desired.x, desired.y), blend);
            _focus = ClampFocus(new Vector3(smoothed.x, smoothed.y, _focus.z));

            if (_shakeTimeLeft > 0f)
            {
                _shakeTimeLeft = Mathf.Max(0f, _shakeTimeLeft - deltaTime);
            }

            ApplyTransform(EvaluateShake(_shakeTimeLeft, _shakeDuration, _shakeAmplitude, _shakeSeed));
        }

        public void Shake()
        {
            float amplitude = config != null ? config.camera.shakeAmplitude : 0.2f;
            float duration = config != null ? config.camera.shakeDuration : 0.15f;
            Shake(amplitude, duration);
        }

        public void Shake(float amplitude, float duration)
        {
            _shakeAmplitude = Mathf.Max(0f, amplitude);
            _shakeDuration = Mathf.Max(0f, duration);
            _shakeTimeLeft = _shakeDuration;
            _shakeSeed = Random.Range(0f, Mathf.PI * 2f);
        }

        /// <summary>
        /// Screen point on the gameplay plane using the clamped follow position, without shake.
        /// </summary>
        public Vector3 ScreenToWorldOnPlane(Vector3 screenPosition)
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            Vector3 viewport = _camera.ScreenToViewportPoint(screenPosition);
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            return new Vector3(
                _focus.x + (viewport.x - 0.5f) * halfWidth * 2f,
                _focus.y + (viewport.y - 0.5f) * halfHeight * 2f,
                0f);
        }

        public static Vector2 ClampToBounds(Vector2 desired, Bounds bounds, float halfWidth, float halfHeight)
        {
            float minX = bounds.min.x + halfWidth;
            float maxX = bounds.max.x - halfWidth;
            float minY = bounds.min.y + halfHeight;
            float maxY = bounds.max.y - halfHeight;

            if (minX > maxX)
            {
                desired.x = bounds.center.x;
            }
            else
            {
                desired.x = Mathf.Clamp(desired.x, minX, maxX);
            }

            if (minY > maxY)
            {
                desired.y = bounds.center.y;
            }
            else
            {
                desired.y = Mathf.Clamp(desired.y, minY, maxY);
            }

            return desired;
        }

        public static Vector2 EvaluateShake(float timeLeft, float duration, float amplitude, float seed)
        {
            if (timeLeft <= 0f || duration <= 0f || amplitude <= 0f)
            {
                return Vector2.zero;
            }

            float damper = Mathf.Clamp01(timeLeft / duration);
            float phase = (1f - damper) * Mathf.PI * 6f;
            Vector2 direction = new Vector2(Mathf.Sin(phase + seed), Mathf.Cos(phase * 0.85f + seed));
            float magnitude = direction.magnitude;
            if (magnitude > 1f)
            {
                direction /= magnitude;
            }

            return direction * (amplitude * damper * damper);
        }

        private Vector3 TargetPlanarPosition()
        {
            if (target == null)
            {
                return _focus;
            }

            return new Vector3(target.position.x, target.position.y, _focus.z);
        }

        private Vector3 ClampFocus(Vector3 desired)
        {
            if (!_hasBounds || _camera == null || !_camera.orthographic)
            {
                return desired;
            }

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0f, _camera.aspect);
            Vector2 clamped = ClampToBounds(new Vector2(desired.x, desired.y), _bounds, halfWidth, halfHeight);
            return new Vector3(clamped.x, clamped.y, desired.z);
        }

        private void ApplyTransform(Vector2 shake)
        {
            transform.position = new Vector3(_focus.x + shake.x, _focus.y + shake.y, _focus.z);
        }

        private static bool TryMeasureBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            if (root == null)
            {
                return false;
            }

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>();
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return found;
        }
    }
}
