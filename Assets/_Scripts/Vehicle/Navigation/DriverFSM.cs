using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Single source of truth passed to every navigation subsystem each frame.
	/// </summary>
	public sealed class NavigationContext
	{
		public VehicleParameters Params { get; }
		public FeedbackState State { get; set; }
		public NavigationRequest Request { get; set; }
		public PathResult Path { get; set; }
		public DrivingPlan Plan { get; set; }
		public int CurrentManeuverIndex { get; set; }
		public float RemainingDistance { get; set; }
		public float CurrentCurvature { get; set; }
		public float DesiredSpeedKmh { get; set; }
		public float TargetSpeedKmh { get; set; }
		public StopReason ActiveStopReason { get; set; }
		public SpeedLimitResult ActiveLimit { get; set; }
		public VehicleDriverMemory Memory { get; }

		public bool HasRequest => Request.Destination != Vector3.zero;
		public bool HasPath => Path.IsValid;
		public bool HasPlan => Plan.IsValid;
		public float TopSpeedKmh => Params.MaxForwardSpeedKmh;
		public Maneuver CurrentManeuver
		{
			get
			{
				if (Plan.Maneuvers == null ||
				    CurrentManeuverIndex < 0 ||
				    CurrentManeuverIndex >= Plan.Maneuvers.Count)
					return null;
				return Plan.Maneuvers[CurrentManeuverIndex];
			}
		}

		public NavigationContext(VehicleParameters _params, VehicleDriverMemory _memory)
		{
			Params = _params;
			Memory = _memory ?? new VehicleDriverMemory();
			Path = PathResult.Invalid;
			Plan = DrivingPlan.Empty;
			CurrentManeuverIndex = 0;
		}
	}

	public sealed class DriverFSM
	{
		public static bool DebugLog = true;
		public enum State
		{
			Idle,
			Driving,
			Arrival,
			Recovery,
			Holding,
			EmergencyStop
		}

		public State CurrentState { get; private set; } = State.Idle;

		private readonly NavigationContext m_Ctx;
		private readonly PathPlanner m_PathPlanner;
		private readonly DrivingPlanner m_DrivingPlanner;
		private readonly ManeuverPlanner m_ManeuverPlanner;
		private readonly PursuitController m_Pursuit;
		private readonly MotionController m_Motion;
		private readonly ArrivalController m_Arrival;
		private readonly RecoveryController m_Recovery;
		private readonly VehicleNavigationSettings m_Settings;

		private readonly SpeedPlanner m_SpeedPlanner;
		private readonly EmergencyStopController m_EmergencyStop;

		private float m_DefaultLookAhead;
		private bool m_PlanDirty;
		private ReverseDriver m_ReverseDriver;
		private DriverContext m_DriverCtx;
		private TrajectoryPrediction m_Prediction;
		private ManeuverFeasibilityChecker m_Feasibility;

		public FeasibilityResult LastFeasibility { get; private set; }

		public void SetPrediction(TrajectoryPrediction _pred, ManeuverFeasibilityChecker _feasibility)
		{
			m_Prediction = _pred;
			m_Feasibility = _feasibility;
			m_DrivingPlanner.SetFeasibility(_feasibility);
		}

		// -- Events --
		public event System.Action PathChanged;
		public event System.Action<Maneuver> ManeuverStarted;
		public event System.Action<Maneuver> ManeuverFinished;
		public event System.Action<string> ReplanTriggered;

		public DriverFSM(
			NavigationContext _ctx,
			PathPlanner _pathPlanner,
			DrivingPlanner _drivingPlanner,
			ManeuverPlanner _maneuverPlanner,
			PursuitController _pursuit,
			MotionController _motion,
			ArrivalController _arrival,
			RecoveryController _recovery,
			VehicleNavigationSettings _settings)
		{
			m_Ctx = _ctx;
			m_PathPlanner = _pathPlanner;
			m_DrivingPlanner = _drivingPlanner;
			m_ManeuverPlanner = _maneuverPlanner;
			m_Pursuit = _pursuit;
			m_Motion = _motion;
			m_Arrival = _arrival;
			m_Recovery = _recovery;
			m_Settings = _settings;
			m_DefaultLookAhead = _settings != null ? _settings.LookAheadBase : 6f;
			m_SpeedPlanner = new SpeedPlanner();
			m_EmergencyStop = new EmergencyStopController();
		}

		public void BuildLimiters(CombatVehicleSystem.VehicleTuning _tuning)
		{
			m_SpeedPlanner.Clear();

			if (_tuning == null)
				return;

			m_SpeedPlanner.Register(new GoalLimiter(
				_tuning.CreepSpeedKmh,
				_tuning.CreepDistance));

			m_SpeedPlanner.Register(new CurveLimiter(
				m_Ctx.Params.CurvatureSpeedCurve));
		}

		public void EmergencyStop(StopReason _reason)
		{
			m_EmergencyStop.Activate(_reason);
			if (CurrentState != State.Idle)
				CurrentState = State.EmergencyStop;
		}

		public void ClearEmergency()
		{
			m_EmergencyStop.Deactivate();
		}

		public void SetDestination(NavigationRequest _request)
		{
			m_Ctx.Request = _request;
			m_Ctx.Memory.ResetForNewOrder();
			m_Recovery.Reset();
			m_Ctx.CurrentManeuverIndex = 0;
			m_PlanDirty = true;
			m_EmergencyStop.Deactivate();
			m_Ctx.ActiveStopReason = StopReason.None;
			CurrentState = State.Driving;
		}

		public void Stop()
		{
			m_Ctx.Request = default;
			m_Ctx.Path = PathResult.Invalid;
			m_Ctx.Plan = DrivingPlan.Empty;
			m_Ctx.CurrentManeuverIndex = 0;
			m_Recovery.Reset();
			m_EmergencyStop.Deactivate();
			m_Ctx.ActiveStopReason = StopReason.None;
			CurrentState = State.Idle;
		}

		public VehicleCommand Tick()
		{
			FeedbackState fb = m_Ctx.State;

			if (m_EmergencyStop.IsActive)
			{
				if (fb.SpeedKmh < 0.1f && m_EmergencyStop.Reason == StopReason.Player)
				{
					m_EmergencyStop.Deactivate();
					m_Ctx.ActiveStopReason = StopReason.Player;
					CurrentState = State.Idle;
					return m_Motion.Idle();
				}
				m_Ctx.ActiveStopReason = m_EmergencyStop.Reason;
				return m_EmergencyStop.EmergencyCommand;
			}

			if (CurrentState == State.Idle)
				return m_Motion.Idle();

			if (CurrentState == State.Holding)
				return TickHolding();

			if ((m_PlanDirty || !m_Ctx.HasPlan) && CurrentState == State.Driving)
			{
				if (m_Ctx.HasRequest)
					RebuildPlan();
				else
					CurrentState = State.Idle;
			}

			if (CurrentState == State.Idle)
				return m_Motion.Idle();

		// Recovery check
		var (recAction, recManeuver) = m_Recovery.EvaluateAndGetManeuver(fb, m_Ctx.Memory);
		if (recAction != RecoveryAction.None)
		{
			switch (recAction)
			{
				case RecoveryAction.RebuildPath:
					m_Recovery.Reset();
					m_PlanDirty = true;
					ReplanTriggered?.Invoke("recovery replan");
					return m_Motion.BrakeToStop(false);

				case RecoveryAction.AbortAndStop:
					CurrentState = State.Idle;
					m_Ctx.ActiveStopReason = StopReason.Stuck;
					m_Ctx.Memory.ResetRecoveryCounters();
					ReplanTriggered?.Invoke("recovery abort");
					return m_Motion.BrakeToStop(true);

				case RecoveryAction.ReverseOut:
					m_Recovery.Reset();
					m_PlanDirty = true;
					ReplanTriggered?.Invoke("recovery reverse");
					return m_Motion.BrakeToStop(false);

				default:
					if (recManeuver != null)
					{
						CurrentState = State.Recovery;
						ReplanTriggered?.Invoke("stuck detected");
					}
					break;
			}
		}

		m_Recovery.Update(Time.fixedDeltaTime);

		if (CurrentState == State.Recovery)
		{
			if (m_Recovery.CheckRecoveryComplete(fb))
			{
				m_Ctx.Memory.ResetRecoveryCounters();
				CurrentState = State.Driving;
				RebuildPlan();
				ReplanTriggered?.Invoke("recovery complete");
			}
			else if (recManeuver != null)
			{
				return ExecuteManeuver(recManeuver);
			}
		}

			// Completion check
			if (CurrentState == State.Driving)
			{
				Maneuver current = m_Ctx.CurrentManeuver;
				if (current != null && current.IsArrivalManeuver &&
				    current.IsComplete(new ManeuverContext(
					    fb.Position, fb.Forward, fb.SpeedKmh,
					    m_DefaultLookAhead * 0.35f, false)))
				{
					CurrentState = State.Arrival;
				}
				else if (AdvanceManeuverIfComplete(fb))
				{
					if (m_Ctx.CurrentManeuverIndex >= m_Ctx.Plan.Maneuvers.Count)
						CurrentState = State.Arrival;
				}
			}

			if (CurrentState == State.Arrival)
				return TickArrival();

			Maneuver maneuver = m_Ctx.CurrentManeuver;
			if (maneuver == null)
			{
				CurrentState = State.Idle;
				return m_Motion.Idle();
			}

			return ExecuteManeuver(maneuver);
		}

		private void RebuildPlan()
		{
			m_PlanDirty = false;
			FeedbackState fb = m_Ctx.State;

			if (!m_Ctx.HasRequest)
			{
				m_Ctx.Plan = DrivingPlan.Empty;
				return;
			}

			m_Ctx.Path = m_PathPlanner.BuildPath(fb.Position, m_Ctx.Request.Destination);
			if (!m_Ctx.Path.IsValid)
			{
				m_Ctx.Path = m_PathPlanner.BuildPath(
					fb.Position + fb.Forward * 0.5f,
					m_Ctx.Request.Destination);
			}

			m_DriverCtx = new DriverContext();
			m_DriverCtx.UpdateFrom(fb, m_Ctx.Params, m_Ctx.Request, m_Ctx.Path);

			Maneuver previous = m_Ctx.CurrentManeuver;
			m_Ctx.Plan = m_DrivingPlanner.BuildPlan(
				m_Ctx.Request, m_Ctx.Path, fb,
				m_Settings.ReverseMaxSegment,
				m_Settings.ReverseAngleDegrees,
				m_Settings.TurnRadius,
				m_DriverCtx);

		m_ManeuverPlanner.BuildWaypoints(
			m_Ctx.Plan, m_Ctx.Request, m_Ctx.Path, fb,
			m_Ctx.Params.MinTurningRadius,
			m_Ctx.Params.WheelBase);

			LastFeasibility = m_Ctx.Plan.Feasibility;
			if (!LastFeasibility.IsValid)
			{
				m_Ctx.Memory.RecordFeasibilityFailure();
				ReplanTriggered?.Invoke($"plan rejected: {LastFeasibility.FailureReason}");
				m_Ctx.Plan = new DrivingPlan(
					new Maneuver[] { new ForwardManeuver() },
					$"fallback — {LastFeasibility.FailureReason}");
				m_ManeuverPlanner.BuildWaypoints(
					m_Ctx.Plan, m_Ctx.Request, m_Ctx.Path, fb,
					m_Ctx.Params.MinTurningRadius,
					m_Ctx.Params.WheelBase);
			}

			m_Ctx.CurrentManeuverIndex = 0;

			if (DebugLog)
			{
				var plan = m_Ctx.Plan;
				string ml = "";
				if (plan.Maneuvers != null)
					for (int i = 0; i < plan.Maneuvers.Count; i++)
						ml += $"[{i}]{plan.Maneuvers[i]?.Type} ";
				Debug.Log($"[DriverFSM] RebuildPlan: mode={plan.DrivingMode} maneuvers=[{ml}] cost={plan.TotalCost:F1} dist={plan.EstimatedDistance:F1}m rev={plan.ReverseDistance:F1}m risk={plan.Risk:F2} reason={plan.Reason}");
			}

			Maneuver next = m_Ctx.CurrentManeuver;
			if (previous != next)
			{
				if (previous != null)
					ManeuverFinished?.Invoke(previous);
				if (next != null)
					ManeuverStarted?.Invoke(next);
			}

			PathChanged?.Invoke();
		}

		private VehicleCommand ExecuteManeuver(Maneuver _maneuver)
		{
			if (_maneuver is ReverseIntentManeuver revMvr)
				return ExecuteReverseManeuver(revMvr);

			float speedFraction = VehicleSpeedModeUtil.Fraction(m_Ctx.Request.SpeedMode) *
			                      _maneuver.SpeedScale;

			PursuitController.Output pursuit = m_Pursuit.Tick(
				m_Ctx, _maneuver,
				speedFraction,
				m_Ctx.TopSpeedKmh,
				m_DefaultLookAhead,
				_maneuver.LookAheadOverride);

			m_Ctx.RemainingDistance = pursuit.DistanceToEnd;
			m_Ctx.CurrentCurvature = pursuit.Command.DesiredCurvature;

			float targetSpeed = m_SpeedPlanner.ComputeTargetSpeed(m_Ctx);
			float finalSpeed = Mathf.Min(pursuit.Command.DesiredSpeedKmh, targetSpeed);
			m_Ctx.DesiredSpeedKmh = finalSpeed;
			m_Ctx.TargetSpeedKmh = targetSpeed;
			m_Ctx.ActiveLimit = m_SpeedPlanner.ActiveLimit;

			if (pursuit.IsComplete && _maneuver.Type == VehicleManeuverType.Stop)
				return m_Motion.BrakeToStop(_hard: false);

			MotionCommand finalCmd = new MotionCommand(
				finalSpeed,
				pursuit.Command.DesiredCurvature,
				pursuit.Command.Reverse);

			return m_Motion.Convert(m_Ctx, finalCmd);
		}

		private VehicleCommand ExecuteReverseManeuver(ReverseIntentManeuver _maneuver)
		{
			if (m_ReverseDriver == null)
			{
				m_ReverseDriver = new ReverseDriver();
				m_ReverseDriver.Configure(
					m_Ctx.Params.CurvatureSpeedCurve,
					m_DriverCtx?.ReverseSteeringLimitCurve,
					m_Prediction ?? new TrajectoryPrediction(m_Settings.GeometryLayers));

				if (m_DriverCtx != null)
				{
					m_DriverCtx.UpdateFrom(m_Ctx.State, m_Ctx.Params, m_Ctx.Request, m_Ctx.Path);
					float speedFraction = VehicleSpeedModeUtil.Fraction(m_Ctx.Request.SpeedMode) * _maneuver.SpeedScale;
					m_ReverseDriver.TryStart(m_DriverCtx, speedFraction);
				}
			}

			if (m_DriverCtx != null)
				m_DriverCtx.UpdateFrom(m_Ctx.State, m_Ctx.Params, m_Ctx.Request, m_Ctx.Path);

			return m_ReverseDriver.Tick(Time.fixedDeltaTime);
		}

		private VehicleCommand TickArrival()
		{
			FeedbackState fb = m_Ctx.State;
			ArrivalCriteria criteria = ArrivalCriteria.FromRequest(m_Ctx.Request);
			float? heading = m_Ctx.Request.HasHeading ? m_Ctx.Request.HeadingYaw : (float?)null;

			if (m_Arrival.HasArrived(fb.Position, fb.Yaw, m_Ctx.Request.Destination, heading))
			{
				CurrentState = State.Holding;
				m_Ctx.ActiveStopReason = StopReason.Goal;
				return m_Motion.BrakeToStop(_hard: false);
			}

			Maneuver arrivalManeuver = m_Ctx.CurrentManeuver;
			if (arrivalManeuver == null)
			{
				if (m_Ctx.Plan.Maneuvers != null && m_Ctx.Plan.Maneuvers.Count > 0)
				{
					foreach (var m in m_Ctx.Plan.Maneuvers)
					{
						if (m.IsArrivalManeuver)
						{
							arrivalManeuver = m;
							break;
						}
					}
				}

				if (arrivalManeuver == null)
				{
					CurrentState = State.Holding;
					m_Ctx.ActiveStopReason = StopReason.Goal;
					return m_Motion.BrakeToStop(_hard: false);
				}
			}

			PursuitController.Output pursuit = m_Pursuit.Tick(
				m_Ctx, arrivalManeuver,
				VehicleSpeedModeUtil.Fraction(m_Ctx.Request.SpeedMode) * arrivalManeuver.SpeedScale,
				m_Ctx.TopSpeedKmh,
				m_DefaultLookAhead,
				arrivalManeuver.LookAheadOverride);
			m_Ctx.RemainingDistance = pursuit.DistanceToEnd;

			MotionCommand cmd = pursuit.Command;

			float targetSpeed = m_SpeedPlanner.ComputeTargetSpeed(m_Ctx);
			float finalSpeed = Mathf.Min(cmd.DesiredSpeedKmh, targetSpeed);
			m_Ctx.DesiredSpeedKmh = finalSpeed;
			m_Ctx.TargetSpeedKmh = targetSpeed;
			m_Ctx.ActiveLimit = m_SpeedPlanner.ActiveLimit;

			float distance = FlatDistance(fb.Position, m_Ctx.Request.Destination);
			if (criteria.HasTargetForward && heading.HasValue &&
			    distance < criteria.HeadingBlendStartDistance)
			{
				float headingError = Mathf.DeltaAngle(fb.Yaw, heading.Value);
				float headingCurv = headingError * Mathf.Deg2Rad / 5f;
				float blend = 1f - Mathf.Clamp01(distance / criteria.HeadingBlendStartDistance);
				float blendedCurv = Mathf.Lerp(cmd.DesiredCurvature, headingCurv, blend);
				float blendedSpeed = Mathf.Min(finalSpeed, criteria.HeadingBlendMaxSpeedKmh);
				cmd = new MotionCommand(blendedSpeed, blendedCurv, cmd.Reverse);
			}
			else
			{
				cmd = new MotionCommand(finalSpeed, cmd.DesiredCurvature, cmd.Reverse);
			}

			return m_Motion.Convert(m_Ctx, cmd);
		}

		private VehicleCommand TickHolding()
		{
			m_Ctx.ActiveStopReason = StopReason.Goal;
			return m_Motion.HoldInPlace();
		}

		private bool AdvanceManeuverIfComplete(FeedbackState _fb)
		{
			Maneuver current = m_Ctx.CurrentManeuver;
			if (current == null)
				return false;

			bool isComplete;
			if (current is ReverseIntentManeuver)
			{
				isComplete = m_ReverseDriver != null && !m_ReverseDriver.IsActive;
			}
			else
			{
				ManeuverContext ctx = new ManeuverContext(
					_fb.Position, _fb.Forward, _fb.SpeedKmh,
					Mathf.Max(4f, m_DefaultLookAhead * 0.6f),
					current.AllowReverse && _fb.IsReversing);
				isComplete = current.IsComplete(ctx);
			}

			if (isComplete)
			{
				ManeuverFinished?.Invoke(current);
				m_Ctx.CurrentManeuverIndex++;
				Maneuver next = m_Ctx.CurrentManeuver;
				if (next != null)
					ManeuverStarted?.Invoke(next);
				return true;
			}

			return false;
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f; _b.y = 0f;
			return Vector3.Distance(_a, _b);
		}
	}
}
