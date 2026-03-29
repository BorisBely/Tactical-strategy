using UnityEngine;

/// <summary>
/// В простое плавно меняет <c>IdleVariant</c> (0…1) для blend tree простоя в NavMeshLocomotion.
/// </summary>
[DisallowMultipleComponent]
public sealed class IdleVariantCycler : MonoBehaviour
{
	public const string ParamIdleVariant = "IdleVariant";

	private static readonly int s_IdleVariant = Animator.StringToHash(ParamIdleVariant);
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);

	[SerializeField] private Animator m_Animator;
	[SerializeField, Min(0.1f)] private float m_StepIntervalSeconds = 5f;
	[SerializeField, Min(0f)] private float m_NavSpeedIdleThreshold = 0.06f;

	private float m_Accumulated;
	private int m_StepIndex;

	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
	}

	private void Update()
	{
		if (m_Animator == null)
			return;

		float speed = m_Animator.GetFloat(s_NavSpeed);
		if (speed > m_NavSpeedIdleThreshold)
		{
			m_Accumulated = 0f;
			return;
		}

		m_Accumulated += Time.deltaTime;
		if (m_Accumulated < m_StepIntervalSeconds)
			return;

		m_Accumulated = 0f;
		m_StepIndex = (m_StepIndex + 1) % 5;
		m_Animator.SetFloat(s_IdleVariant, m_StepIndex * 0.25f);
	}
}
