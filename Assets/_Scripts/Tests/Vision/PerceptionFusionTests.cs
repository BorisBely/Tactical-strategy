using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class PerceptionFusionTests
	{
		private GameObject m_Observer;
		private GameObject m_Target;
		private DetectionProcessor m_Processor;
		private TargetSelector m_Selector;
		private EngagementDecisionController m_Engagement;

		[SetUp]
		public void SetUp()
		{
			m_Target = new GameObject("G7FusionTarget");
			m_Target.transform.position = new Vector3(5f, 0f, 0f);
			m_Observer = CreateObserver("G7FusionObserver");
			m_Processor = m_Observer.GetComponent<DetectionProcessor>();
			m_Selector = m_Observer.GetComponent<TargetSelector>();
			m_Engagement = m_Observer.GetComponent<EngagementDecisionController>();
			m_Processor.SetSimulatedTime(0f);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Observer != null)
				Object.DestroyImmediate(m_Observer);
			if (m_Target != null)
				Object.DestroyImmediate(m_Target);
		}

		[Test]
		public void SameTransform_OneContact_ThreeChannels()
		{
			Vector3 seen = new Vector3(5f, 0f, 0f);
			float now = Observe(seen, 20);

			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact observed), Is.True);
			Assert.AreEqual(ObservationState.Observed, observed.ObservationState);
			object contactRef = observed;

			Vector3 heard = seen + Vector3.right * 4f;
			m_Processor.SetSimulatedTime(now);
			m_Processor.ApplySyntheticSound(m_Target.transform, heard, 1f);
			m_Processor.ApplySyntheticShared(m_Target.transform, heard + Vector3.forward, 1f);
			now += 0.05f;
			m_Processor.Advance(0.05f, now);

			Assert.AreEqual(1, m_Processor.Contacts.Count);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact mixed), Is.True);
			Assert.AreSame(contactRef, mixed);
			Assert.IsTrue(mixed.HasVisualEvidence);
			Assert.IsTrue(mixed.HasSoundEvidence);
			Assert.IsTrue(mixed.HasSharedEvidence);
			Assert.IsTrue(mixed.EvidenceIsMixed);
			Assert.AreEqual(seen, mixed.LastSeenPosition);
			Assert.AreEqual(seen, mixed.LastKnownPosition);
			Assert.AreNotEqual(heard, mixed.LastKnownPosition);
			Assert.IsTrue(mixed.LastObservation.HasAimPoint);
			Assert.AreEqual(m_Target.transform, m_Selector.SelectedTarget);
			Assert.IsTrue(m_Selector.HasSelectedAimPoint);
		}

		[Test]
		public void HideThenSound_DoesNotCreateAimOrFire_AndVisionMemoryStillDecays()
		{
			Vector3 seen = new Vector3(5f, 0f, 1f);
			float now = Observe(seen, 16);
			m_Processor.ApplyEmptyObservationFrame();
			now += 0.25f;
			m_Processor.Advance(0.25f, now);

			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact hidden), Is.True);
			Assert.AreNotEqual(ObservationState.Observed, hidden.ObservationState);
			float memoryBefore = hidden.LastSeenConfidence;
			Assert.Less(memoryBefore, 1f);
			Assert.Greater(memoryBefore, 0f);
			Assert.IsFalse(m_Selector.HasSelectedAimPoint);
			Assert.AreEqual(EngagementDecision.Track, m_Engagement.CurrentDecision);

			Vector3 heard = seen + Vector3.forward * 3f;
			m_Processor.SetSimulatedTime(now);
			m_Processor.ApplySyntheticSound(m_Target.transform, heard, 1f);
			now += 0.2f;
			m_Processor.Advance(0.2f, now);

			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact afterSound), Is.True);
			Assert.AreEqual(1, m_Processor.Contacts.Count);
			Assert.AreNotEqual(ObservationState.Observed, afterSound.ObservationState);
			Assert.IsTrue(afterSound.HasSoundEvidence);
			Assert.AreEqual(seen, afterSound.LastSeenPosition);
			Assert.Less(afterSound.LastSeenConfidence, memoryBefore + 0.0001f);
			Assert.AreNotEqual(1f, afterSound.LastSeenConfidence);
			Assert.AreEqual(seen, afterSound.LastKnownPosition);
			Assert.AreEqual(heard, afterSound.SoundPosition);
			Assert.IsFalse(m_Selector.HasSelectedAimPoint);
			Assert.AreEqual(EngagementDecision.Track, m_Engagement.CurrentDecision);
			Assert.AreNotEqual(EngagementDecision.Fire, m_Engagement.CurrentDecision);
			Assert.IsNull(m_Selector.GetEngageableSelectedTarget());
		}

		[Test]
		public void SoundDoesNotPretendToBeVisionObservation()
		{
			m_Processor.ApplySyntheticSound(m_Target.transform, m_Target.transform.position, 1f);
			m_Processor.Advance(0.05f, 0.05f);
			UnitPerception perception = m_Observer.GetComponent<UnitPerception>();
			Assert.AreEqual(0, perception.ObservationCount);
			Assert.AreEqual(1, perception.SoundEvents.Count);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
			Assert.IsFalse(contact.LastObservation.IsVisible);
			Assert.IsFalse(contact.LastObservation.HasAimPoint);
		}

		private float Observe(Vector3 _position, int _ticks)
		{
			float now = 0f;
			for (int i = 0; i < _ticks; i++)
			{
				m_Processor.ApplySyntheticObservation(m_Target.transform, 4f, 0f, 1f, _position);
				now += 0.05f;
				m_Processor.Advance(0.05f, now);
			}

			return now;
		}

		private static GameObject CreateObserver(string _name)
		{
			var go = new GameObject(_name);
			go.AddComponent<UnitObservationSource>();
			go.AddComponent<UnitPerception>();
			if (go.GetComponent<DetectionProcessor>() == null)
				go.AddComponent<DetectionProcessor>();
			if (go.GetComponent<TargetSelector>() == null)
				go.AddComponent<TargetSelector>();
			if (go.GetComponent<EngagementDecisionController>() == null)
				go.AddComponent<EngagementDecisionController>();
			return go;
		}
	}
}
