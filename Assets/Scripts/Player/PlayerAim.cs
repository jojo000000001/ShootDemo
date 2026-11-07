using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Player
{
    /// <summary>
    /// Turns the player toward the mouse and keeps a crosshair on that point.
    /// The character art faces local +X. Movement facing is disabled so the two systems do not both write rotation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class PlayerAim : MonoBehaviour
    {
        [SerializeField] private Vector2 localForward = Vector2.right;
        [SerializeField] private int crosshairSortingOrder = 11000;

        private PlayerMovement movement;
        private PlayerDash dash;
        private Rigidbody2D body;
        private Camera mainCamera;
        private ArenaCamera arenaCamera;
        private Transform crosshair;
        private SpriteRenderer crosshairRenderer;
        private Texture2D crosshairTexture;
        private float targetFacingAngle;

        public Vector2 AimDirection { get; private set; } = Vector2.right;
        public Vector2 AimPoint { get; private set; }

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            dash = GetComponent<PlayerDash>();
            body = GetComponent<Rigidbody2D>();
            AimPoint = transform.position;
            targetFacingAngle = body != null ? body.rotation : transform.eulerAngles.z;
            CreateCrosshair();
        }

        private void OnEnable()
        {
            if (movement != null)
            {
                movement.SetFacingEnabled(false);
            }
        }

        private void OnDisable()
        {
            SetCrosshairVisible(false);
        }

        private void OnDestroy()
        {
            if (crosshair != null)
            {
                Destroy(crosshair.gameObject);
            }

            if (crosshairTexture != null)
            {
                Destroy(crosshairTexture);
            }
        }

        private void Update()
        {
            if (!CanAim())
            {
                SetCrosshairVisible(false);
                return;
            }

            ResolveCamera();
            Vector2 aimPoint = ReadAimPoint();
            AimPoint = aimPoint;
            AimDirection = DirectionFrom(transform.position, aimPoint);
            targetFacingAngle = PlayerMovement.CalculateFacingAngle(localForward, AimDirection);

            crosshair.position = new Vector3(aimPoint.x, aimPoint.y, 0f);
            SetCrosshairVisible(true);
        }

        private void FixedUpdate()
        {
            if (!CanAim() || (dash != null && dash.IsDashing))
            {
                return;
            }

            ApplyFacing(Time.fixedDeltaTime);
        }

        public static Vector2 DirectionFrom(Vector2 origin, Vector2 point)
        {
            Vector2 delta = point - origin;
            if (delta.sqrMagnitude <= 0.000001f)
            {
                return Vector2.right;
            }

            return delta.normalized;
        }

        private void ApplyFacing(float deltaTime)
        {
            float turnSpeed = movement.TurnSpeed;
            float currentAngle = body != null ? body.rotation : transform.eulerAngles.z;
            float angle = PlayerMovement.CalculateNextFacingAngle(
                currentAngle,
                targetFacingAngle,
                turnSpeed,
                deltaTime);

            if (body != null)
            {
                body.SetRotation(angle);
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private Vector2 ReadAimPoint()
        {
            Vector3 screen = Input.mousePosition;
            if (arenaCamera != null)
            {
                return arenaCamera.ScreenToWorldOnPlane(screen);
            }

            if (mainCamera == null)
            {
                return transform.position;
            }

            Vector3 world = mainCamera.ScreenToWorldPoint(screen);
            return new Vector2(world.x, world.y);
        }

        private void ResolveCamera()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            if (arenaCamera == null && mainCamera != null)
            {
                arenaCamera = mainCamera.GetComponent<ArenaCamera>();
            }
        }

        private bool CanAim()
        {
            if (!Application.isFocused) return false;
            GameStateMachine stateMachine = GameStateMachine.Instance;
            return stateMachine == null || stateMachine.CurrentState == GameState.Playing;
        }

        private void CreateCrosshair()
        {
            crosshairTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            crosshairTexture.filterMode = FilterMode.Point;
            crosshairTexture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[32 * 32];
            Color mark = Color.white;
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    bool horizontal = y >= 14 && y <= 17 && (x < 12 || x > 19);
                    bool vertical = x >= 14 && x <= 17 && (y < 12 || y > 19);
                    pixels[y * 32 + x] = horizontal || vertical ? mark : Color.clear;
                }
            }

            crosshairTexture.SetPixels(pixels);
            crosshairTexture.Apply(false, false);

            Sprite sprite = Sprite.Create(crosshairTexture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
            var crosshairObject = new GameObject("Crosshair");
            crosshairObject.hideFlags = HideFlags.DontSave;
            crosshair = crosshairObject.transform;
            crosshairRenderer = crosshairObject.AddComponent<SpriteRenderer>();
            crosshairRenderer.sprite = sprite;
            crosshairRenderer.sortingOrder = crosshairSortingOrder;
            SetCrosshairVisible(false);
        }

        private void SetCrosshairVisible(bool visible)
        {
            if (crosshairRenderer != null)
            {
                crosshairRenderer.enabled = visible;
            }
        }
    }
}
