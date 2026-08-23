using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class SoundPerceptionTests
	{
		private GameObject m_Observer;
		private GameObject m_Target;
		private DetectionProcessor m_Processor;
		private TargetSelector m_Selector;
		private UnitPerception m_Perception;

		[SetUp]
		public void SetUp()
		{
			m_Target = new GameObject("G7SoundTarget");
			m_Target.transform.position = new Vector3(6f, 0f, 0f);
			m_Observer = CreateObserver("G7SoundObserver");
			m_Processor = m_Observer.GetComponent<DetectionProcessor>();
			m_Selector = m_Observer.GetComponent<TargetSelector>();
			m_Perception = m_Observer.GetComponent<UnitPerception>();
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
		public void NullSource_DoesNotCreateGhostContact()
		{
			m_Perception.ApplySoundEvents(new[]
			{
				new SoundObservation
				{
					Source = null,
					Position = Vector3.one,
					SourceConfidence = 1f,
					Type = SoundEventType.Gunshot,
					Time = 0f
				}
			});
			m_Processor.Advance(0.05f, 0.05f);
			Assert.AreEqual(0, m_Processor.Contacts.Count);
		}

		[Test]
		public void Sound_CreatesContactWithoutVision()
		{
			Vector3 heard = new Vector3(6f, 0f, 1f);
			m_Processor.ApplySyntheticSound(m_Target.transform, heard, 1f);
			m_Processor.Advance(0.05f, 0.05f);

			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.AreEqual(0f, contact.LastSeenConfidence, 0.0001f);
			Assert.AreEqual(0f, contact.DetectionProgress, 0.0001f);
			Assert.IsFalse(contact.LastObservation.HasAimPoint);
			Assert.AreEqual(heard, contact.SoundPosition);
			Assert.AreNotEqual(heard, contact.LastKnownPosition);
			Assert.AreEqual(Vector3.zero, contact.LastKnownPosition);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.IsTrue(contact.HasSoundEvidence);
			Assert.IsFalse(contact.HasVisualEvidence);
			Assert.AreEqual(0, m_Perception.ObservationCount);
			Assert.AreEqual(m_Target.transform, m_Selector.SelectedTarget);
			Assert.IsFalse(m_Selector.HasSelectedAimPoint);
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(contact, out _));
		}

		[Test]
		public void SoundConfidence_DecaysIndependently_ThenForgotten()
		{
			m_Processor.ApplySyntheticSound(m_Target.transform, m_Target.transform.position, 1f);
			m_Processor.Advance(0.05f, 0.05f);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact start), Is.True);
			float startConf = start.SoundConfidence;
			Assert.Greater(startConf, 0.8f);

			float horizon = m_Processor.SoundHorizonSeconds;
			m_Processor.Advance(horizon * 0.5f, 0.05f + horizon * 0.5f);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact mid), Is.True);
			Assert.Less(mid.SoundConfidence, startConf);
			Assert.Greater(mid.SoundConfidence, 0f);
			Assert.AreEqual(0f, mid.LastSeenConfidence, 0.0001f);
			Assert.AreEqual(m_Target.transform, m_Selector.SelectedTarget);

			m_Processor.Advance(horizon, 0.05f + horizon * 0.5f + horizon);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact gone), Is.True);
			Assert.AreEqual(0f, gone.SoundConfidence, 0.0001f);
			Assert.IsFalse(gone.HasKnowledge);
			Assert.IsNull(m_Selector.SelectedTarget);
		}

		[Test]
		public void SoundMath_HorizonIsZero()
		{
			Assert.AreEqual(1f, SoundKnowledgeMath.Evaluate(0f, 1f), 0.0001f);
			Assert.AreEqual(0f, SoundKnowledgeMath.Evaluate(SoundKnowledgeMath.DefaultHorizonSeconds, 1f), 0.0001f);
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
