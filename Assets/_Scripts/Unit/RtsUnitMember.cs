using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// RTS-обёртка над существующими компонентами юнита:
/// регистрация в списке selectable-юнитов, групповые команды и состояние выделения.
/// </summary>
[DisallowMultipleComponent]
public sealed class RtsUnitMember : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponReloadController m_WeaponReloadController;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilizationController;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOtherController;
	[SerializeField] private UnitFiremanCarryController m_FiremanCarryController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[SerializeField] private Collider m_SelectionCollider;
	[SerializeField] private GameObject m_SelectionVisualRoot;
	[SerializeField] private bool m_DisableDirectInputForRts = true;
	[Header("Selection Name Label")]
	[SerializeField] private GameObject m_SelectionNameLabelRoot;
	[SerializeField] private TextMeshProUGUI m_SelectionNameText;
	[SerializeField, Min(0.1f)] private float m_SelectionLabelHeight = 2.2f;
	[Header("Animator Variation")]
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMin = 0.97f;
	[SerializeField, Range(0.85f, 1.15f)] private float m_MoveAnimatorSpeedMax = 1.03f;
	[SerializeField] private float m_RuntimeMoveAnimatorSpeed = 1f;
	[SerializeField] 	private bool m_IsSelected;
	private FormationType m_CurrentFormation;
	[Header("Route Corner Smoothing")]
	[SerializeField] private bool m_EnableRouteCornerSmoothing = true;
	[SerializeField, Min(0.1f)] private float m_CornerSmoothingMaxRadius = 2.5f;
	[SerializeField, Range(0.05f, 0.49f)] private float m_CornerSmoothingSegmentFraction = 0.35f;
	[SerializeField, Range(5f, 175f)] private float m_CornerSmoothingMinAngle = 12f;
	[SerializeField, Range(2, 24)] private int m_CornerSmoothingMaxSamples = 12;
	[SerializeField, Min(0.1f)] private float m_CornerSmoothingNavMeshSampleRadius = 1.5f;
	[SerializeField] private bool m_EnableCornerSmoothingMovement;
	[SerializeField, Min(0.2f)] private float m_ContinuousRouteLookaheadDistance = 1.25f;
	[Header("Segment Facing (ПКМ по отрезку маршрута)")]
	[SerializeField, Min(0.5f)] private float m_FacingTurnOverDistance = 5f;

	private static Material s_PathLineMaterial;
	private static readonly Vector3 s_PathLineYOffset = Vector3.up * 0.03f;
	private static readonly Color s_PathLineNormalColor = new Color(0.75f, 0.75f, 0.75f, 0.8f);
	private static readonly Color s_PathLinePreviewColor = new Color(0.75f, 0.75f, 0.75f, 0.35f);
	private const float c_PathLineNormalWidth = 0.06f;
	private const float c_PathLinePreviewWidth = 0.04f;
	private static readonly List<RtsUnitMember> s_Instances = new List<RtsUnitMember>(128);
	private Coroutine m_PendingCommandCoroutine;
	private int m_PendingCommandVersion;
	private UnitRosterDisplayState m_RosterDisplay;
	private Transform m_CachedCameraTransform;
	private LineRenderer m_PathLine;
	private bool m_HasActiveDestination;
	private bool m_IsMovePreviewVisualActive;
	private Vector3? m_MovePreviewDestination;
	private UnitClickToMove.MoveTier m_ActiveMoveTier = UnitClickToMove.MoveTier.Walk;
	private Vector3 m_ActiveRouteSegmentStart;
	private float m_DestinationSetTime = -1f;
	private bool m_HasWantedFacing;
	private float m_WantedFacingAngle;
	private bool m_IsRotatingToFacing;
	private float m_FacingRotateVelocity;
	private bool m_FacingSuppressedReady;
	private bool m_WasReadyBeforeFacing;
	private bool m_IsInFacingTurn;
	private FacingArrowMode m_FacingTurnMode;
	private Vector3 m_FacingTurnStartPos;
	private float m_FacingTurnStartAngle;
	private float m_FacingTurnTargetAngle;
	private float m_FacingTurnDistanceTraveled;
	private Vector3 m_FacingLookPoint;
	private FormationSyncGroup m_FormationSyncGroup;
	private bool m_HasFormationFacingAngle;
	private float m_FormationFacingAngle;
	private float m_FormationFrontFacingAngle;
	private bool m_HasLastArrivalMovementAngle;
	private float m_LastArrivalMovementAngle;
	private LineRenderer m_FormationFacingArrowLine;
	private readonly List<Vector3> m_Waypoints = new List<Vector3>();
	private float m_NextWaypointCheckTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
	private float m_RouteDebugNextStateLogTime;
	private bool m_RouteDebugLastSuppressEarlyStop;
#endif
	private readonly List<Vector3> m_SmoothingSubtargets = new List<Vector3>(16);
	private int m_SmoothingSubtargetIndex;
	private bool m_IsExecutingSmoothingArc;
	private bool m_PreferContinuousNextMoveOrder;
	private UnitClickToMove.MoveTier m_SmoothingMoveTier = UnitClickToMove.MoveTier.Walk;
	private readonly List<Vector3> m_RawRoutePoints = new List<Vector3>(32);
	private readonly List<Vector3> m_SmoothedRoutePoints = new List<Vector3>(64);
	private readonly List<Vector3> m_CornerArcSamples = new List<Vector3>(16);

	public enum FacingArrowMode
	{
		TurnOverDistance,
		HoldToEnd,
		LookAtPoint,
	}

	public readonly struct WaitPointDescriptor
	{
		public WaitPointDescriptor(int _waypointIndex, bool _isActiveWaypoint, Vector3 _worldPosition, int _waitGroup)
		{
			WaypointIndex = _waypointIndex;
			IsActiveWaypoint = _isActiveWaypoint;
			WorldPosition = _worldPosition;
			WaitGroup = _waitGroup;
		}

		public int WaypointIndex { get; }
		public bool IsActiveWaypoint { get; }
		public Vector3 WorldPosition { get; }
		public int WaitGroup { get; }
	}

	public readonly struct FacingArrowDescriptor
	{
		public FacingArrowDescriptor(
			int _segmentIndex,
			int _arrowIndex,
			bool _isActiveSegment,
			Vector3 _anchor,
			float _angle,
			FacingArrowMode _mode,
			Vector3 _lookPoint,
			bool _hasLookPoint)
		{
			SegmentIndex = _segmentIndex;
			ArrowIndex = _arrowIndex;
			IsActiveSegment = _isActiveSegment;
			Anchor = _anchor;
			Angle = _angle;
			Mode = _mode;
			LookPoint = _lookPoint;
			HasLookPoint = _hasLookPoint;
		}

		public int SegmentIndex { get; }
		public int ArrowIndex { get; }
		public bool IsActiveSegment { get; }
		public Vector3 Anchor { get; }
		public float Angle { get; }
		public FacingArrowMode Mode { get; }
		public Vector3 LookPoint { get; }
		public bool HasLookPoint { get; }
	}

	#endregion

	#region Private Types
	private struct QueuedCommand
	{
		public Vector3 Destination;
		public UnitClickToMove.MoveTier MoveTier;
		public List<FacingArrow> FacingArrows;
		public int WaitGroup;
		public bool WaitIconAtWaypoint;
		public bool HasWaitRouteBinding;
		public int WaitRouteSegmentIndex;
		public float WaitRouteSegmentT;
	}

	private struct FacingArrow
	{
		public float Angle;
		public FacingArrowMode Mode;
		public bool ForceReadyOnActivation;
		public bool ActivateAtSegmentStart;
		public bool HasLookPoint;
		public int RouteSegmentIndex;
		public float RouteSegmentT;
		public Vector3 LookOffsetFromAnchor;
	}

	private struct FacingArrowVisualSource
	{
		public LineRenderer Line;
		public bool IsActiveSegment;
		public int CommandIndex;
		public int ArrowIndex;
	}
	#endregion

	#region Public Properties

	private readonly List<QueuedCommand> m_CommandQueue = new List<QueuedCommand>();

	private static readonly Color s_FacingArrowColor = new Color(1f, 0.85f, 0.2f, 0.95f);
	private static readonly Color s_FacingArrowHoldColor = new Color(0.2f, 0.7f, 1f, 0.95f);
	private static readonly Color s_FacingArrowLookColor = new Color(0.3f, 0.95f, 0.3f, 0.95f);
	private static readonly Color s_FormationFacingArrowColor = new Color(0.72f, 0.38f, 1f, 0.92f);
	private static readonly Vector3 s_FacingArrowYOffset = Vector3.up * 0.05f;
	private readonly List<FacingArrowVisualSource> m_FacingArrowVisuals = new List<FacingArrowVisualSource>();
	private bool m_FacingArrowsDirty;
	private List<FacingArrow> m_ActiveFacingArrows;
	private int m_ActiveWaitGroup;
	private bool m_IsWaitingAtRouteGate;
	private const float FacingArrowActivationDistance = 1.5f;
	private const float c_FacingArrowShaftStartOffset = 0.15f;
	private const float c_FacingArrowFixedLength = 2f;
	#endregion

	#region Public Properties
	public static IReadOnlyList<RtsUnitMember> Instances => s_Instances;
	public CharacterInventory CharacterInventory => m_CharacterInventory;
	public bool IsSelected => m_IsSelected;
	public bool IsPlayerSelectable => m_Team != null && m_Team.Team == UnitTeamId.Player;
	public bool WantsReady => m_ReadyHands != null && m_ReadyHands.WantsReady;
	public bool HasQueuedCommands => m_CommandQueue.Count > 0;
	public bool HasActiveDestination => m_HasActiveDestination;
	public bool HasWantedFacing => m_HasWantedFacing;
	public bool IsWaitingAtRouteGate => m_IsWaitingAtRouteGate;
	public int ActiveWaitGroup => m_ActiveWaitGroup;
	public FormationType CurrentFormation { get => m_CurrentFormation; set => m_CurrentFormation = value; }
	public float FormationSpacing { get; set; } = 2f;
	public bool HasFormationFacingAngle => m_HasFormationFacingAngle;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_SelectionCollider == null)
			m_SelectionCollider = GetComponent<Collider>();

		m_RuntimeMoveAnimatorSpeed = UnityEngine.Random.Range(
			Mathf.Min(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax),
			Mathf.Max(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax));

		if (m_DisableDirectInputForRts)
			ApplyDirectInputState(false);

		CreatePathLine();
	}

	private void CreatePathLine()
	{
		if (s_PathLineMaterial == null)
		{
			s_PathLineMaterial = new Material(Shader.Find("Sprites/Default"));
			s_PathLineMaterial.hideFlags = HideFlags.HideAndDontSave;
		}

		GameObject lineGo = new GameObject("PathLine");
		lineGo.transform.SetParent(transform, false);
		m_PathLine = lineGo.AddComponent<LineRenderer>();
		m_PathLine.positionCount = 0;
		m_PathLine.startWidth = c_PathLineNormalWidth;
		m_PathLine.endWidth = c_PathLineNormalWidth;
		m_PathLine.sharedMaterial = s_PathLineMaterial;
		m_PathLine.startColor = s_PathLineNormalColor;
		m_PathLine.endColor = s_PathLineNormalColor;
		m_PathLine.enabled = false;
	}

	private void ApplyPathLineVisualStyle(bool _preview)
	{
		if (m_PathLine == null)
			return;

		float width = _preview ? c_PathLinePreviewWidth : c_PathLineNormalWidth;
		Color color = _preview ? s_PathLinePreviewColor : s_PathLineNormalColor;
		m_PathLine.startWidth = width;
		m_PathLine.endWidth = width;
		m_PathLine.startColor = color;
		m_PathLine.endColor = color;
	}

	private void RebuildPathLine()
	{
		if (m_PathLine == null)
			return;

		if (m_IsMovePreviewVisualActive)
		{
			RefreshMovePreviewPathLine();
			return;
		}

		if (m_Waypoints.Count == 0)
		{
			m_PathLine.positionCount = 0;
			m_PathLine.enabled = false;
			return;
		}

		BuildRawRoutePoints(m_RawRoutePoints, includeUnitStart: !IsAtFirstWaypoint(), previewDestination: null);
		BuildSmoothedPathPoints(m_RawRoutePoints, m_SmoothedRoutePoints);
		ApplyPathLinePoints(m_SmoothedRoutePoints);
		m_PathLine.enabled = m_IsSelected;
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this))
			s_Instances.Add(this);
		SetSelected(false);
		ApplyAnimatorSpeedVariation();
	}

	private void OnDisable()
	{
		if (m_FormationSyncGroup != null)
			m_FormationSyncGroup.Members.Remove(this);
		CancelPendingCommand();
		ClearWaypoints();
		ResetAnimatorSpeed();
		s_Instances.Remove(this);
		SetSelected(false);
	}

	private void Update()
	{
		ApplyAnimatorSpeedVariation();
		UpdateSelectionLabelBillboard();
		UpdateContinuousRouteLocomotionFlags();
		UpdatePathLinePosition();
		UpdateActiveFacingArrows();
		UpdateFacingTurn();
		SyncFacingArrows();
		UpdateFacingArrows();
		TryRemoveArrivedDestination();
		TryAdvanceWaypointEarly();
		UpdateFacingRotation();
		UpdateFormationFacing();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		UpdateRouteDebugPeriodicState();
#endif
	}

	private void TryAdvanceWaypointEarly()
	{
		if (!m_HasActiveDestination)
			return;
		if (m_IsRotatingToFacing)
			return;
		if (m_IsWaitingAtRouteGate)
			return;
		if (m_IsExecutingSmoothingArc)
			return;

		bool isIntermediate = IsIntermediateRouteSegment();
		if (!isIntermediate && Time.time < m_NextWaypointCheckTime)
			return;
		if (!isIntermediate)
			m_NextWaypointCheckTime = Time.time + 0.2f;

		if (!isIntermediate &&
		    m_DestinationSetTime >= 0f && Time.time - m_DestinationSetTime < 0.3f)
			return;

		if (m_DestinationSetTime >= 0f && Time.time - m_DestinationSetTime < 0.15f)
			return;

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent == null || !agent.isOnNavMesh || agent.pathPending)
			return;
		if (!agent.hasPath)
			return;

		float advanceDistance = isIntermediate ? m_ContinuousRouteLookaheadDistance : 0.5f;
		if (agent.remainingDistance > advanceDistance)
			return;

		if (m_EnableRouteCornerSmoothing && m_Waypoints.Count >= 2)
		{
			Vector3 corner = m_Waypoints[0];
			float cornerProximity = m_ContinuousRouteLookaheadDistance + 0.25f;
			if (!IsNearDestination(transform.position, corner, cornerProximity))
				return;

			if (TryBeginSmoothingArcMovement(m_ActiveMoveTier))
				return;
		}

		if (m_CommandQueue.Count == 0)
			return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"EARLY_ADVANCE rem={agent.remainingDistance:F2} intermediate={isIntermediate} {BuildRouteDebugSnapshot()}");
