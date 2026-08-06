using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct MotionCommand
	{
		public readonly float DesiredSpeedKmh;
		public readonly float DesiredCurvature;
		public readonly bool Reverse;
		public readonly StopIntent StopIntent;

		public GearDirection Gear
		{
			get
			{
				if (Reverse) return GearDirection.Reverse;
				if (DesiredSpeedKmh < -0.01f) return GearDirection.Reverse;
				return GearDirection.Forward;
			}
		}

		public static MotionCommand Empty => new MotionCommand(0f, 0f, false);

		public MotionCommand(
			float _desiredSpeedKmh,
			float _desiredCurvature,
			bool _reverse,
			StopIntent _stopIntent = StopIntent.None)
		{
			DesiredSpeedKmh = _desiredSpeedKmh;
			DesiredCurvature = _desiredCurvature;
			Reverse = _reverse;
			StopIntent = _stopIntent;
		}
	}

	public readonly struct VehicleParameters
	{
		public readonly float Length;
		public readonly float Width;
		public readonly float WheelBase;
		public readonly float MaxForwardSpeedKmh;
		public readonly float MaxReverseSpeedKmh;
		public readonly float MaxSteeringAngleDeg;
		public readonly float MinTurningRadius;
		public readonly float SteeringRateDegPerSec;
		public readonly float HardBrakeDecelMs2;
		public readonly AnimationCurve CurvatureSpeedCurve;
		public readonly VehicleKinematicsProfile Kinematics;

		public float ComfortBrakeDecelMs2 =>
			Mathf.Clamp(HardBrakeDecelMs2 * 0.3f, 0.7f, 1.2f);

		public float MaxSteeringAngleRad => MaxSteeringAngleDeg * Mathf.Deg2Rad;
		public float EffectiveTurnRadius =>
			Kinematics != null ? Kinematics.EffectiveTurnRadius : MinTurningRadius;

		public VehicleParameters(
			float _length, float _width, float _wheelBase,
			float _maxForwardSpeedKmh, float _maxReverseSpeedKmh,
			float _maxSteeringAngleDeg, float _steeringRateDegPerSec,
			float _hardBrakeDecelMs2, AnimationCurve _curvatureSpeedCurve,
			VehicleKinematicsProfile _kinematics = null)
		{
			Length = Mathf.Max(0.5f, _length);
			Width = Mathf.Max(0.5f, _width);
			WheelBase = Mathf.Max(0.5f, _wheelBase);
			MaxForwardSpeedKmh = Mathf.Max(1f, _maxForwardSpeedKmh);
			MaxReverseSpeedKmh = Mathf.Max(1f, _maxReverseSpeedKmh);
			MaxSteeringAngleDeg = Mathf.Clamp(_maxSteeringAngleDeg, 10f, 60f);
			MinTurningRadius = WheelBase / Mathf.Tan(MaxSteeringAngleDeg * Mathf.Deg2Rad);
			SteeringRateDegPerSec = Mathf.Max(1f, _steeringRateDegPerSec);
			HardBrakeDecelMs2 = Mathf.Max(1f, _hardBrakeDecelMs2);
			CurvatureSpeedCurve = _curvatureSpeedCurve ?? new AnimationCurve(
				new Keyframe(0f, 1f), new Keyframe(0.15f, 0.55f), new Keyframe(0.3f, 0.18f));
			Kinematics = _kinematics;
		}

		public static VehicleParameters FromTuning(VehicleTuning _tuning, Transform _root = null, VehicleNavigationSettings _settings = null)
		{
			if (_tuning == null) return Default;
			var profile = VehicleKinematicsProfile.FromVehicle(_root, _tuning, _settings);
			return profile.ToVehicleParameters(_tuning);
		}

		public static VehicleParameters Default => new VehicleParameters(
			4.8f, 2.4f, 3.5f, 90f, 30f, 30f, 120f, 5.5f, null,
			new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 30f));
	}

	/// <summary>
	/// Pure-pursuit trajectory controller.
	/// Takes a maneuver's waypoints and returns a MotionCommand
	/// (desired speed + curvature) — no raw steering/throttle.
	/// </summary>
	public sealed class PursuitController
	{
		/// <summary>
		/// Debug info captured during the last Tick() call.
		/// </summary>
		public readonly struct PursuitDebugInfo
		{
			public readonly float LookAheadDistance;
			public readonly int NearestWaypointIndex;
			public readonly int LookAheadTargetIndex;
			public readonly Vector3 LookAheadTargetPoint;
			public readonly float CrossTrackError;
			public readonly float RawCurvature;
			public readonly float ClampedCurvature;
			public readonly float CurvatureFraction;
			public readonly float ArrivalScale;
			public readonly float LaunchRamp;
			public readonly float CappedSpeedKmh;
			public readonly float PreviewCurvature;
		public readonly float DesiredSpeedBeforeReverse;
		public readonly bool IsReversing;
		public readonly int TotalWaypoints;

			public PursuitDebugInfo(
				float _lookAhead, int _nearest, int _target, Vector3 _targetPoint,
				float _crossTrack, float _rawCurvature, float _clampedCurvature,
				float _curvatureFraction, float _arrivalScale, float _launchRamp,
				float _cappedSpeed, float _previewCurvature,
				float _desiredSpeedKmh, bool _reversing, int _totalWp)
			{
				LookAheadDistance = _lookAhead;
				NearestWaypointIndex = _nearest;
				LookAheadTargetIndex = _target;
				LookAheadTargetPoint = _targetPoint;
				CrossTrackError = _crossTrack;
				RawCurvature = _rawCurvature;
				ClampedCurvature = _clampedCurvature;
				CurvatureFraction = _curvatureFraction;
				ArrivalScale = _arrivalScale;
				LaunchRamp = _launchRamp;
				CappedSpeedKmh = _cappedSpeed;
				PreviewCurvature = _previewCurvature;
				DesiredSpeedBeforeReverse = _desiredSpeedKmh;
				IsReversing = _reversing;
				TotalWaypoints = _totalWp;
			}
		}

		public struct Output
		{
			public MotionCommand Command;
			public float DistanceToEnd;
			public bool IsComplete;
			public bool TargetBehind;
		}

		public PursuitDebugInfo LastDebugInfo { get; private set; }

		private readonly AnimationCurve m_CurvatureSpeedCurve;
		private readonly AnimationCurve m_SteeringLimitCurve;

		private float m_PrevCurvatureSign;
		private int m_CurvatureFlipCount;
		private float m_AdaptiveLookAheadMult = 1f;
		private const float c_AdaptiveDecayRate = 0.3f;
		private const float c_BehindDot = -0.2f;
		private const float c_BehindMinCross = 0.05f;
		private const float c_BehindMaxSpeedKmh = 8f;
		private readonly TrajectoryPath m_Trajectory = new TrajectoryPath();
		private int m_LastPathRevision = -1;

		public PursuitController(AnimationCurve _curvatureSpeedCurve = null)
		{
			m_CurvatureSpeedCurve = _curvatureSpeedCurve ?? new AnimationCurve(
				new Keyframe(0f, 1f),
				new Keyframe(0.15f, 0.55f),
				new Keyframe(0.3f, 0.18f));
			m_SteeringLimitCurve = new AnimationCurve(
				new Keyframe(0f, 1f),
				new Keyframe(15f, 0.85f),
				new Keyframe(25f, 0.65f),
				new Keyframe(35f, 0.45f),
				new Keyframe(50f, 0.25f));
		}

		public Output Tick(
			NavigationContext _ctx,
			Maneuver _maneuver,
			float _speedCapFraction,
			float _topSpeedKmh,
			float _defaultLookAhead,
			float? _lookAheadOverride)
		{
			Output result = new Output();
			FeedbackState fb = _ctx.State;

			Vector3[] waypoints = _maneuver.Waypoints as Vector3[] ?? System.Array.Empty<Vector3>();
			if (waypoints.Length == 0)
			{
				result.IsComplete = true;
				result.Command = MotionCommand.Empty;
				return result;
			}

			bool isReversing = _maneuver.AllowReverse && _maneuver.Type != VehicleManeuverType.Forward;
			float lookAhead = ComputeLookAhead(fb.SpeedKmh,
				_lookAheadOverride ?? _defaultLookAhead);

			int pathRevision = _ctx.CurrentManeuverIndex * 1000 + waypoints.Length;
			if (pathRevision != m_LastPathRevision)
			{
				m_Trajectory.Build(waypoints, pathRevision);
				m_LastPathRevision = pathRevision;
			}

			Vector3 controlPoint = isReversing
				? GetRearAxle(fb.Position, fb.Forward, _ctx.Params)
				: GetFrontAxle(fb.Position, fb.Forward, _ctx.Params);

			m_Trajectory.Project(controlPoint, out int nearest, out _, out float crossTrackDeb);
			Vector3 target = m_Trajectory.GetLookAheadPoint(controlPoint, lookAhead);
			int targetIndex = nearest;

			// Remaining distance (needed early for curvature clamping).
			float distanceToEnd = m_Trajectory.RemainingDistance(controlPoint);
			result.DistanceToEnd = distanceToEnd;

			// --- curvature via pure pursuit: κ = 2·Δx / L² ---
			Vector3 toTarget = target - controlPoint;
			toTarget.y = 0f;
			float dist = toTarget.magnitude;
			float rawCurvature = 0f;
			float curvature = 0f;
			bool targetBehind = false;
			float maxCurv = 1f / Mathf.Max(0.5f, _ctx.Params.EffectiveTurnRadius);
			if (dist > 0.05f && lookAhead > 0.05f)
			{
				Vector3 toTargetDir = toTarget / dist;
				float cross = Vector3.Cross(fb.Forward, toTargetDir).y;
				float fwdDot = Vector3.Dot(fb.Forward, toTargetDir);
				if (!isReversing && fwdDot < c_BehindDot)
				{
					targetBehind = true;
					float side = Mathf.Abs(cross) < c_BehindMinCross ? 1f : Mathf.Sign(cross);
					rawCurvature = side * maxCurv;
				}
				else
				{
					float crossTrack = cross * dist;
					rawCurvature = 2f * crossTrack / (lookAhead * lookAhead);
				}
				curvature = rawCurvature;

				float closeness = 1f - Mathf.Clamp01(distanceToEnd / 6f);
				maxCurv = Mathf.Lerp(maxCurv, maxCurv * 0.35f, closeness);
				curvature = Mathf.Clamp(curvature, -maxCurv, maxCurv);

				float speedLimit = m_SteeringLimitCurve.Evaluate(Mathf.Abs(fb.SpeedKmh));
				float speedMaxCurv = maxCurv * speedLimit;
				curvature = Mathf.Clamp(curvature, -speedMaxCurv, speedMaxCurv);

				if (isReversing)
					curvature = -curvature;
			}

			// Adaptive lookahead: detect oscillation
			UpdateOscillation(rawCurvature);

			// --- desired speed from curvature ---
			float capKmh = Mathf.Max(1f, _topSpeedKmh) * Mathf.Clamp01(_speedCapFraction);
			if (targetBehind)
				capKmh = Mathf.Min(capKmh, c_BehindMaxSpeedKmh);

			// Preview: look ahead for tight turns and brake early.
			float previewCurvature = EvaluatePreviewCurvature(waypoints, nearest, targetIndex, lookAhead);
			float maxCurvature = Mathf.Max(Mathf.Abs(curvature), previewCurvature);
			float curvatureFraction = m_CurvatureSpeedCurve.Evaluate(maxCurvature);

			// Arrival: scale by remaining distance.
			float arrivalScale = Mathf.Clamp01(distanceToEnd / 15f);

			// Precision arrival: tight control when very close
			if (distanceToEnd < 2f)
			{
				lookAhead = Mathf.Min(lookAhead, 1.2f);
				arrivalScale = Mathf.Clamp01(distanceToEnd / 3f);
				curvature = Mathf.Clamp(curvature, -0.25f, 0.25f);
			}

			// Final approach speed clamp + lookahead reduction
			if (distanceToEnd < 1.5f)
			{
				capKmh = Mathf.Min(capKmh, 3f);
				lookAhead = Mathf.Min(lookAhead, 0.8f);
			}
			if (distanceToEnd < 0.6f)
			{
				capKmh = Mathf.Min(capKmh, 1f);
				lookAhead = Mathf.Min(lookAhead, 0.4f);
			}

			float targetKmh = capKmh * Mathf.Min(curvatureFraction, arrivalScale);
			float speedBeforeReverse = targetKmh;

			// Gentle start ramp.
			float absSpeed = Mathf.Abs(fb.SpeedKmh);
			float launchRamp = Mathf.Clamp01(absSpeed / 5f + 0.15f);
			targetKmh = Mathf.Lerp(Mathf.Min(targetKmh, 8f), targetKmh, launchRamp);

			if (isReversing)
				targetKmh = -targetKmh;

			result.Command = new MotionCommand(targetKmh, curvature, isReversing);
			result.TargetBehind = targetBehind;

			LastDebugInfo = new PursuitDebugInfo(
				lookAhead, nearest, targetIndex, target,
				crossTrackDeb, rawCurvature, curvature,
				curvatureFraction, arrivalScale, launchRamp,
				capKmh, previewCurvature, speedBeforeReverse,
				isReversing, waypoints.Length);

			// Completion check.
			ManeuverContext ctx = new ManeuverContext(
				fb.Position,
				fb.Forward,
				fb.SpeedKmh,
				lookAhead * 0.35f,
				isReversing);
			result.IsComplete = _maneuver.IsComplete(ctx);

			return result;
		}

		private float ComputeLookAhead(float _speedKmh, float _base)
		{
			float speed = Mathf.Max(0f, _speedKmh);
			float baseLA = Mathf.Clamp(_base + speed * 0.35f, 3f, 16f);
			return baseLA * m_AdaptiveLookAheadMult;
		}

		private void UpdateOscillation(float _rawCurvature)
		{
			float sign = _rawCurvature < -0.002f ? -1f : _rawCurvature > 0.002f ? 1f : 0f;
			if (sign != 0f && m_PrevCurvatureSign != 0f && sign != m_PrevCurvatureSign)
			{
				m_CurvatureFlipCount++;
				if (m_CurvatureFlipCount >= 3)
					m_AdaptiveLookAheadMult = 1.5f;
			}
			else if (m_CurvatureFlipCount > 0)
			{
				m_CurvatureFlipCount = System.Math.Max(0, m_CurvatureFlipCount - 1);
			}
			if (m_CurvatureFlipCount == 0)
				m_AdaptiveLookAheadMult = Mathf.Lerp(m_AdaptiveLookAheadMult, 1f, c_AdaptiveDecayRate);
			m_PrevCurvatureSign = sign;
		}

		private static int FindNearestWaypointIndex(Vector3[] _waypoints, Vector3 _position)
		{
			int best = 0;
			float bestSqr = float.MaxValue;
			for (int i = 0; i < _waypoints.Length; i++)
			{
				Vector3 d = _waypoints[i] - _position;
				d.y = 0f;
				float sqr = d.sqrMagnitude;
				if (sqr < bestSqr)
				{
					bestSqr = sqr;
					best = i;
				}
			}
			return best;
		}

		private static int FindLookAheadIndex(
			Vector3[] _waypoints, int _nearest, Vector3 _position, float _lookAhead)
		{
			float accumulated = 0f;
			for (int i = _nearest; i < _waypoints.Length - 1; i++)
			{
				Vector3 a = _waypoints[i];
				Vector3 b = _waypoints[i + 1];
				a.y = 0f;
				b.y = 0f;
				accumulated += Vector3.Distance(a, b);
				if (accumulated >= _lookAhead)
					return Mathf.Min(_waypoints.Length - 1, i + 1);
			}
			return _waypoints.Length - 1;
		}

		private static float EstimateDistanceToEnd(Vector3[] _waypoints, int _fromIndex, Vector3 _position)
		{
			float distance = 0f;
			if (_fromIndex < _waypoints.Length)
			{
				Vector3 a = _position;
				Vector3 b = _waypoints[_fromIndex];
				a.y = 0f;
				b.y = 0f;
				distance += Vector3.Distance(a, b);
				for (int i = _fromIndex; i < _waypoints.Length - 1; i++)
				{
					a = _waypoints[i];
					b = _waypoints[i + 1];
					a.y = 0f;
					b.y = 0f;
					distance += Vector3.Distance(a, b);
				}
			}
			return distance;
		}

		private static float EvaluatePreviewCurvature(
			Vector3[] _waypoints, int _nearest, int _targetIndex, float _lookAhead)
		{
			if (_waypoints == null || _waypoints.Length < 3)
				return 0f;

			float maxCurvature = 0f;
			int end = Mathf.Min(_targetIndex + 4, _waypoints.Length - 1);
			for (int i = _nearest + 1; i < end && i < _waypoints.Length - 1; i++)
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
					// Approximate curvature for this segment.
					float segCurv = angle * Mathf.Deg2Rad / segLength;
					if (segCurv > maxCurvature)
						maxCurvature = segCurv;
				}
			}
			return maxCurvature;
		}

		private static Vector3 GetFrontAxle(Vector3 _position, Vector3 _forward, VehicleParameters _params)
		{
			if (_params.Kinematics != null)
				return _params.Kinematics.FrontAxlePosition(_position, _forward);
			return _position + _forward.normalized * (_params.WheelBase * 0.5f);
		}

		private static Vector3 GetRearAxle(Vector3 _position, Vector3 _forward, VehicleParameters _params)
		{
			if (_params.Kinematics != null)
				return _params.Kinematics.RearAxlePosition(_position, _forward);
			return _position - _forward.normalized * (_params.WheelBase * 0.5f);
		}
	}
}
