using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class TargetSelectorContactTests
	{
		private GameObject m_ObserverA;
		private GameObject m_ObserverB;
		private GameObject m_Target;

		[SetUp]
		public void SetUp()
		{
			m_Target = new GameObject("G5SelectTarget");
			m_Target.transform.position = new Vector3(4f, 0f, 0f);
			m_ObserverA = CreateObserver("G5ObserverA");
			m_ObserverB = CreateObserver("G5ObserverB");
		}

		[TearDown]
		public void TearDown()
		{
			if (m_ObserverA != null)
				Object.DestroyImmediate(m_ObserverA);
			if (m_ObserverB != null)
				Object.DestroyImmediate(m_ObserverB);
			if (m_Target != null)
				Object.DestroyImmediate(m_Target);
		}

		[Test]
		public void UnknownObservedContact_IsSelected()
		{
			DetectionProcessor processor = m_ObserverA.GetComponent<DetectionProcessor>();
			TargetSelector selector = m_ObserverA.GetComponent<TargetSelector>();
			Observe(processor, 20, m_Target.transform.position);

			Assert.AreEqual(m_Target.transform, selector.SelectedTarget);
			Assert.IsTrue(selector.HasSelectedAimPoint);
			Assert.AreEqual(m_Target.transform, selector.GetEngageableSelectedTarget());
		}

		[Test]
		public void FriendlyContact_IsNotSelected()
		{
			DetectionProcessor processor = m_ObserverA.GetComponent<DetectionProcessor>();
			TargetSelector selector = m_ObserverA.GetComponent<TargetSelector>();
			processor.SetAffiliationCue(m_Target.transform, ObservableAffiliation.Friendly);
			Observe(processor, 50, m_Target.transform.position);

			Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedIdentity.Friendly, contact.Identity);
			Assert.IsNull(selector.SelectedTarget);
		}

		[Test]
		public void ForgottenContact_IsNotSelected()
		{
			DetectionProcessor processor = m_ObserverA.GetComponent<DetectionProcessor>();
			TargetSelector selector = m_ObserverA.GetComponent<TargetSelector>();
			float now = Observe(processor, 20, m_Target.transform.position);
			processor.ApplyEmptyObservationFrame();
			processor.Advance(MemoryDecayMath.DefaultHorizonSeconds + 0.2f, now + MemoryDecayMath.DefaultHorizonSeconds + 0.2f);

			Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(0f, contact.LastSeenConfidence, 0.0001f);
			Assert.IsNull(selector.SelectedTarget);
		}

		[Test]
		public void RecentlyLost_RemainsSelected_WithoutEngageableAim()
		{
			DetectionProcessor processor = m_ObserverA.GetComponent<DetectionProcessor>();
			TargetSelector selector = m_ObserverA.GetComponent<TargetSelector>();
			float now = Observe(processor, 20, m_Target.transform.position);
			Assert.AreEqual(m_Target.transform, selector.SelectedTarget);

			processor.ApplyEmptyObservationFrame();
			processor.Advance(0.25f, now + 0.25f);

			Assert.AreEqual(m_Target.transform, selector.SelectedTarget);
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.IsNull(selector.GetEngageableSelectedTarget());
			Assert.AreEqual(Vector3.zero, selector.GetEngageableAimPointWorld());
		}

		[Test]
		public void LastKnown_IsNotCombatAim()
		{
			DetectionProcessor processor = m_ObserverA.GetComponent<DetectionProcessor>();
			TargetSelector selector = m_ObserverA.GetComponent<TargetSelector>();
			Vector3 seen = new Vector3(4f, 0f, 1f);
			float now = Observe(processor, 12, seen);
			processor.ApplyEmptyObservationFrame();
			processor.Advance(1f, now + 1f);

			Assert.That(processor.TryGetContact(m_Target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(seen, contact.LastKnownPosition);
			Assert.AreEqual(m_Target.transform, selector.SelectedTarget);
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.AreNotEqual(contact.LastKnownPosition, selector.SelectedAimPointWorld);
		}

		[Test]
		public void DualObservers_IndependentSelection()
		{
			DetectionProcessor a = m_ObserverA.GetComponent<DetectionProcessor>();
			DetectionProcessor b = m_ObserverB.GetComponent<DetectionProcessor>();
			TargetSelector selectorA = m_ObserverA.GetComponent<TargetSelector>();
			TargetSelector selectorB = m_ObserverB.GetComponent<TargetSelector>();

			Observe(a, 20, m_Target.transform.position);
			b.Advance(1f, 1f);

			Assert.AreEqual(m_Target.transform, selectorA.SelectedTarget);
			Assert.IsNull(selectorB.SelectedTarget);
		}

		[Test]
		public void ForcedPriority_WithoutContact_IsIgnored()
		{
			DetectionProcessor processor = m_ObserverA.GetComponent<DetectionProcessor>();
			TargetSelector selector = m_ObserverA.GetComponent<TargetSelector>();
			var dummy = new GameObject("G5ForcedDummy");
			try
			{
				Observe(processor, 16, m_Target.transform.position);
				selector.ForcedPriorityTarget = dummy.transform;
				processor.Advance(0.05f, 2f);
				Assert.AreNotEqual(dummy.transform, selector.SelectedTarget);
				Assert.AreEqual(m_Target.transform, selector.SelectedTarget);
			}
			finally
			{
				Object.DestroyImmediate(dummy);
			}
		}

		[Test]
		public void ClearContacts_Deselects()
		{
			DetectionProcessor processor = m_ObserverA.GetComponent<DetectionProcessor>();
			TargetSelector selector = m_ObserverA.GetComponent<TargetSelector>();
			Observe(processor, 16, m_Target.transform.position);
			Assert.AreEqual(m_Target.transform, selector.SelectedTarget);
			processor.ClearContacts();
			Assert.IsNull(selector.SelectedTarget);
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
			return go;
		}

		private float Observe(DetectionProcessor _processor, int _ticks, Vector3 _position)
		{
			float now = 0f;
			for (int i = 0; i < _ticks; i++)
			{
				_processor.ApplySyntheticObservation(m_Target.transform, 4f, 0f, 1f, _position);
				now += 0.05f;
				_processor.Advance(0.05f, now);
			}

			return now;
		}
	}
}
