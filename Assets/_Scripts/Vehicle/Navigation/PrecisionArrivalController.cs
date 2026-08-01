using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class PrecisionArrivalController
	{
		public float ActivationDistance = 6f;
		public float CompletionDistance = 0.3f;
		public float CompletionSpeed = 1f;

		private const float MaxSpeedClose = 3f;
		private const float MaxSpeedVeryClose = 1.5f;
		private const float MaxSpeedFinal = 0.8f;

		private const float CloseThreshold = 1.5f;
		private const float VeryCloseThreshold = 0.6f;
		private const float FinalThreshold = 0.3f;

		public bool IsActive { get; private set; }

		public void Activate() { IsActive = true; }
		public void Deactivate() { IsActive = false; }

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
			bool shouldReverse = Mathf.Abs(signedAngleToGoal) > 100f && _speedKmh < 2f && dist > 2f;

			// Reverse: recompute errors from REAR AXLE (same formula as DriverContext.RearAxlePosition)
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

			// Completion check
			bool headingOk = true;
			if (_goalHeadingYaw.HasValue)
			{
				float hErr = Mathf.Abs(Mathf.DeltaAngle(_yaw, _goalHeadingYaw.Value));
				result.HeadingError = hErr;
				headingOk = hErr < 4f;
			}

			if (dist < CompletionDistance && _speedKmh < CompletionSpeed && headingOk)
			{
				result.IsComplete = true;
				result.Command = MotionCommand.Empty;
				return result;
			}

			// --- Unified steer calculation (forward AND reverse) ---
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

			// --- Speed ---
			float speedCap;
			if (dist < FinalThreshold)
				speedCap = MaxSpeedFinal;
			else if (dist < VeryCloseThreshold)
				speedCap = MaxSpeedVeryClose;
			else if (dist < CloseThreshold)
				speedCap = MaxSpeedClose;
			else
				speedCap = Mathf.Lerp(MaxSpeedClose, 12f, (dist - CloseThreshold) / (ActivationDistance - CloseThreshold));

			float steerAbs = Mathf.Abs(steer);
			if (steerAbs > 0.3f)
				speedCap *= 1f - (steerAbs - 0.3f) * 0.6f;

			float targetSpeed = Mathf.Max(0.5f, speedCap);

			if (_speedKmh > targetSpeed * 1.5f)
				targetSpeed = 0f;

			result.Command = new MotionCommand(shouldReverse ? -targetSpeed : targetSpeed, curvature, shouldReverse);
			return result;
		}
	}
}
