using UnityEngine;

/// <summary>
/// Runtime-состояние юнита для будущих ранений, боли и подавления.
/// Пока компонент отдаёт только множители для систем прицеливания и отдачи.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitCombatCondition : MonoBehaviour
{
	#region Serialized Fields
	[Header("Runtime State")]
	[SerializeField] private bool m_ArmsWounded;
	[SerializeField] private bool m_LegsWounded;
	[SerializeField] private bool m_HeavyPain;
	[SerializeField] private bool m_Suppressed;

	[Header("Aim And Accuracy Penalties")]
	[SerializeField, Min(0.01f)] private float m_ArmsWoundedDispersionMultiplier = 1.35f;
	[SerializeField, Min(0.01f)] private float m_ArmsWoundedAimTimeMultiplier = 1.25f;
	[SerializeField, Min(0.01f)] private float m_LegsWoundedMovingAimTimeMultiplier = 1.2f;
	[SerializeField, Min(0.01f)] private float m_HeavyPainDispersionMultiplier = 1.2f;
	[SerializeField, Min(0.01f)] private float m_HeavyPainAimTimeMultiplier = 1.15f;
	[SerializeField, Min(0.01f)] private float m_SuppressedDispersionMultiplier = 1.15f;
	[SerializeField, Min(0.01f)] private float m_SuppressedAimTimeMultiplier = 1.2f;

	[Header("Recoil Penalties")]
	[SerializeField, Min(0.01f)] private float m_ArmsWoundedRecoilAddedMultiplier = 1.25f;
	[SerializeField, Min(0.01f)] private float m_ArmsWoundedRecoilRecoveryMultiplier = 0.85f;
	[SerializeField, Min(0.01f)] private float m_HeavyPainRecoilAddedMultiplier = 1.1f;
	[SerializeField, Min(0.01f)] private float m_HeavyPainRecoilRecoveryMultiplier = 0.9f;

	[Header("Ready Stamina Exhaustion")]
	[SerializeField, Min(0.01f)] private float m_ReadyStaminaDispersionMultiplier = 1.2f;
	[SerializeField, Min(0.01f)] private float m_ReadyStaminaRecoilMultiplier = 1.5f;
	#endregion

	#region Public Properties
	public bool ArmsWounded => m_ArmsWounded;
	public bool LegsWounded => m_LegsWounded;
	public bool HeavyPain => m_HeavyPain;
	public bool Suppressed => m_Suppressed;
	#endregion

	#region Private Fields
	private bool m_IsReadyStaminaExhausted;
	#endregion

	#region Public Methods
	public void SetReadyStaminaExhausted(bool _exhausted)
	{
		m_IsReadyStaminaExhausted = _exhausted;
	}
	public void SetArmsWounded(bool _value)
	{
		m_ArmsWounded = _value;
	}

	public void SetLegsWounded(bool _value)
	{
		m_LegsWounded = _value;
	}

	public void SetHeavyPain(bool _value)
	{
		m_HeavyPain = _value;
	}

	public void SetSuppressed(bool _value)
	{
		m_Suppressed = _value;
	}

	public float GetDispersionMultiplier()
	{
		float multiplier = 1f;
		if (m_ArmsWounded)
			multiplier *= m_ArmsWoundedDispersionMultiplier;
		if (m_HeavyPain)
			multiplier *= m_HeavyPainDispersionMultiplier;
		if (m_Suppressed)
			multiplier *= m_SuppressedDispersionMultiplier;
		if (m_IsReadyStaminaExhausted)
			multiplier *= m_ReadyStaminaDispersionMultiplier;
		return Mathf.Max(0.01f, multiplier);
	}

	public float GetAimTimeMultiplier(bool _isMoving)
	{
		float multiplier = 1f;
		if (m_ArmsWounded)
			multiplier *= m_ArmsWoundedAimTimeMultiplier;
		if (m_LegsWounded && _isMoving)
			multiplier *= m_LegsWoundedMovingAimTimeMultiplier;
		if (m_HeavyPain)
			multiplier *= m_HeavyPainAimTimeMultiplier;
		if (m_Suppressed)
			multiplier *= m_SuppressedAimTimeMultiplier;
		return Mathf.Max(0.01f, multiplier);
	}

	public float GetRecoilAddedMultiplier()
	{
		float multiplier = 1f;
		if (m_ArmsWounded)
			multiplier *= m_ArmsWoundedRecoilAddedMultiplier;
		if (m_HeavyPain)
			multiplier *= m_HeavyPainRecoilAddedMultiplier;
		if (m_IsReadyStaminaExhausted)
			multiplier *= m_ReadyStaminaRecoilMultiplier;
		return Mathf.Max(0.01f, multiplier);
	}

	public float GetRecoilRecoveryMultiplier()
	{
		float multiplier = 1f;
		if (m_ArmsWounded)
			multiplier *= m_ArmsWoundedRecoilRecoveryMultiplier;
		if (m_HeavyPain)
			multiplier *= m_HeavyPainRecoilRecoveryMultiplier;
		return Mathf.Max(0.01f, multiplier);
	}
	#endregion
}
