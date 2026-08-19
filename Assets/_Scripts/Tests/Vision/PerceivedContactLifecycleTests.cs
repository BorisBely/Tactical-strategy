using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// Legacy-named suite kept for asmdef discovery; mirrors EditMode PerceivedContactLifecycleTests.
	/// </summary>
	public sealed class PerceivedContactLifecycleTests
	{
		private GameObject m_Root;
		private Transform m_Target;

		[SetUp]
		public void SetUp()
		{
			m_Root = new GameObject("VisionLifecycleRoot");
			m_Target = new GameObject("VisionLifecycleTarget").transform;
			m_Target.SetParent(m_Root.transform);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Root != null)
				Object.DestroyImmediate(m_Root);
		}

		[Test]
		public void Lifecycle_Evidence_DetectingThenDetected_ThenRecentlyLost_ThenLost()
		{
			var sim = new PerceivedContactLifecycleSimulator(
				acquireTimeSeconds: 0.2f,
				recentlyLostDurationSeconds: 1f);

			float t = 0f;
			sim.ApplyEvidence(m_Target, 1f, Vector3.forward * 10f, t);
			Assert.That(sim.TryGet(m_Target, out _), Is.False);

			for (int i = 0; i < 20; i++)
			{
				t += 0.05f;
				sim.Advance(0.05f, t);
			}

			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(DetectionState.Detected, contact.State);
			Assert.AreEqual(ObservationState.Observed, contact.ObservationState);

			VisionObservation last = contact.LastObservation;
			Vector3 lastPos = contact.LastSeenPosition;

			sim.SoftLose(m_Target, t);
			Assert.That(sim.TryGet(m_Target, out contact), Is.True);
			Assert.AreEqual(ObservationState.RecentlyLost, contact.ObservationState);
			Assert.AreEqual(DetectionState.Detected, contact.State);
			Assert.AreEqual(last.AimPoint, contact.LastObservation.AimPoint);
			Assert.AreEqual(lastPos, contact.LastSeenPosition);

			t += 0.5f;
			sim.Advance(0.5f, t);
			Assert.AreEqual(ObservationState.RecentlyLost, contact.ObservationState);

			t += 0.6f;
			sim.Advance(0.6f, t);
			Assert.AreEqual(ObservationState.Lost, contact.ObservationState);
			Assert.AreEqual(lastPos, contact.LastSeenPosition);
		}

		[Test]
		public void Lifecycle_ProgressDecaysWhileRecentlyLost()
		{
			var sim = new PerceivedContactLifecycleSimulator(acquireTimeSeconds: 0.15f);
			float t = 0f;
			sim.ApplyEvidence(m_Target, 1f, Vector3.zero, t);
			for (int i = 0; i < 30; i++)
			{
				t += 0.05f;
				sim.Advance(0.05f, t);
			}

			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(DetectionState.Detected, contact.State);
			float before = contact.DetectionProgress;

			sim.SoftLose(m_Target, t);
			for (int i = 0; i < 10; i++)
			{
				t += 0.1f;
				sim.Advance(0.1f, t);
			}

			Assert.That(sim.TryGet(m_Target, out contact), Is.True);
			Assert.Less(contact.DetectionProgress, before);
			Assert.AreEqual(ObservationState.RecentlyLost, contact.ObservationState);
		}

		[Test]
		public void Lifecycle_DetectedPlusRecentlyLost_IsValid()
		{
			var sim = new PerceivedContactLifecycleSimulator(acquireTimeSeconds: 0.1f);
			float t = 0f;
			sim.ApplyEvidence(m_Target, 1f, Vector3.one, t);
			for (int i = 0; i < 25; i++)
			{
				t += 0.05f;
				sim.Advance(0.05f, t);
			}

			sim.SoftLose(m_Target, t);
			Assert.That(sim.TryGet(m_Target, out PerceivedContact contact), Is.True);
			Assert.AreEqual(DetectionState.Detected, contact.State);
			Assert.AreEqual(ObservationState.RecentlyLost, contact.ObservationState);
		}
	}
}
