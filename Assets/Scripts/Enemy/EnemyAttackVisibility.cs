using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Enemy
{
    /// <summary>Arms ranged attacks only after the body has been on the gameplay camera for a short time.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // Sample after ArenaCamera.LateUpdate, independently of AI decision LOD.
    public sealed class EnemyAttackVisibility : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private Camera viewCamera;
        [SerializeField, Min(0f)] private float visibleBeforeAttack = 0.2f;
        private RangedEnemy enemy;
        private bool hasVisual;
        private int lastVisibleFrame = -1;
        private int visibleFrames;
        private float visibleSeconds;

        public bool CanAttack => isActiveAndEnabled && (!hasVisual
            || (visibleFrames >= 2 && visibleSeconds >= visibleBeforeAttack && BodyIsInView()));

        private void Awake()
        {
            enemy = GetComponent<RangedEnemy>();
            if (bodyRenderer == null)
            {
                foreach (var sprite in GetComponentsInChildren<SpriteRenderer>(true))
                    if (sprite.name == "outuzhe_body") { bodyRenderer = sprite; break; }
            }
            hasVisual = bodyRenderer != null;
        }

        private void OnEnable() { ResetVisibility(); }

        private void LateUpdate() { TickVisibility(Time.deltaTime, Time.frameCount); }

        internal void TickVisibility(float deltaTime, int frame)
        {
            // Headless agents used for simulation have no presentation to wait for.
            if (!hasVisual) return;
            var state = GameStateMachine.Instance;
            if (state != null && state.CurrentState != GameState.Playing)
            {
                if (visibleFrames > 0) lastVisibleFrame = frame;
                return;
            }
            if (viewCamera == null) viewCamera = Camera.main;
            if (!BodyIsInView())
            {
                ResetVisibility();
                if (enemy != null) enemy.CancelUnreleasedAttack();
                return;
            }
            if (frame == lastVisibleFrame) return;
            if (lastVisibleFrame >= 0 && frame == lastVisibleFrame + 1)
            {
                visibleSeconds += Mathf.Max(0f, deltaTime);
                visibleFrames++;
            }
            else
            {
                // Do not credit time elapsed before this body first entered the picture.
                visibleSeconds = 0f;
                visibleFrames = 1;
            }
            lastVisibleFrame = frame;
        }

        private bool BodyIsInView()
        {
            if (bodyRenderer == null || !bodyRenderer.enabled || !bodyRenderer.gameObject.activeInHierarchy
                || bodyRenderer.forceRenderingOff || bodyRenderer.sprite == null || bodyRenderer.color.a <= 0.001f
                || viewCamera == null || !viewCamera.isActiveAndEnabled
                || (viewCamera.cullingMask & (1 << bodyRenderer.gameObject.layer)) == 0) return false;
            // Check the body, never the long warning LineRenderer or an Editor Scene View's isVisible flag.
            Vector3 point = viewCamera.WorldToViewportPoint(bodyRenderer.bounds.center);
            const float inset = 0.02f;
            return point.z >= viewCamera.nearClipPlane && point.z <= viewCamera.farClipPlane
                && point.x >= inset && point.x <= 1f - inset && point.y >= inset && point.y <= 1f - inset;
        }

        private void ResetVisibility()
        {
            visibleSeconds = 0f;
            visibleFrames = 0;
            lastVisibleFrame = -1;
        }

        private void OnDisable()
        {
            ResetVisibility();
            if (enemy != null) enemy.CancelUnreleasedAttack();
        }
    }
}
