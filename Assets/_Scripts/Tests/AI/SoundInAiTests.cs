using System;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	[TestFixture]
	public sealed class SoundInAiTests
	{
		#region Private Fields
		private GameObject m_Observer;
		private GameObject m_Enemy;
		private GameObject m_Ally;
		#endregion

		#region Setup
		[SetUp]
		public void SetUp()
		{
			CombatEventHub.ResetForTests();
			WorldSoundHub.ResetForTests();
			m_Observer = CreateListener("S9_Observer", UnitTeamId.Player);
			m_Enemy = CreateEmitter("S9_Enemy", UnitTeamId.Enemy);
			m_Ally = CreateEmitter("S9_Ally", UnitTeamId.Player);
		}

		[TearDown]
		public void TearDown()
		{
			CombatEventHub.ResetForTests();
			WorldSoundHub.ResetForTests();
			DestroyAll(m_Observer, m_Enemy, m_Ally);
			m_Observer = null;
			m_Enemy = null;
			m_Ally = null;
		}
		#endregion

		#region A Sound Contact
		[Test]
		public void A_Gunshot_CreatesSoundContact_NotVisual()
		{
			Hear(SoundEventType.Gunshot);
			AIPerceptionFrame frame = Snapshot();
			Assert.AreEqual(1, frame.SoundContacts.Count);
			Assert.AreEqual(SoundEventType.Gunshot, frame.SoundContacts[0].Type);
			Assert.AreEqual(0, frame.AllContacts.Count);
			Assert.AreEqual(0, frame.VisibleContacts.Count);
		}

		[Test]
		public void A_Explosion_CreatesSoundContact()
		{
			Hear(SoundEventType.Explosion);
			Assert.AreEqual(SoundEventType.Explosion, Snapshot().SoundContacts[0].Type);
		}

		[Test]
		public void A_Footstep_CreatesSoundContact_NotCombatCue()
		{
			Hear(SoundEventType.Footstep);
			AIPerceptionFrame frame = Snapshot();
			Assert.AreEqual(1, frame.SoundContacts.Count);
			Assert.AreEqual(SoundEventType.Footstep, frame.SoundContacts[0].Type);
			Assert.IsFalse(frame.SoundContacts[0].IsCombatCue);
		}

		[Test]
		public void A_CombatEventImpact_DoesNotCreateSoundContact()
		{
			CombatEventHub.Publish(CombatEvent.Impact(
				m_Enemy.GetComponent<UnitTeam>(),
				m_Enemy.GetComponent<UnitTeam>(),
				null,
				Vector3.zero));
			AIPerceptionFrame frame = Snapshot();
			Assert.AreEqual(0, frame.SoundContacts.Count);
			Assert.AreEqual(1, CombatEventHub.PublishCount);
		}

		[Test]
		public void A_UnknownType_IsIgnoredInSnapshot()
		{
			Processor().ApplySyntheticSound(m_Enemy.transform, Vector3.forward, 0.9f, SoundEventType.Unknown);
			Assert.AreEqual(0, Snapshot().SoundContacts.Count);
		}
		#endregion

		#region B Isolation
		[Test]
		public void B_Sound_DoesNotSetObservedAimOrIdentity()
		{
			Hear(SoundEventType.Gunshot);
			Assert.IsTrue(Processor().TryGetContact(m_Enemy.transform, out PerceivedContact contact));
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(0f, contact.LastSeenConfidence);
			Assert.AreEqual(Vector3.zero, contact.LastKnownPosition);
			Assert.AreNotEqual(Vector3.zero, contact.SoundPosition);

			AIPerceptionFrame frame = Snapshot();
			Assert.AreEqual(0, frame.VisibleContacts.Count);
			Assert.IsFalse(frame.TryGetContact(m_Enemy.transform, out _));
		}
		#endregion

		#region C Decay
		[Test]
		public void C_SoundConfidence_DecaysThenExpires()
		{
			DetectionProcessor processor = Processor();
			processor.SetSimulatedTime(0f);
			processor.ApplySyntheticSound(m_Enemy.transform, Vector3.right * 8f, 1f, SoundEventType.Gunshot);
			Assert.Greater(Snapshot().SoundContacts[0].Confidence, 0.5f);

			processor.Advance(1.5f, 1.5f);
			float mid = Snapshot().SoundContacts[0].Confidence;
			Assert.Greater(mid, 0f);
			Assert.Less(mid, 1f);

			processor.Advance(2f, 3.6f);
			Assert.AreEqual(0, Snapshot().SoundContacts.Count);
		}
		#endregion

		#region D Snapshot
		[Test]
		public void D_SoundExists_VisualChannelUnchanged()
		{
			AIPerceptionFrame empty = Snapshot();
			Assert.AreEqual(0, empty.SoundContacts.Count);
			Assert.AreEqual(0, empty.AllContacts.Count);

			Hear(SoundEventType.Gunshot);
			AIPerceptionFrame frame = Snapshot();
			Assert.AreEqual(1, frame.SoundContacts.Count);
			Assert.AreEqual(0, frame.AllContacts.Count);
			Assert.AreEqual(0, frame.VisibleContacts.Count);
			Assert.AreEqual(0, frame.HostileContacts.Count);
		}

		[Test]
		public void D_ReportChannel_IsNotVision()
		{
			Processor().ApplySyntheticShared(
				m_Enemy.transform,
				new Vector3(5f, 0f, 1f),
				0.7f,
				m_Ally.transform,
				PerceivedIdentity.Hostile);
			AIPerceptionFrame frame = Snapshot();
			Assert.AreEqual(1, frame.ReportContacts.Count);
			Assert.AreEqual(PerceivedIdentity.Hostile, frame.ReportContacts[0].Identity);
			Assert.AreEqual(0, frame.VisibleContacts.Count);
			Assert.AreEqual(0, frame.SoundContacts.Count);
			Assert.IsTrue(Processor().TryGetContact(m_Enemy.transform, out PerceivedContact contact));
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
		}
		#endregion

		#region E Tactical
		[Test]
		public void E1_Defense_HostileGunshot_SearchesSoundPosition()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			Vector3 soundPos = new Vector3(12f, 0f, 4f);
			Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
			ai.SetPerceptionFrame(SoundFrame(soundPos, true, SoundEventType.Gunshot));
			ai.Tick(0.05f);

			Assert.AreEqual(UnitAIState.Search, ai.CurrentState);
			Assert.AreEqual(soundPos, ai.CurrentContext.SearchPosition);
			Assert.AreEqual(UnitAISearchCue.Sound, ai.CurrentContext.SearchCue);
			Assert.AreEqual(UnitAIState.Defense, ai.CurrentContext.ResumeState);
		}

		[Test]
		public void E2_Attack_HostileGunshot_SearchesSoundPosition()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			Vector3 soundPos = new Vector3(9f, 0f, 2f);
			Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Attack(
				UnitAIStateContext.ForAttack(Vector3.forward * 2f, Vector3.forward))));
			ai.SetPerceptionFrame(SoundFrame(soundPos, true, SoundEventType.Gunshot));
			ai.Tick(0.05f);

			Assert.AreEqual(UnitAIState.Search, ai.CurrentState);
			Assert.AreEqual(soundPos, ai.CurrentContext.SearchPosition);
			Assert.AreEqual(UnitAISearchCue.Sound, ai.CurrentContext.SearchCue);
			Assert.AreEqual(UnitAIState.Attack, ai.CurrentContext.ResumeState);
		}

		[Test]
		public void E3_Idle_HostileGunshot_DoesNothing()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			ai.SetPerceptionFrame(SoundFrame(Vector3.right * 6f, true, SoundEventType.Gunshot));
			ai.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Idle, ai.CurrentState);
		}

		[Test]
		public void E4_Search_NewSound_DoesNotRestart()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			Vector3 first = new Vector3(4f, 0f, 0f);
			Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
			ai.SetPerceptionFrame(SoundFrame(first, true, SoundEventType.Gunshot));
			ai.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Search, ai.CurrentState);

			ai.SetPerceptionFrame(SoundFrame(new Vector3(20f, 0f, 0f), true, SoundEventType.Gunshot));
			ai.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Search, ai.CurrentState);
			Assert.AreEqual(first, ai.CurrentContext.SearchPosition);
		}

		[Test]
		public void E5_Attack_VisibleHostile_SoundDoesNotResetAttack()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			Vector3 dest = new Vector3(3f, 0f, 1f);
			Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Attack(
				UnitAIStateContext.ForAttack(dest, Vector3.forward, m_Enemy.transform))));
			ai.SetPerceptionFrame(VisiblePlusSoundFrame(dest));
			ai.Tick(0.05f);

			Assert.AreEqual(UnitAIState.Attack, ai.CurrentState);
			Assert.AreEqual(dest, ai.CurrentContext.Destination);
		}

		[Test]
		public void E_FriendlyGunshot_DoesNotSearch()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
			ai.SetPerceptionFrame(SoundFrame(Vector3.right * 5f, false, SoundEventType.Gunshot));
			ai.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Defense, ai.CurrentState);
		}

		[Test]
		public void E_Footstep_DoesNotSearch()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
			ai.SetPerceptionFrame(SoundFrame(Vector3.right * 5f, true, SoundEventType.Footstep));
			ai.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Defense, ai.CurrentState);
		}

		[Test]
		public void E_SearchFound_VisibleHostile_ResumesDefense()
		{
			UnitAIController ai = m_Observer.GetComponent<UnitAIController>();
			Assert.IsTrue(ai.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.one))));
			ai.SetPerceptionFrame(SoundFrame(Vector3.right * 8f, true, SoundEventType.Gunshot));
			ai.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Search, ai.CurrentState);

			ai.SetPerceptionFrame(VisibleOnlyFrame());
			ai.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Defense, ai.CurrentState);
		}
		#endregion

		#region #8 Regression
		[Test]
		public void R8_Gunshot_PublishesCombatFact_AndSound_Independently()
		{
			DetectionProcessor processor = Processor();
			WorldSoundHub.Register(processor);
			m_Enemy.transform.position = new Vector3(10f, 0f, 0f);

			int soundBefore = WorldSoundHub.LastPublishDeliveryCount;
			CombatEventHub.Publish(CombatEvent.Gunshot(
				m_Enemy.GetComponent<UnitTeam>(),
				m_Enemy.GetComponent<UnitTeam>(),
				m_Observer.transform,
				m_Observer.transform.position));
			Assert.AreEqual(1, CombatEventHub.PublishCount);
			Assert.AreEqual(0, Snapshot().SoundContacts.Count);
			Assert.AreEqual(soundBefore, WorldSoundHub.LastPublishDeliveryCount);

			WorldSoundHub.PublishGunshot(m_Enemy.transform, m_Enemy.transform.position);
			Assert.AreEqual(1, CombatEventHub.PublishCount);
			Assert.Greater(WorldSoundHub.LastPublishDeliveryCount, 0);
			Assert.Greater(Snapshot().SoundContacts.Count, 0);
		}
		#endregion

		#region Private Methods
		private void Hear(SoundEventType _type)
		{
			Processor().ApplySyntheticSound(m_Enemy.transform, new Vector3(10f, 0f, 0f), 0.85f, _type);
		}

		private DetectionProcessor Processor() => m_Observer.GetComponent<DetectionProcessor>();

		private AIPerceptionFrame Snapshot()
		{
			return AIPerceptionFrameBuilder.Build(Processor());
		}

		private static UnitAIStateContext DefenseCtx(Vector3 _anchor)
		{
			return UnitAIStateContext.ForDefense(_anchor, _anchor, 8f, Vector3.forward);
		}

		private AIPerceptionFrame SoundFrame(Vector3 _position, bool _hostile, SoundEventType _type)
		{
			return new AIPerceptionFrame(
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.None,
				new[]
				{
					new AISoundContact(m_Enemy.transform, _position, _type, 0.82f, 0f, 0.1f, _hostile)
				},
				Array.Empty<AIReportContact>());
		}

		private AIPerceptionFrame VisibleOnlyFrame()
		{
			AIContactKnowledge visible = VisibleHostile(m_Enemy.transform);
			return new AIPerceptionFrame(
				new[] { visible },
				new[] { visible },
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				new[] { visible },
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.High);
		}

		private AIPerceptionFrame VisiblePlusSoundFrame(Vector3 _soundPos)
		{
			AIContactKnowledge visible = VisibleHostile(m_Enemy.transform);
			return new AIPerceptionFrame(
				new[] { visible },
				new[] { visible },
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				new[] { visible },
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.High,
				new[]
				{
					new AISoundContact(
						m_Enemy.transform,
						_soundPos,
						SoundEventType.Gunshot,
						0.9f,
						0f,
						0f,
						true)
				},
				Array.Empty<AIReportContact>());
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
				_target != null ? _target.position : Vector3.zero,
				_target != null ? _target.position : Vector3.zero,
				0f,
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

		private static GameObject CreateListener(string _name, UnitTeamId _team)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitTeam>().SetTeam(_team);
			go.AddComponent<UnitPerception>();
			DetectionProcessor processor = go.AddComponent<DetectionProcessor>();
			go.AddComponent<UnitAIController>();
			WorldSoundHub.Register(processor);
			processor.SetSimulatedTime(0f);
			return go;
		}

		private static GameObject CreateEmitter(string _name, UnitTeamId _team)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitTeam>().SetTeam(_team);
			return go;
		}

		private static void DestroyAll(params GameObject[] _objects)
		{
			for (int i = 0; i < _objects.Length; i++)
			{
				if (_objects[i] != null)
					UnityEngine.Object.DestroyImmediate(_objects[i]);
			}
		}
		#endregion
	}
}
