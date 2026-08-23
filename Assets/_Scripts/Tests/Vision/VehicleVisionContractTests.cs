using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// Vision Stage 13: passenger = infantry envelope (not 100 m); turret optic or 150, not Aiming.
	/// Does not retune Q, ScopeVisionRange, E, Accuracy, AimTime, Fire Discipline, or rocket life.
	/// </summary>
	public sealed class VehicleVisionContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.011f;
		#endregion

		#region Private Fields
		private WeaponAttachmentDefinition m_Scope9;
		#endregion

		#region Setup
		[SetUp]
		public void SetUp()
		{
			m_Scope9 = ScriptableObject.CreateInstance<WeaponAttachmentDefinition>();
			m_Scope9.SetScopeVisionRangeMeters(300f);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Scope9 != null)
				UnityEngine.Object.DestroyImmediate(m_Scope9);
		}
		#endregion

		#region Tests
		[Test]
		public void Frozen_E_ScopeVision_AimTime_RocketLife_TurretOpticZero()
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
		public void InfantryEye_NoOptic_149Visible_151Not()
		{
			ResolvedVisionProfile profile = Infantry(WeaponPoseState.PointAim, null);
			Assert.AreEqual(150f, profile.MaxRangeMeters, c_Tol);
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(149f, profile.MaxRangeMeters));
			Assert.IsFalse(UnitVisionProfile.IsWithinResolvedRange(151f, profile.MaxRangeMeters));
		}

		[Test]
		public void Passenger_IsNotCappedAt100()
		{
			ResolvedVisionProfile profile = Passenger(false, WeaponPoseState.PointAim, null);
			Assert.Greater(Mathf.Abs(profile.MaxRangeMeters - 100f), 1f);
			Assert.AreEqual(150f, profile.MaxRangeMeters, c_Tol);
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(99f, profile.MaxRangeMeters));
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(101f, profile.MaxRangeMeters));
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(149f, profile.MaxRangeMeters));
			Assert.IsFalse(UnitVisionProfile.IsWithinResolvedRange(151f, profile.MaxRangeMeters));
		}

		[Test]
		public void InfantryBesidePassenger_BothSee101()
		{
			ResolvedVisionProfile infantry = Infantry(WeaponPoseState.PointAim, null);
			ResolvedVisionProfile passenger = Passenger(true, WeaponPoseState.PointAim, null);
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(101f, infantry.MaxRangeMeters));
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(101f, passenger.MaxRangeMeters));
		}

		[Test]
		public void Passenger_Scope9Ready_Sees250_NotReadyStays150()
		{
			WeaponAttachmentDefinition[] optics = { m_Scope9 };
			ResolvedVisionProfile ready = Passenger(true, WeaponPoseState.PointAim, optics);
			Assert.IsTrue(ready.IsScopeActive);
			Assert.AreEqual(300f, ready.MaxRangeMeters, c_Tol);
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(250f, ready.MaxRangeMeters));

			ResolvedVisionProfile notReady = Passenger(false, WeaponPoseState.PointAim, optics);
			Assert.IsFalse(notReady.IsScopeActive);
			Assert.AreEqual(150f, notReady.MaxRangeMeters, c_Tol);
			Assert.IsFalse(UnitVisionProfile.IsWithinResolvedRange(250f, notReady.MaxRangeMeters));
		}

		[Test]
		public void Infantry_Scope9_RequiresAiming()
		{
			WeaponAttachmentDefinition[] optics = { m_Scope9 };
			ResolvedVisionProfile point = Infantry(WeaponPoseState.PointAim, optics);
			Assert.IsFalse(point.IsScopeActive);
			Assert.AreEqual(150f, point.MaxRangeMeters, c_Tol);

			ResolvedVisionProfile aiming = Infantry(WeaponPoseState.Aiming, optics);
			Assert.IsTrue(aiming.IsScopeActive);
			Assert.AreEqual(300f, aiming.MaxRangeMeters, c_Tol);
		}

		[Test]
		public void Turret_NoOptic_149Visible_151Not_IgnoresAiming()
		{
			ResolvedVisionProfile point = Turret(0f, WeaponPoseState.PointAim);
			ResolvedVisionProfile aiming = Turret(0f, WeaponPoseState.Aiming);
			Assert.AreEqual(150f, point.MaxRangeMeters, c_Tol);
			Assert.AreEqual(150f, aiming.MaxRangeMeters, c_Tol);
			Assert.IsFalse(point.IsScopeActive);
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(149f, point.MaxRangeMeters));
			Assert.IsFalse(UnitVisionProfile.IsWithinResolvedRange(151f, point.MaxRangeMeters));
		}

		[Test]
		public void Turret_InjectedOptic250_VisibleWithoutAiming()
		{
			ResolvedVisionProfile profile = Turret(250f, WeaponPoseState.PointAim);
			Assert.IsTrue(profile.IsScopeActive);
			Assert.AreEqual(250f, profile.MaxRangeMeters, c_Tol);
			Assert.IsTrue(UnitVisionProfile.IsWithinResolvedRange(250f, profile.MaxRangeMeters));
			Assert.IsFalse(UnitVisionProfile.IsWithinResolvedRange(251f, profile.MaxRangeMeters));
		}

		[Test]
		public void Mk19AndRpg_PermitRegressionUnchanged()
		{
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(149f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.OutsideVision, AuthorizeObserved(151f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(250f, 300f));

			Vector3 origin = Vector3.zero;
			Assert.IsFalse(ProjectileLaunchPermit.TryAuthorize(
				false, origin, new Vector3(0f, 0f, 140f), 150f, true, true, false,
				out ProjectileLaunchDeny lost));
			Assert.AreEqual(ProjectileLaunchDeny.NoAimPoint, lost);
		}

		[Test]
		public void Architecture_OneVisionSystem_NoPassenger100Field()
		{
			Assert.IsNull(
				typeof(VehiclePassengerFireValidator).GetField(
					"m_MaxFireRange",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));

			Type[] types = typeof(UnitVision).Assembly.GetTypes();
			int extraVision = 0;
			for (int i = 0; i < types.Length; i++)
			{
				if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
					extraVision++;
			}

			Assert.AreEqual(0, extraVision);
		}
		#endregion

		#region Private Methods
		private static ResolvedVisionProfile Infantry(
			WeaponPoseState _pose,
			WeaponAttachmentDefinition[] _optics)
		{
			return UnitVisionProfile.ResolveForSource(
				VisionSourceKind.InfantryEye,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				_pose,
				_optics,
				false,
				0f);
		}

		private static ResolvedVisionProfile Passenger(
			bool _ready,
			WeaponPoseState _pose,
			WeaponAttachmentDefinition[] _optics)
		{
			return UnitVisionProfile.ResolveForSource(
				VisionSourceKind.Passenger,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				_pose,
				_optics,
				_ready,
				0f);
		}

		private static ResolvedVisionProfile Turret(float _opticMeters, WeaponPoseState _pose)
		{
			return UnitVisionProfile.ResolveForSource(
				VisionSourceKind.Turret,
				UnitVisionProfile.BaseRangeMeters,
				UnitVisionProfile.BaseFovDegrees,
				_pose,
				null,
				false,
				_opticMeters);
		}

		private static ProjectileLaunchDeny AuthorizeObserved(float _distance, float _vision)
		{
			Vector3 origin = Vector3.zero;
			Vector3 aim = new Vector3(0f, 0f, _distance);
			ProjectileLaunchPermit.TryAuthorize(
				true, origin, aim, _vision, true, true, false, out ProjectileLaunchDeny reason);
			return reason;
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
				if (path.Replace('\\', '/').IndexOf("/Test/", System.StringComparison.OrdinalIgnoreCase) >= 0)
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
				if (path.Replace('\\', '/').IndexOf("/Test/", System.StringComparison.OrdinalIgnoreCase) >= 0)
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
