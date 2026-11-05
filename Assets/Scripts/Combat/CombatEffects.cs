using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HitStopController))]
    public sealed class CombatEffects : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private DamageNumber damageNumberPrefab;
        [SerializeField] private CombatVisualEffect impactPrefab;
        [SerializeField] private CombatVisualEffect muzzlePrefab;
        private Pool<DamageNumber> numbers;
        private Pool<CombatVisualEffect> impacts;
        private Pool<CombatVisualEffect> muzzles;
        private int numberSequence;
        public static CombatEffects Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            if (config == null || damageNumberPrefab == null || impactPrefab == null || muzzlePrefab == null)
            {
                Debug.LogError("CombatEffects needs config, damage number, impact and muzzle prefabs.", this);
                enabled = false;
                return;
            }
            Instance = this;
            numbers = new Pool<DamageNumber>(damageNumberPrefab, 24);
            impacts = new Pool<CombatVisualEffect>(impactPrefab, 16);
            muzzles = new Pool<CombatVisualEffect>(muzzlePrefab, 8);
        }

        public void ShowDamage(float amount, Vector2 position)
        {
            if (numbers == null) return;
            // Small deterministic offsets keep successive hits readable.
            float offset = (numberSequence++ % 3 - 1) * 0.18f;
            var number = numbers.Get(position + Vector2.right * offset, Quaternion.identity);
            number.Show(numbers, amount, config.combatFeedback.damageNumberLifetime,
                config.combatFeedback.damageNumberRise);
        }

        public void ShowImpact(Vector2 point, Vector2 normal)
        {
            if (impacts == null) return;
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            impacts.Get(point, Quaternion.Euler(0f, 0f, angle)).Play(impacts, 0.35f);
        }

        public void ShowMuzzle(Vector2 point, Vector2 direction)
        {
            if (muzzles == null) return;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            muzzles.Get(point, Quaternion.Euler(0f, 0f, angle)).Play(muzzles, config.combatFeedback.muzzleLifetime);
        }

        private void OnDestroy()
        {
            numbers?.Clear();
            impacts?.Clear();
            muzzles?.Clear();
            if (Instance == this) Instance = null;
        }
    }
}
