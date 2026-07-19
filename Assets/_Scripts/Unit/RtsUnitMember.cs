using System;
using System.Collections;
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
	#region Temporary
	/// <summary>TEMP: отключает подворот/возврат корня при переходе готов ↔ не готов.</summary>
	private static readonly bool s_ReadyFacingTransitionEnabled = false;
	#endregion

	#region Constants
	#endregion

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
	private UnitConsciousness m_Consciousness;
	private UnitHealth m_Health;
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
	[SerializeField, Min(0.2f)] private float m_ContinuousRouteLookaheadDistance = 0.6f;
	[Header("Segment Facing (ПКМ по отрезку маршрута)")]
	[SerializeField, Min(0.5f)] private float m_FacingTurnOverDistance = 5f;

	private static Material s_PathLineMaterial;
	private static readonly Vector3 s_PathLineYOffset = Vector3.up * 0.03f;
	private const float c_PathLineWalkWidth = 0.06f;
	private const float c_PathLineCrouchWidth = 0.045f;
	private const float c_PathLineRunWidth = 0.085f;
	private const float c_PathLinePreviewWidthScale = 0.67f;
	private const float c_PathLinePreviewAlpha = 0.35f;
	private const float c_PathLineNormalAlpha = 0.8f;
	private static readonly List<RtsUnitMember> s_Instances = new List<RtsUnitMember>(128);
	private Coroutine m_PendingCommandCoroutine;
	private int m_PendingCommandVersion;
	private UnitCombatStats m_CombatStats;
	private UnitRosterDisplayState m_RosterDisplay;
	private Transform m_CachedCameraTransform;
	private readonly List<LineRenderer> m_PathSegmentLines = new List<LineRenderer>(8);
	private UnitClickToMove.MoveTier m_PreviewMoveTier = UnitClickToMove.MoveTier.Walk;
	private bool m_PreviewAppendsToExistingRoute;
	private bool m_SuppressLiveAgentRoutePathVisual;
	private int m_RouteEditDragWaypointIndex = -1;
	private bool m_HasActiveDestination;
	private bool m_IsMovePreviewVisualActive;
	private Vector3? m_MovePreviewDestination;
	private UnitClickToMove.MoveTier m_ActiveMoveTier = UnitClickToMove.MoveTier.Walk;
	private LocomotionStance m_ActiveRouteStance = LocomotionStance.Standing;
	private Vector3 m_ActiveRouteSegmentStart;
	private float m_DestinationSetTime = -1f;
	private bool m_HasWantedFacing;
	private float m_WantedFacingAngle;
	private bool m_IsRotatingToFacing;
	private float m_FacingRotateVelocity;
	private bool m_FacingSuppressedReady;
	private bool m_FacingAutoRestoreReady;
	private bool m_WasReadyBeforeFacing;
	private bool m_LastTrackedWantsReady;
	private bool m_IsInFacingTurn;
	private FacingArrowMode m_FacingTurnMode;
	private Vector3 m_FacingTurnStartPos;
	private float m_FacingTurnStartAngle;
	private float m_FacingTurnTargetAngle;
	private float m_FacingTurnDistanceTraveled;
	private Vector3 m_FacingLookPoint;
	private FormationSyncGroup m_FormationSyncGroup;
	private bool m_HasPendingFormationSlotArrivalYaw;
	private float m_PendingFormationSlotArrivalYaw;
	private bool m_RouteMarchEngaged;
	private float m_RouteSegmentLength;
	private readonly List<Vector3> m_Waypoints = new List<Vector3>();
	private float m_NextWaypointCheckTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
	private float m_RouteDebugNextStateLogTime;
	private bool m_RouteDebugLastSuppressEarlyStop;
	private float m_FacingArrowDebugNextPendingLogTime;
	private readonly HashSet<int> m_FacingArrowMissReportedKeys = new HashSet<int>();
	private readonly HashSet<int> m_FacingArrowVisualDriftReportedKeys = new HashSet<int>();
#endif
	private readonly List<Vector3> m_SmoothingSubtargets = new List<Vector3>(16);
	private int m_SmoothingSubtargetIndex;
	private bool m_IsExecutingSmoothingArc;
	private bool m_PreferContinuousNextMoveOrder;
	private UnitClickToMove.MoveTier m_SmoothingMoveTier = UnitClickToMove.MoveTier.Walk;
	private readonly List<Vector3> m_RawRoutePoints = new List<Vector3>(32);
	private readonly List<Vector3> m_SmoothedRoutePoints = new List<Vector3>(64);
	private readonly List<Vector3> m_CornerArcSamples = new List<Vector3>(16);
	private NavMeshPath m_ReusableNavMeshPath;
	private readonly List<Vector3> m_RouteSegmentPolylineBuffer = new List<Vector3>(32);
	private readonly List<GrenadeRouteOrder> m_GrenadeOrders = new List<GrenadeRouteOrder>();
	private bool m_IsExecutingGrenadeOrder;
	private GrenadeRouteOrder m_PendingGrenadeOrder;
	private UnitGrenadeThrowController m_GrenadeThrowController;

	public enum FacingArrowMode
	{
		TurnOverDistance,
		HoldToEnd,
		LookAtPoint,
		/// <summary>Поворот на месте только после полной остановки в точке назначения.</summary>
		TurnOnArrival,
	}

	public enum ArrowPriorityPhase
	{
		None,
		Turning,
		YellowReturning,
		BlueHold,
		GreenHold,
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
		public LocomotionStance RouteStance;
		public List<FacingArrow> FacingArrows;
		public int WaitGroup;
		public bool WaitIconAtWaypoint;
		public bool HasWaitRouteBinding;
		public int WaitRouteSegmentIndex;
		public float WaitRouteSegmentT;
		public bool HasPendingFormationSlotArrivalYaw;
		public float PendingFormationSlotArrivalYaw;
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
		public Vector3 LookPointWorld;
		public Vector3 AnchorWorld;
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
	private static readonly Vector3 s_FacingArrowYOffset = Vector3.up * 0.05f;
	private readonly List<FacingArrowVisualSource> m_FacingArrowVisuals = new List<FacingArrowVisualSource>();
	private bool m_FacingArrowsDirty;
	private List<FacingArrow> m_ActiveFacingArrows;
	private int m_ActiveWaitGroup;
	private bool m_IsWaitingAtRouteGate;
	/// <summary>Wait group to enter after arriving at the current active destination (not before walking there).</summary>
	private int m_ActiveDestinationWaitGroup;
	private bool m_ActiveDestinationWaitHasRouteBinding;
	private int m_ActiveDestinationWaitSegmentIndex;
	private float m_ActiveDestinationWaitSegmentT;
	private Vector3 m_WaitGateWorldPosition;
	private const float c_WaitBindingReachRadius = 0.35f;
	/// <summary>Anchor reach tolerance (0 m + nav mesh slop). Arrows activate only at the waypoint, not early.</summary>
	private const float c_FacingArrowActivationReachRadius = c_WaitBindingReachRadius;
	private const float c_FacingArrowShaftStartOffset = 0.15f;
	private const float c_FacingArrowFixedLength = 2f;
	/// <summary>Screen-space min gap between facing-arrow anchors (camera-distance independent).</summary>
	private const float c_FacingArrowMinSpacingPixels = 28f;
	/// <summary>Screen-space snap-to-anchor radius (≈ half of min spacing).</summary>
	private const float c_FacingArrowSnapPixels = 14f;
	private const float c_FacingArrowMinSpacingWorldMin = 0.05f;
	private const float c_FacingArrowMinSpacingWorldMax = 1.5f;
	private static bool s_EnableArrowSpacingConstraint = true;

	private ArrowPriorityPhase m_ArrowPriorityPhase;
	private bool m_ArrowTurnScanRequested;
	private bool m_HasOldTargetAngle;
	private float m_OldTargetAngle;
	private bool m_YellowDeferredActive;
	private float m_YellowArrowAngle;
	private Vector3 m_YellowArrowWorldPos;
	private FacingArrowMode m_ActiveArrowPriorityMode;
	private UnitVision m_CachedVision;
	private const float c_ArrowFullTurnThresholdDegrees = 5f;
	private const float c_YellowArrowMaxWanderDistance = 5f;
	private FacingArrow? m_PersistentFacingIndicator;
	private Color m_PersistentFacingIndicatorColor;
	private Coroutine m_FacingIndicatorClearCoroutine;
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
	public bool IsRotatingToRouteFacing => m_IsRotatingToFacing;
	public bool IsWaitingAtRouteGate => m_IsWaitingAtRouteGate;
	public int ActiveWaitGroup => m_ActiveWaitGroup;
	public FormationType CurrentFormation { get => m_CurrentFormation; set => m_CurrentFormation = value; }
	public float FormationSpacing { get; set; } = 2f;

	/// <summary>Количество приказов на бросок гранаты на маршруте.</summary>
	public int GrenadeOrderCount => m_GrenadeOrders.Count;

	/// <summary>
	/// Угол слота формации для поворота после полной остановки. Не влияет на марш.
	/// </summary>
	public void SetPendingFormationSlotArrivalYaw(float _yawDegrees)
	{
		m_HasPendingFormationSlotArrivalYaw = true;
		m_PendingFormationSlotArrivalYaw = _yawDegrees;
	}

	public void ClearPendingFormationSlotArrivalYaw()
	{
		m_HasPendingFormationSlotArrivalYaw = false;
	}
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
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();

		m_RuntimeMoveAnimatorSpeed = UnityEngine.Random.Range(
			Mathf.Min(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax),
			Mathf.Max(m_MoveAnimatorSpeedMin, m_MoveAnimatorSpeedMax));

		if (m_DisableDirectInputForRts)
			ApplyDirectInputState(false);

		EnsurePathLineMaterial();
	}

	private void EnsurePathLineMaterial()
	{
		if (s_PathLineMaterial != null)
			return;

		s_PathLineMaterial = new Material(Shader.Find("Sprites/Default"));
		s_PathLineMaterial.hideFlags = HideFlags.HideAndDontSave;
	}

	private struct RouteSegmentVisualStyle
	{
		public Color Color;
		public float Width;
	}

	private static RouteSegmentVisualStyle GetRouteSegmentVisualStyle(
		UnitClickToMove.MoveTier _moveTier,
		LocomotionStance _stance,
		bool _preview)
	{
		float alpha = _preview ? c_PathLinePreviewAlpha : c_PathLineNormalAlpha;
		float widthScale = _preview ? c_PathLinePreviewWidthScale : 1f;
		bool isRun = _moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint;
		if (isRun)
		{
			return new RouteSegmentVisualStyle
			{
				Color = new Color(1f, 0.65f, 0.1f, alpha),
				Width = c_PathLineRunWidth * widthScale,
			};
		}

		if (_stance == LocomotionStance.Crouch)
		{
			return new RouteSegmentVisualStyle
			{
				Color = new Color(0.35f, 0.85f, 1f, alpha),
				Width = c_PathLineCrouchWidth * widthScale,
			};
		}

		return new RouteSegmentVisualStyle
		{
			Color = new Color(0.75f, 0.75f, 0.75f, alpha),
			Width = c_PathLineWalkWidth * widthScale,
		};
	}

	private LocomotionStance ResolveDefaultRouteStance(UnitClickToMove.MoveTier _moveTier)
	{
		if (_moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint)
			return LocomotionStance.Standing;
		if (m_Stance != null && m_Stance.CurrentStance == LocomotionStance.Crouch)
			return LocomotionStance.Crouch;
		return LocomotionStance.Standing;
	}

	private LocomotionStance ResolveRouteStanceForWaypointInsert(int _segmentIndex)
	{
		if (m_HasActiveDestination)
		{
			if (_segmentIndex == 0)
				return ResolveDefaultRouteStance(m_ActiveMoveTier);

			int previousQueueIndex = _segmentIndex - 2;
			if (previousQueueIndex >= 0 && previousQueueIndex < m_CommandQueue.Count)
				return m_CommandQueue[previousQueueIndex].RouteStance;

			int queueIndex = _segmentIndex - 1;
			if (queueIndex >= 0 && queueIndex < m_CommandQueue.Count)
				return m_CommandQueue[queueIndex].RouteStance;
		}
		else if (_segmentIndex > 0 && _segmentIndex - 1 < m_CommandQueue.Count)
		{
			return m_CommandQueue[_segmentIndex - 1].RouteStance;
		}

		return ResolveDefaultRouteStance(UnitClickToMove.MoveTier.Walk);
	}

	private void ResolveRouteStyleForSegment(
		int _segmentIndex,
		out UnitClickToMove.MoveTier _moveTier,
		out LocomotionStance _routeStance)
	{
		if (m_HasActiveDestination && _segmentIndex == 0)
		{
			_moveTier = m_ActiveMoveTier;
			_routeStance = m_ActiveRouteStance;
			return;
		}

		int queueIndex = m_HasActiveDestination ? _segmentIndex - 1 : _segmentIndex;
		if (queueIndex >= 0 && queueIndex < m_CommandQueue.Count)
		{
			QueuedCommand cmd = m_CommandQueue[queueIndex];
			_moveTier = cmd.MoveTier;
			_routeStance = cmd.RouteStance;
			return;
		}

		_moveTier = m_ActiveMoveTier;
		_routeStance = m_ActiveRouteStance;
	}

	private LineRenderer GetOrCreatePathSegmentLine(int _index)
	{
		while (m_PathSegmentLines.Count <= _index)
		{
			GameObject lineGo = new GameObject($"PathSegmentLine_{m_PathSegmentLines.Count}");
			lineGo.transform.SetParent(transform, false);
			LineRenderer line = lineGo.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			line.sharedMaterial = s_PathLineMaterial;
			m_PathSegmentLines.Add(line);
		}

		return m_PathSegmentLines[_index];
	}

	private void HideUnusedPathSegmentLines(int _usedCount)
	{
		for (int i = _usedCount; i < m_PathSegmentLines.Count; i++)
		{
			LineRenderer line = m_PathSegmentLines[i];
			if (line == null)
				continue;

			line.positionCount = 0;
			line.enabled = false;
		}
	}

	private void ClearPathSegmentLineGeometry()
	{
		for (int i = 0; i < m_PathSegmentLines.Count; i++)
		{
			LineRenderer line = m_PathSegmentLines[i];
			if (line == null)
				continue;

			line.positionCount = 0;
			line.enabled = false;
		}
	}

	private void SetPathSegmentLinesSelected(bool _selected)
	{
		for (int i = 0; i < m_PathSegmentLines.Count; i++)
		{
			LineRenderer line = m_PathSegmentLines[i];
			if (line != null)
				line.enabled = _selected && line.positionCount >= 2;
		}
	}

	private bool TryBuildRouteSegmentPolyline(Vector3 _start, Vector3 _end, List<Vector3> _output)
	{
		_output.Clear();
		if (TryAppendCalculatedNavMeshPath(_start, _end, _output))
			return _output.Count >= 2;

		AppendStraightSegmentFallback(_start, _end, _output);
		return _output.Count >= 2;
	}

	private void ApplyPathSegmentLine(
		int _lineIndex,
		IReadOnlyList<Vector3> _worldPoints,
		UnitClickToMove.MoveTier _moveTier,
		LocomotionStance _routeStance,
		bool _preview)
	{
		LineRenderer line = GetOrCreatePathSegmentLine(_lineIndex);
		if (_worldPoints == null || _worldPoints.Count < 2)
		{
			line.positionCount = 0;
			line.enabled = false;
			return;
		}

		RouteSegmentVisualStyle style = GetRouteSegmentVisualStyle(_moveTier, _routeStance, _preview);
		line.positionCount = _worldPoints.Count;
		for (int i = 0; i < _worldPoints.Count; i++)
			line.SetPosition(i, _worldPoints[i] + s_PathLineYOffset);
		line.startWidth = style.Width;
		line.endWidth = style.Width;
		line.startColor = style.Color;
		line.endColor = style.Color;
		line.enabled = m_IsSelected;
	}

	private bool ShouldUseLiveAgentPathForRouteVisual(bool _requested)
	{
		return _requested && !m_SuppressLiveAgentRoutePathVisual;
	}

	public void SetRoutePathVisualEditing(bool _editing, int _dragWaypointIndex = -1)
	{
		if (_editing)
		{
			if (m_SuppressLiveAgentRoutePathVisual && m_RouteEditDragWaypointIndex == _dragWaypointIndex)
				return;

			m_SuppressLiveAgentRoutePathVisual = true;
			m_RouteEditDragWaypointIndex = _dragWaypointIndex;
			RebuildPathLine();
			return;
		}

		if (!m_SuppressLiveAgentRoutePathVisual)
			return;

		m_SuppressLiveAgentRoutePathVisual = false;
		m_RouteEditDragWaypointIndex = -1;
		RebuildPathLine();

		if (m_HasActiveDestination && m_Waypoints.Count > 0)
		{
			ClearSmoothingArcState();
			m_DestinationSetTime = Time.time;
			IssueMoveOrderForCurrentWaypoint(m_Waypoints[0], m_ActiveMoveTier);
		}
	}

	private bool ShouldSuppressRouteArrivalDuringEdit()
	{
		return m_SuppressLiveAgentRoutePathVisual && m_RouteEditDragWaypointIndex == 0;
	}

	private void RefreshPathSegmentLines(bool _useLiveAgentPathForActiveLeg)
	{
		EnsurePathLineMaterial();
		bool useLiveAgentPath = ShouldUseLiveAgentPathForRouteVisual(_useLiveAgentPathForActiveLeg);

		if (UnitFallenStateUtility.IsFallenOrDead(this))
		{
			ClearPathSegmentLineGeometry();
			return;
		}

		int lineIndex = 0;

		if (m_IsMovePreviewVisualActive)
		{
			if (m_PreviewAppendsToExistingRoute)
			{
				for (int seg = 0; seg < m_Waypoints.Count; seg++)
				{
					if (!CollectRouteSegmentPolyline(seg, m_RouteSegmentPolylineBuffer, useLiveAgentPath))
						continue;

					ResolveRouteStyleForSegment(seg, out UnitClickToMove.MoveTier tier, out LocomotionStance stance);
					ApplyPathSegmentLine(lineIndex++, m_RouteSegmentPolylineBuffer, tier, stance, _preview: false);
				}
			}

			if (m_MovePreviewDestination.HasValue)
			{
				Vector3 previewStart = m_PreviewAppendsToExistingRoute && m_Waypoints.Count > 0
					? m_Waypoints[m_Waypoints.Count - 1]
					: transform.position;
				if (TryBuildRouteSegmentPolyline(previewStart, m_MovePreviewDestination.Value, m_RouteSegmentPolylineBuffer))
				{
					LocomotionStance previewStance = ResolveDefaultRouteStance(m_PreviewMoveTier);
					ApplyPathSegmentLine(
						lineIndex++,
						m_RouteSegmentPolylineBuffer,
						m_PreviewMoveTier,
						previewStance,
						_preview: true);
				}
			}

			HideUnusedPathSegmentLines(lineIndex);
			SetPathSegmentLinesSelected(m_IsSelected);
			return;
		}

		if (m_Waypoints.Count == 0)
		{
			ClearPathSegmentLineGeometry();
			return;
		}

		for (int seg = 0; seg < m_Waypoints.Count; seg++)
		{
			if (!CollectRouteSegmentPolyline(seg, m_RouteSegmentPolylineBuffer, useLiveAgentPath))
				continue;

			ResolveRouteStyleForSegment(seg, out UnitClickToMove.MoveTier tier, out LocomotionStance stance);
			ApplyPathSegmentLine(lineIndex++, m_RouteSegmentPolylineBuffer, tier, stance, _preview: false);
		}

		HideUnusedPathSegmentLines(lineIndex);
		SetPathSegmentLinesSelected(m_IsSelected);
	}

	private void RebuildPathLine()
	{
		if (m_IsMovePreviewVisualActive)
		{
			RefreshMovePreviewPathLine();
			return;
		}

		if (m_Waypoints.Count == 0)
		{
			ClearPathSegmentLineGeometry();
			return;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent(
			$"PATH_REBUILD segments={m_Waypoints.Count} preview={m_IsMovePreviewVisualActive} {BuildRouteDebugSnapshot()}");
#endif
		RefreshPathSegmentLines(_useLiveAgentPathForActiveLeg: true);
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this))
			s_Instances.Add(this);
		SetSelected(false);
		ApplyAnimatorSpeedVariation();
		SubscribeFallenStateListeners();
		ApplyFallenRtsIsolationIfNeeded();
		m_LastTrackedWantsReady = WantsReady;
	}

	private void OnDisable()
	{
		UnsubscribeFallenStateListeners();
		if (m_FormationSyncGroup != null)
			m_FormationSyncGroup.Members.Remove(this);
		CancelPendingCommand();
		ClearWaypoints();
		ResetAnimatorSpeed();
		s_Instances.Remove(this);
		SetSelected(false);
	}

	private void SubscribeFallenStateListeners()
	{
		if (m_Consciousness == null)
			m_Consciousness = GetComponentInChildren<UnitConsciousness>(true);
		if (m_Health == null)
			m_Health = GetComponentInChildren<UnitHealth>(true);

		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged += HandleFallenStateChangedForRts;
		if (m_Health != null)
			m_Health.Changed += HandleFallenStateChangedForRts;
	}

	private void UnsubscribeFallenStateListeners()
	{
		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged -= HandleFallenStateChangedForRts;
		if (m_Health != null)
			m_Health.Changed -= HandleFallenStateChangedForRts;
	}

	private void HandleFallenStateChangedForRts(bool _isConscious)
	{
		ApplyFallenRtsIsolationIfNeeded();
	}

	private void HandleFallenStateChangedForRts()
	{
		ApplyFallenRtsIsolationIfNeeded();
	}

	private void ApplyFallenRtsIsolationIfNeeded()
	{
		if (!UnitFallenStateUtility.IsFallenOrDead(this))
			return;

		ClearWaypoints();
		RtsUnitSelectionManager.Instance?.NotifyUnitBecameNonControllable(this);
	}

	private void Update()
	{
		TrackReadyWantedTransition();
		ApplyAnimatorSpeedVariation();
		UpdateSelectionLabelBillboard();
		UpdateContinuousRouteLocomotionFlags();
		UpdatePathLinePosition();
		UpdateActiveFacingArrows();
		UpdateFacingTurn();
		UpdateArrowPriority();
		SyncFacingArrows();
		UpdateFacingArrows();
		UpdateRouteMarchEngaged();
		TryTriggerActiveDestinationSegmentStartWait();
		TryRemoveArrivedDestination();
		TryAdvanceWaypointEarly();
		UpdateFacingRotation();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		UpdateRouteDebugPeriodicState();
#endif
	}

	private void TryAdvanceWaypointEarly()
	{
		if (ShouldSuppressRouteArrivalDuringEdit())
			return;
		if (m_IsExecutingGrenadeOrder)
			return;
		if (!m_HasActiveDestination)
			return;
		if (m_IsRotatingToFacing)
			return;
		if (m_IsWaitingAtRouteGate)
			return;
		// Wait points must be reached fully — no early cutover into the gate.
		if (m_ActiveDestinationWaitGroup >= 1)
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

		float advanceDistance = isIntermediate ? m_ContinuousRouteLookaheadDistance : 0.2f;
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
		if (m_IsMovePreviewVisualActive)
		{
			RefreshMovePreviewPathLine();
			return;
		}

		if (m_Waypoints.Count == 0)
			return;

		RefreshPathSegmentLines(_useLiveAgentPathForActiveLeg: true);
	}

	private void RefreshMovePreviewPathLine()
	{
		if (!m_IsMovePreviewVisualActive)
			return;

		RefreshPathSegmentLines(_useLiveAgentPathForActiveLeg: true);
	}

	private void UpdateFacingRotation()
	{
		if (!m_IsRotatingToFacing)
			return;

		if (ShouldDeferRouteFacingOverride())
			return;

		float rotateSpeed = GetEffectiveRotateSpeed();
		float resolvedWantedYaw = UnitHorizontalFacingUtility.ResolveHorizontalFacingBodyYaw(
			transform,
			m_UnitEquipment,
			m_ReadyHands,
			m_WantedFacingAngle);

		Quaternion targetRot = Quaternion.Euler(0f, resolvedWantedYaw, 0f);
		float angle = Quaternion.Angle(transform.rotation, targetRot);

		HandleFacingTurnReady(angle);

		if (angle < 0.5f)
		{
			transform.rotation = targetRot;
			m_IsRotatingToFacing = false;
			m_HasWantedFacing = false;
			if (m_FacingSuppressedReady)
			{
				if (m_FacingAutoRestoreReady)
					m_ReadyHands?.SetReadyWanted(true);

				m_FacingSuppressedReady = false;
				m_FacingAutoRestoreReady = false;
			}

			TryAdvanceRouteQueue();
			return;
		}

		float smoothAngle = Mathf.SmoothDampAngle(
			transform.rotation.eulerAngles.y,
			resolvedWantedYaw,
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
				{
					m_ReadyHands.ApplyTemporaryReadySuppression();
					m_FacingAutoRestoreReady = true;
				}

				m_FacingSuppressedReady = true;
			}
		}
		else if (_angleDegrees < 20f && m_FacingSuppressedReady)
		{
			if (m_FacingAutoRestoreReady)
				m_ReadyHands?.SetReadyWanted(true);

			m_FacingSuppressedReady = false;
			m_FacingAutoRestoreReady = false;
		}
	}

	private void TryRemoveArrivedDestination()
	{
		if (ShouldSuppressRouteArrivalDuringEdit())
			return;
		if (m_IsExecutingGrenadeOrder)
			return;

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

		if (TryActivateFormationSlotArrivalFacing())
			return;

		if (TryActivateTurnOnArrivalFacing())
			return;

		if (ShouldClearFacingOnLegArrival())
		{
			if (m_IsInFacingTurn)
			{
				ClearFacingTurn(ShouldPreserveHeadingOnLegArrival());
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

		if (!m_HasActiveDestination && m_Waypoints.Count == 0 && m_CommandQueue.Count == 0)
		{
			ResetActiveArrowFacingHold();
			ResetActiveMoveTierWhenIdle();
			ClearPathSegmentLineGeometry();
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
			MarkFacingArrowsDirty();
		}

		SetPathSegmentLinesSelected(_selected);

		if (m_SelectionNameLabelRoot != null)
			m_SelectionNameLabelRoot.SetActive(_selected);
	}

	public void IssueMoveOrder(Vector3 _worldPosition, UnitClickToMove.MoveTier _moveTier, float _groupStaggerDelaySeconds = 0f)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (UnitFallenStateUtility.IsFallenOrDead(this))
			LogRouteDebugEvent($"MOVE_ORDER_BLOCKED fallen dest={FormatRoutePoint(_worldPosition)}");
#endif
		IssueRouteMoveOrder(_worldPosition, _moveTier, _continuous: false, _groupStaggerDelaySeconds);
	}

	public void BeginActiveRouteMovement(UnitClickToMove.MoveTier _moveTier)
	{
		if (!m_HasActiveDestination || m_Waypoints.Count == 0)
			return;

		m_ActiveMoveTier = _moveTier;
		m_ActiveRouteStance = ResolveDefaultRouteStance(_moveTier);
		IssueMoveOrderForCurrentWaypoint(m_Waypoints[0], _moveTier);
	}

	private void IssueRouteMoveOrder(
		Vector3 _worldPosition,
		UnitClickToMove.MoveTier _moveTier,
		bool _continuous,
		float _groupStaggerDelaySeconds = 0f)
	{
		if (UnitFallenStateUtility.IsFallenOrDead(this))
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent($"NAV_BLOCKED fallen dest={FormatRoutePoint(_worldPosition)}");
#endif
			return;
		}

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
			{
				m_MagazineLoadingController?.StopLoading();
				m_WeaponReloadController?.StopReload();
			}

			bool isRunOrSprint = _moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint;
			if (isRunOrSprint && TryGetComponent(out UnitStamina stamina) && stamina.IsExhausted)
				_moveTier = UnitClickToMove.MoveTier.Walk;

			isRunOrSprint = _moveTier == UnitClickToMove.MoveTier.Run || _moveTier == UnitClickToMove.MoveTier.Sprint;
			SyncActiveLegMoveTierForNavOrder(_worldPosition, _moveTier);

			bool preserveFacingOverride = ShouldPreserveFacingOverrideForNavOrder();
			if (isRunOrSprint && !preserveFacingOverride)
				ClearFacingOverride();

			if (!WantsReady && !preserveFacingOverride)
				ClearFacingOverride();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent(
				$"NAV_ORDER mode={(_continuous ? "continuous" : "reset")} tier={_moveTier} dest={FormatRoutePoint(_worldPosition)} {BuildRouteDebugSnapshot()}");
#endif

			bool issued = false;
			if (m_ClickToMove != null)
			{
				issued = _continuous
					? m_ClickToMove.IssueNavOrderContinuous(_worldPosition, _moveTier)
					: m_ClickToMove.IssueNavOrder(_worldPosition, _moveTier);
			}
			else if (m_LocomotionDriver != null)
			{
				UnitNavLocomotionDriver.MoveTier navTier = _moveTier switch
				{
					UnitClickToMove.MoveTier.Run => UnitNavLocomotionDriver.MoveTier.Run,
					UnitClickToMove.MoveTier.Sprint => UnitNavLocomotionDriver.MoveTier.Sprint,
					_ => UnitNavLocomotionDriver.MoveTier.Walk
				};
				issued = _continuous
					? m_LocomotionDriver.IssueNavOrderContinuous(_worldPosition, navTier)
					: m_LocomotionDriver.IssueNavOrder(_worldPosition, navTier);
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (!issued)
			{
				LogRouteDebugEvent(
					$"NAV_ORDER_FAILED mode={(_continuous ? "continuous" : "reset")} tier={_moveTier} dest={FormatRoutePoint(_worldPosition)} {BuildRouteDebugSnapshot()}");
			}
#endif
		}, _groupStaggerDelaySeconds);
	}

	public void BeginMovePreviewVisual()
	{
		m_IsMovePreviewVisualActive = true;
		m_MovePreviewDestination = null;
		m_PreviewAppendsToExistingRoute = false;
	}

	public void EndMovePreviewVisual()
	{
		if (!m_IsMovePreviewVisualActive)
			return;

		m_IsMovePreviewVisualActive = false;
		m_MovePreviewDestination = null;
		m_PreviewAppendsToExistingRoute = false;
		if (m_Waypoints.Count > 0)
			RebuildPathLine();
		else
			ClearPathSegmentLineGeometry();
	}

	public void SetPreviewMoveTier(UnitClickToMove.MoveTier _moveTier)
	{
		m_PreviewMoveTier = _moveTier;
		if (m_IsMovePreviewVisualActive)
			RefreshMovePreviewPathLine();
	}

	public void SetPreviewAppendToExistingRoute(bool _append)
	{
		m_PreviewAppendsToExistingRoute = _append;
		if (m_IsMovePreviewVisualActive)
			RefreshMovePreviewPathLine();
	}

	public void SetPreviewLine(Vector3 _dest)
	{
		if (UnitFallenStateUtility.IsFallenOrDead(this))
		{
			ClearPathSegmentLineGeometry();
			return;
		}

		if (!m_IsMovePreviewVisualActive)
			BeginMovePreviewVisual();

		m_MovePreviewDestination = _dest;
		RefreshMovePreviewPathLine();
	}

	public void SetDestinationDirect(Vector3 _dest, UnitClickToMove.MoveTier _moveTier = UnitClickToMove.MoveTier.Walk)
	{
		ResetActiveArrowFacingHold();
		ClearSmoothingArcState();
		ClearRouteWaitState();
		m_Waypoints.Clear();
		m_Waypoints.Add(_dest);
		RebuildPathLine();
		m_HasActiveDestination = true;
		m_ActiveMoveTier = _moveTier;
		m_ActiveRouteStance = ResolveDefaultRouteStance(_moveTier);
		m_ActiveRouteSegmentStart = transform.position;
		m_RouteMarchEngaged = false;
		m_RouteSegmentLength = (FlattenToGround(_dest) - FlattenToGround(transform.position)).magnitude;
		m_DestinationSetTime = Time.time;
		m_IsRotatingToFacing = false;
		m_ActiveFacingArrows = null;
		MarkFacingArrowsDirty();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"ROUTE_SET_DIRECT tier={_moveTier} dest={FormatRoutePoint(_dest)} {BuildRouteDebugSnapshot()}");