#endif
		TryAdvanceRouteQueue();
	}

	private void AdvanceSmoothingArc()
	{
		m_SmoothingSubtargetIndex++;
		if (m_SmoothingSubtargetIndex < m_SmoothingSubtargets.Count)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent(
				$"ARC_SUBTARGET {m_SmoothingSubtargetIndex}/{m_SmoothingSubtargets.Count} {BuildRouteDebugSnapshot()}");
#endif
			m_DestinationSetTime = Time.time;
			IssueRouteMoveOrder(m_SmoothingSubtargets[m_SmoothingSubtargetIndex], m_SmoothingMoveTier, _continuous: true);
			return;
		}

		CompleteSmoothingArcAndContinueQueue();
	}

	private void CompleteSmoothingArcAndContinueQueue()
	{
		ClearSmoothingArcState();
		m_PreferContinuousNextMoveOrder = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"ARC_COMPLETE {BuildRouteDebugSnapshot()}");
#endif

		TryAdvanceRouteQueue();
	}

	private void ClearSmoothingArcState()
	{
		m_IsExecutingSmoothingArc = false;
		m_SmoothingSubtargets.Clear();
		m_SmoothingSubtargetIndex = 0;
	}

	private void ResetContinuousRouteLocomotionFlags()
	{
		if (m_ClickToMove != null)
			m_ClickToMove.SuppressEarlyArrivalStop = false;
		if (m_LocomotionDriver != null)
			m_LocomotionDriver.SuppressEarlyArrivalStop = false;
	}

	private void UpdatePathLinePosition()
	{
		if (m_PathLine == null)
			return;

		if (m_IsMovePreviewVisualActive)
		{
			RefreshMovePreviewPathLine();
			return;
		}

		if (!m_PathLine.enabled || m_Waypoints.Count == 0)
			return;

		BuildRawRoutePoints(m_RawRoutePoints, includeUnitStart: true, previewDestination: null);
		BuildSmoothedPathPoints(m_RawRoutePoints, m_SmoothedRoutePoints);
		if (m_SmoothedRoutePoints.Count > 0)
			m_SmoothedRoutePoints[0] = transform.position;
		ApplyPathLinePoints(m_SmoothedRoutePoints);
	}

	private void RefreshMovePreviewPathLine()
	{
		if (!m_IsMovePreviewVisualActive || !m_MovePreviewDestination.HasValue || m_PathLine == null)
			return;

		BuildRawRoutePoints(m_RawRoutePoints, includeUnitStart: true, previewDestination: m_MovePreviewDestination);
		BuildSmoothedPathPoints(m_RawRoutePoints, m_SmoothedRoutePoints);
		if (m_SmoothedRoutePoints.Count > 0)
			m_SmoothedRoutePoints[0] = transform.position;
		ApplyPathLinePoints(m_SmoothedRoutePoints);
		m_PathLine.enabled = m_IsSelected;
	}

	private void UpdateFacingRotation()
	{
		if (!m_IsRotatingToFacing)
			return;

		if (ShouldDeferRouteFacingOverride())
			return;

		float rotateSpeed = GetEffectiveRotateSpeed();

		Quaternion targetRot = Quaternion.Euler(0f, m_WantedFacingAngle, 0f);
		float angle = Quaternion.Angle(transform.rotation, targetRot);

		HandleFacingTurnReady(angle);

		if (angle < 0.5f)
		{
			transform.rotation = targetRot;
			m_IsRotatingToFacing = false;
			if (m_FacingSuppressedReady)
			{
				m_ReadyHands?.SetReadyWanted(true);
				m_FacingSuppressedReady = false;
			}

			TryAdvanceRouteQueue();
			return;
		}

		float smoothAngle = Mathf.SmoothDampAngle(
			transform.rotation.eulerAngles.y,
			m_WantedFacingAngle,
			ref m_FacingRotateVelocity,
			1f / rotateSpeed,
			Mathf.Infinity,
			Time.deltaTime);

		transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);
	}

	private void HandleFacingTurnReady(float _angleDegrees)
	{
		if (m_ReadyHands == null)
			return;
		if (!m_WasReadyBeforeFacing)
			return;

		if (_angleDegrees > 90f)
		{
			if (!m_FacingSuppressedReady)
			{
				if (m_ReadyHands.IsWeaponEquipped() && m_ReadyHands.WantsReady)
					m_ReadyHands.SetReadyWanted(false);
				m_FacingSuppressedReady = true;
			}
		}
		else if (_angleDegrees < 20f && m_FacingSuppressedReady)
		{
			m_ReadyHands.SetReadyWanted(true);
			m_FacingSuppressedReady = false;
		}
	}

	private void TryRemoveArrivedDestination()
	{
		if (!m_HasActiveDestination)
			return;
		if (m_IsWaitingAtRouteGate)
			return;

		bool isIntermediate = IsIntermediateRouteSegment();
		float arrivalGrace = isIntermediate ? 0.2f : 0.5f;
		if (m_DestinationSetTime >= 0f && Time.time - m_DestinationSetTime < arrivalGrace)
			return;

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent == null || !agent.isOnNavMesh)
			return;
		if (agent.pathPending)
			return;

		if (m_IsExecutingSmoothingArc)
		{
			if (!m_IsRotatingToFacing && HasReachedCurrentSmoothingSubtarget(agent))
				AdvanceSmoothingArc();
			return;
		}

		if (!HasReachedActiveDestination(agent))
			return;

		if (ShouldClearFacingOnLegArrival())
		{
			if (m_IsInFacingTurn)
			{
				ClearFacingTurn();
			}
			else if (m_HasWantedFacing)
			{
				if (m_IsRotatingToFacing)
				{
					m_FacingRotateVelocity = 0f;
					m_FacingSuppressedReady = false;
					m_WasReadyBeforeFacing = m_ReadyHands != null && m_ReadyHands.WantsReady;
				}
				else
				{
					ClearFacingOverride();
				}

				m_HasWantedFacing = false;
				m_ActiveFacingArrows = null;
				MarkFacingArrowsDirty();
			}
		}

		if (!m_IsRotatingToFacing)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent($"ARRIVED fullStop intermediate={isIntermediate} {BuildRouteDebugSnapshot()}");
