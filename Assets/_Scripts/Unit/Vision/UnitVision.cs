using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Периодическое зрение: дистанция → FOV → пучок лучей к коллайдеру цели → ближайшая видимая цель.
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

	[Header("Зрение")]
	[SerializeField, Min(0.5f)] private float m_VisionRange = 18f;
	[SerializeField, Range(1f, 179f)] private float m_FieldOfViewDegrees = 90f;
	[SerializeField, Min(0f)] private float m_EyeHeight = 1.6f;

	[Header("Опрос")]
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMin = 0.25f;
	[SerializeField, Min(0.02f)] private float m_ScanIntervalMax = 0.45f;

	[Header("Физика")]
	[SerializeField] private LayerMask m_LayerMask = ~0;
	[SerializeField] private QueryTriggerInteraction m_QueryTriggerInteraction = QueryTriggerInteraction.Ignore;

	[Header("Отладка")]
	[SerializeField] private bool m_DrawVisionGizmos = true;
	[SerializeField] private Color m_GizmoFovColor = new Color(0.2f, 0.8f, 0.3f, 0.9f);
	[SerializeField] private Color m_GizmoRayHitColor = new Color(1f, 0.3f, 0.1f, 0.9f);
	[SerializeField] private Color m_GizmoRayMissColor = new Color(0.4f, 0.4f, 0.9f, 0.6f);

	private readonly List<UnitVision> m_OpponentBuffer = new List<UnitVision>(128);
	private readonly List<Vector3> m_BundleScratch = new List<Vector3>(c_BundlePoints);
	private readonly List<(Vector3 from, Vector3 to, bool hitTarget)> m_DebugRays = new List<(Vector3, Vector3, bool)>(256);

	private RaycastHit[] m_Hits;
	private float m_NextScanTime;
	private Transform m_VisibleTarget;
	private Vector3 m_DebugEye;
	private Vector3 m_DebugForwardXZ;
	#endregion

	#region Public Properties
	/// <summary>Корень видимой цели (null если никого не видит).</summary>
	public Transform VisibleTarget => m_VisibleTarget;

	public Collider BodyCollider => m_BodyCollider;
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
	}

	private void OnEnable()
	{
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

		if (Time.time < m_NextScanTime)
			return;

		RunVisionScan();
		ScheduleNextScan(0f);
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

		Vector3 eye = GetEyeWorldPosition();
		Vector3 forwardXZ = GetForwardXZ();
		m_DebugEye = eye;
		m_DebugForwardXZ = forwardXZ;

		m_Registry.GetOpponents(m_Team.Team, m_OpponentBuffer);

		UnitVision best = null;
		float bestDistSq = float.MaxValue;
		float rangeSq = m_VisionRange * m_VisionRange;
		float halfFov = m_FieldOfViewDegrees * 0.5f;

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
			Vector3 toTarget = targetCenter - eye;
			toTarget.y = 0f;
			float distSq = toTarget.sqrMagnitude;
			if (distSq > rangeSq || distSq < 0.0001f)
				continue;

			float ang = Vector3.Angle(forwardXZ, toTarget.normalized);
			if (ang > halfFov)
				continue;

			if (!IsAnyBundlePointVisible(eye, targetCol, other.transform))
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

	private Vector3 GetForwardXZ()
	{
		Vector3 f = transform.forward;
		f.y = 0f;
		if (f.sqrMagnitude < 0.0001f)
			f = Vector3.forward;
		return f.normalized;
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

		Vector3 eye = GetEyeWorldPosition();
		Vector3 fwd = GetForwardXZ();
		if (Application.isPlaying && m_DebugForwardXZ.sqrMagnitude > 0.0001f)
		{
			eye = m_DebugEye;
			fwd = m_DebugForwardXZ;
		}

		Gizmos.color = m_GizmoFovColor;
		Gizmos.DrawWireSphere(eye, 0.12f);

		float half = m_FieldOfViewDegrees * 0.5f;
		Vector3 l = (Quaternion.AngleAxis(-half, Vector3.up) * fwd) * m_VisionRange;
		Vector3 r = (Quaternion.AngleAxis(half, Vector3.up) * fwd) * m_VisionRange;
		Gizmos.DrawLine(eye, eye + l);
		Gizmos.DrawLine(eye, eye + r);

		Vector3 prev = eye + (Quaternion.AngleAxis(-half, Vector3.up) * fwd).normalized * m_VisionRange;
		const int arcSeg = 24;
		for (int i = 1; i <= arcSeg; i++)
		{
			float a = -half + (2f * half * i / arcSeg);
			Vector3 next = eye + (Quaternion.AngleAxis(a, Vector3.up) * fwd).normalized * m_VisionRange;
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
			Gizmos.DrawLine(eye, m_VisibleTarget.position + Vector3.up * 1f);
		}
	}
	#endregion
}