#endif
	}

	public int GetNextAutoWaitGroup()
	{
		int maxGroup = 0;
		for (int i = 0; i < m_CommandQueue.Count; i++)
		{
			if (m_CommandQueue[i].WaitGroup > maxGroup)
				maxGroup = m_CommandQueue[i].WaitGroup;
		}

		if (m_ActiveDestinationWaitGroup > maxGroup)
			maxGroup = m_ActiveDestinationWaitGroup;

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
		bool _activateAtSegmentStart = false,
		float _groupStaggerDelaySeconds = 0f)
	{
		if (_facing.HasValue && !float.IsNaN(_facing.Value))
			ResetArrowHoldForNewRouteArrow("IssueDirectMoveOrderWithWait.facing");

		CancelPendingCommand();
		ClearWaypoints();

		m_Waypoints.Add(_dest);

		var cmd = new QueuedCommand
		{
			Destination = _dest,
			MoveTier = _tier,
			RouteStance = ResolveDefaultRouteStance(_tier),
			FacingArrows = new List<FacingArrow>(),
		};

		if (_facing.HasValue && !float.IsNaN(_facing.Value))
		{
			bool hasLookPoint = _mode == FacingArrowMode.LookAtPoint && _lookPoint.HasValue;
			cmd.FacingArrows.Add(BindFacingArrowToRouteSegment(new FacingArrow
			{
				Angle = _facing.Value,
				Mode = _mode,
				ForceReadyOnActivation = ResolveForceReadyOnFacingActivation(_mode, _activateAtSegmentStart, false),
				ActivateAtSegmentStart = _activateAtSegmentStart,
				HasLookPoint = hasLookPoint,
			}, 0, _dest, hasLookPoint ? _lookPoint : null));
		}

		AssignWaitMetadata(ref cmd, _waitGroup, _iconAtWaypoint: false);

		m_CommandQueue.Add(cmd);
		if (cmd.WaitGroup >= 1)
		{
			cmd = m_CommandQueue[0];
			BindWaitHoldToRoute(ref cmd, 0, 0f);
			m_CommandQueue[0] = cmd;
		}
		RebuildPathLine();
		MarkFacingArrowsDirty();
		TryStartNextQueuedCommand(_groupStaggerDelaySeconds);
	}

	public void EnqueueWaypoint(
		Vector3 _dest,
		UnitClickToMove.MoveTier _tier,
		float? _facing,
		FacingArrowMode _mode = FacingArrowMode.TurnOverDistance,
		int _waitGroup = 0,
		Vector3? _lookPoint = null,
		bool _activateAtSegmentStart = false,
		float _groupStaggerDelaySeconds = 0f,
		float? _formationSlotArrivalYaw = null,
		bool _waitAtSegmentStart = false)
	{
		if (_facing.HasValue && !float.IsNaN(_facing.Value))
			ResetArrowHoldForNewRouteArrow("EnqueueWaypoint.facing");

		m_Waypoints.Add(_dest);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"ROUTE_ENQUEUE tier={_tier} dest={FormatRoutePoint(_dest)} wait={_waitGroup} {BuildRouteDebugSnapshot()}");
