using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Bicycle-model motion primitives for lattice / Hybrid A* expansion.
	/// Optional steer-ramp (clothoid-like) matches physical wheel slew rate.
	/// </summary>
	public static class BicycleKinematics
	{
		/// <summary>deg/s physical road-wheel slew used by ResampleWithSteerRamp / Integrate ramp.</summary>
		public static float SteerRateDegPerSec = 160f;
		/// <summary>Max road-wheel angle (deg) for δ↔κ conversion during ramp.</summary>
		public static float MaxSteerDegForRamp = 28f;
		/// <summary>Reference speed (m/s) when converting steer time → arc length.</summary>
		public static float SteerRampRefSpeedMps = 2.5f;
		public static bool EnableSteerRamp = true;

		public readonly struct Primitive
		{
			public readonly float Length;
			public readonly float Curvature;
			public readonly TrajectoryGear Gear;
			public readonly Vector3 EndPosition;
			public readonly float EndYawDegrees;
			public readonly List<TrajectoryPoint> Samples;

			public Primitive(
				float _length,
				float _curvature,
				TrajectoryGear _gear,
				Vector3 _endPosition,
				float _endYawDegrees,
				List<TrajectoryPoint> _samples)
			{
				Length = _length;
				Curvature = _curvature;
				Gear = _gear;
				EndPosition = _endPosition;
				EndYawDegrees = _endYawDegrees;
				Samples = _samples;
			}
		}

		public static readonly float[] DefaultCurvatureFractions = { 0f, 0.5f, 1f, -0.5f, -1f };

		public static List<Primitive> Expand(
			Vector3 _position,
			float _yawDegrees,
			float _wheelBase,
			float _minTurnRadius,
			float _stepLength,
			float _arcLengthStart,
			bool _allowReverse,
			float[] _curvatureFractions = null)
		{
			float maxCurv = 1f / Mathf.Max(0.5f, _minTurnRadius);
			float[] fractions = _curvatureFractions ?? DefaultCurvatureFractions;
			var result = new List<Primitive>(fractions.Length * (_allowReverse ? 2 : 1));

			for (int g = 0; g < (_allowReverse ? 2 : 1); g++)
			{
				TrajectoryGear gear = g == 0 ? TrajectoryGear.Forward : TrajectoryGear.Reverse;
				for (int i = 0; i < fractions.Length; i++)
				{
					float curv = fractions[i] * maxCurv;
					// Lattice keeps constant-κ primitives (geometry / hashing); ramp is applied
					// to finalized analytic trajectories via ResampleWithSteerRamp.
					Primitive prim = Integrate(
						_position, _yawDegrees, curv, gear, _stepLength, _wheelBase, _arcLengthStart);
					if (prim.Samples != null && prim.Samples.Count > 0)
						result.Add(prim);
				}
			}

			return result;
		}

		/// <summary>
		/// Constant-curvature integrate (analytic CS/Dubins/RS geometry). No steer ramp.
		/// </summary>
		public static Primitive Integrate(
			Vector3 _start,
			float _yawDegrees,
			float _curvature,
			TrajectoryGear _gear,
			float _length,
			float _wheelBase,
			float _arcLengthStart,
			int _sampleCount = 0)
		{
			return IntegrateInternal(
				_start, _yawDegrees, _curvature, _curvature, _gear, _length, _wheelBase,
				_arcLengthStart, _sampleCount, false);
		}

		/// <summary>
		/// Integrate with linear κ ramp from _startCurvature to _targetCurvature over L_ramp.
		/// </summary>
		public static Primitive IntegrateWithSteerRamp(
			Vector3 _start,
			float _yawDegrees,
			float _startCurvature,
			float _targetCurvature,
			TrajectoryGear _gear,
			float _length,
			float _wheelBase,
			float _arcLengthStart,
			int _sampleCount = 0)
		{
			return IntegrateInternal(
				_start, _yawDegrees, _startCurvature, _targetCurvature, _gear, _length, _wheelBase,
				_arcLengthStart, _sampleCount, true);
		}

		public static float ComputeSteerRampLength(
			float _wheelBase,
			float _kappa0,
			float _kappa1,
			float _steerRateDegPerSec = -1f,
			float _maxSteerDeg = -1f,
			float _vRefMps = -1f)
		{
			float wb = Mathf.Max(0.5f, _wheelBase);
			float rate = _steerRateDegPerSec > 0f ? _steerRateDegPerSec : SteerRateDegPerSec;
			float maxSteer = _maxSteerDeg > 0f ? _maxSteerDeg : MaxSteerDegForRamp;
			float vRef = _vRefMps > 0f ? _vRefMps : SteerRampRefSpeedMps;

			float d0 = Mathf.Atan(wb * _kappa0) * Mathf.Rad2Deg;
			float d1 = Mathf.Atan(wb * _kappa1) * Mathf.Rad2Deg;
			float deltaDeg = Mathf.Abs(d1 - d0);
			if (deltaDeg < 0.5f)
				return 0f;

			float time = deltaDeg / Mathf.Max(1f, rate);
			// 1.4x: road-wheel slew + tire lag so plan is not sharper than physics can hold.
			float len = time * Mathf.Max(0.5f, vRef) * 1.4f;
			return Mathf.Clamp(len, 0.35f, 1.2f);
		}

		/// <summary>
		/// Re-integrate an accepted path with steer ramps between sample curvatures.
		/// Geometry shifts slightly; caller should refine the endpoint if needed.
		/// </summary>
		public static VehicleTrajectory ResampleWithSteerRamp(
			VehicleTrajectory _src,
			float _wheelBase,
			string _reasonSuffix = "+clothoid")
		{
			if (_src == null || !_src.IsValid || _src.PointCount < 2 || !EnableSteerRamp)
				return _src;

			float wb = Mathf.Max(0.5f, _wheelBase);
			var pts = new List<TrajectoryPoint>(_src.PointCount * 2);
			TrajectoryPoint p0 = _src.Points[0];
			// Assume wheels start near zero at path begin (rest / pre-steer).
			float κPrev = 0f;
			pts.Add(new TrajectoryPoint(p0.Position, p0.YawDegrees, κPrev, p0.Gear, 0f, p0.IsCusp));

			Vector3 pos = p0.Position;
			float yaw = p0.YawDegrees;
			float arc = 0f;

			for (int i = 1; i < _src.PointCount; i++)
			{
				TrajectoryPoint b = _src.Points[i];
				TrajectoryPoint a = _src.Points[i - 1];
				float segLen = b.ArcLength > a.ArcLength + 0.001f
					? b.ArcLength - a.ArcLength
					: FlatDistance(a.Position, b.Position);
				segLen = Mathf.Max(0.05f, segLen);

				if (a.Gear != b.Gear)
				{
					// Cusp: reset ramp state; keep cusp point.
					κPrev = 0f;
					pos = b.Position;
					yaw = b.YawDegrees;
					arc = b.ArcLength;
					pts.Add(new TrajectoryPoint(pos, yaw, κPrev, b.Gear, arc, true));
					continue;
				}

				float κTarget = Mathf.Abs(b.Curvature) > 1e-4f ? b.Curvature : a.Curvature;
				int samples = Mathf.Max(2, Mathf.CeilToInt(segLen / 0.25f));
				Primitive prim = IntegrateWithSteerRamp(
					pos, yaw, κPrev, κTarget, a.Gear, segLen, wb, arc, samples);
				if (prim.Samples == null || prim.Samples.Count < 2)
				{
					pos = b.Position;
					yaw = b.YawDegrees;
					arc = b.ArcLength;
					κPrev = κTarget;
					pts.Add(new TrajectoryPoint(pos, yaw, κPrev, b.Gear, arc, b.IsCusp));
					continue;
				}

				for (int s = 1; s < prim.Samples.Count; s++)
					pts.Add(prim.Samples[s]);

				pos = prim.EndPosition;
				yaw = prim.EndYawDegrees;
				arc = prim.Samples[prim.Samples.Count - 1].ArcLength;
				κPrev = κTarget;
			}

			NormalizeArcLengths(pts);
			var rebuilt = new VehicleTrajectory();
			string reason = _src.DebugReason ?? "path";
			if (!reason.Contains("clothoid"))
				reason += _reasonSuffix;
			rebuilt.Build(pts, _src.Cost, _src.ExpandedNodes, reason);
			return rebuilt.IsValid ? rebuilt : _src;
		}

		public static void ConfigureSteerRampFromProfile(VehicleKinematicsProfile _profile, float _steerRateDegPerSec)
		{
			if (_profile == null)
				return;
			MaxSteerDegForRamp = Mathf.Max(10f, _profile.MaxSteeringAngleDeg);
			if (_steerRateDegPerSec > 1f)
				SteerRateDegPerSec = _steerRateDegPerSec;
		}

		private static Primitive IntegrateInternal(
			Vector3 _start,
			float _yawDegrees,
			float _startCurvature,
			float _targetCurvature,
			TrajectoryGear _gear,
			float _length,
			float _wheelBase,
			float _arcLengthStart,
			int _sampleCount,
			bool _ramp)
		{
			float length = Mathf.Max(0.05f, _length);
			int samples = _sampleCount > 0
				? _sampleCount
				: Mathf.Max(2, Mathf.CeilToInt(length / 0.25f));

			float signedLen = _gear == TrajectoryGear.Reverse ? -length : length;
			float step = signedLen / samples;
			float absStep = Mathf.Abs(step);

			float rampLen = 0f;
			if (_ramp && EnableSteerRamp &&
			    Mathf.Abs(_targetCurvature - _startCurvature) > 1e-4f)
			{
				rampLen = ComputeSteerRampLength(_wheelBase, _startCurvature, _targetCurvature);
				rampLen = Mathf.Min(rampLen, length);
			}

			Vector3 pos = _start;
			float yaw = _yawDegrees;
			float arc = _arcLengthStart;
			float κ0 = _startCurvature;
			var pts = new List<TrajectoryPoint>(samples + 1)
			{
				new TrajectoryPoint(pos, yaw, κ0, _gear, arc)
			};

			float traveled = 0f;
			for (int i = 1; i <= samples; i++)
			{
				traveled += absStep;
				float κ = _targetCurvature;
				if (rampLen > 1e-4f)
				{
					float t = Mathf.Clamp01(traveled / rampLen);
					κ = Mathf.Lerp(_startCurvature, _targetCurvature, t);
				}

				float yawRad = yaw * Mathf.Deg2Rad;
				pos = new Vector3(
					pos.x + Mathf.Sin(yawRad) * step,
					pos.y,
					pos.z + Mathf.Cos(yawRad) * step);

				if (Mathf.Abs(κ) > 1e-5f)
				{
					float dYawRad = step * κ;
					yaw = NormalizeYaw(yaw + dYawRad * Mathf.Rad2Deg);
				}

				arc += absStep;
				pts.Add(new TrajectoryPoint(pos, yaw, κ, _gear, arc));
			}

			return new Primitive(length, _targetCurvature, _gear, pos, yaw, pts);
		}

		private static void NormalizeArcLengths(List<TrajectoryPoint> _points)
		{
			if (_points == null || _points.Count == 0)
				return;
			float arc = 0f;
			for (int i = 0; i < _points.Count; i++)
			{
				TrajectoryPoint p = _points[i];
				if (i > 0)
					arc += FlatDistance(_points[i - 1].Position, p.Position);
				_points[i] = new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, arc, p.IsCusp);
			}
		}

		public static float HeuristicDistance(
			Vector3 _from,
			float _fromYaw,
			Vector3 _to,
			float? _toYaw,
			float _minTurnRadius)
		{
			float dist = FlatDistance(_from, _to);
			if (!_toYaw.HasValue)
				return dist;

			float yawErr = Mathf.Abs(Mathf.DeltaAngle(_fromYaw, _toYaw.Value));
			float turnCost = yawErr * Mathf.Deg2Rad * Mathf.Max(1f, _minTurnRadius);
			return dist + 0.35f * turnCost;
		}

		public static float NormalizeYaw(float _yaw)
		{
			_yaw %= 360f;
			if (_yaw < 0f) _yaw += 360f;
			return _yaw;
		}

		public static Vector3 YawToForward(float _yawDegrees)
		{
			float rad = _yawDegrees * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
		}

		public static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
