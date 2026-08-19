using NUnit.Framework;
using UnityEngine;

public sealed class PerceivedContactLifecycleTests
{
	private GameObject m_Root;
	private Transform m_Target;

	[SetUp]
	public void SetUp()
	{
		m_Root = new GameObject("LifecycleRoot");
		m_Target = new GameObject("LifecycleTarget").transform;
		m_Target.SetParent(m_Root.transform);
		m_Target.position = new Vector3(10f, 0f, 0f);
	}

	[TearDown]
	public void TearDown()
	{
		if (m_Root != null)
			Object.DestroyImmediate(m_Root);
		if (m_Target != null)
			Object.DestroyImmediate(m_Target.gameObject);
	}

	[Test]
	public void SoftLose_KeepsLastSeen_AndTransitionsObserved_RecentlyLost_Lost()
	{
		var sim = new PerceivedContactLifecycleSimulator();
		float now = 0f;
		Vector3 seenPos = new Vector3(5f, 0f, 1f);

		sim.ApplyEvidence(m_Target, 1f, seenPos, now);
		Assert.That(sim.TryGet(m_Target, out _), Is.False, "pending only until progress > 0");

		for (int i = 0; i < 20; i++)
		{
			now += 0.05f;
			sim.Advance(0.05f, now);
		}

		Assert.That(sim.TryGet(m_Target, out PerceivedContact detected), Is.True);
		Assert.That(detected.State, Is.EqualTo(DetectionState.Detected));
		Assert.That(detected.ObservationState, Is.EqualTo(ObservationState.Observed));
		Assert.That(detected.LastSeenPosition, Is.EqualTo(seenPos));

		float frozenSeenTime = detected.LastSeenTime;
		Vector3 frozenPos = detected.LastSeenPosition;
		sim.SoftLose(m_Target, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact soft), Is.True);
		Assert.That(soft.ObservationState, Is.EqualTo(ObservationState.RecentlyLost));
		Assert.That(soft.LastSeenPosition, Is.EqualTo(frozenPos));
		Assert.That(soft.LastSeenTime, Is.EqualTo(frozenSeenTime));