#endif

		int commandIndex = m_CommandQueue.Count;
		var cmd = new QueuedCommand
		{
			Destination = _dest,
			MoveTier = _tier,
			RouteStance = ResolveDefaultRouteStance(_tier),
			FacingArrows = new List<FacingArrow>(),
			HasPendingFormationSlotArrivalYaw = _formationSlotArrivalYaw.HasValue,
			PendingFormationSlotArrivalYaw = _formationSlotArrivalYaw ?? 0f,
		};
		
		if (_facing.HasValue && !float.IsNaN(_facing.Value))
		{
			int waypointIndex = m_Waypoints.Count - 1;
			bool hasLookPoint = _mode == FacingArrowMode.LookAtPoint && _lookPoint.HasValue;
			cmd.FacingArrows.Add(BindFacingArrowToRouteSegment(new FacingArrow
			{
				Angle = _facing.Value,
				Mode = _mode,
				ForceReadyOnActivation = ResolveForceReadyOnFacingActivation(_mode, _activateAtSegmentStart, false),
				ActivateAtSegmentStart = _activateAtSegmentStart,
				HasLookPoint = hasLookPoint,
			}, waypointIndex, m_Waypoints[waypointIndex], hasLookPoint ? _lookPoint : null));
		}

		AssignWaitMetadata(ref cmd, _waitGroup, _iconAtWaypoint: false);
		
		m_CommandQueue.Add(cmd);
		if (cmd.WaitGroup >= 1)
		{
			cmd = m_CommandQueue[commandIndex];
			float bindT = _waitAtSegmentStart ? 0f : 1f;
			BindWaitHoldToRoute(ref cmd, commandIndex, bindT);
			m_CommandQueue[commandIndex] = cmd;
		}

		if (m_IsMovePreviewVisualActive)
			RefreshMovePreviewPathLine();
		else
			RebuildPathLine();

		bool isIdle = !m_HasActiveDestination && !m_IsRotatingToFacing && !m_IsWaitingAtRouteGate;
		if (isIdle)
			TryStartNextQueuedCommand(_groupStaggerDelaySeconds);

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
				m_ActiveRouteStance = LocomotionStance.Standing;
				ClearSmoothingArcState();
				ClearFacingOverride();
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
			cmd.RouteStance = LocomotionStance.Standing;
			m_CommandQueue[commandIndex] = cmd;
			RebuildPathLine();
			MarkFacingArrowsDirty();
			return true;
		}

		return false;
	}

	public int WaypointCount => m_Waypoints.Count;

	/// <summary>
	/// Собирает NavMesh-полилинию логического сегмента маршрута (0 = юнит→wp0, i = wp[i-1]→wp[i]).
	/// </summary>
	public bool CollectRouteSegmentPolyline(int _segmentIndex, List<Vector3> _output, bool _useLiveAgentPathForActiveLeg = true)
	{
		_output.Clear();
		if (_segmentIndex < 0 || _segmentIndex >= m_Waypoints.Count)
			return false;

		bool isActiveLeg = _segmentIndex == 0 && m_HasActiveDestination && _useLiveAgentPathForActiveLeg;
		Vector3 segStart = _segmentIndex == 0
			? ResolveRouteSegmentStartForPolyline(isActiveLeg)
			: m_Waypoints[_segmentIndex - 1];
		Vector3 segEnd = m_Waypoints[_segmentIndex];

		if (isActiveLeg && TryAppendActiveAgentPathPolyline(_output))
			return _output.Count >= 2;

		if (TryAppendCalculatedNavMeshPath(segStart, segEnd, _output))
			return _output.Count >= 2;

		AppendStraightSegmentFallback(segStart, segEnd, _output);
		return _output.Count >= 2;
	}

	/// <summary>
	/// Выбор точки на видимой NavMesh-полилинии сегмента (для hover/insert/edit).
	/// </summary>
	public bool TryPickRouteSegment(
		Camera _camera,
		int _segmentIndex,
		Vector2 _mouseScreen,
		float _thresholdPixels,
		bool _hasMouseWorld,
		Vector3 _mouseWorld,
		out Vector3 _worldPoint,
		out float _segmentT,
		out float _screenDistSqr)
	{
		_worldPoint = Vector3.zero;
		_segmentT = 0f;
		_screenDistSqr = float.MaxValue;
		if (_camera == null || !CollectRouteSegmentPolyline(_segmentIndex, m_RouteSegmentPolylineBuffer))
			return false;

		float thresholdSqr = _thresholdPixels * _thresholdPixels;
		float bestDistSqr = thresholdSqr;
		bool found = false;
		Vector3 bestWorld = Vector3.zero;

		for (int i = 1; i < m_RouteSegmentPolylineBuffer.Count; i++)
		{
			Vector3 segmentStart = m_RouteSegmentPolylineBuffer[i - 1];
			Vector3 segmentEnd = m_RouteSegmentPolylineBuffer[i];
			Vector2 startScreen = _camera.WorldToScreenPoint(segmentStart);
			Vector2 endScreen = _camera.WorldToScreenPoint(segmentEnd);
			float distSqr = DistPointToSegmentSqrScreen(
				_mouseScreen,
				startScreen,
				endScreen,
				out Vector2 closestScreen,
				out _);

			if (distSqr >= bestDistSqr)
				continue;

			bestDistSqr = distSqr;
			found = true;
			if (_hasMouseWorld)
			{
				bestWorld = ClosestPointOnLineSegment3D(_mouseWorld, segmentStart, segmentEnd);
			}
			else
			{
				Vector2 segmentScreen = endScreen - startScreen;
				float segmentScreenLenSqr = segmentScreen.sqrMagnitude;
				float t = segmentScreenLenSqr > 0.0001f
					? Vector2.Dot(closestScreen - startScreen, segmentScreen) / segmentScreenLenSqr
					: 0f;
				bestWorld = Vector3.Lerp(segmentStart, segmentEnd, Mathf.Clamp01(t));
			}
		}

		if (!found)
			return false;

		_worldPoint = bestWorld;
		_segmentT = ComputeRouteSegmentTAlongPolyline(m_RouteSegmentPolylineBuffer, bestWorld);
		_screenDistSqr = bestDistSqr;
		return true;
	}

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
			total += GetNavMeshSegmentLengthOrPlanar(transform.position, m_Waypoints[0]);
		}

		for (int i = 0; i < m_Waypoints.Count - 1; i++)
			total += GetNavMeshSegmentLengthOrPlanar(m_Waypoints[i], m_Waypoints[i + 1]);

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

	public bool TryAdjustPointForFacingArrowSpacing(int _segmentIndex, ref Vector3 _worldPoint, Camera _camera = null)
	{
		if (!s_EnableArrowSpacingConstraint)
			return false;

		Camera camera = _camera != null ? _camera : Camera.main;
		bool adjusted = false;
		Vector3 bestAdjusted = _worldPoint;
		float bestDistSqr = float.MaxValue;

		if (m_HasActiveDestination && _segmentIndex == 0 && m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
			{
				Vector3 anchor = ResolveFacingArrowAnchor(m_ActiveFacingArrows[i], _isActiveSegment: true);
				if (ComputeFacingArrowExclusionAdjustment(
					    camera, anchor, _worldPoint, out Vector3 candidate, ref bestDistSqr))
				{
					bestAdjusted = candidate;
					adjusted = true;
				}
			}
		}

		int commandIndex = m_HasActiveDestination ? _segmentIndex - 1 : _segmentIndex;
		if (commandIndex >= 0 && commandIndex < m_CommandQueue.Count)
		{
			List<FacingArrow> arrows = m_CommandQueue[commandIndex].FacingArrows;
			if (arrows != null)
			{
				for (int i = 0; i < arrows.Count; i++)
				{
					Vector3 anchor = ResolveFacingArrowAnchor(arrows[i]);
					if (ComputeFacingArrowExclusionAdjustment(
						    camera, anchor, _worldPoint, out Vector3 candidate, ref bestDistSqr))
					{
						bestAdjusted = candidate;
						adjusted = true;
					}
				}
			}
		}

		if (adjusted)
			_worldPoint = bestAdjusted;

		return adjusted;
	}

	private static bool ComputeFacingArrowExclusionAdjustment(
		Camera _camera,
		Vector3 _arrowAnchor,
		Vector3 _cursorPoint,
		out Vector3 _adjusted,
		ref float _bestDistSqr)
	{
		_adjusted = _cursorPoint;
		ResolveFacingArrowSpacingWorldRadii(
			_camera,
			_arrowAnchor,
			out float minSpacing,
			out float snapRadius);

		Vector3 toCursor = _cursorPoint - _arrowAnchor;
		toCursor.y = 0f;
		float dist = toCursor.magnitude;

		if (dist >= minSpacing)
			return false;

		Vector3 dir = dist > 0.001f ? toCursor / dist : Vector3.forward;

		if (dist < snapRadius)
			_adjusted = _arrowAnchor;
		else
			_adjusted = _arrowAnchor + dir * minSpacing;

		float sqrDist = (_adjusted - _cursorPoint).sqrMagnitude;
		if (sqrDist >= _bestDistSqr)
			return false;

		_bestDistSqr = sqrDist;
		return true;
	}

	private static void ResolveFacingArrowSpacingWorldRadii(
		Camera _camera,
		Vector3 _worldAnchor,
		out float _minSpacing,
		out float _snapRadius)
	{
		float metersPerPixel = EstimateMetersPerPixel(_camera, _worldAnchor);
		_minSpacing = Mathf.Clamp(
			c_FacingArrowMinSpacingPixels * metersPerPixel,
			c_FacingArrowMinSpacingWorldMin,
			c_FacingArrowMinSpacingWorldMax);
		_snapRadius = Mathf.Clamp(
			c_FacingArrowSnapPixels * metersPerPixel,
			c_FacingArrowMinSpacingWorldMin * 0.5f,
			_minSpacing * 0.5f);
	}

	private static float EstimateMetersPerPixel(Camera _camera, Vector3 _worldPoint)
	{
		if (_camera == null)
			return c_FacingArrowMinSpacingWorldMin / Mathf.Max(1f, c_FacingArrowMinSpacingPixels);

		Vector3 screen = _camera.WorldToScreenPoint(_worldPoint);
		if (screen.z <= 0.01f)
			return c_FacingArrowMinSpacingWorldMin / Mathf.Max(1f, c_FacingArrowMinSpacingPixels);

		// Horizontal pixel → world meters on the ground plane through the anchor.
		const float samplePixels = 16f;
		Ray centerRay = _camera.ScreenPointToRay(screen);
		Ray offsetRay = _camera.ScreenPointToRay(screen + new Vector3(samplePixels, 0f, 0f));
		Plane groundPlane = new Plane(Vector3.up, _worldPoint);
		if (!groundPlane.Raycast(centerRay, out float centerDist) ||
		    !groundPlane.Raycast(offsetRay, out float offsetDist))
		{
			float depth = Mathf.Abs(screen.z);
			float frustumHeight = 2f * depth * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
			return frustumHeight / Mathf.Max(1f, _camera.pixelHeight);
		}

		Vector3 centerHit = centerRay.GetPoint(centerDist);
		Vector3 offsetHit = offsetRay.GetPoint(offsetDist);
		centerHit.y = 0f;
		offsetHit.y = 0f;
		return (offsetHit - centerHit).magnitude / samplePixels;
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

		if (m_IsWaitingAtRouteGate && m_ActiveWaitGroup >= 1)
		{
			_output.Add(new WaitPointDescriptor(
				-1,
				true,
				m_WaitGateWorldPosition,
				m_ActiveWaitGroup));
		}
		else if (m_HasActiveDestination &&
		         m_ActiveDestinationWaitGroup >= 1)
		{
			_output.Add(new WaitPointDescriptor(
				0,
				true,
				ResolveActiveDestinationWaitIconWorldPosition(),
				m_ActiveDestinationWaitGroup));
		}

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
			return ResolveWaitBindingWorldPosition(_command.WaitRouteSegmentIndex, _command.WaitRouteSegmentT, _command.Destination);

		if (_command.WaitIconAtWaypoint)
		{
			int waypointIndex = m_HasActiveDestination ? _commandIndex + 1 : _commandIndex;
			if (waypointIndex >= 0 && waypointIndex < m_Waypoints.Count)
				return m_Waypoints[waypointIndex];
		}

		return ComputeAutoWaitHoldWorldPosition(_commandIndex);
	}

	private Vector3 ResolveActiveDestinationWaitIconWorldPosition()
	{
		if (!m_ActiveDestinationWaitHasRouteBinding)
			return m_Waypoints.Count > 0 ? m_Waypoints[0] : transform.position;

		Vector3 holdDestination = m_Waypoints.Count > 0 ? m_Waypoints[0] : transform.position;
		return ResolveWaitBindingWorldPosition(
			m_ActiveDestinationWaitSegmentIndex,
			m_ActiveDestinationWaitSegmentT,
			holdDestination);
	}

	/// <summary>Maps wait binding to the same world point used for arrival logic.</summary>
	private Vector3 ResolveWaitBindingWorldPosition(int _segmentIndex, float _segmentT, Vector3 _holdDestination)
	{
		if (TryGetRouteSegmentEndpoints(
			    _segmentIndex,
			    _useActiveSegmentStart: _segmentIndex == 0 && m_HasActiveDestination,
			    out Vector3 segmentStart,
			    out Vector3 segmentEnd))
			return Vector3.Lerp(segmentStart, segmentEnd, Mathf.Clamp01(_segmentT));

		return _holdDestination;
	}

	private Vector3 ResolveRouteSegmentPoint(int _segmentIndex, float _segmentT)
	{
		if (TryGetRouteSegmentEndpoints(
			    _segmentIndex,
			    _useActiveSegmentStart: _segmentIndex == 0 && m_HasActiveDestination,
			    out Vector3 segmentStart,
			    out Vector3 segmentEnd))
			return Vector3.Lerp(segmentStart, segmentEnd, _segmentT);

		return transform.position;
	}

	private void BindWaitHoldToRoute(ref QueuedCommand _command, int _commandIndex, float _segmentT = 1f)
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
		_command.WaitRouteSegmentT = Mathf.Clamp01(_segmentT);
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

	private Vector3 ResolveRouteSegmentStartForPolyline(bool _useCurrentUnitPositionForActiveLeg = false)
	{
		if (!m_HasActiveDestination || _useCurrentUnitPositionForActiveLeg)
			return transform.position;

		if (m_ActiveRouteSegmentStart == Vector3.zero)
			m_ActiveRouteSegmentStart = transform.position;

		return m_ActiveRouteSegmentStart;
	}

	private Vector3 ResolveFacingArrowAnchor(in FacingArrow _arrow, bool _isActiveSegment = false)
	{
		if (_arrow.AnchorWorld != Vector3.zero)
			return _arrow.AnchorWorld;

		bool useActiveSegmentStart = _isActiveSegment && m_HasActiveDestination;
		if (CollectRouteSegmentPolyline(_arrow.RouteSegmentIndex, m_RouteSegmentPolylineBuffer, _useLiveAgentPathForActiveLeg: false) &&
		    m_RouteSegmentPolylineBuffer.Count >= 2)
			return EvaluatePolylineAtT(m_RouteSegmentPolylineBuffer, _arrow.RouteSegmentT);

		if (TryGetRouteSegmentEndpoints(_arrow.RouteSegmentIndex, useActiveSegmentStart, out Vector3 segmentStart, out Vector3 segmentEnd))
			return Vector3.Lerp(segmentStart, segmentEnd, _arrow.RouteSegmentT);

		return transform.position;
	}

	private bool TryEvaluateFacingArrowAnchorFromLogicalRoute(
		in FacingArrow _arrow,
		bool _isActiveSegment,
		out Vector3 _anchorWorld)
	{
		_anchorWorld = default;
		bool useActiveSegmentStart = _isActiveSegment && m_HasActiveDestination;
		if (CollectRouteSegmentPolyline(_arrow.RouteSegmentIndex, m_RouteSegmentPolylineBuffer, _useLiveAgentPathForActiveLeg: false) &&
		    m_RouteSegmentPolylineBuffer.Count >= 2)
		{
			_anchorWorld = EvaluatePolylineAtT(m_RouteSegmentPolylineBuffer, _arrow.RouteSegmentT);
			return true;
		}

		if (TryGetRouteSegmentEndpoints(
			    _arrow.RouteSegmentIndex,
			    useActiveSegmentStart,
			    out Vector3 segmentStart,
			    out Vector3 segmentEnd))
		{
			_anchorWorld = Vector3.Lerp(segmentStart, segmentEnd, _arrow.RouteSegmentT);
			return true;
		}

		return false;
	}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
	private void LogFacingArrowVisualAnchorDriftIfAny(in FacingArrow _arrow, bool _isActiveSegment)
	{
		if (!FacingArrowDebug.VisualDriftLoggingEnabled || _arrow.AnchorWorld == Vector3.zero)
			return;

		int driftKey = BuildFacingArrowVisualDriftKey(_arrow);
		if (!m_FacingArrowVisualDriftReportedKeys.Add(driftKey))
			return;

		if (!TryEvaluateFacingArrowAnchorFromLogicalRoute(_arrow, _isActiveSegment, out Vector3 polylineAnchor))
			return;

		float dx = polylineAnchor.x - _arrow.AnchorWorld.x;
		float dz = polylineAnchor.z - _arrow.AnchorWorld.z;
		float drift = Mathf.Sqrt(dx * dx + dz * dz);
		if (drift < FacingArrowDebug.VisualDriftLogMinMeters)
			return;

		FacingArrowDebug.Log(
			this,
			$"VISUAL_DRIFT {FormatFacingArrowShort(_arrow)} fixed={FormatRoutePoint(_arrow.AnchorWorld)} " +
			$"polyline={FormatRoutePoint(polylineAnchor)} drift={drift:F2}m activeSeg={_isActiveSegment} " +
			$"legStart={FormatRoutePoint(ResolveRouteSegmentStartForPolyline())}");
	}
