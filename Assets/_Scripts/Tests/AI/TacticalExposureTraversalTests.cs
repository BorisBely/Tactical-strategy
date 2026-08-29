using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.4 Exposure-aware Traversal. Profile, not speed/stance. Overlay does not Move.
	/// </summary>
	public sealed class TacticalExposureTraversalTests
	{
		#region A Sampling
		[Test]
		public void A1_Route_IsSampled()
		{
			TacticalRouteCandidate route = Direct(20f);
			TacticalRouteSituation situation = Threatened();
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.GreaterOrEqual(route.ExposureSamples.Count, 3);
			Assert.Greater(route.PeakExposure01, 0f);
		}

		[Test]
		public void A2_SampleCount_Bounded()
		{
			TacticalRouteCandidate route = Direct(80f);
			TacticalRouteSituation situation = Threatened();
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.LessOrEqual(
				route.ExposureSamples.Count,
				TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.AreEqual(
				TacticalExposureTraversalMath.DefaultMaxExposureSamples,
				TacticalExposureTraversalMath.ResolveSampleCount(
					80f, TacticalExposureTraversalMath.DefaultMaxExposureSamples));
		}

		[Test]
		public void A3_SamplePositions_Deterministic()
		{
			TacticalRouteCandidate route = Direct(24f);
			Vector3 a = TacticalExposureTraversalMath.SamplePosition(
				route, 2, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Vector3 b = TacticalExposureTraversalMath.SamplePosition(
				route, 2, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.AreEqual(a, b);
			Assert.Greater(a.x, 0f);
		}
		#endregion

		#region B Profile
		[Test]
		public void B1_SafeRoute_LowSamples()
		{
			TacticalRouteSituation situation = Threatened();
			situation.CoverHints = LinedCovers(0f, 20f, 2.5f);
			TacticalRouteCandidate route = Direct(20f);
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.Less(route.PeakExposure01, 0.25f);
			Assert.Less(route.TimeAboveThresholdSeconds, 0.2f);
		}

		[Test]
		public void B2_ExposedRoute_HighSamples()
		{
			TacticalRouteCandidate route = Direct(20f);
			TacticalRouteSituation situation = Threatened();
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.Greater(route.PeakExposure01, 0.6f);
			Assert.Greater(route.TimeExposedSeconds, 1f);
		}

		[Test]
		public void B3_MixedRoute_MixedProfile()
		{
			TacticalRouteSituation situation = Threatened();
			situation.CoverHints = new[] { Vector3.zero, new Vector3(20f, 0f, 0f) };
			TacticalRouteCandidate route = Direct(20f);
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.Greater(route.PeakExposure01, 0.55f);
			Assert.Less(route.PeakExposure01 - Average(route), 0.7f);
			Assert.Greater(MaxMinusMin(route), 0.3f);
		}
		#endregion

		#region C Peak
		[Test]
		public void C1_LowAverage_HighPeak_Detected()
		{
			TacticalRouteSituation situation = Threatened();
			situation.CoverHints = new[]
			{
				Vector3.zero, new Vector3(2f, 0f, 0f),
				new Vector3(18f, 0f, 0f), new Vector3(20f, 0f, 0f)
			};
			TacticalRouteCandidate route = Direct(20f);
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.Greater(route.PeakExposure01, Average(route) + 0.15f);
		}

		[Test]
		public void C2_HighAverage_LowPeakGap()
		{
			TacticalRouteCandidate route = Direct(20f);
			TacticalRouteSituation situation = Threatened();
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.Less(route.PeakExposure01 - Average(route), 0.12f);
			Assert.Greater(Average(route), 0.5f);
		}
		#endregion

		#region D Duration
		[Test]
		public void D1_ShortExposure_LowerDurationThanLong()
		{
			TacticalRouteSituation shortSit = Threatened();
			shortSit.CoverHints = LinedCovers(0f, 20f, 3f);
			shortSit.CoverHints = WithGap(shortSit.CoverHints, 9f, 12f);
			TacticalRouteCandidate shortRoute = Direct(20f);
			TacticalExposureTraversalMath.Fill(
				shortRoute, in shortSit, TacticalExposureTraversalMath.DefaultMaxExposureSamples);

			TacticalRouteCandidate longRoute = Direct(20f);
			TacticalRouteSituation longSit = Threatened();
			TacticalExposureTraversalMath.Fill(
				longRoute, in longSit, TacticalExposureTraversalMath.DefaultMaxExposureSamples);

			Assert.Greater(longRoute.TimeAboveThresholdSeconds, shortRoute.TimeAboveThresholdSeconds);
			Assert.Greater(longRoute.ExposureCost, shortRoute.ExposureCost);
		}
		#endregion

		#region E Cover transition
		[Test]
		public void E1_ExposureThenCover_ReducesRiskAfter()
		{
			TacticalRouteSituation situation = Threatened();
			situation.CoverHints = new[] { new Vector3(20f, 0f, 0f) };
			TacticalRouteCandidate route = Direct(20f);
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			TacticalExposureSample last = route.ExposureSamples[route.ExposureSamples.Count - 1];
			TacticalExposureSample mid = route.ExposureSamples[route.ExposureSamples.Count / 2];
			Assert.Greater(mid.Exposure01, last.Exposure01);
			Assert.Less(last.Risk, TacticalExposureRisk.Critical);
			Assert.Less(mid.MetersToNextCover, 20f);
		}
		#endregion

		#region F Unknown
		[Test]
		public void F1_UnknownEnemy_NotAutomaticallySafe()
		{
			TacticalRouteCandidate route = Direct(16f);
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, false);
			TacticalExposureTraversalMath.Fill(
				route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.Greater(Average(route), 0.2f);
			Assert.AreEqual(TacticalRouteScoreMath.UnknownExposure, route.ExposureSamples[1].Exposure01, 0.001f);
		}
		#endregion

		#region G Ranking
		[Test]
		public void G1_SameAverage_ProfileBreaksTie()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			TacticalRouteCandidate grind = Authored(
				1, new Vector3(5f, 0f, 6f), 0.32f, 0.32f, 0f, 8f);
			TacticalRouteCandidate spike = Authored(
				2, new Vector3(5f, 0f, -6f), 0.32f, 0.95f, 0.8f, 1.2f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, new[] { grind, spike });
			Assert.AreEqual(2, decision.Selected.Candidate.CandidateId);
			Assert.AreEqual(grind.Exposure01, spike.Exposure01, 0.001f);
			Assert.Greater(spike.PeakExposure01, grind.PeakExposure01);
			Assert.AreNotEqual(Find(decision, 1).Score, Find(decision, 2).Score);
		}
		#endregion

		#region Extra
		[Test]
		public void H1_Overlay_DoesNotMove()
		{
			var go = new GameObject("AI144_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				TacticalRouteSituation situation = Threatened();
				controller.TacticalMovement.Update(in situation, null);
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void I1_Profile_CachedWithRoute()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation situation = Threatened();
			situation.CoverHints = LinedCovers(0f, 20f, 4f);
			TacticalRouteDecision first = evaluator.Evaluate(in situation, null);
			float peak = first.Selected.Candidate.PeakExposure01;
			int builds = TacticalExposureTraversalMath.BuildCount;
			for (int i = 0; i < 10; i++)
			{
				TacticalRouteDecision again = evaluator.Evaluate(in situation, null);
				Assert.IsTrue(again.FromCache);
				Assert.AreEqual(peak, again.Selected.Candidate.PeakExposure01);
			}

			Assert.AreEqual(1, evaluator.EvaluationCount);
			Assert.AreEqual(builds, TacticalExposureTraversalMath.BuildCount);
		}

		[Test]
		public void J1_HundredCandidates_StayBounded()
		{
			TacticalExposureTraversalMath.ResetBuildCount();
			TacticalRouteSituation situation = Threatened();
			int max = 0;
			for (int i = 0; i < 100; i++)
			{
				TacticalRouteCandidate route = Direct(12f + i * 0.2f);
				TacticalExposureTraversalMath.Fill(
					route, in situation, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
				if (route.ExposureSamples.Count > max)
					max = route.ExposureSamples.Count;
			}

			Assert.LessOrEqual(max, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			Assert.AreEqual(100, TacticalExposureTraversalMath.BuildCount);
		}

		[Test]
		public void K1_Score_KeepsFourteenOneAverage()
		{
			TacticalRouteCandidate candidate = Authored(1, new Vector3(5f, 0f, 6f), 0.4f, 0f, 0f, 0f);
			TacticalRouteScoreFactors factors = TacticalRouteScoreMath.EvaluateFactors(
				candidate, TacticalMovementMode.Tactical);
			Assert.AreEqual(factors.Total, factors.RebuiltTotal, 0.0001f);
			Assert.AreEqual(0f, factors.PeakHold);
			Assert.AreEqual(0f, factors.TimeAbove);
		}
		#endregion

		#region Helpers
		private static TacticalRouteSituation Threatened()
		{
			return Sit(TacticalMovementMode.Tactical, true);
		}

		private static TacticalRouteSituation Sit(TacticalMovementMode _mode, bool _threat)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = new Vector3(20f, 0f, 0f),
				HasDestination = true,
				Mode = _mode,
				HasKnownThreat = _threat,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed
			};
		}

		private static TacticalRouteCandidate Direct(float _meters)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetDirect(1, Vector3.zero, new Vector3(_meters, 0f, 0f));
			return candidate;
		}

		private static TacticalRouteCandidate Authored(
			int _id,
			Vector3 _hop,
			float _average,
			float _peak,
			float _timeAbove,
			float _timeExposed)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetWaypoint(_id, Vector3.zero, new Vector3(20f, 0f, 0f), _hop);
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = 16f;
			candidate.TravelTimeSeconds = 10.7f;
			candidate.Exposure01 = _average;
			candidate.Cover01 = 0.2f;
			candidate.Danger01 = 0.3f;
			candidate.MissionProgress01 = 0.5f;
			candidate.UseAuthoredExposureProfile = true;
			candidate.PeakExposure01 = _peak;
			candidate.TimeAboveThresholdSeconds = _timeAbove;
			candidate.TimeExposedSeconds = _timeExposed;
			return candidate;
		}

		private static Vector3[] LinedCovers(float _from, float _to, float _step)
		{
			var list = new List<Vector3>(12);
			for (float x = _from; x <= _to + 0.01f; x += _step)
				list.Add(new Vector3(x, 0f, 0f));
			return list.ToArray();
		}

		private static Vector3[] WithGap(IReadOnlyList<Vector3> _covers, float _gapFrom, float _gapTo)
		{
			var kept = new List<Vector3>(12);
			for (int i = 0; i < _covers.Count; i++)
			{
				if (_covers[i].x >= _gapFrom && _covers[i].x <= _gapTo)
					continue;
				kept.Add(_covers[i]);
			}

			return kept.ToArray();
		}

		private static float Average(TacticalRouteCandidate _candidate)
		{
			if (_candidate.ExposureSamples.Count == 0)
				return 0f;
			float sum = 0f;
			for (int i = 0; i < _candidate.ExposureSamples.Count; i++)
				sum += _candidate.ExposureSamples[i].Exposure01;
			return sum / _candidate.ExposureSamples.Count;
		}

		private static float MaxMinusMin(TacticalRouteCandidate _candidate)
		{
			float min = 1f;
			float max = 0f;
			for (int i = 0; i < _candidate.ExposureSamples.Count; i++)
			{
				float e = _candidate.ExposureSamples[i].Exposure01;
				if (e < min)
					min = e;
				if (e > max)
					max = e;
			}

			return max - min;
		}

		private static TacticalRouteEvaluation Find(in TacticalRouteDecision _decision, int _id)
		{
			for (int i = 0; i < _decision.Evaluations.Count; i++)
			{
				if (_decision.Evaluations[i].Candidate != null &&
				    _decision.Evaluations[i].Candidate.CandidateId == _id)
					return _decision.Evaluations[i];
			}

			return default;
		}
		#endregion
	}
}
