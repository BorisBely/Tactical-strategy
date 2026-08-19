using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class UnitAIPerceptionActionTests
	{
		[Test]
		public void Defense_HostileVisible_StaysDefenseAndEngages()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_Hostile").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.ClearTrace();
				controller.SetPerceptionFrame(Frame(VisibleHostile(target, ThreatLevel.High)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreSame(target, controller.CurrentEngageTarget);
				Assert.IsTrue(controller.HasHostileVisible);
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Attack_HostileVisible_StaysAttackAndEngages()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_AttackHostile").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(Vector3.forward))));
				controller.ClearTrace();
				controller.SetPerceptionFrame(Frame(VisibleHostile(target, ThreatLevel.Medium)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreSame(target, controller.CurrentEngageTarget);
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Idle_HostileVisible_StaysIdleAndDoesNotEngage()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_IdleHostile").transform;
			try
			{
				controller.ClearTrace();
				controller.SetPerceptionFrame(Frame(VisibleHostile(target, ThreatLevel.High)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.None, controller.CurrentAction);
				Assert.IsNull(controller.CurrentEngageTarget);
				Assert.IsTrue(controller.HasHostileVisible);
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Defense_UnknownVisible_HoldsAndIsNotHostile()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_Unknown").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.SetPerceptionFrame(Frame(VisibleUnknown(target)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Hold, controller.CurrentAction);
				Assert.IsFalse(controller.HasHostileVisible);
				Assert.IsNull(controller.CurrentEngageTarget);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Defense_FriendlyVisible_Holds()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_Friendly").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.SetPerceptionFrame(Frame(VisibleFriendly(target)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Hold, controller.CurrentAction);
				Assert.IsFalse(controller.HasHostileVisible);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Defense_HostileStale_Holds_DoesNotSearch()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_Stale").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.ClearTrace();
				controller.SetPerceptionFrame(Frame(StaleHostile(target)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Hold, controller.CurrentAction);
				Assert.AreNotEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void SearchFound_GoesToAttack_RetreatFleeDoNotAutoEngage()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_SearchFlee").transform;
			try
			{
				AIPerceptionFrame hostile = Frame(VisibleHostile(target, ThreatLevel.High));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Search(SearchCtx(Vector3.zero))));
				controller.SetPerceptionFrame(hostile);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(Vector3.right))));
				controller.SetPerceptionFrame(hostile);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.None, controller.CurrentAction);

				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Flee(UnitAIStateContext.ForFlee(Vector3.left))));
				controller.SetPerceptionFrame(hostile);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Flee, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.None, controller.CurrentAction);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void EngagePicksHighestVisibleHostileThreat()
		{
			Transform low = new GameObject("AI19_Low").transform;
			Transform high = new GameObject("AI19_High").transform;
			try
			{
				AIPerceptionFrame frame = Frame(
					VisibleHostile(low, ThreatLevel.Low),
					VisibleHostile(high, ThreatLevel.High));
				Assert.IsTrue(UnitAIActionResolver.TryGetEngageContact(frame, out AIContactKnowledge pick));
				Assert.AreSame(high, pick.Target);
				Assert.AreEqual(UnitAIAction.Engage, UnitAIActionResolver.Resolve(UnitAIState.Defense, frame));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(low.gameObject);
				UnityEngine.Object.DestroyImmediate(high.gameObject);
			}
		}

		[Test]
		public void RememberedHostile_IsNotEngage()
		{
			Transform remembered = new GameObject("AI19_Remembered").transform;
			try
			{
				AIPerceptionFrame frame = Frame(LostUsefulHostile(remembered));
				Assert.IsFalse(UnitAIActionResolver.HasHostileVisible(frame));
				Assert.AreEqual(UnitAIAction.Hold, UnitAIActionResolver.Resolve(UnitAIState.Defense, frame));
				Assert.AreEqual(UnitAIAction.None, UnitAIActionResolver.Resolve(UnitAIState.Idle, frame));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(remembered.gameObject);
			}
		}

		[Test]
		public void BindRegistry_HostileVisible_DoesNotChangeState()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI19_Registry").transform;
			try
			{
				var registry = new FakeRegistry();
				registry.Add(PerceivedVisibleHostile(target));
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.one))));
				controller.ClearTrace();
				controller.BindPerception(registry);
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreSame(target, controller.CurrentEngageTarget);
				Assert.AreEqual(0, controller.Trace.Count);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void SixStates_EngageIsActionNotState()
		{
			Assert.AreEqual(6, Enum.GetNames(typeof(UnitAIState)).Length);
			Assert.Less(Array.IndexOf(Enum.GetNames(typeof(UnitAIState)), "Engage"), 0);
			Assert.Less(Array.IndexOf(Enum.GetNames(typeof(UnitAIState)), "DefenseEnemyDetected"), 0);
			Assert.IsTrue(Enum.IsDefined(typeof(UnitAIAction), UnitAIAction.Engage));
		}

		private static UnitAIController CreateController()
		{
			var go = new GameObject("AI19_Controller");
			return go.AddComponent<UnitAIController>();
		}

		private static void Destroy(UnitAIController _controller, Transform _target)
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

		private static UnitAIStateContext SearchCtx(Vector3 _origin)
		{
			return UnitAIStateContext.ForSearch(_origin, _origin + Vector3.forward * 4f, 12f);
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

		private static AIContactKnowledge VisibleHostile(Transform _target, ThreatLevel _threat)
		{
			return Knowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				_threat,
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
				true);
		}

		private static AIContactKnowledge VisibleUnknown(Transform _target)
		{
			return Knowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Unknown,
				PerceivedRelationship.Unknown,
				ThreatLevel.None,
				1f,
				true,
				false,
				false,
				true,
				false,
				true,
				false,
				false,
				false,
				false);
		}

		private static AIContactKnowledge VisibleFriendly(Transform _target)
		{
			return Knowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Friendly,
				PerceivedRelationship.Friendly,
				ThreatLevel.None,
				1f,
				true,
				false,
				false,
				true,
				false,
				false,
				true,
				true,
				false,
				false);
		}

		private static AIContactKnowledge StaleHostile(Transform _target)
		{
			return Knowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Lost,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.Low,
				0.1f,
				false,
				false,
				true,
				false,
				true,
				false,
				true,
				false,
				false,
				true);
		}

		private static AIContactKnowledge LostUsefulHostile(Transform _target)
		{
			return Knowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Lost,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				0.6f,
				false,
				false,
				true,
				true,
				false,
				false,
				true,
				false,
				false,
				true);
		}

		private static AIContactKnowledge Knowledge(
			Transform _target,
			DetectionState _detection,
			ObservationState _observation,
			PerceivedIdentity _identity,
			PerceivedRelationship _relationship,
			ThreatLevel _threat,
			float _lastSeenConfidence,
			bool _visibleNow,
			bool _recentlyLost,
			bool _lost,
			bool _useful,
			bool _stale,
			bool _unknown,
			bool _known,
			bool _friendly,
			bool _neutral,
			bool _hostile,
			Vector3 _lastKnown = default)
		{
			return new AIContactKnowledge(
				_target,
				_detection,
				_observation,
				_identity,
				_identity == PerceivedIdentity.Unknown ? 0f : 1f,
				_relationship,
				_threat,
				_lastKnown,
				_lastKnown,
				0f,
				_lastSeenConfidence,
				_visibleNow,
				_recentlyLost,
				_lost,
				_useful,
				_stale,
				_unknown,
				_known,
				_friendly,
				_neutral,
				_hostile,
				_threat == ThreatLevel.None,
				_threat == ThreatLevel.Low,
				_threat == ThreatLevel.Medium,
				_threat == ThreatLevel.High);
		}

		private static PerceivedContact PerceivedVisibleHostile(Transform _target)
		{
			return new PerceivedContact
			{
				Target = _target,
				State = DetectionState.Detected,
				DetectionProgress = 1f,
				ObservationState = ObservationState.Observed,
				Identity = PerceivedIdentity.Hostile,
				IdentityConfidence = 1f,
				Relationship = PerceivedRelationship.Hostile,
				Threat = ThreatLevel.High,
				LastSeenConfidence = 1f,
				LastKnownPosition = Vector3.zero,
				LastSeenPosition = Vector3.zero,
				CurrentEvaluation = new DetectionEvaluation { VisibilityQuality = 1f }
			};
		}

		private sealed class FakeRegistry : IPerceivedContactRegistry
		{
			private readonly Dictionary<Transform, PerceivedContact> m_Contacts =
				new Dictionary<Transform, PerceivedContact>();

			public IReadOnlyDictionary<Transform, PerceivedContact> Contacts => m_Contacts;

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
