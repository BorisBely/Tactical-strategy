using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class AIPerceptionContractTests
	{
		[Test]
		public void VisibleDetectedObservedHostile_IsVisibleNow()
		{
			PerceivedContact contact = Contact(
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				1f,
				1f);

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			Assert.IsTrue(knowledge.VisibleNow);
			Assert.IsTrue(knowledge.IdentityKnown);
			Assert.IsTrue(knowledge.Hostile);
			Assert.IsTrue(knowledge.ThreatHigh);
			Assert.IsFalse(knowledge.IdentityUnknown);
			Assert.IsFalse(knowledge.RecentlyLost);
		}

		[Test]
		public void RecentlyLost_IsNotVisible_HasUsefulMemory()
		{
			PerceivedContact contact = Contact(
				DetectionState.Detected,
				ObservationState.RecentlyLost,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				1f,
				0.95f);

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			Assert.IsFalse(knowledge.VisibleNow);
			Assert.IsTrue(knowledge.RecentlyLost);
			Assert.IsTrue(knowledge.HasUsefulMemory);
			Assert.IsFalse(knowledge.MemoryStale);
		}

		[Test]
		public void LostUsefulMemory_IsValid()
		{
			PerceivedContact contact = Contact(
				DetectionState.Detected,
				ObservationState.Lost,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.Medium,
				1f,
				0.6f);

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			Assert.IsTrue(knowledge.Lost);
			Assert.IsFalse(knowledge.VisibleNow);
			Assert.IsTrue(knowledge.HasUsefulMemory);
			Assert.IsFalse(knowledge.MemoryStale);
		}

		[Test]
		public void LostStale_IsMemoryStale()
		{
			PerceivedContact contact = Contact(
				DetectionState.Detected,
				ObservationState.Lost,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.Low,
				1f,
				0.1f);

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			Assert.IsTrue(knowledge.MemoryStale);
			Assert.IsFalse(knowledge.HasUsefulMemory);
			Assert.IsFalse(knowledge.VisibleNow);
		}

		[Test]
		public void DetectedUnknown_DoesNotBecomeHostile()
		{
			PerceivedContact contact = Contact(
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Unknown,
				PerceivedRelationship.Unknown,
				ThreatLevel.None,
				0f,
				1f);

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			Assert.IsTrue(knowledge.VisibleNow);
			Assert.IsTrue(knowledge.IdentityUnknown);
			Assert.IsFalse(knowledge.IdentityKnown);
			Assert.IsFalse(knowledge.Hostile);
			Assert.IsTrue(knowledge.ThreatNone);
		}

		[Test]
		public void ThreatBands_PassThroughWithoutChangingSource()
		{
			AssertThreat(ThreatLevel.High, true, false, false);
			AssertThreat(ThreatLevel.Medium, false, true, false);
			AssertThreat(ThreatLevel.Low, false, false, true);
		}

		[Test]
		public void ObserverA_IsNotObserverB()
		{
			var targetGo = new GameObject("AI0_Contract_Target");
			try
			{
				Transform target = targetGo.transform;
				var registryA = new FakeRegistry();
				registryA.Add(ContactOn(
					target,
					DetectionState.Detected,
					ObservationState.Observed,
					PerceivedIdentity.Hostile,
					PerceivedRelationship.Hostile,
					ThreatLevel.High,
					1f,
					1f));
				var registryB = new FakeRegistry();
				registryB.Add(ContactOn(
					target,
					DetectionState.Detected,
					ObservationState.Observed,
					PerceivedIdentity.Unknown,
					PerceivedRelationship.Unknown,
					ThreatLevel.None,
					0f,
					1f));

				AIPerceptionFrame frameA = AIPerceptionFrameBuilder.Build(registryA);
				AIPerceptionFrame frameB = AIPerceptionFrameBuilder.Build(registryB);

				Assert.AreEqual(1, frameA.HostileContacts.Count);
				Assert.AreEqual(0, frameB.HostileContacts.Count);
				Assert.AreEqual(1, frameB.UnknownContacts.Count);
				Assert.IsTrue(frameA.TryGetContact(target, out AIContactKnowledge a));
				Assert.IsTrue(frameB.TryGetContact(target, out AIContactKnowledge b));
				Assert.IsTrue(a.Hostile);
				Assert.IsTrue(b.IdentityUnknown);
				Assert.AreNotEqual(a.Identity, b.Identity);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(targetGo);
			}
		}

		[Test]
		public void Snapshot_OmitsDetectionProgressAndVisibilityQuality()
		{
			Assert.IsNull(typeof(AIContactKnowledge).GetField("DetectionProgress"));
			Assert.IsNull(typeof(AIContactKnowledge).GetField("VisibilityQuality"));
			Assert.IsNull(typeof(AIContactKnowledge).GetField("CurrentEvaluation"));
			Assert.IsNull(typeof(AIPerceptionFrame).GetField("SelectedTarget"));

			FieldInfo[] fields = typeof(AIContactKnowledge).GetFields(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			for (int i = 0; i < fields.Length; i++)
			{
				Assert.AreNotEqual(typeof(DetectionEvaluation), fields[i].FieldType);
				Assert.AreNotEqual(typeof(VisionObservation), fields[i].FieldType);
				Assert.AreNotEqual(typeof(UnitTeam), fields[i].FieldType);
				Assert.AreNotEqual(typeof(UnitTeamId), fields[i].FieldType);
			}
		}

		[Test]
		public void Builder_IgnoresDetectionProgressDifferences()
		{
			var go = new GameObject("AI0_Contract_Progress");
			try
			{
				Transform target = go.transform;
				PerceivedContact low = ContactOn(
					target,
					DetectionState.Detected,
					ObservationState.Observed,
					PerceivedIdentity.Hostile,
					PerceivedRelationship.Hostile,
					ThreatLevel.High,
					1f,
					1f);
				low.DetectionProgress = 0.1f;
				low.CurrentEvaluation = new DetectionEvaluation { VisibilityQuality = 0.2f };

				PerceivedContact high = ContactOn(
					target,
					DetectionState.Detected,
					ObservationState.Observed,
					PerceivedIdentity.Hostile,
					PerceivedRelationship.Hostile,
					ThreatLevel.High,
					1f,
					1f);
				high.DetectionProgress = 1f;
				high.CurrentEvaluation = new DetectionEvaluation { VisibilityQuality = 1f };

				var registryA = new FakeRegistry();
				registryA.Add(low);
				var registryB = new FakeRegistry();
				registryB.Add(high);

				Assert.IsTrue(AIPerceptionFrameBuilder.Build(registryA).TryGetContact(target, out AIContactKnowledge a));
				Assert.IsTrue(AIPerceptionFrameBuilder.Build(registryB).TryGetContact(target, out AIContactKnowledge b));
				Assert.AreEqual(a.VisibleNow, b.VisibleNow);
				Assert.AreEqual(a.Hostile, b.Hostile);
				Assert.AreEqual(a.Threat, b.Threat);
				Assert.AreEqual(a.IdentityConfidence, b.IdentityConfidence);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Forgotten_IsNeitherUsefulNorStale()
		{
			PerceivedContact contact = Contact(
				DetectionState.Detected,
				ObservationState.Lost,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				ThreatLevel.Low,
				1f,
				0f);

			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			Assert.IsFalse(knowledge.HasUsefulMemory);
			Assert.IsFalse(knowledge.MemoryStale);
		}

		[Test]
		public void Frame_BucketsRememberedAndStrongestThreat()
		{
			var visibleGo = new GameObject("AI0_Visible");
			var rememberedGo = new GameObject("AI0_Remembered");
			try
			{
				var registry = new FakeRegistry();
				registry.Add(ContactOn(
					visibleGo.transform,
					DetectionState.Detected,
					ObservationState.Observed,
					PerceivedIdentity.Hostile,
					PerceivedRelationship.Hostile,
					ThreatLevel.Low,
					1f,
					1f));
				registry.Add(ContactOn(
					rememberedGo.transform,
					DetectionState.Detected,
					ObservationState.Lost,
					PerceivedIdentity.Hostile,
					PerceivedRelationship.Hostile,
					ThreatLevel.High,
					1f,
					0.6f));

				AIPerceptionFrame frame = AIPerceptionFrameBuilder.Build(registry);
				Assert.AreEqual(1, frame.VisibleContacts.Count);
				Assert.AreEqual(1, frame.RememberedContacts.Count);
				Assert.AreEqual(2, frame.HostileContacts.Count);
				Assert.AreEqual(ThreatLevel.High, frame.StrongestThreat);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(visibleGo);
				UnityEngine.Object.DestroyImmediate(rememberedGo);
			}
		}

		private static void AssertThreat(ThreatLevel _threat, bool _high, bool _medium, bool _low)
		{
			PerceivedContact contact = Contact(
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Hostile,
				PerceivedRelationship.Hostile,
				_threat,
				1f,
				1f);
			AIContactKnowledge knowledge = AIContactKnowledge.From(contact);
			Assert.AreEqual(_threat, knowledge.Threat);
			Assert.AreEqual(_high, knowledge.ThreatHigh);
			Assert.AreEqual(_medium, knowledge.ThreatMedium);
			Assert.AreEqual(_low, knowledge.ThreatLow);
			Assert.AreEqual(_threat, contact.Threat);
		}

		private static PerceivedContact Contact(
			DetectionState _state,
			ObservationState _observation,
			PerceivedIdentity _identity,
			PerceivedRelationship _relationship,
			ThreatLevel _threat,
			float _identityConfidence,
			float _lastSeenConfidence)
		{
			return ContactOn(
				null,
				_state,
				_observation,
				_identity,
				_relationship,
				_threat,
				_identityConfidence,
				_lastSeenConfidence);
		}

		private static PerceivedContact ContactOn(
			Transform _target,
			DetectionState _state,
			ObservationState _observation,
			PerceivedIdentity _identity,
			PerceivedRelationship _relationship,
			ThreatLevel _threat,
			float _identityConfidence,
			float _lastSeenConfidence)
		{
			return new PerceivedContact
			{
				Target = _target,
				State = _state,
				DetectionProgress = 1f,
				ObservationState = _observation,
				Identity = _identity,
				IdentityConfidence = _identityConfidence,
				Relationship = _relationship,
				Threat = _threat,
				LastSeenConfidence = _lastSeenConfidence,
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
				Transform key = _contact.Target != null ? _contact.Target : CreateKey();
				_contact.Target = key;
				m_Contacts[key] = _contact;
			}

			public bool TryGetContact(Transform _target, out PerceivedContact _contact)
			{
				return m_Contacts.TryGetValue(_target, out _contact);
			}

			private Transform CreateKey()
			{
				var go = new GameObject("AI0_FakeContact");
				return go.transform;
			}
		}
	}
}
