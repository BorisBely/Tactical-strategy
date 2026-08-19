using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Periodic vision detection orchestrator: cheap range/FOV → optional LOS/hit-zones → <see cref="VisionObservation"/> frame → Perception.
/// G8 LOD only changes when expensive work runs. Does not choose combat targets.
/// TargetSelector reacts to Perception frames independently.
///
/// Responsibility map:
/// - Scan scheduling / immediate rescan → UnitVision
/// - Candidate collection → VisionCandidateProvider
/// - Distance / FOV → VisionGeometry
/// - LOS / aim samples → VisibilityChecker
/// - Observation origin → UnitObservationSource
/// - Build VisionObservation → UnitVision evaluator
/// - Perception update → UnitPerception.ApplyVisionFrame
/// - Target selection → TargetSelector (not called from here)
/// - Engageable checks → TargetEngageability
/// - Facing / bore / ready FOV widen / gizmos → temporary UnitVision (not Combat AI)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitTeam))]
[RequireComponent(typeof(UnitObservationSource))]
[RequireComponent(typeof(UnitPerception))]
public sealed class UnitVision : MonoBehaviour
{
	#region Constants
	private const int c_RaycastHitBuffer = 16;
	private const int c_AimCandidateCapacity = 32;
	#endregion

	#region Private Fields
	[SerializeField] private UnitVisionRegistry m_Registry;
	[SerializeField] private ShootingRangeTargetRegistry m_RangeTargetRegistry;
	[SerializeField] private UnitTeam m_Team;
	[Tooltip("Legacy fallback. Зрение и прицел используют UnitBodyHitZone; если пусто — ищутся хитбоксы на дочерних объектах.")]
	[SerializeField] private Collider m_BodyCollider;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitObservationSource m_ObservationSource;
	[SerializeField] private UnitPerception m_Perception;
	[SerializeField] private TargetSelector m_TargetSelector;

	[Header("Зрение")]
	[SerializeField, Min(0.5f)] private float m_VisionRange = 18f;
	[SerializeField, Range(1f, 179f)] private float m_FieldOfViewDegrees = 120f;
	[Tooltip("Пока в прошлом кадре уже была цель, к половине FOV добавляется этот угол — реже теряем цель на краю конуса.")]
	[SerializeField, Range(0f, 30f)] private float m_TrackingHalfFovExtraDegrees = 15f;
	[SerializeField, Min(0f)] private float m_EyeHeight = 1.6f;

	[Header("Опрос")]
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMin = 0.25f;
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMax = 0.45f;
	[Tooltip("При повороте прицела сильнее этого угла (град.) — внеочередной скан.")]
	[SerializeField, Range(0.5f, 15f)] private float m_ImmediateRescanAngleDegrees = 2.5f;

	[Header("G8 LOD / cheap-before-expensive")]
	[SerializeField, Range(0f, 20f)] private float m_CoarseFovPadDegrees = 8f;
	[SerializeField, Min(0f)] private float m_CoarseRangePadMeters = 4f;
	[SerializeField, Min(0.05f)] private float m_DetailQueueDelaySeconds = 0.35f;
	[SerializeField, Min(0.05f)] private float m_DiscoverIntervalSeconds = 0.5f;
	[SerializeField, Min(0.05f)] private float m_MembershipIntervalSeconds = 1.5f;
	[SerializeField, Min(0.02f)] private float m_LosCacheTtlSeconds = 0.3f;
	[SerializeField, Min(0.01f)] private float m_LosCacheMoveEpsilonMeters = 0.35f;

	[Header("Физика")]
	[SerializeField] private LayerMask m_LayerMask = ~0;
	[SerializeField] private QueryTriggerInteraction m_QueryTriggerInteraction = QueryTriggerInteraction.Ignore;

	[Header("Ось конуса (FOV)")]
	[Tooltip("Если задано — горизонтальная ось «куда смотрит» для проверки угла цели (иначе торс humanoid или корень).")]
	[SerializeField] private Transform m_ViewForwardOverride;
	[Tooltip("Брать горизонталь с UpperChest/Chest/Spine. У части ригов ось кости ≠ «вперёд» юнита — при странном секторе выключите или задайте View Forward Override.")]
	[SerializeField] private bool m_UseHumanoidTorsoForward = false;
	[Tooltip("Сглаживание направления конуса и проверки FOV (сек): торс дёргается от анимации каждый кадр без этого.")]
	[SerializeField, Min(0f)] private float m_VisionForwardSmoothTime = 0.07f;
	[Tooltip("When weapon is equipped and in low ready, the root often doesn't match the gaze axis — don't let half‑FOV become narrower than this threshold (degrees from axis).")]
	[SerializeField, Range(1f, 89f)] private float m_MinHalfFovDegreesWhenWeaponNotReady = 70f;

	[Header("Sight (weapon in high ready)")]
	[Tooltip("Редкий оверрайд на юните. Обычно прицел задаётся на префабе в EquippedWeapon → Sight Pivot.")]
	[SerializeField] private Transform m_SightPivotOverride;
	[Tooltip("Если Override пуст и на EquippedWeapon нет Sight Pivot: искать под визуалом оружия дочерний Transform с этим именем.")]
	[SerializeField] private string m_SightPivotChildName = "";

	[Header("Transition to high ready")]
	[Tooltip("TEMP OFF: when entering high ready, smoothly rotate the root so the barrel points where the unit was looking before the transition.")]
	[SerializeField] private bool m_PreserveBoreForwardDuringReadyTransition = false;
	[SerializeField, Min(0.01f)] private float m_ReadyBoreRootTurnDuration = 0.22f;
	[SerializeField, Range(0f, 120f)] private float m_MaxReadyBoreRootTurnDegrees = 90f;
	[SerializeField, Range(0f, 10f)] private float m_MinReadyBoreRootTurnDegrees = 0.5f;

	[Header("Отладка")]
	[SerializeField] private bool m_DrawVisionGizmos = true;
	[SerializeField] private Color m_GizmoRayHitColor = new Color(1f, 0.3f, 0.1f, 0.9f);
	[SerializeField] private Color m_GizmoRayMissColor = new Color(0.4f, 0.4f, 0.9f, 0.6f);