#endif

	private Vector3 ResolveFacingArrowLookPoint(in FacingArrow _arrow, bool _isActiveSegment = false)
	{
		if (!_arrow.HasLookPoint)
			return Vector3.zero;

		// Точка на земле фиксирована в world space; offset от anchor на маршруте устаревает при движении юнита.
		if (_arrow.LookPointWorld != Vector3.zero)
			return _arrow.LookPointWorld;

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
				? ResolveRouteSegmentStartForPolyline()
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
		_arrow.AnchorWorld = _anchorWorld;
		if (CollectRouteSegmentPolyline(_segmentIndex, m_RouteSegmentPolylineBuffer, _useLiveAgentPathForActiveLeg: false) &&
		    m_RouteSegmentPolylineBuffer.Count >= 2)
			_arrow.RouteSegmentT = ComputeRouteSegmentTAlongPolyline(m_RouteSegmentPolylineBuffer, _anchorWorld);
		else if (TryGetRouteSegmentEndpoints(_segmentIndex, _useActiveSegmentStart, out Vector3 segmentStart, out Vector3 segmentEnd))
			_arrow.RouteSegmentT = ComputeRouteSegmentT(_anchorWorld, segmentStart, segmentEnd);
		else
			_arrow.RouteSegmentT = 0f;

		if (_arrow.HasLookPoint && _lookPointWorld.HasValue)
		{
			Vector3 anchor = ResolveFacingArrowAnchor(_arrow, _useActiveSegmentStart);
			_arrow.LookPointWorld = _lookPointWorld.Value;
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
		if (_waypointIndex < 0 && m_IsWaitingAtRouteGate && m_ActiveWaitGroup >= 1)
		{
			m_ActiveWaitGroup = m_ActiveWaitGroup >= 3 ? 1 : m_ActiveWaitGroup + 1;
			MarkFacingArrowsDirty();
			return true;
		}

		if (!TryGetWaitGroupForWaypoint(_waypointIndex, out int currentGroup))
			return TrySetWaitGroupForWaypoint(_waypointIndex, 1, _manualPlacement: false);

		int nextGroup = currentGroup >= 3 ? 1 : currentGroup + 1;
		return TrySetWaitGroupForWaypoint(_waypointIndex, nextGroup, _preserveWaitHoldPosition: true);
	}

	public bool TryRemoveWaitPointAtWaypoint(int _waypointIndex)
	{
		if (_waypointIndex < 0 && m_IsWaitingAtRouteGate)
		{
			ResumeAfterWaitGroupRemoved();
			MarkFacingArrowsDirty();
			return true;
		}

		if (!TryGetWaitGroupForWaypoint(_waypointIndex, out _))
			return false;

		if (!TrySetWaitGroupForWaypoint(_waypointIndex, 0))
			return false;

		return true;
	}

	#region Grenade Orders
	public void AddGrenadeOrder(GrenadeRouteOrder _order)
	{
		m_GrenadeOrders.Add(_order);
	}

	public bool TryRemoveGrenadeOrder(int _index)
	{
		if (_index < 0 || _index >= m_GrenadeOrders.Count)
			return false;

		m_GrenadeOrders.RemoveAt(_index);
		return true;
	}

	public bool TryGetGrenadeOrderWorldPosition(int _index, out Vector3 _worldPos)
	{
		if (_index < 0 || _index >= m_GrenadeOrders.Count)
		{
			_worldPos = Vector3.zero;
			return false;
		}

		_worldPos = m_GrenadeOrders[_index].WaypointPosition;
		return true;
	}

	public int GetGrenadeOrderCountByType(GrenadeType _type)
	{
		int count = 0;
		for (int i = 0; i < m_GrenadeOrders.Count; i++)
		{
			if (m_GrenadeOrders[i].Type == _type)
				count++;
		}

		return count;
	}

	public bool HasGrenadeOrderAtWaypoint(int _waypointIndex)
	{
		for (int i = 0; i < m_GrenadeOrders.Count; i++)
		{
			if (m_GrenadeOrders[i].RouteWaypointIndex == _waypointIndex)
				return true;
		}

		return false;
	}

	public bool TryGetGrenadeOrderForWaypoint(int _waypointIndex, out GrenadeRouteOrder _order)
	{
		for (int i = 0; i < m_GrenadeOrders.Count; i++)
		{
			if (m_GrenadeOrders[i].RouteWaypointIndex == _waypointIndex)
			{
				_order = m_GrenadeOrders[i];
				return true;
			}
		}

		_order = default;
		return false;
	}

	public void ClearGrenadeOrders()
	{
		m_GrenadeOrders.Clear();
	}

	public bool IsExecutingGrenadeOrder => m_IsExecutingGrenadeOrder;

	public bool TryStartGrenadeOrderAtWaypoint()
	{
		if (m_IsExecutingGrenadeOrder)
			return false;
		if (m_GrenadeOrders.Count == 0)
			return false;
		if (!TryGetGrenadeOrderForWaypoint(0, out GrenadeRouteOrder order))
			return false;

		if (m_GrenadeThrowController == null)
			m_GrenadeThrowController = GetComponent<UnitGrenadeThrowController>();
		if (m_GrenadeThrowController == null || !m_GrenadeThrowController.CanStartThrow())
		{
			RemoveGrenadeOrderForWaypoint(0);
			return false;
		}

		if (!m_GrenadeThrowController.SetSelectedType(order.Type))
		{
			RemoveGrenadeOrderForWaypoint(0);
			return false;
		}

		m_IsExecutingGrenadeOrder = true;
		m_PendingGrenadeOrder = order;

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.isOnNavMesh)
		{
			agent.isStopped = true;
			agent.ResetPath();
		}

		m_GrenadeThrowController.BeginAiming();
		m_GrenadeThrowController.SetTargetPosition(order.TargetPosition);
		m_GrenadeThrowController.ThrowCompleted += OnGrenadeThrowCompleted;
		m_GrenadeThrowController.ConfirmThrow(false);

		return true;
	}

	private void OnGrenadeThrowCompleted()
	{
		if (m_GrenadeThrowController != null)
			m_GrenadeThrowController.ThrowCompleted -= OnGrenadeThrowCompleted;

		if (!m_IsExecutingGrenadeOrder)
			return;

		RemoveGrenadeOrderForWaypoint(0);
		m_IsExecutingGrenadeOrder = false;

		TryAdvanceRouteQueue();
	}

	private void RemoveGrenadeOrderForWaypoint(int _waypointIndex)
	{
		for (int i = m_GrenadeOrders.Count - 1; i >= 0; i--)
		{
			if (m_GrenadeOrders[i].RouteWaypointIndex == _waypointIndex)
				m_GrenadeOrders.RemoveAt(i);
		}
	}
	#endregion

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
			if (m_HasActiveDestination && m_Waypoints.Count > 0)
				IssueMoveOrderForCurrentWaypoint(m_Waypoints[0], m_ActiveMoveTier);
			else
				TryStartNextQueuedCommand();
			changed = true;
		}

		if (m_ActiveDestinationWaitGroup == normalizedGroup)
		{
			m_ActiveDestinationWaitGroup = 0;
			changed = true;
		}

		for (int i = 0; i < m_CommandQueue.Count; i++)
		{
			QueuedCommand cmd = m_CommandQueue[i];
			if (cmd.WaitGroup != normalizedGroup)
				continue;

			cmd.WaitGroup = 0;
			cmd.WaitIconAtWaypoint = false;
			cmd.HasWaitRouteBinding = false;
			m_CommandQueue[i] = cmd;
			changed = true;
		}

		if (changed)
			MarkFacingArrowsDirty();

		return changed;
	}

	/// <summary>
	/// Places a wait marker on a route segment by logical index (0 = first leg) and param t (0 = start, 1 = end).
	/// </summary>
	public bool TrySetWaitAtRouteSegment(int _segmentIndex, float _segmentT, int _waitGroup)
	{
		if (m_Waypoints.Count == 0)
			return false;

		int normalizedWait = NormalizeWaitGroup(_waitGroup);
		if (normalizedWait < 1)
			return false;

		_segmentIndex = Mathf.Clamp(_segmentIndex, 0, m_Waypoints.Count - 1);
		_segmentT = Mathf.Clamp01(_segmentT);

		if (_segmentIndex == 0 && _segmentT <= 0.001f)
			return InsertWaitHoldAtRouteStart(normalizedWait);

		int endWaypointIndex = _segmentIndex;

		if (m_HasActiveDestination && endWaypointIndex == 0)
		{
			m_ActiveDestinationWaitGroup = normalizedWait;
			m_ActiveDestinationWaitHasRouteBinding = true;
			m_ActiveDestinationWaitSegmentIndex = 0;
			m_ActiveDestinationWaitSegmentT = _segmentT;
			MarkFacingArrowsDirty();
			return true;
		}

		int commandIndex = m_HasActiveDestination ? endWaypointIndex - 1 : endWaypointIndex;
		if (commandIndex < 0 || commandIndex >= m_CommandQueue.Count)
			return false;

		QueuedCommand cmd = m_CommandQueue[commandIndex];
		AssignWaitMetadata(ref cmd, normalizedWait, _iconAtWaypoint: false);
		cmd.HasWaitRouteBinding = true;
		cmd.WaitRouteSegmentIndex = endWaypointIndex;
		cmd.WaitRouteSegmentT = _segmentT;
		m_CommandQueue[commandIndex] = cmd;
		MarkFacingArrowsDirty();
		return true;
	}

	private bool InsertWaitHoldAtRouteStart(int _waitGroup)
	{
		if (m_Waypoints.Count == 0)
			return false;

		Vector3 routeStart = ResolveRouteSegmentPoint(0, 0f);
		TrySampleNavMeshPoint(routeStart, out routeStart);

		if (m_HasActiveDestination)
		{
			float distToStartSqr = (FlattenToGround(transform.position) - FlattenToGround(routeStart)).sqrMagnitude;
			if (distToStartSqr <= 0.35f * 0.35f)
			{
				EnterWaitAfterArrival(_waitGroup, routeStart);
				return true;
			}

			return TryInsertRouteWaypointAtSegment(0, routeStart, _waitGroup, 0f);
		}

		if (m_CommandQueue.Count > 0 &&
		    m_CommandQueue[0].WaitGroup >= 1 &&
		    m_CommandQueue[0].HasWaitRouteBinding &&
		    m_CommandQueue[0].WaitRouteSegmentIndex == 0 &&
		    m_CommandQueue[0].WaitRouteSegmentT <= 0.001f &&
		    (FlattenToGround(m_CommandQueue[0].Destination) - FlattenToGround(routeStart)).sqrMagnitude <= 0.15f * 0.15f)
			return true;

		UnitClickToMove.MoveTier tier = m_CommandQueue.Count > 0
			? m_CommandQueue[0].MoveTier
			: UnitClickToMove.MoveTier.Walk;

		m_Waypoints.Insert(0, routeStart);

		var holdCommand = new QueuedCommand
		{
			Destination = routeStart,
			MoveTier = tier,
			RouteStance = m_CommandQueue.Count > 0 ? m_CommandQueue[0].RouteStance : ResolveDefaultRouteStance(tier),
			FacingArrows = new List<FacingArrow>(),
		};
		AssignWaitMetadata(ref holdCommand, _waitGroup, _iconAtWaypoint: false);
		holdCommand.HasWaitRouteBinding = true;
		holdCommand.WaitRouteSegmentIndex = 0;
		holdCommand.WaitRouteSegmentT = 0f;

		ShiftFacingArrowSegmentsForWaypointInsert(0, 0f);
		ShiftWaitHoldSegmentsForWaypointInsert(0, 0f);
		m_CommandQueue.Insert(0, holdCommand);
		RebuildPathLine();
		MarkFacingArrowsDirty();
		return true;
	}

	public bool TryInsertRouteWaypointAtSegment(int _segmentIndex, Vector3 _worldPoint, int _waitGroup = 0)
	{
		return TryInsertRouteWaypointAtSegment(_segmentIndex, _worldPoint, _waitGroup, null);
	}

	public bool TryInsertRouteWaypointAtSegment(
		int _segmentIndex,
		Vector3 _worldPoint,
		int _waitGroup,
		float? _forcedInsertSegmentT)
	{
		if (_segmentIndex < 0 || _segmentIndex > m_Waypoints.Count || m_Waypoints.Count == 0)
			return false;

		Vector3 sampledPoint = _worldPoint;
		TrySampleNavMeshPoint(_worldPoint, out sampledPoint);

		UnitClickToMove.MoveTier tier = ResolveMoveTierForWaypointInsert(_segmentIndex);
		float insertSegmentT = _forcedInsertSegmentT ?? 1f;
		if (!_forcedInsertSegmentT.HasValue)
		{
			bool useLiveAgent = m_HasActiveDestination && _segmentIndex == 0;
			if (CollectRouteSegmentPolyline(_segmentIndex, m_RouteSegmentPolylineBuffer, useLiveAgent))
				insertSegmentT = ComputeRouteSegmentTAlongPolyline(m_RouteSegmentPolylineBuffer, _worldPoint);
			else if (TryGetRouteSegmentEndpoints(_segmentIndex, out Vector3 insertSegmentStart, out Vector3 insertSegmentEnd))
				insertSegmentT = ComputeRouteSegmentT(_worldPoint, insertSegmentStart, insertSegmentEnd);
		}
		else if (TryGetRouteSegmentEndpoints(
			         _segmentIndex,
			         _useActiveSegmentStart: m_HasActiveDestination && _segmentIndex == 0,
			         out Vector3 forcedStart,
			         out Vector3 forcedEnd))
		{
			sampledPoint = Vector3.Lerp(forcedStart, forcedEnd, Mathf.Clamp01(insertSegmentT));
			TrySampleNavMeshPoint(sampledPoint, out sampledPoint);
		}

		ShiftFacingArrowSegmentsForWaypointInsert(_segmentIndex, insertSegmentT);
		ShiftWaitHoldSegmentsForWaypointInsert(_segmentIndex, insertSegmentT);

		int normalizedWait = NormalizeWaitGroup(_waitGroup);

		if (m_HasActiveDestination && _segmentIndex == 0)
		{
			Vector3 previousActiveDestination = m_Waypoints[0];
			int preservedArrivalWait = m_ActiveDestinationWaitGroup;
			bool preservedBinding = m_ActiveDestinationWaitHasRouteBinding;
			int preservedSegmentIndex = m_ActiveDestinationWaitSegmentIndex;
			float preservedSegmentT = m_ActiveDestinationWaitSegmentT;
			m_Waypoints.Insert(0, sampledPoint);

			var promoteCommand = new QueuedCommand
			{
				Destination = previousActiveDestination,
				MoveTier = m_ActiveMoveTier,
				RouteStance = m_ActiveRouteStance,
				FacingArrows = m_ActiveFacingArrows != null
					? new List<FacingArrow>(m_ActiveFacingArrows)
					: new List<FacingArrow>(),
			};
			AssignWaitMetadata(ref promoteCommand, preservedArrivalWait, _iconAtWaypoint: true);
			if (preservedArrivalWait >= 1)
			{
				if (preservedBinding)
				{
					promoteCommand.HasWaitRouteBinding = true;
					promoteCommand.WaitRouteSegmentIndex = preservedSegmentIndex;
					promoteCommand.WaitRouteSegmentT = preservedSegmentT;
				}
				else
				{
					BindWaitHoldToRoute(ref promoteCommand, 0);
				}
			}

			m_CommandQueue.Insert(0, promoteCommand);
			m_ActiveFacingArrows = null;
			ClearSmoothingArcState();
			m_ActiveDestinationWaitGroup = normalizedWait;
			m_ActiveDestinationWaitHasRouteBinding = normalizedWait >= 1;
			m_ActiveDestinationWaitSegmentIndex = _segmentIndex;
			m_ActiveDestinationWaitSegmentT = insertSegmentT;
			m_ActiveRouteSegmentStart = transform.position;
			m_HasActiveDestination = true;
			IssueMoveOrderForCurrentWaypoint(sampledPoint, m_ActiveMoveTier);
			RebindFacingArrowsAfterRouteTopologyChange();
			RebuildPathLine();
			MarkFacingArrowsDirty();
			return true;
		}

		m_Waypoints.Insert(_segmentIndex, sampledPoint);

		var insertCommand = new QueuedCommand
		{
			Destination = sampledPoint,
			MoveTier = tier,
			RouteStance = ResolveRouteStanceForWaypointInsert(_segmentIndex),
			FacingArrows = new List<FacingArrow>(),
		};
		AssignWaitMetadata(ref insertCommand, normalizedWait, _iconAtWaypoint: true);

		if (m_HasActiveDestination)
		{
			int insertCommandIndex = _segmentIndex - 1;
			if (normalizedWait >= 1)
				BindWaitHoldToRoute(ref insertCommand, insertCommandIndex);
			m_CommandQueue.Insert(insertCommandIndex, insertCommand);
		}
		else
		{
			if (normalizedWait >= 1)
				BindWaitHoldToRoute(ref insertCommand, _segmentIndex);
			m_CommandQueue.Insert(_segmentIndex, insertCommand);
		}

		RebindFacingArrowsAfterRouteTopologyChange();
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
		RefreshFacingArrowBindingsAfterWaypointEdit(_waypointIndex);
		RebuildPathLine();
		MarkFacingArrowsDirty();

		if (m_HasActiveDestination && _waypointIndex == 0 && !m_SuppressLiveAgentRoutePathVisual)
		{
			ClearSmoothingArcState();
			IssueMoveOrderForCurrentWaypoint(sampledPoint, m_ActiveMoveTier);
		}
	}

	private void RefreshFacingArrowBindingsAfterWaypointEdit(int _waypointIndex)
	{
		if (m_HasActiveDestination && _waypointIndex == 0 && m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
			{
				FacingArrow arrow = m_ActiveFacingArrows[i];
				if (arrow.RouteSegmentIndex != _waypointIndex)
					continue;

				arrow.AnchorWorld = Vector3.zero;
				Vector3 reboundAnchor = ResolveFacingArrowAnchor(arrow, _isActiveSegment: true);
				Vector3? lookPoint = arrow.HasLookPoint ? ResolveFacingArrowLookPoint(arrow, _isActiveSegment: true) : null;
				m_ActiveFacingArrows[i] = BindFacingArrowToRouteSegment(
					arrow,
					_waypointIndex,
					reboundAnchor,
					lookPoint,
					_useActiveSegmentStart: true);
			}
		}

		int commandIndex = m_HasActiveDestination ? _waypointIndex - 1 : _waypointIndex;
		if (commandIndex < 0 || commandIndex >= m_CommandQueue.Count)
			return;

		QueuedCommand cmd = m_CommandQueue[commandIndex];
		if (cmd.FacingArrows == null || cmd.FacingArrows.Count == 0)
			return;

		for (int i = 0; i < cmd.FacingArrows.Count; i++)
		{
			FacingArrow arrow = cmd.FacingArrows[i];
			if (arrow.RouteSegmentIndex != _waypointIndex)
				continue;

			Vector3 reboundAnchor = ResolveFacingArrowAnchor(arrow, _isActiveSegment: false);
			Vector3? lookPoint = arrow.HasLookPoint ? ResolveFacingArrowLookPoint(arrow, _isActiveSegment: false) : null;
			cmd.FacingArrows[i] = BindFacingArrowToRouteSegment(
				arrow,
				_waypointIndex,
				reboundAnchor,
				lookPoint,
				_useActiveSegmentStart: false);
		}

		m_CommandQueue[commandIndex] = cmd;
	}

	private static bool ResolveForceReadyOnFacingActivation(
		FacingArrowMode _mode,
		bool _activateAtSegmentStart,
		bool _explicitForceReady)
	{
		if (_mode == FacingArrowMode.TurnOnArrival)
			return _explicitForceReady;

		// Жёлтая / синяя / зелёная стрелки всегда требуют high ready при активации.
		return true;
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
		ResetArrowHoldForNewRouteArrow("SetWaypointFacing");

		bool hasLookPoint = _mode == FacingArrowMode.LookAtPoint && _lookPoint.HasValue;
		bool bindToActiveSegment = m_HasActiveDestination && _index == 0;
		Vector3 bindAnchor = _activateAtSegmentStart && bindToActiveSegment
			? transform.position
			: _anchor;
		var facingArrow = BindFacingArrowToRouteSegment(new FacingArrow
		{
			Angle = _angle,
			Mode = _mode,
			ForceReadyOnActivation = ResolveForceReadyOnFacingActivation(_mode, _activateAtSegmentStart, _forceReadyOnActivation),
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
		ResetArrowHoldForNewRouteArrow("AddFacingArrow.activeSegment");

		if (m_ActiveFacingArrows == null)
			m_ActiveFacingArrows = new List<FacingArrow>();
		
		m_ActiveFacingArrows.Add(_arrow);

		MarkFacingArrowsDirty();

		if (_arrow.ActivateAtSegmentStart && m_HasActiveDestination)
			TryActivateSegmentStartFacingArrows();
		else
			TryActivateClosestFacingArrowInRange();

		MarkFacingArrowsDirty();
	}

	public LineRenderer PathLine => m_PathSegmentLines.Count > 0 ? m_PathSegmentLines[0] : null;

	public void ClearWaypoints()
	{
		CancelPendingCommand();
		ResetActiveArrowFacingHold();
		ClearSmoothingArcState();
		ClearRouteWaitState();
		ResetContinuousRouteLocomotionFlags();
		m_CommandQueue.Clear();
		m_Waypoints.Clear();
		ClearFacingArrows();
		m_ActiveFacingArrows = null;
		ClearPathSegmentLineGeometry();
		m_HasActiveDestination = false;
		m_SuppressLiveAgentRoutePathVisual = false;
		m_RouteEditDragWaypointIndex = -1;
		ClearFormationSync();
		ClearPendingFormationSlotArrivalYaw();
		m_RouteMarchEngaged = false;
	}

	/// <summary>Заглушка для совместимости с менеджером выделения.</summary>
	public void ClearFormationFacing()
	{
	}

	/// <summary>
	/// Завершает «застрявший» приказ у точки назначения (после формации и т.п.):
	/// активирует TurnOnArrival или снимает destination/стрелки, если юнит уже стоит на месте.
	/// </summary>
	public void TryFinalizeIdleNearDestination()
	{
		if (m_IsWaitingAtRouteGate || m_IsExecutingSmoothingArc)
			return;
		if (!m_HasActiveDestination || m_Waypoints.Count == 0)
			return;

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent == null || !agent.isOnNavMesh || agent.pathPending)
			return;

		Vector3 velocity = agent.velocity;
		velocity.y = 0f;
		if (velocity.sqrMagnitude > 0.01f)
			return;
		if (!IsNearDestination(transform.position, m_Waypoints[0], 0.75f))
			return;

		if (TryActivateFormationSlotArrivalFacing())
			return;

		if (TryActivateTurnOnArrivalFacing())
			return;

		if (ShouldClearFacingOnLegArrival())
		{
			if (m_IsInFacingTurn)
				ClearFacingTurn(ShouldPreserveHeadingOnLegArrival());
			else if (m_HasWantedFacing)
			{
				if (!m_IsRotatingToFacing)
					ClearFacingOverride();
				m_HasWantedFacing = false;
				m_ActiveFacingArrows = null;
				MarkFacingArrowsDirty();
			}
		}

		if (!m_IsRotatingToFacing)
			TryAdvanceRouteQueue();

		if (!m_HasActiveDestination && m_Waypoints.Count == 0 && m_CommandQueue.Count == 0)
		{
			ResetActiveArrowFacingHold();
			ResetActiveMoveTierWhenIdle();
			ClearPathSegmentLineGeometry();
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
			ApplyLocomotionFacingOverride(_angle, "IssueInPlaceFacingOrder");
		else
			m_IsRotatingToFacing = true;
	}


	public void IssueInPlaceFacingOrder(
		float _angle,
		FacingArrowMode _mode = FacingArrowMode.TurnOverDistance,
		float _groupStaggerDelaySeconds = 0f,
		bool _showFacingIndicator = false)
	{
		ScheduleRtsCommand(() =>
		{
			ResetActiveArrowFacingHold();
			ClearSmoothingArcState();
			ClearRouteWaitState();
			ResetContinuousRouteLocomotionFlags();
			m_CommandQueue.Clear();
			m_Waypoints.Clear();
			m_ActiveFacingArrows = null;
			m_HasActiveDestination = false;
			ClearPathSegmentLineGeometry();

			if (m_ClickToMove != null)
				m_ClickToMove.HardStop();
			else if (m_LocomotionDriver != null)
				m_LocomotionDriver.HardStop();

			SetWantedFacingAngle(_angle);
			if (_showFacingIndicator)
				SetFacingIndicator(_angle, s_FacingArrowColor);
		}, _groupStaggerDelaySeconds);
	}

	/// <summary>
	/// Q / rotate-to-point: тот же сценарий, что у жёлтой стрелки на маршруте
	/// (доворот → скан → sector/return/hold). Маршрут и движение не сбрасываются.
	/// </summary>
	public void IssueYellowFacingCheckOrder(
		float _angle,
		float _groupStaggerDelaySeconds = 0f,
		bool _showFacingIndicator = false)
	{
		ScheduleRtsCommand(() =>
		{
			var arrow = new FacingArrow
			{
				Angle = _angle,
				Mode = FacingArrowMode.TurnOverDistance,
			};
			StartFacingTurn(arrow, transform.position, _isActiveSegment: true);
			if (_showFacingIndicator)
				SetFacingIndicator(_angle, s_FacingArrowColor);
		}, _groupStaggerDelaySeconds);
	}

	public bool HasActiveMovementIntent => HasActiveLocomotionMovement();

	public bool AllowsInMovementManualFacingOverride =>
		m_ArrowPriorityPhase == ArrowPriorityPhase.Turning ||
		m_ArrowPriorityPhase == ArrowPriorityPhase.YellowReturning ||
		m_ArrowPriorityPhase == ArrowPriorityPhase.BlueHold ||
		m_ArrowPriorityPhase == ArrowPriorityPhase.GreenHold ||
		(m_IsInFacingTurn && IsInMovementManualFacingMode(m_FacingTurnMode));

	[System.Obsolete("Use CurrentArrowPriorityPhase instead.")]
	public bool IsBlueGreenHolding =>
		m_ArrowPriorityPhase == ArrowPriorityPhase.BlueHold ||
		m_ArrowPriorityPhase == ArrowPriorityPhase.GreenHold;

	public ArrowPriorityPhase CurrentArrowPriorityPhase => m_ArrowPriorityPhase;

	public bool IsReturningToSavedTarget => m_ArrowPriorityPhase == ArrowPriorityPhase.YellowReturning;

	/// <summary>Ручной facing (стрелки, override, wanted facing) — ствол должен совпадать с заданным yaw.</summary>
	public bool IsManualBarrelFacingActive
	{
		get
		{
			if (m_ArrowPriorityPhase == ArrowPriorityPhase.Turning ||
			    m_ArrowPriorityPhase == ArrowPriorityPhase.BlueHold ||
			    m_ArrowPriorityPhase == ArrowPriorityPhase.GreenHold ||
			    m_ArrowPriorityPhase == ArrowPriorityPhase.YellowReturning)
				return true;

			if (m_ClickToMove != null && m_ClickToMove.OverrideFacingAngle.HasValue)
				return true;
			if (m_LocomotionDriver != null && m_LocomotionDriver.OverrideFacingAngle.HasValue)
				return true;
			if (m_IsRotatingToFacing || m_HasWantedFacing)
				return true;

			return false;
		}
	}

	public void SetFacingIndicator(float _angle, Color _color)
	{
		if (m_FacingIndicatorClearCoroutine != null)
			StopCoroutine(m_FacingIndicatorClearCoroutine);

		m_PersistentFacingIndicator = new FacingArrow
		{
			Angle = _angle,
			Mode = FacingArrowMode.TurnOverDistance,
		};
		m_PersistentFacingIndicatorColor = _color;
		MarkFacingArrowsDirty();
		m_FacingIndicatorClearCoroutine = StartCoroutine(ClearFacingIndicatorAfter(2f));
	}

	public void ClearFacingIndicator()
	{
		if (m_FacingIndicatorClearCoroutine != null)
		{
			StopCoroutine(m_FacingIndicatorClearCoroutine);
			m_FacingIndicatorClearCoroutine = null;
		}
		m_PersistentFacingIndicator = null;
		MarkFacingArrowsDirty();
	}

	private System.Collections.IEnumerator ClearFacingIndicatorAfter(float _seconds)
	{
		yield return new WaitForSeconds(_seconds);
		m_PersistentFacingIndicator = null;
		m_FacingIndicatorClearCoroutine = null;
		MarkFacingArrowsDirty();
	}

	public float? YellowRememberedAngle =>
		m_YellowDeferredActive ? m_YellowArrowAngle : (float?)null;

	public Vector3 YellowArrowWorldPos => m_YellowArrowWorldPos;

	public bool IsActiveRunOrSprintMovement
	{
		get
		{
			UnitClickToMove.MoveTier activeTier = m_IsExecutingSmoothingArc
				? m_SmoothingMoveTier
				: m_ActiveMoveTier;
			return IsRunOrSprintMoveTier(activeTier);
		}
	}

	private void ClearFacingOverride(string _reason = "unspecified")
	{
		if (m_ClickToMove != null)
			m_ClickToMove.OverrideFacingAngle = null;
		if (m_LocomotionDriver != null)
			m_LocomotionDriver.OverrideFacingAngle = null;
	}

	public void SetReadyWanted(bool _ready, float _groupStaggerDelaySeconds = 0f)
	{
		ScheduleRtsCommand(() =>
		{
			UnitFiremanCarryController firemanCarry = ResolveFiremanCarryController();
			if (_ready && firemanCarry != null && firemanCarry.IsCarryingFallen)
				return;

			if (m_ReadyHands != null)
				m_ReadyHands.SetReadyWanted(_ready);

			if (!_ready)
			{
				if (s_ReadyFacingTransitionEnabled)
					HandleReadyBecameFalse();
			}
			else
				DowngradeActiveMovementTierForReady();
		}, _groupStaggerDelaySeconds);
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

	public void RequestStance(LocomotionStance _stance, float _groupStaggerDelaySeconds = 0f)
	{
		if (_stance == LocomotionStance.Prone && !LocomotionProneFeature.Enabled)
			return;

		ScheduleRtsCommand(() =>
		{
			if (_stance == LocomotionStance.Prone)
				m_MagazineLoadingController?.StopLoading();

			if (m_Stance != null)
				m_Stance.RequestStance(_stance);
		}, _groupStaggerDelaySeconds);
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
		}, _immediate: true);
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

	public void StartManualMagazineLoading(float _groupStaggerDelaySeconds = 0f)
	{
		ScheduleRtsCommand(() =>
		{
			if (m_MagazineLoadingController == null)
				return;

			m_MagazineLoadingController.TryStartLoadingMagazineFromAmmoBoxes();
		}, _groupStaggerDelaySeconds);
	}

	public void StartWeaponReload(float _groupStaggerDelaySeconds = 0f)
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponReloadController == null)
				return;

			m_WeaponReloadController.TryStartReload();
		}, _groupStaggerDelaySeconds);
	}

	/// <summary>Следующий доступный режим огня по <see cref="WeaponDefinition.AvailableFireModes"/>.</summary>
	public void CycleWeaponFireMode(float _groupStaggerDelaySeconds = 0f)
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
			string disciplineLabel = WeaponFireDisciplineModeUtility.GetDisplayName(m_WeaponRuntime.SelectedFireDisciplineMode);
			Debug.Log(
				$"{name}: режим огня {WeaponFireModeUtility.GetDisplayName(before)} → {afterLabel}. " +
				$"Дисциплина сейчас: {disciplineLabel}. " +
				"Режим огня = что разрешено селектором; дисциплина = длина очередей, паузы и порог прицела.",
				this);
			PlayFireModeSwitchSound();
		}, _groupStaggerDelaySeconds);
	}

	/// <summary>
	/// Следующий режим огневой дисциплины: экономный → точный → подавляющий → авто.
	/// Режимы прицеливания больше не выбираются вручную — их заменяет дисциплина.
	/// </summary>
	public void CycleWeaponAimMode(float _groupStaggerDelaySeconds = 0f)
	{
		CycleWeaponFireDisciplineMode(_groupStaggerDelaySeconds);
	}

	/// <summary>Следующий режим огневой дисциплины юнита.</summary>
	public void CycleWeaponFireDisciplineMode(float _groupStaggerDelaySeconds = 0f)
	{
		ScheduleRtsCommand(() =>
		{
			if (m_WeaponRuntime == null)
			{
				Debug.LogWarning($"{name}: смена огневой дисциплины — нет runtime оружия.", this);
				return;
			}

			WeaponFireDisciplineMode before = m_WeaponRuntime.SelectedFireDisciplineMode;
			if (!m_WeaponRuntime.TryCycleToNextFireDisciplineMode(out WeaponFireDisciplineMode after))
			{
				Debug.Log($"{name}: огневая дисциплина не изменена. Сейчас: {before}.", this);
				return;
			}

			m_FireController?.ResetBurstStateForFireModeChange();

			Debug.Log(
				$"{name}: огневая дисциплина {WeaponFireDisciplineModeUtility.GetDisplayName(before)} → " +
				$"{WeaponFireDisciplineModeUtility.GetDisplayName(after)}. " +
				"Взаимодействие: режим огня (Semi/Burst/FullAuto/Auto) задаёт, что разрешено оружию; " +
				"дисциплина задаёт длину серий, паузы и порог прицела. " +
				"Оба в Auto — юнит сам выбирает стиль и механику под дистанцию.",
				this);
			PlayFireModeSwitchSound();
		}, _groupStaggerDelaySeconds);
	}

	private void PlayFireModeSwitchSound()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (weaponDefinition == null || !weaponDefinition.TryPickFireModeSwitchSound(out AudioClip clip))
			return;

		Vector3 position = transform.position + Vector3.up * 1.35f;
		if (m_UnitEquipment != null && m_UnitEquipment.MainWeaponRoot != null)
			position = m_UnitEquipment.MainWeaponRoot.position;

		UnitNonFireAudioUtility.PlayAtPoint(
			clip,
			position,
			weaponDefinition.FireModeSwitchSoundVolume,
			40f);
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
		ResetActiveArrowFacingHold();
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

	private void DequeueAndExecuteNextCommand(float _groupStaggerDelaySeconds = 0f)
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
		LogRouteDebugEvent($"DEQUEUE tier={cmd.MoveTier} dest={FormatRoutePoint(cmd.Destination)} wait={cmd.WaitGroup} {BuildRouteDebugSnapshot()}");