#endif
			TryAdvanceRouteQueue();
		}

		if (!m_HasActiveDestination && m_Waypoints.Count == 0)
		{
			ResetActiveMoveTierWhenIdle();
			if (m_PathLine != null)
				m_PathLine.enabled = false;
		}
	}
	#endregion

	#region Public Methods
	public void SetSelected(bool _selected)
	{
		m_IsSelected = _selected;
		if (m_SelectionVisualRoot != null)
			m_SelectionVisualRoot.SetActive(false);

		if (_selected)
		{
			m_RosterDisplay = UnitRosterDisplayState.GetOrCreate(gameObject);
			EnsureSelectionNameLabel();
			RefreshSelectionNameLabel();
		}

		if (m_PathLine != null)
			m_PathLine.enabled = _selected && m_PathLine.positionCount >= 2;

		if (m_SelectionNameLabelRoot != null)
			m_SelectionNameLabelRoot.SetActive(_selected);
	}

	public void IssueMoveOrder(Vector3 _worldPosition, UnitClickToMove.MoveTier _moveTier)
	{
		IssueRouteMoveOrder(_worldPosition, _moveTier, _continuous: false);
	}

	public void BeginActiveRouteMovement(UnitClickToMove.MoveTier _moveTier)
	{
		if (!m_HasActiveDestination || m_Waypoints.Count == 0)
			return;

		m_ActiveMoveTier = _moveTier;
		IssueMoveOrderForCurrentWaypoint(m_Waypoints[0], _moveTier);
	}

	private void IssueRouteMoveOrder(Vector3 _worldPosition, UnitClickToMove.MoveTier _moveTier, bool _continuous)
	{
		ScheduleRtsCommand(() =>
		{
			UnitSelfStabilizationController selfStabilization = ResolveSelfStabilizationController();
			if (selfStabilization != null &&
			    (selfStabilization.IsSelfHealing || selfStabilization.IsHealPresentationActive))
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				LogRouteDebugEvent("NAV_BLOCKED selfHeal");
#endif
				return;
			}

			UnitStabilizeOtherController stabilizeOther = ResolveStabilizeOtherController();
			if (stabilizeOther != null &&
			    (stabilizeOther.IsStabilizingOther || stabilizeOther.IsHealPresentationActive))
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				LogRouteDebugEvent("NAV_BLOCKED stabilizeOther");
#endif
				return;
			}

			if (_moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint)
				m_MagazineLoadingController?.StopLoading();

			bool isRunOrSprint = _moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint;
			if (isRunOrSprint && TryGetComponent(out UnitStamina stamina) && stamina.IsExhausted)
				_moveTier = UnitClickToMove.MoveTier.Walk;

			if (_moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint)
				ClearFacingOverride();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent(
				$"NAV_ORDER mode={(_continuous ? "continuous" : "reset")} tier={_moveTier} dest={FormatRoutePoint(_worldPosition)} {BuildRouteDebugSnapshot()}");
#endif

			if (m_ClickToMove != null)
			{
				if (_continuous)
					m_ClickToMove.IssueNavOrderContinuous(_worldPosition, _moveTier);
				else
					m_ClickToMove.IssueNavOrder(_worldPosition, _moveTier);
				return;
			}

			if (m_LocomotionDriver != null)
			{
				UnitNavLocomotionDriver.MoveTier navTier = _moveTier switch
				{
					UnitClickToMove.MoveTier.Run => UnitNavLocomotionDriver.MoveTier.Run,
					UnitClickToMove.MoveTier.Sprint => UnitNavLocomotionDriver.MoveTier.Sprint,
					_ => UnitNavLocomotionDriver.MoveTier.Walk
				};
				if (_continuous)
					m_LocomotionDriver.IssueNavOrderContinuous(_worldPosition, navTier);
				else
					m_LocomotionDriver.IssueNavOrder(_worldPosition, navTier);
			}
		});
	}

	public void BeginMovePreviewVisual()
	{
		m_IsMovePreviewVisualActive = true;
		m_MovePreviewDestination = null;
		ApplyPathLineVisualStyle(_preview: true);
	}

	public void EndMovePreviewVisual()
	{
		if (!m_IsMovePreviewVisualActive)
			return;

		m_IsMovePreviewVisualActive = false;
		m_MovePreviewDestination = null;
		ApplyPathLineVisualStyle(_preview: false);
		if (m_Waypoints.Count > 0)
			RebuildPathLine();
		else if (m_PathLine != null)
		{
			m_PathLine.positionCount = 0;
			m_PathLine.enabled = false;
		}
	}

	public void SetPreviewLine(Vector3 _dest)
	{
		if (m_PathLine == null)
			return;

		if (!m_IsMovePreviewVisualActive)
			BeginMovePreviewVisual();

		m_MovePreviewDestination = _dest;
		RefreshMovePreviewPathLine();
	}

	public void SetDestinationDirect(Vector3 _dest, UnitClickToMove.MoveTier _moveTier = UnitClickToMove.MoveTier.Walk)
	{
		ClearFacingTurn();
		ClearSmoothingArcState();
		ClearRouteWaitState();
		m_Waypoints.Clear();
		m_Waypoints.Add(_dest);
		RebuildPathLine();
		m_HasActiveDestination = true;
		m_ActiveMoveTier = _moveTier;
		m_ActiveRouteSegmentStart = transform.position;
		m_HasLastArrivalMovementAngle = false;
		m_DestinationSetTime = Time.time;
		m_IsRotatingToFacing = false;
		m_HasWantedFacing = false;
		ClearFacingOverride();
		m_ActiveFacingArrows = null;
		MarkFacingArrowsDirty();
	}

	public int GetNextAutoWaitGroup()
	{
		int maxGroup = 0;
		for (int i = 0; i < m_CommandQueue.Count; i++)
		{
			if (m_CommandQueue[i].WaitGroup > maxGroup)
				maxGroup = m_CommandQueue[i].WaitGroup;
		}

		if (m_IsWaitingAtRouteGate && m_ActiveWaitGroup > maxGroup)
			maxGroup = m_ActiveWaitGroup;

		if (maxGroup >= 3)
			return 3;
		if (maxGroup <= 0)
			return 1;
		return maxGroup + 1;
	}

	public void IssueDirectMoveOrderWithWait(
		Vector3 _dest,
		UnitClickToMove.MoveTier _tier,
		float? _facing,
		FacingArrowMode _mode,
		int _waitGroup,
		Vector3? _lookPoint = null,
		bool _activateAtSegmentStart = false)
	{
		CancelPendingCommand();
		ClearWaypoints();

		m_Waypoints.Add(_dest);

		var cmd = new QueuedCommand
		{
			Destination = _dest,
			MoveTier = _tier,
			FacingArrows = new List<FacingArrow>(),
		};

		if (_facing.HasValue && !float.IsNaN(_facing.Value))
		{
			bool hasLookPoint = _mode == FacingArrowMode.LookAtPoint && _lookPoint.HasValue;
			cmd.FacingArrows.Add(BindFacingArrowToRouteSegment(new FacingArrow
			{
				Angle = _facing.Value,
				Mode = _mode,
				ForceReadyOnActivation = false,
				ActivateAtSegmentStart = _activateAtSegmentStart,
				HasLookPoint = hasLookPoint,
			}, 0, _dest, hasLookPoint ? _lookPoint : null));
		}

		AssignWaitMetadata(ref cmd, _waitGroup, _iconAtWaypoint: false);

		m_CommandQueue.Add(cmd);
		if (cmd.WaitGroup >= 1)
		{
			cmd = m_CommandQueue[0];
			BindWaitHoldToRoute(ref cmd, 0);
			m_CommandQueue[0] = cmd;
		}
		RebuildPathLine();
		MarkFacingArrowsDirty();
		TryStartNextQueuedCommand();
	}

	public void EnqueueWaypoint(
		Vector3 _dest,
		UnitClickToMove.MoveTier _tier,
		float? _facing,
		FacingArrowMode _mode = FacingArrowMode.TurnOverDistance,
		int _waitGroup = 0,
		Vector3? _lookPoint = null,
		bool _activateAtSegmentStart = false)
	{
		m_Waypoints.Add(_dest);

		int commandIndex = m_CommandQueue.Count;
		var cmd = new QueuedCommand
		{
			Destination = _dest,
			MoveTier = _tier,
			FacingArrows = new List<FacingArrow>(),
		};
		
		if (_facing.HasValue && !float.IsNaN(_facing.Value))
		{
			int waypointIndex = m_Waypoints.Count - 1;
			bool hasLookPoint = _mode == FacingArrowMode.LookAtPoint && _lookPoint.HasValue;
			cmd.FacingArrows.Add(BindFacingArrowToRouteSegment(new FacingArrow
			{
				Angle = _facing.Value,
				Mode = _mode,
				ForceReadyOnActivation = false,
				ActivateAtSegmentStart = _activateAtSegmentStart,
				HasLookPoint = hasLookPoint,
			}, waypointIndex, m_Waypoints[waypointIndex], hasLookPoint ? _lookPoint : null));
		}

		AssignWaitMetadata(ref cmd, _waitGroup, _iconAtWaypoint: false);
		
		m_CommandQueue.Add(cmd);
		if (cmd.WaitGroup >= 1)
		{
			cmd = m_CommandQueue[commandIndex];
			BindWaitHoldToRoute(ref cmd, commandIndex);
			m_CommandQueue[commandIndex] = cmd;
		}

		RebuildPathLine();

		bool isIdle = !m_HasActiveDestination && !m_IsRotatingToFacing && !m_IsWaitingAtRouteGate;
		if (isIdle)
			TryStartNextQueuedCommand();

		MarkFacingArrowsDirty();
	}

	/// <summary>
	/// Upgrades an existing walk command to run when double-clicking the same destination (shift queue).
	/// Avoids adding a duplicate waypoint that would briefly switch to run and back to walk.
	/// </summary>
	public bool TryUpgradeMoveTargetToRun(Vector3 _destination, float _destinationEpsilon = 0.75f)
	{
		if (m_Waypoints.Count == 0)
			return false;

		for (int waypointIndex = m_Waypoints.Count - 1; waypointIndex >= 0; waypointIndex--)
		{
			if (!IsNearDestination(m_Waypoints[waypointIndex], _destination, _destinationEpsilon))
				continue;

			if (m_HasActiveDestination && waypointIndex == 0)
			{
				if (m_ActiveMoveTier == UnitClickToMove.MoveTier.Run)
					return true;

				m_ActiveMoveTier = UnitClickToMove.MoveTier.Run;
				ClearSmoothingArcState();
				IssueMoveOrderForCurrentWaypoint(m_Waypoints[0], UnitClickToMove.MoveTier.Run);
				RebuildPathLine();
				MarkFacingArrowsDirty();
				return true;
			}

			int commandIndex = m_HasActiveDestination ? waypointIndex - 1 : waypointIndex;
			if (commandIndex < 0 || commandIndex >= m_CommandQueue.Count)
				return false;

			QueuedCommand cmd = m_CommandQueue[commandIndex];
			cmd.MoveTier = UnitClickToMove.MoveTier.Run;
			m_CommandQueue[commandIndex] = cmd;
			RebuildPathLine();
			MarkFacingArrowsDirty();
			return true;
		}

		return false;
	}

	public int WaypointCount => m_Waypoints.Count;

	/// <summary>
	/// Оценка оставшегося маршрута: NavMesh до активной точки + длины следующих сегментов очереди.
	/// Без аллокаций; используется для непрерывной синхронизации скорости формации.
	/// </summary>
	public float GetTotalRouteRemainingDistance()
	{
		if (m_Waypoints.Count == 0 && !m_HasActiveDestination)
			return 0f;

		float total = 0f;
		NavMeshAgent agent = GetComponent<NavMeshAgent>();

		if (m_IsExecutingSmoothingArc && m_SmoothingSubtargets.Count > 0 &&
		    m_SmoothingSubtargetIndex >= 0 && m_SmoothingSubtargetIndex < m_SmoothingSubtargets.Count)
		{
			Vector3 subtarget = m_SmoothingSubtargets[m_SmoothingSubtargetIndex];
			if (agent != null && agent.isOnNavMesh && agent.hasPath && !agent.pathPending &&
			    !float.IsPositiveInfinity(agent.remainingDistance))
				total += agent.remainingDistance;
			else
				total += PlanarDistance(transform.position, subtarget);

			for (int i = m_SmoothingSubtargetIndex + 1; i < m_SmoothingSubtargets.Count; i++)
				total += PlanarDistance(m_SmoothingSubtargets[i - 1], m_SmoothingSubtargets[i]);
		}
		else if (m_HasActiveDestination)
		{
			if (agent != null && agent.isOnNavMesh && agent.hasPath && !agent.pathPending &&
			    !float.IsPositiveInfinity(agent.remainingDistance))
				total += agent.remainingDistance;
			else if (m_Waypoints.Count > 0)
				total += PlanarDistance(transform.position, m_Waypoints[0]);
		}
		else if (m_Waypoints.Count > 0)
		{
			total += PlanarDistance(transform.position, m_Waypoints[0]);
		}

		for (int i = 0; i < m_Waypoints.Count - 1; i++)
			total += PlanarDistance(m_Waypoints[i], m_Waypoints[i + 1]);

		return total;
	}

	private static float PlanarDistance(Vector3 _a, Vector3 _b)
	{
		float dx = _a.x - _b.x;
		float dz = _a.z - _b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	public Vector3 GetWaypointWorld(int _index)
	{
		return _index >= 0 && _index < m_Waypoints.Count ? m_Waypoints[_index] : Vector3.zero;
	}

	public float GetWaypointFacing(int _index, out FacingArrowMode _mode)
	{
		_mode = FacingArrowMode.TurnOverDistance;
		int cmdIndex = _index;
		if (m_HasActiveDestination)
		{
			if (cmdIndex == 0)
			{
				if (m_ActiveFacingArrows != null && m_ActiveFacingArrows.Count > 0)
				{
					_mode = m_ActiveFacingArrows[m_ActiveFacingArrows.Count - 1].Mode;
					return m_ActiveFacingArrows[m_ActiveFacingArrows.Count - 1].Angle;
				}
				return float.NaN;
			}
			cmdIndex--;
		}
		if (cmdIndex < 0 || cmdIndex >= m_CommandQueue.Count)
			return float.NaN;
		
		var arrows = m_CommandQueue[cmdIndex].FacingArrows;
		if (arrows == null || arrows.Count == 0)
			return float.NaN;
		_mode = arrows[arrows.Count - 1].Mode;
		return arrows[arrows.Count - 1].Angle;
	}

	public void CollectFacingArrowDescriptors(List<FacingArrowDescriptor> _output)
	{
		if (_output == null)
			return;

		_output.Clear();

		if (m_HasActiveDestination && m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
			{
				FacingArrow arrow = m_ActiveFacingArrows[i];
				Vector3 anchor = ResolveFacingArrowAnchor(arrow, _isActiveSegment: true);
				Vector3 lookPoint = arrow.HasLookPoint ? ResolveFacingArrowLookPoint(arrow, _isActiveSegment: true) : Vector3.zero;
				_output.Add(new FacingArrowDescriptor(
					0,
					i,
					true,
					anchor,
					arrow.Angle,
					arrow.Mode,
					lookPoint,
					arrow.HasLookPoint));
			}
		}

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			List<FacingArrow> arrows = m_CommandQueue[commandIndex].FacingArrows;
			if (arrows == null || arrows.Count == 0)
				continue;

			int segmentIndex = m_HasActiveDestination ? commandIndex + 1 : commandIndex;
			for (int arrowIndex = 0; arrowIndex < arrows.Count; arrowIndex++)
			{
				FacingArrow arrow = arrows[arrowIndex];
				Vector3 anchor = ResolveFacingArrowAnchor(arrow);
				Vector3 lookPoint = arrow.HasLookPoint ? ResolveFacingArrowLookPoint(arrow) : Vector3.zero;
				_output.Add(new FacingArrowDescriptor(
					segmentIndex,
					arrowIndex,
					false,
					anchor,
					arrow.Angle,
					arrow.Mode,
					lookPoint,
					arrow.HasLookPoint));
			}
		}
	}

	public bool TryRemoveFacingArrow(int _segmentIndex, int _arrowIndex)
	{
		if (_arrowIndex < 0)
			return false;

		if (m_HasActiveDestination && _segmentIndex == 0)
		{
			if (m_ActiveFacingArrows == null || _arrowIndex >= m_ActiveFacingArrows.Count)
				return false;

			m_ActiveFacingArrows.RemoveAt(_arrowIndex);
			if (m_ActiveFacingArrows.Count == 0)
				m_ActiveFacingArrows = null;

			MarkFacingArrowsDirty();
			return true;
		}

		int commandIndex = m_HasActiveDestination ? _segmentIndex - 1 : _segmentIndex;
		if (commandIndex < 0 || commandIndex >= m_CommandQueue.Count)
			return false;

		QueuedCommand cmd = m_CommandQueue[commandIndex];
		if (cmd.FacingArrows == null || _arrowIndex >= cmd.FacingArrows.Count)
			return false;

		cmd.FacingArrows.RemoveAt(_arrowIndex);
		m_CommandQueue[commandIndex] = cmd;
		MarkFacingArrowsDirty();
		return true;
	}

	public void CollectWaitPointDescriptors(List<WaitPointDescriptor> _output)
	{
		if (_output == null)
			return;

		_output.Clear();
		EnsureWaitRouteBindings();

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			int waitGroup = cmd.WaitGroup;
			if (waitGroup < 1)
				continue;

			int waypointIndex = m_HasActiveDestination ? commandIndex + 1 : commandIndex;
			if (waypointIndex < 0 || waypointIndex >= m_Waypoints.Count)
				continue;

			_output.Add(new WaitPointDescriptor(
				waypointIndex,
				false,
				ResolveWaitHoldWorldPosition(commandIndex, cmd),
				waitGroup));
		}
	}

	private Vector3 ResolveWaitHoldWorldPosition(int _commandIndex, in QueuedCommand _command)
	{
		if (_command.HasWaitRouteBinding)
			return ResolveRouteSegmentPoint(_command.WaitRouteSegmentIndex, _command.WaitRouteSegmentT);

		if (_command.WaitIconAtWaypoint)
		{
			int waypointIndex = m_HasActiveDestination ? _commandIndex + 1 : _commandIndex;
			if (waypointIndex >= 0 && waypointIndex < m_Waypoints.Count)
				return m_Waypoints[waypointIndex];
		}

		return ComputeAutoWaitHoldWorldPosition(_commandIndex);
	}

	private Vector3 ResolveRouteSegmentPoint(int _segmentIndex, float _segmentT)
	{
		if (TryGetRouteSegmentEndpoints(_segmentIndex, out Vector3 segmentStart, out Vector3 segmentEnd))
			return Vector3.Lerp(segmentStart, segmentEnd, _segmentT);

		return transform.position;
	}

	private void BindWaitHoldToRoute(ref QueuedCommand _command, int _commandIndex)
	{
		if (_command.WaitIconAtWaypoint)
		{
			int waypointIndex = m_HasActiveDestination ? _commandIndex + 1 : _commandIndex;
			if (waypointIndex < 0 || waypointIndex >= m_Waypoints.Count)
				return;

			BindRouteVertexToSegment(
				waypointIndex,
				out _command.WaitRouteSegmentIndex,
				out _command.WaitRouteSegmentT);
			_command.HasWaitRouteBinding = true;
			return;
		}

		int waitSegmentIndex = m_HasActiveDestination ? _commandIndex + 1 : _commandIndex;
		if (!TryGetRouteSegmentEndpoints(waitSegmentIndex, out _, out _))
			return;

		_command.HasWaitRouteBinding = true;
		_command.WaitRouteSegmentIndex = waitSegmentIndex;
		_command.WaitRouteSegmentT = 0f;
	}

	private void BindRouteVertexToSegment(int _waypointIndex, out int _segmentIndex, out float _segmentT)
	{
		if (_waypointIndex < m_Waypoints.Count - 1)
		{
			_segmentIndex = _waypointIndex + 1;
			_segmentT = 0f;
			return;
		}

		_segmentIndex = Mathf.Max(0, _waypointIndex);
		_segmentT = 1f;
	}

	private void EnsureWaitRouteBindings()
	{
		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			if (cmd.WaitGroup >= 1 && !cmd.HasWaitRouteBinding)
			{
				BindWaitHoldToRoute(ref cmd, commandIndex);
				m_CommandQueue[commandIndex] = cmd;
			}
		}
	}

	private void ShiftWaitHoldSegmentsForWaypointInsert(int _insertSegmentIndex, float _insertSegmentT)
	{
		EnsureWaitRouteBindings();

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			if (!cmd.HasWaitRouteBinding)
				continue;

			RemapRouteSegmentBindingForInsert(
				ref cmd.WaitRouteSegmentIndex,
				ref cmd.WaitRouteSegmentT,
				_insertSegmentIndex,
				_insertSegmentT);
			m_CommandQueue[commandIndex] = cmd;
		}
	}

	private void ShiftWaitHoldSegmentsAfterWaypointRemoved(int _removedWaypointIndex)
	{
		EnsureWaitRouteBindings();

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			if (!cmd.HasWaitRouteBinding)
				continue;

			RemapRouteSegmentBindingForRemove(ref cmd.WaitRouteSegmentIndex, ref cmd.WaitRouteSegmentT, _removedWaypointIndex);
			m_CommandQueue[commandIndex] = cmd;
		}
	}

	private static void RemapRouteSegmentBindingForInsert(
		ref int _segmentIndex,
		ref float _segmentT,
		int _insertSegmentIndex,
		float _insertSegmentT)
	{
		if (_segmentIndex < _insertSegmentIndex)
			return;

		if (_segmentIndex > _insertSegmentIndex)
		{
			_segmentIndex++;
			return;
		}

		float clampedInsertT = Mathf.Clamp(_insertSegmentT, 0.0001f, 0.9999f);
		if (_segmentT <= _insertSegmentT)
		{
			_segmentT = _segmentT / clampedInsertT;
			return;
		}

		_segmentIndex = _insertSegmentIndex + 1;
		_segmentT = (_segmentT - _insertSegmentT) / (1f - clampedInsertT);
	}

	private static void RemapRouteSegmentBindingForRemove(
		ref int _segmentIndex,
		ref float _segmentT,
		int _removedWaypointIndex)
	{
		if (_segmentIndex > _removedWaypointIndex + 1)
		{
			_segmentIndex--;
			return;
		}

		if (_segmentIndex == _removedWaypointIndex + 1)
			_segmentIndex = Mathf.Max(0, _removedWaypointIndex);
	}

	private Vector3 ResolveFacingArrowAnchor(in FacingArrow _arrow, bool _isActiveSegment = false)
	{
		if (TryGetRouteSegmentEndpoints(_arrow.RouteSegmentIndex, _isActiveSegment, out Vector3 segmentStart, out Vector3 segmentEnd))
			return Vector3.Lerp(segmentStart, segmentEnd, _arrow.RouteSegmentT);

		return transform.position;
	}

	private Vector3 ResolveFacingArrowLookPoint(in FacingArrow _arrow, bool _isActiveSegment = false)
	{
		return ResolveFacingArrowAnchor(_arrow, _isActiveSegment) + _arrow.LookOffsetFromAnchor;
	}

	private bool TryGetRouteSegmentEndpoints(int _segmentIndex, out Vector3 _start, out Vector3 _end)
	{
		return TryGetRouteSegmentEndpoints(_segmentIndex, _useActiveSegmentStart: false, out _start, out _end);
	}

	private bool TryGetRouteSegmentEndpoints(
		int _segmentIndex,
		bool _useActiveSegmentStart,
		out Vector3 _start,
		out Vector3 _end)
	{
		_start = Vector3.zero;
		_end = Vector3.zero;
		if (m_Waypoints.Count == 0)
			return false;

		if (_segmentIndex == 0)
		{
			_start = _useActiveSegmentStart && m_HasActiveDestination
				? m_ActiveRouteSegmentStart
				: transform.position;
			_end = m_Waypoints[0];
			return true;
		}

		if (_segmentIndex < 1 || _segmentIndex >= m_Waypoints.Count)
			return false;

		_start = m_Waypoints[_segmentIndex - 1];
		_end = m_Waypoints[_segmentIndex];
		return true;
	}

	private static float ComputeRouteSegmentT(Vector3 _point, Vector3 _start, Vector3 _end)
	{
		Vector3 segment = _end - _start;
		segment.y = 0f;
		if (segment.sqrMagnitude < 0.0001f)
			return 0f;

		Vector3 toPoint = _point - _start;
		toPoint.y = 0f;
		return Mathf.Clamp01(Vector3.Dot(toPoint, segment) / segment.sqrMagnitude);
	}

	private FacingArrow BindFacingArrowToRouteSegment(
		FacingArrow _arrow,
		int _segmentIndex,
		Vector3 _anchorWorld,
		Vector3? _lookPointWorld = null,
		bool _useActiveSegmentStart = false)
	{
		_arrow.RouteSegmentIndex = _segmentIndex;
		if (TryGetRouteSegmentEndpoints(_segmentIndex, _useActiveSegmentStart, out Vector3 segmentStart, out Vector3 segmentEnd))
			_arrow.RouteSegmentT = ComputeRouteSegmentT(_anchorWorld, segmentStart, segmentEnd);
		else
			_arrow.RouteSegmentT = 0f;

		if (_arrow.HasLookPoint && _lookPointWorld.HasValue)
		{
			Vector3 anchor = ResolveFacingArrowAnchor(_arrow, _useActiveSegmentStart);
			_arrow.LookOffsetFromAnchor = _lookPointWorld.Value - anchor;
		}

		return _arrow;
	}

	private bool TryGetFacingArrow(bool _isActiveSegment, int _commandIndex, int _arrowIndex, out FacingArrow _arrow)
	{
		_arrow = default;
		if (_isActiveSegment)
		{
			if (m_ActiveFacingArrows == null || _arrowIndex < 0 || _arrowIndex >= m_ActiveFacingArrows.Count)
				return false;

			_arrow = m_ActiveFacingArrows[_arrowIndex];
			return true;
		}

		if (_commandIndex < 0 || _commandIndex >= m_CommandQueue.Count)
			return false;

		List<FacingArrow> arrows = m_CommandQueue[_commandIndex].FacingArrows;
		if (arrows == null || _arrowIndex < 0 || _arrowIndex >= arrows.Count)
			return false;

		_arrow = arrows[_arrowIndex];
		return true;
	}

	public bool TryCycleWaitGroupForWaypoint(int _waypointIndex)
	{
		if (!TryGetWaitGroupForWaypoint(_waypointIndex, out int currentGroup))
			return TrySetWaitGroupForWaypoint(_waypointIndex, 1, _manualPlacement: false);

		int nextGroup = currentGroup >= 3 ? 1 : currentGroup + 1;
		return TrySetWaitGroupForWaypoint(_waypointIndex, nextGroup, _preserveWaitHoldPosition: true);
	}

	public bool TryRemoveWaitPointAtWaypoint(int _waypointIndex)
	{
		if (!TryGetWaitGroupForWaypoint(_waypointIndex, out _))
			return false;

		int commandIndex = m_HasActiveDestination ? _waypointIndex - 1 : _waypointIndex;
		if (!TrySetWaitGroupForWaypoint(_waypointIndex, 0))
			return false;

		if (m_IsWaitingAtRouteGate && commandIndex == 0)
			ResumeAfterWaitGroupRemoved();

		return true;
	}

	public bool TryContinueRouteWaitGroup(int _waitGroup)
	{
		int normalizedGroup = NormalizeWaitGroup(_waitGroup);
		if (normalizedGroup < 1)
			return false;

		bool changed = false;

		if (m_IsWaitingAtRouteGate && m_ActiveWaitGroup == normalizedGroup)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent($"WAIT_CONTINUE group={normalizedGroup} {BuildRouteDebugSnapshot()}");
#endif
			m_IsWaitingAtRouteGate = false;
			m_ActiveWaitGroup = 0;
			ResumeAgentAfterRouteGate();
			if (m_CommandQueue.Count > 0)
				DequeueAndExecuteNextCommand();
			changed = true;
		}

		for (int i = 0; i < m_CommandQueue.Count; i++)
		{
			QueuedCommand cmd = m_CommandQueue[i];
			if (cmd.WaitGroup != normalizedGroup)
				continue;

			cmd.WaitGroup = 0;
			cmd.WaitIconAtWaypoint = false;
			m_CommandQueue[i] = cmd;
			changed = true;
		}

		if (changed)
			MarkFacingArrowsDirty();

		return changed;
	}

	public bool TryInsertRouteWaypointAtSegment(int _segmentIndex, Vector3 _worldPoint, int _waitGroup = 0)
	{
		if (_segmentIndex < 0 || _segmentIndex > m_Waypoints.Count || m_Waypoints.Count == 0)
			return false;

		Vector3 sampledPoint = _worldPoint;
		TrySampleNavMeshPoint(_worldPoint, out sampledPoint);

		UnitClickToMove.MoveTier tier = ResolveMoveTierForWaypointInsert(_segmentIndex);
		float insertSegmentT = 1f;
		if (TryGetRouteSegmentEndpoints(_segmentIndex, out Vector3 insertSegmentStart, out Vector3 insertSegmentEnd))
			insertSegmentT = ComputeRouteSegmentT(_worldPoint, insertSegmentStart, insertSegmentEnd);
		ShiftFacingArrowSegmentsForWaypointInsert(_segmentIndex, insertSegmentT);
		ShiftWaitHoldSegmentsForWaypointInsert(_segmentIndex, insertSegmentT);

		if (m_HasActiveDestination && _segmentIndex == 0)
		{
			Vector3 previousActiveDestination = m_Waypoints[0];
			m_Waypoints.Insert(0, sampledPoint);

			var promoteCommand = new QueuedCommand
			{
				Destination = previousActiveDestination,
				MoveTier = m_ActiveMoveTier,
				FacingArrows = m_ActiveFacingArrows != null
					? new List<FacingArrow>(m_ActiveFacingArrows)
					: new List<FacingArrow>(),
				WaitGroup = 0,
			};

			int normalizedWait = NormalizeWaitGroup(_waitGroup);
			if (normalizedWait >= 1)
			{
				var insertCommand = new QueuedCommand
				{
					Destination = sampledPoint,
					MoveTier = tier,
					FacingArrows = new List<FacingArrow>(),
				};
				AssignWaitMetadata(ref insertCommand, normalizedWait, _iconAtWaypoint: true);
				m_CommandQueue.Insert(0, promoteCommand);
				m_ActiveFacingArrows = null;
				ClearSmoothingArcState();
				m_HasActiveDestination = false;
				BindWaitHoldToRoute(ref insertCommand, 0);
				m_CommandQueue.Insert(0, insertCommand);

				NavMeshAgent agent = GetComponent<NavMeshAgent>();
				if (agent != null && agent.isOnNavMesh)
				{
					agent.isStopped = true;
					agent.ResetPath();
				}

				RebuildPathLine();
				MarkFacingArrowsDirty();
				TryStartNextQueuedCommand();
				return true;
			}

			m_CommandQueue.Insert(0, promoteCommand);
			m_ActiveFacingArrows = null;
			ClearSmoothingArcState();
			IssueMoveOrderForCurrentWaypoint(sampledPoint, m_ActiveMoveTier);
			RebuildPathLine();
			MarkFacingArrowsDirty();
			return true;
		}

		m_Waypoints.Insert(_segmentIndex, sampledPoint);

		if (m_HasActiveDestination)
		{
			int normalizedWait = NormalizeWaitGroup(_waitGroup);
			var insertCommand = new QueuedCommand
			{
				Destination = sampledPoint,
				MoveTier = tier,
				FacingArrows = new List<FacingArrow>(),
			};
			AssignWaitMetadata(ref insertCommand, normalizedWait, _iconAtWaypoint: true);
			int insertCommandIndex = _segmentIndex - 1;
			if (normalizedWait >= 1)
				BindWaitHoldToRoute(ref insertCommand, insertCommandIndex);
			m_CommandQueue.Insert(insertCommandIndex, insertCommand);
		}
		else
		{
			int normalizedWait = NormalizeWaitGroup(_waitGroup);
			var insertCommand = new QueuedCommand
			{
				Destination = sampledPoint,
				MoveTier = tier,
				FacingArrows = new List<FacingArrow>(),
			};
			AssignWaitMetadata(ref insertCommand, normalizedWait, _iconAtWaypoint: true);
			if (normalizedWait >= 1)
				BindWaitHoldToRoute(ref insertCommand, _segmentIndex);
			m_CommandQueue.Insert(_segmentIndex, insertCommand);
		}

		RebuildPathLine();
		MarkFacingArrowsDirty();
		return true;
	}

	public void UpdateRouteEditWaypoint(int _waypointIndex, Vector3 _worldPoint)
	{
		if (_waypointIndex < 0 || _waypointIndex >= m_Waypoints.Count)
			return;

		Vector3 sampledPoint = _worldPoint;
		TrySampleNavMeshPoint(_worldPoint, out sampledPoint);
		m_Waypoints[_waypointIndex] = sampledPoint;
		SyncCommandDestinationForWaypointIndex(_waypointIndex, sampledPoint);
		RebuildPathLine();
		MarkFacingArrowsDirty();

		if (m_HasActiveDestination && _waypointIndex == 0)
		{
			ClearSmoothingArcState();
			IssueMoveOrderForCurrentWaypoint(sampledPoint, m_ActiveMoveTier);
		}
	}

	public void SetWaypointFacing(
		int _index,
		float _angle,
		Vector3 _anchor,
		FacingArrowMode _mode = FacingArrowMode.TurnOverDistance,
		Vector3? _lookPoint = null,
		bool _forceReadyOnActivation = true,
		bool _activateAtSegmentStart = false)
	{
		bool hasLookPoint = _mode == FacingArrowMode.LookAtPoint && _lookPoint.HasValue;
		bool bindToActiveSegment = m_HasActiveDestination && _index == 0;
		Vector3 bindAnchor = _activateAtSegmentStart && bindToActiveSegment
			? transform.position
			: _anchor;
		var facingArrow = BindFacingArrowToRouteSegment(new FacingArrow
		{
			Angle = _angle,
			Mode = _mode,
			ForceReadyOnActivation = _forceReadyOnActivation,
			ActivateAtSegmentStart = _activateAtSegmentStart,
			HasLookPoint = hasLookPoint,
		}, _index, bindAnchor, hasLookPoint ? _lookPoint : null, bindToActiveSegment || _activateAtSegmentStart);

		int cmdIndex = _index;
		if (m_HasActiveDestination)
		{
			if (cmdIndex == 0)
			{
				AddFacingArrowToActiveSegment(facingArrow);
				return;
			}
			cmdIndex--;
		}
		if (cmdIndex < 0 || cmdIndex >= m_CommandQueue.Count)
			return;
		
		var cmd = m_CommandQueue[cmdIndex];
		if (cmd.FacingArrows == null)
			cmd.FacingArrows = new List<FacingArrow>();
		
		cmd.FacingArrows.Add(facingArrow);
		m_CommandQueue[cmdIndex] = cmd;
		MarkFacingArrowsDirty();
	}
	
	private void AddFacingArrowToActiveSegment(FacingArrow _arrow)
	{
		if (m_ActiveFacingArrows == null)
			m_ActiveFacingArrows = new List<FacingArrow>();
		
		m_ActiveFacingArrows.Add(_arrow);
		
		if (_arrow.ForceReadyOnActivation &&
		    m_ReadyHands != null &&
		    m_ReadyHands.IsWeaponEquipped() &&
		    !m_ReadyHands.WantsReady)
			m_ReadyHands.SetReadyWanted(true, false);

		if (_arrow.ActivateAtSegmentStart && m_HasActiveDestination)
			TryActivateSegmentStartFacingArrows();
		
		MarkFacingArrowsDirty();
	}

	public LineRenderer PathLine => m_PathLine;

	public void ClearWaypoints()
	{
		ClearFacingTurn();
		ClearSmoothingArcState();
		ClearRouteWaitState();
		ResetContinuousRouteLocomotionFlags();
		m_CommandQueue.Clear();
		m_Waypoints.Clear();
		ClearFacingArrows();
		m_ActiveFacingArrows = null;
		if (m_PathLine != null)
		{
			m_PathLine.positionCount = 0;
			m_PathLine.enabled = false;
		}
		m_HasActiveDestination = false;
		m_HasWantedFacing = false;
		ClearFacingOverride();
		ClearFormationSync();
		ClearFormationFacing();
	}

	public void SetFormationFacingAngle(float _sectorOffsetDegrees, float _frontAngleDegrees)
	{
		m_HasFormationFacingAngle = true;
		m_FormationFacingAngle = _sectorOffsetDegrees;
		m_FormationFrontFacingAngle = _frontAngleDegrees;
		EnsureFormationFacingVisual();
		ApplyFormationFacingIfNeeded();
	}

	public void ClearFormationFacing()
	{
		m_HasFormationFacingAngle = false;
		m_HasLastArrivalMovementAngle = false;
		if (m_FormationFacingArrowLine != null)
		{
			Destroy(m_FormationFacingArrowLine.gameObject);
			m_FormationFacingArrowLine = null;
		}
	}

	public void SetWantedFacingAngle(float _angle)
	{
		m_HasWantedFacing = true;
		m_WantedFacingAngle = _angle;
		m_IsRotatingToFacing = false;

		if (ShouldDeferRouteFacingOverride())
		{
			ClearFacingOverride();
			return;
		}

		if (m_ClickToMove != null || m_LocomotionDriver != null)
			ApplyLocomotionFacingOverride(_angle);
		else
			m_IsRotatingToFacing = true;
	}


	public void IssueInPlaceFacingOrder(float _angle, FacingArrowMode _mode = FacingArrowMode.TurnOverDistance)
	{
		ScheduleRtsCommand(() =>
		{
			ClearFacingTurn();
			ClearSmoothingArcState();
			ClearRouteWaitState();
			ResetContinuousRouteLocomotionFlags();
			m_CommandQueue.Clear();
			m_Waypoints.Clear();
			m_ActiveFacingArrows = null;
			m_HasActiveDestination = false;
			if (m_PathLine != null)
			{
				m_PathLine.positionCount = 0;
				m_PathLine.enabled = false;
			}

			if (m_ClickToMove != null)
				m_ClickToMove.HardStop();
			else if (m_LocomotionDriver != null)
				m_LocomotionDriver.HardStop();

			SetWantedFacingAngle(_angle);
		});
	}

	private void ClearFacingOverride()
	{
		if (m_ClickToMove != null)
			m_ClickToMove.OverrideFacingAngle = null;
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.OverrideFacingAngle = null;
	}

	public void SetReadyWanted(bool _ready)
	{
		ScheduleRtsCommand(() =>
		{
			UnitFiremanCarryController firemanCarry = ResolveFiremanCarryController();
			if (_ready && firemanCarry != null && firemanCarry.IsCarryingFallen)
				return;

			if (m_ReadyHands != null)
				m_ReadyHands.SetReadyWanted(_ready);

			if (!_ready && HasActiveLocomotionMovement() && !HasManualRouteFacingActive())
			{
				ClearFacingOverride();
				if (m_IsInFacingTurn)
					ClearFacingTurn();
			}

			if (_ready)
			{
				DowngradeActiveMovementTierForReady();
				ApplyFormationFacingIfNeeded();
			}
		});
	}

	private void DowngradeActiveMovementTierForReady()
	{
		if (!IsRunOrSprintMoveTier(m_ActiveMoveTier))
			return;

		m_ActiveMoveTier = UnitClickToMove.MoveTier.Walk;
		if (m_IsExecutingSmoothingArc)
			m_SmoothingMoveTier = UnitClickToMove.MoveTier.Walk;

		if (m_ClickToMove != null)
			m_ClickToMove.ForceWalkMoveMode();
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.ForceWalkMoveMode();
	}

	public void RequestStance(LocomotionStance _stance)
	{
		if (_stance == LocomotionStance.Prone && !LocomotionProneFeature.Enabled)
			return;

		ScheduleRtsCommand(() =>
		{
			if (_stance == LocomotionStance.Prone)
				m_MagazineLoadingController?.StopLoading();

			if (m_Stance != null)
				m_Stance.RequestStance(_stance);
		});
	}

	public void HardStop()
	{
		ScheduleRtsCommand(() =>
		{
			UnitSelfStabilizationController selfStabilization = ResolveSelfStabilizationController();
			selfStabilization?.StopSelfStabilization();

			UnitStabilizeOtherController stabilizeOther = ResolveStabilizeOtherController();
			stabilizeOther?.StopStabilizeOther();

			UnitFiremanCarryController firemanCarry = ResolveFiremanCarryController();
			firemanCarry?.RequestRelease();

			m_MagazineLoadingController?.StopLoading();
			m_WeaponReloadController?.StopReload();
			m_FireController?.StopFiring();

			ClearWaypoints();

			if (m_ClickToMove != null)
			{
				m_ClickToMove.HardStop();
				return;
			}

			if (m_LocomotionDriver != null)
				m_LocomotionDriver.HardStop();
		});
	}

	public void StartFiring()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				m_FireController.StartFiring();
		});
	}

	public void StopFiring()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				m_FireController.StopFiring();
		});
	}

	public WeaponShotAttemptResult TryFireSingleShot()
	{
		WeaponShotAttemptResult result = WeaponShotAttemptResult.NoWeapon;
		ScheduleRtsCommand(() =>
		{
			if (m_FireController != null)
				result = m_FireController.TryFireSingleShot();
		});

		return result;
	}

	public void StartManualMagazineLoading()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_MagazineLoadingController == null)
				return;

			m_MagazineLoadingController.TryStartLoadingMagazineFromAmmoBoxes();
		});
	}

	public void StartWeaponReload()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponReloadController == null)
				return;

			m_WeaponReloadController.TryStartReload();
		});
	}

	/// <summary>Следующий доступный режим огня по <see cref="WeaponDefinition.AvailableFireModes"/>.</summary>
	public void CycleWeaponFireMode()
	{
		ScheduleRtsCommand(() =>
		{
			m_FireController?.ResetBurstStateForFireModeChange();

			if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			{
				Debug.LogWarning($"{name}: смена режима огня — нет состояния оружия.", this);
				return;
			}

			WeaponFireMode before = m_WeaponRuntime.RuntimeState.SelectedFireMode;
			if (!m_WeaponRuntime.TryCycleToNextFireMode())
			{
				Debug.Log($"{name}: режим огня не изменён (один доступный режим или нет экипированного оружия). Сейчас: {before}.", this);
				return;
			}

			WeaponFireMode after = m_WeaponRuntime.RuntimeState.SelectedFireMode;
			WeaponFireMode effectiveAfter = m_FireController != null
				? m_FireController.ResolveEffectiveFireMode()
				: after;
			string afterLabel = after == WeaponFireMode.Auto
				? $"{WeaponFireModeUtility.GetDisplayName(after)}→{WeaponFireModeUtility.GetDisplayName(effectiveAfter)}"
				: WeaponFireModeUtility.GetDisplayName(after);
			Debug.Log(
				$"{name}: режим огня {WeaponFireModeUtility.GetDisplayName(before)} → {afterLabel}.",
				this);
			PlayFireModeSwitchSound();
		});
	}

	/// <summary>Следующий режим прицеливания юнита: полное, быстрое, на вскидку, авто.</summary>
	public void CycleWeaponAimMode()
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponRuntime == null)
			{
				Debug.LogWarning($"{name}: смена режима прицеливания — нет runtime оружия.", this);
				return;
			}

			WeaponAimMode before = m_WeaponRuntime.SelectedAimMode;
			if (!m_WeaponRuntime.TryCycleToNextAimMode(out WeaponAimMode after))
			{
				Debug.Log($"{name}: режим прицеливания не изменён. Сейчас: {before}.", this);
				return;
			}

			Debug.Log(
				$"{name}: режим прицеливания {WeaponAimModeUtility.GetDisplayName(before)} → {WeaponAimModeUtility.GetDisplayName(after)} " +
				$"(порог выстрела: {WeaponAimModeUtility.GetRequiredAimProgress01(after, 0f):P0}; в авто порог зависит от дистанции).",
				this);
			PlayFireModeSwitchSound();
		});
	}

	private void PlayFireModeSwitchSound()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (weaponDefinition == null || !weaponDefinition.TryPickFireModeSwitchSound(out AudioClip clip))
			return;

		Vector3 position = transform.position + Vector3.up * 1.35f;
		if (m_UnitEquipment != null && m_UnitEquipment.MainWeaponRoot != null)
			position = m_UnitEquipment.MainWeaponRoot.position;

		float volume = weaponDefinition.FireModeSwitchSoundVolume;
		AudioSource.PlayClipAtPoint(clip, position, volume);
	}

	public bool TryGetCurrentStance(out LocomotionStance _stance)
	{
		if (m_Stance == null)
		{
			_stance = LocomotionStance.Standing;
			return false;
		}

		_stance = m_Stance.CurrentStance;
		return true;
	}

	public bool TryGetSelectionBounds(out Bounds _bounds)
	{
		if (m_SelectionCollider != null)
		{
			_bounds = m_SelectionCollider.bounds;
			return true;
		}

		_bounds = new Bounds(transform.position, Vector3.one);
		return true;
	}

	public void ApplyDirectInputState(bool _enabled)
	{
		if (m_ClickToMove != null)
			m_ClickToMove.SetDirectInputEnabled(_enabled);
		if (m_Stance != null)
			m_Stance.SetKeyboardInputEnabled(_enabled);
		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(_enabled);
	}

	public void ClearCommandQueue()
	{
		ClearSmoothingArcState();
		m_CommandQueue.Clear();
		if (m_HasActiveDestination && m_Waypoints.Count > 0)
			m_Waypoints.RemoveRange(1, m_Waypoints.Count - 1);
		if (m_IsWaitingAtRouteGate)
			ClearRouteWaitState();
		MarkFacingArrowsDirty();
	}

	#endregion

	#region Private Methods
	private UnitSelfStabilizationController ResolveSelfStabilizationController()
	{
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();

		return m_SelfStabilizationController;
	}

	private UnitStabilizeOtherController ResolveStabilizeOtherController()
	{
		if (m_StabilizeOtherController == null)
			m_StabilizeOtherController = GetComponent<UnitStabilizeOtherController>();

		return m_StabilizeOtherController;
	}

	private UnitFiremanCarryController ResolveFiremanCarryController()
	{
		if (m_FiremanCarryController == null)
			m_FiremanCarryController = GetComponent<UnitFiremanCarryController>();

		return m_FiremanCarryController;
	}

	private void DequeueAndExecuteNextCommand()
	{
		bool persistFacingTurn = ShouldPersistFacingTurnAcrossQueuedCommand();
		if (!persistFacingTurn)
			ClearFacingTurn();

		ClearSmoothingArcState();
		if (m_CommandQueue.Count == 0)
			return;

		QueuedCommand cmd = m_CommandQueue[0];
		m_CommandQueue.RemoveAt(0);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"DEQUEUE tier={cmd.MoveTier} dest={FormatRoutePoint(cmd.Destination)} {BuildRouteDebugSnapshot()}");
