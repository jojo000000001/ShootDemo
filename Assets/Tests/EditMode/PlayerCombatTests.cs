using NUnit.Framework;
using ShootDemo.Combat;
using ShootDemo.Player;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class PlayerCombatTests
    {
        [Test]
        public void DirectionFrom_PointsFromThePlayerToTheCursor()
        {
            Vector2 direction = PlayerAim.DirectionFrom(new Vector2(2f, 1f), new Vector2(5f, 5f));

            Assert.That(direction.x, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(direction.y, Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void FacingAngle_KeepsTheRightFacingGunAlignedWithTheCursor()
        {
            Vector2 direction = PlayerAim.DirectionFrom(Vector2.zero, Vector2.up);
            float angle = PlayerMovement.CalculateFacingAngle(Vector2.right, direction);

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, 90f)), Is.LessThan(0.001f));
        }

        [Test]
        public void TryFire_HoldsAStableIntervalWhileTheTriggerIsHeld()
        {
            float interval = PlayerWeapon.ShotInterval(6f);
            float cooldown = 0f;

            Assert.That(PlayerWeapon.TryFire(true, ref cooldown, interval, 0.02f), Is.True);
            Assert.That(PlayerWeapon.TryFire(true, ref cooldown, interval, 0.02f), Is.False);

            bool firedAgain = false;
            for (int i = 0; i < 8; i++)
            {
                firedAgain = PlayerWeapon.TryFire(true, ref cooldown, interval, 0.02f);
            }

            Assert.That(firedAgain, Is.True);
            Assert.That(interval, Is.EqualTo(1f / 6f).Within(0.0001f));
        }

        [Test]
        public void MuzzleAndBullet_TravelAlongTheAimDirection()
        {
            Vector2 direction = Vector2.right;
            Vector2 muzzle = PlayerWeapon.MuzzlePosition(new Vector2(1f, 2f), direction, 0.75f);
            Vector2 next = Bullet.NextPosition(muzzle, direction, 18f, 0.1f);

            Assert.That(muzzle, Is.EqualTo(new Vector2(1.75f, 2f)));
            Assert.That(next.x, Is.EqualTo(3.55f).Within(0.001f));
            Assert.That(next.y, Is.EqualTo(2f).Within(0.001f));
        }
    }
}