#endif

		m_ActiveFacingArrows = cmd.FacingArrows != null && cmd.FacingArrows.Count > 0
			? new List<FacingArrow>(cmd.FacingArrows) 
			: null;

		if (m_ActiveFacingArrows != null && m_ActiveFacingArrows.Count > 0 && persistFacingTurn)
		{
			ResetActiveArrowFacingHold();
			persistFacingTurn = false;
		}
		
		if (m_ActiveFacingArrows != null &&
		    HasReadyForcingFacingArrow(m_ActiveFacingArrows) &&
		    !IsRunOrSprintMoveTier(cmd.MoveTier) &&
		    m_ReadyHands != null &&
		    m_ReadyHands.IsWeaponEquipped() &&
		    !m_ReadyHands.WantsReady)
			m_ReadyHands.SetReadyWanted(true, false);

		if (!persistFacingTurn)
		{
			ClearFacingOverride();
			m_HasWantedFacing = false;
		}

		m_HasActiveDestination = true;
		m_ActiveDestinationWaitGroup = NormalizeWaitGroup(cmd.WaitGroup);
		if (cmd.WaitGroup >= 1 && cmd.HasWaitRouteBinding)
		{
			m_ActiveDestinationWaitHasRouteBinding = true;
			m_ActiveDestinationWaitSegmentIndex = cmd.WaitRouteSegmentIndex;
			m_ActiveDestinationWaitSegmentT = cmd.WaitRouteSegmentT;
		}
		else
		{
			m_ActiveDestinationWaitHasRouteBinding = false;
		}

		m_ActiveMoveTier = cmd.MoveTier;
		m_ActiveRouteStance = cmd.RouteStance;
		m_ActiveRouteSegmentStart = transform.position;
		m_RouteMarchEngaged = false;
		m_RouteSegmentLength = (FlattenToGround(cmd.Destination) - FlattenToGround(transform.position)).magnitude;
		m_DestinationSetTime = Time.time;
		ApplyFormationSlotPendingFromCommand(cmd);

		if (TryEnterActiveDestinationSegmentStartWaitImmediate())
		{
			TryActivateSegmentStartFacingArrows();
			RebindFacingArrowsAfterRouteTopologyChange();
			MarkFacingArrowsDirty();
			RebuildPathLine();
			return;
		}

		IssueMoveOrderForCurrentWaypoint(cmd.Destination, cmd.MoveTier, _groupStaggerDelaySeconds);

		TryActivateSegmentStartFacingArrows();
		RebindFacingArrowsAfterRouteTopologyChange();

		MarkFacingArrowsDirty();
		RebuildPathLine();
	}

	private void ApplyFormationSlotPendingFromCommand(QueuedCommand _cmd)
	{
		if (_cmd.HasPendingFormationSlotArrivalYaw)
			SetPendingFormationSlotArrivalYaw(_cmd.PendingFormationSlotArrivalYaw);
		else
			ClearPendingFormationSlotArrivalYaw();
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (FacingArrowDebug.ActivationLoggingEnabled)
			{
				FacingArrowDebug.Log(
					this,
					$"SEGMENT_START {FormatFacingArrowShort(rebound)} anchor={FormatRoutePoint(rebound.AnchorWorld)}");
			}
#endif

			StartFacingTurn(rebound, transform.position, _isActiveSegment: true);
		}
	}

	private void IssueMoveOrderForCurrentWaypoint(
		Vector3 _logicalDestination,
		UnitClickToMove.MoveTier _moveTier,
		float _groupStaggerDelaySeconds = 0f)
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
		IssueRouteMoveOrder(_logicalDestination, _moveTier, continuous, _groupStaggerDelaySeconds);
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
		// Arrival-wait destinations need a real stop so HasReachedActiveDestination can fire.
		bool suppressEarlyStop = IsIntermediateRouteSegment() && m_ActiveDestinationWaitGroup < 1;
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
		{
			if (!m_HasActiveDestination || m_Waypoints.Count == 0)
				return true;

			return IsNearDestination(transform.position, m_Waypoints[0], 0.2f);
		}

		if (float.IsPositiveInfinity(_agent.remainingDistance) || _agent.remainingDistance > 0.2f)
			return false;

		if (m_Waypoints.Count == 0)
			return true;

		return IsNearDestination(transform.position, m_Waypoints[0], 0.2f);
	}

	private void UpdateRouteMarchEngaged()
	{
		if (m_RouteMarchEngaged || !m_HasActiveDestination || m_Waypoints.Count == 0)
			return;

		Vector3 marched = FlattenToGround(transform.position) - FlattenToGround(m_ActiveRouteSegmentStart);
		if (marched.sqrMagnitude > 1f)
		{
			m_RouteMarchEngaged = true;
			return;
		}

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath
		    && !float.IsPositiveInfinity(agent.remainingDistance) && agent.remainingDistance > 1.5f)
			m_RouteMarchEngaged = true;
	}

	private bool HasEngagedRouteMarch()
	{
		if (m_RouteSegmentLength <= 0.75f)
			return true;

		return m_RouteMarchEngaged;
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

	private void SyncActiveLegMoveTierForNavOrder(Vector3 _worldPosition, UnitClickToMove.MoveTier _moveTier)
	{
		if (!m_HasActiveDestination || m_Waypoints.Count == 0)
			return;
		if (!IsNearDestination(_worldPosition, m_Waypoints[0], 0.75f))
			return;

		LocomotionStance routeStance = ResolveDefaultRouteStance(_moveTier);
		if (m_ActiveMoveTier == _moveTier && m_ActiveRouteStance == routeStance)
			return;

		m_ActiveMoveTier = _moveTier;
		m_ActiveRouteStance = routeStance;
		RebuildPathLine();
	}

	private bool ShouldPreserveFacingOverrideForNavOrder()
	{
		if (m_ArrowPriorityPhase != ArrowPriorityPhase.None)
			return true;
		if (m_IsInFacingTurn)
			return true;
		if (m_YellowDeferredActive)
			return true;
		if (m_PersistentFacingIndicator.HasValue)
			return true;

		return false;
	}

	private void ResetActiveMoveTierWhenIdle()
	{
		if (m_HasActiveDestination || m_Waypoints.Count > 0 || m_CommandQueue.Count > 0)
			return;
		if (m_IsWaitingAtRouteGate || m_IsExecutingSmoothingArc)
			return;

		if (IsRunOrSprintMoveTier(m_ActiveMoveTier))
		{
			m_ActiveMoveTier = UnitClickToMove.MoveTier.Walk;
			m_ActiveRouteStance = LocomotionStance.Standing;
		}
	}

	private void TransitionActiveMovementToWalk()
	{
		if (!IsRunOrSprintMoveTier(m_ActiveMoveTier) && !IsActiveRunOrSprintMovement)
			return;

		DowngradeMovementToWalkForArrowFacing();
		ClearFacingOverride();
	}

	/// <summary>Walk/sprint → walk для стрелочного facing; override не трогаем.</summary>
	private void DowngradeMovementToWalkForArrowFacing()
	{
		if (!IsRunOrSprintMoveTier(m_ActiveMoveTier) && !IsActiveRunOrSprintMovement)
			return;

		m_ActiveMoveTier = UnitClickToMove.MoveTier.Walk;

		if (m_IsExecutingSmoothingArc)
			m_SmoothingMoveTier = UnitClickToMove.MoveTier.Walk;

		if (m_ClickToMove != null)
			m_ClickToMove.ForceWalkMoveMode();
		if (m_LocomotionDriver != null)
			m_LocomotionDriver.ForceWalkMoveMode();
	}

	private bool IsWeaponReloadBusy()
	{
		if (m_WeaponReloadController == null)
			m_WeaponReloadController = GetComponent<UnitWeaponReloadController>();
		return m_WeaponReloadController != null && m_WeaponReloadController.IsReloadBusy;
	}

	private void ApplyReadyForArrowActivation()
	{
		UnitFiremanCarryController firemanCarry = ResolveFiremanCarryController();
		if (firemanCarry != null && firemanCarry.IsCarryingFallen)
			return;
		if (IsWeaponReloadBusy())
			return;

		if (m_ReadyHands != null &&
		    m_ReadyHands.IsWeaponEquipped() &&
		    !m_ReadyHands.WantsReady)
			m_ReadyHands.SetReadyWanted(true, false);
	}

	private void PrepareLocomotionForArrowFacing()
	{
		DowngradeMovementToWalkForArrowFacing();
		ApplyReadyForArrowActivation();
	}

	private void EnforceArrowPriorityLocomotionConstraints()
	{
		if (m_ArrowPriorityPhase == ArrowPriorityPhase.None)
			return;

		PrepareLocomotionForArrowFacing();
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

		if (TryActivateClosestFacingArrowInRange())
			return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogPendingFacingArrowsThrottled();
		LogMissedFacingArrowsIfAny();
#endif

		if (m_HasWantedFacing && !m_IsInFacingTurn)
		{
			ClearFacingOverride();
			m_HasWantedFacing = false;
		}
	}

	private bool TryActivateClosestFacingArrowInRange()
	{
		if (!m_HasActiveDestination || m_ActiveFacingArrows == null || m_ActiveFacingArrows.Count == 0)
			return false;

		Vector3 unitPos = transform.position;
		int bestIndex = -1;
		int bestSegmentIndex = int.MaxValue;
		float bestSegmentT = float.MaxValue;

		for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
		{
			FacingArrow arrow = m_ActiveFacingArrows[i];
			if (arrow.Mode == FacingArrowMode.TurnOnArrival)
				continue;

			if (!HasReachedFacingArrowAnchor(arrow, unitPos, out _))
				continue;

			int segmentIndex = arrow.RouteSegmentIndex;
			float segmentT = arrow.RouteSegmentT;
			if (bestIndex < 0 ||
			    segmentIndex < bestSegmentIndex ||
			    (segmentIndex == bestSegmentIndex && segmentT < bestSegmentT))
			{
				bestIndex = i;
				bestSegmentIndex = segmentIndex;
				bestSegmentT = segmentT;
			}
		}

		if (bestIndex < 0)
			return false;

		FacingArrow activatedArrow = m_ActiveFacingArrows[bestIndex];

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		EvaluateFacingArrowReach(
			activatedArrow,
			unitPos,
			out Vector3 activatedAnchor,
			out float activatedDist,
			out float activatedUnitT,
			out bool activatedHasUnitT,
			out string activatedReachReason);
		if (FacingArrowDebug.ActivationLoggingEnabled)
		{
			FacingArrowDebug.Log(
				this,
				$"ACTIVATED #{bestIndex} {FormatFacingArrowShort(activatedArrow)} " +
				$"reach={activatedReachReason} dist={activatedDist:F2} " +
				$"unitT={(activatedHasUnitT ? activatedUnitT.ToString("F3") : "n/a")} " +
				$"anchor={FormatRoutePoint(activatedAnchor)} pos={FormatRoutePoint(unitPos)} " +
				$"priorPhase={m_ArrowPriorityPhase}");
		}
#endif

		if (activatedArrow.Mode != FacingArrowMode.TurnOnArrival)
			m_ActiveFacingArrows.RemoveAt(bestIndex);

		StartFacingTurn(activatedArrow, unitPos, _isActiveSegment: true, _logActivation: false);
		MarkFacingArrowsDirty();
		return true;
	}

	private bool HasReachedFacingArrowAnchor(in FacingArrow _arrow, Vector3 _unitPos, out Vector3 _anchorWorld)
	{
		return EvaluateFacingArrowReach(
			_arrow,
			_unitPos,
			out _anchorWorld,
			out _,
			out _,
			out _,
			out _);
	}

	private bool EvaluateFacingArrowReach(
		in FacingArrow _arrow,
		Vector3 _unitPos,
		out Vector3 _anchorWorld,
		out float _planarDist,
		out float _unitSegmentT,
		out bool _hasUnitSegmentT,
		out string _reachReason)
	{
		_anchorWorld = default;
		_planarDist = float.MaxValue;
		_unitSegmentT = 0f;
		_hasUnitSegmentT = false;
		_reachReason = "invalidAnchor";

		if (!TryResolveFacingArrowAnchorForActivation(_arrow, out _anchorWorld))
			return false;

		float dx = _unitPos.x - _anchorWorld.x;
		float dz = _unitPos.z - _anchorWorld.z;
		_planarDist = Mathf.Sqrt(dx * dx + dz * dz);
		_hasUnitSegmentT = TryGetRouteSegmentProgressForUnit(_arrow.RouteSegmentIndex, _unitPos, out _unitSegmentT);

		if (_planarDist <= c_FacingArrowActivationReachRadius)
		{
			_reachReason = "distance";
			return true;
		}

		_reachReason = _hasUnitSegmentT
			? $"waiting dist={_planarDist:F2}/{c_FacingArrowActivationReachRadius:F2} unitT={_unitSegmentT:F3} arrowT={_arrow.RouteSegmentT:F3}"
			: $"waiting dist={_planarDist:F2}/{c_FacingArrowActivationReachRadius:F2} unitT=n/a arrowT={_arrow.RouteSegmentT:F3}";
		return false;
	}

	private bool TryGetRouteSegmentProgressForUnit(int _segmentIndex, Vector3 _unitWorld, out float _segmentT)
	{
		_segmentT = 0f;
		if (!IsFacingArrowSegmentBindingValid(_segmentIndex))
			return false;

		if (CollectRouteSegmentPolyline(_segmentIndex, m_RouteSegmentPolylineBuffer, _useLiveAgentPathForActiveLeg: false) &&
		    m_RouteSegmentPolylineBuffer.Count >= 2)
		{
			_segmentT = ComputeRouteSegmentTAlongPolyline(m_RouteSegmentPolylineBuffer, _unitWorld);
			return true;
		}

		bool useActiveSegmentStart = _segmentIndex == 0 && m_HasActiveDestination;
		if (TryGetRouteSegmentEndpoints(
			    _segmentIndex,
			    useActiveSegmentStart,
			    out Vector3 segmentStart,
			    out Vector3 segmentEnd))
		{
			_segmentT = ComputeRouteSegmentT(_unitWorld, segmentStart, segmentEnd);
			return true;
		}

		return false;
	}

	private void StartFacingTurn(
		FacingArrow _arrow,
		Vector3 _unitPos,
		bool _isActiveSegment = false,
		bool _logActivation = true)
	{
		bool preserveYellowTargetMemory = _arrow.Mode == FacingArrowMode.TurnOverDistance;
		ResetActiveArrowFacingHold(_clearYellowTargetMemory: !preserveYellowTargetMemory);

		if (_arrow.Mode != FacingArrowMode.TurnOnArrival)
			PrepareLocomotionForArrowFacing();

		ResolveCachedVision();
		if (!m_HasOldTargetAngle &&
		    m_CachedVision != null &&
		    m_CachedVision.VisibleTarget != null)
		{
			Vector3 toTarget = m_CachedVision.VisibleTarget.position - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude > 0.01f)
			{
				m_HasOldTargetAngle = true;
				m_OldTargetAngle = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
			}
		}

		if (_arrow.Mode == FacingArrowMode.HoldToEnd || _arrow.Mode == FacingArrowMode.LookAtPoint)
		{
			if (_arrow.Mode == FacingArrowMode.LookAtPoint && _arrow.HasLookPoint && _arrow.LookPointWorld == Vector3.zero)
			{
				_arrow.LookPointWorld = ResolveFacingArrowLookPoint(_arrow, _isActiveSegment);
			}

			m_PersistentFacingIndicator = _arrow;
			m_PersistentFacingIndicatorColor = GetFacingArrowColor(_arrow.Mode);
		}

		m_ArrowPriorityPhase = ArrowPriorityPhase.Turning;
		m_ArrowTurnScanRequested = false;
		m_ActiveArrowPriorityMode = _arrow.Mode;
		m_YellowDeferredActive = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (_logActivation && FacingArrowDebug.ActivationLoggingEnabled)
		{
			FacingArrowDebug.Log(
				this,
				$"START_TURN {FormatFacingArrowShort(_arrow)} pos={FormatRoutePoint(_unitPos)} activeSegment={_isActiveSegment}");
		}
#endif

		if (_arrow.Mode == FacingArrowMode.TurnOverDistance)
		{
			m_YellowArrowWorldPos = _unitPos;
			m_YellowArrowAngle = _arrow.Angle;
		}

		switch (_arrow.Mode)
		{
			case FacingArrowMode.TurnOverDistance:
				m_FacingTurnMode = FacingArrowMode.TurnOverDistance;
				m_FacingTurnStartPos = _unitPos;
				m_FacingTurnStartAngle = transform.eulerAngles.y;
				m_FacingTurnTargetAngle = _arrow.Angle;
				m_FacingTurnDistanceTraveled = 0f;
				break;

			case FacingArrowMode.HoldToEnd:
				m_FacingTurnMode = FacingArrowMode.HoldToEnd;
				m_FacingTurnTargetAngle = _arrow.Angle;
				break;

			case FacingArrowMode.LookAtPoint:
				m_FacingTurnMode = FacingArrowMode.LookAtPoint;
				m_FacingLookPoint = _arrow.HasLookPoint
					? ResolveFacingArrowLookPoint(_arrow, _isActiveSegment)
					: ResolveFacingArrowAnchor(_arrow, _isActiveSegment) +
					  Quaternion.Euler(0f, _arrow.Angle, 0f) * Vector3.forward * c_FacingArrowFixedLength;
				break;

			case FacingArrowMode.TurnOnArrival:
				break;
		}

		// Arrow-priority (TurnOverDistance / Blue / Green) ведёт facing через UpdateArrowPriority, не legacy UpdateFacingTurn.
		m_IsInFacingTurn = false;
	}

	private bool IsFinalRouteStopForArrivalFacing()
	{
		if (m_IsWaitingAtRouteGate)
			return false;
		if (m_CommandQueue.Count > 0)
			return false;
		if (m_IsExecutingSmoothingArc)
			return false;

		return m_Waypoints.Count <= 1;
	}

	private bool IsCurrentSegmentStopForArrivalFacing()
	{
		if (m_IsWaitingAtRouteGate || m_IsExecutingSmoothingArc)
			return false;
		if (!m_HasActiveDestination || m_Waypoints.Count == 0)
			return false;

		return true;
	}

	private bool HasPendingTurnOnArrivalFacing()
	{
		if (m_ActiveFacingArrows == null || m_ActiveFacingArrows.Count == 0)
			return false;

		for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
		{
			if (m_ActiveFacingArrows[i].Mode == FacingArrowMode.TurnOnArrival)
				return true;
		}

		return false;
	}

	private bool TryActivateFormationSlotArrivalFacing()
	{
		if (m_IsRotatingToFacing || m_HasWantedFacing)
			return false;
		if (!IsCurrentSegmentStopForArrivalFacing())
			return false;
		if (IsPhysicallyMoving())
			return false;
		if (!m_HasPendingFormationSlotArrivalYaw)
			return false;
		if (!HasEngagedRouteMarch())
			return false;
		if (HasManualRouteFacingActive())
			return false;
		if (m_FormationSyncGroup == null || m_FormationSyncGroup.Members.Count < 2)
			return false;

		float yaw = m_PendingFormationSlotArrivalYaw;
		m_HasPendingFormationSlotArrivalYaw = false;

		ClearFacingTurn();
		ClearFacingOverride("FormationSlotArrival.activate");
		StopNavAgentForArrivalFacing();
		m_HasWantedFacing = true;
		m_WantedFacingAngle = yaw;
		m_IsRotatingToFacing = true;
		m_WasReadyBeforeFacing = m_ReadyHands != null && m_ReadyHands.WantsReady;
		return true;
	}

	private bool TryActivateTurnOnArrivalFacing()
	{
		if (m_IsRotatingToFacing || m_HasWantedFacing)
			return false;
		if (!IsFinalRouteStopForArrivalFacing())
			return false;
		if (IsPhysicallyMoving())
			return false;
		if (m_ActiveFacingArrows == null || m_ActiveFacingArrows.Count == 0)
			return false;

		for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
		{
			if (m_ActiveFacingArrows[i].Mode != FacingArrowMode.TurnOnArrival)
				continue;

			FacingArrow arrow = m_ActiveFacingArrows[i];
			m_ActiveFacingArrows.RemoveAt(i);
			MarkFacingArrowsDirty();

			ClearFacingTurn();
			ClearFacingOverride("TurnOnArrival.activate");
			StopNavAgentForArrivalFacing();
			m_HasWantedFacing = true;
			m_WantedFacingAngle = arrow.Angle;
			m_IsRotatingToFacing = true;
			m_WasReadyBeforeFacing = m_ReadyHands != null && m_ReadyHands.WantsReady;
			return true;
		}

		return false;
	}

	private void StopNavAgentForArrivalFacing()
	{
		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent == null || !agent.enabled || !agent.isOnNavMesh)
			return;

		agent.isStopped = true;
		agent.ResetPath();
	}

	private void UpdateFacingTurn()
	{
		if (!m_IsInFacingTurn)
			return;

		if (m_ArrowPriorityPhase != ArrowPriorityPhase.None)
			return;

		// Жёлтая стрелка и hold-фазы полностью обслуживаются UpdateArrowPriority — legacy turn не должен
		// снова выставлять override после PHASE -> None (иначе engage/path дёргаются).
		if (m_YellowDeferredActive)
			return;

		if (s_ReadyFacingTransitionEnabled &&
		    !WantsReady &&
		    (IsPhysicallyMoving() || HasActiveLocomotionMovement()))
		{
			if (m_FacingAutoRestoreReady ||
			    (m_ReadyHands != null && m_ReadyHands.HasPendingReadyRestore))
				return;

			ClearFacingTurn();
			ClearFacingOverride("UpdateFacingTurn.notReady");
			BeginLocomotionNotReadyFacingRealign();
			return;
		}

		if (!m_HasActiveDestination && !IsExecutingMoveOrder())
		{
			ClearFacingTurn(ShouldPreserveHeadingOnLegArrival());
			return;
		}

		UnitClickToMove.MoveTier activeTier = m_IsExecutingSmoothingArc
			? m_SmoothingMoveTier
			: m_ActiveMoveTier;
		if (IsRunOrSprintMoveTier(activeTier))
			return;

		switch (m_FacingTurnMode)
		{
			case FacingArrowMode.TurnOverDistance:
			{
				float dx = transform.position.x - m_FacingTurnStartPos.x;
				float dz = transform.position.z - m_FacingTurnStartPos.z;
				float dist = Mathf.Sqrt(dx * dx + dz * dz);
				m_FacingTurnDistanceTraveled = dist;

				ApplyLocomotionFacingOverride(m_FacingTurnTargetAngle, "FacingTurn.TurnOverDistance");

				if (dist >= m_FacingTurnOverDistance)
					ClearFacingTurn();
				break;
			}

			case FacingArrowMode.HoldToEnd:
				ApplyLocomotionFacingOverride(m_FacingTurnTargetAngle, "FacingTurn.HoldToEnd");
				break;

			case FacingArrowMode.LookAtPoint:
			{
				Vector3 toLook = m_FacingLookPoint - transform.position;
				toLook.y = 0f;
				if (toLook.sqrMagnitude > 0.01f)
				{
					float angle = Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg;
					ApplyLocomotionFacingOverride(angle, "FacingTurn.LookAtPoint");
				}
				break;
			}
		}
	}

	private void UpdateArrowPriority()
	{
		EnforceArrowPriorityLocomotionConstraints();

		switch (m_ArrowPriorityPhase)
		{
			case ArrowPriorityPhase.Turning:
				UpdateTurningPhase();
				break;

			case ArrowPriorityPhase.YellowReturning:
				UpdateYellowReturningPhase();
				break;

			case ArrowPriorityPhase.BlueHold:
				UpdateBlueHoldPhase();
				break;

			case ArrowPriorityPhase.GreenHold:
				UpdateGreenHoldPhase();
				break;
		}

		UpdateYellowDeferredMonitor();
	}

	private void UpdateTurningPhase()
	{
		float centerAngle = GetArrowCenterAngle();
		ApplyLocomotionFacingOverride(centerAngle, "ArrowPriority.turning");

		if (!IsFacingAngleReached(centerAngle))
			return;

		if (!m_ArrowTurnScanRequested)
		{
			ResolveCachedVision();
			m_CachedVision?.RequestImmediateScan();
			m_ArrowTurnScanRequested = true;
		}

		switch (m_ActiveArrowPriorityMode)
		{
			case FacingArrowMode.TurnOverDistance:
				CompleteYellowArrowPostScan();
				break;

			case FacingArrowMode.HoldToEnd:
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (FacingArrowDebug.PhaseLoggingEnabled)
					FacingArrowDebug.Log(this, $"PHASE Turning->BlueHold angle={m_FacingTurnTargetAngle:F1}");
#endif
				m_ArrowPriorityPhase = ArrowPriorityPhase.BlueHold;
				break;

			case FacingArrowMode.LookAtPoint:
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (FacingArrowDebug.PhaseLoggingEnabled)
					FacingArrowDebug.Log(this, $"PHASE Turning->GreenHold look={FormatRoutePoint(m_FacingLookPoint)}");
#endif
				m_ArrowPriorityPhase = ArrowPriorityPhase.GreenHold;
				break;
		}
	}

	private void CompleteYellowArrowPostScan()
	{
		ResolveCachedVision();
		bool hasTargetInArrowSector = HasTargetInYellowArrowScanSector();

		if (hasTargetInArrowSector)
		{
			ClearOldTargetAngle("postScan.sectorHit");
			m_YellowDeferredActive = true;
			FinishArrowPriorityTurning(_clearOverride: true);
			return;
		}

		if (m_HasOldTargetAngle)
		{
			m_IsInFacingTurn = false;
			m_ArrowPriorityPhase = ArrowPriorityPhase.YellowReturning;
			ApplyLocomotionFacingOverride(m_OldTargetAngle, "Yellow.returnOldTarget");
			return;
		}

		if (HasActiveMovementIntent)
			ClearFacingOverride("Yellow.movingNoTarget");
		else
			ApplyLocomotionFacingOverride(m_YellowArrowAngle, "Yellow.standingHold");

		ClearOldTargetAngle("postScan.fallback");
		FinishArrowPriorityTurning(_clearOverride: false);
	}

	private bool HasTargetInYellowArrowScanSector()
	{
		if (m_CachedVision == null)
			return false;

		float halfFov = m_CachedVision.ResolveHalfFovDegreesForScan();
		return m_CachedVision.TryFindTargetInDirection(m_YellowArrowAngle, halfFov, out _);
	}

	private void FinishArrowPriorityTurning(bool _clearOverride)
	{
		m_IsInFacingTurn = false;
		m_ArrowTurnScanRequested = false;
		m_ArrowPriorityPhase = ArrowPriorityPhase.None;

		if (_clearOverride)
			ClearFacingOverride("ArrowPriority.finishTurning");
	}

	private void UpdateYellowReturningPhase()
	{
		if (!m_HasOldTargetAngle)
		{
			m_IsInFacingTurn = false;
			ClearFacingOverride("YellowReturning.noOldTarget");
			m_ArrowPriorityPhase = ArrowPriorityPhase.None;
			return;
		}

		ResolveCachedVision();
		if (m_CachedVision != null && m_CachedVision.VisibleTarget != null)
		{
			ClearOldTargetAngle("yellowReturning.targetVisible");
			m_YellowDeferredActive = true;
			m_IsInFacingTurn = false;
			ClearFacingOverride("YellowReturning.targetAcquired");
			m_ArrowPriorityPhase = ArrowPriorityPhase.None;
			return;
		}

		// Держим угол старой цели на ходу, пока цель снова не попадёт в зрение или маршрут не сбросит память.
		ApplyLocomotionFacingOverride(m_OldTargetAngle, "YellowReturning.toOldTarget");
	}

	private void UpdateYellowDeferredMonitor()
	{
		if (!m_YellowDeferredActive || m_ArrowPriorityPhase != ArrowPriorityPhase.None)
			return;

		if (!IsNearYellowArrowPosition())
		{
			m_YellowDeferredActive = false;
			return;
		}

		ResolveCachedVision();
		if (m_CachedVision != null && m_CachedVision.VisibleTarget != null)
			return;

		if (!HasActiveMovementIntent)
			return;

		BeginYellowDeferredTurn();
	}

	private void BeginYellowDeferredTurn()
	{
		ClearOldTargetAngle("deferred.rescan");
		PrepareLocomotionForArrowFacing();
		m_ActiveArrowPriorityMode = FacingArrowMode.TurnOverDistance;
		m_FacingTurnMode = FacingArrowMode.TurnOverDistance;
		m_FacingTurnTargetAngle = m_YellowArrowAngle;
		m_IsInFacingTurn = false;
		m_ArrowPriorityPhase = ArrowPriorityPhase.Turning;
		m_ArrowTurnScanRequested = false;
	}

	private void UpdateBlueHoldPhase()
	{
		float centerAngle = m_FacingTurnTargetAngle;
		ResolveCachedVision();

		if (m_CachedVision != null && m_CachedVision.VisibleTarget != null)
		{
			Vector3 toTarget = m_CachedVision.VisibleTarget.position - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude > 0.01f)
			{
				float targetAngle = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
				ApplyLocomotionFacingOverride(targetAngle, "BlueHold.lookTarget");
				return;
			}
		}

		ApplyLocomotionFacingOverride(centerAngle, "BlueHold.lookCenter");
	}

	private void UpdateGreenHoldPhase()
	{
		Vector3 unitPos = transform.position;
		Vector3 toLook = m_FacingLookPoint - unitPos;
		toLook.y = 0f;
		if (toLook.sqrMagnitude < 0.01f)
			return;

		float centerAngle = Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg;
		ResolveCachedVision();
		float halfFov = m_CachedVision != null
			? m_CachedVision.ResolveHalfFovDegreesForScan()
			: 60f;

		if (m_CachedVision != null && m_CachedVision.VisibleTarget != null)
		{
			Vector3 toTarget = m_CachedVision.VisibleTarget.position - unitPos;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude > 0.01f)
			{
				float targetAngle = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
				float delta = Mathf.DeltaAngle(centerAngle, targetAngle);

				if (Mathf.Abs(delta) <= halfFov)
				{
					ApplyLocomotionFacingOverride(targetAngle, "GreenHold.lookTarget");
					return;
				}
			}
		}

		ApplyLocomotionFacingOverride(centerAngle, "GreenHold.lookCenter");
	}

	private void ClearArrowPriorityState()
	{
		m_ArrowPriorityPhase = ArrowPriorityPhase.None;
		m_ArrowTurnScanRequested = false;
		m_YellowDeferredActive = false;
		m_YellowArrowWorldPos = default;
		m_YellowArrowAngle = 0f;
		m_ActiveArrowPriorityMode = FacingArrowMode.TurnOverDistance;
	}

	/// <summary>
	/// Полный сброс BlueHold/GreenHold/Turning/YellowReturning и связанного manual facing override.
	/// </summary>
	private void ResetActiveArrowFacingHold(bool _clearYellowTargetMemory = true)
	{
		ClearArrowPriorityState();
		if (_clearYellowTargetMemory)
			ClearOldTargetAngle("reset.hold");
		m_IsInFacingTurn = false;
		m_FacingTurnMode = FacingArrowMode.TurnOverDistance;
		m_FacingRotateVelocity = 0f;
		m_FacingSuppressedReady = false;
		m_FacingAutoRestoreReady = false;
		m_HasWantedFacing = false;
		ClearFacingIndicator();
		ClearFacingOverride();
	}

	private void ClearOldTargetAngle(string _reason = null)
	{
		if (!m_HasOldTargetAngle)
			return;

		m_HasOldTargetAngle = false;
		m_OldTargetAngle = 0f;
	}

	private bool IsArrowPriorityBlockingReactivation()
	{
		return m_ArrowPriorityPhase == ArrowPriorityPhase.Turning ||
		       m_ArrowPriorityPhase == ArrowPriorityPhase.BlueHold ||
		       m_ArrowPriorityPhase == ArrowPriorityPhase.GreenHold ||
		       m_ArrowPriorityPhase == ArrowPriorityPhase.YellowReturning;
	}

	private bool HasActiveArrowPriorityHold()
	{
		if (IsArrowPriorityBlockingReactivation())
			return true;

		if (m_YellowDeferredActive)
			return true;

		if (m_PersistentFacingIndicator.HasValue)
		{
			FacingArrowMode mode = m_PersistentFacingIndicator.Value.Mode;
			if (mode == FacingArrowMode.HoldToEnd || mode == FacingArrowMode.LookAtPoint)
				return true;
		}

		return m_IsInFacingTurn &&
		       (m_FacingTurnMode == FacingArrowMode.HoldToEnd ||
		        m_FacingTurnMode == FacingArrowMode.LookAtPoint);
	}

	private void ResetArrowHoldForNewRouteArrow(string _reason)
	{
		if (!HasActiveArrowPriorityHold())
			return;

		ResetActiveArrowFacingHold();
	}

	private float GetArrowCenterAngle()
	{
		if (m_FacingTurnMode == FacingArrowMode.LookAtPoint)
		{
			Vector3 toLook = m_FacingLookPoint - transform.position;
			toLook.y = 0f;
			if (toLook.sqrMagnitude > 0.01f)
				return Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg;
			return m_FacingTurnTargetAngle;
		}

		return m_FacingTurnTargetAngle;
	}

	private bool IsFacingAngleReached(float _targetBarrelYaw)
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();

		return UnitHorizontalFacingUtility.IsBarrelYawReached(
			transform,
			m_UnitEquipment,
			m_ReadyHands,
			_targetBarrelYaw,
			c_ArrowFullTurnThresholdDegrees);
	}

	private UnitVision ResolveCachedVision()
	{
		if (m_CachedVision == null)
			m_CachedVision = GetComponent<UnitVision>();
		return m_CachedVision;
	}

	private bool IsNearYellowArrowPosition()
	{
		float dx = transform.position.x - m_YellowArrowWorldPos.x;
		float dz = transform.position.z - m_YellowArrowWorldPos.z;
		return dx * dx + dz * dz <= c_YellowArrowMaxWanderDistance * c_YellowArrowMaxWanderDistance;
	}

	private void ClearFacingTurn(bool _preserveHeadingOnArrival = false)
	{
		ArrowPriorityPhase phase = m_ArrowPriorityPhase;
		FacingArrowMode turnMode = m_FacingTurnMode;
		float targetAngle = m_FacingTurnTargetAngle;

		m_IsInFacingTurn = false;
		m_FacingTurnMode = FacingArrowMode.TurnOverDistance;

		if (phase == ArrowPriorityPhase.BlueHold ||
		    phase == ArrowPriorityPhase.GreenHold ||
		    phase == ArrowPriorityPhase.Turning ||
		    phase == ArrowPriorityPhase.YellowReturning)
		{
			if (turnMode == FacingArrowMode.LookAtPoint)
			{
				Vector3 toLook = m_FacingLookPoint - transform.position;
				toLook.y = 0f;
				if (toLook.sqrMagnitude > 0.01f)
				{
					float angle = Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg;
					ApplyLocomotionFacingOverride(angle, "FacingTurn.priorityPreserve");
				}
			}
			else
			{
				ApplyLocomotionFacingOverride(targetAngle, "FacingTurn.priorityPreserve");
			}

			return;
		}

		if (_preserveHeadingOnArrival)
			ApplyLocomotionFacingOverride(targetAngle, "FacingTurn.arrivalPreserve");
		else
			ClearFacingOverride();
		m_HasWantedFacing = false;
		m_FacingRotateVelocity = 0f;
		m_FacingSuppressedReady = false;
		m_FacingAutoRestoreReady = false;
	}

	private bool ShouldPreserveHeadingOnLegArrival()
	{
		if (!m_IsInFacingTurn)
			return false;

		if (m_FacingTurnMode != FacingArrowMode.TurnOverDistance &&
		    m_FacingTurnMode != FacingArrowMode.HoldToEnd)
			return false;

		return m_CommandQueue.Count == 0;
	}

	private void TrackReadyWantedTransition()
	{
		bool wantsReady = WantsReady;
		if (s_ReadyFacingTransitionEnabled && m_LastTrackedWantsReady && !wantsReady)
			HandleReadyBecameFalse();

		m_LastTrackedWantsReady = wantsReady;
	}

	private void HandleReadyBecameFalse()
	{
		if (!s_ReadyFacingTransitionEnabled)
			return;

		if (m_FacingAutoRestoreReady)
		{
			m_FacingAutoRestoreReady = false;
			return;
		}

		if (m_ReadyHands != null && m_ReadyHands.HasPendingReadyRestore)
			return;

		m_FacingSuppressedReady = false;

		if (!HasActiveLocomotionMovement() && !IsPhysicallyMoving())
			return;

		if (m_IsInFacingTurn)
			m_IsInFacingTurn = false;

		ClearFacingOverride("HandleReadyBecameFalse");
		m_HasWantedFacing = false;
		m_FacingRotateVelocity = 0f;
		BeginLocomotionNotReadyFacingRealign();
	}

	private void BeginLocomotionNotReadyFacingRealign()
	{
		if (!s_ReadyFacingTransitionEnabled)
			return;

		if (m_ClickToMove != null)
			m_ClickToMove.BeginNotReadyMovementFacingRealign();
		else if (m_LocomotionDriver != null)
			m_LocomotionDriver.BeginNotReadyMovementFacingRealign();
	}

	private float GetEffectiveRotateSpeed()
	{
		if (m_ClickToMove != null)
			return m_ClickToMove.RotateSpeed;
		if (m_LocomotionDriver != null)
			return m_LocomotionDriver.RotateSpeed;
		return 6f;
	}

	private void ApplyLocomotionFacingOverride(float _angle, string _reason)
	{
		// _angle — world yaw линии огня (ствол) в high ready; компенсация корня в locomotion driver.
		if (m_ClickToMove != null)
			m_ClickToMove.OverrideFacingAngle = _angle;
		if (m_LocomotionDriver != null)
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
		return m_ArrowPriorityPhase == ArrowPriorityPhase.BlueHold ||
		       m_ArrowPriorityPhase == ArrowPriorityPhase.GreenHold;
	}

	private bool ShouldClearFacingOnLegArrival()
	{
		if (m_IsRotatingToFacing)
			return false;

		if (m_ArrowPriorityPhase == ArrowPriorityPhase.BlueHold ||
		    m_ArrowPriorityPhase == ArrowPriorityPhase.GreenHold ||
		    m_ArrowPriorityPhase == ArrowPriorityPhase.Turning ||
		    m_ArrowPriorityPhase == ArrowPriorityPhase.YellowReturning)
			return false;

		if (m_IsInFacingTurn)
		{
			if (m_FacingTurnMode == FacingArrowMode.LookAtPoint)
				return m_CommandQueue.Count == 0;

			if (m_FacingTurnMode == FacingArrowMode.HoldToEnd)
				return true;

			return true;
		}

		return m_HasWantedFacing;
	}

	private void MarkFacingArrowsDirty()
	{
		m_FacingArrowsDirty = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		m_FacingArrowMissReportedKeys.Clear();
		m_FacingArrowVisualDriftReportedKeys.Clear();
#endif
	}

	private void SyncFacingArrows()
	{
		if (!m_FacingArrowsDirty)
			return;
		m_FacingArrowsDirty = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		int activeCount = m_ActiveFacingArrows != null ? m_ActiveFacingArrows.Count : 0;
		int queuedCount = 0;
		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			if (m_CommandQueue[commandIndex].FacingArrows != null)
				queuedCount += m_CommandQueue[commandIndex].FacingArrows.Count;
		}
