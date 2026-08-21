using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class GameCommandServiceTests
	{
		[Test]
		public void Source_FromIdle_AcceptsDefenseAttackSearchFleeCancel()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, DebugGameCommandSource.Defense(controller, new Vector3(5f, 0f, 1f)),
					UnitAIState.Defense);
				AssertAccepted(controller, DebugGameCommandSource.Cancel(controller), UnitAIState.Idle);
				AssertAccepted(controller, DebugGameCommandSource.Attack(controller, new Vector3(12f, 0f, 3f)),
					UnitAIState.Attack);
				AssertAccepted(controller, DebugGameCommandSource.Cancel(controller), UnitAIState.Idle);
				AssertAccepted(controller, DebugGameCommandSource.Search(controller, new Vector3(4f, 0f, 8f)),
					UnitAIState.Search);
				AssertAccepted(controller, DebugGameCommandSource.Cancel(controller), UnitAIState.Idle);
				AssertAccepted(controller, DebugGameCommandSource.Flee(controller, new Vector3(-10f, 0f, 0f)),
					UnitAIState.Flee);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Source_RetreatFromDefense_Accepts()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, DebugGameCommandSource.Defense(controller, new Vector3(1f, 0f, 0f)),
					UnitAIState.Defense);
				Vector3 dest = new Vector3(-8f, 0f, 2f);
				AssertAccepted(controller, DebugGameCommandSource.Retreat(controller, dest), UnitAIState.Retreat);
				Assert.AreEqual(dest, controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Source_CancelAfterAttack_GoesIdle()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, DebugGameCommandSource.Attack(controller, new Vector3(8f, 0f, 0f)),
					UnitAIState.Attack);
				AssertAccepted(controller, DebugGameCommandSource.Cancel(controller), UnitAIState.Idle);
				Assert.IsFalse(controller.CurrentContext.HasDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Source_SameStateAttack_ReplacesDestinationWithoutIdleBounce()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 first = new Vector3(4f, 0f, 0f);
				Vector3 next = new Vector3(9f, 0f, 2f);
				AssertAccepted(controller, DebugGameCommandSource.Attack(controller, first), UnitAIState.Attack);
				controller.ClearTrace();
				GameCommandResult result = DebugGameCommandSource.Attack(controller, next);
				Assert.IsTrue(result.Accepted);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(next, controller.CurrentContext.Destination);
				CollectionAssert.DoesNotContain(controller.Trace, "Enter:Idle");
				CollectionAssert.Contains(controller.Trace, "Exit:Attack");
				CollectionAssert.Contains(controller.Trace, "Enter:Attack");
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Issue_NullUnit_RejectsInvalidUnit()
		{
			GameCommandResult result = GameCommandService.Issue((Component)null,
				TacticalCommand.Attack(Vector3.one, null, TacticalCommandSource.Game));
			AssertRejected(result, GameCommandRejectReason.InvalidUnit);
		}

		[Test]
		public void Issue_DeadUnit_RejectsInvalidUnit()
		{
			UnitAIController controller = Create();
			try
			{
				UnitHealth health = controller.gameObject.AddComponent<UnitHealth>();
				health.EnterDead();
				GameCommandResult result = DebugGameCommandSource.Attack(controller, new Vector3(8f, 0f, 0f));
				AssertRejected(result, GameCommandRejectReason.InvalidUnit);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Issue_NoReceiver_RejectsNoAI()
		{
			var go = new GameObject("AI62_NoAI");
			try
			{
				GameCommandResult result = GameCommandService.Issue(go,
					TacticalCommand.Attack(new Vector3(3f, 0f, 0f), null, TacticalCommandSource.Game));
				AssertRejected(result, GameCommandRejectReason.NoAI);
				Assert.IsNull(go.GetComponent<UnitAIController>());
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Issue_MissingDestination_ForwardsAiReason()
		{
			UnitAIController controller = Create();
			try
			{
				GameCommandResult result = GameCommandService.Issue(controller,
					TacticalCommand.Create(TacticalCommandType.Attack, default, false, null, TacticalCommandSource.Game));
				AssertRejected(result, GameCommandRejectReason.MissingDestination);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Issue_StubReceiver_DoesNotRequireUnitAIController()
		{
			var go = new GameObject("AI62_StubReceiver");
			StubCommandReceiver stub = go.AddComponent<StubCommandReceiver>();
			try
			{
				Assert.IsNull(go.GetComponent<UnitAIController>());
				TacticalCommand command = TacticalCommand.Attack(new Vector3(6f, 0f, 0f), null, TacticalCommandSource.Game);
				GameCommandResult result = GameCommandService.Issue(go, in command);
				Assert.IsTrue(result.Accepted);
				Assert.AreEqual(1, stub.IssueCount);
				Assert.AreEqual(TacticalCommandType.Attack, stub.LastType);
				Assert.IsNull(go.GetComponent<UnitAIController>());
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Source_DoesNotChangeRoeOrPerception()
		{
			UnitAIController controller = Create();
			try
			{
				UseOfForceLevel roe = controller.CurrentUseOfForceLevel;
				controller.SetPerceptionFrame(AIPerceptionFrame.Empty);
				GameCommandResult result = DebugGameCommandSource.Attack(controller, new Vector3(8f, 0f, 0f));
				Assert.IsTrue(result.Accepted);
				Assert.AreEqual(roe, controller.CurrentUseOfForceLevel);
				Assert.AreEqual(0, controller.CurrentPerception.AllContacts.Count);
				Assert.IsFalse(controller.HasHostileVisible);
				Assert.IsNull(controller.CurrentEngageTarget);
				Assert.IsNull(controller.GetComponent<TargetSelector>());
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Source_UsesGameSource()
		{
			UnitAIController controller = Create();
			try
			{
				DebugGameCommandSource.Attack(controller, new Vector3(2f, 0f, 0f));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		private static void AssertAccepted(UnitAIController _controller, GameCommandResult _result, UnitAIState _state)
		{
			Assert.IsTrue(_result.Accepted, _state.ToString());
			Assert.AreEqual(GameCommandRejectReason.None, _result.Reason);
			Assert.AreEqual(_state, _controller.CurrentState);
		}

		private static void AssertRejected(GameCommandResult _result, GameCommandRejectReason _reason)
		{
			Assert.IsFalse(_result.Accepted);
			Assert.AreEqual(_reason, _result.Reason);
		}

		private static UnitAIController Create()
		{
			var go = new GameObject("AI62_GameCommand");
			return go.AddComponent<UnitAIController>();
		}

		private static void Destroy(UnitAIController _controller)
		{
			if (_controller != null)
				UnityEngine.Object.DestroyImmediate(_controller.gameObject);
		}

		private sealed class StubCommandReceiver : MonoBehaviour, ITacticalCommandReceiver
		{
			public int IssueCount;
			public TacticalCommandType LastType;

			public TacticalCommandResult IssueCommand(in TacticalCommand _command)
			{
				IssueCount++;
				LastType = _command.Type;
				return TacticalCommandResult.Ok();
			}
		}
	}
}
