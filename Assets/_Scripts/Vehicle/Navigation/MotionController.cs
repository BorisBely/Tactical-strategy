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
		private float m_SmoothedThrottle;

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

			float currentSpeed = m_Ctx.State.SpeedKmh;
			float signedCurrent = _cmd.Reverse ? -currentSpeed : currentSpeed;
			float speedError = _cmd.DesiredSpeedKmh - signedCurrent;

			float throttle;
			VehicleBrakeMode brakeMode;

			if (speedError > 0.3f)
			{
				throttle = Mathf.Clamp(speedError * 0.04f, 0.15f, 1f);
				brakeMode = VehicleBrakeMode.None;
			}
			else if (speedError < HardBrakeThreshold)
			{
				throttle = 0f;
				brakeMode = VehicleBrakeMode.Hard;
			}
			else if (speedError < SoftBrakeThreshold)
			{
				throttle = 0f;
				brakeMode = VehicleBrakeMode.Soft;
			}
			else if (speedError < CoastThreshold)
			{
				throttle = 0f;
				brakeMode = VehicleBrakeMode.Coast;
			}
			else
			{
				throttle = Mathf.Clamp01(speedError * 0.02f + 0.02f);
				brakeMode = Mathf.Abs(speedError) < 0.15f ? VehicleBrakeMode.Coast : VehicleBrakeMode.Coast;
			}

			if (_cmd.Reverse)
				throttle = -throttle;

			// DriverComfort: smooth throttle changes (prevents 100% → -100% in one frame)
			float throttleRate = 0.15f;
			m_SmoothedThrottle = Mathf.MoveTowards(m_SmoothedThrottle, throttle, throttleRate);
			throttle = m_SmoothedThrottle;

			// SteeringDamping: exponential slow-down at high speed
			float absSpeed = Mathf.Abs(currentSpeed);
			if (absSpeed > 40f)
			{
				float dampFactor = Mathf.Clamp01((absSpeed - 40f) / 30f);
				float exportDamp = 1f - dampFactor * 0.6f;
				m_SmoothedSteer *= Mathf.Lerp(1f, exportDamp, 0.1f);
			}

			DrivingPhase phase = ResolvePhase(_ctx);

			return new VehicleCommand
			{
				Steer = Mathf.Clamp(m_SmoothedSteer, -1f, 1f),
				Throttle = Mathf.Clamp(throttle, -1f, 1f),
				BrakeMode = brakeMode,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false,
				Phase = phase
			};
		}

		public VehicleCommand BrakeToStop(bool _hard)
		{
			float steerRate = 120f / 90f * Time.fixedDeltaTime;
			if (m_Ctx != null)
				steerRate = m_Ctx.Params.SteeringRateDegPerSec / 90f * Time.fixedDeltaTime;
			m_SmoothedSteer = Mathf.MoveTowards(m_SmoothedSteer, 0f, steerRate);

			return new VehicleCommand
			{
				Steer = m_SmoothedSteer,
				Throttle = 0f,
				BrakeMode = _hard ? VehicleBrakeMode.Hard : VehicleBrakeMode.Soft,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false,
				Phase = m_Ctx?.CurrentManeuver != null
					? ResolvePhase(m_Ctx) : DrivingPhase.Cruise
			};
		}

		public VehicleCommand HoldInPlace()
		{
			return new VehicleCommand
			{
				Steer = m_SmoothedSteer,
				Throttle = 0f,
				BrakeMode = VehicleBrakeMode.Soft,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false,
				HoldPosition = true,
				Phase = DrivingPhase.Parking
			};
		}

		public VehicleCommand Park()
		{
			float steerRate = 120f / 90f * Time.fixedDeltaTime;
			if (m_Ctx != null)
				steerRate = m_Ctx.Params.SteeringRateDegPerSec / 90f * Time.fixedDeltaTime;
			m_SmoothedSteer = Mathf.MoveTowards(m_SmoothedSteer, 0f, steerRate);

			var cmd = HoldInPlace();
			cmd.Steer = m_SmoothedSteer;
			return cmd;
		}

		public VehicleCommand Idle()
		{
			return VehicleCommand.Idle;
		}

		private static DrivingPhase ResolvePhase(NavigationContext _ctx)
		{
			if (_ctx == null || _ctx.CurrentManeuver == null)
				return DrivingPhase.Cruise;

			// Distance-based precision: close to target → parking phase
			if (_ctx.RemainingDistance < 3f &&
			    (_ctx.CurrentManeuver.IsArrivalManeuver ||
			     _ctx.CurrentManeuver.Type == VehicleManeuverType.Parking ||
			     _ctx.CurrentManeuver.Type == VehicleManeuverType.ApproachWithHeading))
				return DrivingPhase.Parking;

			switch (_ctx.CurrentManeuver.Type)
			{
				case VehicleManeuverType.Parking:
				case VehicleManeuverType.ApproachWithHeading:
				case VehicleManeuverType.PostTurnAlignment:
					return DrivingPhase.Parking;

				case VehicleManeuverType.Unstuck:
					return DrivingPhase.Recovery;

				case VehicleManeuverType.Reverse:
				case VehicleManeuverType.TurnAround:
				case VehicleManeuverType.ThreePointTurn:
					return DrivingPhase.Precision;

				default:
					return DrivingPhase.Cruise;
			}
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
