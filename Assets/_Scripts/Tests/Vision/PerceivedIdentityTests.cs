using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class PerceivedIdentityTests
	{
		private GameObject m_Root;
		private Transform m_Target;
		private UnitTeam m_Team;

		[SetUp]
		public void SetUp()
		{
			m_Root = new GameObject("G3IdentityRoot");
			m_Target = new GameObject("G3IdentityTarget").transform;
			m_Target.SetParent(m_Root.transform);
			m_Target.position = new Vector3(15f, 0f, 0f);
			m_Team = m_Target.gameObject.AddComponent<UnitTeam>();
			m_Team.SetTeam(UnitTeamId.Neutral);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Root != null)
				Object.DestroyImmediate(m_Root);
		}

		[Test]
		public void ActualTeam_IsNotModifiedByPerception()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			Observe(sim, 1f, 40);

			Assert.AreEqual(UnitTeamId.Neutral, m_Team.Team);
			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedIdentity.Hostile, contact.Identity);
		}

		[Test]
		public void SameTarget_CanHaveDifferentPerceivedIdentityPerObservers()
		{
			var a = new PerceivedContactLifecycleSimulator();
			var b = new PerceivedContactLifecycleSimulator();
			a.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			b.SetAffiliationCue(m_Target, ObservableAffiliation.Friendly);

			Observe(a, 1f, 40);
			Observe(b, 1f, 40);

			Assert.That(a.TryGet(m_Target, out PerceivedContact ca), Is.True);
			Assert.That(b.TryGet(m_Target, out PerceivedContact cb), Is.True);
			Assert.AreEqual(PerceivedIdentity.Hostile, ca.Identity);
			Assert.AreEqual(PerceivedIdentity.Friendly, cb.Identity);
			Assert.AreEqual(UnitTeamId.Neutral, m_Team.Team);
			Assert.That(ReferenceEquals(ca, cb), Is.False);
		}

		[Test]
		public void DetectionAndIdentityConfidence_AreIndependent()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			Observe(sim, 1f, 8);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(DetectionState.Detected, contact.State);
			Assert.GreaterOrEqual(contact.DetectionProgress, 0.99f);
			Assert.Less(contact.IdentityConfidence, IdentityKnowledgeMath.DefaultCommitThreshold);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreNotEqual(contact.DetectionProgress, contact.IdentityConfidence);
		}

		[Test]
		public void IdentityConfidence_StaysSeparateFromDetectionProgress()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			Observe(sim, 1f, 50);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(DetectionState.Detected, contact.State);
			Assert.AreEqual(PerceivedIdentity.Hostile, contact.Identity);
			Assert.GreaterOrEqual(contact.IdentityConfidence, IdentityKnowledgeMath.DefaultCommitThreshold);
			Assert.AreNotEqual(contact.DetectionProgress, contact.IdentityConfidence);
		}

		[Test]
		public void Relationship_IsIndependentFromActualTeam()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			Observe(sim, 1f, 40);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedRelationship.Hostile, contact.Relationship);
			Assert.AreEqual(UnitTeamId.Neutral, m_Team.Team);
		}

		[Test]
		public void Threat_IsIndependentFromRelationship()
		{
			Assert.AreEqual(
				ThreatLevel.Low,
				IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 400f));
			Assert.AreEqual(
				ThreatLevel.High,
				IdentityKnowledgeMath.EvaluateThreat(PerceivedRelationship.Hostile, 10f));

			var far = new PerceivedContactLifecycleSimulator();
			far.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			m_Target.position = new Vector3(400f, 0f, 0f);
			Observe(far, 1f, 40, m_Target.position);

			Assert.That(far.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedRelationship.Hostile, contact.Relationship);
			Assert.AreEqual(ThreatLevel.Low, contact.Threat);
		}

		[Test]
		public void UnknownIdentity_IsValidAfterDetection()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			Observe(sim, 1f, 20);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(DetectionState.Detected, contact.State);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(0f, contact.IdentityConfidence, 0.0001f);
			Assert.AreEqual(PerceivedRelationship.Unknown, contact.Relationship);
			Assert.AreEqual(ThreatLevel.None, contact.Threat);
		}

		[Test]
		public void HostilePerception_DoesNotMutateWorldTeam()
		{
			m_Team.SetTeam(UnitTeamId.Neutral);
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			Observe(sim, 1f, 40);

			Assert.AreEqual(UnitTeamId.Neutral, m_Team.Team);
			m_Team.SetTeam(UnitTeamId.Player);
			Assert.AreEqual(UnitTeamId.Player, m_Team.Team);
		}

		[Test]
		public void Reacquire_PreservesSubjectiveIdentityState()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			float now = Observe(sim, 1f, 40);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact before), Is.True);
			object beforeRef = before;
			PerceivedIdentity id = before.Identity;
			float conf = before.IdentityConfidence;
			Assert.AreEqual(PerceivedIdentity.Hostile, id);

			sim.SoftLose(m_Target, now);
			now += MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f;
			sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f, now);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact lost), Is.True);
			Assert.AreEqual(ObservationState.Lost, lost.ObservationState);
			Assert.AreEqual(id, lost.Identity);
			Assert.AreEqual(conf, lost.IdentityConfidence, 0.0001f);
			Assert.That(ReferenceEquals(lost, beforeRef), Is.True);

			now += 0.05f;
			sim.ApplyEvidence(m_Target, 1f, m_Target.position, now);
			sim.Advance(0.05f, now);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact again), Is.True);
			Assert.That(ReferenceEquals(again, beforeRef), Is.True);
			Assert.AreEqual(PerceivedIdentity.Hostile, again.Identity);
			Assert.GreaterOrEqual(again.IdentityConfidence, conf);
		}

		private float Observe(
			PerceivedContactLifecycleSimulator _sim,
			float _quality,
			int _ticks,
			Vector3? _position = null)
		{
			float now = 0f;
			Vector3 pos = _position ?? m_Target.position;
			for (int i = 0; i < _ticks; i++)
			{
				_sim.ApplyEvidence(m_Target, _quality, pos, now);
				now += 0.05f;
				_sim.Advance(0.05f, now);
			}

			return now;
		}
	}
}
