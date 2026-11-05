using ShootDemo.Config;
using UnityEngine;

namespace ShootDemo.Player
{
    /// <summary>Multiplies sprite tint without overwriting animation-authored colors or visibility.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerHealthFeedback : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private Color hurtColor = new Color(1f, 0.3f, 0.3f, 1f);
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int DeathState = Animator.StringToHash("Base Layer.neo2_dead");

        private PlayerHealth health;
        private Animator animator;
        private SpriteRenderer[] sprites;
        private MaterialPropertyBlock[] originalBlocks;
        private MaterialPropertyBlock[] effectBlocks;
        private Color[] originalTints;
        private float flashRemaining;
        private float blinkElapsed;
        private bool effectApplied;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            animator = GetComponent<Animator>();
            if (config == null)
            {
                Debug.LogError("PlayerHealthFeedback needs a GameConfig reference.", this);
                enabled = false;
                return;
            }

            sprites = GetComponentsInChildren<SpriteRenderer>(true);
            originalBlocks = new MaterialPropertyBlock[sprites.Length];
            effectBlocks = new MaterialPropertyBlock[sprites.Length];
            originalTints = new Color[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                originalBlocks[i] = new MaterialPropertyBlock();
                effectBlocks[i] = new MaterialPropertyBlock();
                sprites[i].GetPropertyBlock(originalBlocks[i]);
                sprites[i].GetPropertyBlock(effectBlocks[i]);
                var material = sprites[i].sharedMaterial;
                originalTints[i] = originalBlocks[i].HasColor(ColorId) ? originalBlocks[i].GetColor(ColorId)
                    : material != null && material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;
            }
        }

        private void OnEnable()
        {
            if (health == null) return;
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }
            flashRemaining = 0f;
            blinkElapsed = 0f;
            RestoreTint();
        }

        private void OnDamaged(float amount)
        {
            flashRemaining = Mathf.Max(0f, config.playerHealth.hurtFlashDuration);
            blinkElapsed = 0f;
            ApplyTint();
        }

        private void OnDied()
        {
            flashRemaining = 0f;
            RestoreTint();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                if (animator.HasState(0, DeathState))
                {
                    animator.Play(DeathState, 0, 0f);
                }
                else
                {
                    Debug.LogError("Player death needs the neo2_dead Animator state.", this);
                }
            }
        }

        private void LateUpdate()
        {
            if (health.IsDead)
            {
                RestoreTint();
                return;
            }

            flashRemaining = Mathf.Max(0f, flashRemaining - Time.deltaTime);
            blinkElapsed = health.IsInvulnerable ? blinkElapsed + Time.deltaTime : 0f;
            ApplyTint();
        }

        private void ApplyTint()
        {
            bool hurt = flashRemaining > 0f && !health.IsDead;
            bool invulnerable = health.IsInvulnerable;
            if (!hurt && !invulnerable)
            {
                RestoreTint();
                return;
            }

            var settings = config.playerHealth;
            float alpha = !hurt && invulnerable
                && (int)(blinkElapsed / Mathf.Max(0.01f, settings.blinkInterval)) % 2 == 0
                ? Mathf.Clamp01(settings.blinkAlpha) : 1f;
            Color multiplier = hurt ? hurtColor : Color.white;
            multiplier.a *= alpha;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                effectBlocks[i].SetColor(ColorId, originalTints[i] * multiplier);
                sprites[i].SetPropertyBlock(effectBlocks[i]);
            }
            effectApplied = true;
        }

        private void RestoreTint()
        {
            if (!effectApplied || sprites == null) return;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null) sprites[i].SetPropertyBlock(originalBlocks[i]);
            }
            effectApplied = false;
        }
    }
}
