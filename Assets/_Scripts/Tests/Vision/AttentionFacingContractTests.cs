using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vision.Tests
{
	/// <summary>
	/// Vision Stage 15: Attention is rate-only. Q / memory / FOV unchanged.
	/// </summary>
	public sealed class AttentionFacingContractTests
	{
		#region Constants
		private const string c_ShootingFolder = "Assets/GameData/Shooting";
		private const float c_Tol = 0.011f;
		private const float c_HighQ = 1f;
		#endregion

		#region Tests
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
			Assert.AreEqual(0f, weapons["Weapon_MK19"].OpticVisionRangeMeters, c_Tol);
			Assert.AreEqual(0f, weapons["Weapon_M2Browning_127"].OpticVisionRangeMeters, c_Tol);
		}

		[Test]
		public void Gate_Q24_AnyMul_DoesNotGrow()
		{
			float none = DetectionQualityMath.IntegrateProgress(
				0f, 0.24f, 1f, _attentionMultiplier: 3f);
			Assert.Less(none, 0.0001f);
		}

		[Test]
		public void Gate_Q26_MulBoostsGrowRate()
		{
			float slow = DetectionQualityMath.IntegrateProgress(
				0f, 0.26f, 0.1f, _attentionMultiplier: 1f);
			float fast = DetectionQualityMath.IntegrateProgress(
				0f, 0.26f, 0.1f, _attentionMultiplier: 2.5f);
			Assert.Greater(fast, slow);
			Assert.Greater(slow, 0f);
		}

		[Test]
		public void Curve_CenterFasterThanPeriphery_FloorAt60()
		{
			float m0 = AttentionMath.EvaluateMultiplier(0f);
			float m10 = AttentionMath.EvaluateMultiplier(10f);
			float m20 = AttentionMath.EvaluateMultiplier(20f);
			float m30 = AttentionMath.EvaluateMultiplier(30f);
			float m45 = AttentionMath.EvaluateMultiplier(45f);
			float m60 = AttentionMath.EvaluateMultiplier(60f);
			Assert.Greater(m0, m10);
			Assert.Greater(m10, m20);
			Assert.Greater(m20, m30);
			Assert.Greater(m30, m45);
			Assert.Greater(m30, 1f);
			Assert.AreEqual(1f, m45, 0.001f);
			Assert.AreEqual(1f, m60, 0.001f);
			Assert.LessOrEqual(m0, AttentionMath.MultiplierMax);
			Assert.GreaterOrEqual(m0, 2f);
			Assert.AreEqual(AttentionBand.High, AttentionMath.EvaluateBand(0f));
			Assert.AreEqual(AttentionBand.Low, AttentionMath.EvaluateBand(60f));
		}

		[Test]
		public void Attention_IsNotVisibilityQualityFactor()
		{
			float qA = DetectionQualityMath.VisibilityQuality(1f, 1f, 1f, 1f);
			float qB = DetectionQualityMath.VisibilityQuality(1f, 1f, 1f, 1f);
			Assert.AreEqual(qA, qB);
			Assert.AreEqual(1f, qA, 0.0001f);
			MethodInfo method = typeof(DetectionQualityMath).GetMethod(
				nameof(DetectionQualityMath.VisibilityQuality));
			Assert.AreEqual(4, method.GetParameters().Length);
		}

		[Test]
		public void PeekMatrix_HighQ_FacingOnly()
		{
			Assert.IsFalse(GrowsToDetected(c_HighQ, 0f, 0.10f));
			Assert.IsTrue(GrowsToDetected(c_HighQ, 0f, 0.20f));
			Assert.IsFalse(GrowsToDetected(c_HighQ, 60f, 0.10f));
			Assert.IsFalse(GrowsToDetected(c_HighQ, 60f, 0.30f));
			Assert.IsTrue(GrowsToDetected(c_HighQ, 60f, 0.50f));
			Assert.IsTrue(GrowsToDetected(c_HighQ, 0f, 0.20f));
			Assert.IsFalse(GrowsToDetected(c_HighQ, 45f, 0.20f));
			Assert.Less(
				TimeToDetected(c_HighQ, 0f),
				TimeToDetected(c_HighQ, 30f));
			Assert.Less(
				TimeToDetected(c_HighQ, 0f),
				TimeToDetected(c_HighQ, 45f));
		}

		[Test]
		public void Fairness_FiftyRequests_NobodyStarvedEightFrames()
		{
			VisionScanScheduler.ResetForTests();
			VisionScanScheduler.DetailSlotsPerFrame = 8;
			const int n = 50;
			int[] starve = new int[n];
			int[] consecutive = new int[n];
			int maxConsecutive = 0;
			for (int frame = 0; frame < 20; frame++)
			{
				VisionScanScheduler.BeginFrameForTests(frame);
				for (int i = 0; i < n; i++)
				{
					VisionScanScheduler.RequestDetailSlot(
						i,
						VisionDetailPriorityMath.Score(1f, false, false, false, starve[i]));
				}

				VisionScanScheduler.FlushPendingDetailIfNeeded();
				int granted = 0;
				for (int i = 0; i < n; i++)
				{
					if (VisionScanScheduler.WasGranted(i))
					{
						granted++;
						starve[i] = 0;
						consecutive[i] = 0;
					}
					else
					{
						starve[i]++;
						consecutive[i]++;
						if (consecutive[i] > maxConsecutive)
							maxConsecutive = consecutive[i];
					}
				}

				Assert.AreEqual(8, granted);
			}

			Assert.LessOrEqual(maxConsecutive, VisionDetailPriorityMath.FairnessMaxConsecutiveSkip);
			VisionScanScheduler.ResetForTests();
		}

		[Test]
		public void IntraObserver_CenterBeforePeriphery()
		{
			int cmp = VisionDetailPriorityMath.CompareIntraObserver(
				0f, false, false, 45f, false, false);
			Assert.Less(cmp, 0);
		}

		[Test]
		public void Architecture_NoWatchingWindow_NoSecondVision()
		{
			int extraVision = 0;
			Type[] types = typeof(UnitVision).Assembly.GetTypes();
			for (int i = 0; i < types.Length; i++)
			{
				string name = types[i].Name;
				Assert.IsFalse(
					name.IndexOf("WatchingWindow", StringComparison.OrdinalIgnoreCase) >= 0,
					name);
				Assert.IsFalse(
					name.IndexOf("GuardWindow", StringComparison.OrdinalIgnoreCase) >= 0,
					name);
				if (types[i] != typeof(UnitVision) && typeof(UnitVision).IsAssignableFrom(types[i]))
					extraVision++;
			}

			Assert.AreEqual(0, extraVision);
			Assert.AreEqual(8, VisionLodMath.DefaultDetailSlotsPerFrame);
		}
		#endregion

		#region Private Methods
		private static bool GrowsToDetected(float _quality, float _angleDegrees, float _duration)
		{
			return TimeToDetected(_quality, _angleDegrees) <= _duration + 1e-4f;
		}

		private static float TimeToDetected(float _quality, float _angleDegrees)
		{
			float mul = AttentionMath.EvaluateMultiplier(_angleDegrees);
			float progress = 0f;
			const float dt = 0.01f;
			float t = 0f;
			while (t < 4f)
			{
				progress = DetectionQualityMath.IntegrateProgress(
					progress, _quality, dt, _attentionMultiplier: mul);
				t += dt;
				if (progress >= 1f)
					return t;
			}

			return 99f;
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
			string[] guids = AssetDatabase.FindAssets(
				"t:WeaponAttachmentDefinition", new[] { c_ShootingFolder });
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
