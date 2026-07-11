using UnityEngine;

/// <summary>
/// Периодически проверяет наличие союзников/нейтралов впереди юнита.
/// При обнаружении — блокирует стрельбу через BusyState.ProximityRelax и опускает оружие стволом вниз.
/// При освобождении зоны — автоматически снимает блокировку.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]
public sealed class UnitProximityReadyController : MonoBehaviour
{
	#region Serialized Fields
	[Header("Proximity")]
	[SerializeField, Range(0.5f, 5f)] private float m_CheckRadius = 2f;
	[SerializeField, Range(0.1f, 1f)] private float m_CheckInterval = 0.35f;
	[SerializeField, Range(0.5f, 5f)] private float m_RetryDistance = 1f;
	[SerializeField, Range(0f, 1f)] private float m_DebounceSeconds = 0.3f;
	[Tooltip("Полу-угол конуса проверки вперёд (градусы). Только союзники в этом конусе считаются помехой.")]
	[SerializeField, Range(1f, 90f)] private float m_CheckHalfAngleDegrees = 10f;
	[Tooltip("Радиус и угол для ВЫХОДА из блокировки (шире чем вход — гистерезис).")]
	[SerializeField, Range(0.5f, 10f)] private float m_HysteresisRadius = 3f;
	[SerializeField, Range(1f, 90f)] private float m_HysteresisHalfAngleDegrees = 20f;
	[Tooltip("Минимальное время блокировки перед разблокировкой.")]
	[SerializeField, Range(0f, 2f)] private float m_MinBlockSeconds = 0.5f;
	[SerializeField] private LayerMask m_CheckLayers = ~0;
	[SerializeField] private QueryTriggerInteraction m_TriggerInteraction = QueryTriggerInteraction.Ignore;

	[Header("Debug")]
	[SerializeField] private bool m_LogProximity;

	[Header("References")]
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	#endregion

	#region Private Fields
	private const int c_HitBufferSize = 64;

