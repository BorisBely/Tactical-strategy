using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class SharedPerceptionTests
	{
		private GameObject m_Observer;
		private GameObject m_Target;
		private GameObject m_Reporter;
		private DetectionProcessor m_Processor;
		private TargetSelector m_Selector;

		[SetUp]
		public void SetUp()
		{
			m_Target = new GameObject("G7SharedTarget");
			m_Target.transform.position = new Vector3(8f, 0f, 0f);
			m_Reporter = new GameObject("G7SharedReporter");
			m_Observer = CreateObserver("G7SharedObserver");
			m_Processor = m_Observer.GetComponent<DetectionProcessor>();
			m_Selector = m_Observer.GetComponent<TargetSelector>();
			m_Processor.SetSimulatedTime(0f);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Observer != null)
				Object.DestroyImmediate(m_Observer);
			if (m_Target != null)
				Object.DestroyImmediate(m_Target);
			if (m_Reporter != null)
				Object.DestroyImmediate(m_Reporter);
		}

		[Test]
		public void NullSubject_DoesNotCreateGhostContact()
		{
			m_Observer.GetComponent<UnitPerception>().ApplySharedEvents(new[]
			{
				new SharedObservation
				{
					Subject = null,
					SourceUnit = m_Reporter.transform,
					Position = Vector3.one,
					SourceConfidence = 1f,
					InformationType = SharedInformationType.ContactReport,
					Time = 0f
				}
			});
			m_Processor.Advance(0.05f, 0.05f);
			Assert.AreEqual(0, m_Processor.Contacts.Count);
		}

		[Test]
		public void Shared_CreatesContactWithoutVision()
		{
			Vector3 reported = new Vector3(8f, 0f, 2f);
			m_Processor.ApplySyntheticShared(m_Target.transform, reported, 1f, m_Reporter.transform);
			m_Processor.Advance(0.05f, 0.05f);

			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.AreEqual(0f, contact.LastSeenConfidence, 0.0001f);
			Assert.IsFalse(contact.HasVisualEvidence);
			Assert.IsTrue(contact.HasSharedEvidence);
			Assert.AreEqual(reported, contact.SharedPosition);
			Assert.AreEqual(reported, contact.LastKnownPosition);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(m_Target.transform, m_Selector.SelectedTarget);
			Assert.IsFalse(m_Selector.HasSelectedAimPoint);
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(contact, out _));
		}

		[Test]
		public void SharedConfidence_DecaysIndependently()
		{
			m_Processor.ApplySyntheticShared(m_Target.transform, m_Target.transform.position, 1f);
			m_Processor.Advance(0.05f, 0.05f);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact start), Is.True);
			float startConf = start.SharedConfidence;

			float horizon = m_Processor.SharedHorizonSeconds;
			m_Processor.Advance(horizon * 0.4f, 0.05f + horizon * 0.4f);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact mid), Is.True);
			Assert.Less(mid.SharedConfidence, startConf);
			Assert.Greater(mid.SharedConfidence, 0f);
			Assert.AreEqual(0f, mid.LastSeenConfidence, 0.0001f);
			Assert.AreEqual(m_Target.transform, m_Selector.SelectedTarget);

			m_Processor.Advance(horizon, 0.05f + horizon * 0.4f + horizon);
			Assert.That(m_Processor.TryGetContact(m_Target.transform, out PerceivedContact gone), Is.True);
			Assert.AreEqual(0f, gone.SharedConfidence, 0.0001f);
			Assert.IsNull(m_Selector.SelectedTarget);
		}

		[Test]
		public void SharedMath_HorizonIsZero()
		{
			Assert.AreEqual(1f, SharedKnowledgeMath.Evaluate(0f, 1f), 0.0001f);
			Assert.AreEqual(0f, SharedKnowledgeMath.Evaluate(SharedKnowledgeMath.DefaultHorizonSeconds, 1f), 0.0001f);
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