		now += 1.5f;
		sim.Advance(1.5f, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact mid), Is.True);
		Assert.That(mid.ObservationState, Is.EqualTo(ObservationState.RecentlyLost));
		Assert.That(mid.LastSeenPosition, Is.EqualTo(frozenPos));

		now += MemoryDecayMath.DefaultRecentlyLostSeconds;
		sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact lost), Is.True);
		Assert.That(lost.ObservationState, Is.EqualTo(ObservationState.Lost));
		Assert.That(lost.LastSeenPosition, Is.EqualTo(frozenPos));
		Assert.That(lost.LastSeenTime, Is.EqualTo(frozenSeenTime));
	}

	[Test]
	public void SoftLose_GraceMinusEpsilon_StillRecentlyLost()
	{
		var sim = new PerceivedContactLifecycleSimulator(recentlyLostDurationSeconds: 3f);
		float now = 0f;
		sim.ApplyEvidence(m_Target, 1f, Vector3.one, now);
		for (int i = 0; i < 30; i++)
		{
			now += 0.05f;
			sim.Advance(0.05f, now);
		}

		sim.SoftLose(m_Target, now);
		now += 2.95f;
		sim.Advance(2.95f, now);

		Assert.That(sim.TryGet(m_Target, out PerceivedContact c), Is.True);
		Assert.That(c.ObservationState, Is.EqualTo(ObservationState.RecentlyLost));
	}

	[Test]
	public void SoftLose_AtExactRecentlyLostDuration_BecomesLost()
	{
		var sim = new PerceivedContactLifecycleSimulator();
		float now = 0f;
		sim.ApplyEvidence(m_Target, 1f, Vector3.one, now);
		for (int i = 0; i < 48; i++)
		{
			now += 0.05f;
			sim.Advance(0.05f, now);
		}

		sim.SoftLose(m_Target, now);
		float lossTime = now;
		now = lossTime + 4.95f;
		sim.Advance(4.95f, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact before), Is.True);
		Assert.That(before.ObservationState, Is.EqualTo(ObservationState.RecentlyLost));

		now = lossTime + MemoryDecayMath.DefaultRecentlyLostSeconds;
		sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds - 4.95f, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact atExact), Is.True);
		Assert.That(atExact.ObservationState, Is.EqualTo(ObservationState.Lost));
	}

	[Test]
	public void Reacquire_KeepsSameContact_AndUpdatesLastSeen()
	{
		var sim = new PerceivedContactLifecycleSimulator();
		float now = 0f;
		Vector3 firstPos = new Vector3(1f, 0f, 0f);
		Vector3 secondPos = new Vector3(9f, 0f, 2f);

		sim.ApplyEvidence(m_Target, 1f, firstPos, now);
		for (int i = 0; i < 20; i++)
		{
			now += 0.05f;
			sim.Advance(0.05f, now);
		}

		Assert.That(sim.TryGet(m_Target, out PerceivedContact before), Is.True);
		object beforeRef = before;
		float firstSeen = before.LastSeenTime;

		sim.SoftLose(m_Target, now);
		now += MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f;
		sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact lost), Is.True);
		Assert.That(lost.ObservationState, Is.EqualTo(ObservationState.Lost));
		Assert.That(ReferenceEquals(lost, beforeRef), Is.True);

		now += 1f;
		sim.ApplyEvidence(m_Target, 1f, secondPos, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact again), Is.True);
		Assert.That(ReferenceEquals(again, beforeRef), Is.True);
		Assert.That(again.ObservationState, Is.EqualTo(ObservationState.Observed));
		Assert.That(again.LastSeenPosition, Is.EqualTo(secondPos));
		Assert.That(again.LastSeenTime, Is.GreaterThan(firstSeen));
	}

	[Test]
	public void DualObservers_IndependentContacts_SameTarget()
	{
		var a = new PerceivedContactLifecycleSimulator();
		var b = new PerceivedContactLifecycleSimulator();
		float now = 0f;

		a.ApplyEvidence(m_Target, 1f, new Vector3(1f, 0f, 0f), now);
		for (int i = 0; i < 20; i++)
		{
			now += 0.05f;
			a.Advance(0.05f, now);
			b.Advance(0.05f, now);
		}

		Assert.That(a.TryGet(m_Target, out PerceivedContact ca), Is.True);
		Assert.That(ca.State, Is.EqualTo(DetectionState.Detected));
		Assert.That(ca.ObservationState, Is.EqualTo(ObservationState.Observed));
		Assert.That(b.TryGet(m_Target, out _), Is.False);

		b.ApplyEvidence(m_Target, 1f, new Vector3(2f, 0f, 0f), now);
		for (int i = 0; i < 20; i++)
		{
			now += 0.05f;
			a.Advance(0.05f, now);
			b.Advance(0.05f, now);
		}

		Assert.That(b.TryGet(m_Target, out PerceivedContact cb), Is.True);
		Assert.That(cb.State, Is.EqualTo(DetectionState.Detected));
		Assert.That(ReferenceEquals(ca, cb), Is.False);

		a.SoftLose(m_Target, now);
		now += 0.1f;
		a.Advance(0.1f, now);
		b.Advance(0.1f, now);

		Assert.That(a.TryGet(m_Target, out PerceivedContact aSoft), Is.True);
		Assert.That(aSoft.ObservationState, Is.EqualTo(ObservationState.RecentlyLost));
		Assert.That(b.TryGet(m_Target, out PerceivedContact bStill), Is.True);
		Assert.That(bStill.ObservationState, Is.EqualTo(ObservationState.Observed));
	}

	[Test]
	public void LastSeen_ComesFromObservation_NotLiveTransform()
	{
		var sim = new PerceivedContactLifecycleSimulator();
		float now = 0f;
		Vector3 observedPos = new Vector3(3f, 0f, 4f);
		m_Target.position = new Vector3(100f, 0f, 100f);

		sim.ApplyEvidence(m_Target, 1f, observedPos, now);
		for (int i = 0; i < 10; i++)
		{
			now += 0.05f;
			sim.Advance(0.05f, now);
		}

		sim.SoftLose(m_Target, now);
		m_Target.position = new Vector3(200f, 5f, 200f);
		now += MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f;
		sim.Advance(MemoryDecayMath.DefaultRecentlyLostSeconds + 0.2f, now);

		Assert.That(sim.TryGet(m_Target, out PerceivedContact c), Is.True);
		Assert.That(c.LastSeenPosition, Is.EqualTo(observedPos));
		Assert.That(c.LastSeenPosition, Is.Not.EqualTo(m_Target.position));
		Assert.That(c.ObservationState, Is.EqualTo(ObservationState.Lost));
	}

	[Test]
	public void ContactCreated_OnlyWhenProgressGreaterThanZero()
	{
		var sim = new PerceivedContactLifecycleSimulator(acquireTimeSeconds: 0.35f);
		float now = 0f;

		sim.ApplyEvidence(m_Target, 1f, Vector3.zero, now);
		Assert.That(sim.TryGet(m_Target, out _), Is.False);

		now += 0.05f;
		sim.Advance(0.05f, now);
		Assert.That(sim.TryGet(m_Target, out PerceivedContact c), Is.True);
		Assert.That(c.DetectionProgress, Is.GreaterThan(0f));
	}

	[Test]
	public void DetectionAxis_StillDetectingWhileObserved_AtLowProgress()
	{
		var sim = new PerceivedContactLifecycleSimulator(acquireTimeSeconds: 2f);
		float now = 0f;
		sim.ApplyEvidence(m_Target, 1f, Vector3.zero, now);
		now += 0.05f;
		sim.Advance(0.05f, now);

		Assert.That(sim.TryGet(m_Target, out PerceivedContact c), Is.True);
		Assert.That(c.ObservationState, Is.EqualTo(ObservationState.Observed));
		Assert.That(c.State, Is.EqualTo(DetectionState.Detecting));
		Assert.That(c.DetectionProgress, Is.GreaterThan(0f).And.LessThan(1f));
	}
}
