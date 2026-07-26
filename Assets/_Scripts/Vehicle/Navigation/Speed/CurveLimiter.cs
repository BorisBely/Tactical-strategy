using UnityEngine;

namespace VehicleNavigation
{
	public sealed class CurveLimiter : ISpeedLimiter
	{
		private readonly AnimationCurve m_CurvatureSpeedCurve;

		public CurveLimiter(AnimationCurve _curvatureSpeedCurve)
		{
			m_CurvatureSpeedCurve = _curvatureSpeedCurve ?? new AnimationCurve(
				new Keyframe(0f, 1f),
				new Keyframe(0.15f, 0.55f),
				new Keyframe(0.3f, 0.18f));
		}

		public SpeedLimitResult GetLimit(NavigationContext _ctx)
		{
			Maneuver maneuver = _ctx.CurrentManeuver;
			if (maneuver == null || maneuver.Waypoints == null || maneuver.Waypoints.Count < 2)
				return SpeedLimitResult.Unlimited;

			Vector3[] waypoints = maneuver.Waypoints as Vector3[] ?? System.Array.Empty<Vector3>();
			if (waypoints.Length < 2)
				return SpeedLimitResult.Unlimited;

			float maxCurvature = EvaluatePreviewCurvature(
				waypoints, _ctx.State.Position, _ctx.State.Forward);

			float speedFraction = m_CurvatureSpeedCurve.Evaluate(maxCurvature);
			float maxSpeedKmh = _ctx.Params.MaxForwardSpeedKmh * speedFraction;

			return new SpeedLimitResult(maxSpeedKmh, StopReason.None, 40, false);
		}

		private static float EvaluatePreviewCurvature(
			Vector3[] _waypoints, Vector3 _position, Vector3 _forward)
		{
			if (_waypoints.Length < 3)
				return 0f;

			int nearest = 0;
			float bestSqr = float.MaxValue;
			for (int i = 0; i < _waypoints.Length; i++)
			{
				Vector3 d = _waypoints[i] - _position;
				d.y = 0f;
				float sqr = d.sqrMagnitude;
				if (sqr < bestSqr)
				{
					bestSqr = sqr;
					nearest = i;
				}
			}

			float maxCurvature = 0f;
			int end = Mathf.Min(nearest + 5, _waypoints.Length - 1);
			for (int i = nearest + 1; i < end && i < _waypoints.Length - 1; i++)
			{
				Vector3 a = _waypoints[Mathf.Max(0, i - 1)];
				Vector3 b = _waypoints[i];
				Vector3 c = _waypoints[i + 1];
				a.y = 0f;
				b.y = 0f;
				c.y = 0f;

				Vector3 ab = (b - a).normalized;
				Vector3 bc = (c - b).normalized;
				float angle = Vector3.Angle(ab, bc);
				float segLength = Vector3.Distance(b, c);
				if (segLength > 0.1f)
				{
					float segCurv = angle * Mathf.Deg2Rad / segLength;
					if (segCurv > maxCurvature)
						maxCurvature = segCurv;
				}
			}
			return maxCurvature;
		}
	}
}
