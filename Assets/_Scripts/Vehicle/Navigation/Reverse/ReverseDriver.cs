using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ReverseDriver
	{
		private ReversePath m_Path;
		private ReversePursuit m_Pursuit;
		private ReverseStateMachine m_State;
		private ReverseSteeringLimiter m_SteeringLimiter;
		private DriverRecovery m_Recovery;
		private TrajectoryPrediction m_Prediction;

		private DriverContext m_Ctx;
		private float m_SpeedFraction;
		private ReverseDecisionResult m_LastDecision;
		private float m_SteeringSaturatedTime;

		public bool IsActive => m_State != null && m_State.Current != ReverseState.Finished
			&& m_State.Current != ReverseState.Failed;
		public ReverseState CurrentState => m_State?.Current ?? ReverseState.Failed;
		public ReversePath Path => m_Path;

		public ReverseDriver()
		{
			m_State = new ReverseStateMachine();
			m_SteeringLimiter = new ReverseSteeringLimiter();
			m_Recovery = new DriverRecovery();
		}

		public void Configure(AnimationCurve _speedCurve, AnimationCurve _steerLimit, TrajectoryPrediction _prediction)
		{
			m_Pursuit = new ReversePursuit(_steerLimit);
			m_Prediction = _prediction;
			m_Recovery.BindPrediction(_prediction);
		}

		public ReverseDecisionResult TryStart(DriverContext _ctx, float _speedFraction)
		{
			m_Ctx = _ctx;
			m_SpeedFraction = _speedFraction;

			m_Path = ReversePathBuilder.Build(_ctx.Path, _ctx);
			m_State.Reset();
			m_Pursuit?.Reset();
			m_SteeringSaturatedTime = 0f;

			Debug.Log($"[RevDriver] TryStart path.valid={m_Path?.IsValid} points={m_Path?.Points?.Count} length={m_Path?.TotalLength:F1}m ctx={m_Ctx!=null}");
			return new ReverseDecisionResult(true, "started");
		}

		public VehicleCommand Tick(float _dt)
		{
			if (m_Ctx == null || m_Path == null || !m_Path.IsValid)
			{
				Debug.Log($"[RevDriver] TICK ABORT ctx={m_Ctx!=null} path={m_Path!=null} valid={m_Path?.IsValid}");
				return VehicleCommandIdle();
			}

			m_Path.Advance(m_Ctx.GetControlPoint(DriverIntent.Reverse));

			var state = m_State.Tick(_dt, m_Ctx, m_Path);

			if (state == ReverseState.Failed || state == ReverseState.Finished)
			{
				Debug.Log($"[RevDriver] TICK state={state} -> FinalCommand");
				return FinalCommand(state);
			}

			var (reason, action) = m_Recovery.Evaluate(m_Ctx, DriverIntent.Reverse, _dt, m_Path);
			if (reason != RecoveryReason.None && action != RecoveryAction.None)
			{
				bool suppress = false;

				if (reason == RecoveryReason.SteeringSaturated)
				{
					m_SteeringSaturatedTime += _dt;
					bool inActive = state == ReverseState.Align || state == ReverseState.Reverse;
					bool moving = m_Ctx.SpeedKmh > 0.5f;
					bool pathOk = m_Path.IsValid && !m_Path.IsComplete;
					bool shortDuration = m_SteeringSaturatedTime < 0.5f;
					suppress = inActive && moving && pathOk && shortDuration;
				}
				else
				{
					m_SteeringSaturatedTime = 0f;
				}

				if (!suppress)
				{
					Debug.Log($"[RevDriver] RECOVERY reason={reason} action={action} -> FAIL");
					m_State.ForceFail();
					return VehicleCommandIdle();
				}
			}
			else
			{
				m_SteeringSaturatedTime = 0f;
			}

			VehicleCommand cmd;
			if (state == ReverseState.Enter || state == ReverseState.Align)
				cmd = AlignCommand(_dt);
			else if (state == ReverseState.SlowDown || state == ReverseState.Stop)
				cmd = SlowStopCommand(_dt);
			else
				cmd = DrivingCommand(_dt);

			if (Time.frameCount % 30 == 0)
			{
				Vector3 dest = m_Path.IsValid && m_Path.Points.Count > 0
					? m_Path.Points[m_Path.Points.Count - 1].Position
					: m_Ctx.Position;
				Vector3 rearAxle = m_Ctx.GetControlPoint(DriverIntent.Reverse);
				Vector3 toDest = dest - rearAxle;
				toDest.y = 0f;
				float latErr = toDest.magnitude > 0.01f
					? Vector3.Cross((-m_Ctx.Forward).normalized, toDest.normalized).y * toDest.magnitude
					: 0f;
				Debug.Log($"[RevDriver] state={state} seg={m_Path.CurrentSegment}/{m_Path.Points.Count} remaining={m_Path.RemainingDistance:F1}m " +
					$"pos=({m_Ctx.Position.x:F2},{m_Ctx.Position.z:F2}) fwd=({m_Ctx.Forward.x:F2},{m_Ctx.Forward.z:F2}) " +
					$"rearAxle=({rearAxle.x:F2},{rearAxle.z:F2}) dest=({dest.x:F2},{dest.z:F2}) " +
					$"latErr={latErr:F2}m thr={cmd.Throttle:F2} steer={cmd.Steer:F2} brk={cmd.BrakeMode} speed={m_Ctx.SpeedKmh:F1}km/h " +
					$"steerSatDur={m_SteeringSaturatedTime:F2}s");
			}

			return cmd;
		}

		private VehicleCommand DrivingCommand(float _dt)
		{
			if (m_Pursuit == null)
				return VehicleCommandIdle();

			var output = m_Pursuit.Tick(m_Ctx, m_Path, m_SpeedFraction);

			m_Ctx.RemainingDistance = output.DistanceToEnd;
			m_Ctx.CurrentPathSegment = m_Path.CurrentSegment;

			float rawSteerRad = Mathf.Atan(m_Ctx.WheelBase * output.DesiredCurvature);
			float steerTarget = Mathf.Clamp(rawSteerRad / m_Ctx.MaxSteeringAngleRad, -1f, 1f);
			float steerRate = m_Ctx.SteeringRateDegPerSec / 90f * _dt;

			m_Ctx.CurrentSteerAngle = Mathf.MoveTowards(m_Ctx.CurrentSteerAngle,
				steerTarget * m_Ctx.MaxSteeringAngleDeg, steerRate * m_Ctx.MaxSteeringAngleDeg);

			float clampedSteer = m_SteeringLimiter.ClampSteer(
				m_Ctx.CurrentSteerAngle / m_Ctx.MaxSteeringAngleDeg, m_Ctx.SpeedKmh);

			// Speed: distance-based + speedFraction * maxReverseSpeed
			float desiredSpeed = m_SpeedFraction * m_Ctx.MaxReverseSpeedKmh;
			float distToEnd = output.DistanceToEnd;

			// Final approach speed cap (mirrors ApplyFinalSpeedCap from plan)
			if (distToEnd < 2.0f) desiredSpeed = Mathf.Min(desiredSpeed, 3f);
			if (distToEnd < 0.8f) desiredSpeed = Mathf.Min(desiredSpeed, 1.5f);
			if (distToEnd < 0.3f) desiredSpeed = Mathf.Min(desiredSpeed, 0.5f);

			float absCurv = Mathf.Abs(output.DesiredCurvature);
			if (absCurv > 0.15f && distToEnd < 2f)
				desiredSpeed *= Mathf.Clamp01(1f - (absCurv - 0.15f) * 3f);

			float currentMag = m_Ctx.SpeedKmh;
			float speedError = desiredSpeed - currentMag;

			float throttle;
			VehicleBrakeMode brake;

			if (speedError > 0.5f)
			{
				throttle = Mathf.Clamp(speedError * 0.04f, 0.15f, 1f);
				brake = VehicleBrakeMode.None;
			}
			else if (speedError < -0.5f)
			{
				throttle = 0f;
				brake = Mathf.Clamp01(-speedError / 8f) > 0.6f ? VehicleBrakeMode.Hard : VehicleBrakeMode.Soft;
			}
			else
			{
				throttle = Mathf.Clamp01(speedError * 0.02f + 0.02f);
				brake = Mathf.Abs(speedError) < 0.3f ? VehicleBrakeMode.None : VehicleBrakeMode.Soft;
			}

			// Kinematic chain log every 30 frames
			if (Time.frameCount % 30 == 0)
			{
				float steerTargetDeg = steerTarget * m_Ctx.MaxSteeringAngleDeg;
				float steerLimitedDeg = clampedSteer * m_Ctx.MaxSteeringAngleDeg;
				Debug.Log($"[RevChain] desiredCurv={output.DesiredCurvature:F4} -> steerTarget={steerTargetDeg:F1}deg -> steerLimited={steerLimitedDeg:F1}deg -> actualSteer={m_Ctx.CurrentSteerAngle:F1}deg   desiredSpeed={desiredSpeed:F1}km/h");
			}

			return new VehicleCommand
			{
				Steer = Mathf.Clamp(clampedSteer, -1f, 1f),
				Throttle = Mathf.Clamp(-throttle, -1f, 1f),
				BrakeMode = brake,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false
			};
		}

		private VehicleCommand AlignCommand(float _dt)
		{
			float steerTarget = 0f;
			if (m_Path != null && m_Path.IsValid && m_Path.Points.Count > 0)
			{
				Vector3 toFirst = m_Path.Points[0].Position - m_Ctx.Position;
				toFirst.y = 0f;
				float angle = Vector3.SignedAngle(m_Ctx.Forward, toFirst.normalized, Vector3.up);
				steerTarget = Mathf.Clamp(angle / 90f, -0.5f, 0.5f);
			}

			return new VehicleCommand
			{
				Steer = Mathf.Clamp(steerTarget, -1f, 1f),
				Throttle = 0f,
				BrakeMode = VehicleBrakeMode.Soft,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false
			};
		}

		private VehicleCommand SlowStopCommand(float _dt)
		{
			if (m_Ctx.SpeedKmh < 0.1f)
			{
				return new VehicleCommand
				{
					Steer = 0f,
					Throttle = 0f,
					BrakeMode = VehicleBrakeMode.Hard,
					FireHeld = false,
					AimWorldPoint = Vector3.zero,
					HasAimPoint = false
				};
			}
			return DrivingCommand(_dt);
		}

		private VehicleCommand FinalCommand(ReverseState _state)
		{
			return new VehicleCommand
			{
				Steer = 0f,
				Throttle = 0f,
				BrakeMode = _state == ReverseState.Finished ? VehicleBrakeMode.Hard : VehicleBrakeMode.Soft,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false
			};
		}

		private static VehicleCommand VehicleCommandIdle()
		{
			return new VehicleCommand
			{
				Steer = 0f,
				Throttle = 0f,
				BrakeMode = VehicleBrakeMode.None,
				FireHeld = false,
				AimWorldPoint = Vector3.zero,
				HasAimPoint = false
			};
		}
	}
}
