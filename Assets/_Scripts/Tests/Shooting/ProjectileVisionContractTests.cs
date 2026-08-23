using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Shooting.Tests
{
	/// <summary>
	/// Vision Stage 12: assignment envelope is Observed VisionRange; projectile life is not clipped.
	/// Does not retune Q, ScopeVisionRange, E, Accuracy, AimTime, Fire Discipline, or rocket speed/life.
	/// </summary>
	public sealed class ProjectileVisionContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.011f;
		private static readonly float[] s_Distances =
		{
			50f, 100f, 149f, 150f, 151f, 200f, 250f, 300f
		};
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
			Assert.AreEqual(130f, rockets.GetMuzzleSpeed(RocketLauncherType.Disposable), c_Tol);
			Assert.AreEqual(12f, rockets.ProjectileLifetimeSeconds, c_Tol);
			Assert.AreEqual(240f, ProjectileLaunchPermit.Mk19MuzzleSpeed, c_Tol);
			Assert.AreEqual(25f, ProjectileLaunchPermit.Mk19LifetimeSeconds, c_Tol);
		}

		[Test]
		public void ObservedInsideEyeEnvelope_AllowsLaunch()
		{
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(50f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(100f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(149f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(150f, 150f));
		}

		[Test]
		public void ObservedBeyondEyeEnvelope_DeniesOutsideVision()
		{
			Assert.AreEqual(ProjectileLaunchDeny.OutsideVision, AuthorizeObserved(151f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.OutsideVision, AuthorizeObserved(200f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.OutsideVision, AuthorizeObserved(250f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.OutsideVision, AuthorizeObserved(300f, 150f));
		}

		[Test]
		public void MagnifiedOptic_ExtendsAssignmentEnvelopeNotLifetime()
		{
			Assert.AreEqual(ProjectileLaunchDeny.OutsideVision, AuthorizeObserved(250f, 150f));
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(250f, 300f));
			Assert.AreEqual(ProjectileLaunchDeny.None, AuthorizeObserved(300f, 300f));
			Assert.Greater(
				ProjectileLaunchPermit.TheoreticalPhysicalRangeMeters(115f, 12f),
				300f);
		}

		[Test]
		public void RecentlyLostAndLost_NeverAimPoint()
		{
			Vector3 origin = Vector3.zero;
			Vector3 lastKnown = new Vector3(0f, 0f, 140f);
			Assert.IsFalse(ProjectileLaunchPermit.TryAuthorize(
				false, origin, lastKnown, 150f, true, true, false, out ProjectileLaunchDeny lost));
			Assert.AreEqual(ProjectileLaunchDeny.NoAimPoint, lost);

			Assert.IsFalse(ProjectileLaunchPermit.TryAuthorize(
				false, origin, new Vector3(0f, 0f, 300f), 150f, true, true, false,
				out ProjectileLaunchDeny farMemory));
			Assert.AreEqual(ProjectileLaunchDeny.NoAimPoint, farMemory);
		}

		[Test]
		public void G6TrackAndBlockedLos_DenyLaunch()
		{
			Assert.AreEqual(
				ProjectileLaunchDeny.NotG6Fire,
				Authorize(true, 80f, 150f, true, false, false));
			Assert.AreEqual(
				ProjectileLaunchDeny.NoLOS,
				Authorize(true, 80f, 150f, true, true, true));
		}

		[Test]
		public void PhysicalLifetime_IsNotClippedToVisionRange()
		{
			float rpgReach = ProjectileLaunchPermit.TheoreticalPhysicalRangeMeters(
				ProjectileLaunchPermit.RpgMuzzleSpeed,
				ProjectileLaunchPermit.RocketLifetimeSeconds);
			Assert.AreEqual(1380f, rpgReach, c_Tol);
			Assert.Greater(rpgReach, UnitVisionProfile.BaseRangeMeters);
			Assert.Greater(
				Mathf.Abs(
					ProjectileLaunchPermit.RocketLifetimeSeconds -
					UnitVisionProfile.BaseRangeMeters / ProjectileLaunchPermit.RpgMuzzleSpeed),
				c_Tol);

			float mk19Reach = ProjectileLaunchPermit.TheoreticalPhysicalRangeMeters(
				ProjectileLaunchPermit.Mk19MuzzleSpeed,
				ProjectileLaunchPermit.Mk19LifetimeSeconds);
			Assert.AreEqual(6000f, mk19Reach, c_Tol);
			Assert.Greater(Mathf.Abs(300f - mk19Reach), 1f);
		}

		[Test]
		public void RocketLead_AppliesAfterHitscanProjection()
		{
			Vector3 aim = new Vector3(0f, 0f, 230f);
			Vector3 velocity = new Vector3(10f, 0f, 0f);
			Vector3 led = ProjectileLaunchPermit.ApplyRocketLead(aim, velocity, 230f, 115f);
			Assert.AreEqual(aim.x + 15f, led.x, 0.05f);
			Assert.AreEqual(aim.z, led.z, c_Tol);
		}

		[Test]
		public void Matrix_EyeAndScope9()
		{
			for (int i = 0; i < s_Distances.Length; i++)
			{
				float distance = s_Distances[i];
				ProjectileLaunchDeny eye = AuthorizeObserved(distance, 150f);
				ProjectileLaunchDeny scope = AuthorizeObserved(distance, 300f);
				if (distance <= 150f + 0.01f)
					Assert.AreEqual(ProjectileLaunchDeny.None, eye, distance.ToString("0"));
				else
					Assert.AreEqual(ProjectileLaunchDeny.OutsideVision, eye, distance.ToString("0"));

				Assert.AreEqual(ProjectileLaunchDeny.None, scope, "scope9 " + distance.ToString("0"));
			}
		}
		#endregion

		#region Private Methods
		private static ProjectileLaunchDeny AuthorizeObserved(float _distance, float _vision)
		{
			return Authorize(true, _distance, _vision, true, true, false);
		}

		private static ProjectileLaunchDeny Authorize(
			bool _hasAim,
			float _distance,
			float _vision,
			bool _hasG6,
			bool _g6Fire,
			bool _lof)
		{
			Vector3 origin = Vector3.zero;
			Vector3 aim = _hasAim ? new Vector3(0f, 0f, _distance) : Vector3.zero;
			ProjectileLaunchPermit.TryAuthorize(
				_hasAim,
				origin,
				aim,
				_vision,
				_hasG6,
				_g6Fire,
				_lof,
				out ProjectileLaunchDeny reason);
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
