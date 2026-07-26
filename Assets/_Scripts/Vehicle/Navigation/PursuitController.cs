using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public readonly struct MotionCommand
	{
		public readonly float DesiredSpeedKmh;
		public readonly float DesiredCurvature;
		public readonly bool Reverse;
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

		public MotionCommand(float _desiredSpeedKmh, float _desiredCurvature, bool _reverse)
		{
			DesiredSpeedKmh = _desiredSpeedKmh;
			DesiredCurvature = _desiredCurvature;
			Reverse = _reverse;
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

		public float MaxSteeringAngleRad => MaxSteeringAngleDeg * Mathf.Deg2Rad;

		public VehicleParameters(
			float _length, float _width, float _wheelBase,
			float _maxForwardSpeedKmh, float _maxReverseSpeedKmh,
			float _maxSteeringAngleDeg, float _steeringRateDegPerSec,
			float _hardBrakeDecelMs2, AnimationCurve _curvatureSpeedCurve)
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
		}

		public static VehicleParameters FromTuning(VehicleTuning _tuning)
		{
			if (_tuning == null) return Default;
			float brakeDecel = _tuning.HardBrakeTorque / Mathf.Max(1f, _tuning.RigidbodyMass);
			if (brakeDecel < 1f) brakeDecel = 5.5f;
			return new VehicleParameters(
				4.8f, 2.4f, _tuning.WheelBase, _tuning.TopSpeedKmh,
				_tuning.TopSpeedKmh * 0.35f, _tuning.DefaultSteerAngle,
				_tuning.SteerRate, brakeDecel, _tuning.CurvatureSpeedCurve);
		}

		public static VehicleParameters Default => new VehicleParameters(
			4.8f, 2.4f, 3.5f, 90f, 30f, 30f, 120f, 5.5f, null);
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
		}

		public PursuitDebugInfo LastDebugInfo { get; private set; }

		private readonly AnimationCurve m_CurvatureSpeedCurve;
		private readonly AnimationCurve m_SteeringLimitCurve;

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
			float lookAhead = ComputeLookAhead(
				fb.SpeedKmh,
				_lookAheadOverride ?? _defaultLookAhead);

			int nearest = FindNearestWaypointIndex(waypoints, fb.Position);
			int targetIndex = FindLookAheadIndex(waypoints, nearest, fb.Position, lookAhead);
			Vector3 target = waypoints[targetIndex];

			// Remaining distance (needed early for curvature clamping).
			float distanceToEnd = EstimateDistanceToEnd(waypoints, targetIndex, fb.Position);
			result.DistanceToEnd = distanceToEnd;

			// --- curvature via pure pursuit: κ = 2·Δx / L² ---
			Vector3 toTarget = target - fb.Position;
			toTarget.y = 0f;
			float dist = toTarget.magnitude;
			float rawCurvature = 0f;
			float crossTrackDeb = 0f;
			float curvature = 0f;
			if (dist > 0.05f && lookAhead > 0.05f)
			{
				// Cross-track (signed lateral offset).
				float cross = Vector3.Cross(fb.Forward, toTarget.normalized).y;
				float crossTrack = cross * dist;
				crossTrackDeb = crossTrack;
				rawCurvature = 2f * crossTrack / (lookAhead * lookAhead);
				curvature = rawCurvature;

				// When very close to the target, small position errors cause huge
				// angular errors — clamp curvature to prevent spinning out.
				float closeness = 1f - Mathf.Clamp01(distanceToEnd / 6f);
				float maxCurv = Mathf.Lerp(0.35f, 0.12f, closeness);
				curvature = Mathf.Clamp(curvature, -maxCurv, maxCurv);

				float speedLimit = m_SteeringLimitCurve.Evaluate(Mathf.Abs(fb.SpeedKmh));
				float speedMaxCurv = maxCurv * speedLimit;
				curvature = Mathf.Clamp(curvature, -speedMaxCurv, speedMaxCurv);

			// Invert steering sense when reversing.
			if (isReversing)
				curvature = -curvature;
		}

		// --- desired speed from curvature ---
			float capKmh = Mathf.Max(1f, _topSpeedKmh) * Mathf.Clamp01(_speedCapFraction);

			// Preview: look ahead for tight turns and brake early.
			float previewCurvature = EvaluatePreviewCurvature(waypoints, nearest, targetIndex, lookAhead);
			float maxCurvature = Mathf.Max(Mathf.Abs(curvature), previewCurvature);
			float curvatureFraction = m_CurvatureSpeedCurve.Evaluate(maxCurvature);

			// Arrival: scale by remaining distance.
			float arrivalScale = Mathf.Clamp01(distanceToEnd / 15f);

			float targetKmh = capKmh * Mathf.Min(curvatureFraction, arrivalScale);
			float speedBeforeReverse = targetKmh;

			// Gentle start ramp.
			float absSpeed = Mathf.Abs(fb.SpeedKmh);
			float launchRamp = Mathf.Clamp01(absSpeed / 5f + 0.15f);
			targetKmh = Mathf.Lerp(Mathf.Min(targetKmh, 8f), targetKmh, launchRamp);

			if (isReversing)
				targetKmh = -targetKmh;

			result.Command = new MotionCommand(targetKmh, curvature, isReversing);

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

		private static float ComputeLookAhead(float _speedKmh, float _base)
		{
			float speed = Mathf.Max(0f, _speedKmh);
			return Mathf.Clamp(_base + speed * 0.35f, 3f, 16f);
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
	}
}
