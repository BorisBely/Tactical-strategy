using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Evaluates all possible DriverIntents and picks the best one based on cost scoring.
	/// This replaces the single-angle decision with a full cost model.
	/// </summary>
	public static class ScoringSystem
	{
		private const float c_Weight_Distance = 1f;
		private const float c_Weight_Turns = 2f;
		private const float c_Weight_Time = 0.5f;

		public static float ScoreCandidate(
			DriverIntent _intent,
			float _pathLength,
			int _turnCount,
			FeasibilityResult _feasibility,
			float _firstAngle = 0f)
		{
			float baseScore = _pathLength * c_Weight_Distance
			                  + _turnCount * c_Weight_Turns
			                  + _pathLength / 10f * c_Weight_Time;

			float absAngle = System.Math.Abs(_firstAngle);

			switch (_intent)
			{
				case DriverIntent.Reverse:
					baseScore += 15f;
					break;
				case DriverIntent.TurnAround:
					baseScore += 10f;
					break;
				case DriverIntent.DriveForward:
					if (absAngle > 135f) baseScore += 50f;      // target behind → huge penalty
					else if (absAngle > 90f) baseScore += 25f;  // target to the side → moderate penalty
					break;
			}

			return ApplyRiskPenalty(baseScore, _feasibility);
		}

		public static float ApplyRiskPenalty(float _baseScore, FeasibilityResult _feasibility)
		{
			if (_feasibility == null)
				return _baseScore;

			float score = _baseScore + _feasibility.RiskScore * 25f;

			score += _feasibility.Severity switch
			{
				FeasibilitySeverity.Impossible => 999999f,
				FeasibilitySeverity.Unsafe => 200f,
				FeasibilitySeverity.Risky => 50f,
				_ => 0f
			};

			if (_feasibility.HasCliffRisk) score += 50f;
			if (_feasibility.HasNarrowPassage) score += 10f;

			return score;
		}

		private static float ScoreForward(DriverContext _ctx)
		{
			float d = FlatDistance(_ctx.Position, _ctx.Request.Destination);
			float turns = CountTurns(_ctx.Path.Corners);
			float time = d / Mathf.Max(1f, _ctx.MaxForwardSpeedKmh) * 3600f;
			return d * c_Weight_Distance + turns * c_Weight_Turns + time * c_Weight_Time;
		}

		private static float ScoreReverse(DriverContext _ctx)
		{
			var decision = ReverseDecision.Evaluate(_ctx);
			if (!decision.ShouldReverse)
				return float.MaxValue;

			float d = FlatDistance(_ctx.Position, _ctx.Request.Destination);
			float turns = CountTurns(_ctx.Path.Corners);
			float speed = Mathf.Max(1f, _ctx.MaxReverseSpeedKmh);
			float time = d / speed * 3600f * 1.5f;
			return d * c_Weight_Distance + turns * c_Weight_Turns + time * c_Weight_Time + 10f;
		}

		private static float ScoreTurnAround(DriverContext _ctx)
		{
			float d = FlatDistance(_ctx.Position, _ctx.Request.Destination);
			float turns = CountTurns(_ctx.Path.Corners) + 1f;
			float time = d / Mathf.Max(1f, _ctx.MaxForwardSpeedKmh) * 3600f + 3f;
			float penalty = _ctx.Geometry.FrontClearance < 4f || _ctx.Geometry.RearClearance < 4f ? 20f : 0f;
			return d * c_Weight_Distance + turns * c_Weight_Turns + time * c_Weight_Time + 15f + penalty;
		}

		private static int CountTurns(Vector3[] _corners)
		{
			if (_corners == null || _corners.Length < 3)
				return 0;
			int turns = 0;
			for (int i = 1; i < _corners.Length - 1; i++)
			{
				Vector3 d1 = (_corners[i] - _corners[i - 1]).normalized;
				Vector3 d2 = (_corners[i + 1] - _corners[i]).normalized;
				if (Vector3.Angle(d1, d2) > 30f)
					turns++;
			}
			return turns;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f; _b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
