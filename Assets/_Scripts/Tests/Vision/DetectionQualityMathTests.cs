using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class DetectionQualityMathTests
	{
		[Test]
		public void DistanceFactor_CloserIsBetterOrEqual()
		{
			float d10 = DetectionQualityMath.DistanceFactor(10f);
			float d50 = DetectionQualityMath.DistanceFactor(50f);
			float d100 = DetectionQualityMath.DistanceFactor(100f);
			float d400 = DetectionQualityMath.DistanceFactor(400f);

			Assert.GreaterOrEqual(d10, d50);
			Assert.GreaterOrEqual(d50, d100);
			Assert.GreaterOrEqual(d100, d400);
			Assert.AreEqual(1f, d10, 0.0001f);
			Assert.AreEqual(DetectionQualityMath.DefaultFarFactor, d400, 0.0001f);
		}

		[Test]
		public void DistanceCurve_KeysMatchContract()
		{
			Assert.AreEqual(1f, DetectionQualityMath.EvaluateDistanceCurve(0f), 0.001f);
			Assert.AreEqual(1f, DetectionQualityMath.EvaluateDistanceCurve(0.10f), 0.001f);
			Assert.AreEqual(0.98f, DetectionQualityMath.EvaluateDistanceCurve(0.25f), 0.001f);
			Assert.AreEqual(0.82f, DetectionQualityMath.EvaluateDistanceCurve(0.55f), 0.001f);
			Assert.AreEqual(0.50f, DetectionQualityMath.EvaluateDistanceCurve(0.82f), 0.001f);
			Assert.AreEqual(0.38f, DetectionQualityMath.EvaluateDistanceCurve(0.90f), 0.001f);
			Assert.AreEqual(0.32f, DetectionQualityMath.EvaluateDistanceCurve(0.96f), 0.001f);
			Assert.AreEqual(DetectionQualityMath.DefaultFarFactor, DetectionQualityMath.EvaluateDistanceCurve(1f), 0.001f);
			Assert.AreEqual(DetectionQualityMath.DefaultFarFactor, DetectionQualityMath.EvaluateDistanceCurve(2f), 0.001f);
		}

		[Test]
		public void DistanceFactor_RelativeDistanceMatchesAcrossRanges()
		{
			float eyeHalf = DetectionQualityMath.DistanceFactor(75f, 150f);
			float scopeHalf = DetectionQualityMath.DistanceFactor(150f, 300f);
			Assert.AreEqual(eyeHalf, scopeHalf, 0.001f);

			float eyeEdge = DetectionQualityMath.DistanceFactor(150f, 150f);
			float scopeEdge = DetectionQualityMath.DistanceFactor(300f, 300f);
			Assert.AreEqual(eyeEdge, scopeEdge, 0.001f);
			Assert.AreEqual(DetectionQualityMath.DefaultFarFactor, eyeEdge, 0.001f);
		}

		[Test]
		public void DistanceCurve_IsMonotonic()
		{
			float prev = 2f;
			for (int i = 0; i <= 100; i++)
			{
				float t = i / 100f;
				float value = DetectionQualityMath.EvaluateDistanceCurve(t);
				Assert.LessOrEqual(value, prev + 0.0001f);
				prev = value;
			}
		}

		[Test]
		public void FovFactor_CenterBetterThanEdge()
		{
			float c0 = DetectionQualityMath.FovFactor(0f);
			float c30 = DetectionQualityMath.FovFactor(30f);
			float c50 = DetectionQualityMath.FovFactor(50f);

			Assert.GreaterOrEqual(c0, c30);
			Assert.GreaterOrEqual(c30, c50);
			Assert.AreEqual(1f, c0, 0.0001f);
		}

		[Test]
		public void CheapZoneExposure_IsVisibleOverTested()
		{
			Assert.AreEqual(0f, VisibilityChecker.CheapZoneExposure01(0, 0), 0.0001f);
			Assert.AreEqual(0f, VisibilityChecker.CheapZoneExposure01(0, 3), 0.0001f);
			Assert.AreEqual(1f / 3f, VisibilityChecker.CheapZoneExposure01(1, 3), 0.0001f);
			Assert.AreEqual(2f / 3f, VisibilityChecker.CheapZoneExposure01(2, 3), 0.0001f);
			Assert.AreEqual(1f, VisibilityChecker.CheapZoneExposure01(3, 3), 0.0001f);
		}

		[Test]
		public void Exposure_ScalesQualityMonotonically()
		{
			float d = DetectionQualityMath.DistanceFactor(80f);
			float f = DetectionQualityMath.FovFactor(0f);
			float m = DetectionQualityMath.MovementFactor(0f);

			float qFull = DetectionQualityMath.VisibilityQuality(d, f, 1f, m);
			float qHalf = DetectionQualityMath.VisibilityQuality(d, f, 0.5f, m);
			float qLow = DetectionQualityMath.VisibilityQuality(d, f, 0.1f, m);

			Assert.GreaterOrEqual(qFull, qHalf);
			Assert.GreaterOrEqual(qHalf, qLow);
		}

		[Test]
		public void MovementFactor_IdleIsOne_AndNeverBelowOne()
		{
			Assert.AreEqual(1f, DetectionQualityMath.MovementFactor(0f), 0.0001f);
			Assert.GreaterOrEqual(DetectionQualityMath.MovementFactor(1.4f), 1f);
			Assert.GreaterOrEqual(DetectionQualityMath.MovementFactor(4.5f), DetectionQualityMath.MovementFactor(1.4f));
		}

		[Test]
		public void Movement_IncreasesQualityButDoesNotIgnoreBadConditions()
		{
			float d = DetectionQualityMath.DistanceFactor(400f);
			float f = DetectionQualityMath.FovFactor(50f);
			float e = 0.1f;

			float qIdle = DetectionQualityMath.VisibilityQuality(d, f, e, DetectionQualityMath.MovementFactor(0f));
			float qRun = DetectionQualityMath.VisibilityQuality(d, f, e, DetectionQualityMath.MovementFactor(4.5f));

			Assert.Greater(qRun, qIdle);
			Assert.Less(qRun, 0.5f);
		}

		[Test]
		public void Acquire_IsFasterThanLoss_AtSameAbsQualitySwing()
		{
			const float dt = 0.1f;
			float acquire = DetectionQualityMath.IntegrateProgress(0f, 1f, dt);
			float lossFromFull = DetectionQualityMath.IntegrateProgress(1f, 0f, dt);

			Assert.Greater(acquire - 0f, 1f - lossFromFull);
			Assert.AreEqual(DetectionState.Detecting, DetectionQualityMath.ResolveState(acquire));
		}

		[Test]
		public void Hysteresis_BandHoldsProgress()
		{
			float progress = 0.5f;
			float mid = (DetectionQualityMath.DefaultLoseThreshold + DetectionQualityMath.DefaultAcquireThreshold) * 0.5f;
			float held = DetectionQualityMath.IntegrateProgress(progress, mid, 0.5f);
			Assert.AreEqual(progress, held, 0.0001f);
		}

		[Test]
		public void Hysteresis_BelowLoseDecays_AboveAcquireGrows()
		{
			float grown = DetectionQualityMath.IntegrateProgress(0.4f, 0.9f, 0.1f);
			float decayed = DetectionQualityMath.IntegrateProgress(0.4f, 0.05f, 0.1f);
			Assert.Greater(grown, 0.4f);
			Assert.Less(decayed, 0.4f);
		}

		[Test]
		public void SoftLose_DoesNotWipeProgressInOneShortGap()
		{
			float progress = 0f;
			for (int i = 0; i < 20; i++)
				progress = DetectionQualityMath.IntegrateProgress(progress, 1f, 0.05f);

			Assert.Greater(progress, 0.7f);

			float afterGap = progress;
			for (int i = 0; i < 3; i++)
				afterGap = DetectionQualityMath.IntegrateProgress(afterGap, 0f, 0.05f);

			Assert.Greater(afterGap, 0.5f);
			Assert.AreNotEqual(DetectionState.Undetected, DetectionQualityMath.ResolveState(afterGap));
		}

		[Test]
		public void State_TransitionsFromProgress()
		{
			Assert.AreEqual(DetectionState.Undetected, DetectionQualityMath.ResolveState(0f));
			Assert.AreEqual(DetectionState.Detecting, DetectionQualityMath.ResolveState(0.4f));
			Assert.AreEqual(DetectionState.Detected, DetectionQualityMath.ResolveState(1f));
		}

		[Test]
		public void AcquisitionFactor_Exponent1_EqualsQuality()
		{
			for (int i = 0; i <= 10; i++)
			{
				float q = i / 10f;
				Assert.AreEqual(q, DetectionQualityMath.AcquisitionFactor(q, 1f), 0.0001f);
			}
		}

		[Test]
		public void IntegrateProgress_Exponent1_MatchesLegacyLinearQ()
		{
			const float dt = 0.05f;
			const float q = 0.60f;
			float expected = DetectionQualityMath.IntegrateProgress(
				0.10f, q, dt,
				DetectionQualityMath.DefaultAcquireTime,
				DetectionQualityMath.DefaultLossTime,
				DetectionQualityMath.DefaultAcquireThreshold,
				DetectionQualityMath.DefaultLoseThreshold,
				1f);
			float legacy = 0.10f + q * (1f / DetectionQualityMath.DefaultAcquireTime) * dt;
			Assert.AreEqual(legacy, expected, 0.0001f);
		}

		[Test]
		public void AcquisitionFactor_IsMonotoneInQ()
		{
			float prev = -1f;
			for (int i = 0; i <= 40; i++)
			{
				float q = i / 40f;
				float factor = DetectionQualityMath.AcquisitionFactor(q);
				Assert.GreaterOrEqual(factor + 0.0001f, prev);
				prev = factor;
			}
		}

		[Test]
		public void Hysteresis_Q250_Holds_Q251_Grows()
		{
			const float start = 0.40f;
			float hold = DetectionQualityMath.IntegrateProgress(start, 0.250f, 0.25f);
			float grow = DetectionQualityMath.IntegrateProgress(start, 0.251f, 0.25f);
			Assert.AreEqual(start, hold, 0.0001f);
			Assert.Greater(grow, start);
		}

		[Test]
		public void LossAndHold_UnchangedByExponent()
		{
			float mid = (DetectionQualityMath.DefaultLoseThreshold + DetectionQualityMath.DefaultAcquireThreshold) * 0.5f;
			float shapedHold = DetectionQualityMath.IntegrateProgress(0.55f, mid, 0.5f, _exponent: 3.8f);
			float linearHold = DetectionQualityMath.IntegrateProgress(0.55f, mid, 0.5f, _exponent: 1f);
			Assert.AreEqual(0.55f, shapedHold, 0.0001f);
			Assert.AreEqual(0.55f, linearHold, 0.0001f);

			float shapedLoss = DetectionQualityMath.IntegrateProgress(0.80f, 0.05f, 0.1f, _exponent: 3.8f);
			float linearLoss = DetectionQualityMath.IntegrateProgress(0.80f, 0.05f, 0.1f, _exponent: 1f);
			Assert.AreEqual(linearLoss, shapedLoss, 0.0001f);
			Assert.Less(shapedLoss, 0.80f);
		}

		[Test]
		public void DistanceCurve_LiveEdge_ExceedsAcquire_PartialExposureStaysGated()
		{
			float q149 = VisionDetectionTimingContract.FullStaticCenterQuality(149f, false);
			float q299 = VisionDetectionTimingContract.FullStaticCenterQuality(299f, true);
			Assert.Greater(q149, DetectionQualityMath.DefaultAcquireThreshold);
			Assert.Greater(q299, DetectionQualityMath.DefaultAcquireThreshold);

			float d149 = DetectionQualityMath.DistanceFactor(149f, 150f);
			float twoThirds = DetectionQualityMath.VisibilityQuality(d149, 1f, 2f / 3f, 1f);
			float oneThird = DetectionQualityMath.VisibilityQuality(d149, 1f, 1f / 3f, 1f);
			Assert.Less(twoThirds, DetectionQualityMath.DefaultAcquireThreshold + 0.0001f);
			Assert.Less(oneThird, DetectionQualityMath.DefaultAcquireThreshold);
		}

		[Test]
		public void TimingContract_MathAnchorsFitBands()
		{
			VisionDetectionTimingContract.TimingAnchor[] anchors =
				VisionDetectionTimingContract.FullStaticCenterAnchors;
			for (int i = 0; i < anchors.Length; i++)
			{
				VisionDetectionTimingContract.TimingAnchor anchor = anchors[i];
				float q = VisionDetectionTimingContract.FullStaticCenterQuality(
					anchor.DistanceMeters, anchor.Optic);
				float t = DetectionQualityMath.EstimateDetectTimeSeconds(q);
				Assert.IsTrue(
					VisionDetectionTimingContract.FitsBand(t, anchor.MinDetectedSeconds, anchor.MaxDetectedSeconds),
					anchor.Id + " Q=" + q.ToString("F3") + " t=" + t.ToString("F3"));
			}
		}

		[Test]
		public void TimingContract_RelativeEyeOptic_SameNormalizedT()
		{
			for (int i = 0; i < VisionDetectionTimingContract.RelativePairs.Length; i++)
			{
				(string eyeId, string opticId) = VisionDetectionTimingContract.RelativePairs[i];
				Assert.IsTrue(VisionDetectionTimingContract.TryFindAnchor(eyeId, out var eye));
				Assert.IsTrue(VisionDetectionTimingContract.TryFindAnchor(opticId, out var optic));
				Assert.AreEqual(eye.NormalizedT, optic.NormalizedT, 0.04f, eyeId + "/" + opticId);

				float tEye = DetectionQualityMath.EstimateDetectTimeSeconds(
					VisionDetectionTimingContract.FullStaticCenterQuality(eye.DistanceMeters, false));
				float tOptic = DetectionQualityMath.EstimateDetectTimeSeconds(
					VisionDetectionTimingContract.FullStaticCenterQuality(optic.DistanceMeters, true));
				Assert.IsTrue(
					VisionDetectionTimingContract.RelativeTimesMatch(tEye, tOptic),
					eyeId + "=" + tEye.ToString("F3") + " " + opticId + "=" + tOptic.ToString("F3"));
			}
		}

		[Test]
		public void TimingContract_DetectTime_MonotoneWithDistance()
		{
			float[] eye = { 25f, 50f, 75f, 100f, 140f, 149f };
			float prev = 0f;
			for (int i = 0; i < eye.Length; i++)
			{
				float t = DetectionQualityMath.EstimateDetectTimeSeconds(
					VisionDetectionTimingContract.FullStaticCenterQuality(eye[i], false));
				Assert.Greater(t, 0f, "Eye " + eye[i]);
				Assert.GreaterOrEqual(t + 0.0001f, prev);
				prev = t;
			}
		}

		[Test]
		public void PresetA_QualityHigherThanPresetF_WithEqualExposure()
		{
			float qA = DetectionQualityMath.VisibilityQuality(
				DetectionQualityMath.DistanceFactor(10f),
				DetectionQualityMath.FovFactor(0f),
				1f,
				DetectionQualityMath.MovementFactor(0f));

			float qF = DetectionQualityMath.VisibilityQuality(
				DetectionQualityMath.DistanceFactor(400f),
				DetectionQualityMath.FovFactor(50f),
				1f,
				DetectionQualityMath.MovementFactor(0f));

			Assert.Greater(qA, qF);
		}

		[Test]
		public void CalibrationScenarios_AhReportPasses()
		{
			DetectionCalibrationScenarios.ReportResult result = DetectionCalibrationScenarios.BuildReport();
			Assert.AreEqual(0, result.FailCount, result.Body);
		}
	}
}
