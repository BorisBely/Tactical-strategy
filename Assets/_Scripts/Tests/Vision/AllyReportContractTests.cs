using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// Vision Stage 17: Ally report is a knowledge channel. Not Observed. Not AimPoint. Not Fire.
	/// </summary>
	public sealed class AllyReportContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.011f;
		#endregion

		#region Private Fields
		private readonly List<GameObject> m_Spawned = new List<GameObject>(32);
		#endregion

		#region Setup
		[SetUp]
		public void SetUp()
		{
			WorldAllyReportHub.ResetForTests();
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
			float q = DetectionQualityMath.VisibilityQuality(0.8f, 0.5f, 1f, 1f);
			Assert.AreEqual(0.4f, q, 0.0001f);
		}

		[Test]
		public void Frozen_E_ScopeVision_AimTime_RocketLife()
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
			Assert.AreEqual(0.35f, WeaponAimModeUtility.SnapShotAimProgress01, c_Tol);
			Assert.AreEqual(0.68f, WeaponAimModeUtility.QuickAimProgress01, c_Tol);
			Assert.AreEqual(1.00f, WeaponAimModeUtility.FullAimProgress01, c_Tol);
			Assert.AreEqual(115f, rockets.GetMuzzleSpeed(RocketLauncherType.Rpg7), c_Tol);
			Assert.AreEqual(12f, rockets.ProjectileLifetimeSeconds, c_Tol);
			Assert.AreEqual(240f, ProjectileLaunchPermit.Mk19MuzzleSpeed, c_Tol);
			Assert.AreEqual(25f, ProjectileLaunchPermit.Mk19LifetimeSeconds, c_Tol);
		}

		[Test]
		public void Frozen_Attention_StillRateOnly()
		{
			Assert.AreEqual(1f, AttentionMath.EvaluateMultiplier(45f), 0.001f);
			Assert.AreEqual(4, typeof(DetectionQualityMath).GetMethod(
				nameof(DetectionQualityMath.VisibilityQuality)).GetParameters().Length);
			float gated = DetectionQualityMath.IntegrateProgress(0f, 0.24f, 1f, _attentionMultiplier: 3f);
			Assert.Less(gated, 0.0001f);
		}

		[Test]
		public void Frozen_SoundHorizon_ThreeSeconds()
		{
			Assert.AreEqual(3f, SoundKnowledgeMath.DefaultHorizonSeconds, 0.0001f);
			Assert.AreEqual(0f, SoundKnowledgeMath.Evaluate(3f, 1f), 0.0001f);
		}

		[Test]
		public void Frozen_SharedHorizon_EightSeconds()
		{
			Assert.AreEqual(8f, SharedKnowledgeMath.DefaultHorizonSeconds, 0.0001f);
			Assert.AreEqual(0f, SharedKnowledgeMath.Evaluate(8f, 1f), 0.0001f);
			Assert.AreEqual(80f, AllyReportEvidenceMath.DefaultRangeMeters, 0.0001f);
			Assert.AreEqual(1f, AllyReportEvidenceMath.MinIntervalSeconds, 0.0001f);
			Assert.AreEqual(8f, AllyReportEvidenceMath.MoveThresholdMeters, 0.0001f);
		}
		#endregion

		#region Contract
		[Test]
		public void A_ReportNotSee_NoAim_NoFire()
		{
			GameObject listener = SpawnAlly("S17A_B", Vector3.zero, UnitTeamId.Player);
			GameObject reporter = SpawnAlly("S17A_A", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17A_E", new Vector3(8f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			TargetSelector selector = listener.GetComponent<TargetSelector>();
			EngagementDecisionController engagement = listener.GetComponent<EngagementDecisionController>();
			processor.SetSimulatedTime(0f);
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Unknown, 1f);
			processor.Advance(0.05f, 0.05f);

			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.IsTrue(contact.HasUsefulShared);
			Assert.IsFalse(contact.LastObservation.HasAimPoint);
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(contact, out _));
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
			Assert.IsNull(selector.GetEngageableSelectedTarget());
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(Vector3.zero, contact.LastKnownPosition);
			Assert.AreEqual(target.transform.position, contact.SharedPosition);
			Assert.AreEqual(reporter.transform, contact.SharedReporter);
		}

		[Test]
		public void B_InRange_HighConfidence()
		{
			DetectionProcessor listener = SpawnAlly("S17B_B", Vector3.zero, UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			GameObject reporter = SpawnAlly("S17B_A", new Vector3(5f, 0f, 0f), UnitTeamId.Player);
			Transform subject = Spawn("S17B_E", new Vector3(8f, 0f, 0f)).transform;
			Publish(reporter, subject.gameObject, subject.position, PerceivedIdentity.Unknown, 1f);
			Assert.That(listener.TryGetContact(subject, out PerceivedContact contact), Is.True);
			Assert.Greater(contact.SharedConfidence, 0.9f);
		}

		[Test]
		public void C_BeyondRange_NoEvidence()
		{
			DetectionProcessor listener = SpawnAlly("S17C_B", new Vector3(90f, 0f, 0f), UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			GameObject reporter = SpawnAlly("S17C_A", Vector3.zero, UnitTeamId.Player);
			Transform subject = Spawn("S17C_E", new Vector3(8f, 0f, 0f)).transform;
			Publish(reporter, subject.gameObject, subject.position, PerceivedIdentity.Unknown, 1f);
			Assert.IsFalse(listener.TryGetContact(subject, out _));
		}

		[Test]
		public void D_Horizon_UsefulSharedFadesByEightSeconds()
		{
			GameObject listener = SpawnAlly("S17D_B", Vector3.zero, UnitTeamId.Player);
			GameObject target = Spawn("S17D_E", new Vector3(6f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			processor.ApplySyntheticShared(target.transform, target.transform.position, 1f);
			processor.Advance(0.001f, 0.001f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t0), Is.True);
			Assert.IsTrue(t0.HasUsefulShared);

			processor.Advance(4f, 4f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t4), Is.True);
			Assert.IsTrue(t4.HasUsefulShared);

			processor.Advance(4f, 8f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t8), Is.True);
			Assert.IsFalse(t8.HasUsefulShared);

			processor.Advance(1f, 9f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t9), Is.True);
			Assert.IsFalse(t9.HasUsefulShared);
		}

		[Test]
		public void E_VisualThenReport_LastKnownStaysVisual()
		{
			GameObject listener = SpawnAlly("S17E_B", Vector3.zero, UnitTeamId.Player);
			GameObject target = Spawn("S17E_E", new Vector3(5f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			Vector3 seen = new Vector3(5f, 0f, 1f);
			float now = Observe(processor, target.transform, seen, 16);

			processor.ApplyEmptyObservationFrame();
			now += 0.1f;
			processor.Advance(0.1f, now);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact hidden), Is.True);
			Assert.Greater(hidden.LastSeenConfidence, 0f);

			Vector3 reported = seen + Vector3.forward * 4f;
			processor.ApplySyntheticShared(target.transform, reported, 1f);
			now += 0.05f;
			processor.Advance(0.05f, now);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact mixed), Is.True);
			Assert.AreEqual(seen, mixed.LastSeenPosition);
			Assert.AreEqual(seen, mixed.LastKnownPosition);
			Assert.AreEqual(reported, mixed.SharedPosition);
			Assert.AreEqual(seen, TargetSelectionMath.ResolveBelievedPosition(mixed));
		}

		[Test]
		public void F_HostileReport_DoesNotCommitVisualIdentity()
		{
			GameObject listener = SpawnAlly("S17F_B", Vector3.zero, UnitTeamId.Player);
			GameObject reporter = SpawnAlly("S17F_A", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17F_E", new Vector3(7f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Hostile, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(PerceivedIdentity.Hostile, contact.SharedIdentity);
			Assert.Less(contact.IdentityConfidence, 0.5f);
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(contact, out _));
		}
		#endregion

		#region Hub / merge / combat
		[Test]
		public void Hub_SelfReport_NotGranted()
		{
			DetectionProcessor processor = SpawnAlly("S17Self", Vector3.zero, UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			GameObject target = Spawn("S17SelfE", new Vector3(6f, 0f, 0f));
			Publish(processor.gameObject, target, target.transform.position, PerceivedIdentity.Unknown, 1f);
			Assert.AreEqual(0, processor.Contacts.Count);
		}

		[Test]
		public void Hub_EnemyTeam_NotGranted()
		{
			DetectionProcessor listener = SpawnAlly("S17EnemyB", Vector3.zero, UnitTeamId.Enemy)
				.GetComponent<DetectionProcessor>();
			GameObject reporter = SpawnAlly("S17EnemyA", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17EnemyE", new Vector3(6f, 0f, 0f));
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Unknown, 1f);
			Assert.IsFalse(listener.TryGetContact(target.transform, out _));
		}

		[Test]
		public void Hub_OnePublish_OnlyInRangeGranted()
		{
			DetectionProcessor near = SpawnAlly("S17Near", Vector3.zero, UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			DetectionProcessor far = SpawnAlly("S17Far", new Vector3(90f, 0f, 0f), UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			GameObject reporter = SpawnAlly("S17Rep", new Vector3(2f, 0f, 0f), UnitTeamId.Player);
			Transform subject = Spawn("S17Sub", new Vector3(8f, 0f, 0f)).transform;
			Publish(reporter, subject.gameObject, subject.position, PerceivedIdentity.Unknown, 1f);
			Assert.That(near.TryGetContact(subject, out _), Is.True);
			Assert.That(far.TryGetContact(subject, out _), Is.False);
			Assert.AreEqual(1, WorldAllyReportHub.LastPublishDeliveryCount);
		}

		[Test]
		public void TwoReporters_OneContact()
		{
			DetectionProcessor listener = SpawnAlly("S17MergeB", Vector3.zero, UnitTeamId.Player)
				.GetComponent<DetectionProcessor>();
			GameObject a = SpawnAlly("S17MergeA", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
			GameObject c = SpawnAlly("S17MergeC", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17MergeE", new Vector3(7f, 0f, 0f));
			Vector3 first = new Vector3(7f, 0f, 0f);
			Vector3 second = new Vector3(9f, 0f, 2f);
			Publish(a, target, first, PerceivedIdentity.Unknown, 1f);
			Publish(c, target, second, PerceivedIdentity.Unknown, 1f);
			Assert.AreEqual(1, listener.Contacts.Count);
			Assert.That(listener.TryGetContact(target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(second, contact.SharedPosition);
			Assert.AreEqual(c.transform, contact.SharedReporter);
			Assert.AreEqual(Vector3.zero, contact.LastKnownPosition);
		}

		[Test]
		public void Conflict_LastReportWinsShared_LastKnownUnchanged()
		{
			GameObject listener = SpawnAlly("S17ConfB", Vector3.zero, UnitTeamId.Player);
			GameObject target = Spawn("S17ConfE", new Vector3(5f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			Vector3 seen = new Vector3(5f, 0f, 1f);
			float now = Observe(processor, target.transform, seen, 16);
			processor.ApplyEmptyObservationFrame();
			now += 0.1f;
			processor.Advance(0.1f, now);

			Vector3 x = seen + Vector3.right * 2f;
			Vector3 y = seen + Vector3.forward * 3f;
			processor.ApplySyntheticShared(target.transform, x, 1f);
			now += 0.05f;
			processor.Advance(0.05f, now);
			processor.ApplySyntheticShared(target.transform, y, 1f);
			now += 0.05f;
			processor.Advance(0.05f, now);

			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(1, processor.Contacts.Count);
			Assert.AreEqual(seen, contact.LastKnownPosition);
			Assert.AreEqual(y, contact.SharedPosition);
		}

		[Test]
		public void LaterVisual_MergesOneContact()
		{
			GameObject listener = SpawnAlly("S17VisB", Vector3.zero, UnitTeamId.Player);
			GameObject target = Spawn("S17VisE", new Vector3(6f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			processor.ApplySyntheticShared(target.transform, target.transform.position, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact shared), Is.True);
			object contactRef = shared;

			Vector3 seen = new Vector3(6f, 0f, 1f);
			Observe(processor, target.transform, seen, 16);
			Assert.AreEqual(1, processor.Contacts.Count);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact merged), Is.True);
			Assert.AreSame(contactRef, merged);
			Assert.AreEqual(ObservationState.Observed, merged.ObservationState);
			Assert.IsTrue(merged.HasUsefulShared);
			Assert.AreEqual(seen, merged.LastKnownPosition);
		}

		[Test]
		public void UnknownReport_DoesNotBecomeHostile()
		{
			GameObject listener = SpawnAlly("S17UnkB", Vector3.zero, UnitTeamId.Player);
			GameObject reporter = SpawnAlly("S17UnkA", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17UnkE", new Vector3(6f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Unknown, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.SharedIdentity);
		}

		[Test]
		public void Combat_SelectMayExist_Track_NoFire()
		{
			GameObject listener = SpawnAlly("S17CbtB", Vector3.zero, UnitTeamId.Player);
			GameObject reporter = SpawnAlly("S17CbtA", new Vector3(3f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17CbtE", new Vector3(8f, 0f, 0f));
			DetectionProcessor processor = listener.GetComponent<DetectionProcessor>();
			TargetSelector selector = listener.GetComponent<TargetSelector>();
			EngagementDecisionController engagement = listener.GetComponent<EngagementDecisionController>();
			processor.SetSimulatedTime(0f);
			Publish(reporter, target, target.transform.position, PerceivedIdentity.Unknown, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.AreEqual(target.transform, selector.SelectedTarget);
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.AreEqual(EngagementDecision.Track, engagement.CurrentDecision);
			Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
			Assert.IsNull(selector.GetEngageableSelectedTarget());
		}

		[Test]
		public void LiveObserve_PublishesToAlly()
		{
			GameObject reporter = SpawnAlly("S17LiveA", Vector3.zero, UnitTeamId.Player);
			GameObject listener = SpawnAlly("S17LiveB", new Vector3(10f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17LiveE", new Vector3(5f, 0f, 0f));
			DetectionProcessor a = reporter.GetComponent<DetectionProcessor>();
			DetectionProcessor b = listener.GetComponent<DetectionProcessor>();
			Observe(a, target.transform, target.transform.position, 16);
			Assert.That(a.TryGetContact(target.transform, out PerceivedContact seen), Is.True);
			Assert.AreEqual(ObservationState.Observed, seen.ObservationState);
			Assert.That(b.TryGetContact(target.transform, out PerceivedContact reported), Is.True);
			Assert.IsTrue(reported.HasUsefulShared);
			Assert.AreEqual(ObservationState.NotObserved, reported.ObservationState);
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(reported, out _));
			Assert.GreaterOrEqual(WorldAllyReportHub.LastPublishDeliveryCount, 1);
		}

		[Test]
		public void Throttle_SecondIdenticalPublishBlocked()
		{
			Assert.IsTrue(AllyReportEvidenceMath.ShouldPublish(
				false, 0f, 0f, Vector3.zero, PerceivedIdentity.Unknown, Vector3.zero, PerceivedIdentity.Unknown));
			Assert.IsFalse(AllyReportEvidenceMath.ShouldPublish(
				true, 0.5f, 0f, Vector3.zero, PerceivedIdentity.Unknown, Vector3.zero, PerceivedIdentity.Unknown));
			Assert.IsFalse(AllyReportEvidenceMath.ShouldPublish(
				true, 1.1f, 0f, Vector3.zero, PerceivedIdentity.Unknown, Vector3.zero, PerceivedIdentity.Unknown));
			Assert.IsTrue(AllyReportEvidenceMath.ShouldPublish(
				true, 1.1f, 0f, Vector3.zero, PerceivedIdentity.Unknown,
				new Vector3(8f, 0f, 0f), PerceivedIdentity.Unknown));
			Assert.IsTrue(AllyReportEvidenceMath.ShouldPublish(
				true, 1.1f, 0f, Vector3.zero, PerceivedIdentity.Unknown,
				Vector3.zero, PerceivedIdentity.Hostile));
		}

		[Test]
		public void NoTeam_DoesNotAutoPublish()
		{
			GameObject reporter = SpawnObserver("S17NoTeamA");
			GameObject listener = SpawnAlly("S17NoTeamB", new Vector3(4f, 0f, 0f), UnitTeamId.Player);
			GameObject target = Spawn("S17NoTeamE", new Vector3(6f, 0f, 0f));
			Observe(reporter.GetComponent<DetectionProcessor>(), target.transform, target.transform.position, 16);
			Assert.IsFalse(listener.GetComponent<DetectionProcessor>().TryGetContact(target.transform, out _));
		}

		[Test]
		public void Architecture_OneUnitVision_SharedNotInQ()
		{
			int extra = 0;
			Type[] types = typeof(UnitVision).Assembly.GetTypes();
			for (int i = 0; i < types.Length; i++)
			{
				if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
					extra++;
			}

			Assert.AreEqual(0, extra);
			Assert.AreEqual(
				4,
				typeof(DetectionQualityMath).GetMethod(nameof(DetectionQualityMath.VisibilityQuality))
					.GetParameters().Length);
			Assert.AreEqual(80f, AllyReportEvidenceMath.DefaultRangeMeters, 0.01f);
			Assert.AreEqual(8f, SharedKnowledgeMath.DefaultHorizonSeconds, 0.01f);
			Assert.AreEqual(3f, SoundKnowledgeMath.DefaultHorizonSeconds, 0.01f);
			Assert.IsFalse(HubUsesRaycast());
		}

		[Test]
		public void ResolveBelievedPosition_SharedOnly_UsesSharedPosition()
		{
			var contact = new PerceivedContact
			{
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 0f,
				LastKnownPosition = Vector3.zero,
				SharedConfidence = 0.7f,
				SharedPosition = new Vector3(9f, 0f, 2f)
			};
			Assert.AreEqual(contact.SharedPosition, TargetSelectionMath.ResolveBelievedPosition(contact));
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

		private static bool HubUsesRaycast()
		{
			MethodInfo[] methods = typeof(WorldAllyReportHub).GetMethods(
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