#endif

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

		if (m_PersistentFacingIndicator.HasValue)
		{
			CreatePersistentFacingArrowVisual(m_PersistentFacingIndicator.Value, m_PersistentFacingIndicatorColor);
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (FacingArrowDebug.VisualSyncLoggingEnabled &&
		    (activeCount > 0 || queuedCount > 0 || m_PersistentFacingIndicator.HasValue))
		{
			FacingArrowDebug.Log(
				this,
				$"VISUAL_SYNC rebuilt active={activeCount} queued={queuedCount} wp={m_Waypoints.Count} " +
				$"hasDest={m_HasActiveDestination} legStart={FormatRoutePoint(m_ActiveRouteSegmentStart)}");
		}
#endif
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
		if (_mode == FacingArrowMode.LookAtPoint && _hasLookPoint)
		{
			Vector3 toLook = _lookPoint - _anchor;
			toLook.y = 0f;
			if (toLook.sqrMagnitude > 0.0001f)
			{
				Vector3 dirToLook = toLook.normalized;
				_shaftStart = _anchor + dirToLook * c_FacingArrowShaftStartOffset + s_FacingArrowYOffset;
			}
			else
				_shaftStart = _anchor + s_FacingArrowYOffset;

			_shaftEnd = _lookPoint + s_FacingArrowYOffset;
			return;
		}

		Vector3 dir = Quaternion.Euler(0f, _angle, 0f) * Vector3.forward;
		_shaftStart = _anchor + dir * c_FacingArrowShaftStartOffset + s_FacingArrowYOffset;
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
	private void CreatePersistentFacingArrowVisual(FacingArrow _arrow, Color _color)
	{
		if (s_PathLineMaterial == null)
			return;

		Vector3 anchor = transform.position;
		Vector3 lookPoint = _arrow.HasLookPoint
			? ResolveFacingArrowLookPoint(_arrow, _isActiveSegment: false)
			: Vector3.zero;
		GetFacingArrowShaftEndpoints(
			anchor,
			_arrow.Angle,
			_arrow.Mode,
			lookPoint,
			_arrow.HasLookPoint,
			out Vector3 shaftStart,
			out Vector3 shaftEnd);

		GameObject go = new GameObject("FacingArrow.Persistent");
		go.transform.SetParent(transform, false);
		LineRenderer lr = go.AddComponent<LineRenderer>();
		lr.positionCount = 2;
		lr.startWidth = 0.02f;
		lr.endWidth = 0.02f;
		lr.sharedMaterial = s_PathLineMaterial;
		lr.startColor = _color;
		lr.endColor = _color;
		lr.enabled = m_IsSelected;
		lr.SetPosition(0, shaftStart);
		lr.SetPosition(1, shaftEnd);

		m_FacingArrowVisuals.Add(new FacingArrowVisualSource
		{
			Line = lr,
			IsActiveSegment = false,
			CommandIndex = -1,
			ArrowIndex = -1,
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

			if (source.CommandIndex < 0 && m_PersistentFacingIndicator.HasValue)
			{
				line.enabled = m_IsSelected;
				FacingArrow pa = m_PersistentFacingIndicator.Value;
				Vector3 paAnchor = transform.position;
				Vector3 paLook = pa.HasLookPoint
					? (m_ArrowPriorityPhase == ArrowPriorityPhase.GreenHold
						? m_FacingLookPoint
						: ResolveFacingArrowLookPoint(pa, _isActiveSegment: false))
					: Vector3.zero;
				GetFacingArrowShaftEndpoints(
					paAnchor,
					pa.Angle,
					pa.Mode,
					paLook,
					pa.HasLookPoint,
					out Vector3 paShaftStart,
					out Vector3 paShaftEnd);
				line.startColor = m_PersistentFacingIndicatorColor;
				line.endColor = m_PersistentFacingIndicatorColor;
				line.SetPosition(0, paShaftStart);
				line.SetPosition(1, paShaftEnd);
				continue;
			}

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
		if (HasNonArrivalFacingArrows(m_ActiveFacingArrows))
			return true;
		if (m_HasWantedFacing)
			return true;
		return false;
	}

	private static bool HasNonArrivalFacingArrows(List<FacingArrow> _arrows)
	{
		if (_arrows == null)
			return false;

		for (int i = 0; i < _arrows.Count; i++)
		{
			if (_arrows[i].Mode != FacingArrowMode.TurnOnArrival)
				return true;
		}

		return false;
	}

	private static bool IsInMovementManualFacingMode(FacingArrowMode _mode)
	{
		return _mode == FacingArrowMode.HoldToEnd || _mode == FacingArrowMode.LookAtPoint;
	}

	private bool IsPhysicallyMoving()
	{
		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.enabled && agent.isOnNavMesh)
		{
			Vector3 velocity = new Vector3(agent.velocity.x, 0f, agent.velocity.z);
			if (velocity.sqrMagnitude > 0.01f)
				return true;
		}

		return false;
	}

	private void ScheduleRtsCommand(Action _command, float _groupStaggerDelaySeconds = 0f, bool _immediate = false)
	{
		if (_command == null)
			return;

		m_PendingCommandVersion++;
		int version = m_PendingCommandVersion;

		if (m_PendingCommandCoroutine != null)
			StopCoroutine(m_PendingCommandCoroutine);

		if (_immediate)
		{
			m_PendingCommandCoroutine = null;
			_command();
			return;
		}

		float totalDelay = ResolveCommandReactionDelaySeconds() + Mathf.Max(0f, _groupStaggerDelaySeconds);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (totalDelay > 0f)
		{
			LogRouteDebugEvent($"CMD_SCHEDULED delay={totalDelay:F2}s version={version}");
		}
#endif
		if (totalDelay <= 0f)
		{
			m_PendingCommandCoroutine = null;
			_command();
			return;
		}

		m_PendingCommandCoroutine = StartCoroutine(ExecutePendingRtsCommandRoutine(version, totalDelay, _command));
	}

	private float ResolveCommandReactionDelaySeconds()
	{
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();

		return m_CombatStats != null ? m_CombatStats.GetReactionDelaySeconds() : 0.35f;
	}

	private IEnumerator ExecutePendingRtsCommandRoutine(int _version, float _delaySeconds, Action _command)
	{
		yield return new WaitForSecondsRealtime(_delaySeconds);

		if (_version != m_PendingCommandVersion)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent($"CMD_CANCELLED staleVersion={_version} current={m_PendingCommandVersion}");
#endif
			yield break;
		}

		m_PendingCommandCoroutine = null;
		_command?.Invoke();
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
		if (m_SelectionNameLabelRoot == null)
		{
			m_SelectionNameLabelRoot = new GameObject("SelectionNameLabel", typeof(RectTransform));
			RectTransform rt = m_SelectionNameLabelRoot.GetComponent<RectTransform>();
			rt.SetParent(transform, false);
			rt.sizeDelta = new Vector2(2f, 0.5f);

			Canvas canvas = m_SelectionNameLabelRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.sortingOrder = 31500;
		}

		if (m_SelectionNameLabelRoot.TryGetComponent(out UnityEngine.UI.GraphicRaycaster legacyRaycaster))
			Destroy(legacyRaycaster);

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
			m_SelectionNameText.raycastTarget = false;
			m_SelectionNameText.fontSize = 0.15f;
			m_SelectionNameText.alignment = TextAlignmentOptions.Center;
			m_SelectionNameText.color = Color.white;
			m_SelectionNameText.outlineWidth = 0.35f;
			m_SelectionNameText.outlineColor = Color.black;
			m_SelectionNameText.fontStyle = FontStyles.Bold;
		}
		else
		{
			m_SelectionNameText.raycastTarget = false;
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
		m_ActiveDestinationWaitGroup = 0;
		m_ActiveDestinationWaitHasRouteBinding = false;
		ResumeAgentAfterRouteGate();
	}

	private bool TryGetWaitGroupForWaypoint(int _waypointIndex, out int _waitGroup)
	{
		_waitGroup = 0;

		if (_waypointIndex < 0)
		{
			if (!m_IsWaitingAtRouteGate || m_ActiveWaitGroup < 1)
				return false;
			_waitGroup = m_ActiveWaitGroup;
			return true;
		}

		if (_waypointIndex >= m_Waypoints.Count)
			return false;

		if (m_HasActiveDestination && _waypointIndex == 0)
		{
			_waitGroup = m_ActiveDestinationWaitGroup;
			return _waitGroup >= 1;
		}

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

		if (m_HasActiveDestination && _waypointIndex == 0)
		{
			m_ActiveDestinationWaitGroup = normalizedGroup;
			m_ActiveDestinationWaitHasRouteBinding = normalizedGroup >= 1;
			m_ActiveDestinationWaitSegmentIndex = 0;
			m_ActiveDestinationWaitSegmentT = 1f;
			MarkFacingArrowsDirty();
			return true;
		}

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

		return true;
	}

	private void TryAdvanceRouteQueue()
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"ADVANCE_QUEUE {BuildRouteDebugSnapshot()}");
#endif
		if (TryStartGrenadeOrderAtWaypoint())
			return;

		ShiftGrenadeOrdersAfterWaypointRemoved();

		int arrivalWaitGroup = m_ActiveDestinationWaitGroup;
		Vector3 arrivalWaitIconPos = ResolveActiveDestinationWaitIconWorldPosition();
		bool waitAtSegmentEnd = arrivalWaitGroup >= 1 &&
		                        (!m_ActiveDestinationWaitHasRouteBinding ||
		                         IsWaitBindingAtSegmentEnd(m_ActiveDestinationWaitSegmentT));

		m_ActiveDestinationWaitGroup = 0;
		m_ActiveDestinationWaitHasRouteBinding = false;

		if (m_Waypoints.Count > 0)
		{
			ShiftFacingArrowSegmentsAfterWaypointRemoved(0);
			ShiftWaitHoldSegmentsAfterWaypointRemoved(0);
			m_Waypoints.RemoveAt(0);
		}

		// Rebind while the completed leg is still marked active and uses stale legStart.
		m_HasActiveDestination = false;
		RebindFacingArrowsAfterRouteTopologyChange();
		RebuildPathLine();
		ClearSmoothingArcState();

		if (arrivalWaitGroup >= 1 && waitAtSegmentEnd)
		{
			EnterWaitAfterArrival(arrivalWaitGroup, arrivalWaitIconPos);
			return;
		}

		TryStartNextQueuedCommand();
	}

	private void ShiftGrenadeOrdersAfterWaypointRemoved()
	{
		for (int i = m_GrenadeOrders.Count - 1; i >= 0; i--)
		{
			GrenadeRouteOrder order = m_GrenadeOrders[i];
			order.RouteWaypointIndex--;
			if (order.RouteWaypointIndex < 0)
				m_GrenadeOrders.RemoveAt(i);
			else
				m_GrenadeOrders[i] = order;
		}
	}

	private void ShiftFacingArrowSegmentsAfterWaypointRemoved(int _removedWaypointIndex)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		int remappedCount = 0;
