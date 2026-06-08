using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Периодическое зрение: дистанция → FOV → сглаживание оси <see cref="m_VisionForwardSmoothTime"/> → пучок лучей → ближайшая цель.
/// При экипированном оружии и «на готове» опционально конус и LOS от прицела на <see cref="EquippedWeapon"/> (или оверрайд / поиск по имени на <c>UnitVision</c>).
/// Иначе точка — «глаза» (<see cref="m_EyeHeight"/>), ось — торс/корень/<see cref="m_ViewForwardOverride"/>.
/// При оружии «не на готове» половина FOV не уже <see cref="m_MinHalfFovDegreesWhenWeaponNotReady"/>. Пока цель удерживается, к половине FOV добавляется <see cref="m_TrackingHalfFovExtraDegrees"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitTeam))]
public sealed class UnitVision : MonoBehaviour
{
	#region Constants
	private const int c_RaycastHitBuffer = 16;
	private const int c_AimCandidateCapacity = 9;
	#endregion

	private readonly struct AimCandidate
	{
		public readonly Vector3 Point;
		public readonly float Weight;

		public AimCandidate(Vector3 _point, float _weight)
		{
			Point = _point;
			Weight = _weight;
		}
	}

	#region Private Fields
	[SerializeField] private UnitVisionRegistry m_Registry;
	[SerializeField] private ShootingRangeTargetRegistry m_RangeTargetRegistry;
	[SerializeField] private UnitTeam m_Team;
	[Tooltip("Коллайдер этого юнита для попадания чужих лучей; если пусто — GetComponentInChildren.")]
	[SerializeField] private Collider m_BodyCollider;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;

	[Header("Зрение")]
	[SerializeField, Min(0.5f)] private float m_VisionRange = 18f;
	[SerializeField, Range(1f, 179f)] private float m_FieldOfViewDegrees = 90f;
	[Tooltip("Пока в прошлом кадре уже была цель, к половине FOV добавляется этот угол — реже теряем цель на краю конуса (меньше скачков поворота юнита).")]
	[SerializeField, Range(0f, 30f)] private float m_TrackingHalfFovExtraDegrees = 12f;
	[SerializeField, Min(0f)] private float m_EyeHeight = 1.6f;

	[Header("Опрос")]
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMin = 0.25f;
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMax = 0.45f;
	[Tooltip("При повороте прицела сильнее этого угла (град.) — внеочередной скан, чтобы быстрее переключать мишени на полигоне.")]
	[SerializeField, Range(0.5f, 15f)] private float m_ImmediateRescanAngleDegrees = 2.5f;

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
	[Tooltip("При экипированном оружии и «не на готове» корень часто не совпадает с осью взгляда — не даём половине FOV быть уже этого порога (градусы от оси).")]
	[SerializeField, Range(1f, 89f)] private float m_MinHalfFovDegreesWhenWeaponNotReady = 52f;

	[Header("Прицел (оружие на готове)")]
	[Tooltip("Редкий оверрайд на юните. Обычно прицел задаётся на префабе в EquippedWeapon → Sight Pivot.")]
	[SerializeField] private Transform m_SightPivotOverride;
	[Tooltip("Если Override пуст и на EquippedWeapon нет Sight Pivot: искать под визуалом оружия дочерний Transform с этим именем.")]
	[SerializeField] private string m_SightPivotChildName = "";

	[Header("Отладка")]
	[SerializeField] private bool m_DrawVisionGizmos;
	[SerializeField] private Color m_GizmoFovColor = new Color(0.2f, 0.8f, 0.3f, 0.9f);
	[SerializeField] private Color m_GizmoRayHitColor = new Color(1f, 0.3f, 0.1f, 0.9f);
	[SerializeField] private Color m_GizmoRayMissColor = new Color(0.4f, 0.4f, 0.9f, 0.6f);

	[Tooltip("Play Mode + Gizmos: луч из точки конуса (глаза или прицел в «готов»), направление = ось FOV по горизонтали.")]
	[SerializeField] private bool m_DrawEyeLookDebugRay;
	[SerializeField, Min(0.1f)] private float m_EyeLookDebugRayLength = 5f;
	[SerializeField] private Color m_EyeLookDebugRayColor = new Color(1f, 0.35f, 0.9f, 1f);

