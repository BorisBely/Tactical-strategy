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
			Planning,
			Driving,
			Arrival,
			FollowingTrajectory,
			Recovery,
			Holding,
			EmergencyStop
		}

		public State CurrentState { get; private set; } = State.Idle;
		public NavigationOutcome Outcome { get; private set; } = NavigationOutcome.None;
		public int ReplanCount { get; private set; }
		public NavigationProgressSnapshot LastProgress { get; private set; } = NavigationProgressSnapshot.Empty;

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
		private readonly LocalPosePlanner m_LocalPlanner = new LocalPosePlanner();
		private readonly TrajectoryTracker m_TrajectoryTracker = new TrajectoryTracker();

		private float m_DefaultLookAhead;
		private bool m_PlanDirty;
		private bool m_GoalLocked;
		private ReverseDriver m_ReverseDriver;
		private DriverContext m_DriverCtx;
		private TrajectoryPrediction m_Prediction;
		private ManeuverFeasibilityChecker m_Feasibility;
		private PrecisionArrivalController m_PrecisionArrival;
		private readonly GoalPoseValidator m_GoalValidator = new GoalPoseValidator();
		private GoalPoseCriteria m_GoalCriteria = GoalPoseCriteria.Default;
		private int m_PathRevision;
		private int m_TickSequence;
		private int m_LastGoalValidationTick = -1;
		private bool m_GoalValidThisTick;
		private Transform m_VehicleRoot;
		private PlanningObstacleSnapshot m_LastSnapshot;
		private GoalPose m_ActiveGoal;
		private bool m_LocalRetryExpanded;
		private bool m_LocalRetryPositionOnly;
		private bool m_LocalHandoffDeferred;
		private float m_LocalHandoffDeferredDist = -1f;
		private int m_HeadingReplanAttempts;
		private int m_PathReplanAttempts;
		private bool m_PendingPathReplan;
		private bool m_PendingHeadingReplan;
		private float m_BestGoalDist = float.MaxValue;
		private float m_DivergeTimer;
		private bool m_HeadingShuffleActive;
		private Vector3 m_ReplanExhaustPos;
		private float m_LastCruisePathLen = -1f;
		private float m_NextCruiseRebuildTime;
		private float m_LastPathReplanTime;
		private int m_LastReplanTrajectoryHash;
		private int m_RebuildPlanFrame = -1;
		private float m_BestConfirmedGoalDist = float.MaxValue;
		private float m_SuppressPathReplanUntilTime;
		private LocalPlanningSession m_PlanningSession;
		private bool m_PlanningActive;
		private int m_LastPlanningSliceFrame = -1;
		private float m_PlanningWallStartTime = -1f;
		private bool m_PlanningWallExhausted;
		private string m_LastFailedPlanSignature;
		private float m_NextPlanRetryTime;
		private Vector3 m_LastSnapshotPos;
		private float m_LastSnapshotYaw;
		private int m_LastSnapshotFrame = -1;
		private TerminalCapturePhase m_TerminalCapture = TerminalCapturePhase.None;
		private bool m_TerminalReacquireUsed;
		private bool m_ForceLocalHandoff;

		private enum TerminalCapturePhase
		{
			None,
			BrakingCaptured,
			Reacquiring
		}

		private const float c_HandoffMaxSpeedKmh = 16f;
		private const float c_HandoffMinSpeedKmh = 4f;
		private const float c_GlobalCruiseMinDist = 8f;
		private const float c_DivergeGrowth = 2f;
		private const float c_DivergeTimeout = 2.5f;
		private const float c_TurnAroundAlignDeg = 110f;
		private const float c_CruiseRebuildInterval = 0.5f;
		private const int c_MaxPathReplanAttempts = 3;
		private const int c_MaxHeadingReplans = 2;
		private const float c_ReplanCooldownSec = 0.25f;
		private const float c_PlanRetryCooldownSec = 0.35f;
		private const float c_PlanSearchPositionTolerance = 0.25f;
		private const float c_TerminalReacquireRadius = 0.35f;
		private const float c_TerminalCaptureRelease = 0.45f;

		public FeasibilityResult LastFeasibility { get; private set; }
		public VehicleTrajectory ActiveTrajectory => m_TrajectoryTracker.Trajectory;
		public TrajectoryTracker.Output LastTrackerOutput => m_TrajectoryTracker.LastOutput;
		public bool TurnEntryGateActive => m_TrajectoryTracker.TurnEntryGateActive;
		public float PathYawAtIndex => m_TrajectoryTracker.PathYawAtIndex;
		public LocalPosePlanner.PlanStats LastLocalPlanStats => m_LocalPlanner.LastStats;
		public PlanningObstacleSnapshot LastObstacleSnapshot => m_LastSnapshot;

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
			m_PrecisionArrival = new PrecisionArrivalController();
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

		public void SetGoalCriteria(GoalPoseCriteria _criteria)
		{
			m_GoalCriteria = _criteria;
		}

		public void SetVehicleRoot(Transform _root)
		{
			m_VehicleRoot = _root;
		}

		public void SetDestination(NavigationRequest _request)
		{
			m_Ctx.Request = _request;
			m_Ctx.Memory.ResetForNewOrder();
			m_Recovery.Reset();
			m_Ctx.CurrentManeuverIndex = 0;
			m_PlanDirty = true;
			m_GoalLocked = false;
			m_GoalValidator.Reset();
			m_LastGoalValidationTick = -1;
			m_GoalValidThisTick = false;
			m_LocalRetryExpanded = false;
			m_LocalRetryPositionOnly = false;
			m_LocalHandoffDeferred = false;
			m_LocalHandoffDeferredDist = -1f;
			m_HeadingReplanAttempts = 0;
			m_PathReplanAttempts = 0;
			m_ReplanExhaustPos = Vector3.zero;
			m_PendingPathReplan = false;
			m_PendingHeadingReplan = false;
			m_BestGoalDist = float.MaxValue;
			m_DivergeTimer = 0f;
			m_HeadingShuffleActive = false;
			m_ReplanExhaustPos = Vector3.zero;
			m_LastCruisePathLen = -1f;
			m_NextCruiseRebuildTime = 0f;
			m_LastPathReplanTime = 0f;
			m_LastReplanTrajectoryHash = 0;
			m_RebuildPlanFrame = -1;
			m_BestConfirmedGoalDist = float.MaxValue;
			m_SuppressPathReplanUntilTime = 0f;
			m_PlanningSession = null;
			m_PlanningActive = false;
			m_LastPlanningSliceFrame = -1;
			m_PlanningWallStartTime = -1f;
			m_PlanningWallExhausted = false;
			m_LastFailedPlanSignature = null;
			m_NextPlanRetryTime = 0f;
			m_LastSnapshotFrame = -1;
			m_TerminalCapture = TerminalCapturePhase.None;
			m_TerminalReacquireUsed = false;
			m_ForceLocalHandoff = false;
			m_TrajectoryTracker.Deactivate();
			m_ActiveGoal = ClampActiveGoalTolerance(GoalPose.FromRequest(_request, m_GoalCriteria));
			Outcome = NavigationOutcome.InProgress;
			ReplanCount = 0;
			m_PathRevision = 0;
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
			m_GoalLocked = false;
			m_GoalValidator.Reset();
			m_Recovery.Reset();
			m_TrajectoryTracker.Deactivate();
			m_EmergencyStop.Deactivate();
			m_Ctx.ActiveStopReason = StopReason.None;
			Outcome = NavigationOutcome.Cancelled;
			CurrentState = State.Idle;
		}

		public VehicleCommand Tick()
		{
			m_TickSequence++;
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

			if (CurrentState == State.Planning)
				return TickPlanning(fb);

			if (CurrentState == State.Holding)
				return TickHolding();

			if ((m_PlanDirty || !m_Ctx.HasPlan) &&
			    (CurrentState == State.Driving || CurrentState == State.FollowingTrajectory || CurrentState == State.Planning))
			{
				if (m_Ctx.HasRequest)
					RebuildPlan();
				else
					CurrentState = State.Idle;
			}

			if (CurrentState == State.Idle)
				return m_Motion.Idle();

			return TickDriving(fb);
		}

		private VehicleCommand TickDriving(FeedbackState fb)
		{
			UpdateProgressSnapshot(fb);

			if (TryValidateGoal(fb))
				return EnterSucceededHolding();

			// Terminal settle must cancel queued replans before they fire.
			if (UpdateTerminalCapture(fb, out VehicleCommand terminalCmd))
				return terminalCmd;

			// Handoff from coarse NavMesh cruise into local pose planner.
			if (CurrentState == State.Driving && ShouldHandoffToLocalPose(fb))
			{
				m_PlanDirty = true;
				RebuildPlan();
				if (CurrentState == State.FollowingTrajectory)
					return TickTrajectory();
			}

			if (TryProcessPendingReplans(out VehicleCommand pendingCmd))
				return pendingCmd;

			if (CurrentState == State.FollowingTrajectory)
			{
				if (!m_TrajectoryTracker.OnStagingSegment &&
				    !m_TrajectoryTracker.StagingMakingProgress &&
				    m_TerminalCapture == TerminalCapturePhase.None &&
				    !m_TrajectoryTracker.LastOutput.RequestTerminalBrake &&
				    UpdateDivergenceWatchdog(fb, FlatDistance(fb.Position, m_Ctx.Request.Destination)))
				{
					HandleDivergenceWatchdog(fb);
					if (CurrentState == State.FollowingTrajectory)
						return TickTrajectory();
					if (CurrentState == State.Idle || Outcome == NavigationOutcome.NoFeasibleManeuver)
						return m_Motion.BrakeToStop(false);
				}

				return TickTrajectory();
			}

			if (CurrentState == State.Driving)
			{
				if (UpdateDivergenceWatchdog(fb, FlatDistance(fb.Position, m_Ctx.Request.Destination)))
				{
					HandleDivergenceWatchdog(fb);
					if (CurrentState == State.FollowingTrajectory)
						return TickTrajectory();
					if (CurrentState == State.Idle || Outcome == NavigationOutcome.NoFeasibleManeuver)
						return m_Motion.BrakeToStop(false);
				}
			}

		// Recovery check
		var (recAction, recManeuver) = m_Recovery.EvaluateAndGetManeuver(fb, m_Ctx.Memory);
		if (recAction != RecoveryAction.None)
		{
			switch (recAction)
			{
				case RecoveryAction.RebuildPath:
					m_Recovery.Reset();
					m_PlanDirty = true;
					m_LocalRetryExpanded = false;
					m_LocalRetryPositionOnly = false;
					ReplanCount++;
					ReplanTriggered?.Invoke("recovery replan");
					return m_Motion.BrakeToStop(false);

				case RecoveryAction.AbortAndStop:
					CurrentState = State.Idle;
					Outcome = NavigationOutcome.Stuck;
					m_Ctx.ActiveStopReason = StopReason.Stuck;
					m_Ctx.Memory.ResetRecoveryCounters();
					ReplanTriggered?.Invoke("recovery abort");
					return m_Motion.BrakeToStop(true);

				case RecoveryAction.ReverseOut:
					m_Recovery.Reset();
					InjectReverseEscape(fb, 4f);
					ReplanCount++;
					ReplanTriggered?.Invoke("recovery reverse out");
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
					    0.5f, false)))
				{
					if (TryValidateGoal(fb))
						return EnterSucceededHolding();
					CurrentState = State.Arrival;
				}
				else if (AdvanceManeuverIfComplete(fb))
				{
					if (m_Ctx.CurrentManeuverIndex >= m_Ctx.Plan.Maneuvers.Count)
					{
						if (TryValidateGoal(fb))
							return EnterSucceededHolding();
						CurrentState = State.Arrival;
					}
				}
			}

			if (CurrentState == State.Arrival)
				return TickArrival();

			Maneuver maneuver = m_Ctx.CurrentManeuver;
			if (maneuver == null)
			{
				if (CurrentState == State.Planning || m_PlanningActive || Outcome == NavigationOutcome.InProgress)
					return m_Motion.HoldInPlace();
				CurrentState = State.Idle;
				return m_Motion.Idle();
			}

			return ExecuteManeuver(maneuver);
		}

		private void RebuildPlan()
		{
			if (!m_PlanDirty && Time.time < m_NextCruiseRebuildTime)
				return;

			if (m_RebuildPlanFrame == Time.frameCount)
				return;

			if (CurrentState == State.FollowingTrajectory &&
			    Time.time - m_LastPathReplanTime < c_ReplanCooldownSec)
				return;

			m_RebuildPlanFrame = Time.frameCount;
			m_PlanDirty = false;
			m_GoalLocked = false;
			m_ReverseDriver = null;
			m_PrecisionArrival.Deactivate();
			FeedbackState fb = m_Ctx.State;

			if (!m_Ctx.HasRequest)
			{
				m_Ctx.Plan = DrivingPlan.Empty;
				m_TrajectoryTracker.Deactivate();
				return;
			}

			CapturePlanningSnapshot();
			EnsureGlobalPathForHeading(fb);
			ResolveImplicitHeading(fb);
			m_ActiveGoal = ClampActiveGoalTolerance(GoalPose.FromRequest(m_Ctx.Request, m_GoalCriteria));
			float flatDist = FlatDistance(fb.Position, m_Ctx.Request.Destination);
			bool useLocal = ShouldUseLocalPosePlanner(flatDist, fb);

			if (useLocal && fb.SpeedKmh > c_HandoffMaxSpeedKmh)
			{
				BuildGlobalCruisePlan(fb);
				return;
			}

			if (useLocal)
			{
				if (TryBuildLocalTrajectoryPlan(fb, flatDist, m_LocalRetryExpanded))
				{
					m_LocalRetryExpanded = false;
					m_LocalRetryPositionOnly = false;
					m_LastPathReplanTime = Time.time;
					return;
				}

				if (CurrentState == State.Planning)
					return;

				// Wall timeout already closed the session terminal — do not restart.
				if (m_PlanningWallExhausted || Outcome == NavigationOutcome.NoFeasibleManeuver)
					return;

				if (!m_LocalRetryExpanded)
				{
					m_LocalRetryExpanded = true;
					m_PlanDirty = true;
					return;
				}

				if (!m_LocalRetryPositionOnly &&
				    m_Ctx.Request.HasAdvisoryHeading)
				{
					m_LocalRetryPositionOnly = true;
					m_ActiveGoal = new GoalPose(
						m_Ctx.Request.Destination,
						null,
						GoalHeadingSource.None,
						m_GoalCriteria.LongitudinalTolerance > 0f
							? m_GoalCriteria.LongitudinalTolerance
							: ArrivalPositionBand.DefaultLongitudinal,
						m_GoalCriteria.LateralTolerance > 0f
							? m_GoalCriteria.LateralTolerance
							: (m_Ctx.Request.MinArrivalDistance > 0f
								? m_Ctx.Request.MinArrivalDistance
								: ArrivalPositionBand.DefaultLateral),
						m_GoalCriteria.HeadingToleranceDeg > 0f
							? m_GoalCriteria.HeadingToleranceDeg
							: m_Ctx.Request.MinArrivalHeading);
					m_PlanDirty = true;
					return;
				}

				// Far enough: keep coarse cruise instead of hard-failing the whole order.
				if (flatDist >= 3f)
				{
					if (IsSideGoal(fb, flatDist) && TryBuildSideRepositionFallback(fb, flatDist))
					{
						m_LocalRetryExpanded = false;
						m_LocalRetryPositionOnly = false;
						m_LastPathReplanTime = Time.time;
						return;
					}

					m_LocalHandoffDeferred = true;
					m_LocalHandoffDeferredDist = flatDist;
					var stats = m_LocalPlanner.LastStats;
					ReplanTriggered?.Invoke(
						$"local pose deferred: {stats.Reason} tried={stats.CandidatesTried} col={stats.RejectedCollision} tol={stats.RejectedTolerance}");
					BuildGlobalCruisePlan(fb);
					return;
				}

				m_Ctx.Memory.RecordFeasibilityFailure();
				if (TryBuildDirectCreepPlan(fb, flatDist))
					return;

				var failStats = m_LocalPlanner.LastStats;
				ReplanTriggered?.Invoke(
					$"local pose failed: {failStats.Reason} tried={failStats.CandidatesTried} col={failStats.RejectedCollision} tol={failStats.RejectedTolerance}");
				Outcome = NavigationOutcome.NoFeasibleManeuver;
				CurrentState = State.Idle;
				m_Ctx.Plan = new DrivingPlan(new Maneuver[] { new StopManeuver() }, "no local pose");
				m_TrajectoryTracker.Deactivate();
				return;
			}

			BuildGlobalCruisePlan(fb);
		}

		private bool ShouldUseLocalPosePlanner(float _flatDist, FeedbackState _fb)
		{
			if (m_Settings == null || !m_Settings.UseLocalPosePlanner)
				return false;
			if (m_ForceLocalHandoff)
				return true;
			float localDist = m_Settings.LocalPlanningDistance > 0f
				? m_Settings.LocalPlanningDistance
				: 15f;

			if (_flatDist <= localDist)
			{
				if (!m_Ctx.Request.RequiresPosePlanning && _flatDist > c_GlobalCruiseMinDist)
				{
					Vector3 toGoal = m_Ctx.Request.Destination - _fb.Position;
					toGoal.y = 0f;
					if (toGoal.sqrMagnitude > 0.01f)
					{
						float travelYaw = Quaternion.LookRotation(toGoal.normalized, Vector3.up).eulerAngles.y;
						float align = Mathf.Abs(Mathf.DeltaAngle(_fb.Yaw, travelYaw));
						// Straight-ahead cruise may stay on NavMesh; rear/oblique stay local.
						if (align <= 25f)
							return false;
					}
				}
				return true;
			}

			// Slightly beyond local band but clearly rear: still prefer reverse local over TurnAround stall.
			if (!m_Ctx.Request.RequiresPosePlanning &&
			    _flatDist <= localDist * 1.35f &&
			    TravelAlignmentDeg(_fb, m_Ctx.Request.Destination) >= 155f)
				return true;

			if (m_Ctx.Request.RequiresPosePlanning && _flatDist <= localDist * 1.25f)
				return true;
			return false;
		}

		private bool ShouldHandoffToLocalPose(FeedbackState _fb)
		{
			if (m_Settings == null || !m_Settings.UseLocalPosePlanner)
				return false;
			if (m_TrajectoryTracker.HasTrajectory)
				return false;
			if (_fb.SpeedKmh > c_HandoffMaxSpeedKmh)
				return false;
			float dist = FlatDistance(_fb.Position, m_Ctx.Request.Destination);
			float handoff = m_Settings.LocalPlanningDistance > 0f
				? m_Settings.LocalPlanningDistance
				: 15f;
			if (dist > handoff)
				return false;

			if (!ShouldUseLocalPosePlanner(dist, _fb))
				return false;

			// Avoid replan spam after a deferred local failure while still far.
			if (m_LocalHandoffDeferred &&
			    m_LocalHandoffDeferredDist > 0f &&
			    dist >= 3f &&
			    Mathf.Abs(dist - m_LocalHandoffDeferredDist) < 1.5f)
				return false;

			return true;
		}

		private void CapturePlanningSnapshot()
		{
			if (m_VehicleRoot == null)
			{
				m_LastSnapshot = null;
				return;
			}

			FeedbackState fb = m_Ctx.State;
			float posDelta = BicycleKinematics.FlatDistance(fb.Position, m_LastSnapshotPos);
			float yawDelta = Mathf.Abs(Mathf.DeltaAngle(fb.Yaw, m_LastSnapshotYaw));
			if (m_LastSnapshot != null && m_LastSnapshot.IsValid &&
			    m_LastSnapshotFrame == Time.frameCount &&
			    posDelta < 0.05f && yawDelta < 2f)
				return;

			int rays = m_Settings != null ? m_Settings.DenseFanRayCount : 40;
			float maxDist = m_Settings != null ? m_Settings.DenseFanMaxDistance : 12f;
			var profile = m_Ctx.Params.Kinematics;
			m_LastSnapshot = PlanningObstacleSnapshot.Build(
				m_VehicleRoot, profile, m_Settings != null ? m_Settings.GeometryLayers : ~0,
				rays, maxDist);
			m_LastSnapshotPos = fb.Position;
			m_LastSnapshotYaw = fb.Yaw;
			m_LastSnapshotFrame = Time.frameCount;

			if (DebugLog && m_LastSnapshot != null)
				Debug.Log($"[DriverFSM] Planning snapshot rays={m_LastSnapshot.RayCount} queries={m_LastSnapshot.PhysicsQueries} front={m_LastSnapshot.FrontClearance:F1} rear={m_LastSnapshot.RearClearance:F1}");
		}

		private float GetPlanSliceBudgetMs() =>
			m_Settings != null && m_Settings.LocalPlanSliceBudgetMs > 0f
				? m_Settings.LocalPlanSliceBudgetMs
				: LocalPosePlanner.RuntimeSliceBudgetMs;

		private float GetPlanTotalBudgetMs() =>
			m_Settings != null && m_Settings.LocalPlanTotalBudgetMs > 0f
				? m_Settings.LocalPlanTotalBudgetMs
				: LocalPosePlanner.RuntimeTotalPlanBudgetMs;

		private float GetPlanWallTimeoutSec() =>
			m_Settings != null && m_Settings.LocalPlanWallTimeoutSec > 0f
				? m_Settings.LocalPlanWallTimeoutSec
				: 6f;

		public float PlanWallTimeoutSec => GetPlanWallTimeoutSec();

		private string ComputePlanSignature(FeedbackState _fb)
		{
			Vector3 g = m_ActiveGoal.Position;
			return $"{_fb.Position.x:F2},{_fb.Position.z:F2}|{_fb.Yaw:F1}|{g.x:F2},{g.z:F2}|{m_ActiveGoal.RequiresPosePlanning}|{m_LocalRetryExpanded}|{m_LocalRetryPositionOnly}";
		}

		private bool IsPlanningWallTimeout()
		{
			return m_PlanningWallStartTime > 0f &&
			       Time.realtimeSinceStartup - m_PlanningWallStartTime >= GetPlanWallTimeoutSec();
		}

		private void FailLocalPlanningTerminal(string _reason)
		{
			m_PlanningActive = false;
			m_PlanningSession = null;
			m_PlanningWallStartTime = -1f;
			m_PlanningWallExhausted = true;
			m_LastFailedPlanSignature = null;
			m_NextPlanRetryTime = Time.time + c_PlanRetryCooldownSec * 4f;
			Outcome = NavigationOutcome.NoFeasibleManeuver;
			CurrentState = State.Idle;
			m_Ctx.Plan = new DrivingPlan(new Maneuver[] { new StopManeuver() }, _reason);
			m_TrajectoryTracker.Deactivate();
			if (DebugLog)
				Debug.LogWarning($"[DriverFSM] {_reason}");
		}

		private bool TryAdvancePlanningSlice(FeedbackState _fb, float _flatDist, bool _expanded, out PlanStepResult _result)
		{
			_result = default;
			if (m_PlanningWallExhausted)
				return false;

			if (IsPlanningWallTimeout())
			{
				if (m_PlanningSession != null && m_PlanningSession.IsActive)
				{
					_result = m_LocalPlanner.ForceFinalize(m_PlanningSession, "wall timeout");
					m_LastPlanningSliceFrame = Time.frameCount;
					m_PlanningSession.PlanningFrameCount++;
					if (DebugLog)
						Debug.LogWarning(
							$"[DriverFSM] Local planning wall timeout → finalize cpu={m_PlanningSession.TotalPlanCpuMs:F0}ms phase={m_PlanningSession.PhaseName} cand={m_PlanningSession.AnalyticCandidateCount}");
					return true;
				}

				FailLocalPlanningTerminal("local pose wall timeout");
				return false;
			}

			if (m_LastPlanningSliceFrame == Time.frameCount)
				return false;

			float step = m_Settings != null ? m_Settings.LocalPrimitiveStep : 0.6f;
			if (_expanded)
				step *= 0.7f;

			var profile = m_Ctx.Params.Kinematics ??
			              new VehicleKinematicsProfile(
				              m_Ctx.Params.WheelBase, m_Ctx.Params.Length, m_Ctx.Params.Width,
				              m_Ctx.Params.MaxSteeringAngleDeg);

			if (!m_PlanningActive || m_PlanningSession == null || !m_PlanningSession.IsActive)
			{
				// Do not restart an identical session after wall exhaustion.
				if (m_PlanningWallExhausted)
					return false;

				m_PlanningSession = m_LocalPlanner.CreateSession(
					_fb.Position, _fb.Yaw, CreatePlanningGoal(), profile, m_LastSnapshot,
					m_Ctx.Request.AllowReverse, step, GetPlanTotalBudgetMs());
				m_PlanningActive = true;
				if (m_PlanningWallStartTime < 0f)
					m_PlanningWallStartTime = Time.realtimeSinceStartup;
			}

			_result = m_LocalPlanner.StepPlan(m_PlanningSession, GetPlanSliceBudgetMs());
			m_LastPlanningSliceFrame = Time.frameCount;
			m_PlanningSession.PlanningFrameCount++;
			return true;
		}

		private bool TryBuildLocalTrajectoryPlan(FeedbackState _fb, float _flatDist, bool _expanded = false)
		{
			if (m_PlanningWallExhausted)
				return false;

			string signature = ComputePlanSignature(_fb);
			if (Time.time < m_NextPlanRetryTime &&
			    signature == m_LastFailedPlanSignature &&
			    !m_PlanningActive)
				return false;

			if (!TryAdvancePlanningSlice(_fb, _flatDist, _expanded, out PlanStepResult stepResult))
			{
				if (m_PlanningWallExhausted)
					return false;
				if (m_PlanningActive)
				{
					CurrentState = State.Planning;
					Outcome = NavigationOutcome.InProgress;
				}
				return false;
			}

			if (stepResult.Status == PlanStepStatus.Pending)
			{
				CurrentState = State.Planning;
				Outcome = NavigationOutcome.InProgress;
				return false;
			}

			bool wallFinalize = m_PlanningSession != null &&
			                    string.Equals(m_PlanningSession.BudgetReason, "wall timeout", System.StringComparison.Ordinal);
			m_PlanningActive = false;
			m_PlanningSession = null;
			m_LastFailedPlanSignature = null;

			if (stepResult.Status == PlanStepStatus.Ready &&
			    stepResult.Trajectory != null && stepResult.Trajectory.IsValid)
			{
				m_PlanningWallStartTime = -1f;
				m_PlanningWallExhausted = false;
				return ActivateLocalTrajectory(stepResult.Trajectory, _fb, _flatDist);
			}

			if (wallFinalize)
			{
				FailLocalPlanningTerminal("local pose wall timeout — no feasible maneuver");
				return false;
			}

			// Keep one wall deadline across expanded / position-only retries.
			m_LastFailedPlanSignature = signature;
			m_NextPlanRetryTime = Time.time + c_PlanRetryCooldownSec;
			return false;
		}

		private VehicleCommand TickPlanning(FeedbackState _fb)
		{
			Outcome = NavigationOutcome.InProgress;
			float flatDist = FlatDistance(_fb.Position, m_Ctx.Request.Destination);

			if (m_PlanningWallExhausted)
			{
				FailLocalPlanningTerminal("local pose wall timeout — no feasible maneuver");
				return m_Motion.BrakeToStop(false, StopIntent.Goal);
			}

			if (m_PlanningSession == null || !m_PlanningActive)
			{
				m_PlanDirty = true;
				RebuildPlan();
				if (CurrentState != State.Planning)
					return TickDriving(_fb);
				return m_Motion.HoldInPlace();
			}

			if (!TryAdvancePlanningSlice(_fb, flatDist, m_LocalRetryExpanded, out PlanStepResult stepResult))
			{
				if (Outcome == NavigationOutcome.NoFeasibleManeuver)
					return m_Motion.BrakeToStop(false, StopIntent.Goal);
				return m_Motion.HoldInPlace();
			}

			if (stepResult.Status == PlanStepStatus.Pending)
				return m_Motion.HoldInPlace();

			bool wallFinalize = m_PlanningSession != null &&
			                    string.Equals(m_PlanningSession.BudgetReason, "wall timeout", System.StringComparison.Ordinal);
			m_PlanningActive = false;
			m_PlanningSession = null;

			if (stepResult.Status == PlanStepStatus.Ready &&
			    stepResult.Trajectory != null && stepResult.Trajectory.IsValid &&
			    ActivateLocalTrajectory(stepResult.Trajectory, _fb, flatDist))
			{
				m_PlanningWallStartTime = -1f;
				m_PlanningWallExhausted = false;
				return TickTrajectory();
			}

			if (wallFinalize)
			{
				FailLocalPlanningTerminal("local pose wall timeout — no feasible maneuver");
				return m_Motion.BrakeToStop(false, StopIntent.Goal);
			}

			// Path-replan hang: fail terminal without synchronous Plan() restart.
			if (m_PathReplanAttempts > 0)
			{
				FailLocalPlanningTerminal("path replan failed — no feasible maneuver");
				return m_Motion.BrakeToStop(false, StopIntent.Goal);
			}

			m_LastFailedPlanSignature = ComputePlanSignature(_fb);
			m_NextPlanRetryTime = Time.time + c_PlanRetryCooldownSec;
			m_PlanDirty = true;
			RebuildPlan();
			if (CurrentState == State.FollowingTrajectory)
				return TickTrajectory();
			if (CurrentState == State.Planning)
				return m_Motion.HoldInPlace();
			return TickDriving(_fb);
		}

		private static bool IsSideGoal(FeedbackState _fb, Vector3 _destination, float _flatDist)
		{
			if (_flatDist > 8f)
				return false;
			float align = ReedsSheppPathBuilder.GetTravelAlignment(_fb.Position, _fb.Yaw, _destination);
			return align >= 55f && align <= 125f;
		}

		private bool IsSideGoal(FeedbackState _fb, float _flatDist) =>
			IsSideGoal(_fb, m_Ctx.Request.Destination, _flatDist);

		private bool TryBuildSideRepositionFallback(FeedbackState _fb, float _flatDist)
		{
			if (!IsSideGoal(_fb, _flatDist))
				return false;

			var profile = m_Ctx.Params.Kinematics ??
			              new VehicleKinematicsProfile(
				              m_Ctx.Params.WheelBase, m_Ctx.Params.Length, m_Ctx.Params.Width,
				              m_Ctx.Params.MaxSteeringAngleDeg);

			var candidates = new System.Collections.Generic.List<VehicleTrajectory>(24);
			ReedsSheppPathBuilder.AddSymmetricCandidates(
				candidates, _fb.Position, _fb.Yaw, m_ActiveGoal,
				profile.EffectiveTurnRadius, profile.WheelBase, m_Ctx.Request.AllowReverse);

			VehicleTrajectory best = null;
			float bestLen = float.MaxValue;
			for (int i = 0; i < candidates.Count; i++)
			{
				var c = candidates[i];
				if (c == null || !c.IsValid)
					continue;
				if (!TrajectoryKinematicsValidator.Validate(c, profile.EffectiveTurnRadius, out _))
					continue;
				if (c.TotalLength >= bestLen)
					continue;
				bestLen = c.TotalLength;
				best = c;
			}

			if (best == null)
				return false;

			if (DebugLog)
				Debug.Log($"[DriverFSM] Side reposition fallback: dist={_flatDist:F1}m len={best.TotalLength:F1}m reason={best.DebugReason}");

			ReplanTriggered?.Invoke($"side reposition fallback dist={_flatDist:F1}");
			return ActivateLocalTrajectory(best, _fb, _flatDist);
		}

		private bool TryBuildDirectCreepPlan(FeedbackState _fb, float _flatDist)
		{
			if (_flatDist >= 3f)
				return false;

			var profile = m_Ctx.Params.Kinematics ??
			              new VehicleKinematicsProfile(
				              m_Ctx.Params.WheelBase, m_Ctx.Params.Length, m_Ctx.Params.Width,
				              m_Ctx.Params.MaxSteeringAngleDeg);

			VehicleTrajectory traj = ReedsSheppPathBuilder.BuildDirectApproach(
				_fb.Position, _fb.Yaw, m_ActiveGoal, profile.WheelBase, m_Ctx.Request.AllowReverse);

			if (traj == null || !traj.IsValid)
				return false;

			if (DebugLog)
				Debug.Log($"[DriverFSM] Direct creep fallback: dist={_flatDist:F1}m reason={traj.DebugReason}");

			ReplanTriggered?.Invoke($"direct creep fallback dist={_flatDist:F1}");
			return ActivateLocalTrajectory(traj, _fb, _flatDist);
		}

		private bool ActivateLocalTrajectory(VehicleTrajectory traj, FeedbackState _fb, float _flatDist)
		{
			int hash = ComputeTrajectoryHash(traj);
			if (hash == m_LastReplanTrajectoryHash && m_TrajectoryTracker.HasTrajectory)
			{
				m_PendingPathReplan = false;
				m_SuppressPathReplanUntilTime = Time.time + c_ReplanCooldownSec * 2f;
				return true;
			}

			m_LastReplanTrajectoryHash = hash;
			NoteTrajectoryProgress(_fb);

			m_LocalHandoffDeferred = false;
			m_LocalHandoffDeferredDist = -1f;
			m_ReplanExhaustPos = Vector3.zero;
			m_HeadingShuffleActive = false;
			m_PathRevision++;
			// Keep physical wheel angle; only sync nav command so we don't briefly steer to 0.
			m_Motion.SyncSteeringFromMotor();
			m_TrajectoryTracker.Activate(traj, m_ActiveGoal, m_DefaultLookAhead * 0.55f, _fb.SpeedKmh);
			m_TrajectoryTracker.SetPlanStats(m_LocalPlanner.LastStats);
			m_ForceLocalHandoff = false;

			var maneuver = new TrajectoryFollowingManeuver(traj);
			VehicleDrivingMode drivingMode = ResolveTrajectoryDrivingMode(traj);
			m_Ctx.Plan = new DrivingPlan(
				new Maneuver[] { maneuver },
				$"localPose len={traj.TotalLength:F1} segs={traj.GearSegmentCount} cost={traj.Cost:F1} ({traj.DebugReason})",
				drivingMode,
				traj.Cost,
				FeasibilityResult.Valid);
			m_Ctx.Plan.BuildSegments();
			m_Ctx.CurrentManeuverIndex = 0;
			m_Ctx.Path = new PathResult(traj.ToPositions(), traj.TotalLength, true, false);
			LastFeasibility = FeasibilityResult.Valid;
			CurrentState = State.FollowingTrajectory;

			if (DebugLog)
				Debug.Log($"[DriverFSM] LocalPose plan: dist={_flatDist:F1}m len={traj.TotalLength:F1}m segs={traj.GearSegmentCount} expanded={m_LocalPlanner.LastStats.Expanded} tried={m_LocalPlanner.LastStats.CandidatesTried} colRej={m_LocalPlanner.LastStats.RejectedCollision} tolRej={m_LocalPlanner.LastStats.RejectedTolerance} rays={m_LocalPlanner.LastStats.SnapshotRays} colQ={m_LocalPlanner.LastStats.CollisionQueries} primQ={m_LocalPlanner.LastStats.PrimitiveCollisionQueries} trajQ={m_LocalPlanner.LastStats.TrajectoryCollisionQueries} planMs={m_LocalPlanner.LastStats.PlanDurationMs:F0} shots={m_LocalPlanner.LastStats.AnalyticShots} budget={m_LocalPlanner.LastStats.BudgetTerminated} reason={traj.DebugReason}");

			ManeuverStarted?.Invoke(maneuver);
			PathChanged?.Invoke();
			return true;
		}

		private bool TryProcessPendingReplans(out VehicleCommand _cmd)
		{
			_cmd = default;
			if (CurrentState != State.FollowingTrajectory)
			{
				m_PendingPathReplan = false;
				m_PendingHeadingReplan = false;
				return false;
			}

				if (m_PendingPathReplan)
			{
				m_PendingPathReplan = false;
				if (m_PathReplanAttempts < c_MaxPathReplanAttempts &&
				    Time.time - m_LastPathReplanTime >= c_ReplanCooldownSec)
				{
					m_PathReplanAttempts++;
					m_TrajectoryTracker.Deactivate();
					m_PlanDirty = true;
					m_LocalRetryExpanded = false;
					ReplanCount++;
					ReplanTriggered?.Invoke($"path replan attempt {m_PathReplanAttempts}");
					RebuildPlan();
					m_LastPathReplanTime = Time.time;
					if (CurrentState == State.FollowingTrajectory)
					{
						_cmd = TickTrajectory();
						return true;
					}

					_cmd = m_Motion.BrakeToStop(false);
					return true;
				}

				if (DebugLog)
					Debug.LogWarning("[DriverFSM] Path replan exhausted — braking");
				_cmd = m_Motion.BrakeToStop(false, StopIntent.Goal);
				return true;
			}

			if (m_PendingHeadingReplan && m_ActiveGoal.RequiresPosePlanning)
			{
				m_PendingHeadingReplan = false;
				if (m_HeadingReplanAttempts < c_MaxHeadingReplans)
				{
					m_HeadingReplanAttempts++;
					m_TrajectoryTracker.Deactivate();
					m_PlanDirty = true;
					m_LocalRetryExpanded = false;
					ReplanCount++;
					ReplanTriggered?.Invoke($"heading replan attempt {m_HeadingReplanAttempts}");
					RebuildPlan();
					if (CurrentState == State.FollowingTrajectory)
					{
						_cmd = TickTrajectory();
						return true;
					}

					_cmd = m_Motion.HoldInPlace();
					return true;
				}

				if (DebugLog)
					Debug.LogWarning("[DriverFSM] Heading replan exhausted — holding at position");
			}

			return false;
		}

		private void BuildGlobalCruisePlan(FeedbackState fb)
		{
			m_TrajectoryTracker.Deactivate();

			m_Ctx.Path = m_PathPlanner.BuildSafePath(
				fb.Position,
				m_Ctx.Request.Destination,
				m_Ctx.Params.Kinematics != null ? m_Ctx.Params.Kinematics.NavAgentRadius : 1.5f);
			if (!m_Ctx.Path.IsValid)
			{
				m_Ctx.Path = m_PathPlanner.BuildPath(
					fb.Position + fb.Forward * 0.5f,
					m_Ctx.Request.Destination,
					PathBuildOptions.SafeOnly);
			}

			if (!m_Ctx.Path.IsValid)
			{
				Outcome = NavigationOutcome.NoPath;
				CurrentState = State.Idle;
				m_Ctx.Plan = new DrivingPlan(new Maneuver[] { new StopManeuver() }, "no path");
				return;
			}

			bool sameCruise = m_LastCruisePathLen > 0f &&
			                  Mathf.Abs(m_LastCruisePathLen - m_Ctx.Path.Length) < 0.5f;
			if (!sameCruise)
				m_PathRevision++;
			m_LastCruisePathLen = m_Ctx.Path.Length;
			m_NextCruiseRebuildTime = Time.time + c_CruiseRebuildInterval;

			m_DriverCtx = new DriverContext();
			m_DriverCtx.UpdateFrom(fb, m_Ctx.Params, m_Ctx.Request, m_Ctx.Path);

			ResolveImplicitHeading(fb);
			m_ActiveGoal = ClampActiveGoalTolerance(GoalPose.FromRequest(m_Ctx.Request, m_GoalCriteria));

			Maneuver previous = m_Ctx.CurrentManeuver;

			float align = TravelAlignmentDeg(fb, m_Ctx.Request.Destination);
			bool useTurnAround = align >= c_TurnAroundAlignDeg;
			Maneuver headManeuver = useTurnAround
				? new TurnAroundManeuver(PickTurnSign(fb))
				: (Maneuver)new ForwardManeuver();
			var maneuvers = new System.Collections.Generic.List<Maneuver> { headManeuver };
			string planReason = useTurnAround
				? $"globalTurnAround dist={m_Ctx.Path.Length:F1}"
				: $"globalCruise dist={m_Ctx.Path.Length:F1}";
			m_Ctx.Plan = new DrivingPlan(
				maneuvers,
				planReason,
				useTurnAround ? VehicleDrivingMode.TurnAround : VehicleDrivingMode.Forward,
				m_Ctx.Path.Length,
				FeasibilityResult.Valid);
			m_Ctx.Plan.BuildSegments();

			m_ManeuverPlanner.BuildWaypoints(
				m_Ctx.Plan, m_Ctx.Request, m_Ctx.Path, fb,
				m_Ctx.Params.MinTurningRadius,
				m_Ctx.Params.WheelBase);

			LastFeasibility = m_Ctx.Plan.Feasibility;
			if (!LastFeasibility.IsValid || m_Ctx.Plan.Maneuvers == null || m_Ctx.Plan.Maneuvers.Count == 0 ||
			    (m_Ctx.Plan.Maneuvers.Count == 1 && m_Ctx.Plan.Maneuvers[0] is StopManeuver))
			{
				m_Ctx.Memory.RecordFeasibilityFailure();
				ReplanTriggered?.Invoke($"plan rejected: {LastFeasibility.FailureReason}");
				Outcome = NavigationOutcome.NoFeasibleManeuver;
				CurrentState = State.Idle;
				return;
			}

			m_Ctx.CurrentManeuverIndex = 0;
			CurrentState = State.Driving;

			if (DebugLog)
			{
				var plan = m_Ctx.Plan;
				string ml = "";
				if (plan.Maneuvers != null)
					for (int i = 0; i < plan.Maneuvers.Count; i++)
						ml += $"[{i}]{plan.Maneuvers[i]?.Type} ";
				Debug.Log($"[DriverFSM] RebuildPlan global: mode={plan.DrivingMode} maneuvers=[{ml}] cost={plan.TotalCost:F1} dist={plan.EstimatedDistance:F1}m reason={plan.Reason}");
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

		private VehicleCommand TickTrajectory()
		{
			FeedbackState fb = m_Ctx.State;
			if (!m_TrajectoryTracker.HasTrajectory)
			{
				CurrentState = State.Driving;
				m_PlanDirty = true;
				return m_Motion.BrakeToStop(false);
			}

			float speedFraction = VehicleSpeedModeUtil.Fraction(m_Ctx.Request.SpeedMode);
			var output = m_TrajectoryTracker.Tick(
				fb.Position, fb.Yaw, fb.SpeedKmh, m_Ctx.Params, speedFraction);

			if (output.RequestSteeringReset)
				m_Motion.SyncSteeringFromMotor();

			m_Ctx.RemainingDistance = output.DistanceToEnd;
			m_Ctx.CurrentCurvature = output.Command.DesiredCurvature;
			m_Ctx.DesiredSpeedKmh = Mathf.Abs(output.Command.DesiredSpeedKmh);
			m_Ctx.TargetSpeedKmh = m_Ctx.DesiredSpeedKmh;

			if (output.RequestTerminalBrake || m_TerminalCapture != TerminalCapturePhase.None)
			{
				m_PendingPathReplan = false;
				m_PendingHeadingReplan = false;
			}

			if (output.NeedPathReplan)
			{
				if (output.RequestTerminalBrake || m_TerminalCapture != TerminalCapturePhase.None)
					return m_Motion.BrakeToStop(false, StopIntent.Goal);

				if (Time.time < m_SuppressPathReplanUntilTime)
					return m_Motion.Convert(m_Ctx, output.Command);

				if (m_PathReplanAttempts < c_MaxPathReplanAttempts && !m_PendingPathReplan &&
				    Time.time - m_LastPathReplanTime >= c_ReplanCooldownSec)
				{
					m_PendingPathReplan = true;
					ReplanTriggered?.Invoke($"path replan queued attempt {m_PathReplanAttempts + 1}");
				}
				else if (m_PathReplanAttempts >= c_MaxPathReplanAttempts)
				{
					if (m_ReplanExhaustPos == Vector3.zero)
						m_ReplanExhaustPos = fb.Position;

					NoteTrajectoryProgress(fb);
					Outcome = NavigationOutcome.NoFeasibleManeuver;
					CurrentState = State.Idle;
					m_TrajectoryTracker.Deactivate();
					return m_Motion.BrakeToStop(false, StopIntent.Goal);
				}
				else if (DebugLog && m_PathReplanAttempts >= c_MaxPathReplanAttempts)
					Debug.LogWarning("[DriverFSM] Path replan exhausted — braking");
				return m_Motion.BrakeToStop(false, StopIntent.Goal);
			}

			if (output.NeedHeadingReplan && m_ActiveGoal.RequiresPosePlanning)
			{
				if (m_HeadingReplanAttempts < c_MaxHeadingReplans && !m_PendingHeadingReplan &&
				    Time.time - m_LastPathReplanTime >= c_ReplanCooldownSec)
				{
					m_PendingHeadingReplan = true;
					ReplanTriggered?.Invoke($"heading replan queued attempt {m_HeadingReplanAttempts + 1}");
				}
				else if (m_HeadingReplanAttempts >= c_MaxHeadingReplans)
				{
					if (!m_HeadingShuffleActive &&
					    m_Ctx.Request.RequiresExplicitHeadingArrival)
					{
						m_HeadingShuffleActive = true;
						m_PrecisionArrival.BeginHeadingShuffle(fb.Position);
					}

					if (m_HeadingShuffleActive &&
					    m_Ctx.Request.RequiresExplicitHeadingArrival)
					{
						if (m_PrecisionArrival.IsHeadingShuffleExhausted)
						{
							Outcome = NavigationOutcome.NoFeasibleManeuver;
							CurrentState = State.Idle;
							m_TrajectoryTracker.Deactivate();
							m_HeadingShuffleActive = false;
							return m_Motion.BrakeToStop(false, StopIntent.Goal);
						}

						var shuffleCmd = m_PrecisionArrival.TickHeadingShuffle(
							fb.Position, fb.Yaw, fb.SpeedKmh, m_ActiveGoal, m_Ctx.Params);
						return m_Motion.Convert(m_Ctx, shuffleCmd);
					}

					Outcome = NavigationOutcome.NoFeasibleManeuver;
					CurrentState = State.Idle;
					m_TrajectoryTracker.Deactivate();
					return m_Motion.BrakeToStop(false, StopIntent.Goal);
				}
				else if (DebugLog)
					Debug.LogWarning("[DriverFSM] Heading replan exhausted — holding at position");
				return m_Motion.BrakeToStop(false, StopIntent.Goal);
			}

			if (output.WaitingForStop)
				return m_Motion.Convert(m_Ctx, output.Command);

			if (output.IsComplete && TryValidateGoal(fb))
			{
				if (DebugLog)
					Debug.Log($"[DriverFSM] Trajectory COMPLETE → Holding dist={output.DistanceToEnd:F2}");
				return EnterSucceededHolding();
			}

			if (TryValidateGoal(fb))
				return EnterSucceededHolding();

			// Terminal brake only when tracker asks to stop (not during settle creep).
			if (output.RequestTerminalBrake &&
			    Mathf.Abs(output.Command.DesiredSpeedKmh) < 0.05f)
				return m_Motion.BrakeToStop(false, StopIntent.Goal);

			return m_Motion.Convert(m_Ctx, output.Command);
		}

		private VehicleCommand ExecuteManeuver(Maneuver _maneuver)
		{
			if (_maneuver is ReverseIntentManeuver revMvr)
				return ExecuteReverseManeuver(revMvr);

			// TurnAround facing the goal: hand off to local pose for the remaining approach.
			if (_maneuver is TurnAroundManeuver)
			{
				FeedbackState taFb = m_Ctx.State;
				float remain = FlatDistance(taFb.Position, m_Ctx.Request.Destination);
				float align = TravelAlignmentDeg(taFb, m_Ctx.Request.Destination);
				if (remain > m_ActiveGoal.PositionTolerance && align < 35f)
				{
					m_ForceLocalHandoff = true;
					m_PlanDirty = true;
					m_LocalHandoffDeferred = false;
					ReplanTriggered?.Invoke("turnaround aligned → local handoff");
					RebuildPlan();
					if (CurrentState == State.FollowingTrajectory)
						return TickTrajectory();
					if (CurrentState == State.Planning)
						return m_Motion.HoldInPlace();
				}
			}

			// Precision arrival: when close to goal, replace PursuitController with local error controller
			if (_maneuver.IsArrivalManeuver && ShouldUsePrecisionArrival())
			{
				m_PrecisionArrival.Activate();
				return TickPrecisionArrival();
			}

			m_PrecisionArrival.Deactivate();

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

			if (CurrentState == State.Driving && ShouldApplyHandoffSpeedCeiling(m_Ctx.State, out float handoffCeiling))
				finalSpeed = Mathf.Min(finalSpeed, handoffCeiling);

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

				float speedFraction = VehicleSpeedModeUtil.Fraction(m_Ctx.Request.SpeedMode);
				speedFraction = Mathf.Max(speedFraction, 0.6f);

				if (m_DriverCtx != null)
				{
					m_DriverCtx.UpdateFrom(m_Ctx.State, m_Ctx.Params, m_Ctx.Request, m_Ctx.Path);
					m_ReverseDriver.TryStart(m_DriverCtx, speedFraction);
				}

				if (DebugLog)
					Debug.Log($"[DriverFSM] Reverse START — pos=({m_Ctx.State.Position.x:F2},{m_Ctx.State.Position.z:F2}) " +
						$"fwd=({m_Ctx.State.Forward.x:F2},{m_Ctx.State.Forward.z:F2}) " +
						$"dest=({m_Ctx.Request.Destination.x:F2},{m_Ctx.Request.Destination.z:F2}) " +
						$"speedFrac={speedFraction:F2}");
			}

			if (m_DriverCtx != null)
				m_DriverCtx.UpdateFrom(m_Ctx.State, m_Ctx.Params, m_Ctx.Request, m_Ctx.Path);

			return m_ReverseDriver.Tick(Time.fixedDeltaTime);
		}

		private bool ShouldUsePrecisionArrival()
		{
			if (!m_Ctx.HasRequest) return false;
			FeedbackState fb = m_Ctx.State;
			Vector3 toGoal = m_Ctx.Request.Destination - fb.Position;
			toGoal.y = 0f;
			float dist = toGoal.magnitude;
			if (dist >= m_PrecisionArrival.ActivationDistance || m_GoalLocked)
				return false;

			float travelAngle = dist > 0.01f
				? Mathf.Abs(Vector3.SignedAngle(fb.Forward, toGoal / dist, Vector3.up))
				: 0f;
			float headingError = m_Ctx.Request.HasHeading
				? Mathf.Abs(Mathf.DeltaAngle(fb.Yaw, m_Ctx.Request.HeadingYaw.Value))
				: 0f;

			// Do not replace a planned staging/arc trajectory with a point seeker
			// while the vehicle is still strongly misaligned.
			bool alignedForward = travelAngle <= 30f && headingError <= 25f;
			bool alignedReverse = travelAngle >= 150f && headingError <= 25f;
			return alignedForward || alignedReverse;
		}

		private VehicleCommand TickPrecisionArrival()
		{
			FeedbackState fb = m_Ctx.State;
			float? heading = m_Ctx.Request.HasHeading ? m_Ctx.Request.HeadingYaw : (float?)null;

			var output = m_PrecisionArrival.Tick(
				fb.Position, fb.Forward, fb.Yaw, fb.SpeedKmh,
				m_Ctx.Request.Destination, heading,
				m_Ctx.Params, Time.fixedDeltaTime);

			m_Ctx.RemainingDistance = output.DistanceToGoal;
			m_Ctx.CurrentCurvature = output.Command.DesiredCurvature;
			m_Ctx.DesiredSpeedKmh = Mathf.Abs(output.Command.DesiredSpeedKmh);

			if (output.IsComplete && TryValidateGoal(fb))
			{
				m_PrecisionArrival.Deactivate();
				if (DebugLog)
					Debug.Log($"[DriverFSM] PrecisionArrival COMPLETE → Holding. dist={output.DistanceToGoal:F2}m speed={fb.SpeedKmh:F1}");
				return EnterSucceededHolding();
			}

			return m_Motion.Convert(m_Ctx, output.Command);
		}

		private VehicleCommand TickArrival()
		{
			FeedbackState fb = m_Ctx.State;
			ArrivalCriteria criteria = ArrivalCriteria.FromRequest(m_Ctx.Request);
			float? heading = m_Ctx.Request.HasHeading ? m_Ctx.Request.HeadingYaw : (float?)null;

			// Latch: already arrived — hold position, don't replan
			if (m_GoalLocked)
				return m_Motion.HoldInPlace();

			float dist = FlatDistance(fb.Position, m_Ctx.Request.Destination);

			if (m_Arrival.HasArrived(fb.Position, fb.Yaw, m_Ctx.Request.Destination, heading) &&
			    TryValidateGoal(fb))
			{
				if (DebugLog)
					Debug.Log($"[DriverFSM] GOAL LOCKED at dist={dist:F2}m speed={fb.SpeedKmh:F1}");
				return EnterSucceededHolding();
			}

			// Guard: very close but too fast — force crawl
			if (dist < 1.5f && fb.SpeedKmh > 5f)
			{
				if (DebugLog)
					Debug.Log($"[DriverFSM] Arrival crawl: dist={dist:F2}m speed={fb.SpeedKmh:F1} — forcing slow");
				return m_Motion.BrakeToStop(_hard: false);
			}

			// Intermediate: close but not stopped — cap speed aggressively
			if (dist < 0.6f && fb.SpeedKmh > 2f)
			{
				if (DebugLog)
					Debug.Log($"[DriverFSM] Arrival slow-cap: dist={dist:F2}m speed={fb.SpeedKmh:F1}");
				return m_Motion.BrakeToStop(_hard: false);
			}

			// Final latch: very close + slow → park only if heading also ok
			if (dist < 0.5f && fb.SpeedKmh < 1f && TryValidateGoal(fb))
			{
				if (DebugLog)
					Debug.Log($"[DriverFSM] Final latch → Holding: dist={dist:F2}m speed={fb.SpeedKmh:F1}");
				return EnterSucceededHolding();
			}

			// Precision arrival: within activation range, use local error controller
			if (ShouldUsePrecisionArrival())
			{
				m_PrecisionArrival.Activate();
				var output = m_PrecisionArrival.Tick(
					fb.Position, fb.Forward, fb.Yaw, fb.SpeedKmh,
					m_Ctx.Request.Destination, heading,
					m_Ctx.Params, Time.fixedDeltaTime);

				m_Ctx.RemainingDistance = output.DistanceToGoal;
				m_Ctx.CurrentCurvature = output.Command.DesiredCurvature;
				m_Ctx.DesiredSpeedKmh = Mathf.Abs(output.Command.DesiredSpeedKmh);

				if (output.IsComplete && TryValidateGoal(fb))
				{
					m_PrecisionArrival.Deactivate();
					if (DebugLog)
						Debug.Log($"[DriverFSM] PrecisionArrival COMPLETE → Holding. dist={output.DistanceToGoal:F2}m");
					return EnterSucceededHolding();
				}

				return m_Motion.Convert(m_Ctx, output.Command);
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
					if (TryValidateGoal(fb))
						return EnterSucceededHolding();
					CurrentState = State.Idle;
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
					GetCompletionDistance(current),
					current.AllowReverse && _fb.IsReversing);
				isComplete = current.IsComplete(ctx);
			}

			if (isComplete)
			{
				if (current is TurnAroundManeuver)
				{
					float remain = FlatDistance(_fb.Position, m_Ctx.Request.Destination);
					if (remain > m_ActiveGoal.PositionTolerance)
					{
						m_ForceLocalHandoff = true;
						m_PlanDirty = true;
						m_LocalRetryExpanded = false;
						m_LocalHandoffDeferred = false;
						ReplanTriggered?.Invoke("turnaround complete → local handoff");
						ManeuverFinished?.Invoke(current);
						m_Ctx.CurrentManeuverIndex++;
						RebuildPlan();
						return true;
					}
				}

				// Reverse failed — rebuild plan from current position instead of advancing
				if (current is ReverseIntentManeuver && m_ReverseDriver != null && m_ReverseDriver.CurrentState == ReverseState.Failed)
				{
					m_ReverseDriver = null;
					m_PlanDirty = true;
					ReplanCount++;
					ReplanTriggered?.Invoke("reverse failed — replan");
					if (DebugLog) Debug.LogWarning($"[DriverFSM] Reverse FAILED at pos=({_fb.Position.x:F2},{_fb.Position.z:F2}) " +
						$"dest=({m_Ctx.Request.Destination.x:F2},{m_Ctx.Request.Destination.z:F2}) " +
						$"dist={FlatDistance(_fb.Position, m_Ctx.Request.Destination):F2}m — triggering replan");
					return false;
				}

				if (current is ReverseIntentManeuver && DebugLog)
					Debug.Log($"[DriverFSM] Reverse COMPLETED at pos=({_fb.Position.x:F2},{_fb.Position.z:F2}) " +
						$"dest=({m_Ctx.Request.Destination.x:F2},{m_Ctx.Request.Destination.z:F2}) " +
						$"dist={FlatDistance(_fb.Position, m_Ctx.Request.Destination):F2}m");

				ManeuverFinished?.Invoke(current);
				m_Ctx.CurrentManeuverIndex++;
				Maneuver next = m_Ctx.CurrentManeuver;
				if (next != null)
					ManeuverStarted?.Invoke(next);
				return true;
			}

			return false;
		}

		private void InjectReverseEscape(FeedbackState _fb, float _distance)
		{
			Vector3 back = _fb.Position - _fb.Forward.normalized * _distance;
			var path = new PathResult(new[] { _fb.Position, back }, _distance, true, false);
			m_DriverCtx = new DriverContext();
			m_DriverCtx.UpdateFrom(_fb, m_Ctx.Params, m_Ctx.Request, path);
			var reversePath = ReversePathBuilder.Build(path, m_DriverCtx);
			var maneuvers = new System.Collections.Generic.List<Maneuver>
			{
				new ReverseIntentManeuver(reversePath)
			};
			m_Ctx.Plan = new DrivingPlan(maneuvers, "recovery reverse out", VehicleDrivingMode.Reverse);
			m_Ctx.CurrentManeuverIndex = 0;
			m_PlanDirty = false;
			CurrentState = State.Driving;
		}

		private float GetCompletionDistance(Maneuver _maneuver)
		{
			if (_maneuver == null)
				return 0.5f;

			switch (_maneuver.Type)
			{
				case VehicleManeuverType.Forward:
				case VehicleManeuverType.Reverse:
					return 0.8f;
				case VehicleManeuverType.TurnAround:
				case VehicleManeuverType.ThreePointTurn:
				case VehicleManeuverType.PostTurnAlignment:
					return 0.6f;
				case VehicleManeuverType.Parking:
				case VehicleManeuverType.ApproachWithHeading:
					return 0.5f;
				default:
					return 0.5f;
			}
		}

		private bool TryValidateGoal(FeedbackState _fb)
		{
			if (!m_Ctx.HasRequest)
				return false;

			// Several FSM branches may ask for goal validity during the same
			// physics tick. Accumulate stable time exactly once per tick.
			if (m_LastGoalValidationTick == m_TickSequence)
				return m_GoalValidThisTick;

			float goalYaw = m_Ctx.Request.HasHeading
				? m_Ctx.Request.HeadingYaw.Value
				: _fb.Yaw;
			bool requireHeading = m_Ctx.Request.RequiresExplicitHeadingArrival;

			m_GoalValidThisTick = m_GoalValidator.Evaluate(
				_fb.Position, _fb.Yaw, _fb.SpeedKmh,
				m_Ctx.Request.Destination, goalYaw,
				requireHeading,
				m_GoalCriteria, Time.fixedDeltaTime,
				out _, out _);
			m_LastGoalValidationTick = m_TickSequence;
			return m_GoalValidThisTick;
		}

		private void UpdateProgressSnapshot(FeedbackState _fb)
		{
			float goalYaw = m_Ctx.Request.HasHeading && m_Ctx.Request.HeadingYaw.HasValue
				? m_Ctx.Request.HeadingYaw.Value
				: _fb.Yaw;
			float yawErr = Mathf.Abs(Mathf.DeltaAngle(_fb.Yaw, goalYaw));
			float dist = FlatDistance(_fb.Position, m_Ctx.Request.Destination);

			LastProgress = new NavigationProgressSnapshot(
				m_Ctx.RemainingDistance,
				dist,
				yawErr,
				0f,
				m_Ctx.CurrentManeuverIndex,
				m_PathRevision,
				ReplanCount,
				m_Ctx.Plan.DrivingMode,
				m_Ctx.Plan.Reason,
				Outcome == NavigationOutcome.None ? string.Empty : Outcome.ToString(),
				StagnationKind.None);
		}

		private static float FlatDistance(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f; _b.y = 0f;
			return Vector3.Distance(_a, _b);
		}

		private GoalPose ClampActiveGoalTolerance(GoalPose _goal)
		{
			// Oval accept band in vehicle frame: tight along chassis, wider sideways.
			float lon = m_GoalCriteria.LongitudinalTolerance > 0f
				? m_GoalCriteria.LongitudinalTolerance
				: _goal.LongitudinalTolerance;
			float lat = m_GoalCriteria.LateralTolerance > 0f
				? m_GoalCriteria.LateralTolerance
				: _goal.LateralTolerance;
			lon = Mathf.Clamp(lon, 0.08f, 0.2f);
			lat = Mathf.Clamp(lat, 0.25f, 0.6f);
			if (Mathf.Abs(lon - _goal.LongitudinalTolerance) < 0.001f &&
			    Mathf.Abs(lat - _goal.LateralTolerance) < 0.001f)
				return _goal;

			return new GoalPose(
				_goal.Position,
				_goal.RequiresPosePlanning ? _goal.YawDegrees : (float?)null,
				_goal.HeadingSource,
				lon,
				lat,
				_goal.HeadingToleranceDeg);
		}

		/// <summary>Planner search tolerance (wider); execution/validator stay on m_ActiveGoal.</summary>
		private GoalPose CreatePlanningGoal()
		{
			float searchTol = Mathf.Max(c_PlanSearchPositionTolerance, m_ActiveGoal.PositionTolerance);
			if (Mathf.Approximately(searchTol, m_ActiveGoal.PositionTolerance))
				return m_ActiveGoal;

			return new GoalPose(
				m_ActiveGoal.Position,
				m_ActiveGoal.RequiresPosePlanning ? m_ActiveGoal.YawDegrees : (float?)null,
				m_ActiveGoal.HeadingSource,
				searchTol,
				m_ActiveGoal.HeadingToleranceDeg);
		}

		private VehicleCommand EnterSucceededHolding()
		{
			m_GoalLocked = true;
			m_TerminalCapture = TerminalCapturePhase.None;
			m_TerminalReacquireUsed = false;
			m_PendingPathReplan = false;
			m_PendingHeadingReplan = false;
			Outcome = NavigationOutcome.Succeeded;
			CurrentState = State.Holding;
			m_Ctx.ActiveStopReason = StopReason.Goal;
			m_TrajectoryTracker.Deactivate();
			return m_Motion.Park();
		}

		private bool UpdateTerminalCapture(FeedbackState _fb, out VehicleCommand _cmd)
		{
			_cmd = default;
			float dist = FlatDistance(_fb.Position, m_Ctx.Request.Destination);
			float lonTol = m_GoalCriteria.LongitudinalTolerance > 0f
				? m_GoalCriteria.LongitudinalTolerance
				: m_ActiveGoal.LongitudinalTolerance;
			float latTol = m_GoalCriteria.LateralTolerance > 0f
				? m_GoalCriteria.LateralTolerance
				: m_ActiveGoal.LateralTolerance;
			bool inStrict = ArrivalPositionBand.IsInside(
				_fb.Position, _fb.Yaw, m_Ctx.Request.Destination, lonTol, latTol);
			bool trackerBrake = m_TrajectoryTracker.HasTrajectory &&
			                    m_TrajectoryTracker.LastOutput.RequestTerminalBrake;

			if (inStrict || trackerBrake || m_TerminalCapture != TerminalCapturePhase.None)
			{
				m_PendingPathReplan = false;
				m_PendingHeadingReplan = false;
			}

			if (inStrict)
			{
				m_TerminalCapture = TerminalCapturePhase.BrakingCaptured;
				if (TryValidateGoal(_fb))
				{
					_cmd = EnterSucceededHolding();
					return true;
				}

				_cmd = m_Motion.BrakeToStop(false, StopIntent.Goal);
				return true;
			}

			if (m_TerminalCapture == TerminalCapturePhase.None)
				return false;

			if (dist > c_TerminalCaptureRelease)
			{
				m_TerminalCapture = TerminalCapturePhase.None;
				m_TerminalReacquireUsed = false;
				return false;
			}

			if (m_TerminalCapture == TerminalCapturePhase.BrakingCaptured)
			{
				bool stopped = Mathf.Abs(_fb.SpeedKmh) <= Mathf.Max(1f, m_GoalCriteria.MaxSpeedKmh);
				if (stopped &&
				    dist <= c_TerminalReacquireRadius &&
				    !m_TerminalReacquireUsed)
				{
					m_TerminalReacquireUsed = true;
					m_TerminalCapture = TerminalCapturePhase.Reacquiring;
					m_TrajectoryTracker.Deactivate();
					m_PlanDirty = true;
					m_LocalRetryExpanded = false;
					ReplanCount++;
					ReplanTriggered?.Invoke("terminal reacquire");
					RebuildPlan();
					if (CurrentState == State.FollowingTrajectory)
					{
						_cmd = TickTrajectory();
						return true;
					}

					_cmd = m_Motion.BrakeToStop(false, StopIntent.Goal);
					return true;
				}

				_cmd = m_Motion.BrakeToStop(false, StopIntent.Goal);
				return true;
			}

			// Reacquiring: follow the one correction path; do not queue more near-goal replans.
			return false;
		}

		private static float TravelAlignmentDeg(FeedbackState _fb, Vector3 _dest)
		{
			Vector3 toGoal = _dest - _fb.Position;
			toGoal.y = 0f;
			if (toGoal.sqrMagnitude < 0.01f)
				return 0f;
			float travelYaw = Quaternion.LookRotation(toGoal.normalized, Vector3.up).eulerAngles.y;
			return Mathf.Abs(Mathf.DeltaAngle(_fb.Yaw, travelYaw));
		}

		private float PickTurnSign(FeedbackState _fb)
		{
			Vector3 toDest = m_Ctx.Request.Destination - _fb.Position;
			toDest.y = 0f;
			if (toDest.sqrMagnitude < 0.01f)
				return 1f;

			float cross = Vector3.Cross(_fb.Forward, toDest.normalized).y;
			if (Mathf.Abs(cross) > 0.05f)
				return cross >= 0f ? 1f : -1f;

			if (_fb.Geometry.LeftClearance >= _fb.Geometry.RightClearance)
				return -1f;
			return 1f;
		}

		private float HandoffSpeedCeilingKmh(float _dist)
		{
			float decel = Mathf.Max(1.2f, m_Ctx.Params.ComfortBrakeDecelMs2);
			float v = Mathf.Sqrt(2f * decel * Mathf.Max(0f, _dist - 0.5f)) * 3.6f;
			return Mathf.Clamp(Mathf.Max(c_HandoffMinSpeedKmh, v), c_HandoffMinSpeedKmh, c_HandoffMaxSpeedKmh);
		}

		private void EnsureGlobalPathForHeading(FeedbackState fb)
		{
			if (m_Ctx.Request.HeadingSource == GoalHeadingSource.RequiredExplicit)
				return;

			if (m_Ctx.Path.IsValid)
				return;

			m_Ctx.Path = m_PathPlanner.BuildSafePath(
				fb.Position,
				m_Ctx.Request.Destination,
				m_Ctx.Params.Kinematics != null ? m_Ctx.Params.Kinematics.NavAgentRadius : 1.5f);
			if (!m_Ctx.Path.IsValid)
			{
				m_Ctx.Path = m_PathPlanner.BuildPath(
					fb.Position + fb.Forward * 0.5f,
					m_Ctx.Request.Destination,
					PathBuildOptions.SafeOnly);
			}
		}

		private void ResolveImplicitHeading(FeedbackState fb)
		{
			if (m_Ctx.Request.HeadingSource == GoalHeadingSource.RequiredExplicit)
				return;

			if (m_Ctx.Path.IsValid && m_Ctx.Path.TryGetLastSegmentTangent(out float yaw))
			{
				m_Ctx.Request = m_Ctx.Request.WithPathTangentHeading(yaw);
				return;
			}

			Vector3 toGoal = m_Ctx.Request.Destination - fb.Position;
			toGoal.y = 0f;
			if (toGoal.sqrMagnitude > 0.01f)
			{
				float fallbackYaw = Quaternion.LookRotation(toGoal.normalized, Vector3.up).eulerAngles.y;
				m_Ctx.Request = m_Ctx.Request.WithPathTangentHeading(fallbackYaw);
			}
		}

		private void NoteTrajectoryProgress(FeedbackState fb)
		{
			float dist = FlatDistance(fb.Position, m_Ctx.Request.Destination);
			if (dist + 0.5f < m_BestConfirmedGoalDist)
			{
				m_BestConfirmedGoalDist = dist;
				if (m_PathReplanAttempts > 0)
				{
					m_PathReplanAttempts = 0;
					m_ReplanExhaustPos = Vector3.zero;
				}
			}
		}

		private static VehicleDrivingMode ResolveTrajectoryDrivingMode(VehicleTrajectory _traj)
		{
			if (_traj == null || !_traj.IsValid || _traj.PointCount == 0)
				return VehicleDrivingMode.Forward;

			if (_traj.GearSegmentCount > 1)
				return _traj.Points[0].Gear == TrajectoryGear.Reverse
					? VehicleDrivingMode.Reverse
					: VehicleDrivingMode.Forward;

			return _traj.Points[0].Gear == TrajectoryGear.Reverse
				? VehicleDrivingMode.Reverse
				: VehicleDrivingMode.Forward;
		}

		private static int ComputeTrajectoryHash(VehicleTrajectory traj)
		{
			if (traj == null || !traj.IsValid || traj.PointCount == 0)
				return 0;

			var p0 = traj.Points[0];
			var pN = traj.Points[traj.PointCount - 1];
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + p0.Position.GetHashCode();
				hash = hash * 31 + pN.Position.GetHashCode();
				hash = hash * 31 + Mathf.RoundToInt(traj.TotalLength * 100f);
				hash = hash * 31 + traj.GearSegmentCount;
				hash = hash * 31 + (traj.DebugReason != null ? traj.DebugReason.GetHashCode() : 0);
				return hash;
			}
		}

		private bool ShouldApplyHandoffSpeedCeiling(FeedbackState _fb, out float _ceilingKmh)
		{
			_ceilingKmh = float.MaxValue;
			if (m_Settings == null || !m_Settings.UseLocalPosePlanner || m_TrajectoryTracker.HasTrajectory)
				return false;

			float dist = FlatDistance(_fb.Position, m_Ctx.Request.Destination);
			float handoff = m_Settings.LocalPlanningDistance > 0f
				? m_Settings.LocalPlanningDistance
				: 15f;
			if (dist > handoff)
				return false;

			_ceilingKmh = HandoffSpeedCeilingKmh(dist);
			return true;
		}

		private bool UpdateDivergenceWatchdog(FeedbackState _fb, float _dist)
		{
			if (_dist < m_BestGoalDist - 0.05f)
			{
				m_BestGoalDist = _dist;
				m_DivergeTimer = 0f;
				return false;
			}

			if (_dist > m_BestGoalDist + c_DivergeGrowth)
				m_DivergeTimer += Time.fixedDeltaTime;
			else
				m_DivergeTimer = 0f;

			return m_DivergeTimer >= c_DivergeTimeout;
		}

		private void HandleDivergenceWatchdog(FeedbackState _fb)
		{
			float dist = FlatDistance(_fb.Position, m_Ctx.Request.Destination);
			m_TrajectoryTracker.Deactivate();
			m_LocalHandoffDeferred = false;
			m_LocalHandoffDeferredDist = -1f;
			m_PlanDirty = true;
			m_DivergeTimer = 0f;
			m_BestGoalDist = dist;
			ReplanCount++;
			ReplanTriggered?.Invoke("divergence watchdog");
			RebuildPlan();

			if (CurrentState != State.Driving)
				return;

			float align = TravelAlignmentDeg(_fb, m_Ctx.Request.Destination);
			if (align >= c_TurnAroundAlignDeg &&
			    m_Ctx.CurrentManeuver != null &&
			    m_Ctx.CurrentManeuver.Type == VehicleManeuverType.Forward)
			{
				Outcome = NavigationOutcome.NoFeasibleManeuver;
				CurrentState = State.Idle;
			}
		}
	}
}