#endif

		m_ActiveFacingArrows = cmd.FacingArrows != null && cmd.FacingArrows.Count > 0
			? new List<FacingArrow>(cmd.FacingArrows) 
			: null;
		
		if (m_ActiveFacingArrows != null && HasReadyForcingFacingArrow(m_ActiveFacingArrows)
		    && m_ReadyHands != null && m_ReadyHands.IsWeaponEquipped() && !m_ReadyHands.WantsReady)
			m_ReadyHands.SetReadyWanted(true, false);

		if (!persistFacingTurn)
		{
			ClearFacingOverride();
			m_HasWantedFacing = false;
		}

		m_HasActiveDestination = true;
		m_ActiveMoveTier = cmd.MoveTier;
		m_ActiveRouteSegmentStart = transform.position;
		m_HasLastArrivalMovementAngle = false;
		m_DestinationSetTime = Time.time;

		IssueMoveOrderForCurrentWaypoint(cmd.Destination, cmd.MoveTier);

		TryActivateSegmentStartFacingArrows();

		MarkFacingArrowsDirty();
	}

	private void TryActivateSegmentStartFacingArrows()
	{
		if (!m_HasActiveDestination || m_ActiveFacingArrows == null || m_ActiveFacingArrows.Count == 0)
			return;

		for (int i = m_ActiveFacingArrows.Count - 1; i >= 0; i--)
		{
			FacingArrow arrow = m_ActiveFacingArrows[i];
			if (!arrow.ActivateAtSegmentStart)
				continue;

			Vector3? lookWorld = arrow.HasLookPoint
				? ResolveFacingArrowLookPoint(arrow, _isActiveSegment: true)
				: (Vector3?)null;

			m_ActiveFacingArrows.RemoveAt(i);

			FacingArrow rebound = BindFacingArrowToRouteSegment(
				arrow,
				0,
				transform.position,
				lookWorld,
				_useActiveSegmentStart: true);
			rebound.ActivateAtSegmentStart = false;

			if (IsRunOrSprintMoveTier(m_ActiveMoveTier))
				TransitionActiveMovementToWalk();

			StartFacingTurn(rebound, transform.position, _isActiveSegment: true);
		}
	}

	private void IssueMoveOrderForCurrentWaypoint(Vector3 _logicalDestination, UnitClickToMove.MoveTier _moveTier)
	{
		if (TryBeginSmoothingArcMovement(_moveTier))
			return;

		bool continuous = m_PreferContinuousNextMoveOrder || ShouldUseContinuousRouteMovement();
		m_PreferContinuousNextMoveOrder = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (!continuous && HasRouteAfterCurrentSegment())
		{
			NavMeshAgent agent = GetComponent<NavMeshAgent>();
			float speed = agent != null
				? new Vector3(agent.velocity.x, 0f, agent.velocity.z).magnitude
				: 0f;
			LogRouteDebugEvent(
				$"CONTINUOUS_SKIPPED speed={speed:F2} dest={FormatRoutePoint(_logicalDestination)} {BuildRouteDebugSnapshot()}");
		}
#endif
		IssueRouteMoveOrder(_logicalDestination, _moveTier, continuous);
	}

	private bool ShouldUseContinuousRouteMovement()
	{
		if (!m_HasActiveDestination)
			return false;
		if (m_IsWaitingAtRouteGate)
			return false;
		return HasRouteAfterCurrentSegment();
	}

	private bool HasRouteAfterCurrentSegment()
	{
		if (m_IsExecutingSmoothingArc && m_SmoothingSubtargetIndex < m_SmoothingSubtargets.Count - 1)
			return true;
		if (m_CommandQueue.Count > 0)
			return true;
		return m_Waypoints.Count > 1;
	}

	private bool IsIntermediateRouteSegment()
	{
		if (m_IsExecutingSmoothingArc)
		{
			if (m_SmoothingSubtargetIndex < m_SmoothingSubtargets.Count - 1)
				return true;

			return m_CommandQueue.Count > 0 || m_Waypoints.Count > 1;
		}

		return m_CommandQueue.Count > 0 || m_Waypoints.Count > 1;
	}

	private void UpdateContinuousRouteLocomotionFlags()
	{
		bool suppressEarlyStop = IsIntermediateRouteSegment();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (suppressEarlyStop != m_RouteDebugLastSuppressEarlyStop)
		{
			m_RouteDebugLastSuppressEarlyStop = suppressEarlyStop;
			LogRouteDebugEvent(
				$"SUPPRESS_EARLY_STOP={(suppressEarlyStop ? "on" : "off")} intermediate={suppressEarlyStop} {BuildRouteDebugSnapshot()}");
		}
#endif
		if (m_ClickToMove != null)
			m_ClickToMove.SuppressEarlyArrivalStop = suppressEarlyStop;
		if (m_LocomotionDriver != null)
			m_LocomotionDriver.SuppressEarlyArrivalStop = suppressEarlyStop;
	}

	private static bool IsNearDestination(Vector3 _a, Vector3 _b, float _epsilon)
	{
		Vector3 flatA = FlattenToGround(_a);
		Vector3 flatB = FlattenToGround(_b);
		return (flatB - flatA).sqrMagnitude <= _epsilon * _epsilon;
	}

	private bool HasReachedActiveDestination(NavMeshAgent _agent)
	{
		if (_agent == null)
			return false;

		Vector3 velocity = _agent.velocity;
		velocity.y = 0f;
		if (velocity.sqrMagnitude > 0.01f)
			return false;

		if (!_agent.hasPath)
			return true;

		if (float.IsPositiveInfinity(_agent.remainingDistance) || _agent.remainingDistance > 0.2f)
			return false;

		if (m_Waypoints.Count == 0)
			return true;

		return IsNearDestination(transform.position, m_Waypoints[0], 0.5f);
	}

	private bool HasReachedCurrentSmoothingSubtarget(NavMeshAgent _agent)
	{
		if (!m_IsExecutingSmoothingArc || m_SmoothingSubtargets.Count == 0)
			return false;
		if (m_SmoothingSubtargetIndex < 0 || m_SmoothingSubtargetIndex >= m_SmoothingSubtargets.Count)
			return false;

		if (m_DestinationSetTime >= 0f && Time.time - m_DestinationSetTime < 0.25f)
			return false;

		Vector3 target = m_SmoothingSubtargets[m_SmoothingSubtargetIndex];
		if (!IsNearDestination(transform.position, target, 0.35f))
			return false;

		if (_agent != null && _agent.hasPath && !float.IsPositiveInfinity(_agent.remainingDistance) &&
		    _agent.remainingDistance > 0.25f)
			return false;

		Vector3 velocity = _agent != null ? _agent.velocity : Vector3.zero;
		velocity.y = 0f;
		return velocity.sqrMagnitude <= 0.0025f;
	}

	private static bool IsRunOrSprintMoveTier(UnitClickToMove.MoveTier _tier)
	{
		return _tier == UnitClickToMove.MoveTier.Run || _tier == UnitClickToMove.MoveTier.Sprint;
	}

	private void ResetActiveMoveTierWhenIdle()
	{
		if (m_HasActiveDestination || m_Waypoints.Count > 0 || m_CommandQueue.Count > 0)
			return;
		if (m_IsWaitingAtRouteGate || m_IsExecutingSmoothingArc)
			return;

		if (IsRunOrSprintMoveTier(m_ActiveMoveTier))
			m_ActiveMoveTier = UnitClickToMove.MoveTier.Walk;
	}

	private void TransitionActiveMovementToWalk()
	{
		if (!IsRunOrSprintMoveTier(m_ActiveMoveTier))
			return;

		m_ActiveMoveTier = UnitClickToMove.MoveTier.Walk;

		if (m_IsExecutingSmoothingArc)
			m_SmoothingMoveTier = UnitClickToMove.MoveTier.Walk;

		ClearFacingOverride();

		if (m_ClickToMove != null)
			m_ClickToMove.ForceWalkMoveMode();
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.ForceWalkMoveMode();
	}

	private bool ShouldDeferRouteFacingOverride()
	{
		if (!m_HasActiveDestination)
			return false;

		UnitClickToMove.MoveTier activeTier = m_IsExecutingSmoothingArc
			? m_SmoothingMoveTier
			: m_ActiveMoveTier;
		return IsRunOrSprintMoveTier(activeTier);
	}

	private bool TryBeginSmoothingArcMovement(UnitClickToMove.MoveTier _moveTier)
	{
		if (!m_EnableCornerSmoothingMovement || !m_EnableRouteCornerSmoothing || m_Waypoints.Count < 2)
			return false;

		if (m_FormationSyncGroup != null && m_FormationSyncGroup.Members.Count > 1)
			return false;

		Vector3 previousPoint = transform.position;
		Vector3 corner = m_Waypoints[0];
		Vector3 nextPoint = m_Waypoints[1];

		float distToCorner = (FlattenToGround(transform.position - corner)).magnitude;
		if (distToCorner > m_ContinuousRouteLookaheadDistance + 0.25f)
			return false;

		if (!TryBuildMovementSubtargets(previousPoint, corner, nextPoint, m_SmoothingSubtargets))
			return false;

		m_IsExecutingSmoothingArc = true;
		m_SmoothingMoveTier = _moveTier;
		m_SmoothingSubtargetIndex = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent(
			$"ARC_BEGIN samples={m_SmoothingSubtargets.Count} corner={FormatRoutePoint(corner)} next={FormatRoutePoint(nextPoint)} {BuildRouteDebugSnapshot()}");
#endif
		IssueRouteMoveOrder(m_SmoothingSubtargets[0], _moveTier, ShouldUseContinuousRouteMovement());
		return true;
	}

	private void UpdateActiveFacingArrows()
	{
		if (!m_HasActiveDestination || m_ActiveFacingArrows == null || m_ActiveFacingArrows.Count == 0)
		{
			if (m_HasActiveDestination &&
			    m_HasWantedFacing &&
			    m_ActiveFacingArrows == null &&
			    !m_IsInFacingTurn)
			{
				ClearFacingOverride();
				m_HasWantedFacing = false;
			}
			return;
		}

		Vector3 unitPos = transform.position;
		float closestDist = float.MaxValue;
		int closestIndex = -1;

		for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
		{
			Vector3 arrowPos = ResolveFacingArrowAnchor(m_ActiveFacingArrows[i], _isActiveSegment: true);
			float dx = unitPos.x - arrowPos.x;
			float dz = unitPos.z - arrowPos.z;
			float dist = Mathf.Sqrt(dx * dx + dz * dz);

			if (dist < closestDist)
			{
				closestDist = dist;
				closestIndex = i;
			}
		}

		if (closestIndex < 0)
			return;

		if (closestDist <= FacingArrowActivationDistance)
		{
			FacingArrow arrow = m_ActiveFacingArrows[closestIndex];
			m_ActiveFacingArrows.RemoveAt(closestIndex);

			if (IsRunOrSprintMoveTier(m_ActiveMoveTier))
				TransitionActiveMovementToWalk();

			StartFacingTurn(arrow, unitPos, _isActiveSegment: true);
			MarkFacingArrowsDirty();
		}
		else if (m_HasWantedFacing && !m_IsInFacingTurn)
		{
			ClearFacingOverride();
			m_HasWantedFacing = false;
		}
	}

	private void StartFacingTurn(FacingArrow _arrow, Vector3 _unitPos, bool _isActiveSegment = false)
	{
		switch (_arrow.Mode)
		{
			case FacingArrowMode.TurnOverDistance:
				m_FacingTurnMode = FacingArrowMode.TurnOverDistance;
				m_FacingTurnStartPos = _unitPos;
				m_FacingTurnStartAngle = transform.eulerAngles.y;
				m_FacingTurnTargetAngle = _arrow.Angle;
				m_FacingTurnDistanceTraveled = 0f;
				m_IsInFacingTurn = true;
				break;

			case FacingArrowMode.HoldToEnd:
				m_FacingTurnMode = FacingArrowMode.HoldToEnd;
				m_FacingTurnTargetAngle = _arrow.Angle;
				SetWantedFacingAngle(_arrow.Angle);
				m_IsInFacingTurn = true;
				break;

			case FacingArrowMode.LookAtPoint:
				m_FacingTurnMode = FacingArrowMode.LookAtPoint;
				m_FacingLookPoint = _arrow.HasLookPoint
					? ResolveFacingArrowLookPoint(_arrow, _isActiveSegment)
					: ResolveFacingArrowAnchor(_arrow, _isActiveSegment) +
					  Quaternion.Euler(0f, _arrow.Angle, 0f) * Vector3.forward * c_FacingArrowFixedLength;
				m_IsInFacingTurn = true;
				break;
		}
	}

	private void UpdateFacingTurn()
	{
		if (!m_IsInFacingTurn)
			return;

		if (!m_HasActiveDestination)
		{
			ClearFacingTurn();
			return;
		}

		if (IsRunOrSprintMoveTier(m_ActiveMoveTier))
			return;

		switch (m_FacingTurnMode)
		{
			case FacingArrowMode.TurnOverDistance:
			{
				float dx = transform.position.x - m_FacingTurnStartPos.x;
				float dz = transform.position.z - m_FacingTurnStartPos.z;
				float dist = Mathf.Sqrt(dx * dx + dz * dz);
				m_FacingTurnDistanceTraveled = dist;

				ApplyLocomotionFacingOverride(m_FacingTurnTargetAngle);

				if (dist >= m_FacingTurnOverDistance)
					ClearFacingTurn();
				break;
			}

			case FacingArrowMode.HoldToEnd:
				ApplyLocomotionFacingOverride(m_FacingTurnTargetAngle);
				break;

			case FacingArrowMode.LookAtPoint:
			{
				Vector3 toLook = m_FacingLookPoint - transform.position;
				toLook.y = 0f;
				if (toLook.sqrMagnitude > 0.01f)
				{
					float angle = Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg;
					ApplyLocomotionFacingOverride(angle);
				}
				break;
			}
		}
	}

	private void ClearFacingTurn()
	{
		m_IsInFacingTurn = false;
		m_FacingTurnMode = FacingArrowMode.TurnOverDistance;
		ClearFacingOverride();
		m_HasWantedFacing = false;
		m_FacingRotateVelocity = 0f;
		m_FacingSuppressedReady = false;
	}

	private float GetEffectiveRotateSpeed()
	{
		if (m_ClickToMove != null)
			return m_ClickToMove.RotateSpeed;
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.RotateSpeed;
		return 6f;
	}

	private void ApplyLocomotionFacingOverride(float _angle)
	{
		if (m_ClickToMove != null)
			m_ClickToMove.OverrideFacingAngle = _angle;
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.OverrideFacingAngle = _angle;
	}

	private bool HasActiveLocomotionMovement()
	{
		if (m_HasActiveDestination)
			return true;

		if (IsExecutingMoveOrder())
			return true;

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.enabled)
		{
			Vector3 velocity = agent.velocity;
			velocity.y = 0f;
			if (velocity.sqrMagnitude > 0.01f)
				return true;
		}

		return false;
	}

	private static bool HasReadyForcingFacingArrow(List<FacingArrow> _arrows)
	{
		if (_arrows == null)
			return false;

		for (int i = 0; i < _arrows.Count; i++)
		{
			if (_arrows[i].ForceReadyOnActivation)
				return true;
		}

		return false;
	}

	private bool ShouldPersistFacingTurnAcrossQueuedCommand()
	{
		return m_IsInFacingTurn &&
		       (m_FacingTurnMode == FacingArrowMode.HoldToEnd ||
		        m_FacingTurnMode == FacingArrowMode.LookAtPoint);
	}

	private bool ShouldClearFacingOnLegArrival()
	{
		if (m_IsInFacingTurn)
		{
			if (m_FacingTurnMode == FacingArrowMode.HoldToEnd ||
			    m_FacingTurnMode == FacingArrowMode.LookAtPoint)
				return m_CommandQueue.Count == 0;

			return true;
		}

		return m_HasWantedFacing;
	}

	private void MarkFacingArrowsDirty()
	{
		m_FacingArrowsDirty = true;
	}

	private void SyncFacingArrows()
	{
		if (!m_FacingArrowsDirty)
			return;
		m_FacingArrowsDirty = false;

		for (int i = 0; i < m_FacingArrowVisuals.Count; i++)
		{
			if (m_FacingArrowVisuals[i].Line != null)
				Destroy(m_FacingArrowVisuals[i].Line.gameObject);
		}
		m_FacingArrowVisuals.Clear();

		if (m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
				CreateFacingArrowVisual(_isActiveSegment: true, _commandIndex: 0, _arrowIndex: i);
		}

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			if (m_CommandQueue[commandIndex].FacingArrows == null)
				continue;

			for (int arrowIndex = 0; arrowIndex < m_CommandQueue[commandIndex].FacingArrows.Count; arrowIndex++)
				CreateFacingArrowVisual(_isActiveSegment: false, commandIndex, arrowIndex);
		}
	}

	public static void GetFacingArrowShaftEndpoints(
		Vector3 _anchor,
		float _angle,
		FacingArrowMode _mode,
		Vector3 _lookPoint,
		bool _hasLookPoint,
		out Vector3 _shaftStart,
		out Vector3 _shaftEnd)
	{
		Vector3 dir = Quaternion.Euler(0f, _angle, 0f) * Vector3.forward;
		_shaftStart = _anchor + dir * c_FacingArrowShaftStartOffset + s_FacingArrowYOffset;
		if (_mode == FacingArrowMode.LookAtPoint && _hasLookPoint)
			_shaftEnd = _lookPoint + s_FacingArrowYOffset;
		else
			_shaftEnd = _anchor + dir * c_FacingArrowFixedLength + s_FacingArrowYOffset;
	}

	private void CreateFacingArrowVisual(bool _isActiveSegment, int _commandIndex, int _arrowIndex)
	{
		if (s_PathLineMaterial == null)
			return;
		if (!TryGetFacingArrow(_isActiveSegment, _commandIndex, _arrowIndex, out FacingArrow arrow))
			return;

		Vector3 anchor = ResolveFacingArrowAnchor(arrow, _isActiveSegment);
		Vector3 lookPoint = arrow.HasLookPoint ? ResolveFacingArrowLookPoint(arrow, _isActiveSegment) : Vector3.zero;
		Color arrowColor = GetFacingArrowColor(arrow.Mode);
		GetFacingArrowShaftEndpoints(
			anchor,
			arrow.Angle,
			arrow.Mode,
			lookPoint,
			arrow.HasLookPoint,
			out Vector3 shaftStart,
			out Vector3 shaftEnd);

		GameObject go = new GameObject("FacingArrow");
		go.transform.SetParent(transform, false);
		LineRenderer lr = go.AddComponent<LineRenderer>();
		lr.positionCount = 2;
		lr.startWidth = 0.02f;
		lr.endWidth = 0.02f;
		lr.sharedMaterial = s_PathLineMaterial;
		lr.startColor = arrowColor;
		lr.endColor = arrowColor;
		lr.enabled = m_IsSelected;
		lr.SetPosition(0, shaftStart);
		lr.SetPosition(1, shaftEnd);

		m_FacingArrowVisuals.Add(new FacingArrowVisualSource
		{
			Line = lr,
			IsActiveSegment = _isActiveSegment,
			CommandIndex = _commandIndex,
			ArrowIndex = _arrowIndex,
		});
	}

	private static Color GetFacingArrowColor(FacingArrowMode _mode)
	{
		return _mode switch
		{
			FacingArrowMode.HoldToEnd => s_FacingArrowHoldColor,
			FacingArrowMode.LookAtPoint => s_FacingArrowLookColor,
			_ => s_FacingArrowColor,
		};
	}

	private void UpdateFacingArrows()
	{
		for (int i = 0; i < m_FacingArrowVisuals.Count; i++)
		{
			FacingArrowVisualSource source = m_FacingArrowVisuals[i];
			LineRenderer line = source.Line;
			if (line == null)
				continue;
			if (!TryGetFacingArrow(source.IsActiveSegment, source.CommandIndex, source.ArrowIndex, out FacingArrow arrow))
				continue;

			line.enabled = m_IsSelected;

			Vector3 anchor = ResolveFacingArrowAnchor(arrow, source.IsActiveSegment);
			Vector3 lookPoint = arrow.HasLookPoint ? ResolveFacingArrowLookPoint(arrow, source.IsActiveSegment) : Vector3.zero;
			GetFacingArrowShaftEndpoints(
				anchor,
				arrow.Angle,
				arrow.Mode,
				lookPoint,
				arrow.HasLookPoint,
				out Vector3 shaftStart,
				out Vector3 shaftEnd);
			line.SetPosition(0, shaftStart);
			line.SetPosition(1, shaftEnd);
		}
	}

	private void ClearFacingArrows()
	{
		for (int i = 0; i < m_FacingArrowVisuals.Count; i++)
		{
			if (m_FacingArrowVisuals[i].Line != null)
				Destroy(m_FacingArrowVisuals[i].Line.gameObject);
		}
		m_FacingArrowVisuals.Clear();
		m_FacingArrowsDirty = false;
	}

	private bool HasManualRouteFacingActive()
	{
		if (m_IsInFacingTurn)
			return true;
		if (m_ActiveFacingArrows != null && m_ActiveFacingArrows.Count > 0)
			return true;
		if (m_HasWantedFacing)
			return true;
		return false;
	}

	private bool ShouldApplyFormationFacing()
	{
		if (!m_HasFormationFacingAngle)
			return false;
		if (IsRunOrSprintMoveTier(m_ActiveMoveTier) && !WantsReady)
			return false;
		if (HasManualRouteFacingActive())
			return false;
		// Sector formation front applies on arrival, not while moving to the destination.
		if (m_HasActiveDestination)
			return false;
		return true;
	}

	private void ApplyFormationFacingIfNeeded()
	{
		if (!ShouldApplyFormationFacing())
			return;

		float facingAngle = WantsReady
			? ResolveReadyFormationSectorWorldAngle()
			: m_FormationFrontFacingAngle;
		ApplyLocomotionFacingOverride(facingAngle);
	}

	private float ResolveReadyFormationSectorWorldAngle()
	{
		return ResolveMovementFacingBaseAngle() + m_FormationFacingAngle;
	}

	private float ResolveMovementFacingBaseAngle()
	{
		if (!m_HasActiveDestination && m_HasLastArrivalMovementAngle)
			return m_LastArrivalMovementAngle;

		if (m_HasActiveDestination && m_Waypoints.Count > 0)
		{
			Vector3 dest = m_Waypoints[0];
			Vector3 moveDir = dest - m_ActiveRouteSegmentStart;
			moveDir.y = 0f;
			if (moveDir.sqrMagnitude > 0.01f)
				return Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
		}

		return transform.eulerAngles.y;
	}

	private void UpdateFormationFacing()
	{
		if (ShouldApplyFormationFacing())
			ApplyFormationFacingIfNeeded();
		else if (HasActiveLocomotionMovement() && !HasManualRouteFacingActive() && !m_HasWantedFacing &&
		         !m_HasFormationFacingAngle)
			ClearFacingOverride();

		bool showVisual = m_HasFormationFacingAngle
		                  && m_IsSelected
		                  && WantsReady
		                  && !IsRunOrSprintMoveTier(m_ActiveMoveTier);
		UpdateFormationFacingVisual(showVisual);
	}

	private void EnsureFormationFacingVisual()
	{
		if (m_FormationFacingArrowLine != null || s_PathLineMaterial == null)
			return;

		GameObject go = new GameObject("FormationFacingArrow");
		go.transform.SetParent(transform, false);
		LineRenderer line = go.AddComponent<LineRenderer>();
		line.positionCount = 2;
		line.startWidth = 0.018f;
		line.endWidth = 0.018f;
		line.material = s_PathLineMaterial;
		line.startColor = s_FormationFacingArrowColor;
		line.endColor = s_FormationFacingArrowColor;
		line.enabled = false;
		m_FormationFacingArrowLine = line;
	}

	private void UpdateFormationFacingVisual(bool _visible)
	{
		if (!m_HasFormationFacingAngle)
		{
			if (m_FormationFacingArrowLine != null)
				m_FormationFacingArrowLine.enabled = false;
			return;
		}

		EnsureFormationFacingVisual();
		if (m_FormationFacingArrowLine == null)
			return;

		bool show = _visible && m_IsSelected && WantsReady;
		m_FormationFacingArrowLine.enabled = show;
		if (!show)
			return;

		float worldSectorAngle = ResolveReadyFormationSectorWorldAngle();
		Vector3 dir = Quaternion.Euler(0f, worldSectorAngle, 0f) * Vector3.forward;
		Vector3 anchor = transform.position + s_FacingArrowYOffset;
		m_FormationFacingArrowLine.SetPosition(0, anchor + dir * c_FacingArrowShaftStartOffset);
		m_FormationFacingArrowLine.SetPosition(1, anchor + dir * (c_FacingArrowFixedLength * 0.85f));
	}

	private void ScheduleRtsCommand(Action _command)
	{
		if (_command == null)
			return;

		m_PendingCommandVersion++;

		if (m_PendingCommandCoroutine != null)
			StopCoroutine(m_PendingCommandCoroutine);

		m_PendingCommandCoroutine = null;
		_command();
	}

	private void CancelPendingCommand()
	{
		m_PendingCommandVersion++;
		if (m_PendingCommandCoroutine == null)
			return;

		StopCoroutine(m_PendingCommandCoroutine);
		m_PendingCommandCoroutine = null;
	}

	private void EnsureSelectionNameLabel()
	{
		if (m_SelectionNameLabelRoot != null && m_SelectionNameText != null)
			return;

		if (m_SelectionNameLabelRoot == null)
		{
			m_SelectionNameLabelRoot = new GameObject("SelectionNameLabel", typeof(RectTransform));
			RectTransform rt = m_SelectionNameLabelRoot.GetComponent<RectTransform>();
			rt.SetParent(transform, false);
			rt.sizeDelta = new Vector2(2f, 0.5f);

			Canvas canvas = m_SelectionNameLabelRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.sortingOrder = 31500;

			m_SelectionNameLabelRoot.AddComponent<UnityEngine.UI.GraphicRaycaster>();
		}

		if (m_SelectionNameText == null)
		{
			GameObject textGo = new GameObject("NameText", typeof(RectTransform));
			RectTransform textRt = textGo.GetComponent<RectTransform>();
			textRt.SetParent(m_SelectionNameLabelRoot.transform, false);
			textRt.anchorMin = Vector2.zero;
			textRt.anchorMax = Vector2.one;
			textRt.offsetMin = Vector2.zero;
			textRt.offsetMax = Vector2.zero;

			m_SelectionNameText = textGo.AddComponent<TextMeshProUGUI>();
			m_SelectionNameText.fontSize = 0.15f;
			m_SelectionNameText.alignment = TextAlignmentOptions.Center;
			m_SelectionNameText.color = Color.white;
			m_SelectionNameText.outlineWidth = 0.35f;
			m_SelectionNameText.outlineColor = Color.black;
			m_SelectionNameText.fontStyle = FontStyles.Bold;
		}
	}

	private void RefreshSelectionNameLabel()
	{
		if (m_SelectionNameText == null)
			return;

		if (m_RosterDisplay == null)
			m_RosterDisplay = UnitRosterDisplayState.GetOrCreate(gameObject);

		m_SelectionNameText.text = m_RosterDisplay != null ? m_RosterDisplay.FullName : gameObject.name;
	}

	private void UpdateSelectionLabelBillboard()
	{
		if (m_SelectionNameLabelRoot == null || !m_SelectionNameLabelRoot.activeSelf)
			return;

		if (m_CachedCameraTransform == null)
		{
			Camera cam = Camera.main;
			if (cam != null)
				m_CachedCameraTransform = cam.transform;
			else
				return;
		}

		Transform labelTransform = m_SelectionNameLabelRoot.transform;
		Vector3 worldPos = transform.position + Vector3.up * m_SelectionLabelHeight;
		labelTransform.position = worldPos;
		labelTransform.rotation = m_CachedCameraTransform.rotation;
	}

	private void ApplyAnimatorSpeedVariation()
	{
		if (m_Animator == null)
			return;

		float playbackSync = 1f;
		if (m_LocomotionDriver != null)
			playbackSync = m_LocomotionDriver.AnimatorPlaybackSpeedMultiplier;
		else if (m_ClickToMove != null)
			playbackSync = m_ClickToMove.AnimatorPlaybackSpeedMultiplier;

		m_Animator.speed = IsExecutingMoveOrder()
			? m_RuntimeMoveAnimatorSpeed * playbackSync
			: 1f;
	}

	private bool IsExecutingMoveOrder()
	{
		if (m_ClickToMove != null)
			return m_ClickToMove.HasMoveIntent;
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.HasMoveIntent;

		return false;
	}

	private void ResetAnimatorSpeed()
	{
		if (m_Animator != null)
			m_Animator.speed = 1f;
	}

	#region Route Corner Smoothing
	private UnitClickToMove.MoveTier ResolveMoveTierForWaypointInsert(int _segmentIndex)
	{
		if (m_HasActiveDestination)
		{
			if (_segmentIndex == 0)
				return m_ActiveMoveTier;

			int previousQueueIndex = _segmentIndex - 2;
			if (previousQueueIndex >= 0 && previousQueueIndex < m_CommandQueue.Count)
				return m_CommandQueue[previousQueueIndex].MoveTier;

			int queueIndex = _segmentIndex - 1;
			if (queueIndex >= 0 && queueIndex < m_CommandQueue.Count)
				return m_CommandQueue[queueIndex].MoveTier;
		}
		else if (_segmentIndex > 0 && _segmentIndex - 1 < m_CommandQueue.Count)
		{
			return m_CommandQueue[_segmentIndex - 1].MoveTier;
		}
		else if (_segmentIndex < m_CommandQueue.Count)
		{
			return m_CommandQueue[_segmentIndex].MoveTier;
		}

		return UnitClickToMove.MoveTier.Walk;
	}

	private void SyncCommandDestinationForWaypointIndex(int _waypointIndex, Vector3 _destination)
	{
		if (m_HasActiveDestination)
		{
			if (_waypointIndex == 0)
				return;

			int queueIndex = _waypointIndex - 1;
			if (queueIndex < 0 || queueIndex >= m_CommandQueue.Count)
				return;

			QueuedCommand cmd = m_CommandQueue[queueIndex];
			cmd.Destination = _destination;
			m_CommandQueue[queueIndex] = cmd;
			return;
		}

		if (_waypointIndex < 0 || _waypointIndex >= m_CommandQueue.Count)
			return;

		QueuedCommand queuedCommand = m_CommandQueue[_waypointIndex];
		queuedCommand.Destination = _destination;
		m_CommandQueue[_waypointIndex] = queuedCommand;
	}

	private static int NormalizeWaitGroup(int _waitGroup)
	{
		if (_waitGroup < 1)
			return 0;
		if (_waitGroup > 3)
			return 3;
		return _waitGroup;
	}

	private Vector3 ComputeAutoWaitHoldWorldPosition(int _commandIndex)
	{
		if (_commandIndex <= 0 && !m_HasActiveDestination)
			return transform.position;

		int holdWaypointIndex = m_HasActiveDestination ? _commandIndex : _commandIndex - 1;
		if (holdWaypointIndex >= 0 && holdWaypointIndex < m_Waypoints.Count)
			return m_Waypoints[holdWaypointIndex];

		return transform.position;
	}

	private void AssignWaitMetadata(
		ref QueuedCommand _command,
		int _waitGroup,
		bool _iconAtWaypoint,
		bool _preserveIconPlacement = false)
	{
		int normalizedGroup = NormalizeWaitGroup(_waitGroup);
		_command.WaitGroup = normalizedGroup;
		if (normalizedGroup < 1)
		{
			_command.WaitIconAtWaypoint = false;
			_command.HasWaitRouteBinding = false;
			return;
		}

		if (!_preserveIconPlacement)
			_command.WaitIconAtWaypoint = _iconAtWaypoint;
	}

	private void ClearRouteWaitState()
	{
		m_ActiveWaitGroup = 0;
		m_IsWaitingAtRouteGate = false;
		ResumeAgentAfterRouteGate();
	}

	private bool TryGetWaitGroupForWaypoint(int _waypointIndex, out int _waitGroup)
	{
		_waitGroup = 0;
		if (_waypointIndex < 0 || _waypointIndex >= m_Waypoints.Count)
			return false;

		int commandIndex = m_HasActiveDestination ? _waypointIndex - 1 : _waypointIndex;
		if (commandIndex < 0 || commandIndex >= m_CommandQueue.Count)
			return false;

		_waitGroup = m_CommandQueue[commandIndex].WaitGroup;
		return _waitGroup >= 1;
	}

	public bool TrySetWaitGroupForWaypoint(
		int _waypointIndex,
		int _waitGroup,
		bool _preserveWaitHoldPosition = false,
		bool _manualPlacement = true)
	{
		if (_waypointIndex < 0 || _waypointIndex >= m_Waypoints.Count)
			return false;

		int normalizedGroup = NormalizeWaitGroup(_waitGroup);
		int commandIndex = m_HasActiveDestination ? _waypointIndex - 1 : _waypointIndex;
		if (commandIndex < 0 || commandIndex >= m_CommandQueue.Count)
			return false;

		QueuedCommand cmd = m_CommandQueue[commandIndex];
		AssignWaitMetadata(
			ref cmd,
			normalizedGroup,
			_iconAtWaypoint: _manualPlacement,
			_preserveIconPlacement: _preserveWaitHoldPosition);
		if (normalizedGroup >= 1 && !_preserveWaitHoldPosition)
			BindWaitHoldToRoute(ref cmd, commandIndex);
		m_CommandQueue[commandIndex] = cmd;

		if (m_IsWaitingAtRouteGate && commandIndex == 0 && normalizedGroup >= 1)
			m_ActiveWaitGroup = normalizedGroup;

		return true;
	}

	private void TryAdvanceRouteQueue()
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"ADVANCE_QUEUE {BuildRouteDebugSnapshot()}");
#endif
		if (m_Waypoints.Count > 0)
		{
			Vector3 dest = m_Waypoints[0];
			Vector3 moveDir = dest - m_ActiveRouteSegmentStart;
			moveDir.y = 0f;
			if (moveDir.sqrMagnitude > 0.01f)
			{
				m_HasLastArrivalMovementAngle = true;
				m_LastArrivalMovementAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
			}

			ShiftFacingArrowSegmentsAfterWaypointRemoved(0);
			ShiftWaitHoldSegmentsAfterWaypointRemoved(0);
			m_Waypoints.RemoveAt(0);
		}
		RebuildPathLine();
		m_HasActiveDestination = false;
		ClearSmoothingArcState();
		TryStartNextQueuedCommand();
		if (!m_HasActiveDestination && m_CommandQueue.Count == 0)
			ApplyFormationFacingIfNeeded();
	}

	private void ShiftFacingArrowSegmentsAfterWaypointRemoved(int _removedWaypointIndex)
	{
		if (m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
				m_ActiveFacingArrows[i] = RemapFacingArrowSegmentForRemove(m_ActiveFacingArrows[i], _removedWaypointIndex);
		}

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			if (cmd.FacingArrows == null)
				continue;

			for (int arrowIndex = 0; arrowIndex < cmd.FacingArrows.Count; arrowIndex++)
				cmd.FacingArrows[arrowIndex] = RemapFacingArrowSegmentForRemove(cmd.FacingArrows[arrowIndex], _removedWaypointIndex);

			m_CommandQueue[commandIndex] = cmd;
		}

		MarkFacingArrowsDirty();
	}

	private void ShiftFacingArrowSegmentsForWaypointInsert(int _insertSegmentIndex, float _insertSegmentT)
	{
		if (m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
				m_ActiveFacingArrows[i] = RemapFacingArrowSegmentForInsert(m_ActiveFacingArrows[i], _insertSegmentIndex, _insertSegmentT);
		}

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			if (cmd.FacingArrows == null)
				continue;

			for (int arrowIndex = 0; arrowIndex < cmd.FacingArrows.Count; arrowIndex++)
				cmd.FacingArrows[arrowIndex] = RemapFacingArrowSegmentForInsert(cmd.FacingArrows[arrowIndex], _insertSegmentIndex, _insertSegmentT);

			m_CommandQueue[commandIndex] = cmd;
		}

		MarkFacingArrowsDirty();
	}

	private static FacingArrow RemapFacingArrowSegmentForInsert(
		FacingArrow _arrow,
		int _insertSegmentIndex,
		float _insertSegmentT)
	{
		RemapRouteSegmentBindingForInsert(
			ref _arrow.RouteSegmentIndex,
			ref _arrow.RouteSegmentT,
			_insertSegmentIndex,
			_insertSegmentT);
		return _arrow;
	}

	private static FacingArrow RemapFacingArrowSegmentForRemove(FacingArrow _arrow, int _removedWaypointIndex)
	{
		RemapRouteSegmentBindingForRemove(
			ref _arrow.RouteSegmentIndex,
			ref _arrow.RouteSegmentT,
			_removedWaypointIndex);
		return _arrow;
	}

	private void TryStartNextQueuedCommand()
	{
		if (m_CommandQueue.Count == 0)
			return;
		if (m_IsWaitingAtRouteGate)
			return;

		QueuedCommand next = m_CommandQueue[0];
		if (next.WaitGroup >= 1)
		{
			EnterWaitBeforeNextCommand(next.WaitGroup);
			return;
		}

		DequeueAndExecuteNextCommand();
	}

	private void EnterWaitBeforeNextCommand(int _waitGroup)
	{
		if (m_CommandQueue.Count == 0)
			return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"WAIT_GATE group={_waitGroup} {BuildRouteDebugSnapshot()}");
