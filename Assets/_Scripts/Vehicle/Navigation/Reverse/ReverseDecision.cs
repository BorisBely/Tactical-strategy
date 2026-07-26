using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct ReverseDecisionResult
	{
		public readonly bool ShouldReverse;
		public readonly string Reason;

		public ReverseDecisionResult(bool _should, string _reason)
		{
			ShouldReverse = _should;
			Reason = _reason;
		}
	}

	/// <summary>
	/// Answers a single question: "Should we drive in reverse or turn around?"
	/// Analyzes: path angles, distances, feasibility.
	/// Does NOT build the path — that's ReversePathBuilder's job.
	/// </summary>
	public static class ReverseDecision
	{
		private const float c_MaxReverseAngle = 35f;
		private const float c_MaxReverseDistance = 25f;
		private const float c_TurnAroundThresholdAngle = 60f;
		private const float c_MinForwardAngle = 90f;

		public static ReverseDecisionResult Evaluate(DriverContext _ctx)
		{
			if (_ctx.Path.Corners == null || _ctx.Path.Corners.Length < 2)
				return new ReverseDecisionResult(false, "no path corners");

			Vector3 forward = _ctx.Forward;
			Vector3 firstTarget = _ctx.Path.Corners[1];
			Vector3 toFirst = firstTarget - _ctx.Position;
			toFirst.y = 0f;
			float firstSegLen = toFirst.magnitude;
			float firstAngle = firstSegLen > 0.01f
				? Mathf.Abs(Vector3.SignedAngle(forward, toFirst.normalized, Vector3.up))
				: 0f;

			float flatDist = FlatDistance(_ctx.Position, _ctx.Request.Destination);

			if (firstAngle <= c_MinForwardAngle)
				return new ReverseDecisionResult(false, $"angle {firstAngle:F0}° ≤ {c_MinForwardAngle}° — forward");

			if (firstAngle >= c_TurnAroundThresholdAngle && flatDist > c_MaxReverseDistance)
				return new ReverseDecisionResult(false, $"angle {firstAngle:F0}° ≥ {c_TurnAroundThresholdAngle}°, dist {flatDist:F1}m > {c_MaxReverseDistance}m — turn around");

			float maxSegAngle = MaxSegmentAngle(_ctx.Path.Corners);
			if (maxSegAngle > c_MaxReverseAngle)
				return new ReverseDecisionResult(false, $"max segment angle {maxSegAngle:F0}° > {c_MaxReverseAngle}° — path too curved");

			var feas = ReverseFeasibility.Check(_ctx);
			if (!feas.Feasible)
				return new ReverseDecisionResult(false, feas.Reason);

			return new ReverseDecisionResult(true, $"angle={firstAngle:F0}°, dist={flatDist:F1}m, maxCurve={maxSegAngle:F0}° — reverse OK");
		}

		private static float MaxSegmentAngle(Vector3[] _corners)
		{
			float max = 0f;
			for (int i = 1; i < _corners.Length - 1; i++)
			{
				Vector3 a = _corners[i - 1];
				Vector3 b = _corners[i];
				Vector3 c = _corners[i + 1];
				var d1 = (b - a).normalized;
				var d2 = (c - b).normalized;
				float angle = Vector3.Angle(d1, d2);
				if (angle > max) max = angle;
			}
			return max;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f; _b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
