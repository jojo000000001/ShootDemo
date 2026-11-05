using NUnit.Framework;
using ShootDemo.Config;
using ShootDemo.Player;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class PlayerMovementTests
    {
        private MovementSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = new MovementSettings
            {
                moveSpeed = 6f,
                accel = 36f,
                decel = 48f,
                turnSpeed = 540f
            };
        }

        [Test]
        public void CalculateNextVelocity_WalkIsSlowerAndDiagonalSpeedIsClamped()
        {
            Vector2 velocity = PlayerMovement.CalculateNextVelocity(
                Vector2.zero, Vector2.one, settings, 1f, false);

            Assert.That(velocity.magnitude, Is.EqualTo(settings.walkSpeed).Within(0.001f));
            Assert.That(velocity.magnitude, Is.LessThan(settings.moveSpeed));
        }

        [Test]
        public void CalculateNextVelocity_ReleasingRunBrakesSmoothlyToWalkSpeed()
        {
            Vector2 velocity = Vector2.right * settings.moveSpeed;
            velocity = PlayerMovement.CalculateNextVelocity(
                velocity, Vector2.right, settings, 0.02f, false);

            Assert.That(velocity.x, Is.EqualTo(settings.moveSpeed - settings.decel * 0.02f).Within(0.001f));

            for (int i = 0; i < 20; i++)
            {
                velocity = PlayerMovement.CalculateNextVelocity(
                    velocity, Vector2.right, settings, 0.02f, false);
            }

            Assert.That(velocity.x, Is.EqualTo(settings.walkSpeed).Within(0.001f));
        }

        [Test]
        public void CalculateNextVelocity_AcceleratesWithoutJumpingToTopSpeed()
        {
            Vector2 velocity = PlayerMovement.CalculateNextVelocity(
                Vector2.zero,
                Vector2.right,
                settings,
                0.02f);

            Assert.That(velocity.x, Is.GreaterThan(0f).And.LessThan(settings.moveSpeed));
            Assert.That(velocity.y, Is.EqualTo(0f));
        }

        [Test]
        public void CalculateNextVelocity_DeceleratesAfterInputIsReleased()
        {
            Vector2 velocity = PlayerMovement.CalculateNextVelocity(
                Vector2.right * settings.moveSpeed,
                Vector2.zero,
                settings,
                0.02f);

            Assert.That(velocity.x, Is.GreaterThan(0f).And.LessThan(settings.moveSpeed));
            Assert.That(velocity.y, Is.EqualTo(0f));
        }

        [Test]
        public void CalculateNextVelocity_UsesCombinedBrakingWhenDirectionReverses()
        {
            Vector2 start = Vector2.right * settings.moveSpeed;

            Vector2 reversed = PlayerMovement.CalculateNextVelocity(
                start,
                Vector2.left,
                settings,
                0.02f);
            Vector2 released = PlayerMovement.CalculateNextVelocity(
                start,
                Vector2.zero,
                settings,
                0.02f);

            Assert.That(reversed.x, Is.LessThan(released.x));
            Assert.That(reversed.y, Is.EqualTo(0f));
        }

        [Test]
        public void CalculateNextVelocity_ClampsDiagonalTargetSpeed()
        {
            Vector2 velocity = Vector2.zero;

            for (int i = 0; i < 100; i++)
            {
                velocity = PlayerMovement.CalculateNextVelocity(
                    velocity,
                    Vector2.one,
                    settings,
                    0.02f);
            }

            Assert.That(velocity.magnitude, Is.EqualTo(settings.moveSpeed).Within(0.001f));
        }

        [TestCase(1f, 0f, 0f)]
        [TestCase(-1f, 0f, 180f)]
        [TestCase(0f, 1f, 90f)]
        [TestCase(0f, -1f, -90f)]
        public void CalculateFacingAngle_UsesCharactersLocalForward(
            float directionX,
            float directionY,
            float expectedAngle)
        {
            float angle = PlayerMovement.CalculateFacingAngle(
                Vector2.right,
                new Vector2(directionX, directionY));

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, expectedAngle)), Is.LessThan(0.001f));
        }

        [Test]
        public void CalculateNextFacingAngle_TurnsWithoutSnapping()
        {
            float angle = PlayerMovement.CalculateNextFacingAngle(
                90f,
                0f,
                settings.turnSpeed,
                0.02f);

            Assert.That(angle, Is.EqualTo(79.2f).Within(0.001f));
        }
    }
}