	private Collider[] m_ProximityHits;
	private float m_NextCheckTime;
	private bool m_IsBlocked;
	private Vector3 m_BlockedPosition;
	private float m_LastUnblockTime;
	private float m_BlockedTime;
	private bool m_WasReadyToFire;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_ProximityHits = new Collider[c_HitBufferSize];

		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
	}

	private void OnEnable()
	{
		m_IsBlocked = false;
		m_NextCheckTime = 0f;
		m_LastUnblockTime = 0f;
	}

	private void OnDisable()
	{
		if (m_IsBlocked)
			ClearBlockedState();
	}

	private void Update()
	{
		if (m_BusyState == null || m_Team == null)
			return;

		if (!IsReadyToFire())
		{
			// Когда не ready из-за самого proximity-блока — продолжаем проверки.
			// Когда не ready по другой причине (sprint, E-key, без оружия) — скипаем.
			if (!m_IsBlocked)
			{
				m_WasReadyToFire = false;
				return;
			}
		}
		else
		{
			if (!m_WasReadyToFire && m_LogProximity)
				Debug.Log($"[Prox] {name}: monitoring — ready to fire, cone {m_CheckHalfAngleDegrees:F0}° r={m_CheckRadius:F1}m", this);
			m_WasReadyToFire = true;
		}

		bool forceCheck = false;
		if (m_IsBlocked)
		{
			Vector3 delta = transform.position - m_BlockedPosition;
			delta.y = 0f;
			if (delta.sqrMagnitude >= m_RetryDistance * m_RetryDistance)
			{
				forceCheck = true;
				if (m_LogProximity)
					Debug.Log($"[Prox] {name}: force re-check — moved {delta.magnitude:F2}m from block pos", this);
			}
		}

		if (!forceCheck && Time.time < m_NextCheckTime)
			return;

		m_NextCheckTime = Time.time + m_CheckInterval;

		bool foundFriendly = HasFriendlyOrNeutralNearby();
		if (foundFriendly)
		{
			SetBlocked();
		}
		else if (m_IsBlocked)
		{
			SetUnblockedIfDebounced();
		}
	}
	#endregion

	#region Private Methods
	private bool IsReadyToFire()
	{
		return m_ReadyHands != null && m_ReadyHands.IsWeaponReadyToFire();
	}

	private bool HasFriendlyOrNeutralNearby()
	{
		Vector3 origin = transform.position;
		UnitTeamId myTeam = m_Team.Team;

		float radius = m_IsBlocked ? m_HysteresisRadius : m_CheckRadius;
		float halfAngle = m_IsBlocked ? m_HysteresisHalfAngleDegrees : m_CheckHalfAngleDegrees;

		int hitCount = Physics.OverlapSphereNonAlloc(
			origin,
			radius,
			m_ProximityHits,
			m_CheckLayers,
			m_TriggerInteraction);

		if (hitCount >= c_HitBufferSize && m_LogProximity)
			Debug.LogWarning($"[Prox] {name}: buffer FULL ({c_HitBufferSize})", this);

		string foundName = null;
		Vector3 forward = transform.forward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			forward = Vector3.forward;

		for (int i = 0; i < hitCount; i++)
		{
			Collider col = m_ProximityHits[i];
			if (col == null)
				continue;

			Transform colTransform = col.transform;
			if (colTransform == transform || colTransform.IsChildOf(transform))
				continue;

			UnitTeam colTeam = col.GetComponentInParent<UnitTeam>();
			if (colTeam == null)
				continue;

			bool isFriendly = colTeam.Team == myTeam || colTeam.Team == UnitTeamId.Neutral;
			if (!isFriendly)
				continue;

			if (colTransform.GetComponentInParent<UnitVision>() == null)
				continue;

			Vector3 toTarget = colTransform.position - origin;
			toTarget.y = 0f;
			float angle = Vector3.Angle(forward, toTarget);
			if (angle > halfAngle)
				continue;

			foundName = colTransform.root.name;
			break;
		}

		if (foundName != null && m_LogProximity)
			Debug.Log($"[Prox] {name}: FOUND '{foundName}' — blocking (r={radius:F1}m cone={halfAngle:F0}° mode={(m_IsBlocked ? "exit" : "entry")})", this);

		return foundName != null;
	}

	private void SetBlocked()
	{
		if (m_IsBlocked)
		{
			m_BlockedPosition = transform.position;
			m_BlockedTime = Time.time;
			return;
		}

		if (m_LogProximity)
			Debug.Log($"[Prox] {name}: BLOCK", this);

		m_IsBlocked = true;
		m_BlockedPosition = transform.position;
		m_BlockedTime = Time.time;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.ProximityRelax, true);
		m_ReadyHands?.SetProximityReadyBlock(true);
	}

	private void SetUnblockedIfDebounced()
	{
		if (Time.time - m_BlockedTime < m_MinBlockSeconds)
		{
			if (m_LogProximity)
				Debug.Log($"[Prox] {name}: unblock pending — min block {(m_MinBlockSeconds - (Time.time - m_BlockedTime)):F2}s remaining", this);
			return;
		}

		if (Time.time - m_LastUnblockTime < m_DebounceSeconds)
		{
			if (m_LogProximity)
				Debug.Log($"[Prox] {name}: unblock pending — debounce {m_DebounceSeconds - (Time.time - m_LastUnblockTime):F2}s remaining", this);
			return;
		}

		if (m_LogProximity)
			Debug.Log($"[Prox] {name}: UNBLOCK", this);

		m_IsBlocked = false;
		m_LastUnblockTime = Time.time;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.ProximityRelax, false);
		m_ReadyHands?.SetProximityReadyBlock(false);
	}

	private void ClearBlockedState()
	{
		m_IsBlocked = false;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.ProximityRelax, false);
		m_ReadyHands?.SetProximityReadyBlock(false);
	}
	#endregion

#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		Gizmos.color = m_IsBlocked ? new Color(1f, 0.3f, 0.1f, 0.4f) : new Color(0.3f, 1f, 0.3f, 0.2f);
		Gizmos.DrawWireSphere(transform.position, m_CheckRadius);
	}
#endif
}
