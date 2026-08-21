using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class VisualIdentityEvidenceTests
	{
		private GameObject m_Target;
		private UnitTeam m_TargetTeam;
		private VisualIdentityEvidence m_Evidence;

		[SetUp]
		public void SetUp()
		{
			m_Target = new GameObject("WorldEvidenceTarget");
			m_Target.transform.position = new Vector3(15f, 0f, 0f);
			m_TargetTeam = m_Target.AddComponent<UnitTeam>();
			m_TargetTeam.SetTeam(UnitTeamId.Enemy);
			m_Evidence = m_Target.AddComponent<VisualIdentityEvidence>();
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Target != null)
				Object.DestroyImmediate(m_Target);
		}

		[Test]
		public void NoEvidence_StaysUnknown()
		{
			Object.DestroyImmediate(m_Evidence);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 88);

				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(DetectionState.Detected, contact.State);
				Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
				Assert.LessOrEqual(contact.IdentityConfidence, 0.0001f);
				Assert.AreEqual(UnitTeamId.Enemy, m_TargetTeam.Team);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void OneSecond_DetectedButStillUnknown()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Enemy);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 20);

				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(DetectionState.Detected, contact.State);
				Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
				Assert.IsFalse(IdentityKnowledgeMath.HasReachedCommitThreshold(contact.IdentityConfidence));
				Assert.AreEqual(UnitTeamId.Enemy, m_TargetTeam.Team);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void TwoSeconds_CommitsHostileFromEnemyLook()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Enemy);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 40);

				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(PerceivedIdentity.Hostile, contact.Identity);
				Assert.AreEqual(PerceivedRelationship.Hostile, contact.Relationship);
				Assert.AreEqual(UnitTeamId.Enemy, m_TargetTeam.Team);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void FriendlyLook_CommitsFriendly()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Player);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 44);

				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(PerceivedIdentity.Friendly, contact.Identity);
				Assert.AreEqual(PerceivedRelationship.Friendly, contact.Relationship);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void CivilianLook_CommitsNeutral()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Civilian);
			m_TargetTeam.SetTeam(UnitTeamId.Neutral);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 44);

				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(PerceivedIdentity.Neutral, contact.Identity);
				Assert.AreEqual(PerceivedRelationship.Neutral, contact.Relationship);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void Disguise_UsesLookNotUnitTeam()
		{
			m_TargetTeam.SetTeam(UnitTeamId.Enemy);
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Player);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 44);

				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(PerceivedIdentity.Friendly, contact.Identity);
				Assert.AreEqual(UnitTeamId.Enemy, m_TargetTeam.Team);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void LosLoss_HoldsCommittedIdentity_SameContact()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Enemy);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				float now = Observe(processor, 44);
				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact before), Is.True);
				Assert.AreEqual(PerceivedIdentity.Hostile, before.Identity);
				object contactRef = before;

				processor.ApplyEmptyObservationFrame();
				processor.Advance(5f, now + 5f);

				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact after), Is.True);
				Assert.AreSame(contactRef, after);
				Assert.AreEqual(PerceivedIdentity.Hostile, after.Identity);
				Assert.AreEqual(PerceivedRelationship.Hostile, after.Relationship);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void TwoObservers_SameLook_DifferentWatchTime()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Enemy);
			GameObject observerA = CreateObserver(UnitTeamId.Player, "EvidenceObserverA");
			GameObject observerB = CreateObserver(UnitTeamId.Player, "EvidenceObserverB");
			try
			{
				DetectionProcessor a = observerA.GetComponent<DetectionProcessor>();
				DetectionProcessor b = observerB.GetComponent<DetectionProcessor>();
				Observe(a, 16);
				Observe(b, 60);

				Assert.That(a.TryGetContact(m_Target.transform, out PerceivedContact contactA), Is.True);
				Assert.That(b.TryGetContact(m_Target.transform, out PerceivedContact contactB), Is.True);
				Assert.AreEqual(PerceivedIdentity.Unknown, contactA.Identity);
				Assert.AreEqual(PerceivedIdentity.Hostile, contactB.Identity);
				Assert.AreNotSame(contactA, contactB);
				Assert.AreEqual(UnitTeamId.Enemy, m_TargetTeam.Team);
			}
			finally
			{
				Object.DestroyImmediate(observerA);
				Object.DestroyImmediate(observerB);
			}
		}

		[Test]
		public void ConflictingLook_ResetsThenRecommits()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Enemy);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 44);
				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(PerceivedIdentity.Hostile, contact.Identity);

				m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Player);
				Observe(processor, 3);
				Assert.That(processor.TryGetContact(m_Target.transform, out contact), Is.True);
				Assert.AreNotEqual(PerceivedIdentity.Friendly, contact.Identity);
				Assert.IsFalse(IdentityKnowledgeMath.HasReachedCommitThreshold(contact.IdentityConfidence));

				Observe(processor, 44);
				Assert.That(processor.TryGetContact(m_Target.transform, out contact), Is.True);
				Assert.AreEqual(PerceivedIdentity.Friendly, contact.Identity);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void Processor_DoesNotWriteTargetTeam()
		{
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Enemy);
			m_TargetTeam.SetTeam(UnitTeamId.Neutral);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 44);
				Assert.AreEqual(UnitTeamId.Neutral, m_TargetTeam.Team);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		[Test]
		public void SpawnConfig_LookIsIndependentFromTeam()
		{
			var player = new UnitSpawnConfig(UnitTeamId.Player, new UnitSpawnLoadout(), false);
			Assert.AreEqual(VisualAffiliation.Player, player.ResolvedVisualAffiliation);

			var enemy = new UnitSpawnConfig(UnitTeamId.Enemy, new UnitSpawnLoadout(), false);
			Assert.AreEqual(VisualAffiliation.Enemy, enemy.ResolvedVisualAffiliation);

			var disguise = new UnitSpawnConfig(
				UnitTeamId.Enemy,
				new UnitSpawnLoadout(),
				false,
				_visualAffiliation: VisualAffiliation.Player);
			Assert.AreEqual(UnitTeamId.Enemy, disguise.Team);
			Assert.AreEqual(VisualAffiliation.Player, disguise.ResolvedVisualAffiliation);

			var civilianMesh = new UnitSpawnConfig(
				UnitTeamId.Player,
				new UnitSpawnLoadout(),
				false,
				_bodyMeshArchetype: UnitBodyMeshArchetype.Civilian);
			Assert.AreEqual(VisualAffiliation.Civilian, civilianMesh.ResolvedVisualAffiliation);
		}

		[Test]
		public void LegacyAppearance_IsIgnored()
		{
#pragma warning disable CS0618
			IdentityAppearance legacy = m_Target.AddComponent<IdentityAppearance>();
			legacy.SetAffiliation(ObservableAffiliation.Hostile);
#pragma warning restore CS0618
			m_Evidence.SetPrimaryAffiliation(VisualAffiliation.Unknown);
			GameObject observer = CreateObserver(UnitTeamId.Player);
			try
			{
				DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
				Observe(processor, 44);
				Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
				Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			}
			finally
			{
				Object.DestroyImmediate(observer);
			}
		}

		private static GameObject CreateObserver(UnitTeamId _side, string _name = "EvidenceObserver")
		{
			var go = new GameObject(_name);
			go.SetActive(false);
			UnitTeam team = go.AddComponent<UnitTeam>();
			team.SetTeam(_side);
			go.AddComponent<UnitObservationSource>();
			go.AddComponent<UnitPerception>();
			if (go.GetComponent<DetectionProcessor>() == null)
				go.AddComponent<DetectionProcessor>();
			go.SetActive(true);

			DetectionProcessor processor = go.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			return go;
		}

		private float Observe(DetectionProcessor _processor, int _ticks)
		{
			float now = 0f;
			Vector3 position = m_Target.transform.position;
			for (int i = 0; i < _ticks; i++)
			{
				_processor.ApplySyntheticObservation(m_Target.transform, 15f, 0f, 1f, position);
				now += 0.05f;
				_processor.Advance(0.05f, now);
			}

			return now;
		}
	}
}
