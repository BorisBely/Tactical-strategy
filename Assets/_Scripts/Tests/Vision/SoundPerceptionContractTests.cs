using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// Vision Stage 16: Sound is a knowledge channel. Q / Attention / AimPoint unchanged.
	/// </summary>
	public sealed class SoundPerceptionContractTests
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
			WorldSoundHub.ResetForTests();
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
			WorldSoundHub.ResetForTests();
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
		#endregion

		#region A–F
		[Test]
		public void A_HearNotSee_NoAim_NoFire()
		{
			GameObject observer = SpawnObserver("S16A_Obs");
			GameObject target = Spawn("S16A_Tgt", new Vector3(8f, 0f, 0f));
			DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
			TargetSelector selector = observer.GetComponent<TargetSelector>();
			EngagementDecisionController engagement = observer.GetComponent<EngagementDecisionController>();
			processor.SetSimulatedTime(0f);
			processor.ApplySyntheticSound(target.transform, target.transform.position, 1f);
			processor.Advance(0.05f, 0.05f);

			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.IsFalse(contact.LastObservation.HasAimPoint);
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(contact, out _));
			Assert.IsFalse(selector.HasSelectedAimPoint);
			Assert.AreNotEqual(EngagementDecision.Fire, engagement.CurrentDecision);
			Assert.IsNull(selector.GetEngageableSelectedTarget());
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(Vector3.zero, contact.LastKnownPosition);
			Assert.AreEqual(target.transform.position, contact.SoundPosition);
		}

		[Test]
		public void B_NearGunshot_HighConfidence()
		{
			DetectionProcessor listener = SpawnObserver("S16B_Obs").GetComponent<DetectionProcessor>();
			Transform source = Spawn("S16B_Src", new Vector3(10f, 0f, 0f)).transform;
			WorldSoundHub.PublishGunshot(source, source.position);
			Assert.That(listener.TryGetContact(source, out PerceivedContact contact), Is.True);
			Assert.Greater(contact.SoundConfidence, 0.9f);
		}

		[Test]
		public void C_BeyondRange_NoEvidence()
		{
			DetectionProcessor listener = SpawnObserver("S16C_Obs").GetComponent<DetectionProcessor>();
			Transform source = Spawn("S16C_Src", new Vector3(400f, 0f, 0f)).transform;
			WorldSoundHub.PublishGunshot(source, source.position);
			Assert.IsFalse(listener.TryGetContact(source, out _));
		}

		[Test]
		public void D_Horizon_UsefulSoundFadesByThreeSeconds()
		{
			GameObject observer = SpawnObserver("S16D_Obs");
			GameObject target = Spawn("S16D_Tgt", new Vector3(6f, 0f, 0f));
			DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			processor.ApplySyntheticSound(target.transform, target.transform.position, 1f);
			processor.Advance(0.001f, 0.001f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t0), Is.True);
			Assert.IsTrue(t0.HasUsefulSound);

			processor.Advance(1f, 1f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t1), Is.True);
			Assert.IsTrue(t1.HasUsefulSound);

			processor.Advance(1f, 2f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t2), Is.True);
			Assert.IsTrue(t2.HasUsefulSound);

			processor.Advance(1f, 3f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t3), Is.True);
			Assert.IsFalse(t3.HasUsefulSound);

			processor.Advance(1f, 4f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact t4), Is.True);
			Assert.IsFalse(t4.HasUsefulSound);
		}

		[Test]
		public void E_VisualThenSound_LastKnownStaysVisual()
		{
			GameObject observer = SpawnObserver("S16E_Obs");
			GameObject target = Spawn("S16E_Tgt", new Vector3(5f, 0f, 0f));
			DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			Vector3 seen = new Vector3(5f, 0f, 1f);
			float now = 0f;
			for (int i = 0; i < 16; i++)
			{
				processor.ApplySyntheticObservation(target.transform, 4f, 0f, 1f, seen);
				now += 0.05f;
				processor.Advance(0.05f, now);
			}

			processor.ApplyEmptyObservationFrame();
			now += 0.1f;
			processor.Advance(0.1f, now);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact hidden), Is.True);
			Assert.Greater(hidden.LastSeenConfidence, 0f);

			Vector3 heard = seen + Vector3.forward * 4f;
			processor.ApplySyntheticSound(target.transform, heard, 1f);
			now += 0.05f;
			processor.Advance(0.05f, now);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact mixed), Is.True);
			Assert.AreEqual(seen, mixed.LastSeenPosition);
			Assert.AreEqual(seen, mixed.LastKnownPosition);
			Assert.AreEqual(heard, mixed.SoundPosition);
			Assert.AreEqual(seen, TargetSelectionMath.ResolveBelievedPosition(mixed));
		}

		[Test]
		public void F_NeverSeen_IdentityUnknown()
		{
			GameObject observer = SpawnObserver("S16F_Obs");
			GameObject target = Spawn("S16F_Tgt", new Vector3(7f, 0f, 0f));
			DetectionProcessor processor = observer.GetComponent<DetectionProcessor>();
			processor.SetSimulatedTime(0f);
			processor.ApplySyntheticSound(target.transform, target.transform.position, 1f);
			processor.Advance(0.05f, 0.05f);
			Assert.That(processor.TryGetContact(target.transform, out PerceivedContact contact), Is.True);
			Assert.AreEqual(PerceivedIdentity.Unknown, contact.Identity);
			Assert.AreEqual(ObservationState.NotObserved, contact.ObservationState);
			Assert.IsFalse(TargetSelectionMath.TryGetObservedAimPoint(contact, out _));
		}
		#endregion

		#region Hub / architecture
		[Test]
		public void Hub_SelfShot_NotGranted()
		{
			DetectionProcessor processor = SpawnObserver("S16Self").GetComponent<DetectionProcessor>();
			WorldSoundHub.PublishGunshot(processor.transform, processor.transform.position);
			Assert.AreEqual(0, processor.Contacts.Count);
		}

		[Test]
		public void Hub_OnePublish_OnlyInRangeGranted()
		{
			DetectionProcessor near = SpawnObserver("S16Near").GetComponent<DetectionProcessor>();
			near.transform.position = Vector3.zero;
			DetectionProcessor far = SpawnObserver("S16Far").GetComponent<DetectionProcessor>();
			far.transform.position = new Vector3(400f, 0f, 0f);
			Transform source = Spawn("S16Src", new Vector3(8f, 0f, 0f)).transform;
			WorldSoundHub.PublishGunshot(source, source.position);
			Assert.That(near.TryGetContact(source, out _), Is.True);
			Assert.That(far.TryGetContact(source, out _), Is.False);
			Assert.AreEqual(1, WorldSoundHub.LastPublishDeliveryCount);
		}

		[Test]
		public void Hub_TenListeners_FanOutWithoutRaycast()
		{
			Transform source = Spawn("S16PSrc", new Vector3(5f, 0f, 0f)).transform;
			var listeners = new DetectionProcessor[10];
			int expected = 0;
			for (int i = 0; i < 10; i++)
			{
				listeners[i] = SpawnObserver("S16L" + i).GetComponent<DetectionProcessor>();
				listeners[i].transform.position = i < 3
					? new Vector3(i * 2f, 0f, 0f)
					: new Vector3(350f + i, 0f, 0f);
				if (i < 3)
					expected++;
			}

			WorldSoundHub.PublishGunshot(source, source.position);
			int granted = 0;
			for (int i = 0; i < 10; i++)
			{
				if (listeners[i].TryGetContact(source, out _))
					granted++;
			}

			Assert.AreEqual(expected, granted);
			Assert.AreEqual(expected, WorldSoundHub.LastPublishDeliveryCount);
			Assert.IsFalse(HubUsesRaycast());
		}

		[Test]
		public void Live_FireAudio_PublishesWithoutClip()
		{
			DetectionProcessor listener = SpawnObserver("S16LiveObs").GetComponent<DetectionProcessor>();
			listener.transform.position = Vector3.zero;
			GameObject shooter = Spawn("S16Shooter", new Vector3(12f, 0f, 0f));
			UnitWeaponFireAudio audio = shooter.AddComponent<UnitWeaponFireAudio>();
			MethodInfo handle = typeof(UnitWeaponFireAudio).GetMethod(
				"HandleShotFired",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(handle);
			handle.Invoke(audio, new object[] { null });
			Assert.That(listener.TryGetContact(shooter.transform, out PerceivedContact contact), Is.True);
			Assert.Greater(contact.SoundConfidence, 0.9f);
		}

		[Test]
		public void Architecture_OneUnitVision_SoundNotInQ()
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
			Assert.AreEqual(300f, SoundEvidenceMath.GunshotRangeMeters, 0.01f);
			Assert.AreEqual(500f, SoundEvidenceMath.ExplosionRangeMeters, 0.01f);
			Assert.AreEqual(25f, SoundEvidenceMath.FootstepRangeMeters, 0.01f);
			Assert.AreEqual(40f, SoundEvidenceMath.ImpactRangeMeters, 0.01f);
		}

		[Test]
		public void ResolveBelievedPosition_SoundOnly_UsesSoundPosition()
		{
			var contact = new PerceivedContact
			{
				ObservationState = ObservationState.NotObserved,
				LastSeenConfidence = 0f,
				LastKnownPosition = Vector3.zero,
				SoundConfidence = 0.8f,
				SoundPosition = new Vector3(9f, 0f, 2f)
			};
			Assert.AreEqual(contact.SoundPosition, TargetSelectionMath.ResolveBelievedPosition(contact));
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

		private static bool HubUsesRaycast()
		{
			MethodInfo[] methods = typeof(WorldSoundHub).GetMethods(
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
