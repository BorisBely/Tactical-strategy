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
			Assert.AreEqual(0.08f, DetectionQualityMath.EvaluateDistanceCurve(1f), 0.001f);
			Assert.AreEqual(0.08f, DetectionQualityMath.EvaluateDistanceCurve(2f), 0.001f);
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
			Assert.AreEqual(0.08f, eyeEdge, 0.001f);
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
	}
}