	[Tooltip("Scene Gizmos + Game view: направление взгляда юнита (горизонталь оси зрения).")]
	[SerializeField] private bool m_DrawEyeLookDebugRay;
	[SerializeField, Min(0.1f)] private float m_EyeLookDebugRayLength = 5f;
	[SerializeField] private Color m_EyeLookDebugRayColor = new Color(1f, 0.35f, 0.9f, 1f);
	[Tooltip("Log to Console on high‑ready / low‑ready change: root angles, vision axis and sight — for debugging gaze shift.")]
	[SerializeField] private bool m_LogReadyForwardShift;

	private readonly List<UnitVision> m_OpponentBuffer = new List<UnitVision>(128);
	private readonly List<VisionCandidateProvider.Candidate> m_CandidateScratch =
		new List<VisionCandidateProvider.Candidate>(128);
	private readonly List<VisionObservation> m_ObservationScratch = new List<VisionObservation>(32);
	private readonly List<UnitBodyHitZoneVisionUtility.VisionAimCandidate> m_AimCandidateScratch =
		new List<UnitBodyHitZoneVisionUtility.VisionAimCandidate>(c_AimCandidateCapacity);
	private UnitBodyHitZone[] m_BodyHitZones = Array.Empty<UnitBodyHitZone>();
	private readonly List<(Vector3 from, Vector3 to, bool hitTarget)> m_DebugRays = new List<(Vector3, Vector3, bool)>(256);

	private RaycastHit[] m_Hits;
	private float m_NextScanTime;
	private float m_NextImmediateRescanAllowedTime;
	private Vector3 m_SmoothedVisionForwardXZ;
	private Vector3 m_LastScanForwardXZ;
	private Vector3 m_ReadyTransitionDesiredBoreForwardXZ;
	private bool m_HasReadyTransitionDesiredBoreForwardXZ;
	private RtsUnitMember m_CachedRtsMember;
	private UnitVehicleSeatPoseController m_CachedSeatPose;

	private VisionCandidateProvider m_CandidateProvider;
	private VisibilityChecker m_VisibilityChecker;
	private DetectionProcessor m_DetectionProcessor;
	private readonly VisionScanStats m_ScanStats = new VisionScanStats();
	private readonly VisionLosCache m_LosCache = new VisionLosCache();
	private readonly Dictionary<Transform, float> m_DetailDue = new Dictionary<Transform, float>(32);
	private VisionScanTier m_CurrentScanTier = VisionScanTier.RangeFov;
	private bool m_ForceDetailThisScan;
	private bool m_BypassLosCacheThisScan;
	private float m_LastDetailScanTime = -999f;
	private float m_LastMembershipScanTime = -999f;
	#endregion

	#region Body / Hit Zones
	public Collider BodyCollider => ResolveBodyCollider();

	public IReadOnlyList<UnitBodyHitZone> BodyHitZones => m_BodyHitZones;

	public UnitBodyHitZone[] GetBodyHitZonesArray() => m_BodyHitZones;

	private Transform SelectedCombatTarget =>
		m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;

	public VisionScanStats ScanStats => m_ScanStats;
	public string DebugLastLosBlocker => m_VisibilityChecker != null ? m_VisibilityChecker.LastLosBlocker : null;
	public VisionScanTier CurrentScanTier => m_CurrentScanTier;
	public float VisionRange => m_VisionRange;
	#endregion

	#region Engage Facing / Scan API
	/// <summary>
	/// Горизонтальный вектор «на цель» при engage: при оружии в ready только позиция прицела,
	/// иначе корень юнита. Если для ready-оружия прицел не найден, возвращает <c>false</c>.
	/// </summary>
	public bool TryGetEngageFacingOriginWorld(out Vector3 _origin)
	{
		if (IsWeaponReadyForSightCone())
		{
			if (m_ObservationSource == null || !m_ObservationSource.TryGetSightTransform(out Transform sight))
			{
				_origin = default;
				return false;
			}

			_origin = sight.position;
			return true;
		}

		_origin = transform.position;
		return true;
	}

	public void SetVisionRange(float _range)
	{
		m_VisionRange = Mathf.Max(0.5f, _range);
		ConfigureHelpers();
	}

	/// <summary>
	/// Defer the next detection scan to the normal interval (after selection clear). Not a combat-target API.
	/// </summary>
	public void DeferNextScan()
	{
		if (!isActiveAndEnabled)
			return;
		ScheduleNextScan(0f);
	}

	/// <summary>
	/// Promote this observer to Detail (T3) and run the same cheap→expensive pipeline.
	/// Does not scan every observer in the world.
	/// </summary>
	public void RequestImmediateScan()
	{
		if (!isActiveAndEnabled || m_Registry == null || m_Team == null)
			return;

		m_ForceDetailThisScan = true;
		m_BypassLosCacheThisScan = true;
		RunScheduledScan(true);
	}

	public bool TryFindTargetInDirection(float _worldAngle, float _halfAngleDegrees, out Transform _bestTarget)
	{
		_bestTarget = null;
		if (!isActiveAndEnabled || m_Registry == null || m_Team == null)
			return false;

		Vector3 dirXZ = new Vector3(
			Mathf.Sin(_worldAngle * Mathf.Deg2Rad),
			0f,
			Mathf.Cos(_worldAngle * Mathf.Deg2Rad));

		Vector3 origin = GetVisionConeOriginWorld();
		float rangeSq = m_VisionRange * m_VisionRange;
		float bestDistSq = float.MaxValue;

		m_CandidateProvider.CollectOpponentsRaw(m_OpponentBuffer);

		for (int i = 0; i < m_OpponentBuffer.Count; i++)
		{
			UnitVision other = m_OpponentBuffer[i];
			if (other == null || other == this || !other.isActiveAndEnabled)
				continue;
			if (!UnitConsciousness.IsTargetableTarget(other.transform))
				continue;
			if (other.TryGetComponent(out DamageableTarget damageable) && !damageable.IsAlive)
				continue;
			if (m_TargetSelector != null && m_TargetSelector.IsLineOfFireSuppressed(other.transform))
				continue;

			Vector3 targetCenter = GetCandidateRoughCenter(other.transform);
			Vector3 toTarget = targetCenter - origin;
			toTarget.y = 0f;
			float distSq = toTarget.sqrMagnitude;
			if (distSq > rangeSq || distSq < 0.0001f)
				continue;

			float angleToTarget = Vector3.Angle(dirXZ, toTarget.normalized);
			if (angleToTarget > _halfAngleDegrees)
				continue;

			if (distSq < bestDistSq)
			{
				bestDistSq = distSq;
				_bestTarget = other.transform;
			}
		}

		return _bestTarget != null;
	}

