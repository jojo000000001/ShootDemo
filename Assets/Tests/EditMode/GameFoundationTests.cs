using NUnit.Framework;
using ShootDemo.Config;
using ShootDemo.Core;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class GameFoundationTests
    {
        private GameObject stateMachineObject;
        private GameStateMachine stateMachine;

        [SetUp]
        public void SetUp()
        {
            stateMachineObject = new GameObject("GameStateMachine_Test");
            stateMachine = stateMachineObject.AddComponent<GameStateMachine>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(stateMachineObject);
        }

        [Test]
        public void StateMachine_UsesPlayingAsInitialState()
        {
            Assert.That(stateMachine.CurrentState, Is.EqualTo(GameState.Playing));
        }

        [Test]
        public void TryChangeState_RejectsInvalidTransitionWithoutRaisingEvent()
        {
            Assert.That(stateMachine.TryChangeState(GameState.GameOver), Is.True);

            int eventCount = 0;
            stateMachine.StateChanged += (_, __) => eventCount++;

            bool changed = stateMachine.TryChangeState(GameState.Paused);

            Assert.That(changed, Is.False);
            Assert.That(stateMachine.CurrentState, Is.EqualTo(GameState.GameOver));
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void TryChangeState_RaisesOneEventWithPreviousAndCurrentState()
        {
            GameState eventPrevious = GameState.GameOver;
            GameState eventCurrent = GameState.GameOver;
            int eventCount = 0;
            stateMachine.StateChanged += (previous, current) =>
            {
                eventPrevious = previous;
                eventCurrent = current;
                eventCount++;
            };

            bool changed = stateMachine.TryChangeState(GameState.Paused);

            Assert.That(changed, Is.True);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(eventPrevious, Is.EqualTo(GameState.Playing));
            Assert.That(eventCurrent, Is.EqualTo(GameState.Paused));
        }

        [Test]
        public void TryChangeState_AllowsMenuToEnterAndLeavePlay()
        {
            Assert.That(stateMachine.TryChangeState(GameState.Menu), Is.True);
            Assert.That(stateMachine.TryChangeState(GameState.Paused), Is.False);
            Assert.That(stateMachine.CurrentState, Is.EqualTo(GameState.Menu));

            Assert.That(stateMachine.TryChangeState(GameState.Playing), Is.True);
            Assert.That(stateMachine.TryChangeState(GameState.GameOver), Is.True);
            Assert.That(stateMachine.TryChangeState(GameState.Menu), Is.True);
            Assert.That(stateMachine.CurrentState, Is.EqualTo(GameState.Menu));
        }

        [Test]
        public void GameConfig_ContainsPlannedStressTiers()
        {
            GameConfig config = ScriptableObject.CreateInstance<GameConfig>();

            try
            {
                Assert.That(config.stress.stressTiers, Is.EqualTo(new[] { 100, 500, 1000 }));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
