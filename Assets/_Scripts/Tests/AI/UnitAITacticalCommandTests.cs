using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class UnitAITacticalCommandTests
	{
		[Test]
		public void SetAttack_FromIdle_EntersAttackWithDestination()
		{
			UnitAIController controller = Create();
			try
			{
				IUnitTacticalCommand orders = controller;
				Vector3 dest = new Vector3(12f, 0f, 3f);
				Assert.IsTrue(orders.SetAttack(dest));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(dest, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetAttack_SameState_ReissuesNewDestination()
		{
			UnitAIController controller = Create();
			try
			{
				Assert.IsTrue(controller.SetAttack(new Vector3(4f, 0f, 0f)));
				Vector3 next = new Vector3(9f, 0f, 2f);
				Assert.IsTrue(controller.SetAttack(next));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(next, controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetDefense_FromIdle_StoresAnchor()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 anchor = new Vector3(5f, 0f, 1f);
				Assert.IsTrue(controller.SetDefense(anchor));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(anchor, controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(anchor, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetRetreat_FromIdle_GoesViaDefenseThenRetreat()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 dest = new Vector3(-8f, 0f, 0f);
				Assert.IsTrue(controller.SetRetreat(dest));
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(dest, controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetDefense_AfterAttack_ClearsPreviousAttack()
		{
			UnitAIController controller = Create();
			try
			{
				Assert.IsTrue(controller.SetAttack(new Vector3(12f, 0f, 0f)));
				Vector3 anchor = new Vector3(1f, 0f, 2f);
				Assert.IsTrue(controller.SetDefense(anchor));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(anchor, controller.CurrentContext.AnchorPosition);
				Assert.AreEqual(anchor, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetAttack_FromRetreat_BouncesThroughIdle()
		{
			UnitAIController controller = Create();
			try
			{
				Assert.IsTrue(controller.SetRetreat(new Vector3(-4f, 0f, 0f)));
				Vector3 dest = new Vector3(6f, 0f, 1f);
				Assert.IsTrue(controller.SetAttack(dest));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(dest, controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetAttack_AfterSearch_DoesNotKeepOldDestination()
		{
			UnitAIController controller = Create();
			try
			{
				Assert.IsTrue(controller.SetAttack(new Vector3(12f, 0f, 0f)));
				Assert.IsTrue(controller.SetSearch(new Vector3(3f, 0f, 4f)));
				Vector3 dest = new Vector3(-5f, 0f, 2f);
				Assert.IsTrue(controller.SetAttack(dest));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(dest, controller.CurrentContext.Destination);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetSearchPoint_FromIdle_SnapshotsPositionAndResumesAttack()
		{
			UnitAIController controller = Create();
			try
			{
				Vector3 search = new Vector3(7f, 0f, 4f);
				Assert.IsTrue(controller.SetSearch(search));
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(search, controller.CurrentContext.SearchPosition);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentContext.ResumeState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetSearch_FromDefense_UsesLastKnown()
		{
			UnitAIController controller = Create();
			Transform target = new GameObject("Cmd_Lost").transform;
			Vector3 lastKnown = new Vector3(4f, 0f, 7f);
			try
			{
				Assert.IsTrue(controller.SetDefense(new Vector3(1f, 0f, 0f)));
				controller.SetPerceptionFrame(Frame(RecentlyLostHostile(target, lastKnown)));
				Assert.IsTrue(controller.SetSearch());
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(lastKnown, controller.CurrentContext.SearchPosition);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentContext.ResumeState);
			}
			finally
			{
				if (target != null)
					UnityEngine.Object.DestroyImmediate(target.gameObject);
				Destroy(controller);
			}
		}

		[Test]
		public void SetSearch_WithoutMemory_Fails()
		{
			UnitAIController controller = Create();
			try
			{
				Assert.IsTrue(controller.SetDefense(Vector3.zero));
				Assert.IsFalse(controller.SetSearch());
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void SetFlee_FromAttack_WalksThenCanIdle()
		{
			UnitAIController controller = Create();
			try
			{
				Assert.IsTrue(controller.SetAttack(new Vector3(3f, 0f, 0f)));
				Vector3 flee = new Vector3(-10f, 0f, 0f);
				Assert.IsTrue(controller.SetFlee(flee));
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(flee, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.SetIdle());
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
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

		private static UnitAIController Create()
		{
			var go = new GameObject("AI1_TacticalCommand");
			return go.AddComponent<UnitAIController>();
		}

		private static void Destroy(UnitAIController _controller)
		{
			if (_controller != null)
				UnityEngine.Object.DestroyImmediate(_controller.gameObject);
		}

		private static AIPerceptionFrame Frame(params AIContactKnowledge[] _contacts)
		{
			return new AIPerceptionFrame(
				_contacts,
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.None);
		}

		private static AIContactKnowledge RecentlyLostHostile(Transform _target, Vector3 _lastKnown)
		{
			return new AIContactKnowledge(
				_target,
				DetectionState.Detected,
				ObservationState.RecentlyLost,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				_lastKnown,
				_lastKnown,
				12.5f,
				0.95f,
				false,
				true,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				false,
				true);
		}
	}
}