#endif
		m_IsWaitingAtRouteGate = true;
		m_ActiveWaitGroup = NormalizeWaitGroup(_waitGroup);
		m_HasActiveDestination = false;
		ClearSmoothingArcState();
		ResetContinuousRouteLocomotionFlags();

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.isOnNavMesh)
		{
			agent.isStopped = true;
			agent.ResetPath();
		}
	}

	private void ResumeAgentAfterRouteGate()
	{
		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.isOnNavMesh)
			agent.isStopped = false;
	}

	private void ResumeAfterWaitGroupRemoved()
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"WAIT_RESUME_REMOVED {BuildRouteDebugSnapshot()}");
#endif
		m_IsWaitingAtRouteGate = false;
		m_ActiveWaitGroup = 0;
		ResumeAgentAfterRouteGate();
		DequeueAndExecuteNextCommand();
	}

	private bool IsAtFirstWaypoint()
	{
		if (m_Waypoints.Count == 0)
			return true;

		float dx = transform.position.x - m_Waypoints[0].x;
		float dz = transform.position.z - m_Waypoints[0].z;
		return dx * dx + dz * dz < 0.25f;
	}

	private void BuildRawRoutePoints(List<Vector3> _output, bool includeUnitStart, Vector3? previewDestination)
	{
		_output.Clear();
		if (m_Waypoints.Count == 0 && !previewDestination.HasValue)
			return;

		if (includeUnitStart || m_Waypoints.Count == 0)
			_output.Add(transform.position);

		for (int i = 0; i < m_Waypoints.Count; i++)
			_output.Add(m_Waypoints[i]);

		if (previewDestination.HasValue)
			_output.Add(previewDestination.Value);
	}

	private void BuildSmoothedPathPoints(IReadOnlyList<Vector3> _rawPoints, List<Vector3> _output)
	{
		_output.Clear();
		int count = _rawPoints.Count;
		if (count == 0)
			return;

		if (!m_EnableRouteCornerSmoothing || count < 3)
		{
			for (int i = 0; i < count; i++)
				_output.Add(_rawPoints[i]);
			return;
		}

		Vector3 approachPoint = _rawPoints[0];
		_output.Add(approachPoint);

		for (int i = 1; i < count - 1; i++)
		{
			Vector3 corner = _rawPoints[i];
			Vector3 nextPoint = _rawPoints[i + 1];
			if (!TryGetCornerArcPoints(approachPoint, corner, nextPoint, out Vector3 enter, out Vector3 exit))
			{
				_output.Add(corner);
				approachPoint = corner;
				continue;
			}

			_output.Add(enter);
			for (int s = 0; s < m_CornerArcSamples.Count; s++)
				_output.Add(m_CornerArcSamples[s]);
			approachPoint = exit;
		}

		Vector3 finalPoint = _rawPoints[count - 1];
		if (_output.Count == 0 || (_output[_output.Count - 1] - finalPoint).sqrMagnitude > 0.0001f)
			_output.Add(finalPoint);
	}

	private void ApplyPathLinePoints(IReadOnlyList<Vector3> _worldPoints)
	{
		if (m_PathLine == null)
			return;

		if (_worldPoints == null || _worldPoints.Count < 2)
		{
			m_PathLine.positionCount = 0;
			m_PathLine.enabled = false;
			return;
		}

		m_PathLine.positionCount = _worldPoints.Count;
		for (int i = 0; i < _worldPoints.Count; i++)
			m_PathLine.SetPosition(i, _worldPoints[i] + s_PathLineYOffset);
	}

	private bool TryBuildMovementSubtargets(Vector3 _previousPoint, Vector3 _corner, Vector3 _nextPoint, List<Vector3> _subtargets)
	{
		_subtargets.Clear();
		if (!TryGetCornerArcPoints(_previousPoint, _corner, _nextPoint, out Vector3 enter, out Vector3 exit))
			return false;

		if (!TrySampleNavMeshPoint(enter, out Vector3 sampledEnter))
			return false;

		_subtargets.Add(sampledEnter);
		for (int i = 0; i < m_CornerArcSamples.Count; i++)
		{
			if (!TrySampleNavMeshPoint(m_CornerArcSamples[i], out Vector3 sampledArcPoint))
				return false;

			if ((_subtargets[_subtargets.Count - 1] - sampledArcPoint).sqrMagnitude > 0.01f)
				_subtargets.Add(sampledArcPoint);
		}

		if (!TrySampleNavMeshPoint(exit, out Vector3 sampledExit))
			return false;

		if ((_subtargets[_subtargets.Count - 1] - sampledExit).sqrMagnitude > 0.01f)
			_subtargets.Add(sampledExit);

		return _subtargets.Count >= 2;
	}

	private bool TryGetCornerArcPoints(
		Vector3 _previousPoint,
		Vector3 _corner,
		Vector3 _nextPoint,
		out Vector3 _enter,
		out Vector3 _exit)
	{
		_enter = _corner;
		_exit = _corner;
		m_CornerArcSamples.Clear();

		Vector3 incoming = FlattenToGround(_corner - _previousPoint);
		Vector3 outgoing = FlattenToGround(_nextPoint - _corner);
		float incomingLength = incoming.magnitude;
		float outgoingLength = outgoing.magnitude;
		if (incomingLength < 0.05f || outgoingLength < 0.05f)
			return false;

		Vector3 incomingDirection = incoming / incomingLength;
		Vector3 outgoingDirection = outgoing / outgoingLength;
		float turnAngle = Vector3.Angle(incomingDirection, outgoingDirection);
		if (turnAngle < m_CornerSmoothingMinAngle)
			return false;

		float halfAngleRad = turnAngle * 0.5f * Mathf.Deg2Rad;
		if (halfAngleRad < 0.001f)
			return false;

		float maxTangentLength = m_CornerSmoothingMaxRadius / Mathf.Tan(halfAngleRad);
		float tangentLength = Mathf.Min(
			incomingLength * m_CornerSmoothingSegmentFraction,
			outgoingLength * m_CornerSmoothingSegmentFraction,
			maxTangentLength,
			m_CornerSmoothingMaxRadius);
		if (tangentLength < 0.05f)
			return false;

		_enter = _corner - incomingDirection * tangentLength;
		_exit = _corner + outgoingDirection * tangentLength;
		_enter.y = _corner.y;
		_exit.y = _corner.y;

		int sampleCount = Mathf.Clamp(Mathf.CeilToInt(turnAngle / 15f), 2, m_CornerSmoothingMaxSamples);
		for (int i = 1; i <= sampleCount; i++)
		{
			float t = i / (float)(sampleCount + 1);
			Vector3 sample = EvaluateQuadraticBezier(_enter, _corner, _exit, t);
			m_CornerArcSamples.Add(sample);
		}

		m_CornerArcSamples.Add(_exit);
		return true;
	}

	private bool TrySampleNavMeshPoint(Vector3 _point, out Vector3 _sampledPoint)
	{
		if (NavMesh.SamplePosition(_point, out NavMeshHit hit, m_CornerSmoothingNavMeshSampleRadius, NavMesh.AllAreas))
		{
			_sampledPoint = hit.position;
			return true;
		}

		_sampledPoint = _point;
		return false;
	}

	private static Vector3 EvaluateQuadraticBezier(Vector3 _start, Vector3 _control, Vector3 _end, float _t)
	{
		float oneMinusT = 1f - _t;
		return oneMinusT * oneMinusT * _start + 2f * oneMinusT * _t * _control + _t * _t * _end;
	}

	private static Vector3 FlattenToGround(Vector3 _vector)
	{
		_vector.y = 0f;
		return _vector;
	}
	#endregion
	#endregion

	public sealed class FormationSyncGroup
	{
		public float LastSpeedUpdateTime;
		public readonly List<RtsUnitMember> Members = new List<RtsUnitMember>(8);
	}

	public FormationSyncGroup ActiveFormationSync => m_FormationSyncGroup;

	public void AssignFormationSyncGroup(FormationSyncGroup _group)
	{
		if (m_FormationSyncGroup != null && m_FormationSyncGroup != _group)
			m_FormationSyncGroup.Members.Remove(this);

		m_FormationSyncGroup = _group;

		if (_group != null && !_group.Members.Contains(this))
			_group.Members.Add(this);
	}

	public void AssignFormationSpeedMultiplier(float _multiplier)
	{
		float clamped = Mathf.Clamp(_multiplier, 0f, 1f);
		if (m_ClickToMove != null)
			m_ClickToMove.FormationSpeedMultiplier = clamped;
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.FormationSpeedMultiplier = clamped;
	}

	public void ClearFormationSync()
	{
		if (m_FormationSyncGroup != null)
			m_FormationSyncGroup.Members.Remove(this);

		m_FormationSyncGroup = null;
		AssignFormationSpeedMultiplier(1f);
	}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
	[Header("Route Movement Debug")]
	[SerializeField] private bool m_DrawRouteMovementDebug;

	internal void NotifyRouteDebugEarlyStop(float _remainingDistance)
	{
		LogRouteDebugEvent($"EARLY_STOP rem={_remainingDistance:F2} {BuildRouteDebugSnapshot()}");
	}

	private void LogRouteDebugEvent(string _event)
	{
		RouteMovementDebug.Log(this, _event);
	}

	private void UpdateRouteDebugPeriodicState()
	{
		if (m_Waypoints.Count == 0 && m_CommandQueue.Count == 0 && !m_HasActiveDestination &&
		    !m_IsWaitingAtRouteGate)
			return;

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		float speed = agent != null
			? new Vector3(agent.velocity.x, 0f, agent.velocity.z).magnitude
			: 0f;
		bool stuckCandidate = m_HasActiveDestination &&
		                      !m_IsWaitingAtRouteGate &&
		                      !m_IsRotatingToFacing &&
		                      !m_IsExecutingSmoothingArc &&
		                      m_DestinationSetTime >= 0f &&
		                      Time.time - m_DestinationSetTime >= 0.35f &&
		                      agent != null &&
		                      agent.isOnNavMesh &&
		                      !agent.pathPending &&
		                      !agent.hasPath &&
		                      speed < 0.15f;

		string prefix = stuckCandidate ? "STUCK?" : "STATE";
		RouteMovementDebug.LogThrottled(
			this,
			ref m_RouteDebugNextStateLogTime,
			$"{prefix} {BuildRouteDebugSnapshot()}");
	}

	private string BuildRouteDebugSnapshot()
	{
		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		float remaining = -1f;
		float speed = 0f;
		bool hasPath = false;
		bool isStopped = false;
		bool pathPending = false;
		if (agent != null)
		{
			hasPath = agent.hasPath;
			isStopped = agent.isStopped;
			pathPending = agent.pathPending;
			speed = new Vector3(agent.velocity.x, 0f, agent.velocity.z).magnitude;
			if (hasPath && !float.IsPositiveInfinity(agent.remainingDistance))
				remaining = agent.remainingDistance;
		}

		bool suppressEarly = m_ClickToMove != null
			? m_ClickToMove.SuppressEarlyArrivalStop
			: m_LocomotionDriver != null && m_LocomotionDriver.SuppressEarlyArrivalStop;

		string syncInfo = m_FormationSyncGroup != null
			? $"syncMembers={m_FormationSyncGroup.Members.Count}"
			: "sync=none";

		string wp0 = m_Waypoints.Count > 0 ? FormatRoutePoint(m_Waypoints[0]) : "none";
		string wp1 = m_Waypoints.Count > 1 ? FormatRoutePoint(m_Waypoints[1]) : "none";

		return
			$"wp={m_Waypoints.Count} q={m_CommandQueue.Count} active={m_HasActiveDestination} " +
			$"gate={m_IsWaitingAtRouteGate} wg={m_ActiveWaitGroup} " +
			$"intermediate={IsIntermediateRouteSegment()} suppressEarly={suppressEarly} " +
			$"{syncInfo} rem={remaining:F2} spd={speed:F2} hasPath={hasPath} stopped={isStopped} pending={pathPending} " +
			$"arc={m_IsExecutingSmoothingArc} rotate={m_IsRotatingToFacing} wp0={wp0} wp1={wp1}";
	}

	private static string FormatRoutePoint(Vector3 _point)
	{
		return $"({_point.x:F1},{_point.z:F1})";
	}

	private void OnGUI()
	{
		if (!m_DrawRouteMovementDebug || !m_IsSelected)
			return;

		DrawRouteMovementDebugPanel();
	}

	private void DrawRouteMovementDebugPanel()
	{
		int panelRow = 0;
		for (int i = 0; i < s_Instances.Count; i++)
		{
			RtsUnitMember member = s_Instances[i];
			if (member == null || !member.m_DrawRouteMovementDebug || !member.m_IsSelected)
				continue;
			if (member == this)
				break;
			panelRow++;
		}

		GUI.Box(
			new Rect(10f, 10f + panelRow * 130f, 420f, 120f),
			$"{name}\n{BuildRouteDebugSnapshot()}");
	}
#endif
}
