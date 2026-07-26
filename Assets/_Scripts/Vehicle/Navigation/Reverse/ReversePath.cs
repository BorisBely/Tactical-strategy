using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public struct PathPoint
	{
		public Vector3 Position;
		public Vector3 Tangent;
		public float Curvature;
		public float DistanceFromStart;

		public PathPoint(Vector3 _pos)
		{
			Position = _pos;
			Tangent = Vector3.forward;
			Curvature = 0f;
			DistanceFromStart = 0f;
		}
	}

	/// <summary>
	/// Full reverse path built from NavMesh corners with Catmull-Rom smoothing.
	/// Not just Vector3[] — a proper object with segment tracking and queries.
	/// </summary>
	public sealed class ReversePath
	{
		public List<PathPoint> Points;
		public float TotalLength;
		public int CurrentSegment;

		public bool IsValid => Points != null && Points.Count >= 2;
		public bool IsComplete => CurrentSegment >= Points.Count - 1;
		public float RemainingDistance
		{
			get
			{
				if (!IsValid || IsComplete)
					return 0f;
				float d = Points[Points.Count - 1].DistanceFromStart
					- Points[CurrentSegment].DistanceFromStart;
				return Mathf.Max(0f, d);
			}
		}

		public ReversePath()
		{
			Points = new List<PathPoint>();
			CurrentSegment = 0;
		}

		/// <summary>
		/// Find a point BEHIND the vehicle (or along the path) at given distance from rear axle.
		/// </summary>
		public Vector3 GetLookBehind(Vector3 _rearAxlePos, float _distance)
		{
			if (!IsValid)
				return _rearAxlePos;

			int startSeg = FindClosestSegment(_rearAxlePos);
			Vector3 closest = ClosestPointOnPath(_rearAxlePos, out float _, out int _);
			float accum = Vector3.Distance(_rearAxlePos, closest);

			for (int i = startSeg; i < Points.Count - 1; i++)
			{
				Vector3 a = Points[i].Position;
				Vector3 b = Points[i + 1].Position;
				float segLen = Vector3.Distance(a, b);
				if (accum + segLen >= _distance)
				{
					float t = (_distance - accum) / segLen;
					return Vector3.Lerp(a, b, Mathf.Clamp01(t));
				}
				accum += segLen;
			}
			return Points[Points.Count - 1].Position;
		}

		/// <summary>
		/// Advance segment when rear axle crosses the perpendicular plane through the next point.
		/// Returns true if segment advanced.
		/// </summary>
		public bool Advance(Vector3 _rearAxlePos)
		{
			if (!IsValid || IsComplete)
				return false;

			Vector3 nextPt = Points[Mathf.Min(CurrentSegment + 1, Points.Count - 1)].Position;
			Vector3 segDir = (nextPt - Points[CurrentSegment].Position).normalized;
			Vector3 toRear = _rearAxlePos - nextPt;
			float dot = Vector3.Dot(toRear, segDir);
			if (dot > 0f)
			{
				CurrentSegment++;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Curvature at a given segment index.
		/// </summary>
		public float CurvatureAt(int _segment)
		{
			if (!IsValid || _segment < 0 || _segment >= Points.Count)
				return 0f;
			return Points[_segment].Curvature;
		}

		/// <summary>
		/// Find the closest point on the path to a given world position.
		/// </summary>
		public Vector3 ClosestPointOnPath(Vector3 _pos, out float _distance, out int _segment)
		{
			_distance = float.MaxValue;
			_segment = 0;
			Vector3 best = _pos;
			if (!IsValid)
				return best;

			for (int i = 0; i < Points.Count - 1; i++)
			{
				Vector3 a = Points[i].Position;
				Vector3 b = Points[i + 1].Position;
				Vector3 ab = b - a;
				float len = ab.magnitude;
				if (len < 0.001f) continue;
				Vector3 dir = ab / len;
				float t = Mathf.Clamp01(Vector3.Dot(_pos - a, dir) / len);
				Vector3 pt = Vector3.Lerp(a, b, t);
				float d = Vector3.Distance(_pos, pt);
				if (d < _distance)
				{
					_distance = d;
					best = pt;
					_segment = i;
				}
			}
			return best;
		}

		public float DistanceAlong(Vector3 _pos)
		{
			if (!IsValid)
				return 0f;
			ClosestPointOnPath(_pos, out float _, out int seg);
			if (seg < 0 || seg >= Points.Count - 1)
				return 0f;
			Vector3 a = Points[seg].Position;
			Vector3 b = Points[seg + 1].Position;
			Vector3 ab = b - a;
			float len = ab.magnitude;
			if (len < 0.001f) return Points[seg].DistanceFromStart;
			float t = Mathf.Clamp01(Vector3.Dot(_pos - a, ab.normalized) / len);
			return Points[seg].DistanceFromStart + t * len;
		}

		public int FindClosestSegment(Vector3 _pos)
		{
			ClosestPointOnPath(_pos, out float _, out int seg);
			return seg;
		}
	}
}
