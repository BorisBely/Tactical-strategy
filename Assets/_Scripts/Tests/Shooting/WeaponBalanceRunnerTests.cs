using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Shooting.Tests
{
	/// <summary>Phase G runner contract tests (G-TEST-1..10).</summary>
	public sealed class WeaponBalanceRunnerTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.02f;
		#endregion

		#region Tests
		[Test]
		public void G_TEST_1_SameCase_IdenticalRecoil()
		{
			WeaponBalanceCase balanceCase = CreateM4BaselineCase();
			RecoilSampleResult a = WeaponBalanceRecoilPass.Evaluate(balanceCase);
			RecoilSampleResult b = WeaponBalanceRecoilPass.Evaluate(balanceCase);
			Assert.AreEqual(a.OffsetMagAfter5, b.OffsetMagAfter5, 0.00001f);
		}

		[Test]
		public void G_TEST_2_M4Baseline_MatchesRecoilContract()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			Assert.IsNotNull(m4);
			WeaponBalanceCase balanceCase = CreateM4BaselineCase(m4);
			RecoilSampleResult sample = WeaponBalanceRecoilPass.Evaluate(balanceCase);
			WeaponRecoilBalanceContract.Metrics contract = WeaponRecoilBalanceContract.EvaluateBaseline(
				m4,
				WeaponFireMode.FullAuto);
			Assert.That(sample.OffsetMagAfter5, Is.EqualTo(contract.OffsetMagnitudeAfter5Shots).Within(c_Tol));
			Assert.That(sample.OffsetMagAfter3, Is.EqualTo(contract.OffsetMagnitudeAfter3Shots).Within(c_Tol));
		}

		[Test]
		public void G_TEST_3_M4Baseline_ScoresPass()
		{
			WeaponBalanceCase balanceCase = CreateM4BaselineCase();
			RecoilSampleResult recoil = WeaponBalanceRecoilPass.Evaluate(balanceCase);
			WeaponBalanceScore score = WeaponBalanceScore.Evaluate(balanceCase, recoil);
			Assert.AreNotEqual(WeaponBalanceBandLevel.High, score.Total);
		}

		[Test]
		public void G_TEST_4_InvalidAttachment_NotInLoadout()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			WeaponDefinition ak = LoadWeapon("Weapon_AK47");
			WeaponAttachmentDefinition akModule = LoadAttachment("Attachment_AK_MuzzleBrakeAK");
			Assert.IsNotNull(m4);
			Assert.IsNotNull(ak);
			Assert.IsNotNull(akModule);
			Assert.IsFalse(WeaponBalanceLoadoutGenerator.IsAttachmentValidForWeapon(
				m4,
				akModule,
				WeaponAttachmentSlotType.Muzzle,
				0));

			WeaponBalanceRunConfig config = WeaponBalanceRunConfig.CreateAttachmentsPreset();
			var catalog = new List<WeaponAttachmentDefinition> { akModule };
			IReadOnlyList<WeaponBalanceLoadout> loadouts =
				WeaponBalanceLoadoutGenerator.Generate(m4, catalog, config);
			for (int i = 0; i < loadouts.Count; i++)
			{
				WeaponAttachmentDefinition[] attachments = loadouts[i].Attachments;
				if (attachments == null)
					continue;
				for (int a = 0; a < attachments.Length; a++)
					Assert.AreNotSame(akModule, attachments[a]);
			}
		}

		[Test]
		public void G_TEST_5_Turret_SkipsInvalidMovement()
		{
			WeaponDefinition m2 = LoadWeapon("Weapon_M2Browning_127");
			Assert.IsNotNull(m2);
			Assert.IsTrue(WeaponBalanceCaseValidator.IsTurretWeapon(m2));
			var walkCase = new WeaponBalanceCase(
				m2,
				WeaponFireMode.FullAuto,
				m2.BuiltInMagazineDefaultAmmo,
				null,
				"Base",
				WeaponPoseState.Aiming,
				WeaponBalanceStance.Standing,
				WeaponBalanceMovement.Walk,
				50f,
				50f,
				true);
			Assert.IsFalse(WeaponBalanceCaseValidator.IsValid(in walkCase, WeaponBalanceRunConfig.CreateReferencePreset()));
		}

		[Test]
		public void G_TEST_6_Theta_InvariantAcrossSkill()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			Assert.IsNotNull(m4);
			WeaponBalanceCase skill0 = CreateM4BaselineCase(m4, 0f);
			WeaponBalanceCase skill100 = CreateM4BaselineCase(m4, 100f);
			float theta0 = WeaponBalanceAccuracyPass.Evaluate(skill0).ThetaHalfAngleDegrees;
			float theta100 = WeaponBalanceAccuracyPass.Evaluate(skill100).ThetaHalfAngleDegrees;
			Assert.AreEqual(theta0, theta100, 0.0001f);
		}

		[Test]
		public void G_TEST_7_Pause_DoesNotResetShotProgression()
		{
			WeaponBalanceCase balanceCase = CreateM4BaselineCase();
			WeaponRecoilContext context = WeaponBalanceContextFactory.CreateRecoilContext(balanceCase);
			float afterPause = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(in context, 5, 0.4f).magnitude;
			float afterSixthShot = WeaponRecoilMath.PredictOffsetAfterShots(in context, 6).magnitude;
			Assert.Greater(afterSixthShot, afterPause + 0.001f);
		}

		[Test]
		public void G_TEST_8_Runner_DoesNotMutateAssets()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			Assert.IsNotNull(m4);
			float beforeV = m4.VerticalRecoil;
			WeaponBalanceRunConfig config = WeaponBalanceRunConfig.CreateSmokePreset();
			var weapons = new List<WeaponDefinition> { m4 };
			WeaponBalanceRunner.Run(config, weapons, new List<WeaponAttachmentDefinition>(), "Smoke");
			Assert.AreEqual(beforeV, m4.VerticalRecoil, 0.00001f);
		}

		[Test]
		public void G_TEST_9_AutoSelector_MatchesN9ContractPath()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			Assert.IsNotNull(m4);
			var balanceCase = new WeaponBalanceCase(
				m4,
				WeaponFireMode.Auto,
				m4.BuiltInMagazineDefaultAmmo,
				null,
				"Base",
				WeaponPoseState.Aiming,
				WeaponBalanceStance.Standing,
				WeaponBalanceMovement.Idle,
				50f,
				50f,
				false);
			FireControlSampleResult fire = WeaponBalanceFireControlPass.Evaluate(
				balanceCase,
				WeaponBalanceRunConfig.CreateReferencePreset());
			var scenario = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(
				m4,
				m4.BuiltInMagazineDefaultAmmo,
				50f);
			var n9 = WeaponAutoModeSelectionUtility.Select(
				RecoilAutoSelectorInputBuilder.BuildContract(scenario));
			Assert.AreEqual(n9.EffectiveFireMode, fire.SelectedAutoFireMode);
			Assert.AreEqual(n9.EffectiveAimMode, fire.SelectedAutoAimMode);
		}

		[Test]
		public void G_TEST_10_SelectorAndPlanner_ThresholdsDiffer()
		{
			Assert.AreNotEqual(
				FireControlSampleResult.SelectorThresholdMeters,
				FireControlSampleResult.PlannerCapMeters);
			Assert.That(
				Mathf.Abs(FireControlSampleResult.SelectorThresholdMeters - FireControlSampleResult.PlannerCapMeters),
				Is.GreaterThan(0.01f));
			Assert.That(FireControlSampleResult.SelectorThresholdMeters, Is.EqualTo(0.775f).Within(0.01f));
			Assert.That(FireControlSampleResult.PlannerCapMeters, Is.EqualTo(0.51f).Within(0.01f));
		}
		#endregion

		#region Private Methods
		private static WeaponBalanceCase CreateM4BaselineCase()
		{
			WeaponDefinition m4 = LoadWeapon(WeaponRecoilBalanceContract.ReferenceWeaponAssetName);
			Assert.IsNotNull(m4);
			return CreateM4BaselineCase(m4, 50f);
		}

		private static WeaponBalanceCase CreateM4BaselineCase(WeaponDefinition _m4, float _skill = 50f)
		{
			return new WeaponBalanceCase(
				_m4,
				WeaponFireMode.FullAuto,
				_m4.BuiltInMagazineDefaultAmmo,
				null,
				"Base",
				WeaponPoseState.Aiming,
				WeaponBalanceStance.Standing,
				WeaponBalanceMovement.Idle,
				50f,
				_skill,
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

		private static WeaponAttachmentDefinition LoadAttachment(string _assetName)
		{
			string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition " + _assetName, new[] { c_ShootingFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				WeaponAttachmentDefinition attachment =
					AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
				if (attachment != null && attachment.name == _assetName)
					return attachment;
			}

			return null;
		}
		#endregion
	}
}
