using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Enemy
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth), typeof(Rigidbody2D))]
    public sealed class EnemyHitFeedback : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        private static readonly int FlashId = Shader.PropertyToID("_FlashAmount");
        private EnemyHealth health;
        private Rigidbody2D body;
        private SpriteRenderer[] sprites;
        private MaterialPropertyBlock[] savedBlocks;
        private MaterialPropertyBlock[] flashBlocks;
        private float flashRemaining;
        private float knockbackRemaining;
        private Vector2 knockback;
        private bool flashing;
        public bool IsKnockedBack => knockbackRemaining > 0f;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            body = GetComponent<Rigidbody2D>();
            if (config == null)
            {
                Debug.LogError("EnemyHitFeedback needs a GameConfig reference.", this);
                enabled = false;
                return;
            }
            sprites = GetComponentsInChildren<SpriteRenderer>(true);
            savedBlocks = new MaterialPropertyBlock[sprites.Length];
            flashBlocks = new MaterialPropertyBlock[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                savedBlocks[i] = new MaterialPropertyBlock();
                flashBlocks[i] = new MaterialPropertyBlock();
                sprites[i].GetPropertyBlock(savedBlocks[i]);
                sprites[i].GetPropertyBlock(flashBlocks[i]);
            }
        }

        private void OnEnable()
        {
            if (health != null) health.Damaged += OnDamaged;
        }

        private void OnDamaged(DamageInfo hit)
        {
            var settings = config.combatFeedback;
            flashRemaining = settings.whiteFlashDuration;
            if (flashRemaining > 0f)
            {
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] == null || sprites[i].sharedMaterial == null
                        || !sprites[i].sharedMaterial.HasProperty(FlashId)) continue;
                    flashBlocks[i].SetFloat(FlashId, 1f);
                    sprites[i].SetPropertyBlock(flashBlocks[i]);
                }
                flashing = true;
            }
            if (!health.IsDead)
            {
                knockback = hit.Direction * settings.knockbackSpeed;
                knockbackRemaining = settings.knockbackDuration;
                body.velocity = knockback;
            }
            CombatEffects.Instance?.ShowDamage(hit.Amount, hit.Point + Vector2.up * 0.4f);
            HitStopController.Instance?.Request(settings.hitStopDuration);
        }

        private void LateUpdate()
        {
            flashRemaining = Mathf.Max(0f, flashRemaining - Time.deltaTime);
            if (flashRemaining <= 0f) RestoreFlash();
        }

        private void FixedUpdate()
        {
            if (!IsKnockedBack || health.IsDead) return;
            body.velocity = knockback;
            float deceleration = config.combatFeedback.knockbackSpeed
                / Mathf.Max(0.01f, config.combatFeedback.knockbackDuration);
            knockback = Vector2.MoveTowards(knockback, Vector2.zero, deceleration * Time.fixedDeltaTime);
            knockbackRemaining = Mathf.Max(0f, knockbackRemaining - Time.fixedDeltaTime);
            if (knockbackRemaining <= 0f) body.velocity = Vector2.zero;
        }

        private void RestoreFlash()
        {
            if (!flashing) return;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null) sprites[i].SetPropertyBlock(savedBlocks[i]);
            }
            flashing = false;
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= OnDamaged;
            RestoreFlash();
            if (IsKnockedBack && body != null) body.velocity = Vector2.zero;
            knockbackRemaining = 0f;
        }
    }
}
