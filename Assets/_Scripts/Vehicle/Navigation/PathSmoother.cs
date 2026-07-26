using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Generates waypoint arcs and smoothed path segments for maneuvers.
	/// </summary>
	public sealed class PathSmoother
	{
		public Vector3[] GenerateTurnAroundArc(
			Vector3 _origin,
			float _startYaw,
			float _turnSign,
			float _turnRadius)
		{
			return GenerateArc(
				_origin,
				_startYaw,
				_turnSign,
				_turnRadius,
				180f,
				8);
		}

		public Vector3[] GenerateTurnaroundTrajectory(
			Vector3 _origin,
			float _startYaw,
			float _turnSign,
			float _minRadius,
			float _vehicleLength)
		{
			_turnSign = Mathf.Sign(_turnSign);
			float radius = Mathf.Max(1f, _minRadius);

			float entryAngle = 20f;
			float exitAngle = 20f;
			float turnAngle = Mathf.Max(100f, 180f - entryAngle - exitAngle);
			float blendFraction = 0.3f;

			var rawPts = new System.Collections.Generic.List<Vector3>();
			float yawRad = _startYaw * Mathf.Deg2Rad;
			float x = _origin.x;
			float z = _origin.z;
			float stepDist = 0.3f;

			rawPts.Add(_origin);

			float[] phaseAngles = { entryAngle, turnAngle, exitAngle };
			float[] phaseSteers = { 0.85f, 1f, 0.6f };
			float[] phaseFracIn  = { 0.4f, 1f, 1f };
			float[] phaseFracOut = { 1f, 1f, 0.3f };

			float smoothedSteer = 0f;
			float prevSegTarget = 0f;

			for (int seg = 0; seg < 3; seg++)
			{
				float segAngle = phaseAngles[seg];
				float targetSteer = phaseSteers[seg];
				float remaining = segAngle;
				float segTotal = segAngle;

				while (remaining > 0.1f)
				{
					float progress = 1f - remaining / segTotal;
					float frac;
					if (seg == 0)
						frac = Mathf.Lerp(phaseFracIn[0], phaseFracOut[0], progress);
					else if (seg == 1)
						frac = 1f;
					else
						frac = Mathf.Lerp(phaseFracIn[2], phaseFracOut[2], progress);

					float rawSteer = targetSteer * frac;
					if (seg > 0)
					{
						float blendIn = Mathf.Clamp01(progress / blendFraction);
						rawSteer = Mathf.Lerp(prevSegTarget, rawSteer, blendIn);
					}
					smoothedSteer = Mathf.Lerp(smoothedSteer, rawSteer, 0.3f);

					float steerRad = Mathf.Atan(_vehicleLength * 0.7f / radius) * smoothedSteer;
					float curRadius = _vehicleLength * 0.7f / Mathf.Tan(steerRad);
					curRadius = Mathf.Max(curRadius, radius);

					float dAngle = stepDist / curRadius * Mathf.Rad2Deg;
					dAngle = Mathf.Min(dAngle, remaining);

					yawRad += dAngle * _turnSign * Mathf.Deg2Rad;
					x += Mathf.Sin(yawRad) * stepDist;
					z += Mathf.Cos(yawRad) * stepDist;
					Vector3 pt = new Vector3(x, _origin.y, z);

					if (rawPts.Count > 0 && Vector3.Distance(rawPts[rawPts.Count - 1], pt) > 0.1f)
						rawPts.Add(pt);

					remaining -= dAngle;
				}
				prevSegTarget = targetSteer;
			}

			Vector3 fwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
			Vector3 finalPt = rawPts[rawPts.Count - 1] + fwd * 1f;
			finalPt.y = _origin.y;
			rawPts.Add(finalPt);

			return rawPts.ToArray();
		}

		public Vector3[] GenerateThreePointWaypoints(
			Vector3 _origin,
			float _startYaw,
			float _turnSign,
			float _turnRadius,
			VehicleLocalGeometry.Sample _geometry)
		{
			List<Vector3> points = new List<Vector3>();
			float step = Mathf.Clamp(_turnRadius * 0.65f, 3f, 8f);

			Vector3 backDir = Quaternion.Euler(0f, _startYaw + 180f, 0f) * Vector3.forward;
			Vector3 p1 = _origin + backDir.normalized * step * 0.6f;
			Vector3 p2 = p1 + Quaternion.Euler(0f, _startYaw + _turnSign * 90f, 0f) * Vector3.forward * step;
			Vector3 p3 = p2 + Quaternion.Euler(0f, _startYaw, 0f) * Vector3.forward * step;

			points.Add(_origin);
			points.Add(p1);
			points.Add(p2);
			points.Add(p3);
			return points.ToArray();
		}

		public Vector3[] GenerateParkingWaypoints(
			Vector3 _fromPosition,
			float _fromYaw,
			Vector3 _destination,
			float _targetYaw,
			float _turnRadius)
		{
			Vector3[] arc = GenerateFinalAlignmentArc(
				_fromPosition,
				_fromYaw,
				_destination,
				_targetYaw,
				_turnRadius);
			return arc;
		}

		public Vector3[] GenerateUnstuckWaypoints(
			Vector3 _origin,
			float _startYaw,
			float _steerSign,
			float _turnRadius)
		{
			return GenerateArc(
				_origin,
				_startYaw + 180f,
				_steerSign,
				_turnRadius * 0.5f,
				70f,
				5);
		}

		public Vector3[] SmoothCorners(Vector3[] _corners, float _cornerCut)
		{
			if (_corners == null || _corners.Length <= 2)
				return _corners;

			List<Vector3> smoothed = new List<Vector3>();
			smoothed.Add(_corners[0]);

			for (int i = 1; i < _corners.Length - 1; i++)
			{
				Vector3 prev = _corners[i - 1];
				Vector3 cur = _corners[i];
				Vector3 next = _corners[i + 1];

				Vector3 a = Vector3.Lerp(cur, prev, _cornerCut);
				Vector3 b = Vector3.Lerp(cur, next, _cornerCut);

				smoothed.Add(a);
				smoothed.Add(b);
			}

			smoothed.Add(_corners[_corners.Length - 1]);
			return smoothed.ToArray();
		}

		private static Vector3[] GenerateArc(
			Vector3 _origin,
			float _startYaw,
			float _turnSign,
			float _radius,
			float _angleSpan,
			int _segments)
		{
			_turnSign = Mathf.Sign(_turnSign);
			_segments = Mathf.Max(2, _segments);
			Vector3[] points = new Vector3[_segments + 1];

			Vector3 startDir = Quaternion.Euler(0f, _startYaw, 0f) * Vector3.forward;
			Vector3 right = Vector3.Cross(Vector3.up, startDir).normalized;
			Vector3 center = _origin + right * _turnSign * _radius;

			for (int i = 0; i <= _segments; i++)
			{
				float t = i / (float)_segments;
				float angle = _startYaw + 90f * _turnSign - _turnSign * _angleSpan * t;
				float rad = angle * Mathf.Deg2Rad;
				Vector3 offset = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * _radius;
				points[i] = center - right * _turnSign * _radius + offset;
				points[i].y = _origin.y;
			}

			return points;
		}

		private static Vector3[] GenerateFinalAlignmentArc(
			Vector3 _from,
			float _fromYaw,
			Vector3 _to,
			float _targetYaw,
			float _radius)
		{
			// Very short line + orientation target. The Pursuit handles heading separately.
			Vector3[] pts = new Vector3[3];
			pts[0] = _from;
			pts[1] = Vector3.Lerp(_from, _to, 0.65f);
			pts[2] = _to;
			return pts;
		}
	}
}
