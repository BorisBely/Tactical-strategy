using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class MotionController
	{
		private readonly WheeledMotor m_WheeledMotor;
		private readonly VehicleBrain m_Brain;
		private NavigationContext m_Ctx;

		private float m_SmoothedSteer;
		private float m_SmoothedTargetMs = -1f;
		private float m_LastThrottle;
		private float m_LastLoggedThr;
		private float m_LastLoggedTarget;

		private const float CoastThreshold = -2f;
		private const float SoftBrakeThreshold = -8f;
		private const float HardBrakeThreshold = -20f;

		public MotionController(
			WheeledMotor _wheeledMotor,
			VehicleBrain _brain)
		{
			m_WheeledMotor = _wheeledMotor;
			m_Brain = _brain;
		}

		public VehicleCommand Convert(NavigationContext _ctx, MotionCommand _cmd)
		{
			m_Ctx = _ctx;
			VehicleParameters p = _ctx.Params;
			ApplySpeedCap(_cmd.DesiredSpeedKmh, p.MaxForwardSpeedKmh);

			float rawSteerRad = Mathf.Atan(p.WheelBase * _cmd.DesiredCurvature);
			float steerTarget = Mathf.Clamp(rawSteerRad / p.MaxSteeringAngleRad, -1f, 1f);
			float steerRate = p.SteeringRateDegPerSec / 90f * Time.fixedDeltaTime;
			m_SmoothedSteer = Mathf.MoveTowards(m_SmoothedSteer, steerTarget, steerRate);

			float currentSpeedMs = m_Ctx.State.SpeedKmh / 3.6f;
			float desiredMs = _cmd.DesiredSpeedKmh / 3.6f;
			float signedCurrent = _cmd.Reverse ? -currentSpeedMs : currentSpeedMs;

			// Smooth target speed to avoid jerk from planner steps
			if (m_SmoothedTargetMs < 0f) m_SmoothedTargetMs = desiredMs;
			m_SmoothedTargetMs = Mathf.MoveTowards(m_SmoothedTargetMs, desiredMs, 2f * Time.fixedDeltaTime);

			float speedError = m_SmoothedTargetMs - signedCurrent;

			DrivePhysics.DesiredSpeedMs = m_SmoothedTargetMs;

			const float maxAccel = 3.0f;
			const float pGain = 1.5f;
			const float deadZoneMs = 0.15f;     // ~0.54 km/h — below this, hold throttle
			const float hysteresisMs = 0.10f;   // must exceed deadZone by this to change

			float throttle = m_LastThrottle;

			if (speedError > deadZoneMs + hysteresisMs)
			{
				float targetAccel = Mathf.Clamp(speedError * pGain, 0f, maxAccel);
				throttle = targetAccel / maxAccel;
			}
			else if (speedError < -deadZoneMs)
			{
				throttle = 0f;
			}
			// else: within [−deadZone, deadZone+hysteresis] — hold m_LastThrottle

			m_LastThrottle = throttle;

			float kmh = m_Ctx.State.SpeedKmh;
			if (Mathf.Abs(throttle - m_LastLoggedThr) > 0.03f || Mathf.Abs(_cmd.DesiredSpeedKmh - m_LastLoggedTarget) > 1f)
			{
				Debug.Log($"[SpeedCtrl] tgt={_cmd.DesiredSpeedKmh:F0} cur={kmh:F0} err={speedError:F1} thr={throttle:F2}");
				m_LastLoggedThr = throttle;
				m_LastLoggedTarget = _cmd.DesiredSpeedKmh;
			}

			if (_cmd.Reverse) throttle = -throttle;

			return new VehicleCommand
			{
				Steer = Mathf.Clamp(m_SmoothedSteer, -1f, 1f),
				Throttle = Mathf.Clamp(throttle, -1f, 1f),
				BrakeMode = VehicleBrakeMode.None,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false
			};
		}

		public VehicleCommand BrakeToStop(bool _hard)
		{
			float steerRate = 120f / 90f * Time.fixedDeltaTime;
			if (m_Ctx != null)
				steerRate = m_Ctx.Params.SteeringRateDegPerSec / 90f * Time.fixedDeltaTime;
			m_SmoothedSteer = Mathf.MoveTowards(m_SmoothedSteer, 0f, steerRate);
			m_LastThrottle = 0f;
			m_SmoothedTargetMs = -1f;

			return new VehicleCommand
			{
				Steer = m_SmoothedSteer,
				Throttle = 0f,
				BrakeMode = _hard ? VehicleBrakeMode.Hard : VehicleBrakeMode.Soft,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false
			};
		}

		public VehicleCommand Idle()
		{
			return VehicleCommand.Idle;
		}

		private void ApplySpeedCap(float _desiredSpeedKmh, float _maxSpeedKmh)
		{
			if (m_WheeledMotor == null)
				return;

			float capKmh = Mathf.Max(1f, Mathf.Abs(_desiredSpeedKmh));
			capKmh = Mathf.Max(capKmh, _maxSpeedKmh * 0.3f);
			m_WheeledMotor.SetSpeedCapKmh(capKmh);
		}
	}
}
