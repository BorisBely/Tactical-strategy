using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class PerceivedMemoryTests
	{
		private GameObject m_Root;
		private Transform m_Target;

		[SetUp]
		public void SetUp()
		{
			m_Root = new GameObject("G4MemoryRoot");
			m_Target = new GameObject("G4MemoryTarget").transform;
			m_Target.SetParent(m_Root.transform);
			m_Target.position = new Vector3(5f, 0f, 1f);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Root != null)
				Object.DestroyImmediate(m_Root);
		}

		[Test]
		public void Observed_ThenLose_ConfidenceDecays_LastSeenFrozen()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			Vector3 seen = new Vector3(5f, 0f, 1f);
			float now = Observe(sim, 1f, 20, seen);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact observed), Is.True);
			Assert.AreEqual(1f, observed.LastSeenConfidence, 0.0001f);
			Assert.AreEqual(seen, observed.LastSeenPosition);
			Assert.AreEqual(seen, observed.LastKnownPosition);

			sim.SoftLose(m_Target, now);
			m_Target.position = new Vector3(200f, 0f, 200f);

			now += 1.5f;
			sim.Advance(1.5f, now);
			Assert.That(sim.TryGet(m_Target, out PerceivedContact mid), Is.True);
			Assert.AreEqual(ObservationState.RecentlyLost, mid.ObservationState);
			Assert.AreEqual(seen, mid.LastSeenPosition);
			Assert.AreEqual(seen, mid.LastKnownPosition);
			Assert.AreNotEqual(mid.LastSeenPosition, m_Target.position);
			Assert.Less(mid.LastSeenConfidence, 1f);
			Assert.Greater(mid.LastSeenConfidence, 0.4f);

			now += MemoryDecayMath.DefaultRecentlyLostSeconds;
			sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds, now);
			Assert.That(sim.TryGet(m_Target, out PerceivedContact lost), Is.True);
			Assert.AreEqual(ObservationState.Lost, lost.ObservationState);
			Assert.Less(lost.LastSeenConfidence, mid.LastSeenConfidence);
			Assert.AreEqual(seen, lost.LastKnownPosition);
		}

		[Test]
		public void Reacquire_RestoresMemory_AndKeepsIdentity()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			Vector3 first = new Vector3(3f, 0f, 0f);
			float now = Observe(sim, 1f, 40, first);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact before), Is.True);
			object beforeRef = before;
			Assert.AreEqual(PerceivedIdentity.Hostile, before.Identity);
			float identityConf = before.IdentityConfidence;

			sim.SoftLose(m_Target, now);
			now += MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f;
			sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f, now);
			Assert.That(sim.TryGet(m_Target, out PerceivedContact lost), Is.True);
			Assert.AreEqual(ObservationState.Lost, lost.ObservationState);
			Assert.Less(lost.LastSeenConfidence, 1f);
			Assert.AreEqual(PerceivedIdentity.Hostile, lost.Identity);
			Assert.AreEqual(identityConf, lost.IdentityConfidence, 0.0001f);

			Vector3 second = new Vector3(9f, 0f, 2f);
			now += 0.05f;
			sim.ApplyEvidence(m_Target, 1f, second, now);
			sim.Advance(0.05f, now);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact again), Is.True);
			Assert.That(ReferenceEquals(again, beforeRef), Is.True);
			Assert.AreEqual(1f, again.LastSeenConfidence, 0.0001f);
			Assert.AreEqual(second, again.LastSeenPosition);
			Assert.AreEqual(second, again.LastKnownPosition);
			Assert.AreEqual(PerceivedIdentity.Hostile, again.Identity);
		}

		[Test]
		public void NeverObserved_HasNoContact_NoFakeLastKnown()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.Advance(1f, 1f);
			Assert.That(sim.TryGet(m_Target, out _), Is.False);
		}

		[Test]
		public void LastSeenPosition_IsNotLiveTransform()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			Vector3 observedPos = new Vector3(3f, 0f, 4f);
			m_Target.position = new Vector3(100f, 0f, 100f);
			float now = Observe(sim, 1f, 10, observedPos);
			sim.SoftLose(m_Target, now);
			m_Target.position = new Vector3(200f, 5f, 200f);
			now += MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f;
			sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f, now);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact c), Is.True);
			Assert.AreEqual(observedPos, c.LastSeenPosition);
			Assert.AreEqual(observedPos, c.LastKnownPosition);
			Assert.AreNotEqual(c.LastSeenPosition, m_Target.position);
		}

		[Test]
		public void LastSeenConfidence_IsIndependentFromDetectionProgress()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			float now = Observe(sim, 1f, 20, m_Target.position);
			sim.SoftLose(m_Target, now);
			now += 1f;
			sim.Advance(1f, now);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact c), Is.True);
			Assert.AreNotEqual(c.DetectionProgress, c.LastSeenConfidence);
			Assert.Greater(c.LastSeenConfidence, 0f);
		}

		[Test]
		public void IdentityConfidence_HoldsWhileMemoryDecays()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			sim.SetAffiliationCue(m_Target, ObservableAffiliation.Hostile);
			float now = Observe(sim, 1f, 40, m_Target.position);
			Assert.That(sim.TryGet(m_Target, out PerceivedContact before), Is.True);
			float identity = before.IdentityConfidence;
			Assert.GreaterOrEqual(identity, IdentityKnowledgeMath.DefaultCommitThreshold);

			sim.SoftLose(m_Target, now);
			now += 5f;
			sim.Advance(5f, now);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact after), Is.True);
			Assert.AreEqual(identity, after.IdentityConfidence, 0.0001f);
			Assert.Less(after.LastSeenConfidence, 1f);
			Assert.Less(after.LastSeenConfidence, identity);
		}

		[Test]
		public void Horizon_ForgetsMemory_ButKeepsContact()
		{
			var sim = new PerceivedContactLifecycleSimulator();
			float now = Observe(sim, 1f, 10, m_Target.position);
			sim.SoftLose(m_Target, now);
			now += MemoryDecayMath.DefaultHorizonSeconds + 0.1f;
			sim.Advance(MemoryDecayMath.DefaultHorizonSeconds + 0.1f, now);

			Assert.That(sim.TryGet(m_Target, out PerceivedContact c), Is.True);
			Assert.AreEqual(0f, c.LastSeenConfidence, 0.0001f);
			Assert.IsTrue(c.IsMemoryForgotten);
			Assert.AreEqual(ObservationState.Lost, c.ObservationState);
		}

		private float Observe(
			PerceivedContactLifecycleSimulator _sim,
			float _quality,
			int _ticks,
			Vector3 _position)
		{
			float now = 0f;
			for (int i = 0; i < _ticks; i++)
			{
				_sim.ApplyEvidence(m_Target, _quality, _position, now);
				now += 0.05f;
				_sim.Advance(0.05f, now);
			}

			return now;
		}
	}
}
