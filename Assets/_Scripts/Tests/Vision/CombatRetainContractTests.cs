using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// Vision Stage 14: reload/misfire retain uses ResolvedMaxRange, not 18 m.
	/// Does not retune Q, ScopeVisionRange, E, SELECT ranking, or memory timers.
	/// </summary>
	public sealed class CombatRetainContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.011f;
		#endregion

		#region Tests
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
			Assert.AreEqual(0f, weapons["Weapon_MK19"].OpticVisionRangeMeters, c_Tol);
			Assert.AreEqual(0f, weapons["Weapon_M2Browning_127"].OpticVisionRangeMeters, c_Tol);
		}

		[Test]
		public void InfantryEye_RetainInsideEnvelope_Not18()
		{
			float range = InfantryRange(false);
			Assert.AreEqual(150f, range, c_Tol);
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(20f, range));
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(80f, range));
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(149f, range));
			Assert.IsFalse(CombatRetainMath.CanRetainAtDistance(151f, range));
			Assert.Greater(Mathf.Abs(range - 18f), 1f);
		}

		[Test]
		public void Scope9_RetainFollowsResolvedMaxRange()
		{
			float range = InfantryRange(true);
			Assert.AreEqual(300f, range, c_Tol);
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(250f, range));
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(300f, range));
			Assert.IsFalse(CombatRetainMath.CanRetainAtDistance(301f, range));
		}

		[Test]
		public void PassengerAndTurret_UseSameResolvedSourceRange()
		{
			float passenger = UnitVisionProfile.ResolveForSource(
				VisionSourceKind.Passenger,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.PointAim,
				null,
				true,
				0f).MaxRangeMeters;
			float turret = UnitVisionProfile.ResolveForSource(
				VisionSourceKind.Turret,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.PointAim,
				null,
				false,
				0f).MaxRangeMeters;
			float turretOptic = UnitVisionProfile.ResolveForSource(
				VisionSourceKind.Turret,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.PointAim,
				null,
				false,
				250f).MaxRangeMeters;

			Assert.AreEqual(150f, passenger, c_Tol);
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(80f, passenger));
			Assert.AreEqual(150f, turret, c_Tol);
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(149f, turret));
			Assert.IsFalse(CombatRetainMath.CanRetainAtDistance(151f, turret));
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(250f, turretOptic));
			Assert.IsFalse(CombatRetainMath.CanRetainAtDistance(251f, turretOptic));
		}

		[Test]
		public void SelectScoring_IsNotCutByOld18()
		{
			ContactSelectionPolicy policy = ContactSelectionPolicy.CreateDefault();
			var farObserved = new PerceivedContact
			{
				ObservationState = ObservationState.Observed,
				LastSeenConfidence = 1f,
				LastKnownPosition = new Vector3(80f, 0f, 0f),
				Threat = ThreatLevel.None
			};
			float score = TargetSelectionMath.Score(farObserved, Vector3.zero, policy);
			Assert.Greater(score, 0f);
			Assert.IsFalse(CombatRetainMath.CanRetainAtDistance(80f, 18f));
			Assert.IsTrue(CombatRetainMath.CanRetainAtDistance(80f, 150f));
		}

		[Test]
		public void LastKnown_IsNotRetainAimPoint()
		{
			Vector3 origin = Vector3.zero;
			Assert.IsFalse(ProjectileLaunchPermit.TryAuthorize(
				false, origin, new Vector3(0f, 0f, 80f), 150f, true, true, false,
				out ProjectileLaunchDeny deny));
			Assert.AreEqual(ProjectileLaunchDeny.NoAimPoint, deny);
		}

		[Test]
		public void Architecture_NoMaxEngageRangeField()
		{
			Assert.IsNull(
				typeof(TargetSelector).GetField(
					"m_MaxEngageRange",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
			Assert.Greater(
				Mathf.Abs(CombatRetainMath.ResolveRetainRangeMeters(150f) - 18f),
				1f);
		}
		#endregion

		#region Private Methods
		private static float InfantryRange(bool _scope9Aiming)
		{
			WeaponAttachmentDefinition[] optics = null;
			if (_scope9Aiming)
			{
				WeaponAttachmentDefinition scope9 = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
				scope9.SetScopeVisionRangeMeters(300f);
				optics = new[] { scope9 };
				float range = UnitVisionProfile.ResolveForSource(
					VisionSourceKind.InfantryEye,
					UnitVisionProfile.BaseRangeMeters,
					UnitVisionProfile.BaseFovDegrees,
					WeaponPoseState.Aiming,
					optics,
					false,
					0f).MaxRangeMeters;
				UnityEngine.Object.DestroyImmediate(scope9);
				return range;
			}

			return UnitVisionProfile.ResolveForSource(
				VisionSourceKind.InfantryEye,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				WeaponPoseState.PointAim,
				null,
				false,
				0f).MaxRangeMeters;
		}

		private static RocketLauncherData LoadRockets()
		{
			return AssetDatabase.LoadAssetAtPath<RocketLauncherData>(
				"Assets/GameData/Combat/RocketLauncherData.asset");
		}

		private static Dictionary<string, WeaponDefinition> LoadCombatWeapons()
		{
			var map = new Dictionary<string, WeaponDefinition>();
			string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
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
			string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
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
