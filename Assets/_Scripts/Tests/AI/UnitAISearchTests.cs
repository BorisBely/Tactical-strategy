using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class UnitAISearchTests
	{
		[Test]
		public void Defense_UsefulLostHostile_SearchesLastKnown()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI110_Lost").transform;
			Vector3 lastKnown = new Vector3(4f, 0f, 7f);
			Vector3 anchor = new Vector3(1f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(anchor))));
				controller.ClearTrace();
				controller.SetPerceptionFrame(Frame(RecentlyLostHostile(target, lastKnown)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(lastKnown, controller.CurrentContext.SearchPosition);
				Assert.AreEqual(anchor, controller.CurrentContext.SearchOrigin);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentContext.ResumeState);
				Assert.AreEqual("Exit:Defense", controller.Trace[0]);
				Assert.AreEqual("Enter:Search", controller.Trace[1]);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Attack_UsefulLostHostile_SearchesLastKnown()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI110_AttackLost").transform;
			Vector3 lastKnown = new Vector3(9f, 0f, 3f);
			Vector3 dest = new Vector3(2f, 0f, 2f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(dest))));
				controller.SetPerceptionFrame(Frame(RecentlyLostHostile(target, lastKnown)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(lastKnown, controller.CurrentContext.SearchPosition);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentContext.ResumeState);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Idle_UsefulLostHostile_DoesNotSearch()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI110_IdleLost").transform;
			try
			{
				controller.SetPerceptionFrame(Frame(RecentlyLostHostile(target, Vector3.right)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Idle, controller.CurrentState);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Search_FoundHostile_ResumesDefense()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI110_Found").transform;
			Vector3 lastKnown = new Vector3(5f, 0f, 1f);
			Vector3 anchor = new Vector3(2f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(anchor))));
				controller.SetPerceptionFrame(Frame(RecentlyLostHostile(target, lastKnown)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);

				controller.ClearTrace();
				controller.SetPerceptionFrame(Frame(VisibleHostile(target)));
				controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreEqual(anchor, controller.CurrentContext.AnchorPosition);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void Search_StaleMemory_ResumesDefense_DoesNotMutateContact()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("AI110_Memory").transform;
			Vector3 lastKnown = new Vector3(6f, 0f, 2f);
			try
			{
				var registry = new FakeRegistry();
				PerceivedContact contact = PerceivedLostHostile(target, lastKnown, 0.95f);
				registry.Add(contact);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.BindPerception(registry);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);

				float conf = contact.LastSeenConfidence;
				Vector3 known = contact.LastKnownPosition;
				float seenTime = contact.LastSeenTime;
				ObservationState obs = contact.ObservationState;
				for (int i = 0; i < 8; i++)
					controller.Tick(0.05f);

				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(conf, contact.LastSeenConfidence);
				Assert.AreEqual(known, contact.LastKnownPosition);
				Assert.AreEqual(seenTime, contact.LastSeenTime);
				Assert.AreEqual(obs, contact.ObservationState);

				contact.LastSeenConfidence = 0.1f;
				controller.ClearPerceptionOverride();
				controller.BindPerception(registry);
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(0.1f, contact.LastSeenConfidence, 0.0001f);
				Assert.AreEqual(lastKnown, contact.LastKnownPosition);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void SearchPicksHighestUsefulConfidence()
		{
			Transform low = new GameObject("AI110_LowConf").transform;
			Transform high = new GameObject("AI110_HighConf").transform;
			try
			{
				AIPerceptionFrame frame = Frame(
					RecentlyLostHostile(low, Vector3.left, 0.4f),
					RecentlyLostHostile(high, Vector3.right, 0.9f));
				Assert.IsTrue(UnitAISearchDecision.TryGetSearchContact(frame, out AIContactKnowledge pick));
				Assert.AreSame(high, pick.Target);
				Assert.AreEqual(Vector3.right, pick.LastKnownPosition);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(low.gameObject);
				UnityEngine.Object.DestroyImmediate(high.gameObject);
			}
		}

		private static UnitAIController CreateController()
		{
			var go = new GameObject("AI110_Controller");
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
				Vector3.zero);
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
				true,
				false,
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
