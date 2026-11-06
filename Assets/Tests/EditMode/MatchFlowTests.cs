using NUnit.Framework;
using ShootDemo.Core;
using ShootDemo.UI;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class MatchFlowTests
    {
        private GameObject stateMachineObject;
        private GameStateMachine stateMachine;

        [SetUp]
        public void SetUp()
        {
            stateMachineObject = new GameObject("MatchFlow_State");
            stateMachine = stateMachineObject.AddComponent<GameStateMachine>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(stateMachineObject);
        }

        [Test]
        public void Menu_CanStartAndCannotEndOrPause()
        {
            stateMachine.TryChangeState(GameState.Menu);

            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Start)), Is.True);
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Pause)), Is.False);
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.End)), Is.False);
        }

        [Test]
        public void Playing_CanPauseAndCannotStart()
        {
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Pause)), Is.True);
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Start)), Is.False);
        }

        [Test]
        public void Paused_CanResumeOrEnd()
        {
            stateMachine.TryChangeState(GameState.Paused);

            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Resume)), Is.True);
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.End)), Is.True);
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Pause)), Is.False);
        }

        [Test]
        public void GameOver_CanRestartOrReturnToMenu()
        {
            stateMachine.TryChangeState(GameState.GameOver);

            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Restart)), Is.True);
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.BackToMenu)), Is.True);
            Assert.That(stateMachine.CanChangeState(Target(MatchFlowAction.Pause)), Is.False);
            Assert.That(MatchFlowScreen.ReloadsScene(MatchFlowAction.Restart), Is.True);
            Assert.That(MatchFlowScreen.ReloadsScene(MatchFlowAction.BackToMenu), Is.True);
            Assert.That(MatchFlowScreen.ReloadsScene(MatchFlowAction.End), Is.False);
            Assert.That(MatchFlowScreen.IsActionLegal(GameState.GameOver, MatchFlowAction.Start), Is.False);
            Assert.That(MatchFlowScreen.IsActionLegal(GameState.Menu, MatchFlowAction.End), Is.False);
            Assert.That(MatchFlowScreen.IsActionLegal(GameState.Playing, MatchFlowAction.Restart), Is.False);
        }

        private static GameState Target(MatchFlowAction action)
        {
            Assert.That(MatchFlowScreen.TryGetTarget(action, out GameState target), Is.True);
            return target;
        }
    }
}