	private readonly List<UnitVision> m_OpponentBuffer = new List<UnitVision>(128);
	private readonly List<ShootingRangeTarget> m_RangeTargetBuffer = new List<ShootingRangeTarget>(32);
	private readonly List<AimCandidate> m_AimCandidateScratch = new List<AimCandidate>(c_AimCandidateCapacity);
	private readonly List<(Vector3 from, Vector3 to, bool hitTarget)> m_DebugRays = new List<(Vector3, Vector3, bool)>(256);

	private RaycastHit[] m_Hits;
	private float m_NextScanTime;
	private Transform m_VisibleTarget;
	private bool m_HasVisibleTargetAimPoint;
	private Vector3 m_VisibleTargetAimPointWorld;
	private Vector3 m_SmoothedVisionForwardXZ;
	private Transform m_CachedSightFromWeapon;
	private ItemDefinition m_CachedSightWeaponDef;
	private bool m_WasUsingSightForward;
	private Vector3 m_LastScanForwardXZ;
	#endregion

	#region Public Properties
	/// <summary>Корень видимой цели (null если никого не видит).</summary>
	public Transform VisibleTarget => m_VisibleTarget;

	/// <summary>Видимая цель, по которой можно вести огонь (мишень не сбита, юнит жив).</summary>
	public Transform GetEngageableVisibleTarget()
	{
		return IsEngageableTarget(m_VisibleTarget) ? m_VisibleTarget : null;
	}

