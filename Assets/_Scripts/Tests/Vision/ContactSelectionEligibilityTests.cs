using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class ContactSelectionEligibilityTests
	{
		private ContactSelectionPolicy m_Policy;
		private GameObject m_TargetGo;

		[SetUp]
		public void SetUp()
		{
			m_Policy = ContactSelectionPolicy.CreateDefault();
			m_TargetGo = new GameObject("G5EligTarget");
		}

		[TearDown]
		public void TearDown()
		{
			if (m_TargetGo != null)
				Object.DestroyImmediate(m_TargetGo);
		}

		[Test]
		public void UnknownDetected_IsEligible()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Unknown, 1f, ObservationState.Observed);
			Assert.IsTrue(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out ContactSelectionRejectReason reason));
			Assert.AreEqual(ContactSelectionRejectReason.None, reason);
		}

		[Test]
		public void Friendly_IsRejected()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Friendly, 1f, ObservationState.Observed);
			c.Relationship = PerceivedRelationship.Friendly;
			Assert.IsFalse(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out ContactSelectionRejectReason reason));
			Assert.AreEqual(ContactSelectionRejectReason.Friendly, reason);
		}

		[Test]
		public void NeutralIdentity_IsRejected()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Neutral, 1f, ObservationState.Observed);
			Assert.IsFalse(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out ContactSelectionRejectReason reason));
			Assert.AreEqual(ContactSelectionRejectReason.NeutralIdentity, reason);
		}

		[Test]
		public void Hostile_IsEligible()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Hostile, 1f, ObservationState.Observed);
			c.Relationship = PerceivedRelationship.Hostile;
			Assert.IsTrue(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out _));
		}

		[Test]
		public void Forgotten_IsRejected()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Unknown, 0f, ObservationState.Lost);
			Assert.IsFalse(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out ContactSelectionRejectReason reason));
			Assert.AreEqual(ContactSelectionRejectReason.Forgotten, reason);
		}

		[Test]
		public void NotEngageable_IsRejected()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Unknown, 1f, ObservationState.Observed);
			Assert.IsFalse(ContactSelectionEligibility.Evaluate(c, false, m_Policy, out ContactSelectionRejectReason reason));
			Assert.AreEqual(ContactSelectionRejectReason.NotWorldEngageable, reason);
		}

		[Test]
		public void Stale_IsEligibleByDefault()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Unknown, 0.2f, ObservationState.Lost);
			Assert.IsTrue(MemoryDecayMath.IsStale(0.2f));
			Assert.IsTrue(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out _));
		}

		[Test]
		public void RecentlyLostWithMemory_IsEligible()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Unknown, 0.8f, ObservationState.RecentlyLost);
			Assert.IsTrue(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out _));
		}

		[Test]
		public void SoundOnly_IsEligible()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Unknown, 0f, ObservationState.NotObserved);
			c.SoundConfidence = 0.8f;
			c.SoundPosition = new Vector3(2f, 0f, 0f);
			c.LastKnownPosition = Vector3.zero;
			Assert.IsTrue(c.HasKnowledge);
			Assert.IsTrue(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out ContactSelectionRejectReason reason));
			Assert.AreEqual(ContactSelectionRejectReason.None, reason);
		}

		[Test]
		public void SharedOnly_IsEligible()
		{
			PerceivedContact c = Contact(PerceivedIdentity.Unknown, 0f, ObservationState.NotObserved);
			c.SharedConfidence = 0.7f;
			c.SharedPosition = new Vector3(3f, 0f, 0f);
			c.LastKnownPosition = Vector3.zero;
			Assert.IsTrue(ContactSelectionEligibility.Evaluate(c, true, m_Policy, out _));
		}

		private PerceivedContact Contact(
			PerceivedIdentity _identity,
			float _confidence,
			ObservationState _obs)
		{
			return new PerceivedContact
			{
				Target = m_TargetGo.transform,
				Identity = _identity,
				Relationship = PerceivedRelationship.Unknown,
				LastSeenConfidence = _confidence,
				ObservationState = _obs,
				LastKnownPosition = new Vector3(1f, 0f, 0f)
			};
		}
	}
}
