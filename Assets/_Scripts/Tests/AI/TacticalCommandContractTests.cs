using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class TacticalCommandContractTests
	{
		[Test]
		public void IssueCommand_FromIdle_AcceptsDefenseAttackSearchFleeCancel()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Defense(new Vector3(5f, 0f, 1f)), UnitAIState.Defense);
				AssertAccepted(controller, TacticalCommand.Cancel(), UnitAIState.Idle);
				AssertAccepted(controller, TacticalCommand.Attack(new Vector3(12f, 0f, 3f)), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Cancel(), UnitAIState.Idle);
				AssertAccepted(controller, TacticalCommand.Search(new Vector3(4f, 0f, 8f)), UnitAIState.Search);
				AssertAccepted(controller, TacticalCommand.Cancel(), UnitAIState.Idle);
				AssertAccepted(controller, TacticalCommand.Flee(new Vector3(-10f, 0f, 0f)), UnitAIState.Flee);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_IdleToRetreat_RejectsWithoutBounce()
		{
			UnitAIController controller = Create();
			try
			{
				TacticalCommandResult result = controller.IssueCommand(TacticalCommand.Retreat(new Vector3(-8f, 0f, 0f)));
				AssertRejected(result, TacticalCommandRejectReason.InvalidStateTransition);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.IsFalse(controller.CurrentContext.HasDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_RetreatFromDefense_AcceptsDestination()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Defense(new Vector3(1f, 0f, 0f)), UnitAIState.Defense);
				Vector3 dest = new Vector3(-8f, 0f, 2f);
				AssertAccepted(controller, TacticalCommand.Retreat(dest), UnitAIState.Retreat);
				Assert.AreEqual(dest, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_WritesExistingContextFields()
		{
			UnitAIController controller = Create();
			GameObject targetGo = new GameObject("CMD_Target");
			try
			{
				Vector3 attack = new Vector3(12f, 0f, 3f);
				AssertAccepted(controller, TacticalCommand.Attack(attack, targetGo.transform), UnitAIState.Attack);
				Assert.AreEqual(attack, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
				Assert.AreSame(targetGo.transform, controller.CurrentContext.TargetEntity);

				Vector3 defense = new Vector3(5f, 0f, 1f);
				AssertAccepted(controller, TacticalCommand.Defense(defense), UnitAIState.Defense);
				Assert.AreEqual(defense, controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(defense, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);

				Vector3 search = new Vector3(7f, 0f, 9f);
				AssertAccepted(controller, TacticalCommand.Search(search), UnitAIState.Search);
				Assert.AreEqual(search, controller.CurrentContext.SearchPosition);

				Vector3 retreat = new Vector3(-6f, 0f, 0f);
				AssertAccepted(controller, TacticalCommand.Retreat(retreat), UnitAIState.Retreat);
				Assert.AreEqual(retreat, controller.CurrentContext.Destination);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(targetGo);
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_SameStateAttack_ReplacesDestinationWithoutIdleBounce()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 first = new Vector3(4f, 0f, 0f);
				Vector3 next = new Vector3(9f, 0f, 2f);
				AssertAccepted(controller, TacticalCommand.Attack(first), UnitAIState.Attack);
				controller.ClearTrace();
				TacticalCommandResult result = controller.IssueCommand(TacticalCommand.Attack(next));
				Assert.IsTrue(result.Accepted);
				Assert.AreEqual(TacticalCommandRejectReason.None, result.Reason);
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
		public void TryApplyCommand_SameState_StillDoesNotChangeContext()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 first = new Vector3(2f, 0f, 0f);
				Assert.IsTrue(controller.TryApplyCommand(
					UnitAICommand.Attack(UnitAIStateContext.ForAttack(first, Vector3.forward))));
				Assert.IsTrue(controller.TryApplyCommand(
					UnitAICommand.Attack(UnitAIStateContext.ForAttack(new Vector3(9f, 0f, 0f), Vector3.right))));
				Assert.AreEqual(first, controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_RetreatToAttackOrSearch_Rejects()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Defense(Vector3.zero), UnitAIState.Defense);
				AssertAccepted(controller, TacticalCommand.Retreat(new Vector3(-4f, 0f, 0f)), UnitAIState.Retreat);

				TacticalCommandResult attack = controller.IssueCommand(TacticalCommand.Attack(new Vector3(8f, 0f, 0f)));
				AssertRejected(attack, TacticalCommandRejectReason.InvalidStateTransition);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);

				TacticalCommandResult search = controller.IssueCommand(TacticalCommand.Search(new Vector3(3f, 0f, 3f)));
				AssertRejected(search, TacticalCommandRejectReason.InvalidStateTransition);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(new Vector3(-4f, 0f, 0f), controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_FleeToDefenseOrAttack_Rejects()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Flee(new Vector3(-10f, 0f, 0f)), UnitAIState.Flee);

				TacticalCommandResult defense = controller.IssueCommand(TacticalCommand.Defense(Vector3.one));
				AssertRejected(defense, TacticalCommandRejectReason.InvalidStateTransition);
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);

				TacticalCommandResult attack = controller.IssueCommand(TacticalCommand.Attack(new Vector3(6f, 0f, 0f)));
				AssertRejected(attack, TacticalCommandRejectReason.InvalidStateTransition);
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_CancelFromFlee_GoesIdle()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Flee(new Vector3(-10f, 0f, 0f)), UnitAIState.Flee);
				AssertAccepted(controller, TacticalCommand.Cancel(), UnitAIState.Idle);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_MissingDestination_Rejects()
		{
			UnitAIController controller = Create();
			try
			{
				TacticalCommandResult result = controller.IssueCommand(
					TacticalCommand.Create(TacticalCommandType.Attack, default, false));
				AssertRejected(result, TacticalCommandRejectReason.MissingDestination);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_InvalidData_Rejects()
		{
			UnitAIController controller = Create();
			try
			{
				TacticalCommandResult nan = controller.IssueCommand(
					TacticalCommand.Attack(new Vector3(float.NaN, 0f, 0f)));
				AssertRejected(nan, TacticalCommandRejectReason.InvalidCommandData);

				TacticalCommandResult unknown = controller.IssueCommand(
					TacticalCommand.Create((TacticalCommandType)99, Vector3.one, true));
				AssertRejected(unknown, TacticalCommandRejectReason.InvalidCommandData);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_CancelFromIdle_AcceptedStayIdle()
		{
			UnitAIController controller = Create();
			try
			{
				TacticalCommandResult result = controller.IssueCommand(TacticalCommand.Cancel());
				Assert.IsTrue(result.Accepted);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void IssueCommand_DoesNotChangeRoeOrPerception()
		{
			UnitAIController controller = Create();
			try
			{
				UseOfForceLevel roe = controller.CurrentUseOfForceLevel;
				controller.SetPerceptionFrame(AIPerceptionFrame.Empty);
				Assert.AreEqual(0, controller.CurrentPerception.AllContacts.Count);

				TacticalCommandResult result = controller.IssueCommand(TacticalCommand.Attack(new Vector3(8f, 0f, 0f)));
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
		public void IssueCommand_ZeroPosition_IsValidDestination()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(Vector3.zero), UnitAIState.Attack);
				Assert.AreEqual(Vector3.zero, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		private static void AssertAccepted(UnitAIController _controller, TacticalCommand _command, UnitAIState _state)
		{
			TacticalCommandResult result = _controller.IssueCommand(in _command);
			Assert.IsTrue(result.Accepted, _command.Type.ToString());
			Assert.AreEqual(TacticalCommandRejectReason.None, result.Reason);
			Assert.AreEqual(_state, _controller.CurrentState);
		}

		private static void AssertRejected(TacticalCommandResult _result, TacticalCommandRejectReason _reason)
		{
			Assert.IsFalse(_result.Accepted);
			Assert.AreEqual(_reason, _result.Reason);
		}

		private static UnitAIController Create()
		{
			var go = new GameObject("AI61_CommandContract");
			return go.AddComponent<UnitAIController>();
		}

		private static void Destroy(UnitAIController _controller)
		{
			if (_controller != null)
				UnityEngine.Object.DestroyImmediate(_controller.gameObject);
		}

	}
}
