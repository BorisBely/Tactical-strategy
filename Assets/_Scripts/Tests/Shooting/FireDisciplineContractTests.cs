using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Shooting.Tests
{
	/// <summary>
	/// Vision Stage 11: Fire Discipline uses the class working envelope, not 25/70/140/220.
	/// Does not retune Q, ScopeVisionRange, E, Accuracy, AimTime, or pose floors.
	/// </summary>
	public sealed class FireDisciplineContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.011f;
		private static readonly float[] s_Distances =
		{
			10f, 25f, 50f, 100f, 150f, 200f, 225f, 250f, 300f
		};
		#endregion

		#region Tests
		[Test]
		public void Frozen_E_ScopeVision_AimTime_PoseFloors()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			Dictionary<string, WeaponAttachmentDefinition> optics = LoadCombatOptics();
			Assert.AreEqual(140f, weapons["Weapon_M4_ModA_1"].EffectiveRangeMeters, c_Tol);
			Assert.AreEqual(225f, weapons["Weapon_Sniper762x51"].EffectiveRangeMeters, c_Tol);
			Assert.AreEqual(150f, optics["Attachment_M4_Reddot1"].ScopeVisionRangeMeters, c_Tol);
			Assert.AreEqual(300f, optics["Attachment_M4_Scope9"].ScopeVisionRangeMeters, c_Tol);
			Assert.AreEqual(1.55f, optics["Attachment_M4_Scope9"].AimTimeModifier, 0.011f);
			Assert.AreEqual(0.35f, WeaponAimModeUtility.SnapShotAimProgress01, c_Tol);
			Assert.AreEqual(0.68f, WeaponAimModeUtility.QuickAimProgress01, c_Tol);
			Assert.AreEqual(1.00f, WeaponAimModeUtility.FullAimProgress01, c_Tol);
		}

		[Test]
		public void Profiles_UseClassWorkingRange_NotVisionOrE()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			WeaponDefinition cqb = weapons["Weapon_AK74U"];
			WeaponDefinition assault = weapons["Weapon_M4_ModA_1"];
			WeaponDefinition lmg = weapons["Weapon_M249"];
			WeaponDefinition marksman = weapons["Weapon_SVD"];
			WeaponDefinition sniper = weapons["Weapon_Sniper762x51"];

			Assert.AreEqual(WeaponFireDisciplineProfileKind.Cqb, WeaponFireDisciplineProfile.ResolveKind(cqb));
			Assert.AreEqual(WeaponFireDisciplineProfileKind.Assault, WeaponFireDisciplineProfile.ResolveKind(assault));
			Assert.AreEqual(WeaponFireDisciplineProfileKind.Lmg, WeaponFireDisciplineProfile.ResolveKind(lmg));
			Assert.AreEqual(WeaponFireDisciplineProfileKind.Marksman, WeaponFireDisciplineProfile.ResolveKind(marksman));
			Assert.AreEqual(WeaponFireDisciplineProfileKind.Sniper, WeaponFireDisciplineProfile.ResolveKind(sniper));

			Assert.AreEqual(150f, WeaponFireDisciplineProfile.GetWorkingRangeMeters(cqb), c_Tol);
			Assert.AreEqual(200f, WeaponFireDisciplineProfile.GetWorkingRangeMeters(assault), c_Tol);
			Assert.AreEqual(220f, WeaponFireDisciplineProfile.GetWorkingRangeMeters(lmg), c_Tol);
			Assert.AreEqual(250f, WeaponFireDisciplineProfile.GetWorkingRangeMeters(marksman), c_Tol);
			Assert.AreEqual(300f, WeaponFireDisciplineProfile.GetWorkingRangeMeters(sniper), c_Tol);

			Assert.AreNotEqual(assault.EffectiveRangeMeters, WeaponFireDisciplineProfile.GetWorkingRangeMeters(assault));
			Assert.AreNotEqual(sniper.EffectiveRangeMeters, WeaponFireDisciplineProfile.GetWorkingRangeMeters(sniper));
		}

		[Test]
		public void Bands_HaveEnterExitHysteresis()
		{
			Assert.AreEqual(
				WeaponFireDisciplineDistanceBand.Close,
				WeaponFireDisciplineProfile.ResolveBand(0.19f, null));
			Assert.AreEqual(
				WeaponFireDisciplineDistanceBand.Near,
				WeaponFireDisciplineProfile.ResolveBand(0.20f, null));
			Assert.AreEqual(
				WeaponFireDisciplineDistanceBand.Near,
				WeaponFireDisciplineProfile.ResolveBand(0.19f, WeaponFireDisciplineDistanceBand.Near));
			Assert.AreEqual(
				WeaponFireDisciplineDistanceBand.Close,
				WeaponFireDisciplineProfile.ResolveBand(0.11f, WeaponFireDisciplineDistanceBand.Near));
			Assert.AreEqual(
				WeaponFireDisciplineDistanceBand.Mid,
				WeaponFireDisciplineProfile.ResolveBand(0.40f, WeaponFireDisciplineDistanceBand.Mid));
			Assert.AreEqual(
				WeaponFireDisciplineDistanceBand.Near,
				WeaponFireDisciplineProfile.ResolveBand(0.36f, WeaponFireDisciplineDistanceBand.Mid));
		}

		[Test]
		public void OldSeventyMeters_IsNotABandEdgeForAssault()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			WeaponFireDisciplinePlan a = Plan(weapons["Weapon_M4_ModA_1"], 69f);
			WeaponFireDisciplinePlan b = Plan(weapons["Weapon_M4_ModA_1"], 71f);
			Assert.AreEqual(a.DistanceBand, b.DistanceBand);
			Assert.AreEqual(WeaponFireDisciplineDistanceBand.Near, a.DistanceBand);
		}

		[Test]
		public void Character_CqbAssaultLmgMarksmanSniper()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			WeaponFireDisciplinePlan cqb10 = Plan(weapons["Weapon_AK74U"], 10f);
			Assert.AreEqual(WeaponFireMode.FullAuto, cqb10.EffectiveFireMode);
			Assert.GreaterOrEqual(cqb10.SeriesShotCount, 3);
			Assert.Less(cqb10.RequiredAimProgress01, 0.70f);

			WeaponFireDisciplinePlan assault100 = Plan(weapons["Weapon_M4_ModA_1"], 100f);
			Assert.IsTrue(
				assault100.EffectiveFireMode == WeaponFireMode.Burst ||
				assault100.EffectiveFireMode == WeaponFireMode.FullAuto);
			Assert.GreaterOrEqual(assault100.SeriesShotCount, 2);
			Assert.LessOrEqual(assault100.SeriesShotCount, 4);

			WeaponFireDisciplinePlan assault150 = Plan(weapons["Weapon_M4_ModA_1"], 150f);
			Assert.AreEqual(WeaponFireMode.SemiAuto, assault150.EffectiveFireMode);
			Assert.LessOrEqual(assault150.SeriesShotCount, 2);
			Assert.GreaterOrEqual(assault150.RequiredAimProgress01, 0.82f);

			WeaponFireDisciplinePlan lmg150 = Plan(weapons["Weapon_M249"], 150f);
			Assert.AreEqual(WeaponFireMode.FullAuto, lmg150.EffectiveFireMode);
			Assert.GreaterOrEqual(lmg150.SeriesShotCount, 5);
			Assert.Greater(lmg150.SeriesShotCount, assault150.SeriesShotCount);

			WeaponFireDisciplinePlan marksman150 = Plan(weapons["Weapon_SVD"], 150f);
			Assert.AreEqual(WeaponFireMode.SemiAuto, marksman150.EffectiveFireMode);
			Assert.LessOrEqual(marksman150.SeriesShotCount, 2);
		}

		[Test]
		public void Sniper_NeverSprays_AndDistanceNeverForbidsFire()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			WeaponDefinition sniper = weapons["Weapon_Sniper762x51"];
			for (int i = 0; i < s_Distances.Length; i++)
			{
				WeaponFireDisciplinePlan plan = Plan(sniper, s_Distances[i]);
				Assert.AreEqual(WeaponFireMode.SemiAuto, plan.EffectiveFireMode, s_Distances[i].ToString("0"));
				Assert.AreEqual(1, plan.SeriesShotCount, s_Distances[i].ToString("0"));
				Assert.IsTrue(WeaponFireDisciplineProfile.DoesNotForbidFire(plan.SeriesShotCount));
			}

			WeaponDefinition[] all =
			{
				weapons["Weapon_AK74U"],
				weapons["Weapon_M4_ModA_1"],
				weapons["Weapon_M249"],
				weapons["Weapon_SVD"],
				sniper
			};
			for (int w = 0; w < all.Length; w++)
			{
				for (int i = 0; i < s_Distances.Length; i++)
				{
					WeaponFireDisciplinePlan plan = Plan(all[w], s_Distances[i]);
					Assert.GreaterOrEqual(plan.SeriesShotCount, 1, all[w].name + " " + s_Distances[i]);
				}
			}
		}

		[Test]
		public void AimProgress_RisesWithNormalizedDistance()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			WeaponFireDisciplinePlan close = Plan(weapons["Weapon_M4_ModA_1"], 25f);
			WeaponFireDisciplinePlan far = Plan(weapons["Weapon_M4_ModA_1"], 150f);
			Assert.Greater(far.RequiredAimProgress01, close.RequiredAimProgress01);
		}

		[Test]
		public void AmmoEconomy_ThreeSecondContactDoesNotEmptyMag()
		{
			Dictionary<string, WeaponDefinition> weapons = LoadCombatWeapons();
			WeaponDefinition assault = weapons["Weapon_M4_ModA_1"];
			WeaponDefinition lmg = weapons["Weapon_M249"];
			Assert.Less(EstimateShotsInSeconds(assault, 10f, 3f), 30f);
			Assert.Less(EstimateShotsInSeconds(lmg, 10f, 3f), 80f);
		}
		#endregion

		#region Private Methods
		private static WeaponFireDisciplinePlan Plan(WeaponDefinition _weapon, float _distanceMeters)
		{
			return WeaponFireDisciplinePlanner.CreatePlan(
				_weapon,
				WeaponFireMode.Auto,
				WeaponFireDisciplineMode.Auto,
				_distanceMeters,
				null,
				null,
				null,
				true);
		}

		private static float EstimateShotsInSeconds(WeaponDefinition _weapon, float _distance, float _seconds)
		{
			WeaponFireDisciplinePlan plan = Plan(_weapon, _distance);
			float rpm = plan.EffectiveFireMode == WeaponFireMode.SemiAuto
				? Mathf.Max(1f, _weapon.SemiAutoFireRateRpm)
				: Mathf.Max(1f, _weapon.FireRateRpm);
			float seriesTime = plan.SeriesShotCount * (60f / rpm) + plan.SeriesPauseSeconds;
			return _seconds / Mathf.Max(0.05f, seriesTime) * plan.SeriesShotCount;
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