	/// <summary>Called from <see cref="UnitWeaponReadyHandsLayer"/> on high‑ready / low‑ready change.</summary>
	public void NotifyWeaponReadyChanged(bool _ready)
	{
		if (!isActiveAndEnabled)
			return;

		m_HasReadyTransitionDesiredBoreForwardXZ = false;
		if (m_ObservationSource != null)
			m_ObservationSource.InvalidateSightCache();
		RequestImmediateScan();
		if (m_LogReadyForwardShift)
			LogReadyForwardShift(_ready);
	}

	public void RefreshBodyHitZones()
	{
		UnitBodyHitZone[] all = GetComponentsInChildren<UnitBodyHitZone>(true);
		int count = 0;
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].gameObject.activeInHierarchy)
				count++;
		}

		m_BodyHitZones = count == all.Length ? all : new UnitBodyHitZone[count];
		if (count != all.Length)
		{
			int w = 0;
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i] != null && all[i].gameObject.activeInHierarchy)
					m_BodyHitZones[w++] = all[i];
			}
		}
	}

	/// <summary>
	/// Горизонтальный forward для разворота на цель: корень/торс без цели; при ready и выбранной цели — ось ствола.
	/// </summary>
	public bool TryGetEngageFacingForwardXZ(out Vector3 _forwardXZ)
	{
		if (IsWeaponReadyForSightCone())
		{
			if (m_ObservationSource == null || !m_ObservationSource.TryGetSightTransform(out _))
			{
				_forwardXZ = default;
				return false;
			}

			if (SelectedCombatTarget != null && TryGetWeaponBoreForwardXZ(out Vector3 boreFwd))
			{
				_forwardXZ = boreFwd;
				return true;
			}
		}

		_forwardXZ = GetVisionForwardXZForGameplay();
		return _forwardXZ.sqrMagnitude > 1e-6f;
	}

	/// <summary>
	/// Старый совместимый API: если источник не найден, возвращает корень юнита.
	/// </summary>
	public Vector3 GetEngageFacingOriginWorld()
	{
		if (TryGetEngageFacingOriginWorld(out Vector3 origin))
			return origin;

		if (m_ObservationSource != null && m_ObservationSource.TryGetSightTransform(out Transform sight))
			return sight.position;
		return transform.position;
	}

	public float ResolveHalfFovDegreesForScan()
	{
		return VisionGeometry.ResolveHalfFovDegrees(
			m_FieldOfViewDegrees,
			ShouldWidenFovForWeaponNotReady(),
			m_MinHalfFovDegreesWhenWeaponNotReady,
			SelectedCombatTarget != null,
			m_TrackingHalfFovExtraDegrees);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Hits = new RaycastHit[c_RaycastHitBuffer];
		EnsurePipelineComponents();
		ResolveRegistryIfNeeded();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		RefreshBodyHitZones();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();
		if (m_CachedRtsMember == null)
			m_CachedRtsMember = GetComponent<RtsUnitMember>();

		m_CandidateProvider = new VisionCandidateProvider(this);
		m_VisibilityChecker = new VisibilityChecker(transform, m_Hits, m_AimCandidateScratch, m_DebugRays);
		m_DetectionProcessor = GetComponent<DetectionProcessor>();
		SyncObservationSourceConfig();
		ConfigureHelpers();
	}

	private void OnEnable()
	{
		EnsurePipelineComponents();
		ResolveRegistryIfNeeded();
		ConfigureHelpers();
		m_SmoothedVisionForwardXZ = Vector3.zero;
		if (m_ObservationSource != null)
			m_ObservationSource.InvalidateSightCache();
		ScheduleNextScan(0f);
		if (m_Registry != null)
			m_Registry.Register(this);
	}

	private void OnDisable()
	{
		if (m_Registry != null)
			m_Registry.Unregister(this);
	}

	private void Update()
	{
		if (m_Registry == null || m_Team == null)
			return;

		if (Application.isPlaying)
			UpdateSmoothedVisionForward();

		bool due = Time.time >= m_NextScanTime;
		bool aimMotion = false;
		if (!due)
			aimMotion = ShouldImmediateRescanForAimMotion();
		if (!due && !aimMotion && !m_ForceDetailThisScan)
			return;

		bool immediate = m_ForceDetailThisScan || aimMotion;
		if (immediate)
			m_BypassLosCacheThisScan = true;
		RunScheduledScan(immediate);
	}

	private void LateUpdate()
	{
		if (!Application.isPlaying || !m_DrawEyeLookDebugRay)
			return;

		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 fwdXz = GetVisionForwardXZForGameplay();
		Vector3 dir = new Vector3(fwdXz.x, 0f, fwdXz.z);
		if (dir.sqrMagnitude < 1e-6f)
			return;
		dir.Normalize();
		Debug.DrawRay(origin, dir * m_EyeLookDebugRayLength, m_EyeLookDebugRayColor);
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		SyncObservationSourceConfig();
	}
