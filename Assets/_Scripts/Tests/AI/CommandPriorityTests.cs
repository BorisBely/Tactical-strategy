using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class CommandPriorityTests
	{
		#region A Command replacement
		[Test]
		public void A1_Idle_ToAttack()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				Assert.AreEqual(UnitAIPriorityDecision.Interrupt, controller.LastPriorityEvaluation.Decision);
				Assert.AreEqual(UnitAIPriorityReason.HigherPriority, controller.LastPriorityEvaluation.Reason);
				Assert.IsFalse(controller.HasPendingCommand);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void A2_Attack_ToRetreat()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Retreat(P(-6f)), UnitAIState.Retreat);
				Assert.AreEqual(UnitAIPriorityDecision.Interrupt, controller.LastPriorityEvaluation.Decision);
				Assert.AreEqual(UnitAIPriorityReason.HigherPriority, controller.LastPriorityEvaluation.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void A3_Search_ToAttack_CancelsSearch()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Search(P(4f, 3f)), UnitAIState.Search);
				AssertAccepted(controller, TacticalCommand.Attack(P(12f)), UnitAIState.Attack);
				Assert.AreEqual(UnitAISearchCompletionReason.NewOrder, controller.LastSearchCompletionReason);
				Assert.IsNull(controller.SearchSession);
				Assert.AreEqual(P(12f), controller.CurrentContext.Destination);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentContext.ResumeState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void A4_Attack_ToSearch_KeepsReturnContext()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 attack = P(10f);
				AssertAccepted(controller, TacticalCommand.Attack(attack), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Search(P(3f, 5f)), UnitAIState.Search);
				Assert.AreEqual(UnitAIPriorityReason.OverlaySearch, controller.LastPriorityEvaluation.Reason);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentContext.ResumeState);
				Assert.AreEqual(attack, controller.CurrentContext.Destination);
				Assert.AreEqual(P(3f, 5f), controller.CurrentContext.SearchPosition);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region B Same-state command
		[Test]
		public void B1_AttackA_ToAttackB_ReplacesContextWithoutIdleBounce()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(4f)), UnitAIState.Attack);
				controller.ClearTrace();
				AssertAccepted(controller, TacticalCommand.Attack(P(9f, 2f)), UnitAIState.Attack);
				Assert.AreEqual(P(9f, 2f), controller.CurrentContext.Destination);
				Assert.AreEqual(UnitAIPriorityDecision.ReplaceContext, controller.LastPriorityEvaluation.Decision);
				CollectionAssert.DoesNotContain(controller.Trace, "Enter:Idle");
				CollectionAssert.Contains(controller.Trace, "Exit:Attack");
				CollectionAssert.Contains(controller.Trace, "Enter:Attack");
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region C Command cancellation
		[Test]
		public void C1_Search_Cancel_ReturnsToAttack()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 attack = P(11f);
				AssertAccepted(controller, TacticalCommand.Attack(attack), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Search(P(2f, 4f)), UnitAIState.Search);
				AssertAccepted(controller, TacticalCommand.Cancel(), UnitAIState.Attack);
				Assert.AreEqual(UnitAISearchCompletionReason.Cancelled, controller.LastSearchCompletionReason);
				Assert.AreEqual(UnitAIPriorityDecision.Resume, controller.LastPriorityEvaluation.Decision);
				Assert.AreEqual(UnitAIPriorityReason.ResumeReturnState, controller.LastPriorityEvaluation.Reason);
				Assert.AreEqual(attack, controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void C2_IdleSearch_Cancel_GoesIdle()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Search(P(5f)), UnitAIState.Search);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentContext.ResumeState);
				AssertAccepted(controller, TacticalCommand.Cancel(), UnitAIState.Idle);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region D Priority conflict
		[Test]
		public void D1_Attack_Retreat_Wins()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Retreat(P(-4f)), UnitAIState.Retreat);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void D2_Search_Attack_Wins()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Search(P(3f)), UnitAIState.Search);
				AssertAccepted(controller, TacticalCommand.Attack(P(9f)), UnitAIState.Attack);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void D3_Retreat_Search_Rejected()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Defense(P(0f)), UnitAIState.Defense);
				AssertAccepted(controller, TacticalCommand.Retreat(P(-5f)), UnitAIState.Retreat);
				TacticalCommandResult result = controller.IssueCommand(TacticalCommand.Search(P(3f)));
				Assert.IsFalse(result.Accepted);
				Assert.AreEqual(TacticalCommandRejectReason.InvalidStateTransition, result.Reason);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(UnitAIPriorityReason.IllegalTransition, controller.LastPriorityEvaluation.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void D4_Retreat_Defense_RejectedLowerPriority()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Defense(P(0f)), UnitAIState.Defense);
				AssertAccepted(controller, TacticalCommand.Retreat(P(-5f)), UnitAIState.Retreat);
				TacticalCommandResult result = controller.IssueCommand(TacticalCommand.Defense(P(2f)));
				Assert.IsFalse(result.Accepted);
				Assert.AreEqual(TacticalCommandRejectReason.LowerPriority, result.Reason);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(P(-5f), controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region E ImmediateThreat
		[Test]
		public void E1_Attack_ImmediateThreat_StaysAttack_NotFlee()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreNotEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(UnitAIPriorityDecision.HoldState, controller.LastPriorityEvaluation.Decision);
				Assert.AreEqual(UnitAIPriorityReason.EmergencyLocal, controller.LastPriorityEvaluation.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void E2_Search_ImmediateThreat_StaysSearch()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 attack = P(8f);
				AssertAccepted(controller, TacticalCommand.Attack(attack), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Search(P(2f)), UnitAIState.Search);
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreNotEqual(UnitAISearchCompletionReason.Threat, controller.LastSearchCompletionReason);
				Assert.AreNotEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(UnitAIPriorityDecision.HoldState, controller.LastPriorityEvaluation.Decision);
				Assert.AreEqual(UnitAIPriorityReason.EmergencyLocal, controller.LastPriorityEvaluation.Reason);
				Assert.AreEqual(
					UnitAIState.Search,
					UnitAICommandPriority.Predict(
						UnitAIState.Search, false, UnitAIState.Attack, false, true, UnitAIState.Attack));
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void E3_Idle_ImmediateThreat_StaysIdle()
		{
			UnitAIController controller = Create();
			try
			{
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.AreEqual(UnitAIPriorityDecision.HoldState, controller.LastPriorityEvaluation.Decision);
				Assert.AreEqual(UnitAIPriorityReason.EmergencyLocal, controller.LastPriorityEvaluation.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region F New command during movement
		[Test]
		public void F1_AttackMove_Retreat_CancelsOldNavigation()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				Vector3 attack = P(14f);
				Vector3 retreat = P(-8f);
				AssertAccepted(controller, TacticalCommand.Attack(attack), UnitAIState.Attack);
				Assert.AreEqual(UnitNavigationReason.Attack, recorder.Reason);
				Assert.IsTrue(recorder.HasMoveIntent);
				int stops = recorder.StopCount;
				AssertAccepted(controller, TacticalCommand.Retreat(retreat), UnitAIState.Retreat);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(retreat, controller.CurrentContext.Destination);
				Assert.AreEqual(UnitNavigationReason.Retreat, recorder.Reason);
				Assert.AreEqual(retreat, recorder.LastDestination);
				Assert.Greater(recorder.StopCount, stops);
				Assert.IsTrue(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region G New command during Search
		[Test]
		public void G1_SearchA_AttackB_DiscardsSearchAndReturnState()
		{
			UnitAIController controller = Create();
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
				AssertAccepted(controller, TacticalCommand.Search(P(1f, 6f)), UnitAIState.Search);
				AssertAccepted(controller, TacticalCommand.Attack(P(15f, 1f)), UnitAIState.Attack);
				Assert.AreEqual(UnitAISearchCompletionReason.NewOrder, controller.LastSearchCompletionReason);
				Assert.IsNull(controller.SearchSession);
				Assert.AreEqual(P(15f, 1f), controller.CurrentContext.Destination);
				Assert.AreEqual(Vector3.zero, controller.CurrentContext.SearchPosition);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentContext.ResumeState);
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region H Same command context replacement
		[Test]
		public void H1_AttackBuildingA_ToAttackBuildingB()
		{
			UnitAIController controller = Create();
			GameObject a = new GameObject("CMDPRI_A");
			GameObject b = new GameObject("CMDPRI_B");
			try
			{
				AssertAccepted(controller, TacticalCommand.Attack(P(4f), a.transform), UnitAIState.Attack);
				Assert.AreSame(a.transform, controller.CurrentContext.TargetEntity);
				AssertAccepted(controller, TacticalCommand.Attack(P(9f), b.transform), UnitAIState.Attack);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(P(9f), controller.CurrentContext.Destination);
				Assert.AreSame(b.transform, controller.CurrentContext.TargetEntity);
				Assert.AreNotSame(a.transform, controller.CurrentContext.TargetEntity);
			}
			finally
			{
				Object.DestroyImmediate(a);
				Object.DestroyImmediate(b);
				Destroy(controller);
			}
		}
		#endregion

		#region I Priority determinism
		[Test]
		public void I1_Attack_Retreat_ImmediateThreat_AlwaysRetreat()
		{
			UnitAIState first = UnitAICommandPriority.Predict(
				UnitAIState.Attack, true, UnitAIState.Retreat, false, true, UnitAIState.Attack);
			UnitAIState second = UnitAICommandPriority.Predict(
				UnitAIState.Attack, true, UnitAIState.Retreat, false, true, UnitAIState.Attack);
			Assert.AreEqual(UnitAIState.Retreat, first);
			Assert.AreEqual(first, second);
			Assert.AreNotEqual(UnitAIState.Flee, first);

			UnitAIController controller = Create();
			try
			{
				for (int i = 0; i < 3; i++)
				{
					controller.IssueCommand(TacticalCommand.Cancel());
					AssertAccepted(controller, TacticalCommand.Attack(P(8f)), UnitAIState.Attack);
					controller.ImmediateThreat = true;
					AssertAccepted(controller, TacticalCommand.Retreat(P(-4f)), UnitAIState.Retreat);
					Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
					controller.Tick(0.05f);
					Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
					controller.ImmediateThreat = false;
				}
			}
			finally
			{
				Destroy(controller);
			}
		}
		#endregion

		#region Helpers
		private static Vector3 P(float _x, float _z = 0f)
		{
			return new Vector3(_x, 0f, _z);
		}

		private static void AssertAccepted(UnitAIController _controller, TacticalCommand _command, UnitAIState _state)
		{
			TacticalCommandResult result = _controller.IssueCommand(in _command);
			Assert.IsTrue(result.Accepted, _command.Type + " -> " + _state);
			Assert.AreEqual(TacticalCommandRejectReason.None, result.Reason);
			Assert.AreEqual(_state, _controller.CurrentState);
		}

		private static UnitAIController Create()
		{
			return new GameObject("AI11_Priority").AddComponent<UnitAIController>();
		}

		private static (UnitAIController controller, UnitMoveCommandRecorder recorder) CreateWithRecorder()
		{
			var go = new GameObject("AI11_PriorityNav");
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			return (controller, recorder);
		}

		private static void Destroy(UnitAIController _controller)
		{
			if (_controller != null)
				Object.DestroyImmediate(_controller.gameObject);
		}
		#endregion
	}
}
