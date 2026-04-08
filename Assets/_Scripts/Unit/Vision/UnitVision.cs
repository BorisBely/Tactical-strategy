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
	private const int c_BundlePoints = 5;
	#endregion

	#region Private Fields
	[SerializeField] private UnitVisionRegistry m_Registry;
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
	private readonly List<Vector3> m_BundleScratch = new List<Vector3>(c_BundlePoints);
	private readonly List<(Vector3 from, Vector3 to, bool hitTarget)> m_DebugRays = new List<(Vector3, Vector3, bool)>(256);

	private RaycastHit[] m_Hits;
	private float m_NextScanTime;
	private Transform m_VisibleTarget;
	private Vector3 m_SmoothedVisionForwardXZ;
	private Transform m_CachedSightFromWeapon;
	private ItemDefinition m_CachedSightWeaponDef;
	private bool m_WasUsingSightForward;
	#endregion

	#region Public Properties
	/// <summary>Корень видимой цели (null если никого не видит).</summary>
	public Transform VisibleTarget => m_VisibleTarget;

	public Collider BodyCollider => m_BodyCollider;

	/// <summary>
	/// Горизонтальный вектор «на цель» при engage: при активном прицеле — позиция прицела, иначе корень юнита.
	/// </summary>
	public Vector3 GetEngageFacingOriginWorld()
	{
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

		if (Time.time < m_NextScanTime)
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
	private void ScheduleNextScan(float _delayOffset)
	{
		float min = Mathf.Min(m_ScanIntervalMin, m_ScanIntervalMax);
		float max = Mathf.Max(m_ScanIntervalMin, m_ScanIntervalMax);
		m_NextScanTime = Time.time + _delayOffset + UnityEngine.Random.Range(min, max);
	}

	private void RunVisionScan()
	{
		m_DebugRays.Clear();

		Vector3 origin = GetVisionConeOriginWorld();
		Vector3 forwardXZ = GetVisionForwardXZForGameplay();

		m_Registry.GetOpponents(m_Team.Team, m_OpponentBuffer);

		UnitVision best = null;
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

			Collider targetCol = other.BodyCollider != null
				? other.BodyCollider
				: other.GetComponentInChildren<Collider>();
			if (targetCol == null)
				continue;

			Vector3 targetCenter = targetCol.bounds.center;
			Vector3 toTarget = targetCenter - origin;
			toTarget.y = 0f;
			float distSq = toTarget.sqrMagnitude;
			if (distSq > rangeSq || distSq < 0.0001f)
				continue;

			float ang = Vector3.Angle(forwardXZ, toTarget.normalized);
			if (ang > halfFov)
				continue;

			if (!IsAnyBundlePointVisible(origin, targetCol, other.transform))
				continue;

			if (distSq < bestDistSq)
			{
				bestDistSq = distSq;
				best = other;
			}
		}

		Transform newTarget = best != null ? best.transform : null;
		if (newTarget != m_VisibleTarget)
		{
			m_VisibleTarget = newTarget;
			VisibleTargetChanged?.Invoke(m_VisibleTarget);
		}
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

		return m_ReadyHands.ShouldUseUnarmedLocomotionBranch();
	}

	private void BuildBundleWorldPoints(Collider _col, List<Vector3> _out)
	{
		_out.Clear();
		Bounds b = _col.bounds;
		float y = b.center.y;
		Vector3 c = new Vector3(b.center.x, y, b.center.z);
		Vector3 e = b.extents;
		_out.Add(c);
		_out.Add(new Vector3(c.x + e.x, y, c.z + e.z));
		_out.Add(new Vector3(c.x - e.x, y, c.z + e.z));
		_out.Add(new Vector3(c.x + e.x, y, c.z - e.z));
		_out.Add(new Vector3(c.x - e.x, y, c.z - e.z));
	}

	private bool IsAnyBundlePointVisible(Vector3 _eye, Collider _targetCol, Transform _opponentRoot)
	{
		BuildBundleWorldPoints(_targetCol, m_BundleScratch);
		for (int i = 0; i < m_BundleScratch.Count; i++)
		{
			Vector3 pt = m_BundleScratch[i];
			bool ok = HasLineOfSightToPoint(_eye, pt, _opponentRoot, _targetCol, out Vector3 rayEnd, out bool hitTarget);
			if (m_DrawVisionGizmos)
				m_DebugRays.Add((_eye, rayEnd, hitTarget && ok));
			if (ok)
				return true;
		}

		return false;
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
			Gizmos.DrawLine(origin, m_VisibleTarget.position + Vector3.up * 1f);
		}
	}
	#endregion
}
