using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class MemoryDecayMathTests
	{
		[Test]
		public void ConfidenceAtZeroTime_EqualsInitial()
		{
			Assert.AreEqual(1f, MemoryDecayMath.Evaluate(0f, 1f), 0.0001f);
			Assert.AreEqual(0.5f, MemoryDecayMath.Evaluate(0f, 0.5f), 0.0001f);
			Assert.AreEqual(0.2f, MemoryDecayMath.Evaluate(-1f, 0.2f), 0.0001f);
		}

		[Test]
		public void ConfidenceNeverNegative_AndNeverAboveOne()
		{
			for (int i = 0; i <= 20; i++)
			{
				float conf = MemoryDecayMath.Evaluate(i * 0.5f, 1f);
				Assert.GreaterOrEqual(conf, 0f);
				Assert.LessOrEqual(conf, 1f);
			}

			Assert.AreEqual(1f, MemoryDecayMath.Evaluate(0f, 1.5f), 0.0001f);
		}

		[Test]
		public void ConfidenceMonotonicDecrease()
		{
			float prev = MemoryDecayMath.Evaluate(0f, 1f);
			for (int i = 1; i <= 20; i++)
			{
				float next = MemoryDecayMath.Evaluate(i * 0.5f, 1f);
				Assert.LessOrEqual(next, prev + 0.0001f);
				prev = next;
			}
		}

		[Test]
		public void ConfidenceAtEarlyTime_GreaterThanLateTime()
		{
			float early = MemoryDecayMath.Evaluate(1f, 1f);
			float late = MemoryDecayMath.Evaluate(7f, 1f);
			Assert.Greater(early, late);
		}

		[Test]
		public void ConfidenceAtHorizon_IsZero()
		{
			Assert.AreEqual(0f, MemoryDecayMath.Evaluate(MemoryDecayMath.DefaultHorizonSeconds, 1f), 0.0001f);
			Assert.AreEqual(0f, MemoryDecayMath.Evaluate(99f, 0.5f), 0.0001f);
		}

		[Test]
		public void StaleBand_UsesThreshold()
		{
			float stale = MemoryDecayMath.DefaultStaleThreshold;
			Assert.IsTrue(MemoryDecayMath.IsStale(stale, stale));
			Assert.IsTrue(MemoryDecayMath.IsStale(stale * 0.5f, stale));
			Assert.IsFalse(MemoryDecayMath.IsStale(stale + 0.01f, stale));
			Assert.IsFalse(MemoryDecayMath.IsStale(0f, stale));
		}

		[Test]
		public void InitialConfidence_ScalesProportionally()
		{
			float full = MemoryDecayMath.Evaluate(3f, 1f);
			float half = MemoryDecayMath.Evaluate(3f, 0.5f);
			float low = MemoryDecayMath.Evaluate(3f, 0.2f);
			Assert.AreEqual(full * 0.5f, half, 0.0001f);
			Assert.AreEqual(full * 0.2f, low, 0.0001f);
		}

		[Test]
		public void HasMemory_FalseWhenForgotten()
		{
			Assert.IsTrue(MemoryDecayMath.HasMemory(0.01f));
			Assert.IsFalse(MemoryDecayMath.HasMemory(0f));
			Assert.IsTrue(MemoryDecayMath.IsForgotten(0f));
		}

		[Test]
		public void Evaluate_PastHorizon_StaysZero()
		{
			float horizon = MemoryDecayMath.DefaultHorizonSeconds;
			Assert.AreEqual(0f, MemoryDecayMath.Evaluate(horizon, 1f), 0.0001f);
			Assert.AreEqual(0f, MemoryDecayMath.Evaluate(horizon + 5f, 1f), 0.0001f);
		}

		[Test]
		public void BaselineDefaults_MatchBlockB1()
		{
			Assert.AreEqual(5f, MemoryDecayMath.DefaultRecentlyLostSeconds);
			Assert.AreEqual(30f, MemoryDecayMath.DefaultHorizonSeconds);
			Assert.AreEqual(1.5f, MemoryDecayMath.DefaultShapeExponent);
			Assert.AreEqual(0.25f, MemoryDecayMath.DefaultStaleThreshold);
		}

		[Test]
		public void ElapsedSecondsForConfidence_InvertsEvaluate()
		{
			float t = MemoryDecayMath.ElapsedSecondsForConfidence(0.25f);
			float conf = MemoryDecayMath.Evaluate(t, 1f);
			Assert.AreEqual(0.25f, conf, 0.001f);
		}

		[Test]
		public void MemoryCalibrationMathReport_Passes()
		{
			MemoryCalibrationScenarios.ReportResult result = MemoryCalibrationScenarios.BuildReport();
			Assert.AreEqual(0, result.FailCount, result.Body);
		}
	}
}
