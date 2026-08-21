using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class UnitAITacticalNavigationTests
	{
		[Test]
		public void Attack_WithDestination_IssuesOneWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(20f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(destination))));
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(destination, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Attack, recorder.Reason);
				Assert.IsTrue(controller.TacticalNavigationIssued);
				Assert.IsFalse(controller.TacticalDestinationReached);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);

				controller.Tick(0.05f);
				controller.Tick(0.05f);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Defense_WithAnchor_IssuesOneWalkAndStaysDefense()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(14f, 0f, 4f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(destination))));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(destination, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Defense, recorder.Reason);
				Assert.IsTrue(controller.TacticalNavigationIssued);
				Assert.IsFalse(controller.TacticalDestinationReached);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);

				controller.Tick(0.05f);
				controller.Tick(0.05f);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Defense_Reached_HardStopsAndStaysDefense()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(11f, 0f, 2f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(destination))));
				Assert.AreEqual(1, recorder.MoveCount);

				controller.transform.position = destination + new Vector3(0.4f, 0f, 0f);
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.IsTrue(controller.TacticalDestinationReached);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Attack_WhileMoving_RemainsAttack()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(new Vector3(22f, 0f, 4f)))));
				controller.transform.position = new Vector3(6f, 0f, 1f);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.IsTrue(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Attack_Reached_HardStopsAndStaysAttack()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(18f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(destination))));
				Assert.AreEqual(1, recorder.MoveCount);

				controller.transform.position = destination + new Vector3(0.4f, 0f, 0f);
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.IsTrue(controller.TacticalDestinationReached);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Attack_WithoutDestination_DoesNotWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(
					new UnitAIStateContext { AttackDirection = Vector3.forward })));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
				Assert.IsFalse(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Attack_VisibleHostile_WalksAndEngages()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Transform target = new GameObject("AITacticalNav_AttackHostile").transform;
			Vector3 destination = new Vector3(24f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(destination))));
				controller.SetPerceptionFrame(Frame(VisibleHostile(target)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreEqual(CombatIntent.Engage, controller.CurrentCombatIntent);
				Assert.AreSame(target, controller.CurrentEngageTarget);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(destination, recorder.LastDestination);
				Assert.IsTrue(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Retreat_WithDestination_IssuesWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(-16f, 0f, 2f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(destination))));
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(destination, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Retreat, recorder.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Retreat_Reached_HardStopsAndStaysRetreat()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(-12f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(destination))));
				controller.transform.position = destination;
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.AreEqual(UnitAIAction.None, controller.CurrentAction);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Retreat_WithoutDestination_DoesNotWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(default)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(0, recorder.MoveCount);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Flee_WithDestination_IssuesWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(0f, 0f, -20f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(
					UnitAIStateContext.ForFlee(Vector3.back, destination))));
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(destination, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Flee, recorder.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Flee_Reached_HardStopsAndGoesIdle()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(14f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(
					UnitAIStateContext.ForFlee(Vector3.right, destination))));
				controller.transform.position = destination + new Vector3(0.2f, 3f, 0f);
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.AreEqual(UnitAIAction.None, controller.CurrentAction);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Flee_WithoutDestination_DoesNotWalkAndStaysFlee()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(UnitAIStateContext.ForFlee(Vector3.left))));
				controller.Tick(0.05f);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.CurrentContext.HasDestination);
				Assert.IsFalse(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void AttackToRetreat_CancelsAAndWalksB()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 attackDest = new Vector3(20f, 0f, 0f);
			Vector3 retreatDest = new Vector3(-18f, 0f, 3f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(attackDest))));
				Assert.AreEqual(attackDest, recorder.LastDestination);
				int stopsBefore = recorder.StopCount;

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreatDest))));
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.Greater(recorder.StopCount, stopsBefore);
				Assert.AreEqual(2, recorder.MoveCount);
				Assert.AreEqual(retreatDest, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Retreat, recorder.Reason);
				Assert.IsTrue(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void AttackToFlee_CancelsAttackWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 fleeDest = new Vector3(0f, 0f, -16f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(new Vector3(19f, 0f, 0f)))));
				int stopsBefore = recorder.StopCount;
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(
					UnitAIStateContext.ForFlee(Vector3.back, fleeDest))));
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.Greater(recorder.StopCount, stopsBefore);
				Assert.AreEqual(fleeDest, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Flee, recorder.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void DefenseToRetreat_IssuesRetreatWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 retreatDest = new Vector3(-15f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreatDest))));
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(UnitNavigationReason.Retreat, recorder.Reason);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void DefenseToFlee_IssuesFleeWalk()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 fleeDest = new Vector3(12f, 0f, -12f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(
					UnitAIStateContext.ForFlee(Vector3.right, fleeDest))));
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(UnitNavigationReason.Flee, recorder.Reason);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Navigation_DoesNotMutateCombatOrMemory()
		{
			var go = new GameObject("AITacticalNav_Isolation");
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			Transform target = new GameObject("AITacticalNav_IsolationTarget").transform;
			Vector3 lastKnown = new Vector3(11f, 0f, 2f);
			try
			{
				var registry = new FakeRegistry();
				PerceivedContact contact = PerceivedHostile(target, lastKnown, 0.1f);
				registry.Add(contact);
				controller.BindPerception(registry);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(new Vector3(21f, 0f, 0f)))));
				controller.Tick(0.05f);
				controller.transform.position = new Vector3(4f, 0f, 0f);
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(PerceivedIdentity.Hostile, contact.Identity);
				Assert.AreEqual(lastKnown, contact.LastKnownPosition);
				Assert.AreEqual(0.1f, contact.LastSeenConfidence, 0.0001f);
				Assert.IsNull(controller.CurrentEngageTarget);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Attack_NoMoveCommand_StaysAttack()
		{
			UnitAIController controller = new GameObject("AITacticalNav_NoNav").AddComponent<UnitAIController>();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(new Vector3(20f, 0f, 0f)))));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Attack_CanIssueFalse_RetriesWhenEnabled()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				recorder.CanIssue = false;
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(new Vector3(17f, 0f, 0f)))));
				Assert.AreEqual(0, recorder.MoveCount);
				recorder.CanIssue = true;
				controller.Tick(0.05f);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
			}
			finally
			{
				Destroy(controller);
			}
		}

		[Test]
		public void Attack_NavFail_StaysAttack()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				recorder.NextMoveFails = true;
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(new Vector3(16f, 0f, 0f)))));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(0, recorder.MoveCount);
			}
			finally
			{
				Destroy(controller);
			}
		}

		private static (UnitAIController controller, UnitMoveCommandRecorder recorder) CreateWithRecorder()
		{
			var go = new GameObject("AITacticalNav");
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			return (controller, recorder);
		}

		private static void Destroy(UnitAIController _controller, Transform _target = null)
		{
			if (_controller != null)
				UnityEngine.Object.DestroyImmediate(_controller.gameObject);
			if (_target != null)
				UnityEngine.Object.DestroyImmediate(_target.gameObject);
		}

		private static UnitAIStateContext DefenseCtx(Vector3 _anchor)
		{
			return UnitAIStateContext.ForDefense(_anchor, _anchor, 8f, Vector3.forward);
		}

		private static UnitAIStateContext AttackCtx(Vector3 _destination)
		{
			return UnitAIStateContext.ForAttack(_destination, Vector3.forward);
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

		private static AIContactKnowledge VisibleHostile(Transform _target)
		{
			return new AIContactKnowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				_target.position,
				_target.position,
				12.5f,
				1f,
				true,
				false,
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

		private static PerceivedContact PerceivedHostile(Transform _target, Vector3 _lastKnown, float _confidence)
		{
			return new PerceivedContact
			{
				Target = _target,
				State = DetectionState.Detected,
				DetectionProgress = 1f,
				ObservationState = ObservationState.Lost,
				Identity = PerceivedIdentity.Hostile,
				IdentityConfidence = 1f,
				Relationship = PerceivedRelationship.Hostile,
				Threat = ThreatLevel.High,
				LastSeenConfidence = _confidence,
				LastKnownPosition = _lastKnown,
				LastSeenPosition = _lastKnown,
				LastSeenTime = 12.5f,
				CurrentEvaluation = new DetectionEvaluation { VisibilityQuality = 1f }
			};
		}

		private sealed class FakeRegistry : IPerceivedContactRegistry
		{
			private readonly System.Collections.Generic.Dictionary<Transform, PerceivedContact> m_Contacts =
				new System.Collections.Generic.Dictionary<Transform, PerceivedContact>();

			public System.Collections.Generic.IReadOnlyDictionary<Transform, PerceivedContact> Contacts => m_Contacts;

			public event Action ContactsChanged
			{
				add { }
				remove { }
			}

			public void Add(PerceivedContact _contact)
			{
				m_Contacts[_contact.Target] = _contact;
			}

			public bool TryGetContact(Transform _target, out PerceivedContact _contact)
			{
				return m_Contacts.TryGetValue(_target, out _contact);
			}
		}
	}
}
