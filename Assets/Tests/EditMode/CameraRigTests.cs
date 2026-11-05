using NUnit.Framework;
using ShootDemo.Player;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class CameraRigTests
    {
        [Test]
        public void ClampToBounds_PullsTheViewBackInsideTheArena()
        {
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(20f, 16f, 0f));

            Vector2 clamped = ArenaCamera.ClampToBounds(new Vector2(100f, 100f), bounds, 3f, 2f);

            Assert.That(clamped.x, Is.EqualTo(7f).Within(0.001f));
            Assert.That(clamped.y, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void ClampToBounds_CentersTheCameraWhenTheViewIsLargerThanTheArena()
        {
            Bounds bounds = new Bounds(new Vector3(2f, -4f, 0f), new Vector3(4f, 4f, 0f));

            Vector2 clamped = ArenaCamera.ClampToBounds(new Vector2(50f, 50f), bounds, 5f, 5f);

            Assert.That(clamped.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(clamped.y, Is.EqualTo(-4f).Within(0.001f));
        }

        [Test]
        public void VerticalLead_PutsThePlayerBelowCenterWhenAimingUp()
        {
            Assert.That(ArenaCamera.VerticalLead(1f, 1.25f), Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(ArenaCamera.VerticalLead(-1f, 1.25f), Is.EqualTo(-1.25f).Within(0.001f));
            Assert.That(ArenaCamera.VerticalLead(0.5f, 1.25f), Is.EqualTo(0.625f).Within(0.001f));
            Assert.That(ArenaCamera.VerticalLead(1f, -2f), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void EvaluateShake_EndsAtZeroAndStaysWithinAmplitude()
        {
            const float amplitude = 0.2f;
            Vector2 ended = ArenaCamera.EvaluateShake(0f, 0.15f, amplitude, 1.2f);
            Vector2 peak = ArenaCamera.EvaluateShake(0.15f, 0.15f, amplitude, 1.2f);

            Assert.That(ended, Is.EqualTo(Vector2.zero));
            Assert.That(peak.magnitude, Is.LessThanOrEqualTo(amplitude + 0.0001f));
        }
    }
}
