using System;
using System.Collections.Generic;
using CombatVehicleSystem;
#pragma warning disable CS0067, CS0414
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// MonoBehaviour entry point for the virtual driver. Replaces VehicleRtsPathFollower.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class VehicleNavigation : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private VehicleNavigationSettings m_Settings;
		[SerializeField] private VehicleController m_Vehicle;
		[SerializeField] private VehicleBrain m_Brain;
		[SerializeField] private WheeledMotor m_WheeledMotor;
		[SerializeField] private bool m_LogNavigation = true;
		#endregion

		#region Private Fields
		private PathPlanner m_PathPlanner;
		private DecisionEvaluator m_DecisionEvaluator;
		private DrivingPlanner m_DrivingPlanner;
		private PathSmoother m_PathSmoother;
		private ManeuverPlanner m_ManeuverPlanner;
		private FeedbackSystem m_FeedbackSystem;
		private PursuitController m_Pursuit;
		private MotionController m_Motion;
		private ArrivalController m_Arrival;
		private RecoveryController m_Recovery;
		private TrajectoryPrediction m_Prediction;
		private NavigationContext m_Ctx;
		private DriverFSM m_FSM;
		private Rigidbody m_Body;

		private VehicleOrderQueue m_OrderQueue;
		private VehicleSafetyController m_Safety;
		private ArrivalPlanner m_ArrivalPlanner;
		private bool m_QueueAutoAdvance = true;
		private bool m_HasDestination;
		private bool m_IsStopped;
		private bool m_TerminalEventSent;
		private DriverFSM.State m_LastLoggedState;
		private VehicleCommand m_LastLoggedCommand;
		#endregion

		#region Public Properties
		public bool HasDestination => m_HasDestination;
		public bool NeedsDriveSimulation => m_HasDestination && !m_IsStopped;
		public DriverFSM.State DriverState => m_FSM?.CurrentState ?? DriverFSM.State.Idle;
		public NavigationRequest ActiveRequest => m_Ctx?.Request ?? default;
		public PathResult ActivePath => m_Ctx?.Path ?? PathResult.Invalid;
		public DrivingPlan ActivePlan => m_Ctx?.Plan ?? DrivingPlan.Empty;
		public Maneuver CurrentManeuver => m_Ctx?.CurrentManeuver;
		public float ThrottleCommand { get; private set; }
		public float SteerCommand { get; private set; }
		public bool IsReversing { get; private set; }
		public VehicleCommand LastCommand { get; private set; }
		public VehicleNavigationSettings Settings => m_Settings;

		public Vector3 Destination => m_Ctx != null ? m_Ctx.Request.Destination : Vector3.zero;
		public bool HasGoalHeading => m_Ctx != null && m_Ctx.Request.HasHeading;
		public float GoalHeadingYaw => m_Ctx != null && m_Ctx.Request.HasHeading
			? m_Ctx.Request.HeadingYaw.Value
			: 0f;
		public IReadOnlyList<Vector3> PathCorners =>
			m_Ctx != null ? m_Ctx.Path.Corners as IReadOnlyList<Vector3> : null;
		public int ActiveCornerIndex => m_Ctx?.CurrentManeuverIndex ?? 0;
		public VehicleSpeedMode ActiveSpeedMode =>
			m_Ctx != null ? m_Ctx.Request.SpeedMode : VehicleSpeedMode.Medium;
		public bool HasLookAheadPoint => CurrentManeuver != null && CurrentManeuver.Waypoints.Count > 0;
		public Vector3 LookAheadPoint => GetLookAheadPoint();
		public float CurrentSpeed => m_Body != null ? m_Body.linearVelocity.magnitude : 0f;
		public string ActivePlanReason => m_Ctx?.Plan.Reason ?? "-";
		public NavigationContext Context => m_Ctx;
		public PursuitController.PursuitDebugInfo PursuitDebug => m_Pursuit?.LastDebugInfo ?? default;
		public bool IsStuck => m_Ctx?.State.IsStuck ?? false;
		public float StuckTimer => m_FeedbackSystem?.StuckTimerValue ?? 0f;
		public VehicleLocalGeometry.Sample Geometry => m_Ctx?.State.Geometry ?? default;
		public FeasibilityResult LastFeasibility => m_FSM?.LastFeasibility;
		public VehicleOrderQueue OrderQueue => m_OrderQueue;
		public NavigationOutcome NavigationOutcome => m_FSM?.Outcome ?? NavigationOutcome.None;
		public NavigationProgressSnapshot ProgressSnapshot => m_FSM?.LastProgress ?? NavigationProgressSnapshot.Empty;
		public VehicleKinematicsProfile KinematicsProfile { get; private set; }
		public VehicleTrajectory ActiveTrajectory => m_FSM?.ActiveTrajectory;
		public TrajectoryTracker.Output LastTrackerOutput =>
			m_FSM != null ? m_FSM.LastTrackerOutput : default;
		public bool TurnEntryGateActive => m_FSM != null && m_FSM.TurnEntryGateActive;
		public float PathYawAtIndex => m_FSM != null ? m_FSM.PathYawAtIndex : 0f;
		public LocalPosePlanner.PlanStats LastLocalPlanStats =>
			m_FSM != null ? m_FSM.LastLocalPlanStats : default;
		/// <summary>Physical WheelCollider steer in [-1,1] (after WheeledMotor rate-limit).</summary>
		public float ActualSteerNormalized =>
			m_WheeledMotor != null ? m_WheeledMotor.CurrentSteerNormalized : 0f;
		#endregion

		#region Events
		public event Action PathChanged;
		public event Action DestinationReached;
		public event Action DestinationFailed;
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			CacheComponents();
			BuildSystems();
		}

		private void OnDestroy()
		{
			if (m_FSM != null)
			{
				m_FSM.PathChanged -= OnFsmPathChanged;
				m_FSM.ManeuverStarted -= OnManeuverStarted;
				m_FSM.ManeuverFinished -= OnManeuverFinished;
				m_FSM.ReplanTriggered -= OnReplanTriggered;
			}
		}

		private void FixedUpdate()
		{
			if (m_Brain == null || !m_Brain.ControlActive)
			{
				ApplyCommand(VehicleCommand.Idle);
				return;
			}

			if (!m_Brain.CanDrive)
			{
				ApplyCommand(VehicleCommand.SoftPark);
				return;
			}

			float dt = Time.fixedDeltaTime;
			m_Ctx.State = m_FeedbackSystem.Update(dt, IsReversing);

			TryPromoteNextOrder(Time.time);

			VehicleCommand command = m_FSM.Tick();

			bool isRecovering = m_FSM.CurrentState == DriverFSM.State.Recovery;
			if (isRecovering || (m_Ctx.State.IsStuck))
				RequestWheelAntiStuckAssist();

			if (m_Safety != null)
			{
				var safetyResult = m_Safety.Apply(
					m_Ctx.State, m_Ctx.Params, command, dt,
					transform.eulerAngles, isRecovering);
				command = safetyResult.Command;

				if (safetyResult.ShouldAbortRecovery && isRecovering)
				{
					m_FSM.Stop();
					m_OrderQueue?.CancelAll("safety-abort-recovery");
					m_QueueAutoAdvance = false;
					if (m_LogNavigation)
						Debug.LogWarning($"[VehicleNav:{name}] Recovery aborted by safety: {safetyResult.Warning}", this);
				}
			}

			m_IsStopped = m_FSM.CurrentState == DriverFSM.State.Idle ||
			              m_FSM.CurrentState == DriverFSM.State.Holding;
			m_HasDestination = m_FSM.CurrentState != DriverFSM.State.Idle ||
			                   m_Ctx.HasRequest;

			ApplyCommand(command);
			ThrottleCommand = command.Throttle;
			SteerCommand = command.Steer;

			if (m_LogNavigation &&
			    (m_FSM.CurrentState != m_LastLoggedState ||
			     Mathf.Abs(command.Throttle - m_LastLoggedCommand.Throttle) > 0.04f ||
			     Mathf.Abs(command.Steer - m_LastLoggedCommand.Steer) > 0.06f ||
			     command.BrakeMode != m_LastLoggedCommand.BrakeMode))
			{
				Debug.Log(
					$"[VehicleNav:{name}] state={m_FSM.CurrentState} cmd=thr{command.Throttle:F2}/ste{command.Steer:F2}/brk{command.BrakeMode} phase={command.Phase} " +
					$"rev={IsReversing} plan={m_Ctx.Plan.Reason} dist={m_Ctx.RemainingDistance:F1}m curv={m_Ctx.CurrentCurvature:F3}",
					this);
				m_LastLoggedState = m_FSM.CurrentState;
				m_LastLoggedCommand = command;
			}

			bool fsmIdle = m_FSM.CurrentState == DriverFSM.State.Idle ||
			               m_FSM.CurrentState == DriverFSM.State.Holding;

			if (fsmIdle && !m_OrderQueue.HasCurrent && !m_TerminalEventSent)
			{
				m_HasDestination = false;
				if (m_FSM.Outcome == NavigationOutcome.Succeeded)
				{
					m_TerminalEventSent = true;
					DestinationReached?.Invoke();
				}
				else if (m_FSM.Outcome != NavigationOutcome.None &&
				         m_FSM.Outcome != NavigationOutcome.Cancelled &&
				         m_FSM.Outcome != NavigationOutcome.InProgress)
				{
					m_TerminalEventSent = true;
					DestinationFailed?.Invoke();
				}
			}
		}
		#endregion

		#region Public Methods
		public void Configure(VehicleController _vehicle, VehicleBrain _brain, WheeledMotor _motor)
		{
			m_Vehicle = _vehicle;
			m_Brain = _brain;
			m_WheeledMotor = _motor;
			CacheComponents();
			BuildSystems();
		}

		public void SetDestination(VehicleMoveGoal _goal)
		{
			m_OrderQueue?.Clear();
			m_QueueAutoAdvance = true;
			SetDestinationFromGoal(_goal);
		}

		private void SetDestinationFromGoal(VehicleMoveGoal _goal)
		{
			EnsureSettings();
			m_TerminalEventSent = false;
			NavigationRequest request = _goal.HasHeading
				? NavigationRequest.FromPositionAndHeading(_goal.Position, _goal.HeadingYawDegrees, _goal.SpeedMode)
				: NavigationRequest.FromPosition(_goal.Position, _goal.SpeedMode);

			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] SetDestination pos={request.Destination} speed={request.SpeedMode} heading={(request.HasHeading ? request.HeadingYaw.Value.ToString("F0") : "none")} source={request.HeadingSource}", this);
			m_FeedbackSystem.ResetStuckTimer();
			m_FSM.SetDestination(request);
			m_HasDestination = true;
			m_IsStopped = false;
		}

		public void SetDestination(Vector3 _worldPosition, VehicleSpeedMode _speedMode)
		{
			SetDestination(VehicleMoveGoal.FromPosition(_worldPosition, _speedMode));
		}

		public void SetDestination(Vector3 _worldPosition, float _headingYaw, VehicleSpeedMode _speedMode)
		{
			SetDestination(VehicleMoveGoal.FromPositionAndHeading(_worldPosition, _headingYaw, _speedMode));
		}

		public void Stop()
		{
			m_OrderQueue?.MarkCurrentOrderAborted();
			m_QueueAutoAdvance = false;
			m_FSM?.Stop();
			ApplyCommand(VehicleCommand.SoftPark);
			m_HasDestination = false;
			m_IsStopped = true;
		}

		public void StopHard()
		{
			m_OrderQueue?.CancelAll("hard-stop");
			m_QueueAutoAdvance = false;
			m_FSM?.EmergencyStop(StopReason.Player);
			m_HasDestination = false;
			m_IsStopped = false;
		}

		public void StopSoft()
		{
			m_OrderQueue?.MarkCurrentOrderAborted();
			m_QueueAutoAdvance = false;
			m_FSM?.Stop();
			ApplyCommand(VehicleCommand.SoftPark);
			m_HasDestination = false;
			m_IsStopped = true;
		}

		public void RebuildLimiters()
		{
			m_FSM?.BuildLimiters(m_Brain != null ? m_Brain.Tuning : null);
		}

		public void EnqueueOrder(VehicleMoveOrder _order)
		{
			if (_order == null)
				return;

			m_OrderQueue.Enqueue(_order);

			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] EnqueueOrder: {_order}", this);

			TryPromoteNextOrder(Time.time);
		}

		public void CancelAllOrders(string _reason)
		{
			m_OrderQueue?.CancelAll(_reason);
			m_FSM?.Stop();
			m_HasDestination = false;
			m_IsStopped = true;
			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] CancelAllOrders: {_reason}", this);
		}

		public void CancelCurrentOrder(string _reason)
		{
			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] CancelCurrentOrder: {_reason}", this);

			if (m_OrderQueue.HasCurrent)
				m_OrderQueue.MarkCurrentOrderAborted();

			m_OrderQueue.CancelCurrent(_reason);
			m_FSM?.Stop();
			m_HasDestination = false;
			m_IsStopped = true;

			TryPromoteNextOrder(Time.time);
		}

		public void SetDestinationFromOrder(VehicleMoveOrder _order)
		{
			if (_order == null)
				return;

			EnsureSettings();
			NavigationRequest request = NavigationRequest.FromOrder(_order);

			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] Order → Request: pos={request.Destination} speed={request.SpeedMode} heading={(request.HasHeading ? request.HeadingYaw.Value.ToString("F0") : "none")}", this);

			m_FeedbackSystem.ResetStuckTimer();
			m_FSM.SetDestination(request);
			m_HasDestination = true;
			m_IsStopped = false;
		}
		#endregion

		#region Private Methods
		private void CacheComponents()
		{
			if (m_Vehicle == null)
				TryGetComponent(out m_Vehicle);
			if (m_Brain == null)
				TryGetComponent(out m_Brain);
			if (m_WheeledMotor == null)
				TryGetComponent(out m_WheeledMotor);
			if (m_Body == null)
				TryGetComponent(out m_Body);
		}

		private void BuildSystems()
		{
			EnsureSettings();

			m_PathPlanner = new PathPlanner();
			m_DecisionEvaluator = new DecisionEvaluator();
			m_DrivingPlanner = new DrivingPlanner(m_DecisionEvaluator);
			m_PathSmoother = new PathSmoother();
			m_ManeuverPlanner = new ManeuverPlanner(m_PathSmoother);
			m_FeedbackSystem = new FeedbackSystem(
				transform,
				m_Body,
				m_WheeledMotor,
				m_Settings.GeometryLayers,
				m_Settings.VehicleWidth,
				m_Settings.StuckSpeedKmh,
				m_Settings.StuckTimeSeconds,
				0.35f,
				m_Settings.LightweightRuntimeProbes);

			KinematicsProfile = VehicleKinematicsProfile.FromVehicle(transform, m_Brain?.Tuning, m_Settings);

			VehicleParameters parameters = m_Brain != null && m_Brain.Tuning != null
				? VehicleParameters.FromTuning(m_Brain.Tuning, transform, m_Settings)
				: VehicleParameters.Default;

			m_ArrivalPlanner = new ArrivalPlanner(parameters.EffectiveTurnRadius);
			m_DrivingPlanner.SetArrivalPlanner(m_ArrivalPlanner);

			m_Ctx = new NavigationContext(parameters, new VehicleDriverMemory());
			m_OrderQueue = new VehicleOrderQueue();
			m_Pursuit = new PursuitController(
				parameters.CurvatureSpeedCurve);
			m_Prediction = new TrajectoryPrediction(m_Settings.GeometryLayers);
			ManeuverFeasibilityChecker feasibility = new ManeuverFeasibilityChecker(m_Prediction);
			m_DrivingPlanner.SetFeasibility(feasibility);
			m_Motion = new MotionController(m_WheeledMotor, m_Brain);
			m_Safety = new VehicleSafetyController(parameters, m_WheeledMotor);
			m_Arrival = new ArrivalController(
				m_Settings.ArrivalLongitudinalTolerance,
				m_Settings.ArrivalLateralTolerance,
				m_Settings.ArrivalHeadingTolerance);
			m_Recovery = new RecoveryController();
			m_FSM = new DriverFSM(
				m_Ctx,
				m_PathPlanner,
				m_DrivingPlanner,
				m_ManeuverPlanner,
				m_Pursuit,
				m_Motion,
				m_Arrival,
				m_Recovery,
				m_Settings);
			m_FSM.SetVehicleRoot(transform);
			m_FSM.SetPrediction(m_Prediction, feasibility);
			m_FSM.PathChanged += OnFsmPathChanged;
			m_FSM.ManeuverStarted += OnManeuverStarted;
			m_FSM.ManeuverFinished += OnManeuverFinished;
			m_FSM.ReplanTriggered += OnReplanTriggered;
			m_FSM.SetGoalCriteria(new GoalPoseCriteria(
				m_Settings.ArrivalLongitudinalTolerance,
				m_Settings.ArrivalLateralTolerance,
				m_Settings.ArrivalHeadingTolerance,
				m_Settings.ArrivalMaxSpeedKmh,
				m_Settings.ArrivalStableWindowSeconds));

			m_FSM.BuildLimiters(m_Brain != null ? m_Brain.Tuning : null);
		}

		private void OnManeuverStarted(Maneuver _maneuver)
		{
			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] ManeuverStarted: {_maneuver?.Type}", this);
		}

		private void OnManeuverFinished(Maneuver _maneuver)
		{
			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] ManeuverFinished: {_maneuver?.Type}", this);
		}

		private void OnReplanTriggered(string _reason)
		{
			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] Replan: {_reason}", this);
		}

		private void OnFsmPathChanged()
		{
			PathChanged?.Invoke();
		}

		private Vector3 GetLookAheadPoint()
		{
			// During local trajectory following, expose the real tracker look-ahead.
			if (DriverState == DriverFSM.State.FollowingTrajectory)
			{
				var trackerOut = LastTrackerOutput;
				if (ActiveTrajectory != null && ActiveTrajectory.IsValid)
					return trackerOut.LookAheadPoint;
			}

			var traj = ActiveTrajectory;
			if (traj != null && traj.IsValid && traj.PointCount > 1)
			{
				int idx = Mathf.Clamp(LastTrackerOutput.NearestIndex, 0, traj.PointCount - 1);
				if (idx <= 0)
					idx = Mathf.Min(traj.PointCount - 1, Mathf.Max(1, traj.PointCount / 4));
				return traj.Points[idx].Position;
			}

			Maneuver maneuver = CurrentManeuver;
			if (maneuver == null || maneuver.Waypoints.Count == 0)
				return Destination;

			return maneuver.Waypoints[Mathf.Min(maneuver.Waypoints.Count - 1, 1)];
		}

		private void TryPromoteNextOrder(float _timeNow)
		{
			if (m_OrderQueue == null)
				return;

			m_OrderQueue.RemoveExpiredOrders(_timeNow);

			if (m_OrderQueue.HasPendingInterrupt)
			{
				if (m_OrderQueue.TryPromoteInterrupt(_timeNow))
				{
					VehicleMoveOrder interrupt = m_OrderQueue.CurrentOrder;
					if (interrupt != null && interrupt.Type == VehicleOrderType.EmergencyStop)
					{
						m_FSM.EmergencyStop(StopReason.Player);
						m_QueueAutoAdvance = false;
					}
					else if (interrupt != null && interrupt.Type == VehicleOrderType.Stop)
					{
						m_FSM.Stop();
						m_QueueAutoAdvance = false;
					}
				}
				return;
			}

			bool fsmIdle = m_FSM.CurrentState == DriverFSM.State.Idle ||
			               m_FSM.CurrentState == DriverFSM.State.Holding;

			if (fsmIdle && m_OrderQueue.HasCurrent)
			{
				VehicleMoveOrder current = m_OrderQueue.CurrentOrder;
				if (current.State == OrderState.Executing)
					m_OrderQueue.MarkCurrentOrderCompleted();

				if (m_QueueAutoAdvance)
				{
					VehicleMoveOrder next = m_OrderQueue.PromoteNext(_timeNow);
					if (next != null)
					{
						SetDestinationFromOrder(next);
						return;
					}
				}
				return;
			}

			if (!m_OrderQueue.HasCurrent && m_QueueAutoAdvance)
			{
				VehicleMoveOrder next = m_OrderQueue.PromoteNext(_timeNow);
				if (next != null)
					SetDestinationFromOrder(next);
			}
		}

		private void EnsureSettings()
		{
			if (m_Settings != null)
				return;

			m_Settings = Resources.Load<VehicleNavigationSettings>("VehicleNavigationSettings");
			if (m_Settings != null)
				return;

			m_Settings = ScriptableObject.CreateInstance<VehicleNavigationSettings>();
			m_Settings.GeometryLayers = LayerMask.GetMask("Default", "Obstacle", "Vehicle", "Ground");
		}

		private void ApplyCommand(VehicleCommand _command)
		{
			LastCommand = _command;
			IsReversing = _command.Throttle < -0.02f;
			if (m_Brain != null)
				m_Brain.SetCommand(_command);
		}

		private void RequestWheelAntiStuckAssist()
		{
			WheelAntiStuck[] wheels = GetComponentsInChildren<WheelAntiStuck>(true);
			if (wheels == null)
				return;
			for (int i = 0; i < wheels.Length; i++)
				wheels[i]?.RequestAssist(1.25f);
		}
		#endregion
	}
}
