using System;
using System.Collections.Generic;
using CombatVehicleSystem;
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

		private bool m_HasDestination;
		private bool m_IsStopped;
		private DriverFSM.State m_LastLoggedState;
		private VehicleCommand m_LastLoggedCommand;
		private float m_LastNavThr;
		private VehicleBrakeMode m_LastNavBrk;
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
				m_FSM.PathChanged -= OnFsmPathChanged;
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
			VehicleCommand command = m_FSM.Tick();

			// Trace who writes throttle: log frame + FSM state
			if (Mathf.Abs(command.Throttle - m_LastNavThr) > 0.02f || command.BrakeMode != m_LastNavBrk)
				Debug.Log($"[NavTrace] f={Time.frameCount} st={m_FSM.CurrentState} thr={command.Throttle:F2} brk={command.BrakeMode}");
			m_LastNavThr = command.Throttle;
			m_LastNavBrk = command.BrakeMode;

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
					$"[VehicleNav:{name}] state={m_FSM.CurrentState} cmd=thr{command.Throttle:F2}/ste{command.Steer:F2}/brk{command.BrakeMode} " +
					$"rev={IsReversing} plan={m_Ctx.Plan.Reason} dist={m_Ctx.RemainingDistance:F1}m curv={m_Ctx.CurrentCurvature:F3}",
					this);
				m_LastLoggedState = m_FSM.CurrentState;
				m_LastLoggedCommand = command;
			}

			if (m_FSM.CurrentState == DriverFSM.State.Idle && m_Ctx.HasRequest)
			{
				m_HasDestination = false;
				DestinationReached?.Invoke();
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
			EnsureSettings();
			NavigationRequest request = _goal.HasHeading
				? NavigationRequest.FromPositionAndHeading(_goal.Position, _goal.HeadingYawDegrees, _goal.SpeedMode)
				: NavigationRequest.FromPosition(_goal.Position, _goal.SpeedMode);

			if (m_LogNavigation)
				Debug.Log($"[VehicleNav:{name}] SetDestination pos={request.Destination} speed={request.SpeedMode} heading={(request.HasHeading ? request.HeadingYaw.Value.ToString("F0") : "none")}", this);
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
			m_FSM?.Stop();
			ApplyCommand(VehicleCommand.SoftPark);
			m_HasDestination = false;
			m_IsStopped = true;
		}

		public void StopHard()
		{
			m_FSM?.EmergencyStop(StopReason.Player);
			m_HasDestination = false;
			m_IsStopped = false;
		}

		public void StopSoft()
		{
			m_FSM?.Stop();
			ApplyCommand(VehicleCommand.SoftPark);
			m_HasDestination = false;
			m_IsStopped = true;
		}

		public void RebuildLimiters()
		{
			m_FSM?.BuildLimiters(m_Brain != null ? m_Brain.Tuning : null);
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
				m_Settings.StuckTimeSeconds);

			VehicleParameters parameters = m_Brain != null && m_Brain.Tuning != null
				? VehicleParameters.FromTuning(m_Brain.Tuning)
				: VehicleParameters.Default;

			m_Ctx = new NavigationContext(parameters, new VehicleDriverMemory());
			m_Pursuit = new PursuitController(
				parameters.CurvatureSpeedCurve);
			m_Prediction = new TrajectoryPrediction(m_Settings.GeometryLayers);
			m_Motion = new MotionController(m_WheeledMotor, m_Brain);
			m_Arrival = new ArrivalController(
				m_Settings.ArrivalPositionTolerance,
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
			m_FSM.SetPrediction(m_Prediction);
			m_FSM.PathChanged += OnFsmPathChanged;

			m_FSM.BuildLimiters(m_Brain != null ? m_Brain.Tuning : null);
		}

		private void OnFsmPathChanged()
		{
			PathChanged?.Invoke();
		}

		private Vector3 GetLookAheadPoint()
		{
			Maneuver maneuver = CurrentManeuver;
			if (maneuver == null || maneuver.Waypoints.Count == 0)
				return Destination;

			return maneuver.Waypoints[Mathf.Min(maneuver.Waypoints.Count - 1, 1)];
		}

		private void EnsureSettings()
		{
			if (m_Settings != null)
				return;

			m_Settings = Resources.Load<VehicleNavigationSettings>("VehicleNavigationSettings");
			if (m_Settings != null)
				return;

			m_Settings = ScriptableObject.CreateInstance<VehicleNavigationSettings>();
			m_Settings.GeometryLayers = LayerMask.GetMask("Default", "Obstacle", "Vehicle");
		}

		private void ApplyCommand(VehicleCommand _command)
		{
			LastCommand = _command;
			IsReversing = _command.Throttle < -0.02f;
			if (m_Brain != null)
				m_Brain.SetCommand(_command);
		}
		#endregion
	}
}
