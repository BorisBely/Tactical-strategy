using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// Vision Stage 18: one local contact, independent evidence, no new channel.
	/// </summary>
	public sealed class FinalPerceptionContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.011f;
		#endregion

		#region Private Fields
		private readonly List<GameObject> m_Spawned = new List<GameObject>(48);
		#endregion

		#region Setup
		[SetUp]
		public void SetUp()
		{
			WorldAllyReportHub.ResetForTests();
			WorldSoundHub.ResetForTests();
			VisionScanScheduler.ResetForTests();
		}

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < m_Spawned.Count; i++)
			{
				if (m_Spawned[i] != null)
					UnityEngine.Object.DestroyImmediate(m_Spawned[i]);
			}

			m_Spawned.Clear();
			WorldAllyReportHub.ResetForTests();
			WorldSoundHub.ResetForTests();
			VisionScanScheduler.ResetForTests();
		}
		#endregion

		#region Freeze
		[Test]
		public void Frozen_Q_Acquire_Lose_Exponent()
		{
			Assert.AreEqual(0.25f, DetectionQualityMath.DefaultAcquireThreshold, c_Tol);
			Assert.AreEqual(0.20f, DetectionQualityMath.DefaultLoseThreshold, c_Tol);
			Assert.AreEqual(3.8f, DetectionQualityMath.DefaultAcquisitionExponent, c_Tol);
			Assert.AreEqual(0.35f, DetectionQualityMath.DefaultAcquireTime, c_Tol);
			Assert.AreEqual(0.4f, DetectionQualityMath.VisibilityQuality(0.8f, 0.5f, 1f, 1f), 0.0001f);
		}

		[Test]
		public void Frozen_Horizons_Ranges_Attention()
		{
			Assert.AreEqual(3f, SoundKnowledgeMath.DefaultHorizonSeconds, c_Tol);
			Assert.AreEqual(8f, SharedKnowledgeMath.DefaultHorizonSeconds, c_Tol);
			Assert.AreEqual(80f, AllyReportEvidenceMath.DefaultRangeMeters, c_Tol);
			Assert.AreEqual(5f, MemoryDecayMath.DefaultRecentlyLostSeconds, c_Tol);
			Assert.AreEqual(30f, MemoryDecayMath.DefaultHorizonSeconds, c_Tol);
			Assert.AreEqual(150f, UnitVisionProfile.BaseRangeMeters, c_Tol);
			Assert.AreEqual(8, VisionLodMath.DefaultDetailSlotsPerFrame);
			Assert.AreEqual(1f, AttentionMath.EvaluateMultiplier(45f), 0.001f);
			Assert.IsFalse(PerceptionContractMath.SharedConfirmsVisualIdentity());
		}

		[Test]
		public void Frozen_E_Scope_Aim_Rockets()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			RocketLauncherData rockets = LoadRockets();
			Assert.IsNotNull(rockets);
			Assert.AreEqual(140f, weapons["Weapon_M4_ModA_1"].EffectiveRangeMeters, c_Tol);
			Assert.AreEqual(225f, weapons["Weapon_Sniper762x51"].EffectiveRangeMeters, c_Tol);
			Assert.AreEqual(300f, weapons["Weapon_MK19"].EffectiveRangeMeters, c_Tol);
			Assert.AreEqual(150f, optics["Attachment_M4_Reddot1"].ScopeVisionRangeMeters, c_Tol);
			Assert.AreEqual(300f, optics["Attachment_M4_Scope9"].ScopeVisionRangeMeters, c_Tol);
			Assert.AreEqual(1.55f, optics["Attachment_M4_Scope9"].AimTimeModifier, c_Tol);
			Assert.AreEqual(115f, rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7), c_Tol);
			Assert.AreEqual(12f, rockets.ProjectileLifetimeSeconds, c_Tol);
			Assert.AreEqual(240f, ProjectileLaunchPermit.Mk19MuzzleSpeed, c_Tol);
			Assert.AreEqual(25f, ProjectileLaunchPermit.Mk19LifetimeSeconds, c_Tol);
		}
		#endregion

		#region Contact
		[Test]
		public void OneTransform_ThreeChannels_OneContact()
		{
			DetectionProcessor processor = SpawnObserver("S18Merge").GetComponent<DetectionProcessor>();
			Transform target = Spawn("S18MergeE", new Vector3(6f, 0f, 0f)).transform;
			Vector3 seen = new Vector3(1f, 0f, 0f);
			Vector3 heard = new Vector3(2f, 0f, 0f);
			Vector3 reported = new Vector3(3f, 0f, 0f);
			Observe(processor, target, seen, 16);
			processor.ApplySyntheticSound(target, heard, 1f);
			processor.ApplySyntheticShared(target, reported, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.IsTrue(processor.TryGetContact(target, out PerceivedContact contact));
			Assert.AreEqual(ObservationState.Observed, contact.ObservationState);
			Assert.IsTrue(contact.HasUsefulSound);
			Assert.IsTrue(contact.HasUsefulShared);
			Assert.AreEqual(seen, contact.LastKnownPosition);
			Assert.AreEqual(heard, contact.SoundPosition);
			Assert.AreEqual(reported, contact.SharedPosition);
			Assert.IsTrue(PerceptionContractMath.IsVisibleNow(contact));
			Assert.IsTrue(PerceptionContractMath.HasVisualAimPoint(contact));
		}

		[Test]
		public void Conflict_LastKnownStaysVisual()
		{
			DetectionProcessor processor = SpawnObserver("S18Conf").GetComponent<DetectionProcessor>();
			Transform target = Spawn("S18ConfE", new Vector3(8f, 0f, 0f)).transform;
			Vector3 a = new Vector3(1f, 0f, 0f);
			Vector3 b = new Vector3(4f, 0f, 0f);
			Vector3 c = new Vector3(7f, 0f, 0f);
			float now = Observe(processor, target, a, 16);
			processor.ApplyEmptyObservationFrame();
			processor.Advance(0.2f, now + 0.2f);
			processor.ApplySyntheticSound(target, b, 1f);
			processor.ApplySyntheticShared(target, c, 1f);
			processor.Advance(0.05f, now + 0.25f);
			Assert.IsTrue(processor.TryGetContact(target, out PerceivedContact contact));
			Assert.AreNotEqual(ObservationState.Observed, contact.ObservationState);
			Assert.AreEqual(a, contact.LastKnownPosition);
			Assert.AreEqual(b, contact.SoundPosition);
			Assert.AreEqual(c, contact.SharedPosition);
			Assert.IsFalse(PerceptionContractMath.HasVisualAimPoint(contact));
			Assert.AreEqual(a, TargetSelectionMath.ResolveBelievedPosition(contact));
		}

		[Test]
		public void SharedHostile_DoesNotCommitVisualIdentity()
		{
			DetectionProcessor processor = SpawnAlly("S18IdB", Vector3.zero, UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			GameObject reporter = SpawnAlly("S18IdA", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S18IdE", new Vector3(6f, 0f, 0f));
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Hostile, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.IsTrue(processor.TryGetContact(target.transform, out PerceivedContact contact));
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.VisualIdentityEvidence);
			Assert.AreEqual(PerceivedIdentity.Hostile, contact.SharedIdentity);
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.IsFalse(PerceptionContractMath.IsVisibleNow(contact));
			Assert.IsFalse(PerceptionContractMath.HasVisualAimPoint(contact));
		}

		[Test]
		public void VisibleNow_RequiresDetectedAndObserved()
		{
			var contact = new PerceivedContact
			{
				State = DetectionState.Detected,
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 1f
			};
			Assert.IsFalse(PerceptionContractMath.IsVisibleNow(contact));
			contact.ObservationState = ObservationState.Observed;
			Assert.IsTrue(PerceptionContractMath.IsVisibleNow(contact));
			contact.State = DetectionState.Undetected;
			Assert.IsFalse(PerceptionContractMath.IsVisibleNow(contact));
		}

		[Test]
		public void SoundSharedMemory_NeverAimPoint()
		{
			var sound = new PerceivedContact
			{
				ObservationState = ObservationState.NotObserved,
				SoundConfidence = 1f,
				SoundPosition = Vector3.one
			};
			var shared = new PerceivedContact
			{
				ObservationState = ObservationState.NotObserved,
				SharedConfidence = 1f,
				SharedPosition = Vector3.one * 2f
			};
			var memory = new PerceivedContact
			{
				ObservationState = ObservationState.Lost,
				LastSeenConfidence = 1f,
				LastKnownPosition = Vector3.one * 3f
			};
			Assert.IsFalse(PerceptionContractMath.HasVisualAimPoint(sound));
			Assert.IsFalse(PerceptionContractMath.HasVisualAimPoint(shared));
			Assert.IsFalse(PerceptionContractMath.HasVisualAimPoint(memory));
			Assert.IsTrue(PerceptionContractMath.ContactStillKnown(sound));
			Assert.IsTrue(PerceptionContractMath.ContactStillKnown(shared));
			Assert.IsTrue(PerceptionContractMath.ContactStillKnown(memory));
		}

		[Test]
		public void ExpiredVision_FreshChannels_KeepContact()
		{
			DetectionProcessor processor = SpawnObserver("S18Keep").GetComponent<DetectionProcessor>();
			Transform target = Spawn("S18KeepE", new Vector3(5f, 0f, 0f)).transform;
			Observe(processor, target, target.position, 16);
			processor.ApplyEmptyObservationFrame();
			processor.Advance(31f, 31f);
			processor.ApplySyntheticSound(target, new Vector3(9f, 0f, 0f), 1f);
			processor.ApplySyntheticShared(target, new Vector3(11f, 0f, 0f), 1f);
			processor.Advance(0.05f, 31.05f);
			Assert.IsTrue(processor.TryGetContact(target, out PerceivedContact contact));
			Assert.IsFalse(contact.HasUsefulVisualMemory);
			Assert.IsTrue(contact.HasUsefulSound);
			Assert.IsTrue(contact.HasUsefulShared);
			Assert.IsTrue(PerceptionContractMath.ContactStillKnown(contact));
			Assert.IsFalse(PerceptionContractMath.IsVisibleNow(contact));
		}

		[Test]
		public void Reacquire_SameContact_VisualWins()
		{
			DetectionProcessor processor = SpawnObserver("S18Re").GetComponent<DetectionProcessor>();
			Transform target = Spawn("S18ReE", new Vector3(5f, 0f, 0f)).transform;
			Vector3 first = new Vector3(1f, 0f, 0f);
			Vector3 later = new Vector3(2f, 0f, 0f);
			float now = Observe(processor, target, first, 16);
			processor.ApplyEmptyObservationFrame();
			processor.Advance(0.4f, now + 0.4f);
			processor.ApplySyntheticSound(target, new Vector3(8f, 0f, 0f), 1f);
			processor.ApplySyntheticShared(target, new Vector3(9f, 0f, 0f), 1f);
			processor.Advance(0.05f, now + 0.45f);
			for (int i = 0; i < 16; i++)
			{
				processor.ApplySyntheticObservation(target, 4f, 0f, 1f, later);
				now += 0.05f;
				processor.Advance(0.05f, now);
			}

			Assert.IsTrue(processor.TryGetContact(target, out PerceivedContact contact));
			Assert.AreEqual(ObservationState.Observed, contact.ObservationState);
			Assert.AreEqual(later, contact.LastKnownPosition);
			Assert.IsTrue(contact.HasUsefulSound);
			Assert.IsTrue(contact.HasUsefulShared);
			Assert.IsTrue(PerceptionContractMath.HasVisualAimPoint(contact));
			Assert.AreEqual(later, TargetSelectionMath.ResolveBelievedPosition(contact));
		}
		#endregion

		#region Combat
		[Test]
		public void SoundOnly_SelectTrack_NoFire()
		{
			GameObject listener = SpawnObserver("S18SndB");
			GameObject target = Spawn("S18SndE", new Vector3(8f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			TargetSelector selector = listener.GetComponent<TargetSelector>();
			EngagementDecisionController engagement = listener.GetComponent<EngagementDecisionController>();
			processor.SetSimulatedTime(0f);
			processor.ApplySyntheticSound(target.transform, target.transform.position, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.AreEqual(target.transform, selector.SelectedTarget);
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.AreEqual(EngagementDecision.Track, engagement.CurrentDecision);
			Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
			Assert.IsNull(selector.GetEngageableSelectedTarget());
		}

		[Test]
		public void SharedOnly_SelectTrack_NoRpgLaunch()
		{
			GameObject listener = SpawnAlly("S18RpgB", Vector3.zero, UnitTeamId.Player);
			GameObject reporter = SpawnAlly("S18RpgA", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S18RpgE", new Vector3(8f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			TargetSelector selector = listener.GetComponent<TargetSelector>();
			EngagementDecisionController engagement = listener.GetComponent<EngagementDecisionController>();
			processor.SetSimulatedTime(0f);
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Hostile, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.AreEqual(target.transform, selector.SelectedTarget);
			Assert.AreEqual(EngagementDecision.Track, engagement.CurrentDecision);
			Assert.IsNull(selector.GetEngageableSelectedTarget());
			Assert.IsFalse(ProjectileLaunchPermit.TryAuthorize(
				false, Vector3.zero, target.transform.position, 150f, true, true, false,
				out ProjectileLaunchDeny reason));
			Assert.AreEqual(ProjectileLaunchDeny.NoAimPoint, reason);
		}

		[Test]
		public void VisionAim_AllowsFireAndProjectile()
		{
			GameObject listener = SpawnObserver("S18VisB");
			GameObject target = Spawn("S18VisE", new Vector3(8f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			TargetSelector selector = listener.GetComponent<TargetSelector>();
			EngagementDecisionController engagement = listener.GetComponent<EngagementDecisionController>();
			Observe(processor, target.transform, target.transform.position, 16);
			Assert.AreEqual(target.transform, selector.SelectedTarget);
			Assert.IsTrue(selector.HasSelectedAimPoint);
			Assert.AreEqual(EngagementDecision.Fire, engagement.CurrentDecision);
			Assert.IsNotNull(selector.GetEngageableSelectedTarget());
			Assert.IsTrue(ProjectileLaunchPermit.TryAuthorize(
				true, Vector3.zero, target.transform.position, 150f, true, true, false,
				out ProjectileLaunchDeny reason));
			Assert.AreEqual(ProjectileLaunchDeny.None, reason);
		}

		[Test]
		public void AttentionPlusShared_DoesNotDetect()
		{
			float gated = DetectionQualityMath.IntegrateProgress(0f, 0.24f, 1f, _attentionMultiplier: 2.5f);
			Assert.Less(gated, 0.0001f);
			DetectionProcessor processor = SpawnAlly("S18AttB", Vector3.zero, UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			GameObject reporter = SpawnAlly("S18AttA", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S18AttE", new Vector3(8f, 0f, 0f));
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Hostile, 1f);
			processor.Advance(0.5f, 0.5f);
			Assert.IsTrue(processor.TryGetContact(target.transform, out PerceivedContact contact));
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.Less(contact.DetectionProgress, 0.001f);
			Assert.IsFalse(PerceptionContractMath.IsVisibleNow(contact));
			Assert.Greater(AttentionMath.EvaluateMultiplier(0f), 1f);
		}
		#endregion

		#region Snapshot / matrices
		[Test]
		public void Snapshot_HasChannels_OmitsQAndProgress()
		{
			var contact = new PerceivedContact
			{
				State = DetectionState.Undetected,
				ObservationState = ObservationState.NotObserved,
				Identity = PerceivedIdentity.Unknown,
				SharedIdentity = PerceivedIdentity.Hostile,
				SoundConfidence = 0.7f,
				SoundPosition = new Vector3(2f, 0f, 0f),
				SharedConfidence = 0.8f,
				SharedPosition = new Vector3(3f, 0f, 0f),
				DetectionProgress = 0.9f
			};
			AIContactKnowledge snap = AIContactKnowledge.From(contact);
			Assert.IsFalse(snap.VisibleNow);
			Assert.IsTrue(snap.SoundPresent);
			Assert.AreEqual(0.7f, snap.SoundConfidence, 0.0001f);
			Assert.IsTrue(snap.SharedPresent);
			Assert.AreEqual(PerceivedIdentity.Hostile, snap.SharedIdentity);
			Assert.AreEqual(PerceivedIdentity.Unknown, snap.Identity);
			Assert.IsFalse(snap.Hostile);
			FieldInfo[] fields = typeof(AIContactKnowledge).GetFields();
			for (int i = 0; i < fields.Length; i++)
			{
				Assert.IsFalse(fields[i].Name == "Q");
				StringAssert.DoesNotContain("Progress", fields[i].Name);
				StringAssert.DoesNotContain("Team", fields[i].Name);
			}
		}

		[Test]
		public void TimeMatrix_IndependentDecay()
		{
			Assert.Greater(MemoryDecayMath.Evaluate(2f, 1f), 0.5f);
			Assert.Greater(SoundKnowledgeMath.Evaluate(2f, 1f), 0f);
			Assert.Greater(SharedKnowledgeMath.Evaluate(2f, 1f), 0f);
			Assert.Greater(MemoryDecayMath.Evaluate(5f, 1f), 0f);
			Assert.AreEqual(0f, SoundKnowledgeMath.Evaluate(3f, 1f), 0.0001f);
			Assert.Greater(SharedKnowledgeMath.Evaluate(5f, 1f), 0f);
			Assert.AreEqual(0f, SharedKnowledgeMath.Evaluate(8f, 1f), 0.0001f);
			Assert.IsTrue(MemoryDecayMath.IsStale(MemoryDecayMath.Evaluate(18f, 1f)));
			Assert.AreEqual(0f, MemoryDecayMath.Evaluate(30f, 1f), 0.0001f);
		}

		[Test]
		public void RangeMatrix_VisionSoundShared()
		{
			ResolvedVisionProfile eye = UnitVisionProfile.ResolveForSource(
				VisionSourceKind.InfantryEye,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.HipFire,
				null,
				false,
				0f);
			Assert.AreEqual(150f, eye.MaxRangeMeters, c_Tol);
			WeaponAttachmentDefinition scope = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			try
			{
				scope.SetScopeVisionRangeMeters(300f);
				ResolvedVisionProfile optic = UnitVisionProfile.ResolveForSource(
					VisionSourceKind.InfantryEye,
					UnitVisionProfile.BaseRangeMeters,
					UnitVisionProfile.BaseFovDegrees,
					WeaponPoseState.Aiming,
					new[] { scope },
					false,
					0f);
				Assert.AreEqual(300f, optic.MaxRangeMeters, c_Tol);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(scope);
			}

			ResolvedVisionProfile passenger = UnitVisionProfile.ResolveForSource(
				VisionSourceKind.Passenger,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.HipFire,
				null,
				true,
				0f);
			Assert.AreEqual(150f, passenger.MaxRangeMeters, c_Tol);
			Assert.Greater(SoundEvidenceMath.GunshotRangeMeters, UnitVisionProfile.BaseRangeMeters);
			Assert.Less(AllyReportEvidenceMath.DefaultRangeMeters, UnitVisionProfile.BaseRangeMeters);
			var farSound = new PerceivedContact
			{
				ObservationState = ObservationState.NotObserved,
				SoundConfidence = 1f,
				SoundPosition = new Vector3(200f, 0f, 0f)
			};
			Assert.IsFalse(PerceptionContractMath.HasVisualAimPoint(farSound));
		}

		[Test]
		public void Architecture_EventDriven_OneVision_DetailCap()
		{
			int extra = 0;
			Type[] types = typeof(UnitVision).Assembly.GetTypes();
			for (int i = 0; i < types.Length; i++)
			{
				if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
					extra++;
			}

			Assert.AreEqual(0, extra);
			Assert.AreEqual(8, VisionLodMath.DefaultDetailSlotsPerFrame);
			Assert.IsFalse(HubUsesRaycast(typeof(WorldSoundHub)));
			Assert.IsFalse(HubUsesRaycast(typeof(WorldAllyReportHub)));
		}

		[Test]
		public void ScanStarvation_FairnessWithinEight()
		{
			VisionScanScheduler.ResetForTests();
			VisionScanScheduler.DetailSlotsPerFrame = 8;
			const int n = 50;
			int[] starve = new int[n];
			int maxConsecutive = 0;
			for (int frame = 0; frame < 24; frame++)
			{
				VisionScanScheduler.BeginFrameForTests(frame);
				for (int i = 0; i < n; i++)
				{
					float score = VisionDetailPriorityMath.Score(
						i < 8 ? 2.5f : 1f, false, false, false, starve[i]);
					VisionScanScheduler.RequestDetailSlot(i, score);
				}

				VisionScanScheduler.FlushPendingDetailIfNeeded();
				for (int i = 0; i < n; i++)
				{
					if (VisionScanScheduler.WasGranted(i))
						starve[i] = 0;
					else
					{
						starve[i]++;
						if (starve[i] > maxConsecutive)
							maxConsecutive = starve[i];
					}
				}
			}

			Assert.LessOrEqual(maxConsecutive, VisionDetailPriorityMath.FairnessMaxConsecutiveSkip);
		}

		[Test]
		public void EndToEnd_ReportThenSeeThenFire()
		{
			GameObject a = SpawnAlly("S18E2EA", Vector3.zero, UnitTeamId.Player);
			GameObject b = SpawnAlly("S18E2EB", new Vector3(10f, 0f, 0f), UnitTeamId.Player);
			GameObject enemy = Spawn("S18E2EE", new Vector3(5f, 0f, 0f));
			DetectionProcessor pa = a.GetComponent<DetectionProcessor>();
			DetectionProcessor pb = b.GetComponent<DetectionProcessor>();
			TargetSelector selectorB = b.GetComponent<TargetSelector>();
			EngagementDecisionController engagementB = b.GetComponent<EngagementDecisionController>();
			Observe(pa, enemy.transform, enemy.transform.position, 16);
			Assert.IsTrue(pa.TryGetContact(enemy.transform, out PerceivedContact seenA));
			Assert.AreEqual(ObservationState.Observed, seenA.ObservationState);
			Assert.IsTrue(pb.TryGetContact(enemy.transform, out PerceivedContact sharedB));
			Assert.IsTrue(sharedB.HasUsefulShared);
			Assert.AreEqual(ObservationState.NotObserved, sharedB.ObservationState);
			Assert.AreEqual(EngagementDecision.Track, engagementB.CurrentDecision);
			Observe(pb, enemy.transform, enemy.transform.position, 16);
			Assert.IsTrue(pb.TryGetContact(enemy.transform, out PerceivedContact seenB));
			Assert.AreEqual(ObservationState.Observed, seenB.ObservationState);
			Assert.IsTrue(selectorB.HasSelectedAimPoint);
			Assert.AreEqual(EngagementDecision.Fire, engagementB.CurrentDecision);
		}
		#endregion

		#region Private Methods
		private GameObject Spawn(string _name, Vector3 _position)
		{
			var go = new GameObject(_name);
			go.transform.position = _position;
			m_Spawned.Add(go);
			return go;
		}

		private GameObject SpawnObserver(string _name)
		{
			GameObject go = Spawn(_name, Vector3.zero);
			go.AddComponent<UnitObservationSource>();
			go.AddComponent<UnitPerception>();
			go.AddComponent<DetectionProcessor>();
			go.AddComponent<TargetSelector>();
			go.AddComponent<EngagementDecisionController>();
			return go;
		}

		private GameObject SpawnAlly(string _name, Vector3 _position, UnitTeamId _team)
		{
			GameObject go = SpawnObserver(_name);
			go.transform.position = _position;
			UnitTeam team = go.AddComponent<UnitTeam>();
			team.SetTeam(_team);
			return go;
		}

		private static void Publish(
			GameObject _reporter,
			GameObject _subject,
			Vector3 _position,
			PerceivedIdentity _identity,
			float _confidence)
		{
			WorldAllyReportHub.Publish(AllyReportEvidenceMath.Create(
				_reporter.transform,
				_subject.transform,
				_position,
				_identity,
				_confidence));
		}

		private static float Observe(
			DetectionProcessor _processor,
			Transform _target,
			Vector3 _position,
			int _ticks)
		{
			_processor.SetSimulatedTime(0f);
			float now = 0f;
			for (int i = 0; i < _ticks; i++)
			{
				_processor.ApplySyntheticObservation(_target, 4f, 0f, 1f, _position);
				now += 0.05f;
				_processor.Advance(0.05f, now);
			}

			return now;
		}

		private static bool HubUsesRaycast(Type _hub)
		{
			MethodInfo[] methods = _hub.GetMethods(
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			for (int i = 0; i < methods.Length; i++)
			{
				if (methods[i].Name.IndexOf("Raycast", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
			}

			return false;
		}

		private static RocketLauncherData LoadRockets()
		{
			return AssetDatabase.LoadAssetAtPath<RocketLauncherData>(
				"Assets/GameData/Combat/RocketLauncherData.asset");
		}

		private static Dictionary<string, WeaponDefinition> LoadCombatWeapons()
		{
			var map = new Dictionary<string, WeaponDefinition>();
			string[] g = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < g.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(g[i]);
				if (path.Replace('\\', '/').IndexOf("/Test/", StringComparison.OrdinalIgnoreCase) >= 0)
					continue;
				WeaponDefinition asset = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
				if (asset != null)
					map[asset.name] = asset;
			}

			return map;
		}

		private static Dictionary<string, WeaponAttachmentDefinition> LoadCombatOptics()
		{
			var map = new Dictionary<string, WeaponAttachmentDefinition>();
			string[] g = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < g.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(g[i]);
				if (path.Replace('\\', '/').IndexOf("/Test/", StringComparison.OrdinalIgnoreCase) >= 0)
					continue;
				WeaponAttachmentDefinition asset =
					AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
				if (asset == null || asset.AttachmentType != WeaponAttachmentType.Optic)
					continue;
				map[asset.name] = asset;
			}

			return map;
		}
		#endregion
	}
}
