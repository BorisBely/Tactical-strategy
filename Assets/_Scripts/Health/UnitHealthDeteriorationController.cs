using UnityEngine;

/// <summary>
/// Тик ухудшения: травмы копят критическое давление по типу; стабилизированные — намного медленнее.
/// В бессознании кровопотеря ускоряется.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(63)]
public sealed class UnitHealthDeteriorationController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitHealth m_Health;
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilizationController;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField, Min(0.1f)] private float m_TickSeconds = 1f;
	[SerializeField, Min(1f)] private float m_LethalPressureThreshold = InjuryDeteriorationTable.LethalPressureThreshold;
	[SerializeField, Min(1f)] private float m_UnconsciousPressureMultiplier =
		InjuryDeteriorationTable.DefaultUnconsciousPressureMultiplier;

	[Header("Debug")]
	[SerializeField] private float m_DebugNextTickTime;
	[SerializeField] private float m_DebugTotalLethalPressure;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void Update()
	{
		if (m_Health == null || m_Health.IsDead || !m_Health.HasInjuries)
			return;
		if (Time.time < m_DebugNextTickTime)
			return;

		float tickSeconds = Mathf.Max(0.1f, m_TickSeconds);
		m_DebugNextTickTime = Time.time + tickSeconds;
		TickDeterioration(tickSeconds);
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Health == null)
			m_Health = GetComponent<UnitHealth>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
	}

	private void TickDeterioration(float _tickSeconds)
	{
		bool isUnconscious = m_Consciousness != null && !m_Consciousness.IsConscious;

		for (int i = 0; i < m_Health.InjuryCount; i++)
		{
			if (!m_Health.TryGetInjury(i, out InjuryUiEntry injury))
				continue;

			float pressurePerSecond = injury.IsStabilized
				? InjuryDeteriorationTable.GetStabilizedPressurePerSecond(injury)
				: InjuryDeteriorationTable.GetPressurePerSecond(injury);
			float pressure = pressurePerSecond * _tickSeconds;
			if (pressure <= 0f)
				continue;

			if (isUnconscious)
				pressure *= m_UnconsciousPressureMultiplier;

			m_Health.AddLethalPressure(i, pressure);
		}

		m_DebugTotalLethalPressure = m_Health.GetTotalLethalPressure();
		if (m_DebugTotalLethalPressure < m_LethalPressureThreshold)
		{
			m_Health.NotifyVitalsChanged();
			return;
		}

		EnterDeadState();
	}

	private void EnterDeadState()
	{
		m_SelfStabilizationController?.StopSelfStabilizationWithoutUserCancel();
		m_ClickToMove?.HardStop();
		m_LocomotionDriver?.HardStop();
		m_Health.EnterDead();
		if (m_Consciousness != null && m_Consciousness.IsConscious)
			m_Consciousness.EnterUnconscious();
	}
	#endregion
}