#endif
		if (m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
			{
				FacingArrow before = m_ActiveFacingArrows[i];
				m_ActiveFacingArrows[i] = RemapFacingArrowSegmentForRemove(before, _removedWaypointIndex);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (before.RouteSegmentIndex != m_ActiveFacingArrows[i].RouteSegmentIndex)
					remappedCount++;
#endif
			}
		}

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			if (cmd.FacingArrows == null)
				continue;

			for (int arrowIndex = 0; arrowIndex < cmd.FacingArrows.Count; arrowIndex++)
			{
				FacingArrow before = cmd.FacingArrows[arrowIndex];
				cmd.FacingArrows[arrowIndex] = RemapFacingArrowSegmentForRemove(before, _removedWaypointIndex);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (before.RouteSegmentIndex != cmd.FacingArrows[arrowIndex].RouteSegmentIndex)
					remappedCount++;
#endif
			}

			m_CommandQueue[commandIndex] = cmd;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (FacingArrowDebug.VisualTopologyLoggingEnabled && remappedCount > 0)
		{
			FacingArrowDebug.Log(
				this,
				$"VISUAL_REMAP removedWaypoint={_removedWaypointIndex} remapped={remappedCount} " +
				$"anchorsPreserved=true wpAfter={Mathf.Max(0, m_Waypoints.Count - 1)}");
		}
#endif

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

	private bool IsFacingArrowSegmentBindingValid(int _segmentIndex)
	{
		return _segmentIndex >= 0 && _segmentIndex < m_Waypoints.Count;
	}

	private bool TryResolveFacingArrowAnchorForActivation(in FacingArrow _arrow, out Vector3 _anchorWorld)
	{
		_anchorWorld = default;
		if (!IsFacingArrowSegmentBindingValid(_arrow.RouteSegmentIndex))
			return false;

		if (_arrow.AnchorWorld != Vector3.zero)
		{
			_anchorWorld = _arrow.AnchorWorld;
			return true;
		}

		if (CollectRouteSegmentPolyline(_arrow.RouteSegmentIndex, m_RouteSegmentPolylineBuffer, _useLiveAgentPathForActiveLeg: false) &&
		    m_RouteSegmentPolylineBuffer.Count >= 2)
		{
			_anchorWorld = EvaluatePolylineAtT(m_RouteSegmentPolylineBuffer, _arrow.RouteSegmentT);
			return true;
		}

		bool useActiveSegmentStart = _arrow.RouteSegmentIndex == 0 && m_HasActiveDestination;
		if (TryGetRouteSegmentEndpoints(
			    _arrow.RouteSegmentIndex,
			    useActiveSegmentStart,
			    out Vector3 segmentStart,
			    out Vector3 segmentEnd))
		{
			_anchorWorld = Vector3.Lerp(segmentStart, segmentEnd, _arrow.RouteSegmentT);
			return true;
		}

		return false;
	}

	private void RebindFacingArrowsAfterRouteTopologyChange()
	{
		if (m_ActiveFacingArrows != null)
		{
			for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
			{
				FacingArrow arrow = m_ActiveFacingArrows[i];
				if (!IsFacingArrowSegmentBindingValid(arrow.RouteSegmentIndex))
					continue;

				RefreshFacingArrowRouteBinding(ref arrow, _useActiveSegmentStart: true);
				m_ActiveFacingArrows[i] = arrow;
			}
		}

		for (int commandIndex = 0; commandIndex < m_CommandQueue.Count; commandIndex++)
		{
			QueuedCommand cmd = m_CommandQueue[commandIndex];
			if (cmd.FacingArrows == null || cmd.FacingArrows.Count == 0)
				continue;

			for (int arrowIndex = 0; arrowIndex < cmd.FacingArrows.Count; arrowIndex++)
			{
				FacingArrow arrow = cmd.FacingArrows[arrowIndex];
				if (!IsFacingArrowSegmentBindingValid(arrow.RouteSegmentIndex))
					continue;

				RefreshFacingArrowRouteBinding(ref arrow, _useActiveSegmentStart: false);
				cmd.FacingArrows[arrowIndex] = arrow;
			}

			m_CommandQueue[commandIndex] = cmd;
		}

		MarkFacingArrowsDirty();
	}

	private void RefreshFacingArrowRouteBinding(ref FacingArrow _arrow, bool _useActiveSegmentStart)
	{
		Vector3 anchorWorld = _arrow.AnchorWorld;
		float previousSegmentT = _arrow.RouteSegmentT;
		if (CollectRouteSegmentPolyline(_arrow.RouteSegmentIndex, m_RouteSegmentPolylineBuffer, _useLiveAgentPathForActiveLeg: false) &&
		    m_RouteSegmentPolylineBuffer.Count >= 2)
		{
			if (anchorWorld != Vector3.zero)
				_arrow.RouteSegmentT = ComputeRouteSegmentTAlongPolyline(m_RouteSegmentPolylineBuffer, anchorWorld);
			else
				anchorWorld = EvaluatePolylineAtT(m_RouteSegmentPolylineBuffer, _arrow.RouteSegmentT);
		}
		else if (TryGetRouteSegmentEndpoints(
			         _arrow.RouteSegmentIndex,
			         _useActiveSegmentStart,
			         out Vector3 segmentStart,
			         out Vector3 segmentEnd))
		{
			if (anchorWorld != Vector3.zero)
				_arrow.RouteSegmentT = ComputeRouteSegmentT(anchorWorld, segmentStart, segmentEnd);
			else
				anchorWorld = Vector3.Lerp(segmentStart, segmentEnd, _arrow.RouteSegmentT);
		}

		_arrow.AnchorWorld = anchorWorld;

		if (_arrow.HasLookPoint && _arrow.LookPointWorld != Vector3.zero)
			_arrow.LookOffsetFromAnchor = _arrow.LookPointWorld - anchorWorld;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (FacingArrowDebug.VisualTopologyLoggingEnabled &&
		    anchorWorld != Vector3.zero &&
		    Mathf.Abs(previousSegmentT - _arrow.RouteSegmentT) > 0.01f)
		{
			FacingArrowDebug.Log(
				this,
				$"VISUAL_REBIND {FormatFacingArrowShort(_arrow)} anchor={FormatRoutePoint(anchorWorld)} " +
				$"t {previousSegmentT:F3}->{_arrow.RouteSegmentT:F3} activeSegStart={_useActiveSegmentStart} " +
				$"hasDest={m_HasActiveDestination} legStart={FormatRoutePoint(ResolveRouteSegmentStartForPolyline())}");
		}

		if (FacingArrowDebug.VisualDriftLoggingEnabled && anchorWorld != Vector3.zero)
			LogFacingArrowVisualAnchorDriftIfAny(_arrow, _useActiveSegmentStart);
#endif
	}

	private void TryStartNextQueuedCommand(float _groupStaggerDelaySeconds = 0f)
	{
		if (m_CommandQueue.Count == 0)
			return;
		if (m_IsWaitingAtRouteGate)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			LogRouteDebugEvent($"QUEUE_BLOCKED waitGate group={m_ActiveWaitGroup} {BuildRouteDebugSnapshot()}");
#endif
			return;
		}

		// Wait groups are evaluated on arrival (see TryAdvanceRouteQueue), not before departure.
		DequeueAndExecuteNextCommand(_groupStaggerDelaySeconds);
	}

	private void EnterWaitAfterArrival(int _waitGroup, Vector3 _iconWorldPosition)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		LogRouteDebugEvent($"WAIT_GATE_ARRIVAL group={_waitGroup} {BuildRouteDebugSnapshot()}");
