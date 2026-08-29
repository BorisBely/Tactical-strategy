using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// #12 Target + Fire Calibration. Selection ≠ Fire. Does not retune G5 Score, G6, RoE, A10.
	/// </summary>
	public sealed class TargetCalibrationTests
	{
		#region Constants
		/// <summary>Keep SphereCast / scene colliders away from infantry at world origin.</summary>
		private static readonly Vector3 c_FixtureOrigin = new Vector3(4000f, 0f, 4000f);
		#endregion

		#region Private Fields
		private GameObject m_Observer;
		private GameObject m_TargetA;
		private GameObject m_TargetB;
		private GameObject m_TargetC;
		#endregion

		#region Unity Lifecycle
		[SetUp]
		public void SetUp()
		{
			m_TargetA = new GameObject("TCal_A");
			m_TargetA.transform.position = c_FixtureOrigin + new Vector3(8f, 0f, 0f);
			m_TargetB = new GameObject("TCal_B");
			m_TargetB.transform.position = c_FixtureOrigin + new Vector3(7f, 0f, 0f);
			m_TargetC = new GameObject("TCal_C");
			m_TargetC.transform.position = c_FixtureOrigin + new Vector3(12f, 0f, 4f);
			m_Observer = CreateObserver();
			m_Observer.transform.SetPositionAndRotation(
				c_FixtureOrigin,
				Quaternion.LookRotation(Vector3.right));
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Observer != null)
				Object.DestroyImmediate(m_Observer);
			if (m_TargetA != null)
				Object.DestroyImmediate(m_TargetA);
			if (m_TargetB != null)
				Object.DestroyImmediate(m_TargetB);
			if (m_TargetC != null)
				Object.DestroyImmediate(m_TargetC);
		}
		#endregion

		#region A Deterministic
		[Test]
		public void A1_Score_IsDeterministic()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			PerceivedContact contact = Observed(m_TargetA.transform, new Vector3(10f, 0f, 0f), ThreatLevel.Medium);
			float first = TargetSelectionMath.Score(contact, Vector3.zero, policy);
			float second = TargetSelectionMath.Score(contact, Vector3.zero, policy);
			Assert.AreEqual(first, second);
		}

		[Test]
		public void A2_Selector_SameState_SameSelected()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			Observe(processor, m_TargetA.transform, m_TargetA.transform.position, 20);
			AssertSelected(m_TargetA.transform);
			Transform first = selector.SelectedTarget;
			selector.SelectFromContacts();
			Assert.AreEqual(first, selector.SelectedTarget);
		}
		#endregion

		#region B Stable target
		[Test]
		public void B1_SlightlyBetter_HysteresisHolds()
		{
			bool switched = TargetSwitchMath.ShouldSwitch(
				m_TargetA.transform,
				true,
				10f,
				m_TargetB.transform,
				10.2f,
				TargetSwitchMath.DefaultSwitchThreshold,
				out TargetSwitchReason reason);
			Assert.IsFalse(switched);
			Assert.AreEqual(TargetSwitchReason.Hysteresis, reason);
		}

		[Test]
		public void B2_Selector_SlightlyCloser_RemainsCurrent()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			Observe(processor, m_TargetA.transform, m_TargetA.transform.position, 20);
			AssertSelected(m_TargetA.transform);

			ObserveTwo(
				processor,
				m_TargetA.transform, m_TargetA.transform.position, PlanarDistance(m_TargetA.transform),
				m_TargetB.transform, m_TargetB.transform.position, PlanarDistance(m_TargetB.transform),
				12);
			AssertSelected(m_TargetA.transform);
			Assert.AreEqual(TargetSwitchReason.Hysteresis, selector.LastSelection.SwitchReason);
			Assert.IsFalse(selector.LastSelection.Switched);
		}
		#endregion

		#region C Meaningful switch
		[Test]
		public void C1_SignificantlyBetter_Switches()
		{
			bool switched = TargetSwitchMath.ShouldSwitch(
				m_TargetA.transform,
				true,
				10f,
				m_TargetB.transform,
				11.2f,
				TargetSwitchMath.DefaultSwitchThreshold,
				out TargetSwitchReason reason);
			Assert.IsTrue(switched);
			Assert.AreEqual(TargetSwitchReason.HigherScore, reason);
		}

		[Test]
		public void C2_Selector_HighThreat_Switches()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			Observe(processor, m_TargetA.transform, m_TargetA.transform.position, 20);
			float sharedDist = PlanarDistance(m_TargetA.transform);
			ObserveTwo(
				processor,
				m_TargetA.transform, m_TargetA.transform.position, sharedDist,
				m_TargetB.transform, m_TargetB.transform.position, sharedDist,
				12);
			AssertSelected(m_TargetA.transform);

			Stamp(processor, m_TargetB.transform, ThreatLevel.High, PerceivedIdentity.Hostile);
			selector.SelectFromContacts();
			AssertSelected(m_TargetB.transform);
			Assert.IsTrue(selector.LastSelection.Switched);
			Assert.AreEqual(TargetSwitchReason.HigherScore, selector.LastSelection.SwitchReason);
		}
		#endregion

		#region D Lost target
		[Test]
		public void D1_LostCurrent_SwitchesToValid()
		{
			bool switched = TargetSwitchMath.ShouldSwitch(
				m_TargetA.transform,
				false,
				4f,
				m_TargetB.transform,
				12f,
				TargetSwitchMath.DefaultSwitchThreshold,
				out TargetSwitchReason reason);
			Assert.IsTrue(switched);
			Assert.AreEqual(TargetSwitchReason.LostCurrent, reason);
		}

		[Test]
		public void D2_Selector_ForgottenCurrent_SelectsRemaining()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			Observe(processor, m_TargetA.transform, m_TargetA.transform.position, 16);
			ObserveTwo(
				processor,
				m_TargetA.transform, m_TargetA.transform.position, PlanarDistance(m_TargetA.transform),
				m_TargetB.transform, m_TargetB.transform.position, PlanarDistance(m_TargetB.transform),
				8);
			AssertSelected(m_TargetA.transform);

			UnitPerception perception = m_Observer.GetComponent<UnitPerception>();
			float now = processor.PerceptionClock;
			perception.ApplyVisionFrame(new[]
			{
				Obs(m_TargetB.transform, m_TargetB.transform.position, PlanarDistance(m_TargetB.transform))
			});
			now += MemoryDecayMath.DefaultHorizonSeconds + 0.5f;
			processor.Advance(MemoryDecayMath.DefaultHorizonSeconds + 0.5f, now);

			TargetSwitchReason switchReason = selector.LastSelection.SwitchReason;
			if (selector.SelectedTarget != m_TargetB.transform)
			{
				selector.SelectFromContacts();
				switchReason = selector.LastSelection.SwitchReason;
			}

			AssertSelected(m_TargetB.transform);
			Assert.That(
				switchReason == TargetSwitchReason.LostCurrent ||
				switchReason == TargetSwitchReason.HigherScore ||
				switchReason == TargetSwitchReason.InitialSelect,
				Is.True,
				switchReason.ToString());
		}
		#endregion

		#region E No LOS
		[Test]
		public void E1_SelectedWithoutAimPoint_IsTrackNotEngageable()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			EngagementDecisionController engagement = m_Observer.GetComponent<EngagementDecisionController>();
			float now = Observe(processor, m_TargetA.transform, m_TargetA.transform.position, 20);
			AssertSelected(m_TargetA.transform);
			Assert.IsTrue(selector.HasSelectedAimPoint);

			processor.ApplyEmptyObservationFrame();
			processor.Advance(0.25f, now + 0.25f);
			selector.SelectFromContacts();
			AssertSelected(m_TargetA.transform);
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.IsNull(selector.GetEngageableSelectedTarget());
			engagement.RefreshDecisionNow();
			Assert.AreEqual(EngagementDecision.Track, engagement.CurrentDecision);
		}
		#endregion

		#region F Memory
		[Test]
		public void F1_MemoryOnly_NeverFire()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			EngagementDecisionController engagement = m_Observer.GetComponent<EngagementDecisionController>();
			Vector3 seen = m_TargetA.transform.position;
			float now = Observe(processor, m_TargetA.transform, seen, 16);
			processor.ApplyEmptyObservationFrame();
			processor.Advance(1f, now + 1f);
			selector.SelectFromContacts();

			AssertSelected(m_TargetA.transform);
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.That(processor.TryGetContact(m_TargetA.transform, out PerceivedContact contact), Is.True);
			Assert.AreNotEqual(contact.LastKnownPosition, selector.SelectedAimPointWorld);
			engagement.RefreshDecisionNow();
			Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
			Assert.AreNotEqual(EngagementDecision.Aim, engagement.CurrentDecision);
			Assert.AreEqual(EngagementDecision.Track, engagement.CurrentDecision);
		}
		#endregion

		#region G Unknown
		[Test]
		public void G1_Unknown_MayBeSelected()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			Observe(processor, m_TargetA.transform, m_TargetA.transform.position, 20);
			AssertSelected(m_TargetA.transform);
			Assert.That(processor.TryGetContact(m_TargetA.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
		}
		#endregion

		#region H Friendly
		[Test]
		public void H1_Friendly_NeverSelected()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			processor.SetAffiliationCue(m_TargetA.transform, ObservableAffiliation.Friendly);
			Observe(processor, m_TargetA.transform, m_TargetA.transform.position, 50);
			Assert.That(processor.TryGetContact(m_TargetA.transform, out _), Is.True, "friendly must still be a contact");
			Assert.IsNull(selector.SelectedTarget);
		}
		#endregion

		#region I Mission
		[Test]
		public void I1_MissionTarget_PreferredWhenScoresClose()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			m_TargetB.transform.position = c_FixtureOrigin + new Vector3(7f, 0f, 0f);
			selector.MissionTarget = m_TargetA.transform;
			ObserveTwo(
				processor,
				m_TargetA.transform, m_TargetA.transform.position, PlanarDistance(m_TargetA.transform),
				m_TargetB.transform, m_TargetB.transform.position, PlanarDistance(m_TargetB.transform),
				16);
			AssertSelected(m_TargetA.transform);
		}

		[Test]
		public void I2_IncidentalHighHostile_BeatsMission()
		{
			DetectionProcessor processor = Processor();
			TargetSelector selector = Selector();
			m_TargetB.transform.position = c_FixtureOrigin + new Vector3(8f, 0f, 1f);
			selector.MissionTarget = m_TargetA.transform;
			float sharedDist = PlanarDistance(m_TargetA.transform);
			ObserveTwo(
				processor,
				m_TargetA.transform, m_TargetA.transform.position, sharedDist,
				m_TargetB.transform, m_TargetB.transform.position, sharedDist,
				16);
			AssertSelected(m_TargetA.transform);

			Stamp(processor, m_TargetB.transform, ThreatLevel.High, PerceivedIdentity.Hostile);
			selector.SelectFromContacts();
			AssertSelected(m_TargetB.transform);
			Assert.AreEqual(TargetSwitchReason.HigherScore, selector.LastSelection.SwitchReason);
		}
		#endregion

		#region J AI mismatch
		[Test]
		public void J1_AiCombatMismatch_IsDiagnostic_NotMerged()
		{
			UnitAIController ai = m_Observer.AddComponent<UnitAIController>();
			ai.TryApplyCommand(
				UnitAICommand.Defense(UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
			ai.SetPerceptionFrame(HostileVisible(m_TargetA.transform));
			ai.Tick(0.05f);

			TargetSelector selector = Selector();
			EngagementDecisionController engagement = m_Observer.GetComponent<EngagementDecisionController>();
			selector.SetSelectedTargetForDiagnostics(m_TargetB.transform, m_TargetB.transform.position);
			engagement.RefreshDecisionNow();

			Assert.AreEqual(m_TargetA.transform, ai.CurrentEngageTarget);
			Assert.AreEqual(m_TargetB.transform, selector.SelectedTarget);
			Assert.IsTrue(engagement.EngageTargetMismatch);
			Assert.AreEqual(TargetCombatMismatch.Explanation, engagement.EngageTargetMismatchReason);
			engagement.RefreshDecisionNow();
			Assert.AreEqual(m_TargetB.transform, selector.SelectedTarget);
			Assert.AreEqual(m_TargetA.transform, ai.CurrentEngageTarget);
		}
		#endregion

		#region Weapon / Threat ≠ Fire
		[Test]
		public void Weapon_SniperBonus_PrefersFartherAmongEquals()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var near = Observed(m_TargetA.transform, new Vector3(40f, 0f, 0f), ThreatLevel.None);
			var far = Observed(m_TargetB.transform, new Vector3(80f, 0f, 0f), ThreatLevel.None);
			float nearScore = TargetSelectionMath.ScoreWithModifiers(
				near, Vector3.zero, policy, WeaponClassType.SniperRifle, 400f, null);
			float farScore = TargetSelectionMath.ScoreWithModifiers(
				far, Vector3.zero, policy, WeaponClassType.SniperRifle, 400f, null);
			Assert.Greater(farScore, nearScore);
		}

		[Test]
		public void Weapon_ShotgunBonus_PrefersNear()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var near = Observed(m_TargetA.transform, new Vector3(5f, 0f, 0f), ThreatLevel.None);
			var far = Observed(m_TargetB.transform, new Vector3(40f, 0f, 0f), ThreatLevel.None);
			float nearScore = TargetSelectionMath.ScoreWithModifiers(
				near, Vector3.zero, policy, WeaponClassType.Shotgun, 40f, null);
			float farScore = TargetSelectionMath.ScoreWithModifiers(
				far, Vector3.zero, policy, WeaponClassType.Shotgun, 40f, null);
			Assert.Greater(nearScore, farScore);
		}

		[Test]
		public void Threat_High_DoesNotForceFireWithoutLos()
		{
			EngagementDecisionContext ctx = new EngagementDecisionContext
			{
				HasSelectedTarget = true,
				HasContact = true,
				Identity = PerceivedIdentity.Hostile,
				Relationship = PerceivedRelationship.Hostile,
				Threat = ThreatLevel.High,
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 0.8f,
				HasKnowledge = true,
				IsWorldEngageable = true,
				HasLosConfirmedAim = false,
				WeaponCanFireEventually = true,
				AimReadyToFire = true
			};
			Assert.AreEqual(EngagementDecision.Track, EngagementDecisionMath.Evaluate(ctx));
			Assert.AreNotEqual(EngagementDecision.Fire, EngagementDecisionMath.Evaluate(ctx));
		}
		#endregion

		#region Private Methods
		private DetectionProcessor Processor()
		{
			return m_Observer.GetComponent<DetectionProcessor>();
		}

		private TargetSelector Selector()
		{
			return m_Observer.GetComponent<TargetSelector>();
		}

		private static GameObject CreateObserver()
		{
			var go = new GameObject("TCal_Observer");
			go.SetActive(false);
			go.AddComponent<UnitObservationSource>();
			go.AddComponent<UnitPerception>();
			if (go.GetComponent<DetectionProcessor>() == null)
				go.AddComponent<DetectionProcessor>();
			if (go.GetComponent<TargetSelector>() == null)
				go.AddComponent<TargetSelector>();
			if (go.GetComponent<EngagementDecisionController>() == null)
				go.AddComponent<EngagementDecisionController>();
			go.SetActive(true);
			go.GetComponent<DetectionProcessor>().SetSimulatedTime(0f);
			go.GetComponent<TargetSelector>().SetLineOfFireLayerMaskForDiagnostics(0);
			return go;
		}

		private void AssertSelected(Transform _expected)
		{
			TargetSelector selector = Selector();
			DetectionProcessor processor = Processor();
			string extra = string.Empty;
			bool hasExpectedContact = false;
			if (_expected != null && processor.TryGetContact(_expected, out PerceivedContact contact) && contact != null)
			{
				hasExpectedContact = true;
				bool worldOk = TargetEngageability.IsEngageable(_expected);
				ContactSelectionEligibility.Evaluate(
					contact, worldOk, ContactSelectionPolicy.CreateDefault(), out ContactSelectionRejectReason reject);
				extra = " know=" + contact.HasKnowledge +
				        " id=" + contact.Identity +
				        " rel=" + contact.Relationship +
				        " obs=" + contact.ObservationState +
				        " reject=" + reject;
			}

			Assert.IsTrue(
				selector.SelectedTarget == _expected,
				"selected=" + Slot(selector.SelectedTarget) +
				" expected=" + Slot(_expected) +
				" contacts=" + processor.Contacts.Count +
				" hasExpectedContact=" + hasExpectedContact +
				" reason=" + selector.LastSelection.SwitchReason +
				" score=" + selector.LastSelection.SelectedScore.ToString("0.00") +
				" scored=" + selector.LastSelection.ScoredCount +
				" registry=" + selector.LastSelection.RegistryCount +
				" skip=" + selector.LastSelection.RejectSummary +
				extra);
		}

		private float PlanarDistance(Transform _target)
		{
			Vector3 from = m_Observer.transform.position;
			Vector3 to = _target.position;
			from.y = 0f;
			to.y = 0f;
			return Mathf.Max(0.01f, Vector3.Distance(from, to));
		}

		private static string Slot(Transform _t)
		{
			return _t != null ? _t.name : "null";
		}

		private static PerceivedContact Observed(Transform _target, Vector3 _position, ThreatLevel _threat)
		{
			return new PerceivedContact
			{
				Target = _target,
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				LastKnownPosition = _position,
				Threat = _threat,
				LastObservation = new VisionObservation
				{
					Target = _target,
					Position = _position,
					AimPoint = _position + Vector3.up * 1.2f,
					HasAimPoint = true,
					IsVisible = true,
					DistanceSq = _position.sqrMagnitude
				}
			};
		}

		private static void Stamp(
			DetectionProcessor _processor,
			Transform _target,
			ThreatLevel _threat,
			PerceivedIdentity _identity)
		{
			Assert.That(_processor.TryGetContact(_target, out PerceivedContact contact), Is.True);
			contact.Threat = _threat;
			contact.Identity = _identity;
			if (_identity == PerceivedIdentity.Hostile)
				contact.Relationship = PerceivedRelationship.Hostile;
		}

		private float Observe(DetectionProcessor _processor, Transform _target, Vector3 _position, int _ticks)
		{
			float now = _processor.PerceptionClock;
			float dist = Mathf.Max(0.01f, PlanarDistance(_target));
			for (int i = 0; i < _ticks; i++)
			{
				_processor.ApplySyntheticObservation(_target, dist, 0f, 1f, _position);
				now += 0.05f;
				_processor.Advance(0.05f, now);
			}

			Selector().SelectFromContacts();
			return now;
		}

		private void ObserveTwo(
			DetectionProcessor _processor,
			Transform _a, Vector3 _aPos, float _aDist,
			Transform _b, Vector3 _bPos, float _bDist,
			int _ticks)
		{
			UnitPerception perception = m_Observer.GetComponent<UnitPerception>();
			_processor.ApplySyntheticObservation(_a, _aDist, 0f, 1f, _aPos);
			float now = _processor.PerceptionClock;
			for (int i = 0; i < _ticks; i++)
			{
				perception.ApplyVisionFrame(new[]
				{
					Obs(_a, _aPos, _aDist),
					Obs(_b, _bPos, _bDist)
				});
				now += 0.05f;
				_processor.Advance(0.05f, now);
			}

			Selector().SelectFromContacts();
		}

		private static VisionObservation Obs(Transform _target, Vector3 _position, float _distance)
		{
			float dist = Mathf.Max(0.01f, _distance);
			return new VisionObservation
			{
				Target = _target,
				Position = _position,
				AimPoint = _position + Vector3.up * 1.2f,
				HasAimPoint = true,
				DistanceSq = dist * dist,
				IsVisible = true,
				FovOffsetDegrees = 0f,
				Exposure01 = 1f
			};
		}

		private static AIPerceptionFrame HostileVisible(Transform _target)
		{
			var knowledge = new AIContactKnowledge(
				_target,
				DetectionState.Detected,
				ObservationState.Observed,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				_target.position,
				_target.position,
				0f,
				1f,
				true,
				false,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				false,
				true);
			return new AIPerceptionFrame(
				new[] { knowledge },
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				System.Array.Empty<AIContactKnowledge>(),
				ThreatLevel.High);
		}
		#endregion
	}
}
