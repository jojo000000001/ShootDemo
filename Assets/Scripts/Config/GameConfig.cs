using System;
using UnityEngine;

namespace ShootDemo.Config
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Shoot Demo/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Player Movement")]
        public MovementSettings movement = new MovementSettings();

        [Header("Dash")]
        public DashSettings dash = new DashSettings();

        [Header("Player Health")]
        public PlayerHealthSettings playerHealth = new PlayerHealthSettings();

        [Header("Shooting")]
        public ShootingSettings shooting = new ShootingSettings();

        [Header("Combat Feedback")]
        public CombatFeedbackSettings combatFeedback = new CombatFeedbackSettings();

        [Header("Enemies")]
        public EnemySettings enemy = new EnemySettings();

        [Header("Spawning")]
        public SpawnSettings spawn = new SpawnSettings();

        [Header("Camera")]
        public CameraSettings camera = new CameraSettings();

        [Header("Stress Test")]
        public StressSettings stress = new StressSettings();

        [Header("Performance")]
        public PerformanceSettings performance = new PerformanceSettings();

        private void OnValidate()
        {
            movement.Validate();
            dash.Validate();
            playerHealth.Validate();
            shooting.Validate();
            combatFeedback.Validate();
            enemy.Validate();
            spawn.Validate();
            camera.Validate();
            stress.Validate();
            performance.Validate();
        }
    }

    [Serializable]
    public sealed class MovementSettings
    {
        [Min(0f)] public float walkSpeed = 3f;
        [Tooltip("Maximum speed while holding Shift.")]
        [Min(0f)] public float moveSpeed = 6f;
        [Min(0f)] public float accel = 36f;
        [Min(0f)] public float decel = 48f;
        [Min(0f)] public float turnSpeed = 540f;

        internal void Validate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            walkSpeed = Mathf.Clamp(walkSpeed, 0f, moveSpeed);
            accel = Mathf.Max(0f, accel);
            decel = Mathf.Max(0f, decel);
            turnSpeed = Mathf.Max(0f, turnSpeed);
        }
    }

    [Serializable]
    public sealed class DashSettings
    {
        [Min(0f)] public float dashDistance = 5f;
        [Min(0.01f)] public float dashDuration = 0.3f;
        [Min(0f)] public float dashCooldown = 0.8f;
        [Min(0f)] public float iFrameDuration = 0.2f;

        internal void Validate()
        {
            dashDistance = Mathf.Max(0f, dashDistance);
            dashDuration = Mathf.Max(0.01f, dashDuration);
            dashCooldown = Mathf.Max(0f, dashCooldown);
            iFrameDuration = Mathf.Clamp(iFrameDuration, 0f, dashDuration);
        }
    }

    [Serializable]
    public sealed class PlayerHealthSettings
    {
        [Min(1f)] public float maxHealth = 100f;
        [Min(0f)] public float hurtInvulnerability = 0.5f;
        [Min(0f)] public float hurtFlashDuration = 0.12f;
        [Min(0.01f)] public float blinkInterval = 0.08f;
        [Range(0f, 1f)] public float blinkAlpha = 0.35f;

        internal void Validate()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            hurtInvulnerability = Mathf.Max(0f, hurtInvulnerability);
            hurtFlashDuration = Mathf.Max(0f, hurtFlashDuration);
            blinkInterval = Mathf.Max(0.01f, blinkInterval);
            blinkAlpha = Mathf.Clamp01(blinkAlpha);
        }
    }

    [Serializable]
    public sealed class ShootingSettings
    {
        [Min(0.01f)] public float fireRate = 6f;
        [Min(0f)] public float bulletSpeed = 18f;
        [Min(0f)] public float bulletDamage = 10f;
        [Min(0.01f)] public float bulletLifetime = 2f;
        [Min(0f)] public float muzzleOffset = 0.75f;

        internal void Validate()
        {
            fireRate = Mathf.Max(0.01f, fireRate);
            bulletSpeed = Mathf.Max(0f, bulletSpeed);
            bulletDamage = Mathf.Max(0f, bulletDamage);
            bulletLifetime = Mathf.Max(0.01f, bulletLifetime);
            muzzleOffset = Mathf.Max(0f, muzzleOffset);
        }
    }

    [Serializable]
    public sealed class CombatFeedbackSettings
    {
        [Min(0f)] public float whiteFlashDuration = 0.08f;
        [Min(0f)] public float knockbackSpeed = 4f;
        [Min(0.01f)] public float knockbackDuration = 0.12f;
        [Min(0f)] public float hitStopDuration = 0.04f;
        [Min(0.01f)] public float damageNumberLifetime = 0.65f;
        [Min(0f)] public float damageNumberRise = 1.2f;
        [Min(0.01f)] public float trailLifetime = 0.08f;
        [Min(0f)] public float trailWidth = 0.055f;
        [Min(0.01f)] public float muzzleLifetime = 0.05f;

        internal void Validate()
        {
            whiteFlashDuration = Mathf.Max(0f, whiteFlashDuration);
            knockbackSpeed = Mathf.Max(0f, knockbackSpeed);
            knockbackDuration = Mathf.Max(0.01f, knockbackDuration);
            hitStopDuration = Mathf.Max(0f, hitStopDuration);
            damageNumberLifetime = Mathf.Max(0.01f, damageNumberLifetime);
            damageNumberRise = Mathf.Max(0f, damageNumberRise);
            trailLifetime = Mathf.Max(0.01f, trailLifetime);
            trailWidth = Mathf.Max(0f, trailWidth);
            muzzleLifetime = Mathf.Max(0.01f, muzzleLifetime);
        }
    }

    [Serializable]
    public sealed class EnemySettings
    {
        [Min(1f)] public float meleeHp = 30f;
        [Min(0f)] public float meleeSpeed = 3.5f;
        [Min(0f)] public float meleeDamage = 10f;
        [Min(0f)] public float meleeWindup = 0.35f;
        [Min(0.01f)] public float meleeInterval = 1f;
        [Min(0.1f)] public float meleeRange = 1.65f;
        [Range(1f, 180f)] public float meleeHalfAngle = 65f;
        [Min(0.01f)] public float meleeRecovery = 0.3f;
        [Min(0f)] public float meleeTurnSpeed = 540f;
        [Min(0.1f)] public float meleeRepathInterval = 0.5f;
        [Min(1f)] public float rangedHp = 20f;
        [Min(0f)] public float rangedKeepDistance = 7f;
        [Min(0f)] public float rangedWindup = 0.45f;
        [Min(0.01f)] public float rangedFireRate = 1.25f;
        [Min(0f)] public float rangedSpeed = 2.8f;
        [Min(0.1f)] public float rangedDistanceTolerance = 0.75f;
        [Min(0.1f)] public float rangedMaxDistance = 12f;
        [Min(0.01f)] public float rangedRecovery = 0.45f;
        [Min(0f)] public float rangedDamage = 10f;
        [Min(0f)] public float rangedProjectileSpeed = 10f;
        [Min(0.01f)] public float rangedProjectileLifetime = 3f;
        [Min(0.01f)] public float deathHoldDuration = 0.5f;

        internal void Validate()
        {
            meleeHp = Mathf.Max(1f, meleeHp);
            meleeSpeed = Mathf.Max(0f, meleeSpeed);
            meleeDamage = Mathf.Max(0f, meleeDamage);
            meleeWindup = Mathf.Max(0f, meleeWindup);
            meleeInterval = Mathf.Max(0.01f, meleeInterval);
            meleeRange = Mathf.Max(0.1f, meleeRange);
            meleeHalfAngle = Mathf.Clamp(meleeHalfAngle, 1f, 180f);
            meleeRecovery = Mathf.Max(0.01f, meleeRecovery);
            meleeTurnSpeed = Mathf.Max(0f, meleeTurnSpeed);
            meleeRepathInterval = Mathf.Max(0.1f, meleeRepathInterval);
            rangedHp = Mathf.Max(1f, rangedHp);
            rangedKeepDistance = Mathf.Max(0f, rangedKeepDistance);
            rangedWindup = Mathf.Max(0f, rangedWindup);
            rangedFireRate = Mathf.Max(0.01f, rangedFireRate);
            rangedSpeed = Mathf.Max(0f, rangedSpeed);
            rangedDistanceTolerance = Mathf.Max(0.1f, rangedDistanceTolerance);
            rangedMaxDistance = Mathf.Max(rangedKeepDistance + rangedDistanceTolerance, rangedMaxDistance);
            rangedRecovery = Mathf.Max(0.01f, rangedRecovery);
            rangedDamage = Mathf.Max(0f, rangedDamage);
            rangedProjectileSpeed = Mathf.Max(0f, rangedProjectileSpeed);
            rangedProjectileLifetime = Mathf.Max(0.01f, rangedProjectileLifetime);
            deathHoldDuration = Mathf.Max(0.01f, deathHoldDuration);
        }
    }

    [Serializable]
    public sealed class SpawnSettings
    {
        [Min(0.01f)] public float spawnInterval = 0.5f;
        [Min(1)] public int maxAlive = 100;
        public AnimationCurve waveCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [Min(0f)] public float spawnRadius = 12f;
        [Min(0f)] public float radiusVariation = 2f;
        [Min(0f)] public float minPlayerDistance = 6f;
        [Min(0f)] public float clearance = 0.15f;
        [Min(0f)] public float initialDelay = 2f;
        [Min(1f)] public float difficultyDuration = 120f;
        [Range(1, 64)] public int positionAttempts = 16;

        internal void Validate()
        {
            spawnInterval = Mathf.Max(0.01f, spawnInterval);
            maxAlive = Mathf.Max(1, maxAlive);
            spawnRadius = Mathf.Max(0f, spawnRadius);
            radiusVariation = Mathf.Max(0f, radiusVariation);
            minPlayerDistance = Mathf.Max(0f, minPlayerDistance);
            clearance = Mathf.Max(0f, clearance);
            initialDelay = Mathf.Max(0f, initialDelay);
            difficultyDuration = Mathf.Max(1f, difficultyDuration);
            positionAttempts = Mathf.Clamp(positionAttempts, 1, 64);

            if (waveCurve == null || waveCurve.length == 0)
            {
                waveCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
            }
        }
    }

    [Serializable]
    public sealed class CameraSettings
    {
        [Range(0f, 30f)] public float followLerp = 10f;
        [Min(0f)] public float shakeAmplitude = 0.2f;
        [Min(0f)] public float shakeDuration = 0.15f;

        internal void Validate()
        {
            followLerp = Mathf.Clamp(followLerp, 0f, 30f);
            shakeAmplitude = Mathf.Max(0f, shakeAmplitude);
            shakeDuration = Mathf.Max(0f, shakeDuration);
        }
    }

    [Serializable]
    public sealed class StressSettings
    {
        public int[] stressTiers = { 100, 500, 1000 };

        internal void Validate()
        {
            if (stressTiers == null || stressTiers.Length == 0)
            {
                stressTiers = new[] { 100, 500, 1000 };
                return;
            }

            for (int i = 0; i < stressTiers.Length; i++)
            {
                stressTiers[i] = Mathf.Max(1, stressTiers[i]);
            }
        }
    }

    [Serializable]
    public sealed class PerformanceSettings
    {
        [Min(0.01f)] public float aiTickInterval = 0.1f;
        [Min(0f)] public float farDistanceThreshold = 15f;
        [Min(1f)] public float farTickMultiplier = 3f;

        internal void Validate()
        {
            aiTickInterval = Mathf.Max(0.01f, aiTickInterval);
            farDistanceThreshold = Mathf.Max(0f, farDistanceThreshold);
            farTickMultiplier = Mathf.Max(1f, farTickMultiplier);
        }
    }
}
