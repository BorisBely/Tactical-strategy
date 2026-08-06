using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class PrecisionArrivalController
	{
		public float ActivationDistance = 1.5f;
		public float CompletionDistance = 0.5f;
		public float CompletionSpeed = 1f;
		public float CompletionHeadingDeg = 5f;

		private const float MaxSpeedClose = 3f;
		private const float ComfortDecelMs2 = 1.2f;
		private const float MinCreepSpeedKmh = 0.25f;
		private const float c_ShuffleLegMeters = 0.6f;
		private const int c_MaxShuffleCycles = 6;

		private bool m_ShuffleReverse;
		private Vector3 m_ShuffleStartPos;
		private int m_ShuffleCycles;

		public bool IsActive { get; private set; }

		public void Activate() { IsActive = true; }
		public void Deactivate()
		{
			IsActive = false;
			m_ShuffleCycles = 0;
			m_ShuffleReverse = false;
		}

		public void BeginHeadingShuffle(Vector3 _startPos)
		{
			m_ShuffleStartPos = _startPos;
			m_ShuffleReverse = false;
			m_ShuffleCycles = 0;
		}

		public bool IsHeadingShuffleExhausted => m_ShuffleCycles > c_MaxShuffleCycles;

		public MotionCommand TickHeadingShuffle(
			Vector3 _pos,
			float _yaw,
			float _speedKmh,
			GoalPose _goal,
			VehicleParameters _p)
		{
			float headingErr = Mathf.DeltaAngle(_yaw, _goal.YawDegrees);
			if (Mathf.Abs(headingErr) <= _goal.HeadingToleranceDeg)
				return MotionCommand.Empty;

			float maxCurv = 1f / Mathf.Max(0.5f, _p.EffectiveTurnRadius);
			float wheelCurvature = Mathf.Clamp(Mathf.Sign(headingErr) * maxCurv * 0.25f, -maxCurv * 0.25f, maxCurv * 0.25f);
			float legTravel = BicycleKinematics.FlatDistance(_pos, m_ShuffleStartPos);

			if (legTravel >= c_ShuffleLegMeters)
			{
				m_ShuffleReverse = !m_ShuffleReverse;
				m_ShuffleStartPos = _pos;
				m_ShuffleCycles++;
			}

			float creep = Mathf.Clamp(Mathf.Abs(headingErr) * 0.08f + 0.8f, 0.8f, 2f);
			if (_speedKmh > c_ShuffleLegMeters * 3.6f)
				creep = 0f;

			return new MotionCommand(creep, wheelCurvature, m_ShuffleReverse);
		}

		public struct Output
		{
			public MotionCommand Command;
			public bool IsComplete;
			public float DistanceToGoal;
			public float HeadingError;
			public float LateralError;
		}

		public Output Tick(
			Vector3 _position,
			Vector3 _forward,
			float _yaw,
			float _speedKmh,
			Vector3 _goalPosition,
			float? _goalHeadingYaw,
			VehicleParameters _params,
			float _dt)
		{
			Output result = new Output();

			float wheelBase = _params.WheelBase;
			float maxSteerRad = _params.MaxSteeringAngleRad;

			Vector3 toGoal = _goalPosition - _position;
			toGoal.y = 0f;
			float dist = toGoal.magnitude;

			Vector3 toGoalDir = dist > 0.01f ? toGoal / dist : _forward;
			float signedAngleToGoal = Vector3.SignedAngle(_forward, toGoalDir, Vector3.up);
			bool shouldReverse = Mathf.Abs(signedAngleToGoal) > 100f &&
			                     _speedKmh < 2f &&
			                     dist > CompletionDistance * 0.8f &&
			                     _goalHeadingYaw.HasValue;

			if (shouldReverse)
			{
				float rearOffset = wheelBase * 0.5f;
				Vector3 rearAxle = _position - _forward * rearOffset;
				Vector3 toGoalRear = _goalPosition - rearAxle;
				toGoalRear.y = 0f;
				dist = toGoalRear.magnitude;
				toGoalDir = dist > 0.01f ? toGoalRear / dist : _forward;
				signedAngleToGoal = Vector3.SignedAngle(-_forward, toGoalDir, Vector3.up);
			}

			result.DistanceToGoal = dist;
			result.LateralError = Mathf.Abs(Mathf.Sin(signedAngleToGoal * Mathf.Deg2Rad)) * dist;
			result.HeadingError = signedAngleToGoal;

			bool headingOk = true;
			if (_goalHeadingYaw.HasValue)
			{
				float hErr = Mathf.Abs(Mathf.DeltaAngle(_yaw, _goalHeadingYaw.Value));
				result.HeadingError = hErr;
				headingOk = hErr < CompletionHeadingDeg;
			}

			if (dist < CompletionDistance && _speedKmh < CompletionSpeed && headingOk)
			{
				result.IsComplete = true;
				result.Command = MotionCommand.Empty;
				return result;
			}

			float steerFromLateral = Mathf.Clamp(signedAngleToGoal / 60f, -1f, 1f) * 0.7f;
			float steerFromHeading = 0f;
			if (_goalHeadingYaw.HasValue)
			{
				float headingErr = Mathf.DeltaAngle(_yaw, _goalHeadingYaw.Value);
				steerFromHeading = Mathf.Clamp(headingErr / 45f, -1f, 1f) * 0.5f;
				float headingWeight = 1f - Mathf.Clamp01(dist / ActivationDistance);
				steerFromLateral *= (1f - headingWeight);
				steerFromHeading *= headingWeight;
			}

			float steer = Mathf.Clamp(steerFromLateral + steerFromHeading, -1f, 1f);
			float curvature = Mathf.Tan(steer * maxSteerRad) / Mathf.Max(0.5f, wheelBase);

			// Continuous braking-distance speed profile: v = sqrt(2*a*d)
			float brakingDist = Mathf.Max(0f, dist - CompletionDistance);
			float comfortDecel = Mathf.Min(ComfortDecelMs2, _params.HardBrakeDecelMs2 * 0.35f);
			float speedFromBrakingMs = Mathf.Sqrt(2f * comfortDecel * brakingDist);
			float speedFromBrakingKmh = speedFromBrakingMs * 3.6f;

			float speedCap = Mathf.Min(MaxSpeedClose, speedFromBrakingKmh);

			float steerAbs = Mathf.Abs(steer);
			if (steerAbs > 0.3f)
				speedCap *= 1f - (steerAbs - 0.3f) * 0.6f;

			float targetSpeed = dist > CompletionDistance * 1.2f
				? Mathf.Max(MinCreepSpeedKmh, speedCap)
				: Mathf.Lerp(0f, MinCreepSpeedKmh, dist / (CompletionDistance * 1.2f));

			result.Command = new MotionCommand(
				shouldReverse ? -targetSpeed : targetSpeed,
				curvature,
				shouldReverse);
			return result;
		}
	}
}
