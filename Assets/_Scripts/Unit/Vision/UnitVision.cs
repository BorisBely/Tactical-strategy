using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Periodic vision: range → FOV → axis smoothing → ray bundle → closest target.
/// When weapon is equipped and in high ready, LOS and cone origin come from the sight on <see cref="EquippedWeapon"/>; horizontal FOV axis and rotation come from root/torso (not from weapon tilt in animation).
/// When weapon is low ready, half‑FOV is never narrower than <see cref="m_MinHalfFovDegreesWhenWeaponNotReady"/>. While a target is held, <see cref="m_TrackingHalfFovExtraDegrees"/> is added to half‑FOV.
/// During reload, bolt cycle, or malfunction fix, <see cref="m_RetainTargetDuringReloadOrMalfunction"/> keeps the current engage target without an FOV check (range + LOS still required).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitTeam))]
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
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;

	[Header("Зрение")]
	[SerializeField, Min(0.5f)] private float m_VisionRange = 18f;
	[SerializeField, Range(1f, 179f)] private float m_FieldOfViewDegrees = 120f;
	[Tooltip("Пока в прошлом кадре уже была цель, к половине FOV добавляется этот угол — реже теряем цель на краю конуса (меньше скачков поворота юнита).")]
	[SerializeField, Range(0f, 30f)] private float m_TrackingHalfFovExtraDegrees = 15f;
	[Tooltip("Пока идёт перезарядка, передёргивание затвора или устранение клина — удерживать текущую цель, даже если она вышла из FOV (нужны дистанция и LOS).")]
	[SerializeField] private bool m_RetainTargetDuringReloadOrMalfunction = true;
	[SerializeField, Min(0f)] private float m_EyeHeight = 1.6f;

	[Header("Опрос")]
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMin = 0.25f;
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMax = 0.45f;
	[Tooltip("При повороте прицела сильнее этого угла (град.) — внеочередной скан, чтобы быстрее переключать мишени на полигоне.")]
	[SerializeField, Range(0.5f, 15f)] private float m_ImmediateRescanAngleDegrees = 2.5f;

	[Header("Экстраполяция точки прицела")]
	[Tooltip("Сглаживание оценки скорости цели по дельте между сканами (сек).")]
	[SerializeField, Min(0f)] private float m_AimPointVelocitySmoothTime = 0.15f;
	[Tooltip("Максимальное время экстраполяции точки прицела вперёд (сек). Ограничивает увод при долгом отсутствии скана.")]
	[SerializeField, Min(0.01f)] private float m_AimPointMaxProjectionSeconds = 0.5f;

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
	[Tooltip("Радиус SphereCast для проверки союзников/нейтралов на линии огня при перезахвате подавленной цели.")]
	[SerializeField, Range(0.05f, 1f)] private float m_LineOfFireSafetyRadius = 0.35f;
	[Tooltip("На сколько продлевать подавление цели, если линия огня всё ещё заблокирована союзником/нейтралом.")]
	[SerializeField, Range(0.05f, 1f)] private float m_LineOfFireBlockedRetrySeconds = 0.15f;

	[Tooltip("Scene Gizmos + Game view: направление взгляда юнита (горизонталь оси зрения).")]
	[SerializeField] private bool m_DrawEyeLookDebugRay;
	[SerializeField, Min(0.1f)] private float m_EyeLookDebugRayLength = 5f;
	[SerializeField] private Color m_EyeLookDebugRayColor = new Color(1f, 0.35f, 0.9f, 1f);
	[Tooltip("Log to Console on high‑ready / low‑ready change: root angles, vision axis and sight — for debugging gaze shift.")]
	[SerializeField] private bool m_LogReadyForwardShift;
	[Tooltip("Log suppression/revalidation of line-of-fire blocked targets.")]
	[SerializeField] private bool m_LogLineOfFireSuppression;

	private readonly List<UnitVision> m_OpponentBuffer = new List<UnitVision>(128);
	private readonly List<ShootingRangeTarget> m_RangeTargetBuffer = new List<ShootingRangeTarget>(32);
	private readonly List<UnitBodyHitZoneVisionUtility.VisionAimCandidate> m_AimCandidateScratch =
		new List<UnitBodyHitZoneVisionUtility.VisionAimCandidate>(c_AimCandidateCapacity);
	private UnitBodyHitZone[] m_BodyHitZones = Array.Empty<UnitBodyHitZone>();
	private readonly List<(Vector3 from, Vector3 to, bool hitTarget)> m_DebugRays = new List<(Vector3, Vector3, bool)>(256);

	private RaycastHit[] m_Hits;
	private float m_NextScanTime;
	private float m_NextImmediateRescanAllowedTime;
	private Transform m_VisibleTarget;
	private bool m_HasVisibleTargetAimPoint;
	private Vector3 m_VisibleTargetAimPointWorld;
	private Vector3 m_SmoothedVisionForwardXZ;
	private Transform m_CachedSightFromWeapon;
	private ItemDefinition m_CachedSightWeaponDef;
	private Vector3 m_LastScanForwardXZ;
	private Vector3 m_PreviousAimPointForVelocity;
	private Vector3 m_TargetVelocityEstimate;
	private float m_LastAimPointUpdateTime;
	private Transform m_VelocityTrackedTarget;
	private Vector3 m_LastVelocityRaw;
	private Vector3 m_ReadyTransitionDesiredBoreForwardXZ;
	private bool m_HasReadyTransitionDesiredBoreForwardXZ;
	private readonly Dictionary<Transform, float> m_LineOfFireSuppressedTargets = new Dictionary<Transform, float>();
	private Transform m_ForcedPriorityTarget;
	private RtsUnitMember m_CachedRtsMember;
	#endregion

	#region Public Properties
	/// <summary>Корень видимой цели (null если никого не видит).</summary>
	public Transform VisibleTarget => m_VisibleTarget;

	/// <summary>Видимая цель, по которой можно вести огонь (мишень не сбита, юнит жив).</summary>
	public Transform GetEngageableVisibleTarget()
	{
		return IsEngageableTarget(m_VisibleTarget) ? m_VisibleTarget : null;
	}

	public Transform ForcedPriorityTarget
	{
		get => m_ForcedPriorityTarget;
		set => m_ForcedPriorityTarget = value;
	}

	public Collider BodyCollider => ResolveBodyCollider();

	public IReadOnlyList<UnitBodyHitZone> BodyHitZones => m_BodyHitZones;

	/// <summary>
	/// Горизонтальный вектор «на цель» при engage: при оружии в ready только позиция прицела,
	/// иначе корень юнита. Если для ready-оружия прицел не найден, возвращает <c>false</c>.
	/// </summary>
	public bool TryGetEngageFacingOriginWorld(out Vector3 _origin)
	{
		if (IsWeaponReadyForSightCone())
		{
			Transform sight = GetActiveSightTransform();
			if (sight == null)
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

	/// <summary>
	/// Точка, к которой разворачиваемся/целимся по видимой цели: лучший видимый aim point среди хитбоксов цели.
	/// При наличии оценки скорости — экстраполируется вперёд на время с последнего скана.
	/// </summary>
	public Vector3 GetVisibleTargetAimPointWorld()
	{
		if (!IsEngageableTarget(m_VisibleTarget))
			return Vector3.zero;

		Vector3 basePoint;
		if (m_HasVisibleTargetAimPoint)
			basePoint = m_VisibleTargetAimPointWorld;
		else if (m_VisibleTarget.TryGetComponent(out ShootingRangeTarget rangeTarget))
			basePoint = rangeTarget.GetAimPointWorld();
		else if (m_VisibleTarget.TryGetComponent(out UnitVision targetVision))
		{
			if (UnitBodyHitZoneVisionUtility.TryGetCombinedBounds(targetVision.m_BodyHitZones, out Bounds combined))
				basePoint = combined.center;
			else if (targetVision.BodyCollider != null)
				basePoint = targetVision.BodyCollider.bounds.center;
			else
				basePoint = m_VisibleTarget.position;
		}
		else
			basePoint = m_VisibleTarget.position;

		// Экстраполируем точку прицела по скорости цели
		if (m_VelocityTrackedTarget == m_VisibleTarget && m_TargetVelocityEstimate.sqrMagnitude > 0.0001f)
		{
			float dt = Mathf.Min(Time.time - m_LastAimPointUpdateTime, m_AimPointMaxProjectionSeconds);
			if (dt > 0.001f)
				basePoint += m_TargetVelocityEstimate * dt;
		}

		return basePoint;
	}

	public void SetVisionRange(float _range)
	{
		m_VisionRange = Mathf.Max(0.5f, _range);
	}

	public void RequestImmediateScan()
	{
		if (!isActiveAndEnabled || m_Registry == null || m_Team == null)
			return;

		RunVisionScan();
		ScheduleNextScan(0f);
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

		m_Registry.GetOpponents(m_Team.Team, m_OpponentBuffer);

		for (int i = 0; i < m_OpponentBuffer.Count; i++)
		{
			UnitVision other = m_OpponentBuffer[i];
			if (other == null || other == this || !other.isActiveAndEnabled)
				continue;
			if (!UnitConsciousness.IsTargetableTarget(other.transform))
				continue;
			if (other.TryGetComponent(out DamageableTarget damageable) && !damageable.IsAlive)
				continue;
			if (IsLineOfFireSuppressed(other.transform))
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

		// TEMP: disable ready/not-ready root yaw correction entirely.
		m_HasReadyTransitionDesiredBoreForwardXZ = false;
		RequestImmediateScan();
		if (m_LogReadyForwardShift)
			LogReadyForwardShift(_ready);
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
			if (m_VisibleTarget != null || !TryGetWeaponBoreForwardXZ(out Vector3 boreForwardXZ))
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
		string targetName = m_VisibleTarget != null ? m_VisibleTarget.name : "none";
		string borePart = TryGetWeaponBoreForwardXZ(out Vector3 boreFwd)
			? $" barrelYaw={Mathf.Atan2(boreFwd.x, boreFwd.z) * Mathf.Rad2Deg:F1}° root↔barrel={Vector3.Angle(rootFwd, boreFwd):F1}°{FormatReadyTargetBoreDelta(boreFwd)}"
			: " barrel=missing";

		Transform sight = GetActiveSightTransform();
		if (sight != null && TryGetSightForwardXZ(sight, out Vector3 sightFwd))
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

	/// <summary>
	/// Временно запрещает текущую видимую цель (союзник на линии огня) и запускает немедленный скан для выбора другой цели.
	/// Если другой цели нет — юнит удержит огонь до истечения таймера или следующего скана.
	/// </summary>
	public void SuppressCurrentTargetForLineOfFire(float _seconds)
	{
		if (!isActiveAndEnabled)
			return;

		Transform currentTarget = m_VisibleTarget;
		if (currentTarget == null)
			return;

		float expireTime = Time.time + Mathf.Max(0f, _seconds);
		bool wasAlreadySuppressed = m_LineOfFireSuppressedTargets.ContainsKey(currentTarget);
		m_LineOfFireSuppressedTargets[currentTarget] = expireTime;

		if (m_LogLineOfFireSuppression)
			Debug.Log($"[LoFSup] {name}: SUPPRESS '{currentTarget.name}' for {_seconds:F2}s (expire={expireTime:F2}) — dict size={m_LineOfFireSuppressedTargets.Count} wasAlready={wasAlreadySuppressed}", this);

		ClearVisibleTargetState();
		VisibleTargetChanged?.Invoke(null);
		RequestImmediateScan();
	}

	/// <summary>
	/// Сбрасывает текущую видимую цель и ждёт следующего планового скана по ранговому интервалу.
	/// Используется после поражения цели или потери engageable-цели, чтобы full auto не перескакивал мгновенно.
	/// </summary>
	public void ClearVisibleTargetAndWaitForNextScan()
	{
		if (!isActiveAndEnabled)
			return;

		bool hadTarget = m_VisibleTarget != null;
		ClearVisibleTargetState();

		if (hadTarget)
			VisibleTargetChanged?.Invoke(null);

		ScheduleNextScan(0f);
	}

	/// <summary>Отслеживает ли зрение указанный корень цели (или его потомка/родителя).</summary>
	public bool IsTrackingTarget(Transform _targetRoot)
	{
		if (_targetRoot == null || m_VisibleTarget == null)
			return false;

		return m_VisibleTarget == _targetRoot ||
		       m_VisibleTarget.IsChildOf(_targetRoot) ||
		       _targetRoot.IsChildOf(m_VisibleTarget);
	}

	public void RefreshBodyHitZones()
	{
		m_BodyHitZones = GetComponentsInChildren<UnitBodyHitZone>(true);
	}

	/// <summary>Мишень полигона доступна, юнит жив — цель годится для прицеливания и огня.</summary>
	public bool IsEngageableTarget(Transform _target)
	{
		if (_target == null)
			return false;

		if (!UnitConsciousness.IsTargetableTarget(_target))
			return false;

		if (_target.TryGetComponent(out ShootingRangeTarget rangeTarget))
			return rangeTarget.IsAvailableForTargeting;

		if (_target.TryGetComponent(out DamageableTarget damageable))
			return damageable.IsAlive;

		return true;
	}

	/// <summary>Оценка скорости видимой цели (world-space, сглаженная).</summary>
	public Vector3 GetVisibleTargetVelocity()
	{
		if (m_VelocityTrackedTarget == m_VisibleTarget && m_VisibleTarget != null)
			return m_TargetVelocityEstimate;
		return Vector3.zero;
	}

	/// <summary>Есть ли прямая видимость до текущей visible цели (быстрый одиночный рейкаст).</summary>
	public bool HasLineOfSightToCurrentTarget()
	{
		return !TryGetLosBlocker(out _);
	}

	/// <summary>Возвращает true если LOS перекрыт, и имя объекта-блокиратора.</summary>
	public bool TryGetLosBlocker(out string _blockerName)
	{
		_blockerName = null;

		if (m_VisibleTarget == null || !m_HasVisibleTargetAimPoint)
		{
			_blockerName = "no target or aim point";
			return true;
		}

		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 aimPoint = m_VisibleTargetAimPointWorld;
		Vector3 dir = (aimPoint - origin);

		float dist = dir.magnitude;
		if (dist < 0.02f)
			return false;

		dir /= dist;
		float castDist = Mathf.Min(dist + 0.1f, m_VisionRange);
		Vector3 rayOrigin = origin + dir * 0.08f;

		int hitCount = Physics.RaycastNonAlloc(
			rayOrigin,
			dir,
			m_Hits,
			castDist - 0.08f,
			m_LayerMask,
			m_QueryTriggerInteraction);

		for (int h = 0; h < hitCount; h++)
		{
			RaycastHit hit = m_Hits[h];
			Collider hc = hit.collider;
			if (hc == null)
				continue;
			if (hc.transform.IsChildOf(transform))
				continue;
			if (hc.transform == m_VisibleTarget || hc.transform.IsChildOf(m_VisibleTarget))
				return false;

			_blockerName = hc.name;
			return true;
		}

		_blockerName = "nothing hit";
		return true;
	}

	/// <summary>
	/// Нужно ли сбрасывать прицел/серию: переход на новую цель после null или смена живой цели A→B.
	/// Не срабатывает при поражении/потере A (в т.ч. мишень на полигоне) и прямом A→B, если A уже невалидна.
	/// </summary>
	public bool ShouldReacquireAimAfterSwitch(Transform _previousEngageable, Transform _nextEngageable)
	{
		if (_nextEngageable == null || _nextEngageable == _previousEngageable)
			return false;

		if (_previousEngageable == null)
			return true;

		return IsEngageableTarget(_previousEngageable);
	}

	/// <summary>
	/// Горизонтальный forward для разворота на цель: корень/торс без цели; при ready и видимой цели — ось ствола.
	/// </summary>
	public bool TryGetEngageFacingForwardXZ(out Vector3 _forwardXZ)
	{
		if (IsWeaponReadyForSightCone())
		{
			if (GetActiveSightTransform() == null)
			{
				_forwardXZ = default;
				return false;
			}

			if (m_VisibleTarget != null && TryGetWeaponBoreForwardXZ(out Vector3 boreFwd))
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

		Transform sight = GetActiveSightTransform();
		if (sight != null)
			return sight.position;
		return transform.position;
	}
	#endregion

	#region Public Events
	public event Action<Transform> VisibleTargetChanged;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Hits = new RaycastHit[c_RaycastHitBuffer];
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
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_CachedRtsMember == null)
			m_CachedRtsMember = GetComponent<RtsUnitMember>();
	}

	private void OnEnable()
	{
		ResolveRegistryIfNeeded();
		m_SmoothedVisionForwardXZ = Vector3.zero;
		m_CachedSightWeaponDef = null;
		m_CachedSightFromWeapon = null;
		m_VelocityTrackedTarget = null;
		m_TargetVelocityEstimate = Vector3.zero;
		m_LastVelocityRaw = Vector3.zero;
		m_PreviousAimPointForVelocity = Vector3.zero;
		m_LastAimPointUpdateTime = 0f;
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

		if (Time.time < m_NextScanTime && !ShouldImmediateRescanForAimMotion())
			return;

		RunVisionScan();
		ScheduleNextScan(0f);
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
	#endregion

	#region Private Methods
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
	}

	private Vector3 GetFireOriginForLofCheck(Vector3 _fallbackOrigin)
	{
		if (m_Equipment != null)
		{
			EquippedWeapon weapon = m_Equipment.EquippedWeapon;
			if (weapon != null && weapon.FireOriginTransform != null)
				return weapon.FireOriginTransform.position;
		}

		return _fallbackOrigin;
	}

	private bool CheckAndSuppressBlockedTarget(
		ref Transform _target,
		ref Vector3 _aimPoint,
		ref bool _hasAimPoint,
		Vector3 _origin)
	{
		if (!_hasAimPoint)
			return false;

		Vector3 dir = _aimPoint - _origin;
		float dist = dir.magnitude;
		if (dist < 0.05f)
			return false;

		dir /= dist;

		int hitCount = Physics.SphereCastNonAlloc(
			_origin,
			m_LineOfFireSafetyRadius,
			dir,
			m_Hits,
			dist,
			m_LayerMask,
			m_QueryTriggerInteraction);

		if (m_LogLineOfFireSuppression && hitCount >= c_RaycastHitBuffer)
			Debug.LogWarning($"[LoFSup] {name}: PRE-BLOCK buffer FULL ({c_RaycastHitBuffer}) — blockers may be missed!", this);

		UnitTeamId myTeam = m_Team != null ? m_Team.Team : UnitTeamId.Player;
		var seenRoots = new System.Collections.Generic.HashSet<Transform>();

		for (int h = 0; h < hitCount; h++)
		{
			Collider hc = m_Hits[h].collider;
			if (hc == null)
				continue;
			if (hc.transform == transform || hc.transform.IsChildOf(transform))
				continue;
			if (hc.transform == _target || hc.transform.IsChildOf(_target))
				return false;

			if (hc.GetComponent<UnitBodyHitZone>() == null && hc.GetComponentInParent<UnitBodyHitZone>() == null)
				continue;

			UnitVision hitVision = hc.transform.GetComponentInParent<UnitVision>();
			if (hitVision != null && !seenRoots.Add(hitVision.transform))
				continue;

			UnitTeam hitTeam = hc.GetComponentInParent<UnitTeam>();
			if (hitTeam == null)
				continue;
			if (hitTeam.Team != myTeam && hitTeam.Team != UnitTeamId.Neutral)
				continue;
			if (hc.transform.GetComponentInParent<UnitVision>() == null)
				continue;

			float expireTime = Time.time + m_LineOfFireBlockedRetrySeconds;
			m_LineOfFireSuppressedTargets[_target] = expireTime;

			if (m_LogLineOfFireSuppression)
				Debug.Log($"[LoFSup] {name}: PRE-BLOCK '{_target.name}' — friendly '{hc.name}' on LoF, suppressed {m_LineOfFireBlockedRetrySeconds:F2}s", this);

			return true;
		}

		return false;
	}

	private void CleanupExpiredSuppressedTargets()
	{
		if (m_LineOfFireSuppressedTargets.Count == 0)
			return;

		float now = Time.time;
		var expiredKeys = new List<Transform>();
		foreach (var kvp in m_LineOfFireSuppressedTargets)
		{
			if (kvp.Key == null || kvp.Value <= now)
				expiredKeys.Add(kvp.Key);
		}

		foreach (var key in expiredKeys)
			m_LineOfFireSuppressedTargets.Remove(key);
	}

	private void ScheduleNextScan(float _delayOffset)
	{
		float min = ResolveScanIntervalMinSeconds();
		float max = ResolveScanIntervalMaxSeconds();
		m_NextScanTime = Time.time + _delayOffset + UnityEngine.Random.Range(min, max);
	}

	private bool IsLineOfFireSuppressed(Transform _candidate)
	{
		if (_candidate == null)
			return false;

		if (m_LineOfFireSuppressedTargets.TryGetValue(_candidate, out float expireTime) && Time.time < expireTime)
		{
			if (m_LogLineOfFireSuppression)
				Debug.Log($"[LoFSup] {name}: SKIP suppressed '{_candidate.name}' — expires in {expireTime - Time.time:F2}s", this);
			return true;
		}

		return false;
	}

	private bool TryRevalidateSuppressedTarget(Transform _candidate, Vector3 _origin)
	{
		if (_candidate == null || !m_LineOfFireSuppressedTargets.TryGetValue(_candidate, out float expireTime))
			return true;

		if (Time.time < expireTime)
			return true;

		m_LineOfFireSuppressedTargets.Remove(_candidate);

		if (m_LogLineOfFireSuppression)
			Debug.Log($"[LoFSup] {name}: REVALIDATE '{_candidate.name}' — timer expired, running SphereCast radius={m_LineOfFireSafetyRadius:F2}m", this);

		Vector3 targetCenter = GetCandidateRoughCenter(_candidate);
		Vector3 dir = targetCenter - _origin;
		float dist = dir.magnitude;
		if (dist < 0.05f)
			return true;

		dir /= dist;

		int hitCount = Physics.SphereCastNonAlloc(
			_origin,
			m_LineOfFireSafetyRadius,
			dir,
			m_Hits,
			dist,
			m_LayerMask,
			m_QueryTriggerInteraction);

		UnitTeamId myTeam = m_Team != null ? m_Team.Team : UnitTeamId.Player;
		float closestDist = float.MaxValue;
		Collider closestCollider = null;
		var seenUnitRoots = new System.Collections.Generic.HashSet<Transform>();

		for (int h = 0; h < hitCount; h++)
		{
			Collider hc = m_Hits[h].collider;
			if (hc == null || hc.transform == transform || hc.transform.IsChildOf(transform))
				continue;
			if (hc.GetComponent<UnitBodyHitZone>() == null && hc.GetComponentInParent<UnitBodyHitZone>() == null)
				continue;

			UnitVision hitVision = hc.transform.GetComponentInParent<UnitVision>();
			if (hitVision != null)
			{
				Transform hitRoot = hitVision.transform;
				if (!seenUnitRoots.Add(hitRoot))
					continue;
			}

			if (m_Hits[h].distance < closestDist)
			{
				closestDist = m_Hits[h].distance;
				closestCollider = hc;
			}
		}

		if (closestCollider != null)
		{
			if (closestCollider.transform == _candidate || closestCollider.transform.IsChildOf(_candidate))
			{
				if (m_LogLineOfFireSuppression)
					Debug.Log($"[LoFSup] {name}:   closest='{closestCollider.name}' IS target → CLEAR, suppression removed", this);
				return true;
			}

			UnitTeam hitTeam = closestCollider.GetComponentInParent<UnitTeam>();
			if (hitTeam != null && (hitTeam.Team == myTeam || hitTeam.Team == UnitTeamId.Neutral))
			{
				if (closestCollider.transform.GetComponentInParent<UnitVision>() == null)
				{
					if (m_LogLineOfFireSuppression)
						Debug.Log($"[LoFSup] {name}:   closest='{closestCollider.name}' friendly but no UnitVision — not a real unit, skip", this);
				}
				else
				{
					m_LineOfFireSuppressedTargets[_candidate] = Time.time + m_LineOfFireBlockedRetrySeconds;
					if (m_LogLineOfFireSuppression)
						Debug.Log($"[LoFSup] {name}:   closest='{closestCollider.name}' team={hitTeam.Team} → STILL BLOCKED, extended +{m_LineOfFireBlockedRetrySeconds:F2}s", this);
					return false;
				}
			}

			if (m_LogLineOfFireSuppression)
				Debug.Log($"[LoFSup] {name}:   closest='{closestCollider.name}' is obstacle/other → CLEAR, suppression removed", this);
		}
		else if (m_LogLineOfFireSuppression)
		{
			Debug.Log($"[LoFSup] {name}:   no hits → CLEAR, suppression removed", this);
		}

		return true;
	}

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

	private void ClearVisibleTargetState()
	{
		m_VisibleTarget = null;
		m_HasVisibleTargetAimPoint = false;
		m_VisibleTargetAimPointWorld = Vector3.zero;
		m_VelocityTrackedTarget = null;
		m_TargetVelocityEstimate = Vector3.zero;
		m_LastVelocityRaw = Vector3.zero;
		m_PreviousAimPointForVelocity = Vector3.zero;
	}

	private bool IsWeaponMaintenanceActive()
	{
		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return true;

		return m_WeaponRuntime != null && m_WeaponRuntime.TransientState.HasActiveMalfunction;
	}

	private bool TryRetainEngageTargetDuringWeaponMaintenance(
		Vector3 _origin,
		ref Transform _newTarget,
		ref Vector3 _aimPoint,
		ref bool _hasAimPoint)
	{
		if (!m_RetainTargetDuringReloadOrMalfunction || !IsWeaponMaintenanceActive() || m_VisibleTarget == null)
			return false;

		if (!TryRevalidateRetainedEngageTarget(m_VisibleTarget, _origin, out Vector3 retainedAim, out bool retainedHasAim))
			return false;

		_newTarget = m_VisibleTarget;
		_aimPoint = retainedAim;
		_hasAimPoint = retainedHasAim;
		return true;
	}

	private bool TryRevalidateRetainedEngageTarget(
		Transform _targetRoot,
		Vector3 _origin,
		out Vector3 _aimPoint,
		out bool _hasAimPoint)
	{
		_aimPoint = Vector3.zero;
		_hasAimPoint = false;

		if (!IsEngageableTarget(_targetRoot))
			return false;

		if (IsLineOfFireSuppressed(_targetRoot))
			return false;

		if (!TryRevalidateSuppressedTarget(_targetRoot, _origin))
			return false;

		float rangeSq = m_VisionRange * m_VisionRange;

		if (_targetRoot.TryGetComponent(out UnitVision other) && other.m_BodyHitZones.Length > 0)
		{
			if (!TryFindBestVisibleAimPointFromHitZones(_origin, other.m_BodyHitZones, _targetRoot, out Vector3 aimPoint))
				return false;

			Vector3 toAim = aimPoint - _origin;
			toAim.y = 0f;
			if (toAim.sqrMagnitude > rangeSq || toAim.sqrMagnitude < 0.0001f)
				return false;

			_aimPoint = aimPoint;
			_hasAimPoint = true;
			return true;
		}

		Collider legacyTargetCol = other != null && other.m_BodyCollider != null
			? other.m_BodyCollider
			: _targetRoot.GetComponentInChildren<Collider>();
		if (legacyTargetCol == null)
			return false;

		Vector3 targetCenter = legacyTargetCol.bounds.center;
		Vector3 toTarget = targetCenter - _origin;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude > rangeSq || toTarget.sqrMagnitude < 0.0001f)
			return false;

		if (!TryFindBestVisibleAimPointFromCollider(_origin, legacyTargetCol, _targetRoot, out Vector3 legacyAimPoint))
			return false;

		_aimPoint = legacyAimPoint;
		_hasAimPoint = true;
		return true;
	}

	private void RunVisionScan()
	{
		m_LastScanForwardXZ = GetVisionForwardXZForGameplay();
		m_DebugRays.Clear();
		CleanupExpiredSuppressedTargets();
		if (!IsEngageableTarget(m_VisibleTarget))
		{
			m_VisibleTarget = null;
			m_HasVisibleTargetAimPoint = false;
			m_VisibleTargetAimPointWorld = Vector3.zero;
		}

		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 forwardXZ = GetVisionForwardXZForGameplay();

		m_Registry.GetOpponents(m_Team.Team, m_OpponentBuffer);

		Transform bestTarget = null;
		bool hasBestAimPoint = false;
		Vector3 bestAimPoint = Vector3.zero;
		float bestDistSq = float.MaxValue;
		float rangeSq = m_VisionRange * m_VisionRange;
		float halfFov = ResolveHalfFovDegreesForScan();

		for (int i = 0; i < m_OpponentBuffer.Count; i++)
		{
			UnitVision other = m_OpponentBuffer[i];
			if (other == null || other == this || !other.isActiveAndEnabled)
				continue;

			if (!UnitConsciousness.IsTargetableTarget(other.transform))
				continue;

			if (other.TryGetComponent(out DamageableTarget damageable) && !damageable.IsAlive)
				continue;

			if (IsLineOfFireSuppressed(other.transform))
				continue;
			if (!TryRevalidateSuppressedTarget(other.transform, origin))
				continue;

			if (other.m_BodyHitZones.Length > 0)
			{
				TryEvaluateHitZoneVisionCandidate(
					origin,
					forwardXZ,
					rangeSq,
					halfFov,
					other.transform,
					other.m_BodyHitZones,
					ref bestDistSq,
					ref bestTarget,
					ref bestAimPoint,
					ref hasBestAimPoint);
				continue;
			}

			Collider legacyTargetCol = other.m_BodyCollider != null
				? other.m_BodyCollider
				: other.GetComponentInChildren<Collider>();
			if (legacyTargetCol == null)
				continue;

			TryEvaluateLegacyVisionCandidate(
				origin,
				forwardXZ,
				rangeSq,
				halfFov,
				other.transform,
				legacyTargetCol,
				ref bestDistSq,
				ref bestTarget,
				ref bestAimPoint,
				ref hasBestAimPoint);
		}

		if (m_Team != null && m_Team.Team == UnitTeamId.Player && m_RangeTargetRegistry != null)
		{
			m_RangeTargetRegistry.GetActiveTargets(m_RangeTargetBuffer);
			for (int i = 0; i < m_RangeTargetBuffer.Count; i++)
			{
				ShootingRangeTarget rangeTarget = m_RangeTargetBuffer[i];
				if (rangeTarget == null || !rangeTarget.IsAvailableForTargeting)
					continue;

				if (IsLineOfFireSuppressed(rangeTarget.transform))
					continue;
				if (!TryRevalidateSuppressedTarget(rangeTarget.transform, origin))
					continue;

				Collider targetCol = rangeTarget.TargetCollider;
				if (targetCol == null)
					continue;

				TryEvaluateLegacyVisionCandidate(
					origin,
					forwardXZ,
					rangeSq,
					halfFov,
					rangeTarget.transform,
					targetCol,
					ref bestDistSq,
					ref bestTarget,
					ref bestAimPoint,
					ref hasBestAimPoint);
			}
		}

		Transform newTarget = bestTarget;
		if (newTarget != null)
		{
			Vector3 fireOrigin = GetFireOriginForLofCheck(origin);
			if (CheckAndSuppressBlockedTarget(ref newTarget, ref bestAimPoint, ref hasBestAimPoint, fireOrigin))
			{
				newTarget = null;
				hasBestAimPoint = false;
				bestAimPoint = Vector3.zero;
			}
		}

		TryRetainEngageTargetDuringWeaponMaintenance(origin, ref newTarget, ref bestAimPoint, ref hasBestAimPoint);

		bool targetChanged = newTarget != m_VisibleTarget;

		if (m_ForcedPriorityTarget != null && m_ForcedPriorityTarget != newTarget)
		{
			Transform forcedRoot = m_ForcedPriorityTarget;
			bool forcedValid = false;
			Vector3 forcedAimPoint = Vector3.zero;

			if (forcedRoot.TryGetComponent(out UnitVision forcedVision) &&
			    forcedVision.isActiveAndEnabled &&
			    UnitConsciousness.IsTargetableTarget(forcedRoot) &&
			    !(forcedRoot.TryGetComponent(out DamageableTarget forcedDmg) && !forcedDmg.IsAlive) &&
			    !IsLineOfFireSuppressed(forcedRoot) &&
			    TryRevalidateSuppressedTarget(forcedRoot, origin))
			{
				forcedAimPoint = GetCandidateRoughCenter(forcedRoot);
				forcedValid = true;
			}
			else if (forcedRoot.TryGetComponent(out ShootingRangeTarget rangeTarget) &&
			         rangeTarget.IsAvailableForTargeting &&
			         !IsLineOfFireSuppressed(forcedRoot) &&
			         TryRevalidateSuppressedTarget(forcedRoot, origin))
			{
				forcedAimPoint = rangeTarget.GetAimPointWorld();
				forcedValid = true;
			}

			if (forcedValid)
			{
				Vector3 eyePos = GetEyeWorldPosition();
				Vector3 rayDir = (forcedAimPoint - eyePos).normalized;
				float rayDist = Vector3.Distance(eyePos, forcedAimPoint);
				if (!Physics.Raycast(eyePos, rayDir, rayDist, m_LayerMask, m_QueryTriggerInteraction))
				{
					newTarget = forcedRoot;
					bestAimPoint = forcedAimPoint;
					hasBestAimPoint = true;
				}
			}
		}

		m_VisibleTarget = newTarget;
		m_HasVisibleTargetAimPoint = newTarget != null && hasBestAimPoint;
		m_VisibleTargetAimPointWorld = m_HasVisibleTargetAimPoint ? bestAimPoint : Vector3.zero;
		if (targetChanged)
		{
			VisibleTargetChanged?.Invoke(m_VisibleTarget);
		}

		UpdateTargetVelocityEstimate(newTarget, bestAimPoint, hasBestAimPoint);
	}

	private void UpdateTargetVelocityEstimate(Transform _newTarget, Vector3 _newAimPoint, bool _hasValidAimPoint)
	{
		float now = Time.time;

		if (_newTarget != m_VelocityTrackedTarget)
		{
			m_VelocityTrackedTarget = _newTarget;
			m_PreviousAimPointForVelocity = _hasValidAimPoint ? _newAimPoint : Vector3.zero;
			m_TargetVelocityEstimate = Vector3.zero;
			m_LastVelocityRaw = Vector3.zero;
			m_LastAimPointUpdateTime = now;
			return;
		}

		if (!_hasValidAimPoint || _newAimPoint == Vector3.zero)
			return;

		float dt = now - m_LastAimPointUpdateTime;
		m_LastAimPointUpdateTime = now;

		if (dt > 0.001f && m_PreviousAimPointForVelocity != Vector3.zero)
		{
			Vector3 rawVelocity = (_newAimPoint - m_PreviousAimPointForVelocity) / dt;

			if (m_AimPointVelocitySmoothTime <= 0.0001f)
			{
				m_TargetVelocityEstimate = rawVelocity;
			}
			else
			{
				float t = 1f - Mathf.Exp(-dt / m_AimPointVelocitySmoothTime);
				m_LastVelocityRaw = rawVelocity;
				m_TargetVelocityEstimate = Vector3.Lerp(m_TargetVelocityEstimate, rawVelocity, t);
			}
		}

		m_PreviousAimPointForVelocity = _newAimPoint;
	}

	private Collider ResolveBodyCollider()
	{
		if (m_BodyCollider != null)
			return m_BodyCollider;

		return UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(m_BodyHitZones, BodyPartType.Chest)
			?? UnitBodyHitZoneVisionUtility.TryGetPreferredCollider(m_BodyHitZones, BodyPartType.Abdomen)
			?? UnitBodyHitZoneVisionUtility.TryGetFirstCollider(m_BodyHitZones);
	}

	private bool TryEvaluateHitZoneVisionCandidate(
		Vector3 _origin,
		Vector3 _forwardXZ,
		float _rangeSq,
		float _halfFov,
		Transform _targetRoot,
		UnitBodyHitZone[] _hitZones,
		ref float _bestDistSq,
		ref Transform _bestTarget,
		ref Vector3 _bestAimPoint,
		ref bool _hasBestAimPoint)
	{
		if (!TryFindBestVisibleAimPointFromHitZones(_origin, _hitZones, _targetRoot, out Vector3 aimPoint))
			return false;

		Vector3 toAim = aimPoint - _origin;
		toAim.y = 0f;
		float distSq = toAim.sqrMagnitude;
		if (distSq > _rangeSq || distSq < 0.0001f)
			return false;

		float ang = Vector3.Angle(_forwardXZ, toAim.normalized);
		if (ang > _halfFov)
			return false;

		if (distSq < _bestDistSq)
		{
			_bestDistSq = distSq;
			_bestTarget = _targetRoot;
			_bestAimPoint = aimPoint;
			_hasBestAimPoint = true;
			return true;
		}

		return false;
	}

	private bool TryEvaluateLegacyVisionCandidate(
		Vector3 _origin,
		Vector3 _forwardXZ,
		float _rangeSq,
		float _halfFov,
		Transform _targetRoot,
		Collider _targetCol,
		ref float _bestDistSq,
		ref Transform _bestTarget,
		ref Vector3 _bestAimPoint,
		ref bool _hasBestAimPoint)
	{
		Vector3 targetCenter = _targetCol.bounds.center;
		Vector3 toTarget = targetCenter - _origin;
		toTarget.y = 0f;
		float distSq = toTarget.sqrMagnitude;
		if (distSq > _rangeSq || distSq < 0.0001f)
			return false;

		float ang = Vector3.Angle(_forwardXZ, toTarget.normalized);
		if (ang > _halfFov)
			return false;

		if (!TryFindBestVisibleAimPointFromCollider(_origin, _targetCol, _targetRoot, out Vector3 aimPoint))
			return false;

		if (distSq < _bestDistSq)
		{
			_bestDistSq = distSq;
			_bestTarget = _targetRoot;
			_bestAimPoint = aimPoint;
			_hasBestAimPoint = true;
			return true;
		}

		return false;
	}

	private Vector3 GetEyeWorldPosition()
	{
		return transform.position + Vector3.up * m_EyeHeight;
	}

	private Vector3 GetVisionConeOriginWorld()
	{
		Transform sight = GetActiveSightTransform();
		if (sight != null)
			return sight.position;
		return GetEyeWorldPosition();
	}

	private bool IsWeaponReadyForSightCone()
	{
		if (m_ReadyHands == null || m_Equipment == null)
			return false;
		ItemDefinition def = m_Equipment.EquippedDefinition;
		if (def == null || !def.IsEquipment || def.EquipmentKind != EquipmentKind.Weapon)
			return false;
		return m_ReadyHands.IsWeaponEquippedAndReady();
	}

	private Transform GetActiveSightTransform()
	{
		if (!IsWeaponReadyForSightCone())
		{
			m_CachedSightWeaponDef = null;
			m_CachedSightFromWeapon = null;
			return null;
		}

		if (m_SightPivotOverride != null)
			return m_SightPivotOverride;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (weapon != null && weapon.SightPivotTransform != null)
			return weapon.SightPivotTransform;

		if (string.IsNullOrWhiteSpace(m_SightPivotChildName))
			return null;

		Transform weaponRoot = m_Equipment.MainWeaponRoot;
		if (weaponRoot == null)
			return null;

		ItemDefinition def = m_Equipment.EquippedDefinition;
		if (def != m_CachedSightWeaponDef)
		{
			m_CachedSightWeaponDef = def;
			m_CachedSightFromWeapon = FindChildTransformByName(weaponRoot, m_SightPivotChildName);
		}

		return m_CachedSightFromWeapon;
	}

	private static Transform FindChildTransformByName(Transform _root, string _name)
	{
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t != _root && t.name == _name)
				return t;
		}

		return null;
	}

	private Vector3 GetRootForwardXZ()
	{
		Vector3 f = transform.forward;
		f.y = 0f;
		if (f.sqrMagnitude < 0.0001f)
			f = Vector3.forward;
		return f.normalized;
	}

	/// <summary>Сырое направление «взгляда» без сглаживания (для первого кадра и Edit Mode). Ось — корень/торс; прицел влияет только на origin LOS.</summary>
	private Vector3 GetVisionForwardXZRaw()
	{
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

		Vector3 f = basis.forward;
		f.y = 0f;
		if (f.sqrMagnitude < 0.0001f)
			return GetRootForwardXZ();
		f.Normalize();

		// На части FBX ось forward кости смотрит назад относительно корня — проецируем в ту же полусферу, что и корень.
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
		Vector3 rootFwd = GetRootForwardXZ();
		if (Vector3.Dot(boreFwd, rootFwd) < 0f)
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

	/// <summary>Направление конуса FOV: в игре сглаженное, в редакторе без Play — мгновенное.</summary>
	private Vector3 GetVisionForwardXZForGameplay()
	{
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

	public float ResolveHalfFovDegreesForScan()
	{
		float halfFov = m_FieldOfViewDegrees * 0.5f;
		if (ShouldWidenFovForWeaponNotReady())
			halfFov = Mathf.Max(halfFov, m_MinHalfFovDegreesWhenWeaponNotReady);
		if (m_VisibleTarget != null)
			halfFov += m_TrackingHalfFovExtraDegrees;
		return halfFov;
	}

	private void BuildLegacyAimCandidates(Collider _col, List<UnitBodyHitZoneVisionUtility.VisionAimCandidate> _out)
	{
		UnitBodyHitZoneVisionUtility.BuildAimCandidates(BodyPartType.Chest, _col, _out);
	}

	private bool TryFindBestVisibleAimPointFromHitZones(
		Vector3 _eye,
		UnitBodyHitZone[] _hitZones,
		Transform _opponentRoot,
		out Vector3 _aimPoint)
	{
		_aimPoint = Vector3.zero;
		bool found = false;
		float bestWeight = float.MinValue;

		for (int z = 0; z < _hitZones.Length; z++)
		{
			UnitBodyHitZone zone = _hitZones[z];
			if (zone == null || !zone.IncludeInVision || !zone.TryGetComponent(out Collider zoneCol) || !zoneCol.enabled)
				continue;

			UnitBodyHitZoneVisionUtility.BuildAimCandidates(zone.BodyPart, zoneCol, m_AimCandidateScratch);
			for (int i = 0; i < m_AimCandidateScratch.Count; i++)
			{
				UnitBodyHitZoneVisionUtility.VisionAimCandidate candidate = m_AimCandidateScratch[i];
				bool ok = HasLineOfSightToPoint(_eye, candidate.Point, _opponentRoot, zoneCol, out Vector3 rayEnd, out bool hitTarget);
				if (m_DrawVisionGizmos)
					m_DebugRays.Add((_eye, rayEnd, hitTarget && ok));
				if (!ok || candidate.Weight <= bestWeight)
					continue;

				bestWeight = candidate.Weight;
				_aimPoint = candidate.Point;
				found = true;
			}
		}

		return found;
	}

	private bool TryFindBestVisibleAimPointFromCollider(
		Vector3 _eye,
		Collider _targetCol,
		Transform _opponentRoot,
		out Vector3 _aimPoint)
	{
		_aimPoint = Vector3.zero;
		bool found = false;
		float bestWeight = float.MinValue;

		BuildLegacyAimCandidates(_targetCol, m_AimCandidateScratch);
		for (int i = 0; i < m_AimCandidateScratch.Count; i++)
		{
			UnitBodyHitZoneVisionUtility.VisionAimCandidate candidate = m_AimCandidateScratch[i];
			bool ok = HasLineOfSightToPoint(_eye, candidate.Point, _opponentRoot, _targetCol, out Vector3 rayEnd, out bool hitTarget);
			if (m_DrawVisionGizmos)
				m_DebugRays.Add((_eye, rayEnd, hitTarget && ok));
			if (!ok || candidate.Weight <= bestWeight)
				continue;

			bestWeight = candidate.Weight;
			_aimPoint = candidate.Point;
			found = true;
		}

		return found;
	}

	private bool HasLineOfSightToPoint(
		Vector3 _eye,
		Vector3 _worldPoint,
		Transform _opponentRoot,
		Collider _primaryTargetCollider,
		out Vector3 _rayEndDebug,
		out bool _hitTargetCollider)
	{
		_hitTargetCollider = false;
		Vector3 dir = (_worldPoint - _eye);
		float dist = dir.magnitude;
		if (dist < 0.02f)
		{
			_rayEndDebug = _worldPoint;
			return true;
		}

		dir /= dist;
		float castMax = Mathf.Min(dist + 0.1f, m_VisionRange);
		Vector3 origin = _eye + dir * 0.08f;

		int hitCount = Physics.RaycastNonAlloc(
			origin,
			dir,
			m_Hits,
			castMax - 0.08f,
			m_LayerMask,
			m_QueryTriggerInteraction);

		_rayEndDebug = origin + dir * (castMax - 0.08f);

		for (int h = 0; h < hitCount; h++)
		{
			RaycastHit hit = m_Hits[h];
			Collider hc = hit.collider;
			if (hc != null && hc.transform.IsChildOf(transform))
				continue;

			if (hc != null && (hc == _primaryTargetCollider || hc.transform.IsChildOf(_opponentRoot)))
			{
				_hitTargetCollider = true;
				_rayEndDebug = hit.point;
				return true;
			}

			_rayEndDebug = hit.point;
			return false;
		}

		return false;
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

		// Cone boundaries
		Quaternion leftRot = Quaternion.Euler(0f, -halfFov, 0f);
		Quaternion rightRot = Quaternion.Euler(0f, halfFov, 0f);
		Vector3 leftDir = leftRot * forwardFlat;
		Vector3 rightDir = rightRot * forwardFlat;

		Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.5f);
		Gizmos.DrawLine(origin, origin + leftDir * range);
		Gizmos.DrawLine(origin, origin + rightDir * range);

		// Center line
		Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
		Gizmos.DrawLine(origin, origin + forwardFlat * range);

		// Arc at max range
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

		if (m_VisibleTarget != null)
		{
			Gizmos.color = Color.yellow;
			Vector3 aimPoint = GetVisibleTargetAimPointWorld();
			Gizmos.DrawLine(origin, aimPoint);
			Gizmos.DrawWireSphere(aimPoint, 0.08f);
		}
	}
	#endregion
}