	public Collider BodyCollider => m_BodyCollider;

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
	/// Точка, к которой разворачиваемся/целимся по видимой цели: лучший видимый aim point внутри одного коллайдера.
	/// </summary>
	public Vector3 GetVisibleTargetAimPointWorld()
	{
		if (!IsEngageableTarget(m_VisibleTarget))
			return Vector3.zero;

		if (m_HasVisibleTargetAimPoint)
			return m_VisibleTargetAimPointWorld;

		if (m_VisibleTarget.TryGetComponent(out ShootingRangeTarget rangeTarget))
			return rangeTarget.GetAimPointWorld();

		if (m_VisibleTarget.TryGetComponent(out UnitVision targetVision) && targetVision.BodyCollider != null)
			return targetVision.BodyCollider.bounds.center;

		return m_VisibleTarget.position;
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

	/// <summary>Мишень полигона доступна, юнит жив — цель годится для прицеливания и огня.</summary>
	public bool IsEngageableTarget(Transform _target)
	{
		if (_target == null)
			return false;

		if (_target.TryGetComponent(out ShootingRangeTarget rangeTarget))
			return rangeTarget.IsAvailableForTargeting;

		if (_target.TryGetComponent(out DamageableTarget damageable))
			return damageable.IsAlive;

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
	/// Горизонтальный forward для разворота на цель: в ready только forward прицела,
	/// иначе forward корня. Если у ready-оружия прицел не найден, возвращает <c>false</c>.
	/// </summary>
	public bool TryGetEngageFacingForwardXZ(out Vector3 _forwardXZ)
	{
		if (IsWeaponReadyForSightCone())
		{
			Transform sight = GetActiveSightTransform();
			if (sight == null)
			{
				_forwardXZ = default;
				return false;
			}

			Vector3 sightFwd = sight.forward;
			sightFwd.y = 0f;
			if (sightFwd.sqrMagnitude < 1e-6f)
			{
				_forwardXZ = default;
				return false;
			}

			sightFwd.Normalize();
			Vector3 rootFwd = GetRootForwardXZ();
			if (Vector3.Dot(sightFwd, rootFwd) < 0f)
				sightFwd = -sightFwd;

			_forwardXZ = sightFwd;
			return true;
		}

		_forwardXZ = GetRootForwardXZ();
		return true;
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
		if (m_BodyCollider == null)
			m_BodyCollider = GetComponentInChildren<Collider>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
	}

	private void OnEnable()
	{
		ResolveRegistryIfNeeded();
		m_SmoothedVisionForwardXZ = Vector3.zero;
		m_WasUsingSightForward = false;
		m_CachedSightWeaponDef = null;
		m_CachedSightFromWeapon = null;
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

	private void ScheduleNextScan(float _delayOffset)
	{
		float min = Mathf.Min(m_ScanIntervalMin, m_ScanIntervalMax);
		float max = Mathf.Max(m_ScanIntervalMin, m_ScanIntervalMax);
		m_NextScanTime = Time.time + _delayOffset + UnityEngine.Random.Range(min, max);
	}

	private void RunVisionScan()
	{
		m_LastScanForwardXZ = GetVisionForwardXZForGameplay();
		m_DebugRays.Clear();

		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 forwardXZ = GetVisionForwardXZForGameplay();

		m_Registry.GetOpponents(m_Team.Team, m_OpponentBuffer);

		Transform bestTarget = null;
		bool hasBestAimPoint = false;
		Vector3 bestAimPoint = Vector3.zero;
		float bestDistSq = float.MaxValue;
		float rangeSq = m_VisionRange * m_VisionRange;
		float halfFov = m_FieldOfViewDegrees * 0.5f;
		if (ShouldWidenFovForWeaponNotReady())
			halfFov = Mathf.Max(halfFov, m_MinHalfFovDegreesWhenWeaponNotReady);
		if (m_VisibleTarget != null)
			halfFov += m_TrackingHalfFovExtraDegrees;

		for (int i = 0; i < m_OpponentBuffer.Count; i++)
		{
			UnitVision other = m_OpponentBuffer[i];
			if (other == null || other == this || !other.isActiveAndEnabled)
				continue;

			if (other.TryGetComponent(out DamageableTarget damageable) && !damageable.IsAlive)
				continue;

			Collider targetCol = other.BodyCollider != null
				? other.BodyCollider
				: other.GetComponentInChildren<Collider>();
			if (targetCol == null)
				continue;

			TryEvaluateVisionCandidate(
				origin,
				forwardXZ,
				rangeSq,
				halfFov,
				other.transform,
				targetCol,
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

				Collider targetCol = rangeTarget.TargetCollider;
				if (targetCol == null)
					continue;

				TryEvaluateVisionCandidate(
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
		bool targetChanged = newTarget != m_VisibleTarget;
		m_VisibleTarget = newTarget;
		m_HasVisibleTargetAimPoint = newTarget != null && hasBestAimPoint;
		m_VisibleTargetAimPointWorld = m_HasVisibleTargetAimPoint ? bestAimPoint : Vector3.zero;
		if (targetChanged)
		{
			VisibleTargetChanged?.Invoke(m_VisibleTarget);
		}
	}

	private bool TryEvaluateVisionCandidate(
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

		if (!TryFindBestVisibleAimPoint(_origin, _targetCol, _targetRoot, out Vector3 aimPoint))
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

	/// <summary>Точка начала конуса и LOS: прицел в «готов» или глаза.</summary>
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

	/// <summary>Сырое направление «взгляда» без сглаживания (для первого кадра и Edit Mode).</summary>
	private Vector3 GetVisionForwardXZRaw()
	{
		Transform sight = GetActiveSightTransform();
		if (sight != null)
		{
			Vector3 sightFwd = sight.forward;
			sightFwd.y = 0f;
			if (sightFwd.sqrMagnitude < 1e-6f)
				return GetRootForwardXZ();
			sightFwd.Normalize();
			Vector3 sightRootF = GetRootForwardXZ();
			if (Vector3.Dot(sightFwd, sightRootF) < 0f)
				sightFwd = -sightFwd;
			return sightFwd;
		}

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

		Vector3 forward = GetVisionForwardXZForGameplay();
		if (forward.sqrMagnitude < 1e-6f || m_LastScanForwardXZ.sqrMagnitude < 1e-6f)
			return false;

		return Vector3.Angle(m_LastScanForwardXZ, forward) >= m_ImmediateRescanAngleDegrees;
	}

	private void UpdateSmoothedVisionForward()
	{
		bool useSight = GetActiveSightTransform() != null;
		Vector3 raw = GetVisionForwardXZRaw();
		if (useSight != m_WasUsingSightForward)
		{
			m_WasUsingSightForward = useSight;
			m_SmoothedVisionForwardXZ = raw;
			return;
		}

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

	/// <summary>Направление конуса FOV: в игре сглаженное, в редакторе без Play — мгновенное.</summary>
	private Vector3 GetVisionForwardXZForGameplay()
	{
		if (!Application.isPlaying)
			return GetVisionForwardXZRaw();
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

	private void BuildAimCandidates(Collider _col, List<AimCandidate> _out)
	{
		_out.Clear();
		Bounds b = _col.bounds;
		Vector3 c = b.center;
		Vector3 e = b.extents;

		// One-collider fallback that mimics future body zones:
		// primary ~= chest, then center/upper/lower, then low-priority visible edges.
		float primaryY = c.y + e.y * 0.35f;
		_out.Add(new AimCandidate(new Vector3(c.x, primaryY, c.z), 100f));
		_out.Add(new AimCandidate(c, 80f));
		_out.Add(new AimCandidate(new Vector3(c.x, c.y + e.y * 0.75f, c.z), 70f));
		_out.Add(new AimCandidate(new Vector3(c.x, c.y - e.y * 0.35f, c.z), 50f));
		_out.Add(new AimCandidate(new Vector3(c.x + e.x, primaryY, c.z), 30f));
		_out.Add(new AimCandidate(new Vector3(c.x - e.x, primaryY, c.z), 30f));
		_out.Add(new AimCandidate(new Vector3(c.x, primaryY, c.z + e.z), 30f));
		_out.Add(new AimCandidate(new Vector3(c.x, primaryY, c.z - e.z), 30f));
		_out.Add(new AimCandidate(new Vector3(c.x, c.y - e.y * 0.75f, c.z), 25f));
	}

	private bool TryFindBestVisibleAimPoint(Vector3 _eye, Collider _targetCol, Transform _opponentRoot, out Vector3 _aimPoint)
	{
		_aimPoint = Vector3.zero;
		bool found = false;
		float bestWeight = float.MinValue;

		BuildAimCandidates(_targetCol, m_AimCandidateScratch);
		for (int i = 0; i < m_AimCandidateScratch.Count; i++)
		{
			AimCandidate candidate = m_AimCandidateScratch[i];
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
		if (!m_DrawVisionGizmos)
			return;

		// Конус FOV всегда из текущего положения/оси (торс/корень), иначе в Play Mode он «застывает»
		// до следующего RunVisionScan — юнит уже повернулся, а зелёные линии остаются старыми.
		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 fwd = GetVisionForwardXZForGameplay();

		Gizmos.color = m_GizmoFovColor;
		Gizmos.DrawWireSphere(origin, 0.12f);

		float half = m_FieldOfViewDegrees * 0.5f;
		Vector3 l = (Quaternion.AngleAxis(-half, Vector3.up) * fwd) * m_VisionRange;
		Vector3 r = (Quaternion.AngleAxis(half, Vector3.up) * fwd) * m_VisionRange;
		Gizmos.DrawLine(origin, origin + l);
		Gizmos.DrawLine(origin, origin + r);

		Vector3 prev = origin + (Quaternion.AngleAxis(-half, Vector3.up) * fwd).normalized * m_VisionRange;
		const int arcSeg = 24;
		for (int i = 1; i <= arcSeg; i++)
		{
			float a = -half + (2f * half * i / arcSeg);
			Vector3 next = origin + (Quaternion.AngleAxis(a, Vector3.up) * fwd).normalized * m_VisionRange;
			Gizmos.DrawLine(prev, next);
			prev = next;
		}

		if (Application.isPlaying && m_DebugRays.Count > 0)
		{
			for (int i = 0; i < m_DebugRays.Count; i++)
			{
				(Vector3 from, Vector3 to, bool hitOk) = m_DebugRays[i];
				Gizmos.color = hitOk ? m_GizmoRayHitColor : m_GizmoRayMissColor;
				Gizmos.DrawLine(from, to);
			}
		}

		if (m_VisibleTarget != null && Application.isPlaying)
		{
			Gizmos.color = Color.yellow;
			Vector3 aimPoint = GetVisibleTargetAimPointWorld();
			Gizmos.DrawLine(origin, aimPoint);
			Gizmos.DrawWireSphere(aimPoint, 0.08f);
		}
	}
	#endregion
}