#endif
	#endregion

	#region Private Methods
	private void EnsurePipelineComponents()
	{
		if (m_ObservationSource == null)
			m_ObservationSource = GetComponent<UnitObservationSource>() ?? gameObject.AddComponent<UnitObservationSource>();
		if (m_Perception == null)
			m_Perception = GetComponent<UnitPerception>() ?? gameObject.AddComponent<UnitPerception>();
		if (GetComponent<DetectionProcessor>() == null)
			gameObject.AddComponent<DetectionProcessor>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>() ?? gameObject.AddComponent<TargetSelector>();
		if (GetComponent<EngagementDecisionController>() == null)
			gameObject.AddComponent<EngagementDecisionController>();
	}

	private void SyncObservationSourceConfig()
	{
		if (m_ObservationSource == null)
			m_ObservationSource = GetComponent<UnitObservationSource>();
		if (m_ObservationSource == null)
			return;

		m_ObservationSource.ApplyConfig(m_EyeHeight, m_SightPivotOverride, m_SightPivotChildName, m_Equipment, m_ReadyHands);
	}

	private void ConfigureHelpers()
	{
		if (m_CandidateProvider != null)
			m_CandidateProvider.Bind(m_Team, m_Registry, m_RangeTargetRegistry);

		if (m_VisibilityChecker != null)
		{
			m_VisibilityChecker.Configure(m_LayerMask, m_QueryTriggerInteraction, m_VisionRange, m_DrawVisionGizmos);
			m_VisibilityChecker.BindStats(m_ScanStats);
		}
	}

	private void ResolveRegistryIfNeeded()
	{
		if (m_Registry == null)
		{
#if UNITY_2023_1_OR_NEWER
			m_Registry = FindAnyObjectByType<UnitVisionRegistry>(FindObjectsInactive.Exclude);
#else
			m_Registry = FindObjectOfType<UnitVisionRegistry>();
#endif
		}

		if (m_RangeTargetRegistry == null)
		{
#if UNITY_2023_1_OR_NEWER
			m_RangeTargetRegistry = FindAnyObjectByType<ShootingRangeTargetRegistry>(FindObjectsInactive.Exclude);
#else
			m_RangeTargetRegistry = FindObjectOfType<ShootingRangeTargetRegistry>();
#endif
		}

		if (m_CandidateProvider != null)
			m_CandidateProvider.Bind(m_Team, m_Registry, m_RangeTargetRegistry);
	}

	private void ScheduleNextScan(float _delayOffset)
	{
		ScheduleNextScan(_delayOffset, m_CurrentScanTier);
	}

	private void ScheduleNextScan(float _delayOffset, VisionScanTier _tier)
	{
		float min = ResolveScanIntervalMinSeconds();
		float max = ResolveScanIntervalMaxSeconds();
		float scale = VisionLodMath.IntervalScale(_tier);
		m_NextScanTime = Time.time + _delayOffset + UnityEngine.Random.Range(min, max) * scale;
	}

	private float ResolveScanIntervalMinSeconds()
	{
		if (m_CombatStats != null)
			return m_CombatStats.GetVisionScanIntervalMinSeconds();

		return Mathf.Min(m_ScanIntervalMin, m_ScanIntervalMax);
	}

	private float ResolveScanIntervalMaxSeconds()
	{
		if (m_CombatStats != null)
			return m_CombatStats.GetVisionScanIntervalMaxSeconds();

		return Mathf.Max(m_ScanIntervalMin, m_ScanIntervalMax);
	}

	#region Detection Pipeline (candidates → geometry → LOS → observation → perception)
	private void RunScheduledScan(bool _immediate)
	{
		m_ScanStats.BeginFrame(Time.frameCount);
		float started = Time.realtimeSinceStartup;
		m_CurrentScanTier = ResolveObserverTier(_immediate);

		if (m_CurrentScanTier == VisionScanTier.Idle && !_immediate)
		{
			ScheduleNextScan(0f, VisionScanTier.Idle);
			m_ForceDetailThisScan = false;
			return;
		}

		if (m_CurrentScanTier == VisionScanTier.Cheap && !_immediate)
		{
			RunCheapMembershipPass();
			ScheduleNextScan(0f, VisionScanTier.Cheap);
			m_ForceDetailThisScan = false;
			m_ScanStats.AddFrameMilliseconds((Time.realtimeSinceStartup - started) * 1000f);
			return;
		}

		if (m_CurrentScanTier == VisionScanTier.RangeFov && !_immediate)
		{
			RunRangeFovPass();
			ScheduleNextScan(0f, VisionScanTier.RangeFov);
			m_ForceDetailThisScan = false;
			m_ScanStats.AddFrameMilliseconds((Time.realtimeSinceStartup - started) * 1000f);
			return;
		}

		if (!VisionScanScheduler.TryAcquireDetailSlot(_immediate))
		{
			m_NextScanTime = Time.time + 0.02f;
			m_ForceDetailThisScan = false;
			return;
		}

		m_ForceDetailThisScan = false;
		RunVisionScan();
		m_LastDetailScanTime = Time.time;
		m_BypassLosCacheThisScan = false;
		ScheduleNextScan(0f, VisionScanTier.Detail);
		m_ScanStats.AddFrameMilliseconds((Time.realtimeSinceStartup - started) * 1000f);
	}

	private VisionScanTier ResolveObserverTier(bool _immediate)
	{
		float now = Time.time;
		bool queuedDue = false;
		foreach (KeyValuePair<Transform, float> pair in m_DetailDue)
		{
			if (pair.Key != null && pair.Value <= now)
			{
				queuedDue = true;
				break;
			}
		}

		if (m_DetectionProcessor == null)
			m_DetectionProcessor = GetComponent<DetectionProcessor>();

		VisionLodObserverContext ctx = new VisionLodObserverContext
		{
			ImmediateScan = _immediate,
			HasSelectedTarget = SelectedCombatTarget != null,
			HasRecentlyLostContact = m_DetectionProcessor != null && m_DetectionProcessor.HasRecentlyLostContact,
			HasQueuedDetailDue = queuedDue,
			SecondsSinceLastDetailScan = now - m_LastDetailScanTime,
			SecondsSinceLastMembershipScan = now - m_LastMembershipScanTime,
			DiscoverIntervalSeconds = m_DiscoverIntervalSeconds,
			MembershipIntervalSeconds = m_MembershipIntervalSeconds
		};
		return VisionLodMath.ResolveObserverTier(ctx);
	}

	private float CoarseCullDistanceSq()
	{
		float range = m_VisionRange + m_CoarseRangePadMeters;
		return range * range;
	}

	private void EnsureScanHelpers()
	{
		if (m_CandidateProvider == null)
			m_CandidateProvider = new VisionCandidateProvider(this);
		if (m_VisibilityChecker == null)
			m_VisibilityChecker = new VisibilityChecker(
				transform,
				m_Hits ?? (m_Hits = new RaycastHit[c_RaycastHitBuffer]),
				m_AimCandidateScratch,
				m_DebugRays);
		ConfigureHelpers();
	}

	private void CollectCulledCandidates(Vector3 _origin)
	{
		EnsureScanHelpers();
		m_CandidateProvider.Collect(m_CandidateScratch, null, _origin, CoarseCullDistanceSq());
	}

	private void RunCheapMembershipPass()
	{
		m_ScanStats.BeginScan();
		Vector3 origin = GetVisionConeOriginWorld();
		CollectCulledCandidates(origin);
		m_ScanStats.AddCandidates(m_CandidateScratch.Count);
		for (int i = 0; i < m_CandidateScratch.Count; i++)
		{
			Transform root = m_CandidateScratch[i].Root;
			if (root == null)
				continue;
			m_ScanStats.AddCandidateDistance(Mathf.Sqrt(VisionGeometry.HorizontalDistanceSq(origin, root.position)));
		}

		m_LastMembershipScanTime = Time.time;
		m_ScanStats.EndScan();
	}

	private void RunRangeFovPass()
	{
		m_ScanStats.BeginScan();
		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 forwardXZ = GetVisionForwardXZForGameplay();
		float rangeSq = CoarseCullDistanceSq();
		float halfFov = ResolveHalfFovDegreesForScan() + m_CoarseFovPadDegrees;
		CollectCulledCandidates(origin);
		m_ScanStats.AddCandidates(m_CandidateScratch.Count);

		float due = Time.time + m_DetailQueueDelaySeconds;
		for (int i = 0; i < m_CandidateScratch.Count; i++)
		{
			VisionCandidateProvider.Candidate candidate = m_CandidateScratch[i];
			if (candidate.Root == null)
				continue;

			m_ScanStats.AddCandidateDistance(
				Mathf.Sqrt(VisionGeometry.HorizontalDistanceSq(origin, candidate.Root.position)));
			TryGetCandidateBounds(candidate, out bool hasBounds, out Bounds bounds);
			bool inside = VisionGeometry.IsWithinCoarseRangeAndFov(
				origin,
				forwardXZ,
				candidate.Root.position,
				hasBounds,
				bounds,
				rangeSq,
				halfFov,
				out _,
				out bool rangePass,
				out bool fovPass);
			if (rangePass)
				m_ScanStats.AddRangePass();
			if (fovPass)
				m_ScanStats.AddFovPass();
			if (!inside)
				continue;

			if (!m_DetailDue.ContainsKey(candidate.Root))
				m_DetailDue[candidate.Root] = due;
		}

		m_LastMembershipScanTime = Time.time;
		m_ScanStats.EndScan();
	}

	private void RunVisionScan()
	{
		EnsureScanHelpers();

		m_LastScanForwardXZ = GetVisionForwardXZForGameplay();
		if (m_VisibilityChecker != null)
			m_VisibilityChecker.ClearDebugRays();
		else
			m_DebugRays.Clear();

		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 forwardXZ = GetVisionForwardXZForGameplay();
		float rangeSq = m_VisionRange * m_VisionRange;
		float halfFov = ResolveHalfFovDegreesForScan();

		m_ScanStats.BeginScan();
		m_ObservationScratch.Clear();
		CollectCulledCandidates(origin);
		m_ScanStats.AddCandidates(m_CandidateScratch.Count);

		for (int i = 0; i < m_CandidateScratch.Count; i++)
		{
			VisionCandidateProvider.Candidate candidate = m_CandidateScratch[i];
			if (candidate.Root != null)
			{
				m_ScanStats.AddCandidateDistance(
					Mathf.Sqrt(VisionGeometry.HorizontalDistanceSq(origin, candidate.Root.position)));
				m_DetailDue.Remove(candidate.Root);
			}

			if (TryBuildObservation(origin, forwardXZ, rangeSq, halfFov, candidate, out VisionObservation observation))
				m_ObservationScratch.Add(observation);
		}

		if (m_Perception != null)
			m_Perception.ApplyVisionFrame(m_ObservationScratch);

		m_LastMembershipScanTime = Time.time;
		m_ScanStats.EndScan();
	}

	private bool TryBuildObservation(
		Vector3 _origin,
		Vector3 _forwardXZ,
		float _rangeSq,
		float _halfFov,
		VisionCandidateProvider.Candidate _candidate,
		out VisionObservation _observation)
	{
		_observation = default;
		if (m_VisibilityChecker == null || _candidate.Root == null)
			return false;

		TryGetCandidateBounds(_candidate, out bool hasBounds, out Bounds bounds);
		float coarseRangeSq = CoarseCullDistanceSq();
		float coarseHalfFov = _halfFov + m_CoarseFovPadDegrees;
		bool coarseInside = VisionGeometry.IsWithinCoarseRangeAndFov(
			_origin,
			_forwardXZ,
			_candidate.Root.position,
			hasBounds,
			bounds,
			coarseRangeSq,
			coarseHalfFov,
			out _,
			out bool rangePass,
			out bool fovPass);
		if (rangePass)
			m_ScanStats.AddRangePass();
		if (fovPass)
			m_ScanStats.AddFovPass();
		if (!coarseInside)
			return false;

		float now = Time.time;
		Vector3 targetPos = _candidate.Root.position;
		if (!m_BypassLosCacheThisScan &&
		    m_LosCache.TryGetValid(
			    _candidate.Root,
			    now,
			    _origin,
			    _forwardXZ,
			    targetPos,
			    m_LosCacheTtlSeconds,
			    m_LosCacheMoveEpsilonMeters,
			    2.5f,
			    out VisionLosCache.Entry cached))
		{
			if (!cached.HasLos)
				return false;
			if (!VisionGeometry.IsWithinRangeAndFov(
				    _origin, _forwardXZ, cached.AimPoint, _rangeSq, _halfFov, out float cachedDistSq))
				return false;

			_observation = BuildVisibleObservation(
				_candidate.Root, cached.AimPoint, cachedDistSq, _forwardXZ, _origin, cached.Exposure01);
			return true;
		}

		bool forceDetail = ShouldForceHitZoneDetail(_candidate.Root) || m_BypassLosCacheThisScan;
		Collider primary = ResolvePrimaryCollider(_candidate);
		if (!forceDetail && hasBounds)
		{
			if (!m_VisibilityChecker.TryCoarseLineOfSightToBounds(
				    _origin, bounds, _candidate.Root, primary, out _) &&
			    m_VisibilityChecker.LastLosWasBlocked)
			{
				m_LosCache.Store(_candidate.Root, false, Vector3.zero, 0f, now, _origin, _forwardXZ, targetPos);
				return false;
			}
		}

		Vector3 aimPoint;
		float exposure01;
		float distSq;
		if (_candidate.HasHitZones)
		{
			if (!m_VisibilityChecker.TryFindBestVisibleAimPointFromHitZones(
				    _origin, _candidate.HitZones, _candidate.Root, out aimPoint, out exposure01))
			{
				m_LosCache.Store(_candidate.Root, false, Vector3.zero, 0f, now, _origin, _forwardXZ, targetPos);
				return false;
			}
		}
		else
		{
			if (primary == null)
				return false;
			if (!m_VisibilityChecker.TryFindBestVisibleAimPointFromCollider(
				    _origin, primary, _candidate.Root, out aimPoint, out exposure01))
			{
				m_LosCache.Store(_candidate.Root, false, Vector3.zero, 0f, now, _origin, _forwardXZ, targetPos);
				return false;
			}
		}

		if (!VisionGeometry.IsWithinRangeAndFov(_origin, _forwardXZ, aimPoint, _rangeSq, _halfFov, out distSq))
			return false;

		m_LosCache.Store(_candidate.Root, true, aimPoint, exposure01, now, _origin, _forwardXZ, targetPos);
		_observation = BuildVisibleObservation(_candidate.Root, aimPoint, distSq, _forwardXZ, _origin, exposure01);
		return true;
	}

	private static VisionObservation BuildVisibleObservation(
		Transform _root,
		Vector3 _aimPoint,
		float _distanceSq,
		Vector3 _forwardXZ,
		Vector3 _origin,
		float _exposure01)
	{
		return new VisionObservation
		{
			Target = _root,
			Position = _root.position,
			AimPoint = _aimPoint,
			HasAimPoint = true,
			DistanceSq = _distanceSq,
			IsVisible = true,
			FovOffsetDegrees = VisionGeometry.HorizontalAngleDegrees(_forwardXZ, _aimPoint - _origin),
			Exposure01 = _exposure01
		};
	}

	private bool ShouldForceHitZoneDetail(Transform _root)
	{
		if (_root == null)
			return false;
		if (SelectedCombatTarget == _root)
			return true;
		if (m_DetectionProcessor != null &&
		    m_DetectionProcessor.TryGetContact(_root, out PerceivedContact contact) &&
		    contact != null &&
		    (contact.ObservationState == ObservationState.Observed ||
		     contact.ObservationState == ObservationState.RecentlyLost))
			return true;
		return false;
	}

	private static void TryGetCandidateBounds(
		VisionCandidateProvider.Candidate _candidate,
		out bool _hasBounds,
		out Bounds _bounds)
	{
		_bounds = default;
		_hasBounds = false;
		if (_candidate.HasHitZones &&
		    UnitBodyHitZoneVisionUtility.TryGetCombinedBounds(_candidate.HitZones, out _bounds))
		{
			_hasBounds = true;
			return;
		}

		if (_candidate.LegacyCollider != null)
		{
			_bounds = _candidate.LegacyCollider.bounds;
			_hasBounds = true;
		}
	}

	private static Collider ResolvePrimaryCollider(VisionCandidateProvider.Candidate _candidate)
	{
		if (_candidate.LegacyCollider != null)
			return _candidate.LegacyCollider;
		if (!_candidate.HasHitZones)
			return null;
		for (int i = 0; i < _candidate.HitZones.Length; i++)
		{
			if (!UnitBodyHitZoneVisionUtility.IsUsableVisionZone(_candidate.HitZones[i], out Collider col))
				continue;
			return col;
		}

		return null;
	}
	#endregion

	private static Vector3 GetCandidateRoughCenter(Transform _candidate)
	{
		if (_candidate.TryGetComponent(out UnitVision uv))
		{
			if (UnitBodyHitZoneVisionUtility.TryGetCombinedBounds(uv.BodyHitZones, out Bounds combined))
				return combined.center;
			if (uv.BodyCollider != null)
				return uv.BodyCollider.bounds.center;
		}

		if (_candidate.TryGetComponent(out ShootingRangeTarget rangeTarget) && rangeTarget.TargetCollider != null)
			return rangeTarget.TargetCollider.bounds.center;

		return _candidate.position;
	}

	private Collider ResolveBodyCollider()
	{
		if (m_BodyCollider != null)
			return m_BodyCollider;

		return UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(m_BodyHitZones, BodyPartType.Chest)
			?? UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(m_BodyHitZones, BodyPartType.Abdomen)
			?? UnitBodyHitZoneVisionUtility.TryGetFirstCollider(m_BodyHitZones);
	}

	private Vector3 GetEyeWorldPosition()
	{
		return m_ObservationSource != null
			? m_ObservationSource.GetEyeWorldPosition()
			: transform.position + Vector3.up * m_EyeHeight;
	}

	private Vector3 GetVisionConeOriginWorld()
	{
		return m_ObservationSource != null
			? m_ObservationSource.GetOriginWorld()
			: GetEyeWorldPosition();
	}

	private bool IsWeaponReadyForSightCone()
	{
		return m_ObservationSource != null && m_ObservationSource.IsWeaponReadyForSightCone();
	}

	private Vector3 GetRootForwardXZ()
	{
		return VisionGeometry.FlattenNormalized(transform.forward, Vector3.forward);
	}

	private Vector3 GetVisionForwardXZRaw()
	{
		if (TryGetTunerVehicleLookForwardXZ(out Vector3 tunerLook))
			return tunerLook;

		Transform basis = transform;
		if (m_ViewForwardOverride != null)
			basis = m_ViewForwardOverride;
		else if (m_UseHumanoidTorsoForward && m_Animator != null && m_Animator.isHuman)
		{
			Transform bone = m_Animator.GetBoneTransform(HumanBodyBones.UpperChest);
			if (bone == null)
				bone = m_Animator.GetBoneTransform(HumanBodyBones.Chest);
			if (bone == null)
				bone = m_Animator.GetBoneTransform(HumanBodyBones.Spine);
			if (bone != null)
				basis = bone;
		}

		Vector3 f = VisionGeometry.FlattenNormalized(basis.forward, GetRootForwardXZ());
		Vector3 rootF = GetRootForwardXZ();
		if (Vector3.Dot(f, rootF) < 0f)
			f = -f;
		return f;
	}

	private bool ShouldImmediateRescanForAimMotion()
	{
		if (m_ImmediateRescanAngleDegrees <= 0f)
			return false;

		if (Time.time < m_NextImmediateRescanAllowedTime)
			return false;

		Vector3 forward = GetVisionForwardXZForGameplay();
		if (forward.sqrMagnitude < 1e-6f || m_LastScanForwardXZ.sqrMagnitude < 1e-6f)
			return false;

		if (Vector3.Angle(m_LastScanForwardXZ, forward) < m_ImmediateRescanAngleDegrees)
			return false;

		m_NextImmediateRescanAllowedTime = Time.time + ResolveScanIntervalMinSeconds();
		return true;
	}

	private void UpdateSmoothedVisionForward()
	{
		Vector3 raw = GetVisionForwardXZRawForUpdate();
		if (m_VisionForwardSmoothTime <= 0.0001f)
		{
			m_SmoothedVisionForwardXZ = raw;
			return;
		}

		float t = 1f - Mathf.Exp(-Time.deltaTime / m_VisionForwardSmoothTime);
		if (m_SmoothedVisionForwardXZ.sqrMagnitude < 1e-6f)
			m_SmoothedVisionForwardXZ = raw;
		else
			m_SmoothedVisionForwardXZ = Vector3.Slerp(m_SmoothedVisionForwardXZ, raw, t).normalized;
	}

	private Vector3 GetVisionForwardXZRawForUpdate()
	{
		if (TryGetTunerVehicleLookForwardXZ(out Vector3 tunerLook))
			return tunerLook;

		if (ShouldUseBarrelForwardForManualFacingVision() &&
		    TryGetWeaponBoreForwardXZ(out Vector3 boreFwd))
			return boreFwd;

		return GetVisionForwardXZRaw();
	}

	private bool ShouldUseBarrelForwardForManualFacingVision()
	{
		if (!IsWeaponReadyForSightCone())
			return false;

		if (m_CachedRtsMember == null)
			m_CachedRtsMember = GetComponent<RtsUnitMember>();
		return m_CachedRtsMember != null && m_CachedRtsMember.IsManualBarrelFacingActive;
	}

	private bool TryGetWeaponBoreForwardXZ(out Vector3 _forwardXZ)
	{
		_forwardXZ = default;
		if (!IsWeaponReadyForSightCone() || m_Equipment == null)
			return false;

		EquippedWeapon weapon = m_Equipment.EquippedWeapon;
		if (weapon == null || weapon.BarrelTransform == null)
			return false;

		Vector3 boreFwd = weapon.BarrelTransform.forward;
		boreFwd.y = 0f;
		if (boreFwd.sqrMagnitude < 1e-6f)
			return false;

		boreFwd.Normalize();
		Vector3 referenceFwd = TryGetTunerVehicleLookForwardXZ(out Vector3 tunerLook)
			? tunerLook
			: GetRootForwardXZ();
		if (Vector3.Dot(boreFwd, referenceFwd) < 0f)
			boreFwd = -boreFwd;

		_forwardXZ = boreFwd;
		return true;
	}

	private static bool TryGetSightForwardXZ(Transform _sight, out Vector3 _forwardXZ)
	{
		_forwardXZ = default;
		if (_sight == null)
			return false;

		Vector3 sightFwd = _sight.forward;
		sightFwd.y = 0f;
		if (sightFwd.sqrMagnitude < 1e-6f)
			return false;

		sightFwd.Normalize();
		_forwardXZ = sightFwd;
		return true;
	}

	private bool TryGetTunerVehicleLookForwardXZ(out Vector3 _forwardXZ)
	{
		_forwardXZ = default;
		if (!Application.isPlaying)
			return false;

		if (m_CachedSeatPose == null)
			m_CachedSeatPose = GetComponent<UnitVehicleSeatPoseController>();
		return m_CachedSeatPose != null && m_CachedSeatPose.TryGetTunerLookForwardXZ(out _forwardXZ);
	}

	private Vector3 GetVisionForwardXZForGameplay()
	{
		if (TryGetTunerVehicleLookForwardXZ(out Vector3 tunerLook))
			return tunerLook;

		if (!Application.isPlaying)
			return GetVisionForwardXZRaw();

		if (ShouldUseBarrelForwardForManualFacingVision() &&
		    TryGetWeaponBoreForwardXZ(out Vector3 boreFwd))
			return boreFwd;

		if (m_SmoothedVisionForwardXZ.sqrMagnitude < 1e-6f)
			return GetVisionForwardXZRaw();
		return m_SmoothedVisionForwardXZ;
	}

	private bool ShouldWidenFovForWeaponNotReady()
	{
		if (m_Equipment == null || m_ReadyHands == null)
			return false;

		ItemDefinition def = m_Equipment.EquippedDefinition;
		if (def == null || !def.IsEquipment || def.EquipmentKind != EquipmentKind.Weapon)
			return false;

		return m_ReadyHands.IsEquippedWeaponUserNotReady();
	}

	private IEnumerator DeferredReadyTransitionRoutine(Vector3 _preReadyForwardXZ)
	{
		yield return null;

		if (m_PreserveBoreForwardDuringReadyTransition)
			yield return SmoothRootToPreserveBoreForwardRoutine(_preReadyForwardXZ);

		RequestImmediateScan();
		if (!m_LogReadyForwardShift)
			yield break;

		LogReadyForwardShift(true);
	}

	private IEnumerator SmoothRootToPreserveBoreForwardRoutine(Vector3 _desiredBoreForwardXZ)
	{
		_desiredBoreForwardXZ.y = 0f;
		if (_desiredBoreForwardXZ.sqrMagnitude < 1e-6f)
			yield break;

		_desiredBoreForwardXZ.Normalize();
		m_ReadyTransitionDesiredBoreForwardXZ = _desiredBoreForwardXZ;
		m_HasReadyTransitionDesiredBoreForwardXZ = true;

		float duration = Mathf.Max(0.01f, m_ReadyBoreRootTurnDuration);
		float elapsed = 0f;
		while (elapsed < duration)
		{
			if (SelectedCombatTarget != null || !TryGetWeaponBoreForwardXZ(out Vector3 boreForwardXZ))
				yield break;

			ApplyReadyRootBoreCorrectionStep(boreForwardXZ, _desiredBoreForwardXZ, Time.deltaTime, duration);
			elapsed += Time.deltaTime;
			yield return null;
		}
	}

	private void ApplyReadyRootBoreCorrectionStep(
		Vector3 _boreForwardXZ,
		Vector3 _desiredBoreForwardXZ,
		float _deltaTime,
		float _duration)
	{
		float yawError = Vector3.SignedAngle(_boreForwardXZ, _desiredBoreForwardXZ, Vector3.up);
		yawError = Mathf.Clamp(yawError, -m_MaxReadyBoreRootTurnDegrees, m_MaxReadyBoreRootTurnDegrees);
		if (Mathf.Abs(yawError) < m_MinReadyBoreRootTurnDegrees)
			return;

		float maxStep = (m_MaxReadyBoreRootTurnDegrees / Mathf.Max(0.01f, _duration)) * _deltaTime;
		float step = Mathf.Clamp(yawError, -maxStep, maxStep);

		transform.rotation = Quaternion.AngleAxis(step, Vector3.up) * transform.rotation;
		m_SmoothedVisionForwardXZ = GetVisionForwardXZRaw();
	}

	private void LogReadyForwardShift(bool _ready)
	{
		Vector3 rootFwd = GetRootForwardXZ();
		Vector3 visionFwd = GetVisionForwardXZForGameplay();
		float bodyYaw = transform.eulerAngles.y;
		float visionYaw = Mathf.Atan2(visionFwd.x, visionFwd.z) * Mathf.Rad2Deg;
		float visionBodyDelta = Mathf.Abs(Mathf.DeltaAngle(bodyYaw, visionYaw));
		string targetName = SelectedCombatTarget != null ? SelectedCombatTarget.name : "none";
		string borePart = TryGetWeaponBoreForwardXZ(out Vector3 boreFwd)
			? $" barrelYaw={Mathf.Atan2(boreFwd.x, boreFwd.z) * Mathf.Rad2Deg:F1}° root↔barrel={Vector3.Angle(rootFwd, boreFwd):F1}°{FormatReadyTargetBoreDelta(boreFwd)}"
			: " barrel=missing";

		if (m_ObservationSource != null &&
		    m_ObservationSource.TryGetSightTransform(out Transform sight) &&
		    TryGetSightForwardXZ(sight, out Vector3 sightFwd))
		{
			float sightYaw = Mathf.Atan2(sightFwd.x, sightFwd.z) * Mathf.Rad2Deg;
			float rootSightDelta = Vector3.Angle(rootFwd, sightFwd);
			Debug.Log(
				$"[UnitVision] Ready={_ready} unit={name} bodyYaw={bodyYaw:F1}° visionYaw={visionYaw:F1}° (Δbody={visionBodyDelta:F1}°) sightYaw={sightYaw:F1}° root↔sight={rootSightDelta:F1}°{borePart} target={targetName}",
				this);
			return;
		}

		Debug.Log(
			$"[UnitVision] Ready={_ready} unit={name} bodyYaw={bodyYaw:F1}° visionYaw={visionYaw:F1}° (Δbody={visionBodyDelta:F1}°) sight=missing{borePart} target={targetName}",
			this);
	}

	private string FormatReadyTargetBoreDelta(Vector3 _boreForwardXZ)
	{
		if (!m_HasReadyTransitionDesiredBoreForwardXZ)
			return "";

		return $" readyTarget↔barrel={Vector3.Angle(m_ReadyTransitionDesiredBoreForwardXZ, _boreForwardXZ):F1}°";
	}
	#endregion

	#region Gizmos
	private void OnDrawGizmos()
	{
		DrawLookDirectionGizmo();

		if (!m_DrawVisionGizmos)
			return;

		DrawVisionDebugGizmos();
	}

	private void DrawLookDirectionGizmo()
	{
		if (!m_DrawEyeLookDebugRay)
			return;

		Vector3 origin = Application.isPlaying ? GetVisionConeOriginWorld() : GetEyeWorldPosition();
		Vector3 direction = ResolveLookDirectionForGizmo();
		if (direction.sqrMagnitude < 1e-8f)
			return;

		GizmoDirectionDrawUtility.DrawArrow(origin, direction, m_EyeLookDebugRayLength, m_EyeLookDebugRayColor);
	}

	private Vector3 ResolveLookDirectionForGizmo()
	{
		if (Application.isPlaying)
		{
			Vector3 fwdXz = GetVisionForwardXZForGameplay();
			return new Vector3(fwdXz.x, 0f, fwdXz.z);
		}

		Vector3 rootForward = transform.forward;
		rootForward.y = 0f;
		return rootForward.sqrMagnitude > 1e-8f ? rootForward.normalized : Vector3.forward;
	}

	private void DrawVisionDebugGizmos()
	{
		Vector3 origin = Application.isPlaying
			? GetVisionConeOriginWorld()
			: transform.position + Vector3.up * m_EyeHeight;

		Vector3 forward = GetVisionForwardXZForGameplay();
		if (forward.sqrMagnitude < 1e-8f)
			return;

		float halfFov = ResolveHalfFovDegreesForScan();
		float range = m_VisionRange;
		Vector3 forwardFlat = forward.normalized;

		Quaternion leftRot = Quaternion.Euler(0f, -halfFov, 0f);
		Quaternion rightRot = Quaternion.Euler(0f, halfFov, 0f);
		Vector3 leftDir = leftRot * forwardFlat;
		Vector3 rightDir = rightRot * forwardFlat;

		Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.5f);
		Gizmos.DrawLine(origin, origin + leftDir * range);
		Gizmos.DrawLine(origin, origin + rightDir * range);

		Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
		Gizmos.DrawLine(origin, origin + forwardFlat * range);

		int arcSegments = 32;
		Vector3 prevPoint = origin + leftDir * range;
		for (int i = 1; i <= arcSegments; i++)
		{
			float t = (float)i / arcSegments;
			float angle = Mathf.Lerp(-halfFov, halfFov, t);
			Quaternion segRot = Quaternion.Euler(0f, angle, 0f);
			Vector3 segDir = segRot * forwardFlat;
			Vector3 segPoint = origin + segDir * range;
			Gizmos.DrawLine(prevPoint, segPoint);
			prevPoint = segPoint;
		}

		if (!Application.isPlaying)
			return;

		if (m_DebugRays.Count > 0)
		{
			for (int i = 0; i < m_DebugRays.Count; i++)
			{
				(Vector3 from, Vector3 to, bool hitOk) = m_DebugRays[i];
				Gizmos.color = hitOk ? m_GizmoRayHitColor : m_GizmoRayMissColor;
				Gizmos.DrawLine(from, to);
			}
		}

		if (SelectedCombatTarget != null && m_TargetSelector != null)
		{
			Gizmos.color = Color.yellow;
			Vector3 aimPoint = m_TargetSelector.GetEngageableAimPointWorld();
			Gizmos.DrawLine(origin, aimPoint);
			Gizmos.DrawWireSphere(aimPoint, 0.08f);
		}
	}
	#endregion
}
