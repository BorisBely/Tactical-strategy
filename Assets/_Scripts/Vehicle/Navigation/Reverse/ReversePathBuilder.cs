using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public static class ReversePathBuilder
	{
		private const int c_SubdivisionsPerSegment = 4;

		public static ReversePath Build(PathResult _navMeshPath, DriverContext _ctx)
		{
			var path = new ReversePath();
			if (_navMeshPath.Corners == null || _navMeshPath.Corners.Length < 2)
				return path;

			var corners = _navMeshPath.Corners;

			// Smoothing decision — by max corner angle, NOT by distance
			float maxAngle = 0f;
			for (int i = 0; i < corners.Length - 2; i++)
			{
				Vector3 ab = (corners[i + 1] - corners[i]).normalized;
				Vector3 bc = (corners[i + 2] - corners[i + 1]).normalized;
				maxAngle = Mathf.Max(maxAngle, Vector3.Angle(ab, bc));
			}
			bool needsSmooth = maxAngle > 10f;

			List<Vector3> finalPoints;
			if (needsSmooth)
			{
				var rawPoints = new List<Vector3>();
				for (int i = 0; i < corners.Length; i++)
					rawPoints.Add(corners[i]);
				finalPoints = CatmullRomSmooth(rawPoints, c_SubdivisionsPerSegment);

				// Destination MUST NOT be moved. Fix PRE-LAST point if heading changed.
				int last = finalPoints.Count - 1;
				if (last >= 2)
				{
					Vector3 origDir = (corners[corners.Length - 1] - corners[corners.Length - 2]).normalized;
					Vector3 smoothDir = (finalPoints[last] - finalPoints[last - 1]).normalized;
					if (Vector3.Angle(origDir, smoothDir) > 5f)
					{
						float segLen = Vector3.Distance(finalPoints[last - 1], finalPoints[last]);
						finalPoints[last - 1] = finalPoints[last] - origDir * segLen;
					}
				}
			}
			else
			{
				finalPoints = new List<Vector3>(corners);
			}

			// Build PathPoints
			float dist = 0f;
			for (int i = 0; i < finalPoints.Count; i++)
			{
				var pp = new PathPoint(finalPoints[i]);
				if (i == 0)
				{
					pp.Tangent = (finalPoints[1] - finalPoints[0]).normalized;
				}
				else if (i == finalPoints.Count - 1)
				{
					pp.Tangent = (finalPoints[i] - finalPoints[i - 1]).normalized;
					pp.DistanceFromStart = dist;
				}
				else
				{
					pp.Tangent = (finalPoints[i + 1] - finalPoints[i - 1]).normalized;
					pp.DistanceFromStart = dist;
				}

				pp.Curvature = ComputeCurvature(finalPoints, i);
				path.Points.Add(pp);

				if (i < finalPoints.Count - 1)
					dist += Vector3.Distance(finalPoints[i], finalPoints[i + 1]);
			}

			path.TotalLength = dist;
			path.CurrentSegment = 0;

			// Diagnostic: visualize raw vs smoothed
			for (int i = 0; i < corners.Length - 1; i++)
				Debug.DrawLine(corners[i], corners[i + 1], Color.red, 5f);
			for (int i = 0; i < finalPoints.Count - 1; i++)
				Debug.DrawLine(finalPoints[i], finalPoints[i + 1], Color.cyan, 5f);

			if (corners.Length >= 2)
			{
				Vector3 first = corners[0];
				Vector3 last = corners[corners.Length - 1];
				Debug.Log($"[RevPathBuilder] maxAngle={maxAngle:F1}° smoothed={needsSmooth} corners={corners.Length} pts={path.Points.Count} " +
					$"length={path.TotalLength:F1}m start=({first.x:F2},{first.z:F2}) end=({last.x:F2},{last.z:F2})");
			}

			return path;
		}

		private static List<Vector3> CatmullRomSmooth(List<Vector3> _raw, int _subdivisions)
		{
			if (_raw.Count < 2)
				return new List<Vector3>(_raw);

			var result = new List<Vector3>();

			for (int i = 0; i < _raw.Count - 1; i++)
			{
				Vector3 p0 = _raw[Mathf.Max(0, i - 1)];
				Vector3 p1 = _raw[i];
				Vector3 p2 = _raw[i + 1];
				Vector3 p3 = _raw[Mathf.Min(_raw.Count - 1, i + 2)];

				for (int s = 0; s < _subdivisions; s++)
				{
					float t = s / (float)_subdivisions;
					result.Add(CatmullRom(p0, p1, p2, p3, t));
				}
			}
			result.Add(_raw[_raw.Count - 1]);

			return result;
		}

		private static Vector3 CatmullRom(Vector3 _p0, Vector3 _p1, Vector3 _p2, Vector3 _p3, float _t)
		{
			float t2 = _t * _t;
			float t3 = t2 * _t;

			return 0.5f * (
				(2f * _p1) +
				(-_p0 + _p2) * _t +
				(2f * _p0 - 5f * _p1 + 4f * _p2 - _p3) * t2 +
				(-_p0 + 3f * _p1 - 3f * _p2 + _p3) * t3
			);
		}

		private static float ComputeCurvature(List<Vector3> _pts, int _i)
		{
			if (_i <= 0 || _i >= _pts.Count - 1)
				return 0f;
			Vector3 a = _pts[_i - 1];
			Vector3 b = _pts[_i];
			Vector3 c = _pts[_i + 1];
			Vector3 ab = (b - a).normalized;
			Vector3 bc = (c - b).normalized;
			float angle = Vector3.Angle(ab, bc);
			float segLen = Vector3.Distance(b, c);
			if (segLen > 0.01f)
				return angle * Mathf.Deg2Rad / segLen;
			return 0f;
		}
	}
}
