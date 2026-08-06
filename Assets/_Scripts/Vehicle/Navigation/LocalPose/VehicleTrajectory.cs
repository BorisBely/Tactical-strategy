using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public enum TrajectoryGear : sbyte
	{
		Forward = 1,
		Reverse = -1
	}

	public readonly struct TrajectoryPoint
	{
		public readonly Vector3 Position;
		public readonly float YawDegrees;
		public readonly float Curvature;
		public readonly TrajectoryGear Gear;
		public readonly float ArcLength;
		public readonly bool IsCusp;

		public TrajectoryPoint(
			Vector3 _position,
			float _yawDegrees,
			float _curvature,
			TrajectoryGear _gear,
			float _arcLength,
			bool _isCusp = false)
		{
			Position = _position;
			YawDegrees = _yawDegrees;
			Curvature = _curvature;
			Gear = _gear;
			ArcLength = _arcLength;
			IsCusp = _isCusp;
		}
	}

	/// <summary>
	/// Validated kinematic trajectory with gear segments and cusp stops.
	/// </summary>
	public sealed class VehicleTrajectory
	{
		private const float c_MaxSampleSpacing = 0.25f;
		private const float c_DensifyEndpointTol = 0.12f;
		/// <summary>Active vehicle wheelBase used by densify integrate (set by planner session).</summary>
		public static float DensifyWheelBase = 3.5f;

		private readonly List<TrajectoryPoint> m_Points = new List<TrajectoryPoint>();
		private readonly List<int> m_CuspIndices = new List<int>();

		public bool IsValid { get; private set; }
		public float TotalLength { get; private set; }
		public float Cost { get; private set; }
		public int ExpandedNodes { get; private set; }
		public string DebugReason { get; private set; } = string.Empty;
		public IReadOnlyList<TrajectoryPoint> Points => m_Points;
		public IReadOnlyList<int> CuspIndices => m_CuspIndices;
		public int PointCount => m_Points.Count;
		public int GearSegmentCount { get; private set; }

		public static VehicleTrajectory Invalid(string _reason, int _expanded = 0)
		{
			return new VehicleTrajectory
			{
				IsValid = false,
				DebugReason = _reason ?? "invalid",
				ExpandedNodes = _expanded
			};
		}

		public void Build(List<TrajectoryPoint> _points, float _cost, int _expanded, string _reason)
		{
			m_Points.Clear();
			m_CuspIndices.Clear();
			Cost = _cost;
			ExpandedNodes = _expanded;
			DebugReason = _reason ?? string.Empty;
			GearSegmentCount = 0;
			TotalLength = 0f;

			if (_points == null || _points.Count < 2)
			{
				IsValid = false;
				DebugReason = string.IsNullOrEmpty(DebugReason) ? "too few points" : DebugReason;
				return;
			}

			_points = DensifySamples(
				_points, c_MaxSampleSpacing, Mathf.Max(0.5f, DensifyWheelBase));

			TrajectoryGear lastGear = _points[0].Gear;
			GearSegmentCount = 1;
			float arc = 0f;

			for (int i = 0; i < _points.Count; i++)
			{
				TrajectoryPoint p = _points[i];
				if (i > 0)
					arc += BicycleKinematics.FlatDistance(_points[i - 1].Position, p.Position);

				bool gearChanged = i > 0 && p.Gear != lastGear;
				if (gearChanged)
				{
					GearSegmentCount++;
					lastGear = p.Gear;
					if (m_Points.Count > 0)
					{
						int cuspIndex = m_Points.Count - 1;
						m_CuspIndices.Add(cuspIndex);
						TrajectoryPoint prev = m_Points[cuspIndex];
						m_Points[cuspIndex] = new TrajectoryPoint(
							prev.Position, prev.YawDegrees, prev.Curvature, prev.Gear, prev.ArcLength, true);
					}
				}

				// Cusp is owned by the previous gear-boundary point only.
				m_Points.Add(new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, arc, false));
			}

			TotalLength = m_Points[m_Points.Count - 1].ArcLength;
			IsValid = TotalLength >= 0f && m_Points.Count >= 2;
		}

		public Vector3[] ToPositions()
		{
			var arr = new Vector3[m_Points.Count];
			for (int i = 0; i < m_Points.Count; i++)
				arr[i] = m_Points[i].Position;
			return arr;
		}

		public float RemainingDistance(int _fromIndex)
		{
			if (!IsValid || m_Points.Count == 0)
				return 0f;
			_fromIndex = Mathf.Clamp(_fromIndex, 0, m_Points.Count - 1);
			return Mathf.Max(0f, TotalLength - m_Points[_fromIndex].ArcLength);
		}

		public int FindNextCusp(int _fromIndex)
		{
			for (int i = 0; i < m_CuspIndices.Count; i++)
			{
				if (m_CuspIndices[i] > _fromIndex)
					return m_CuspIndices[i];
			}
			return -1;
		}

		public TrajectoryGear GearAt(int _index)
		{
			if (!IsValid || m_Points.Count == 0)
				return TrajectoryGear.Forward;
			_index = Mathf.Clamp(_index, 0, m_Points.Count - 1);
			return m_Points[_index].Gear;
		}

		public void GetSegmentBounds(int _index, out int _start, out int _end)
		{
			_start = 0;
			_end = m_Points.Count - 1;
			if (!IsValid || m_Points.Count == 0)
				return;

			_index = Mathf.Clamp(_index, 0, m_Points.Count - 1);
			TrajectoryGear gear = m_Points[_index].Gear;

			// Bound by continuous gear — never include the next maneuver's samples.
			// (Cusp-index bounds previously set _end to the next cusp or path end while
			// still sitting on the cusp point, so look-ahead jumped into gear #2 early.)
			_start = _index;
			while (_start > 0 && m_Points[_start - 1].Gear == gear)
				_start--;

			_end = _index;
			while (_end < m_Points.Count - 1 && m_Points[_end + 1].Gear == gear)
				_end++;
		}

		private static List<TrajectoryPoint> DensifySamples(
			List<TrajectoryPoint> _source,
			float _maxSpacing,
			float _wheelBase = 3.5f)
		{
			if (_source == null || _source.Count < 2 || _maxSpacing <= 0f)
				return _source;

			var result = new List<TrajectoryPoint>(_source.Count * 3) { _source[0] };
			for (int i = 1; i < _source.Count; i++)
			{
				TrajectoryPoint a = _source[i - 1];
				TrajectoryPoint b = _source[i];

				// Never densify across a gear boundary (cusp stop).
				if (a.Gear != b.Gear)
				{
					result.Add(b);
					continue;
				}

				float dist = BicycleKinematics.FlatDistance(a.Position, b.Position);
				if (dist <= _maxSpacing)
				{
					result.Add(b);
					continue;
				}

				float arcLen = b.ArcLength > a.ArcLength + 0.001f
					? b.ArcLength - a.ArcLength
					: dist;
				float curv = Mathf.Abs(a.Curvature) > 1e-4f ? a.Curvature : b.Curvature;
				float dyaw = Mathf.Abs(Mathf.DeltaAngle(a.YawDegrees, b.YawDegrees));
				if (Mathf.Abs(curv) < 1e-4f && dyaw > 2f && arcLen > 0.05f)
					curv = Mathf.DeltaAngle(a.YawDegrees, b.YawDegrees) * Mathf.Deg2Rad / arcLen;

				if (Mathf.Abs(curv) > 1e-4f &&
				    TryDensifyByIntegrate(a, b, curv, arcLen, _maxSpacing, _wheelBase, result))
					continue;

				// Straight / fallback: position lerp + shortest-angle yaw (never chord-yaw).
				int segments = Mathf.CeilToInt(dist / _maxSpacing);
				for (int j = 1; j < segments; j++)
					result.Add(LerpTrajectoryPoint(a, b, j / (float)segments));
				result.Add(b);
			}

			return result;
		}

		/// <summary>
		/// Densify via bicycle integration only when the integrated endpoint matches the source endpoint.
		/// </summary>
		private static bool TryDensifyByIntegrate(
			TrajectoryPoint _a,
			TrajectoryPoint _b,
			float _curv,
			float _arcLen,
			float _maxSpacing,
			float _wheelBase,
			List<TrajectoryPoint> _result)
		{
			int samples = Mathf.Max(2, Mathf.CeilToInt(_arcLen / _maxSpacing));
			var prim = BicycleKinematics.Integrate(
				_a.Position, _a.YawDegrees, _curv, _a.Gear, _arcLen, _wheelBase, 0f, samples);
			if (prim.Samples == null || prim.Samples.Count < 2)
				return false;

			float posErr = BicycleKinematics.FlatDistance(prim.EndPosition, _b.Position);
			float yawErr = Mathf.Abs(Mathf.DeltaAngle(prim.EndYawDegrees, _b.YawDegrees));
			if (posErr > c_DensifyEndpointTol || yawErr > 8f)
				return false;

			for (int j = 1; j < prim.Samples.Count - 1; j++)
				_result.Add(prim.Samples[j]);
			_result.Add(_b);
			return true;
		}

		private static TrajectoryPoint LerpTrajectoryPoint(
			TrajectoryPoint _a,
			TrajectoryPoint _b,
			float _t)
		{
			float yaw = _a.YawDegrees + Mathf.DeltaAngle(_a.YawDegrees, _b.YawDegrees) * _t;
			return new TrajectoryPoint(
				Vector3.Lerp(_a.Position, _b.Position, _t),
				yaw,
				Mathf.Lerp(_a.Curvature, _b.Curvature, _t),
				_a.Gear,
				0f);
		}
	}
}
