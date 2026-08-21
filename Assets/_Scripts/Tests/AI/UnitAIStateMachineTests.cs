using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class UnitAIStateMachineTests
	{
		[Test]
		public void InitialState_IsIdle()
		{
			UnitAIController controller = CreateController();
			try
			{
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.AreEqual("Enter:Idle", controller.Trace[0]);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void LegalTransitions_Succeed()
		{
			UnitAIController controller = CreateController();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Idle()));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(Vector3.forward))));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(Vector3.right))));
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.up))));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Search(SearchCtx(Vector3.one))));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(Vector3.back))));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Search(SearchCtx(Vector3.one))));
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(Vector3.right))));
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void IllegalTransition_IsRejected()
		{
			UnitAIController controller = CreateController();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(UnitAIStateContext.ForFlee(Vector3.left))));
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				UnitAIStateContext before = controller.CurrentContext;

				Assert.IsFalse(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.one))));
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(before.EscapeDirection, controller.CurrentContext.EscapeDirection);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void SameState_DoesNotDuplicateEnterExit()
		{
			UnitAIController controller = CreateController();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.ClearTrace();
				int enter = Count(controller.Trace, "Enter:");
				int exit = Count(controller.Trace, "Exit:");

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.one))));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(enter, Count(controller.Trace, "Enter:"));
				Assert.AreEqual(exit, Count(controller.Trace, "Exit:"));
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void Transition_ExitsOldThenEntersNew()
		{
			UnitAIController controller = CreateController();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.ClearTrace();
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(Vector3.forward))));

				Assert.AreEqual(2, controller.Trace.Count);
				Assert.AreEqual("Exit:Defense", controller.Trace[0]);
				Assert.AreEqual("Enter:Attack", controller.Trace[1]);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void Context_StoresPlaceForEachState()
		{
			UnitAIController controller = CreateController();
			try
			{
				Vector3 defensePos = new Vector3(10f, 0f, 0f);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(defensePos))));
				Assert.AreEqual(defensePos, controller.CurrentContext.AnchorPosition);

				Vector3 attackDest = new Vector3(20f, 0f, 5f);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(attackDest))));
				Assert.AreEqual(attackDest, controller.CurrentContext.Destination);

				Vector3 origin = new Vector3(1f, 0f, 1f);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Idle()));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Search(SearchCtx(origin))));
				Assert.AreEqual(origin, controller.CurrentContext.SearchOrigin);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));

				Vector3 retreat = new Vector3(-8f, 0f, 0f);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreat))));
				Assert.AreEqual(retreat, controller.CurrentContext.Destination);

				Vector3 fleeDir = Vector3.back;
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(UnitAIStateContext.ForFlee(fleeDir, retreat))));
				Assert.AreEqual(fleeDir, controller.CurrentContext.EscapeDirection);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void AttackToDefense_ReplacesContext()
		{
			UnitAIController controller = CreateController();
			try
			{
				Vector3 p1 = new Vector3(4f, 0f, 0f);
				Vector3 p2 = new Vector3(9f, 0f, 2f);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(p1))));
				Assert.AreEqual(p1, controller.CurrentContext.Destination);

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(p2))));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(p2, controller.CurrentContext.AnchorPosition);
				Assert.AreNotEqual(p1, controller.CurrentContext.Destination);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void SameState_TrySetContext_UpdatesWithoutEnterExit()
		{
			UnitAIController controller = CreateController();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.ClearTrace();
				Assert.IsTrue(controller.TrySetContext(DefenseCtx(Vector3.right * 3f)));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(new Vector3(3f, 0f, 0f), controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void Tick_AdvancesStateTime()
		{
			UnitAIController controller = CreateController();
			try
			{
				controller.Tick(0.25f);
				Assert.Greater(controller.StateTime, 0.24f);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.AreEqual(0f, controller.StateTime);
				controller.Tick(0.1f);
				Assert.Greater(controller.StateTime, 0.09f);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		private static UnitAIController CreateController()
		{
			var go = new GameObject("AI1_StateMachine");
			return go.AddComponent<UnitAIController>();
		}

		private static UnitAIStateContext DefenseCtx(Vector3 _anchor)
		{
			return UnitAIStateContext.ForDefense(_anchor, _anchor, 8f, Vector3.forward);
		}

		private static UnitAIStateContext AttackCtx(Vector3 _destination)
		{
			return UnitAIStateContext.ForAttack(_destination, Vector3.forward);
		}

		private static UnitAIStateContext SearchCtx(Vector3 _origin)
		{
			return UnitAIStateContext.ForSearch(_origin, _origin + Vector3.forward * 4f, 12f);
		}

		private static int Count(IReadOnlyList<string> _trace, string _prefix)
		{
			int n = 0;
			for (int i = 0; i < _trace.Count; i++)
			{
				if (_trace[i].StartsWith(_prefix))
					n++;
			}

			return n;
		}
	}
}
