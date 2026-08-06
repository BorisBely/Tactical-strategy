using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Lightweight Reeds-Shepp / Dubins candidate generator for parking and tight-space staging.
	/// Produces forward-only or reverse-capable arc sequences bounded by min turning radius.
	/// </summary>
	public static class ReedsSheppPlanner
	{
		public struct Pose
		{
			public Vector3 Position;
			public float YawDeg;

			public Pose(Vector3 _position, float _yawDeg)
			{
				Position = _position;
				YawDeg = _yawDeg;
			}
		}

		public static Vector3[] PlanForwardArc(
			Pose _from,
			Pose _to,
			float _turnRadius,
			int _segments = 12)
		{
			float dist = FlatDistance(_from.Position, _to.Position);
			if (dist < 0.2f)
				return new[] { _from.Position, _to.Position };

			float headingErr = Mathf.DeltaAngle(_from.YawDeg, _to.YawDeg);
			float sign = headingErr >= 0f ? 1f : -1f;
			float radius = Mathf.Max(1f, _turnRadius);

			var pts = new List<Vector3> { _from.Position };

			if (Mathf.Abs(headingErr) > 8f && dist > radius * 0.4f)
			{
				float arcAngle = Mathf.Clamp(Mathf.Abs(headingErr) * 0.65f, 10f, 90f);
				AppendArc(pts, _from.Position, _from.YawDeg, sign, radius, arcAngle, _segments / 2);
			}

			Vector3 last = pts[pts.Count - 1];
			float lastYaw = _from.YawDeg + headingErr * 0.65f;
			Vector3 fwd = YawToForward(lastYaw);
			int lineSteps = Mathf.Max(2, Mathf.CeilToInt(FlatDistance(last, _to.Position) / 0.5f));
			for (int i = 1; i <= lineSteps; i++)
			{
				float t = i / (float)lineSteps;
				pts.Add(Vector3.Lerp(last, _to.Position, t));
			}

			return pts.ToArray();
		}

		public static Vector3[] PlanStagingApproach(
			Pose _from,
			Pose _to,
			float _turnRadius)
		{
			float approach = Mathf.Max(2f, _turnRadius * 0.45f);
			Vector3 targetFwd = YawToForward(_to.YawDeg);
			Vector3 stagingPos = _to.Position - targetFwd * approach;
			float stagingYaw = _to.YawDeg;

			var staging = new Pose(stagingPos, stagingYaw);
			var toStaging = PlanForwardArc(_from, staging, _turnRadius);
			var final = new List<Vector3>(toStaging);
			final.Add(_to.Position);
			return final.ToArray();
		}

		private static void AppendArc(
			List<Vector3> _points,
			Vector3 _origin,
			float _startYaw,
			float _turnSign,
			float _radius,
			float _angleSpan,
			int _segments)
		{
			_segments = Mathf.Max(2, _segments);
			Vector3 startDir = YawToForward(_startYaw);
			Vector3 right = Vector3.Cross(Vector3.up, startDir).normalized;
			Vector3 center = _origin + right * _turnSign * _radius;

			for (int i = 1; i <= _segments; i++)
			{
				float t = i / (float)_segments;
				float angle = _startYaw + 90f * _turnSign - _turnSign * _angleSpan * t;
				float rad = angle * Mathf.Deg2Rad;
				Vector3 offset = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * _radius;
				Vector3 pt = center - right * _turnSign * _radius + offset;
				pt.y = _origin.y;
				_points.Add(pt);
			}
		}

		private static Vector3 YawToForward(float _yawDeg)
		{
			float rad = _yawDeg * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
