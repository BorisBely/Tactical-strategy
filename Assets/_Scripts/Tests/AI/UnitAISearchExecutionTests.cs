using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class UnitAISearchExecutionTests
	{
		[Test]
		public void PlanarDistance_IgnoresHeight()
		{
			float distance = UnitSearchNavigationMath.PlanarDistance(
				new Vector3(0f, 8f, 0f),
				new Vector3(3f, -2f, 4f));
			Assert.AreEqual(5f, distance, 0.0001f);
			Assert.IsTrue(UnitSearchNavigationMath.IsInsideSearchArea(
				Vector3.zero,
				new Vector3(15f, 40f, 0f),
				15f));
			Assert.IsFalse(UnitSearchNavigationMath.IsInsideSearchArea(
				Vector3.zero,
				new Vector3(15.1f, 0f, 0f),
				15f));
		}

		[Test]
		public void Enter_IssuesOneWalkToSnapshottedSearchPosition()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			Vector3 lastKnown = new Vector3(20f, 0f, 0f);
			try
			{
				StartSearch(controller, target, lastKnown);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(lastKnown, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Search, recorder.Reason);
				Assert.IsTrue(controller.SearchNavigationIssued);
				Assert.IsFalse(controller.SearchAreaReached);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);

				controller.Tick(0.05f);
				controller.Tick(0.05f);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Tick_DoesNotFollowUpdatedLastKnown()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			Vector3 firstKnown = new Vector3(18f, 0f, 2f);
			Vector3 laterKnown = new Vector3(40f, 0f, 9f);
			try
			{
				StartSearch(controller, target, firstKnown);
				Assert.AreEqual(firstKnown, controller.CurrentContext.SearchPosition);

				controller.SetPerceptionFrame(Frame(RecentlyLostHostile(target, laterKnown)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(firstKnown, controller.CurrentContext.SearchPosition);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(firstKnown, recorder.LastDestination);
				Assert.AreEqual(laterKnown, LiveLastKnown(controller));
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void AlreadyInsideRadius_DoesNotIssueMove()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			Vector3 lastKnown = new Vector3(5f, 0f, 0f);
			try
			{
				StartSearch(controller, target, lastKnown);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsTrue(controller.SearchAreaReached);
				Assert.IsFalse(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void InsideRadius_StopsAndStaysInSearch()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			Vector3 lastKnown = new Vector3(20f, 0f, 0f);
			try
			{
				StartSearch(controller, target, lastKnown);
				Assert.AreEqual(1, recorder.MoveCount);

				controller.transform.position = new Vector3(8f, 0f, 0f);
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.GreaterOrEqual(recorder.StopCount, 1);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.IsTrue(controller.SearchAreaReached);
				Assert.AreEqual(CombatIntent.Hold, controller.CurrentCombatIntent);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Found_StopsAndResumesDefenseEngage()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			Vector3 lastKnown = new Vector3(22f, 0f, 0f);
			try
			{
				StartSearch(controller, target, lastKnown);
				int stopsBefore = recorder.StopCount;

				controller.SetPerceptionFrame(Frame(VisibleHostile(target)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreEqual(CombatIntent.Engage, controller.CurrentCombatIntent);
				Assert.Greater(recorder.StopCount, stopsBefore);
				Assert.IsFalse(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void StaleMemory_StopsAndResumesWithoutMutatingContact()
		{
			var go = new GameObject("AISearchExec_Stale");
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			Transform target = new GameObject("AISearchExec_StaleTarget").transform;
			Vector3 lastKnown = new Vector3(19f, 0f, 1f);
			try
			{
				var registry = new FakeRegistry();
				PerceivedContact contact = PerceivedLostHostile(target, lastKnown, 0.95f);
				registry.Add(contact);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.BindPerception(registry);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(1, recorder.MoveCount);

				float conf = contact.LastSeenConfidence;
				contact.LastSeenConfidence = 0.1f;
				controller.ClearPerceptionOverride();
				controller.BindPerception(registry);
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(0.1f, contact.LastSeenConfidence, 0.0001f);
				Assert.AreEqual(lastKnown, contact.LastKnownPosition);
				Assert.AreEqual(conf, 0.95f, 0.0001f);
				Assert.IsFalse(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void ExternalRetreat_CancelsSearchAndWalksRetreat()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			Vector3 retreat = new Vector3(-18f, 0f, 0f);
			try
			{
				StartSearch(controller, target, new Vector3(21f, 0f, 0f));
				int stopsBefore = recorder.StopCount;
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreat))));
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.Greater(recorder.StopCount, stopsBefore);
				Assert.AreEqual(2, recorder.MoveCount);
				Assert.AreEqual(retreat, recorder.LastDestination);
				Assert.AreEqual(UnitNavigationReason.Retreat, recorder.Reason);
				Assert.IsTrue(recorder.HasMoveIntent);
				Assert.AreEqual(UnitAIAction.None, controller.CurrentAction);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void NavFail_WithCanIssue_Resumes()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			try
			{
				recorder.NextMoveFails = true;
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(recorder.HasMoveIntent);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void NoMoveCommand_StaysInSearch_DoesNotAutoResume()
		{
			UnitAIController controller = new GameObject("AISearchExec_NoNav").AddComponent<UnitAIController>();
			Transform target = new GameObject("AISearchExec_NoNavTarget").transform;
			try
			{
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				controller.Tick(0.05f);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.IsFalse(controller.SearchNavigationIssued);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void CanIssueFalse_StaysInSearch()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			try
			{
				recorder.CanIssue = false;
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(0, recorder.MoveCount);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void NoUsefulMemory_DoesNotStartSearch()
		{
			(UnitAIController controller, _, Transform target) = CreateWithRecorder();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.SetPerceptionFrame(Frame(RecentlyLostHostile(target, Vector3.right * 20f, 0.1f)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		private static (UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) CreateWithRecorder()
		{
			var go = new GameObject("AISearchExec");
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			Transform target = new GameObject("AISearchExec_Target").transform;
			return (controller, recorder, target);
		}

		private static void StartSearch(UnitAIController _controller, Transform _target, Vector3 _lastKnown)
		{
			Assert.IsTrue(_controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
			_controller.SetPerceptionFrame(Frame(RecentlyLostHostile(_target, _lastKnown)));
			_controller.Tick(0.05f);
		}

		private static Vector3 LiveLastKnown(UnitAIController _controller)
		{
			Assert.IsTrue(_controller.TryGetLiveHostileLastKnown(out Vector3 position, out _));
			return position;
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
			return Knowledge(
				_target,
				ObservationState.Observed,
				1f,
				true,
				false,
				true,
				false,
				_target.position);
		}

		private static AIContactKnowledge RecentlyLostHostile(
			Transform _target,
			Vector3 _lastKnown,
			float _confidence = 0.95f)
		{
			return Knowledge(
				_target,
				ObservationState.RecentlyLost,
				_confidence,
				false,
				true,
				_confidence > 0.25f,
				_confidence <= 0.25f,
				_lastKnown);
		}

		private static AIContactKnowledge Knowledge(
			Transform _target,
			ObservationState _observation,
			float _lastSeenConfidence,
			bool _visibleNow,
			bool _recentlyLost,
			bool _useful,
			bool _stale,
			Vector3 _lastKnown)
		{
			return new AIContactKnowledge(
				_target,
				DetectionState.Detected,
				_observation,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				_lastKnown,
				_lastKnown,
				12.5f,
				_lastSeenConfidence,
				_visibleNow,
				_recentlyLost,
				_observation == ObservationState.Lost,
				_useful,
				_stale,
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

		private static PerceivedContact PerceivedLostHostile(Transform _target, Vector3 _lastKnown, float _confidence)
		{
			return new PerceivedContact
			{
				Target = _target,
				State = DetectionState.Detected,
				DetectionProgress = 1f,
				ObservationState = ObservationState.RecentlyLost,
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
