using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Подгоняет размеры <see cref="NavMeshAgent"/> под <see cref="LocomotionStance"/> (стоя / присед / лёжа),
/// чтобы капсула навигации совпадала с позой персонажа.
/// После <see cref="NavMeshAgent.Warp"/> путь сбрасывается — при активном движении цель задаётся снова.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public sealed class UnitNavMeshStanceSync : MonoBehaviour
{
	[SerializeField] private UnitAnimatorStance m_StanceSource;

	[Header("Высота капсулы (множитель к сохранённой при старте)")]
	[SerializeField, Range(0.15f, 1f)] private float m_CrouchHeightMul = 0.55f;
	[SerializeField, Range(0.1f, 0.6f)] private float m_ProneHeightMul = 0.28f;

	[Header("Радиус (множитель к сохранённому при старте)")]
	[SerializeField, Range(0.5f, 1.1f)] private float m_CrouchRadiusMul = 1f;
	[SerializeField, Range(0.5f, 1.1f)] private float m_ProneRadiusMul = 0.9f;

	private NavMeshAgent m_Agent;
	private float m_BaseHeight;
	private float m_BaseRadius;
	private float m_BaseBaseOffset;
	private LocomotionStance m_LastStance = LocomotionStance.Standing;

	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		if (m_StanceSource == null)
			m_StanceSource = GetComponent<UnitAnimatorStance>();

		m_BaseHeight = m_Agent.height;
		m_BaseRadius = m_Agent.radius;
		m_BaseBaseOffset = m_Agent.baseOffset;

		if (m_StanceSource != null)
		{
			m_LastStance = m_StanceSource.CurrentStance;
			ApplyStance(m_LastStance);
		}
	}

	private void LateUpdate()
	{
		if (m_StanceSource == null)
			return;

		LocomotionStance stance = m_StanceSource.CurrentStance;
		if (stance == m_LastStance)
			return;

		m_LastStance = stance;
		ApplyStance(stance);
	}

	private void ApplyStance(LocomotionStance _stance)
	{
		float heightMul = 1f;
		float radiusMul = 1f;

		switch (_stance)
		{
			case LocomotionStance.Standing:
				break;
			case LocomotionStance.Crouch:
				heightMul = m_CrouchHeightMul;
				radiusMul = m_CrouchRadiusMul;
				break;
			case LocomotionStance.Prone:
				heightMul = m_ProneHeightMul;
				radiusMul = m_ProneRadiusMul;
				break;
		}

		float newHeight = Mathf.Max(0.05f, m_BaseHeight * heightMul);
		float newRadius = Mathf.Max(0.05f, m_BaseRadius * radiusMul);
		float scale = newHeight / m_BaseHeight;
		float newBaseOffset = m_BaseBaseOffset * scale;

		m_Agent.height = newHeight;
		m_Agent.radius = newRadius;
		m_Agent.baseOffset = newBaseOffset;

		bool restoreMove = ShouldPreserveMovementIntent();
		Vector3 savedDestination = m_Agent.destination;

		if (m_Agent.isOnNavMesh)
			m_Agent.Warp(transform.position);

		if (restoreMove)
		{
			m_Agent.isStopped = false;
			m_Agent.SetDestination(savedDestination);
		}
	}

	/// <summary>Тот же смысл, что активный заказ в <c>UnitClickToMove</c>: не терять ПКМ-цель при смене стойки.</summary>
	private bool ShouldPreserveMovementIntent()
	{
		if (m_Agent.isStopped)
			return false;
		if (m_Agent.pathPending)
			return true;
		if (!m_Agent.hasPath)
			return false;
		if (float.IsPositiveInfinity(m_Agent.remainingDistance))
			return false;
		return m_Agent.remainingDistance > m_Agent.stoppingDistance + 0.02f;
	}
}
