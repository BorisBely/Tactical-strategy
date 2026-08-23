using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Shooting.Tests
{
	/// <summary>Phase H report contract tests (H-TEST-1..11).</summary>
	public sealed class WeaponBalanceHRunnerTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.05f;
		#endregion

		#region Tests
		[Test]
		public void H_TEST_1_EachReferenceWeapon_HasSummaryRow()
		{
			WeaponBalanceHReport report = BuildReport();
			Assert.GreaterOrEqual(report.WeaponSummaries.Count, WeaponBalanceRunConfig.ReferenceWeaponAssetNames.Length);
		}

		[Test]
		public void H_TEST_2_M4BaselineRow_Present()
		{
			WeaponBalanceHReport report = BuildReport();
			Assert.IsNotNull(report.M4BaselineRow.Case.Weapon);
			Assert.AreEqual(WeaponRecoilBalanceContract.ReferenceWeaponAssetName, report.M4BaselineRow.Case.Weapon.name);
		}

		[Test]
		public void H_TEST_3_M4RelativeRatio_CorrectForAk47()
		{
			WeaponBalanceHReport report = BuildReport();
			WeaponBalanceWeaponSummary akSummary = default;
			bool foundAk = false;
			for (int i = 0; i < report.WeaponSummaries.Count; i++)
			{
				if (report.WeaponSummaries[i].WeaponName == "Weapon_AK47")
				{
					akSummary = report.WeaponSummaries[i];
					foundAk = true;
					break;
				}
			}

			Assert.IsTrue(foundAk);
			Assert.Greater(akSummary.RelativeToM4.Count, 0);
			float ratio = akSummary.RelativeToM4[0].Ratio;
			Assert.That(ratio, Is.EqualTo(0.750f / 0.313f).Within(c_Tol));
		}

		[Test]
		public void H_TEST_4_ClassGroups_DoNotMixWeaponClasses()
		{
			WeaponBalanceHReport report = BuildReport();
			for (int i = 0; i < report.ClassGroups.Count; i++)
			{
				WeaponBalanceClassGroup group = report.ClassGroups[i];
				for (int w = 0; w < group.WeaponNames.Count; w++)
				{
					WeaponDefinition weapon = LoadWeapon(group.WeaponNames[w]);
					Assert.IsNotNull(weapon);
					Assert.AreEqual(group.ClassType, weapon.WeaponClass);
				}
			}
		}

		[Test]
		public void H_TEST_5_ScoreDetail_ExplainsAxes()
		{
			WeaponBalanceHReport report = BuildReport();
			Assert.Greater(report.WeaponSummaries.Count, 0);
			WeaponBalanceScoreDetail detail = report.WeaponSummaries[0].ScoreDetail;
			Assert.Greater(detail.TotalNumeric, 0f);
			Assert.GreaterOrEqual(detail.Reasons.Count, 4);
		}

		[Test]
		public void H_TEST_6_Verdict_UsesRangeNotPointEquality()
		{
			WeaponBalanceCase m4Case = CreateM4BaselineCase();
			var recoil = WeaponBalanceRecoilPass.Evaluate(m4Case);
			var score = WeaponBalanceScore.Evaluate(m4Case, recoil);
			WeaponBalanceVerdict verdict = WeaponBalanceVerdictResolver.Resolve(
				m4Case,
				recoil,
				score,
				false,
				WeaponBalanceWarnKind.None);
			Assert.AreNotEqual(WeaponBalanceVerdict.Fail, verdict);
		}

		[Test]
		public void H_TEST_7_RawXY_PresentInSummary()
		{
			WeaponBalanceHReport report = BuildReport();
			WeaponBalanceRow m4 = report.M4BaselineRow;
			Assert.That(Mathf.Abs(m4.Recoil.OffsetAfter5.y), Is.GreaterThan(0.01f));
		}

		[Test]
		public void H_TEST_8_AttachmentDelta_FromBase()
		{
			WeaponBalanceHReport report = BuildReport();
			bool found = false;
			for (int i = 0; i < report.LoadoutDeltas.Count; i++)
			{
				WeaponBalanceLoadoutDelta delta = report.LoadoutDeltas[i];
				if (delta.LoadoutLabel == WeaponBalanceComparableKey.CanonicalLoadoutLabel)
					continue;
				Assert.AreNotEqual(0f, delta.BaseOffsetMag5);
				found = true;
				break;
			}

			Assert.IsTrue(found || report.LoadoutDeltas.Count == 0);
		}

		[Test]
		public void H_TEST_9_GOutlier_AppearsInOutlierTable()
		{
			WeaponBalanceHInput input = BuildInput();
			if (input.ReferenceReport.OutlierCount == 0)
				Assert.Ignore("No G outliers in reference run.");

			WeaponBalanceHReport report = WeaponBalanceHReportBuilder.Build(
				in input,
				LoadReferenceWeapons());
			Assert.Greater(report.OutlierRecords.Count, 0);
		}

		[Test]
		public void H_TEST_10_HRun_DoesNotMutateAssets()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			Assert.IsNotNull(m4);
			float before = m4.VerticalRecoil;
			BuildReport();
			Assert.AreEqual(before, m4.VerticalRecoil, 0.00001f);
		}

		[Test]
		public void H_TEST_11_LoadoutDeltas_NoDuplicateKeys()
		{
			WeaponBalanceHReport report = BuildReport();
			var keys = new HashSet<string>();
			bool foundAkForegrip = false;
			for (int i = 0; i < report.LoadoutDeltas.Count; i++)
			{
				WeaponBalanceLoadoutDelta delta = report.LoadoutDeltas[i];
				string key = delta.WeaponName + "|" + delta.LoadoutLabel + "|" + delta.Pose + "|" +
				             delta.Stance + "|" + delta.Movement + "|" + delta.DistanceMeters.ToString("F1") + "|" +
				             delta.FireMode;
				Assert.IsTrue(keys.Add(key), "Duplicate loadout delta key: " + key);
				if (delta.WeaponName == "Weapon_AK47" &&
				    delta.LoadoutLabel != WeaponBalanceComparableKey.CanonicalLoadoutLabel &&
				    delta.Pose == WeaponPoseState.Aiming &&
				    delta.Stance == WeaponBalanceStance.Standing &&
				    delta.Movement == WeaponBalanceMovement.Idle &&
				    Mathf.Approximately(delta.DistanceMeters, WeaponBalanceComparableKey.CanonicalDistanceMeters))
				{
					foundAkForegrip = true;
				}
			}

			Assert.IsTrue(foundAkForegrip || report.LoadoutDeltas.Count == 0);
		}
		#endregion

		#region Private Methods
		private static WeaponBalanceHReport BuildReport()
		{
			List<WeaponDefinition> weapons = LoadReferenceWeapons();
			List<WeaponAttachmentDefinition> attachments = LoadAttachmentCatalog();
			return WeaponBalanceHRunner.Run(weapons, attachments);
		}

		private static WeaponBalanceHInput BuildInput()
		{
			return WeaponBalanceHRunner.RunFrozenGInput(LoadReferenceWeapons(), LoadAttachmentCatalog());
		}

		private static List<WeaponDefinition> LoadReferenceWeapons()
		{
			var list = new List<WeaponDefinition>(WeaponBalanceRunConfig.ReferenceWeaponAssetNames.Length);
			for (int i = 0; i < WeaponBalanceRunConfig.ReferenceWeaponAssetNames.Length; i++)
			{
				WeaponDefinition weapon = LoadWeapon(WeaponBalanceRunConfig.ReferenceWeaponAssetNames[i]);
				if (weapon != null)
					list.Add(weapon);
			}

			return list;
		}

		private static List<WeaponAttachmentDefinition> LoadAttachmentCatalog()
		{
			string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition", new[] { c_ShootingFolder });
			var list = new List<WeaponAttachmentDefinition>(guids.Length);
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				WeaponAttachmentDefinition attachment =
					AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
				if (attachment != null)
					list.Add(attachment);
			}

			return list;
		}

		private static WeaponBalanceCase CreateM4BaselineCase()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			Assert.IsNotNull(m4);
			return new WeaponBalanceCase(
				m4,
				WeaponFireMode.FullAuto,
				m4.BuiltInMagazineDefaultAmmo,
				null,
				"Base",
				WeaponPoseState.Aiming,
				WeaponBalanceStance.Standing,
				WeaponBalanceMovement.Idle,
				50f,
				50f,
				false);
		}

		private static WeaponDefinition LoadWeapon(string _assetName)
		{
			string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition " + _assetName, new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
				if (weapon != null && weapon.name == _assetName)
					return weapon;
			}

			return null;
		}
		#endregion
	}
}
