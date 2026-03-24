using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Список целей в инспекторе; «видимость» — луч из точки обзора до точки прицеливания, первое попадание по маске — этот враг.
/// Ближайший видимый в горизонтальном секторе — поворот в walk/run (см. <see cref="UnitClickToMove"/>).
/// </summary>
[DisallowMultipleComponent]
public class UnitEnemyFacing : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitClickToMove m_ClickToMove;

	[Header("Performance")]
	[Tooltip("Интервал между полными проходами по списку врагов (луч на каждого кандидата в секторе).")]
	[SerializeField, Min(0.02f)] private float m_ScanInterval = 0.14f;
	[Tooltip("Полный скан только в кадрах frameCount % N == фаза юнита.")]
	[SerializeField, Range(1, 32)] private int m_StaggerFrameMod = 10;
	[Tooltip("При удержании цели — повторная проверка лучом не чаще (сек).")]
	[SerializeField, Min(0.05f)] private float m_LosRecheckWhileTracked = 0.35f;

	[Header("Targets")]
	[Tooltip("Корни врагов (объекты с коллайдером на себе или в дочерних). Перетащите сюда все мишени/врагов.")]
	[SerializeField] private List<Transform> m_EnemyTargets = new List<Transform>();

	[Header("Detection")]
	[Tooltip("Слои, по которым идёт луч видимости (враг + препятствия). Первое попадание должно быть коллайдером этой цели.")]
	[SerializeField] private LayerMask m_EnemyLayers;
	[SerializeField] private LayerMask m_LayersToIgnore;
	[Tooltip("Дополнительно к Enemy Layers для стен между глазами и целью.")]
	[SerializeField] private LayerMask m_ObstacleLayers;
	[SerializeField, Min(0.5f)] private float m_DetectionRange = 24f;
	[SerializeField, Range(5f, 90f)] private float m_HorizontalHalfAngle = 42f;
	[SerializeField, Min(0.1f)] private float m_RayOriginHeight = 1.35f;
	[SerializeField, Min(0f)] private float m_RayForwardBias = 0.35f;
	[Tooltip("Сдвиг начала LOS-луча вдоль направления к цели — чтобы не попасть в свой коллайдер изнутри капсулы.")]
	[SerializeField, Min(0f)] private float m_LosRayStartInset = 0.12f;
	[SerializeField, Min(0.1f)] private float m_TargetHeightOffset = 0.9f;
	[SerializeField] private bool m_CheckLineOfSight = true;
	[SerializeField] private bool m_HitEnemyTriggers = true;

	[Header("Rotation")]
	[SerializeField, Min(0.1f)] private float m_FaceEnemyRotationSpeed = 14f;

	[Header("Visualization")]
	[SerializeField] private bool m_DrawGizmosInSceneView = true;
	[SerializeField] private bool m_DrawDebugRaysInPlayMode;
	[SerializeField] private Color m_GizmoRangeColor = new Color(1f, 0.55f, 0.1f, 0.15f);
	[SerializeField] private Color m_GizmoLosColor = new Color(0.2f, 1f, 0.35f, 0.9f);
	[SerializeField, Min(0f)] private float m_DebugRayDuration;
	#endregion

	#region Private Fields
	private NavMeshAgent m_Agent;
	private int m_ScanPhase;
	private float m_NextScanTime;
	private float m_LastLosCheckTime;
	private Collider m_TrackedEnemy;
	private Vector3 m_LastGizmoAimPoint;
	private bool m_LastGizmoHadTarget;
	private RaycastHit[] m_RaycastBuffer;
	#endregion

	public Collider TrackedEnemy => m_TrackedEnemy;

	/// <summary>
	/// Если true, <see cref="UnitClickToMove"/> не перезаписывает yaw по направлению движения — оставляет поворот к врагу из LateUpdate.
	/// </summary>
	public bool ShouldSuppressMovementRotationTowardVelocity()
	{
		if (m_ClickToMove == null || m_Agent == null)
			return false;
		if (!m_ClickToMove.IsWalkOrRunMoveMode)
			return false;
		if (!IsMovingOnNavMesh())
			return false;
		return m_TrackedEnemy != null && m_TrackedEnemy.gameObject.activeInHierarchy;
	}

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();

		m_Agent = GetComponent<NavMeshAgent>();
		m_ScanPhase = Mathf.Abs(GetInstanceID()) % Mathf.Max(1, m_StaggerFrameMod);
		m_NextScanTime = Random.Range(0f, m_ScanInterval);

		int playerLayer = LayerMask.NameToLayer("Player");
		if (playerLayer >= 0)
			m_LayersToIgnore |= 1 << playerLayer;

		m_RaycastBuffer = new RaycastHit[16];
	}

	private void LateUpdate()
	{
		m_LastGizmoHadTarget = false;

		if (m_ClickToMove == null || m_Agent == null)
			return;

		if (!m_ClickToMove.IsWalkOrRunMoveMode)
		{
			m_TrackedEnemy = null;
			return;
		}

		if (!IsMovingOnNavMesh())
		{
			m_TrackedEnemy = null;
			return;
		}

		Vector3 forward = GetPlanarForwardFromTransform();
		Vector3 eye = GetEyePosition(forward);

		bool trackedOk = ValidateTrackedTarget(eye, forward, out Vector3 aimIfOk);
		if (trackedOk)
		{
			m_LastGizmoAimPoint = aimIfOk;
			m_LastGizmoHadTarget = true;
			if (m_DrawDebugRaysInPlayMode)
				DebugDrawTracking(eye, aimIfOk);
			ApplyRotationTowards(aimIfOk);
			return;
		}

		m_TrackedEnemy = null;

		if (!ShouldRunHeavyScanThisFrame())
			return;

		if (TryPickNearestVisibleFromList(eye, forward, out Collider chosen, out Vector3 aimPoint))
		{
			m_TrackedEnemy = chosen;
			m_LastLosCheckTime = Time.time;
			m_LastGizmoAimPoint = aimPoint;
			m_LastGizmoHadTarget = true;
			m_NextScanTime = Time.time + m_ScanInterval;
			if (m_DrawDebugRaysInPlayMode)
				DebugDrawTracking(eye, aimPoint);
			ApplyRotationTowards(aimPoint);
		}
		else
			m_NextScanTime = Time.time + m_ScanInterval;
	}
	#endregion

	#region Private Methods
	private Vector3 GetPlanarForwardFromTransform()
	{
		Vector3 forward = transform.forward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 1e-6f)
			forward = Vector3.forward;
		return forward.normalized;
	}

	private Vector3 GetEyePosition(Vector3 _planarForward)
	{
		return transform.position + Vector3.up * m_RayOriginHeight + _planarForward * m_RayForwardBias;
	}

	private bool IsMovingOnNavMesh()
	{
		Vector3 v = m_Agent.velocity;
		v.y = 0f;
		if (v.sqrMagnitude > 0.01f)
			return true;

		return m_Agent.hasPath &&
		       m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
	}

	private bool ShouldRunHeavyScanThisFrame()
	{
		if (Time.time < m_NextScanTime)
			return false;
		int mod = Mathf.Max(1, m_StaggerFrameMod);
		return Time.frameCount % mod == m_ScanPhase % mod;
	}

	private QueryTriggerInteraction GetTriggerQuery()
	{
		return m_HitEnemyTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
	}

	private int GetVisibilityRayMask()
	{
		int mask = m_EnemyLayers.value == 0 ? Physics.DefaultRaycastLayers : m_EnemyLayers.value;
		mask |= m_ObstacleLayers.value;
		mask &= ~m_LayersToIgnore.value;
		return mask;
	}

	private bool ValidateTrackedTarget(Vector3 _eye, Vector3 _forwardPlanar, out Vector3 _aimPoint)
	{
		_aimPoint = default;
		if (m_TrackedEnemy == null || !m_TrackedEnemy.gameObject.activeInHierarchy)
			return false;

		_aimPoint = GetAimPoint(m_TrackedEnemy);
		Vector3 toPlanar = _aimPoint - transform.position;
		toPlanar.y = 0f;
		float rangeSq = m_DetectionRange * m_DetectionRange;
		if (toPlanar.sqrMagnitude > rangeSq * 1.02f)
			return false;

		float ang = Vector3.Angle(_forwardPlanar, toPlanar.normalized);
		if (ang > m_HorizontalHalfAngle + 3f)
			return false;

		if (m_CheckLineOfSight && Time.time - m_LastLosCheckTime >= m_LosRecheckWhileTracked)
		{
			m_LastLosCheckTime = Time.time;
			if (!RaySeesEnemyCollider(_eye, m_TrackedEnemy, _aimPoint))
				return false;
		}

		return true;
	}

	private bool TryPickNearestVisibleFromList(Vector3 _eye, Vector3 _forwardPlanar, out Collider _chosen, out Vector3 _aimPoint)
	{
		_chosen = null;
		_aimPoint = default;

		if (m_EnemyTargets == null || m_EnemyTargets.Count == 0)
			return false;

		float rangeSq = m_DetectionRange * m_DetectionRange;
		float cosLimit = Mathf.Cos(m_HorizontalHalfAngle * Mathf.Deg2Rad);
		float bestPlanarDistSq = float.MaxValue;
		QueryTriggerInteraction tq = GetTriggerQuery();

		for (int i = 0; i < m_EnemyTargets.Count; i++)
		{
			Transform root = m_EnemyTargets[i];
			if (root == null || !root.gameObject.activeInHierarchy)
				continue;

			if (!TryResolveTargetCollider(root, out Collider c))
				continue;

			Vector3 aim = GetAimPoint(c);
			Vector3 toPlanar = aim - transform.position;
			toPlanar.y = 0f;
			float dSq = toPlanar.sqrMagnitude;
			if (dSq < 1e-4f || dSq > rangeSq)
				continue;

			Vector3 dir = toPlanar.normalized;
			if (Vector3.Dot(_forwardPlanar, dir) < cosLimit)
				continue;

			if (m_CheckLineOfSight && !RaySeesEnemyCollider(_eye, c, aim, tq))
				continue;

			if (dSq < bestPlanarDistSq)
			{
				bestPlanarDistSq = dSq;
				_chosen = c;
				_aimPoint = aim;
			}
		}

		return _chosen != null;
	}

	private static bool TryResolveTargetCollider(Transform _root, out Collider _collider)
	{
		_collider = _root.GetComponent<Collider>();
		if (_collider == null)
			_collider = _root.GetComponentInChildren<Collider>();
		return _collider != null;
	}

	private bool RaySeesEnemyCollider(Vector3 _eye, Collider _enemy, Vector3 _aimPoint)
	{
		return RaySeesEnemyCollider(_eye, _enemy, _aimPoint, GetTriggerQuery());
	}

	private bool RaySeesEnemyCollider(Vector3 _eye, Collider _enemy, Vector3 _aimPoint, QueryTriggerInteraction _tq)
	{
		Vector3 to = _aimPoint - _eye;
		float dist = to.magnitude;
		if (dist < 0.01f)
			return true;

		Vector3 dir = to / dist;
		float inset = Mathf.Min(m_LosRayStartInset, dist * 0.45f);
		Vector3 origin = inset > 0.001f ? _eye + dir * inset : _eye;
		float castDist = dist - inset;
		if (castDist < 0.02f)
			return true;

		int mask = GetVisibilityRayMask();
		if (m_RaycastBuffer == null || m_RaycastBuffer.Length == 0)
			m_RaycastBuffer = new RaycastHit[16];

		int num = Physics.RaycastNonAlloc(origin, dir, m_RaycastBuffer, castDist, mask, _tq);
		int closest = -1;
		float closestDist = float.MaxValue;
		for (int i = 0; i < num; i++)
		{
			Collider hc = m_RaycastBuffer[i].collider;
			if (hc == null || IsOwnUnitCollider(hc))
				continue;
			float d = m_RaycastBuffer[i].distance;
			if (d < closestDist)
			{
				closestDist = d;
				closest = i;
			}
		}

		if (closest < 0)
			return false;
		return IsSameFacingTarget(m_RaycastBuffer[closest].collider, _enemy);
	}

	private bool IsOwnUnitCollider(Collider _c)
	{
		Transform t = _c.transform;
		return t == transform || t.IsChildOf(transform);
	}

	private static bool IsSameFacingTarget(Collider _hit, Collider _registered)
	{
		if (_hit == null || _registered == null)
			return false;
		if (_hit == _registered)
			return true;

		Transform ra = GetFacingRoot(_hit.transform);
		Transform rb = GetFacingRoot(_registered.transform);
		return ra == rb;
	}

	private static Transform GetFacingRoot(Transform _t)
	{
		Rigidbody rb = _t.GetComponentInParent<Rigidbody>();
		return rb != null ? rb.transform : _t.root;
	}

	private void ApplyRotationTowards(Vector3 _worldAim)
	{
		Vector3 planar = _worldAim - transform.position;
		planar.y = 0f;
		if (planar.sqrMagnitude < 0.0001f)
			return;

		Quaternion targetRot = Quaternion.LookRotation(planar.normalized, Vector3.up);
		transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, m_FaceEnemyRotationSpeed * Time.deltaTime);
	}

	private void DebugDrawTracking(Vector3 _eye, Vector3 _aim)
	{
		float d = m_DebugRayDuration > 0f ? m_DebugRayDuration : Time.deltaTime;
		Debug.DrawLine(_eye, _aim, m_GizmoLosColor, d);
	}

	private Vector3 GetAimPoint(Collider _c)
	{
		if (_c == null)
			return Vector3.zero;
		Bounds b = _c.bounds;
		float y = Mathf.Clamp(b.min.y + m_TargetHeightOffset, b.min.y + 0.15f, b.max.y);
		return new Vector3(b.center.x, y, b.center.z);
	}
	#endregion

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (!m_DrawGizmosInSceneView)
			return;

		Vector3 forward = Application.isPlaying ? GetPlanarForwardFromTransform() : transform.forward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 1e-6f)
			forward = Vector3.forward;
		forward.Normalize();

		Gizmos.color = m_GizmoRangeColor;
		Gizmos.DrawWireSphere(transform.position, m_DetectionRange);

		if (m_LastGizmoHadTarget)
		{
			Vector3 eye = transform.position + Vector3.up * m_RayOriginHeight + forward * m_RayForwardBias;
			Gizmos.color = m_GizmoLosColor;
			Gizmos.DrawLine(eye, m_LastGizmoAimPoint);
			Gizmos.DrawSphere(m_LastGizmoAimPoint, 0.12f);
		}
	}
#endif
}
