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
		private float m_SpeedIntegral;
		private float m_BreakawayTimer;
		private float m_PreSteerTimer;

		private const float AccelThresholdKmh = 0.3f;
		private const float CoastThresholdKmh = -0.15f;
		private const float SoftBrakeThresholdKmh = -1.2f;
		/// <summary>GearChange/Goal: Soft only when still hot after Coast drag — Soft=1400Nm jolts.</summary>
		private const float ManeuverSoftBrakeThresholdKmh = 7.5f;
		private const float OppositeGearSoftThresholdKmh = 4f;
		private const float EmergencyOverspeedKmh = 12f;
		private const float SpeedKp = 0.06f;
		private const float SpeedKi = 0.015f;
		private const float SpeedDeadbandKmh = 0.25f;
		private const float c_BreakawayThrottle = 0.20f;
		private const float c_BreakawayHoldSec = 0.35f;
		private const float CurvatureDeadband = 0.003f;
		private const float SteerDeadbandNormalized = 0.02f;
		private const float c_PreSteerTargetMin = 0.15f;
		private const float c_PreSteerErrorMax = 0.20f;
		private const float c_PreSteerCatchFraction = 0.8f;
		private const float c_PreSteerTimeoutSec = 0.6f;
		private const float c_PreSteerMaxSpeedKmh = 1.2f;
		private const float c_PreSteerCreepThrottle = 0.05f;

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

			float rawSteerRad = Mathf.Abs(_cmd.DesiredCurvature) < CurvatureDeadband
				? 0f
				: Mathf.Atan(p.WheelBase * _cmd.DesiredCurvature);
			float steerTarget = Mathf.Clamp(rawSteerRad / p.MaxSteeringAngleRad, -1f, 1f);
			if (Mathf.Abs(steerTarget) < SteerDeadbandNormalized)
				steerTarget = 0f;

			// Apply nav steer target immediately; WheeledMotor owns the only rate-limit.
			m_SmoothedSteer = steerTarget;

			float currentAlongCmd = _cmd.Reverse
				? -m_Ctx.State.SpeedSignedKmh
				: m_Ctx.State.SpeedSignedKmh;
			float speedError = _cmd.DesiredSpeedKmh - currentAlongCmd;
			DrivingPhase phase = ResolvePhase(_ctx);

			float throttle;
			VehicleBrakeMode brakeMode = ResolveBrakeMode(speedError, _cmd.StopIntent);

			// Moving opposite to commanded gear — Coast first; Soft only if still rolling hard.
			// Soft at every cusp gear flip was the main chassis jolt (peakDecel ~5–7 m/s²).
			if (_cmd.DesiredSpeedKmh > 0.05f && currentAlongCmd < -0.5f)
			{
				throttle = 0f;
				m_SpeedIntegral = 0f;
				float oppositeSpeed = Mathf.Abs(currentAlongCmd);
				brakeMode = oppositeSpeed >= OppositeGearSoftThresholdKmh
					? VehicleBrakeMode.Soft
					: VehicleBrakeMode.Coast;
				m_SmoothedThrottle = Mathf.MoveTowards(m_SmoothedThrottle, 0f, 0.15f);

				return new VehicleCommand
				{
					Steer = Mathf.Clamp(m_SmoothedSteer, -1f, 1f),
					Throttle = m_SmoothedThrottle,
					BrakeMode = brakeMode,
					FireHeld = false,
					AimWorldPoint = Vector3.zero,
					HasAimPoint = false,
					Phase = phase
				};
			}

			// Hold / creep until physical wheels catch commanded steer (plan assumes instant curvature).
			if (_cmd.DesiredSpeedKmh > 0.05f && ShouldHoldForPreSteer(steerTarget, currentAlongCmd))
			{
				m_PreSteerTimer += Time.fixedDeltaTime;
				m_BreakawayTimer = 0f;
				m_SpeedIntegral = 0f;
				throttle = c_PreSteerCreepThrottle;
				brakeMode = VehicleBrakeMode.None;
				float signedCreep = _cmd.Reverse ? -throttle : throttle;
				m_SmoothedThrottle = Mathf.MoveTowards(m_SmoothedThrottle, signedCreep, 0.15f);
				return new VehicleCommand
				{
					Steer = Mathf.Clamp(m_SmoothedSteer, -1f, 1f),
					Throttle = m_SmoothedThrottle,
					BrakeMode = brakeMode,
					FireHeld = false,
					AimWorldPoint = Vector3.zero,
					HasAimPoint = false,
					Phase = phase
				};
			}

			m_PreSteerTimer = 0f;

			if (_cmd.DesiredSpeedKmh <= 0.05f)
			{
				throttle = 0f;
				m_SpeedIntegral = 0f;
				m_BreakawayTimer = 0f;
			}
			else if (Mathf.Abs(currentAlongCmd) < 0.2f)
			{
				m_BreakawayTimer += Time.fixedDeltaTime;
				if (m_BreakawayTimer < c_BreakawayHoldSec)
				{
					throttle = c_BreakawayThrottle;
					m_SpeedIntegral = 0f;
					brakeMode = VehicleBrakeMode.None;
					m_SmoothedThrottle = Mathf.MoveTowards(m_SmoothedThrottle, _cmd.Reverse ? -throttle : throttle, 0.15f);
					return new VehicleCommand
					{
						Steer = Mathf.Clamp(m_SmoothedSteer, -1f, 1f),
						Throttle = m_SmoothedThrottle,
						BrakeMode = brakeMode,
						FireHeld = false,
						AimWorldPoint = Vector3.zero,
						HasAimPoint = false,
						Phase = phase
					};
				}

				if (Mathf.Abs(speedError) <= SpeedDeadbandKmh && _cmd.DesiredSpeedKmh < 1f)
				{
					throttle = 0f;
					m_SpeedIntegral = 0f;
				}
				else if (speedError > 0f)
				{
					m_SpeedIntegral = Mathf.Clamp(
						m_SpeedIntegral + speedError * Time.fixedDeltaTime,
						0f,
						0.5f);
					throttle = Mathf.Clamp(
						SpeedKp * speedError + SpeedKi * m_SpeedIntegral,
						0.08f,
						1f);
					brakeMode = VehicleBrakeMode.None;
				}
				else
				{
					m_SpeedIntegral = Mathf.Max(0f, m_SpeedIntegral + speedError * Time.fixedDeltaTime);
					throttle = 0f;
				}
			}
			else
			{
				m_BreakawayTimer = 0f;
				if (Mathf.Abs(speedError) <= SpeedDeadbandKmh && _cmd.DesiredSpeedKmh < 1f)
				{
					throttle = 0f;
					m_SpeedIntegral = 0f;
				}
				else if (speedError > 0f)
				{
					m_SpeedIntegral = Mathf.Clamp(
						m_SpeedIntegral + speedError * Time.fixedDeltaTime,
						0f,
						0.5f);
					throttle = Mathf.Clamp(
						SpeedKp * speedError + SpeedKi * m_SpeedIntegral,
						0.08f,
						1f);
					brakeMode = VehicleBrakeMode.None;
				}
				else
				{
					m_SpeedIntegral = Mathf.Max(0f, m_SpeedIntegral + speedError * Time.fixedDeltaTime);
					throttle = 0f;
				}
			}

			if (_cmd.Reverse)
				throttle = -throttle;

			float throttleRate = 0.15f;
			m_SmoothedThrottle = Mathf.MoveTowards(m_SmoothedThrottle, throttle, throttleRate);
			throttle = m_SmoothedThrottle;

			float absSpeed = m_Ctx.State.SpeedKmh;
			if (absSpeed > 40f)
			{
				float dampFactor = Mathf.Clamp01((absSpeed - 40f) / 30f);
				float exportDamp = 1f - dampFactor * 0.6f;
				m_SmoothedSteer *= Mathf.Lerp(1f, exportDamp, 0.1f);
			}

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

		public VehicleCommand BrakeToStop(bool _hard, StopIntent _intent = StopIntent.Goal)
		{
			m_SmoothedSteer = Mathf.MoveTowards(m_SmoothedSteer, 0f, NavSteerRateNormPerSec() * Time.fixedDeltaTime);
			m_SmoothedThrottle = 0f;
			m_SpeedIntegral = 0f;

			StopIntent intent = _hard
				? (_intent == StopIntent.PlayerEmergency ? StopIntent.PlayerEmergency : StopIntent.SafetyEmergency)
				: _intent;

			VehicleBrakeMode brake;
			if (IsEmergencyStopIntent(intent))
			{
				brake = VehicleBrakeMode.Hard;
			}
			else
			{
				float speed = Mathf.Abs(m_Ctx != null ? m_Ctx.State.SpeedSignedKmh : 0f);
				if (_intent == StopIntent.GearChange)
					brake = VehicleBrakeMode.Coast;
				else
					brake = speed >= ManeuverSoftBrakeThresholdKmh
						? VehicleBrakeMode.Soft
						: VehicleBrakeMode.Coast;
			}

			return new VehicleCommand
			{
				Steer = m_SmoothedSteer,
				Throttle = 0f,
				BrakeMode = brake,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false,
				Phase = m_Ctx?.CurrentManeuver != null
					? ResolvePhase(m_Ctx) : DrivingPhase.Cruise
			};
		}

		public VehicleCommand HoldInPlace()
		{
			m_SmoothedThrottle = 0f;
			m_SmoothedSteer = Mathf.MoveTowards(m_SmoothedSteer, 0f, NavSteerRateNormPerSec() * Time.fixedDeltaTime);
			float speed = Mathf.Abs(m_Ctx != null ? m_Ctx.State.SpeedSignedKmh : 0f);

			return new VehicleCommand
			{
				Steer = m_SmoothedSteer,
				Throttle = 0f,
				BrakeMode = speed >= OppositeGearSoftThresholdKmh
					? VehicleBrakeMode.Soft
					: VehicleBrakeMode.Coast,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false,
				HoldPosition = true,
				Phase = DrivingPhase.Parking
			};
		}

		public VehicleCommand Park()
		{
			m_SmoothedSteer = Mathf.MoveTowards(m_SmoothedSteer, 0f, NavSteerRateNormPerSec() * Time.fixedDeltaTime);

			var cmd = HoldInPlace();
			cmd.Steer = m_SmoothedSteer;
			return cmd;
		}

		public VehicleCommand Idle()
		{
			return VehicleCommand.Idle;
		}

		/// <summary>
		/// Clears nav-side steer smoothing only. Does not zero WheeledMotor wheels —
		/// physical angle must keep catching the next commanded curvature.
		/// </summary>
		public void ResetSteering(bool _immediate = true)
		{
			if (_immediate)
				m_SmoothedSteer = 0f;
			m_PreSteerTimer = 0f;
			m_BreakawayTimer = 0f;
		}

		/// <summary>
		/// Sync nav steer command to the current physical wheel angle so the next
		/// Convert() does not briefly command zero before the tracker updates.
		/// </summary>
		public void SyncSteeringFromMotor()
		{
			if (m_WheeledMotor != null)
				m_SmoothedSteer = m_WheeledMotor.CurrentSteerNormalized;
			m_PreSteerTimer = 0f;
			m_BreakawayTimer = 0f;
		}

		public void ResetDriveState()
		{
			m_SmoothedSteer = 0f;
			m_SmoothedThrottle = 0f;
			m_SpeedIntegral = 0f;
			m_PreSteerTimer = 0f;
			m_BreakawayTimer = 0f;
		}

		private bool ShouldHoldForPreSteer(float _steerTarget, float _currentAlongCmdKmh)
		{
			if (m_WheeledMotor == null)
				return false;
			if (Mathf.Abs(_steerTarget) < c_PreSteerTargetMin)
				return false;
			if (Mathf.Abs(_currentAlongCmdKmh) > c_PreSteerMaxSpeedKmh)
				return false;
			if (m_PreSteerTimer >= c_PreSteerTimeoutSec)
				return false;

			float actual = m_WheeledMotor.CurrentSteerNormalized;
			float err = Mathf.Abs(actual - _steerTarget);
			if (err <= c_PreSteerErrorMax)
				return false;

			// Caught up enough relative to commanded lock.
			if (Mathf.Abs(_steerTarget) > 0.01f &&
			    Mathf.Abs(actual) >= Mathf.Abs(_steerTarget) * c_PreSteerCatchFraction &&
			    Mathf.Sign(actual) == Mathf.Sign(_steerTarget))
				return false;

			return true;
		}

		private static VehicleBrakeMode ResolveBrakeMode(float _speedErrorKmh, StopIntent _stopIntent)
		{
			if (_speedErrorKmh >= 0f)
				return VehicleBrakeMode.None;

			float overspeed = -_speedErrorKmh;
			if (IsEmergencyStopIntent(_stopIntent) && overspeed >= EmergencyOverspeedKmh)
				return VehicleBrakeMode.Hard;

			bool maneuverStop = _stopIntent == StopIntent.GearChange || _stopIntent == StopIntent.Goal;
			float softThreshold = maneuverStop
				? ManeuverSoftBrakeThresholdKmh
				: -SoftBrakeThresholdKmh;

			// Maneuver / cusp / goal: Coast drag only until clearly too fast for CoastDecel.
			if (maneuverStop)
			{
				if (_stopIntent == StopIntent.GearChange)
				{
					// Never Soft on gear cusp — SoftBrake + reverse→forward is the visible jolt.
					return VehicleBrakeMode.Coast;
				}

				if (overspeed >= softThreshold)
					return VehicleBrakeMode.Soft;
				return VehicleBrakeMode.Coast;
			}

			if (overspeed >= -SoftBrakeThresholdKmh)
				return VehicleBrakeMode.Soft;

			if (_speedErrorKmh >= CoastThresholdKmh)
				return VehicleBrakeMode.Coast;

			return VehicleBrakeMode.Coast;
		}

		private static bool IsEmergencyStopIntent(StopIntent _intent)
		{
			return _intent == StopIntent.SafetyEmergency || _intent == StopIntent.PlayerEmergency;
		}

		private static DrivingPhase ResolvePhase(NavigationContext _ctx)
		{
			if (_ctx == null || _ctx.CurrentManeuver == null)
				return DrivingPhase.Cruise;

			if (_ctx.CurrentManeuver is TrajectoryFollowingManeuver)
			{
				if (_ctx.RemainingDistance < 3f)
					return DrivingPhase.Parking;
				if (_ctx.RemainingDistance < 8f)
					return DrivingPhase.Precision;
				return DrivingPhase.Cruise;
			}

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

			float capKmh = Mathf.Max(0.5f, Mathf.Abs(_desiredSpeedKmh));
			m_WheeledMotor.SetSpeedCapKmh(capKmh);
		}

		private float NavSteerRateNormPerSec()
		{
			float degPerSec = m_Ctx != null ? m_Ctx.Params.SteeringRateDegPerSec : 120f;
			float maxSteerDeg = m_Ctx != null ? Mathf.Max(1f, m_Ctx.Params.MaxSteeringAngleDeg) : 28f;
			return degPerSec / maxSteerDeg;
		}
	}
}