#endif
		m_IsWaitingAtRouteGate = true;
		m_ActiveWaitGroup = NormalizeWaitGroup(_waitGroup);
		m_WaitGateWorldPosition = _iconWorldPosition;
		m_HasActiveDestination = false;
		m_ActiveDestinationWaitGroup = 0;
		m_ActiveDestinationWaitHasRouteBinding = false;
		ClearSmoothingArcState();
		ResetContinuousRouteLocomotionFlags();

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.isOnNavMesh)
		{
			agent.isStopped = true;
			agent.ResetPath();
		}

		MarkFacingArrowsDirty();
	}

	/// <summary>Pause on a wait binding without completing the active leg (segment-start waits).</summary>
	private void EnterWaitAtRouteBinding(int _waitGroup, Vector3 _worldPosition)
	{
		m_IsWaitingAtRouteGate = true;
		m_ActiveWaitGroup = NormalizeWaitGroup(_waitGroup);
		m_WaitGateWorldPosition = _worldPosition;
		m_ActiveDestinationWaitGroup = 0;
		m_ActiveDestinationWaitHasRouteBinding = false;
		ClearSmoothingArcState();
		ResetContinuousRouteLocomotionFlags();

		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent != null && agent.isOnNavMesh)
		{
			agent.isStopped = true;
			agent.ResetPath();
		}

		MarkFacingArrowsDirty();
	}

	private static bool IsWaitBindingAtSegmentEnd(float _segmentT)
	{
		return _segmentT >= 0.999f;
	}

	private static bool IsWaitBindingAtSegmentStart(float _segmentT)
	{
		return _segmentT <= 0.001f;
	}

	private bool TryEnterActiveDestinationSegmentStartWaitImmediate()
	{
		if (m_ActiveDestinationWaitGroup < 1 ||
		    !m_ActiveDestinationWaitHasRouteBinding ||
		    !IsWaitBindingAtSegmentStart(m_ActiveDestinationWaitSegmentT))
			return false;

		Vector3 waitPos = ResolveActiveDestinationWaitIconWorldPosition();
		if (!IsNearDestination(transform.position, waitPos, c_WaitBindingReachRadius))
			return false;

		EnterWaitAtRouteBinding(m_ActiveDestinationWaitGroup, waitPos);
		return true;
	}

	private void TryTriggerActiveDestinationSegmentStartWait()
	{
		if (ShouldSuppressRouteArrivalDuringEdit())
			return;
		if (!m_HasActiveDestination ||
		    m_IsWaitingAtRouteGate ||
		    m_IsExecutingGrenadeOrder ||
		    m_IsExecutingSmoothingArc ||
		    m_ActiveDestinationWaitGroup < 1 ||
		    !m_ActiveDestinationWaitHasRouteBinding ||
		    !IsWaitBindingAtSegmentStart(m_ActiveDestinationWaitSegmentT))
			return;

		if (m_DestinationSetTime >= 0f && Time.time - m_DestinationSetTime < 0.05f)
			return;

		Vector3 waitPos = ResolveActiveDestinationWaitIconWorldPosition();
		if (!IsNearDestination(transform.position, waitPos, c_WaitBindingReachRadius))
			return;

		EnterWaitAtRouteBinding(m_ActiveDestinationWaitGroup, waitPos);
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
		TryStartNextQueuedCommand();
	}

	private bool IsAtFirstWaypoint()
	{
		if (m_Waypoints.Count == 0)
			return true;

		float dx = transform.position.x - m_Waypoints[0].x;
		float dz = transform.position.z - m_Waypoints[0].z;
		return dx * dx + dz * dz < 0.25f;
	}

	private void BuildNavMeshRoutePoints(
		List<Vector3> _output,
		bool includeUnitStart,
		Vector3? previewDestination,
		bool useLiveAgentPathForActiveLeg)
	{
		_output.Clear();
		if (m_Waypoints.Count == 0 && !previewDestination.HasValue)
			return;

		BuildRawRoutePoints(m_RawRoutePoints, includeUnitStart, previewDestination);
		if (m_RawRoutePoints.Count < 2)
		{
			if (m_RawRoutePoints.Count == 1)
				_output.Add(m_RawRoutePoints[0]);
			return;
		}

		for (int i = 0; i < m_RawRoutePoints.Count - 1; i++)
		{
			Vector3 segStart = m_RawRoutePoints[i];
			Vector3 segEnd = m_RawRoutePoints[i + 1];
			bool isActiveLeg = i == 0 && includeUnitStart && m_HasActiveDestination && useLiveAgentPathForActiveLeg;

			if (isActiveLeg && TryAppendActiveAgentPathPolyline(_output))
				continue;

			if (!TryAppendCalculatedNavMeshPath(segStart, segEnd, _output))
				AppendStraightSegmentFallback(segStart, segEnd, _output);
		}
	}

	private bool TryAppendActiveAgentPathPolyline(List<Vector3> _output)
	{
		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent == null || !agent.isOnNavMesh || agent.pathPending || !agent.hasPath)
			return false;
		if (!m_HasActiveDestination || m_Waypoints.Count == 0)
			return false;

		Vector3 expectedDestination = m_Waypoints[0];
		Vector3 agentDestination = agent.destination;
		float destDx = agentDestination.x - expectedDestination.x;
		float destDz = agentDestination.z - expectedDestination.z;
		if (destDx * destDx + destDz * destDz > 0.36f)
			return false;

		NavMeshPath path = agent.path;
		if (path.status != NavMeshPathStatus.PathComplete && path.status != NavMeshPathStatus.PathPartial)
			return false;

		Vector3[] corners = path.corners;
		if (corners == null || corners.Length < 2)
			return false;

		_output.Add(transform.position);
		int startCorner = FindFirstPathCornerAheadOfUnit(corners, transform.position, expectedDestination);
		for (int i = startCorner; i < corners.Length; i++)
		{
			if (_output.Count > 0)
			{
				Vector3 last = _output[_output.Count - 1];
				Vector3 next = corners[i];
				if ((next - last).sqrMagnitude < 0.0001f)
					continue;
			}

			_output.Add(corners[i]);
		}

		return _output.Count >= 2;
	}

	private static int FindFirstPathCornerAheadOfUnit(Vector3[] _corners, Vector3 _unitWorld, Vector3 _destinationWorld)
	{
		if (_corners == null || _corners.Length == 0)
			return 0;

		Vector3 flatUnit = FlattenToGround(_unitWorld);
		Vector3 flatDest = FlattenToGround(_destinationWorld);
		Vector3 toDest = flatDest - flatUnit;
		if (toDest.sqrMagnitude < 0.0001f)
			return _corners.Length - 1;

		toDest.Normalize();
		for (int i = 0; i < _corners.Length; i++)
		{
			Vector3 toCorner = FlattenToGround(_corners[i]) - flatUnit;
			if (toCorner.sqrMagnitude < 0.04f)
				continue;

			if (Vector3.Dot(toCorner, toDest) >= -0.1f)
				return i;
		}

		return _corners.Length - 1;
	}

	private bool TryAppendCalculatedNavMeshPath(Vector3 _start, Vector3 _end, List<Vector3> _output)
	{
		if (!TrySampleNavMeshPoint(_start, out Vector3 sampledStart))
			sampledStart = _start;
		if (!TrySampleNavMeshPoint(_end, out Vector3 sampledEnd))
			sampledEnd = _end;

		if (m_ReusableNavMeshPath == null)
			m_ReusableNavMeshPath = new NavMeshPath();

		if (!NavMesh.CalculatePath(sampledStart, sampledEnd, NavMesh.AllAreas, m_ReusableNavMeshPath))
			return false;
		if (m_ReusableNavMeshPath.status != NavMeshPathStatus.PathComplete)
			return false;

		Vector3[] corners = m_ReusableNavMeshPath.corners;
		if (corners == null || corners.Length < 2)
			return false;

		AppendPathCornersDeduped(_output, corners);
		return true;
	}

	private static void AppendPathCornersDeduped(List<Vector3> _output, Vector3[] _corners)
	{
		for (int i = 0; i < _corners.Length; i++)
		{
			if (_output.Count > 0 && (_output[_output.Count - 1] - _corners[i]).sqrMagnitude < 0.0001f)
				continue;

			_output.Add(_corners[i]);
		}
	}

	private static void AppendStraightSegmentFallback(Vector3 _start, Vector3 _end, List<Vector3> _output)
	{
		if (_output.Count == 0 || (_output[_output.Count - 1] - _start).sqrMagnitude > 0.0001f)
			_output.Add(_start);
		if ((_output[_output.Count - 1] - _end).sqrMagnitude > 0.0001f)
			_output.Add(_end);
	}

	private float GetNavMeshSegmentLengthOrPlanar(Vector3 _start, Vector3 _end)
	{
		m_RouteSegmentPolylineBuffer.Clear();
		if (TryAppendCalculatedNavMeshPath(_start, _end, m_RouteSegmentPolylineBuffer) &&
		    m_RouteSegmentPolylineBuffer.Count >= 2)
			return ComputePolylineLength(m_RouteSegmentPolylineBuffer);

		return PlanarDistance(_start, _end);
	}

	private static float ComputePolylineLength(IReadOnlyList<Vector3> _polyline)
	{
		float total = 0f;
		for (int i = 1; i < _polyline.Count; i++)
			total += PlanarDistance(_polyline[i - 1], _polyline[i]);
		return total;
	}

	private static float ComputeRouteSegmentTAlongPolyline(IReadOnlyList<Vector3> _polyline, Vector3 _point)
	{
		if (_polyline == null || _polyline.Count < 2)
			return 0f;

		float totalLength = ComputePolylineLength(_polyline);
		if (totalLength < 0.001f)
			return 0f;

		float bestDistSqr = float.MaxValue;
		float bestAccumulated = 0f;
		float accumulated = 0f;

		for (int i = 1; i < _polyline.Count; i++)
		{
			Vector3 segmentStart = _polyline[i - 1];
			Vector3 segmentEnd = _polyline[i];
			float segmentLength = PlanarDistance(segmentStart, segmentEnd);
			Vector3 closest = ClosestPointOnLineSegment3D(_point, segmentStart, segmentEnd);
			Vector3 planarClosest = FlattenToGround(closest);
			Vector3 planarPoint = FlattenToGround(_point);
			float distSqr = (planarPoint - planarClosest).sqrMagnitude;
			if (distSqr < bestDistSqr)
			{
				bestDistSqr = distSqr;
				float segmentT = segmentLength > 0.001f
					? PlanarDistance(segmentStart, closest) / segmentLength
					: 0f;
				bestAccumulated = accumulated + segmentT * segmentLength;
			}

			accumulated += segmentLength;
		}

		return Mathf.Clamp01(bestAccumulated / totalLength);
	}

	private static Vector3 EvaluatePolylineAtT(IReadOnlyList<Vector3> _polyline, float _t)
	{
		if (_polyline == null || _polyline.Count == 0)
			return Vector3.zero;
		if (_polyline.Count == 1)
			return _polyline[0];

		float totalLength = ComputePolylineLength(_polyline);
		if (totalLength < 0.001f)
			return _polyline[0];

		float targetDistance = Mathf.Clamp01(_t) * totalLength;
		float accumulated = 0f;
		for (int i = 1; i < _polyline.Count; i++)
		{
			Vector3 segmentStart = _polyline[i - 1];
			Vector3 segmentEnd = _polyline[i];
			float segmentLength = PlanarDistance(segmentStart, segmentEnd);
			if (accumulated + segmentLength >= targetDistance)
			{
				float segmentT = segmentLength > 0.001f
					? (targetDistance - accumulated) / segmentLength
					: 0f;
				return Vector3.Lerp(segmentStart, segmentEnd, segmentT);
			}

			accumulated += segmentLength;
		}

		return _polyline[_polyline.Count - 1];
	}

	private static Vector3 ClosestPointOnLineSegment3D(Vector3 _point, Vector3 _start, Vector3 _end)
	{
		Vector3 segment = _end - _start;
		float segmentSqr = segment.sqrMagnitude;
		if (segmentSqr < 1e-9f)
			return _start;

		float t = Mathf.Clamp01(Vector3.Dot(_point - _start, segment) / segmentSqr);
		return _start + segment * t;
	}

	private static float DistPointToSegmentSqrScreen(
		Vector2 _point,
		Vector2 _segmentStart,
		Vector2 _segmentEnd,
		out Vector2 _closest,
		out float _t)
	{
		Vector2 segment = _segmentEnd - _segmentStart;
		float segmentLenSqr = segment.sqrMagnitude;
		if (segmentLenSqr < 0.0001f)
		{
			_closest = _segmentStart;
			_t = 0f;
			return (_point - _segmentStart).sqrMagnitude;
		}

		_t = Mathf.Clamp01(Vector2.Dot(_point - _segmentStart, segment) / segmentLenSqr);
		_closest = _segmentStart + segment * _t;
		return (_point - _closest).sqrMagnitude;
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

		private int m_CentroidFrame = -1;
		private Vector3 m_CentroidWorld;

		public Vector3 GetCentroidWorld()
		{
			int frame = Time.frameCount;
			if (m_CentroidFrame == frame)
				return m_CentroidWorld;

			m_CentroidFrame = frame;
			Vector3 sum = Vector3.zero;
			int count = 0;
			for (int i = 0; i < Members.Count; i++)
			{
				RtsUnitMember member = Members[i];
				if (member == null)
					continue;

				Vector3 pos = member.transform.position;
				pos.y = 0f;
				sum += pos;
				count++;
			}

			m_CentroidWorld = count > 0 ? sum / count : Vector3.zero;
			return m_CentroidWorld;
		}
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
		string arrowInfo = m_ActiveFacingArrows != null && m_ActiveFacingArrows.Count > 0
			? $" pendingArrows={m_ActiveFacingArrows.Count} arrowPhase={m_ArrowPriorityPhase}"
			: $" arrowPhase={m_ArrowPriorityPhase}";

		return
			$"wp={m_Waypoints.Count} q={m_CommandQueue.Count} active={m_HasActiveDestination} " +
			$"gate={m_IsWaitingAtRouteGate} wg={m_ActiveWaitGroup} arriveWait={m_ActiveDestinationWaitGroup} " +
			$"intermediate={IsIntermediateRouteSegment()} suppressEarly={suppressEarly} " +
			$"{syncInfo}{arrowInfo} rem={remaining:F2} spd={speed:F2} hasPath={hasPath} stopped={isStopped} pending={pathPending} " +
			$"arc={m_IsExecutingSmoothingArc} rotate={m_IsRotatingToFacing} wp0={wp0} wp1={wp1}";
	}

	private void LogPendingFacingArrowsThrottled()
	{
		if (!FacingArrowDebug.PeriodicPendingLoggingEnabled ||
		    m_ActiveFacingArrows == null ||
		    m_ActiveFacingArrows.Count == 0)
			return;

		Vector3 unitPos = transform.position;
		string details = BuildPendingFacingArrowsDebugDetails(unitPos);
		FacingArrowDebug.LogThrottled(
			this,
			ref m_FacingArrowDebugNextPendingLogTime,
			$"PENDING phase={m_ArrowPriorityPhase} {details}");
	}

	private string BuildPendingFacingArrowsDebugDetails(Vector3 _unitPos)
	{
		var parts = new List<string>(m_ActiveFacingArrows.Count);
		for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
		{
			FacingArrow arrow = m_ActiveFacingArrows[i];
			if (arrow.Mode == FacingArrowMode.TurnOnArrival)
			{
				parts.Add($"#{i} arrival seg={arrow.RouteSegmentIndex} t={arrow.RouteSegmentT:F3}");
				continue;
			}

			bool reached = EvaluateFacingArrowReach(
				arrow,
				_unitPos,
				out Vector3 anchor,
				out float dist,
				out float unitT,
				out bool hasUnitT,
				out string reachReason);
			parts.Add(
				$"#{i} {FormatFacingArrowShort(arrow)} reached={reached} {reachReason} " +
				$"anchor={FormatRoutePoint(anchor)}");
		}

		return string.Join(" | ", parts);
	}

	private void LogMissedFacingArrowsIfAny()
	{
		if (!FacingArrowDebug.LoggingEnabled ||
		    !FacingArrowDebug.MissedArrowLoggingEnabled ||
		    m_ActiveFacingArrows == null ||
		    m_ActiveFacingArrows.Count == 0)
			return;

		Vector3 unitPos = transform.position;
		float slack = FacingArrowDebug.MissedArrowRouteTSlack;

		for (int i = 0; i < m_ActiveFacingArrows.Count; i++)
		{
			FacingArrow arrow = m_ActiveFacingArrows[i];
			if (arrow.Mode == FacingArrowMode.TurnOnArrival)
				continue;

			if (!TryGetRouteSegmentProgressForUnit(arrow.RouteSegmentIndex, unitPos, out float unitT))
			{
				LogMissedFacingArrowOnce(
					arrow,
					i,
					$"MISSED #{i} {FormatFacingArrowShort(arrow)} reason=noUnitSegmentProgress " +
					$"arrowSeg={arrow.RouteSegmentIndex} arrowT={arrow.RouteSegmentT:F3}");
				continue;
			}

			if (unitT <= arrow.RouteSegmentT + slack)
				continue;

			if (EvaluateFacingArrowReach(
				    arrow,
				    unitPos,
				    out Vector3 anchor,
				    out float dist,
				    out _,
				    out _,
				    out string reachReason))
				continue;

			LogMissedFacingArrowOnce(
				arrow,
				i,
				$"MISSED #{i} {FormatFacingArrowShort(arrow)} unitT={unitT:F3} arrowT={arrow.RouteSegmentT:F3} " +
				$"dist={dist:F2}/{c_FacingArrowActivationReachRadius:F2} anchor={FormatRoutePoint(anchor)} " +
				$"{reachReason} phase={m_ArrowPriorityPhase}");
		}
	}

	private void LogMissedFacingArrowOnce(in FacingArrow _arrow, int _listIndex, string _message)
	{
		int key = BuildFacingArrowDebugKey(_arrow, _listIndex);
		if (!m_FacingArrowMissReportedKeys.Add(key))
			return;

		FacingArrowDebug.Log(this, _message);
	}

	private static int BuildFacingArrowDebugKey(in FacingArrow _arrow, int _listIndex)
	{
		return _listIndex * 1000000
		       + (_arrow.RouteSegmentIndex + 1) * 10000
		       + Mathf.RoundToInt(Mathf.Clamp01(_arrow.RouteSegmentT) * 9999f)
		       + (int)_arrow.Mode * 100000;
	}

	private static int BuildFacingArrowVisualDriftKey(in FacingArrow _arrow)
	{
		return (_arrow.RouteSegmentIndex + 1) * 100000
		       + (int)_arrow.Mode * 10000
		       + Mathf.RoundToInt(_arrow.AnchorWorld.x * 10f)
		       + Mathf.RoundToInt(_arrow.AnchorWorld.z * 10f) * 1000;
	}

	private static string FormatFacingArrowShort(in FacingArrow _arrow)
	{
		string mode = _arrow.Mode switch
		{
			FacingArrowMode.HoldToEnd => "blue",
			FacingArrowMode.LookAtPoint => "green",
			FacingArrowMode.TurnOnArrival => "arrival",
			_ => "yellow",
		};
		return $"{mode} seg={_arrow.RouteSegmentIndex} t={_arrow.RouteSegmentT:F3} angle={_arrow.Angle:F0}";
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
