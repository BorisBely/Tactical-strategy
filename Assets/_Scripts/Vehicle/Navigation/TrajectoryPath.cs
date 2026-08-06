using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Arc-length indexed polyline with monotonic progress tracking.
	/// </summary>
	public sealed class TrajectoryPath
	{
		private readonly List<Vector3> m_Points = new List<Vector3>();
		private readonly List<float> m_Cumulative = new List<float>();
		private int m_CurrentSegment;
		private int m_Revision;

		public int Revision => m_Revision;
		public int CurrentSegment => m_CurrentSegment;
		public int PointCount => m_Points.Count;
		public float TotalLength => m_Cumulative.Count > 0 ? m_Cumulative[m_Cumulative.Count - 1] : 0f;
		public IReadOnlyList<Vector3> Points => m_Points;

		public void Build(Vector3[] _points, int _revision = 0)
		{
			m_Points.Clear();
			m_Cumulative.Clear();
			m_CurrentSegment = 0;
			m_Revision = _revision;

			if (_points == null || _points.Length == 0)
				return;

			m_Points.Add(_points[0]);
			m_Cumulative.Add(0f);

			for (int i = 1; i < _points.Length; i++)
			{
				Vector3 a = _points[i - 1];
				Vector3 b = _points[i];
				a.y = 0f;
				b.y = 0f;
				if (Vector3.Distance(a, b) < 0.05f)
					continue;

				float len = m_Cumulative[m_Cumulative.Count - 1] + Vector3.Distance(a, b);
				m_Points.Add(_points[i]);
				m_Cumulative.Add(len);
			}
		}

		public void ResetProgress() => m_CurrentSegment = 0;

		public float RemainingDistance(Vector3 _position)
		{
			if (m_Points.Count == 0)
				return 0f;

			Project(_position, out int seg, out float segT, out float distToSeg);
			float traveledOnSeg = segT * SegmentLength(seg);
			float segRemain = SegmentLength(seg) - traveledOnSeg;
			float total = segRemain;
			for (int i = seg + 1; i < m_Points.Count - 1; i++)
				total += SegmentLength(i);
			return total;
		}

		public Vector3 GetLookAheadPoint(Vector3 _position, float _lookAhead)
		{
			if (m_Points.Count == 0)
				return _position;
			if (m_Points.Count == 1)
				return m_Points[0];

			Project(_position, out int seg, out float segT, out _);
			float startLen = m_Cumulative[seg] + segT * SegmentLength(seg);
			float targetLen = startLen + Mathf.Max(0.05f, _lookAhead);

			for (int i = seg; i < m_Points.Count - 1; i++)
			{
				float segStart = m_Cumulative[i];
				float segEnd = m_Cumulative[i + 1];
				if (targetLen <= segEnd)
				{
					float t = SegmentLength(i) > 0.001f
						? (targetLen - segStart) / SegmentLength(i)
						: 1f;
					return Vector3.Lerp(m_Points[i], m_Points[i + 1], Mathf.Clamp01(t));
				}
			}

			return m_Points[m_Points.Count - 1];
		}

		public void Project(
			Vector3 _position,
			out int _segment,
			out float _segmentT,
			out float _crossTrack)
		{
			_segment = Mathf.Clamp(m_CurrentSegment, 0, Mathf.Max(0, m_Points.Count - 2));
			_segmentT = 0f;
			_crossTrack = float.MaxValue;

			if (m_Points.Count < 2)
			{
				_crossTrack = m_Points.Count == 1
					? FlatDistance(_position, m_Points[0])
					: 0f;
				return;
			}

			int bestSeg = _segment;
			float bestCross = float.MaxValue;
			float bestT = 0f;

			int searchEnd = Mathf.Min(m_Points.Count - 2, _segment + 6);
			for (int i = _segment; i <= searchEnd; i++)
			{
				Vector3 a = m_Points[i];
				Vector3 b = m_Points[i + 1];
				a.y = 0f;
				b.y = 0f;
				Vector3 p = _position;
				p.y = 0f;

				Vector3 ab = b - a;
				float abLen = ab.magnitude;
				if (abLen < 0.001f)
					continue;

				float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / (abLen * abLen));
				Vector3 closest = a + ab * t;
				float cross = Vector3.Distance(p, closest);
				if (cross < bestCross)
				{
					bestCross = cross;
					bestSeg = i;
					bestT = t;
				}
			}

			_segment = bestSeg;
			_segmentT = bestT;
			_crossTrack = bestCross;

			if (bestT > 0.85f && bestSeg < m_Points.Count - 2)
				m_CurrentSegment = bestSeg + 1;
			else
				m_CurrentSegment = bestSeg;
		}

		private float SegmentLength(int _seg)
		{
			if (_seg < 0 || _seg >= m_Cumulative.Count - 1)
				return 0f;
			return m_Cumulative[_seg + 1] - m_Cumulative[_seg];
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
