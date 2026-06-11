using UnityEngine;

/// <summary>
/// Накопление и восстановление штрафа отдачи для экипированного оружия.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponRecoilController : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Runtime оружия, где хранится transient recoil penalty.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[Tooltip("Источник события успешного выстрела.")]
	[SerializeField] private UnitWeaponFireController m_FireController;
	[Tooltip("Проверка ready для логики восстановления отдачи.")]
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitStanceCombatModifiers m_StanceCombatModifiers;

	[Header("Recovery")]
	[Tooltip("Максимальный накопленный штраф отдачи. Ограничивает подъём паттерна и рост разброса при длинной очереди.")]
	[SerializeField, Min(0.1f)] private float m_MaxRecoilPenalty = 30f;
	[Tooltip("Множитель восстановления отдачи, пока удерживается огонь.")]
	[SerializeField, Min(0f)] private float m_RecoveryWhileFiringMultiplier = 0.7f;
	[Tooltip("Множитель восстановления отдачи, если оружие сейчас не на ready.")]
	[SerializeField, Min(0f)] private float m_RecoveryWhenNotReadyMultiplier = 1.2f;

	[Header("Debug")]
	[SerializeField, Min(0f)] private float m_DebugLastRecoilAdded;
	[SerializeField, Min(0f)] private float m_DebugLastRecoveryPerSecond;
	[SerializeField, Min(0.01f)] private float m_DebugSkillRecoilAddedMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugConditionRecoilAddedMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugSkillRecoveryMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugConditionRecoveryMultiplier = 1f;
	#endregion

	#region Public Properties
	public float MaxRecoilPenalty => m_MaxRecoilPenalty;
	public float RecoveryWhileFiringMultiplier => m_RecoveryWhileFiringMultiplier;
	public bool IsRecoveringWhileFiring =>
		m_FireController != null && m_FireController.IsFiringCommandActive;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();
		if (m_IndividualTraits == null)
			m_IndividualTraits = GetComponent<UnitIndividualTraits>();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
		if (m_StanceCombatModifiers == null)
			m_StanceCombatModifiers = GetComponent<UnitStanceCombatModifiers>();
	}

	private void OnEnable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
	}

	private void OnDisable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
	}

	private void Update()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float currentPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
		if (currentPenalty <= 0f)
		{
			m_DebugLastRecoveryPerSecond = 0f;
			return;
		}

		float recoveryPerSecond = CalculateCurrentRecoveryPerSecond();
		float nextPenalty = ClampRecoilPenalty(Mathf.MoveTowards(currentPenalty, 0f, recoveryPerSecond * Time.deltaTime));
		m_WeaponRuntime.SetRecoilPenalty(nextPenalty);
		m_DebugLastRecoveryPerSecond = recoveryPerSecond;
	}
	#endregion

	#region Private Methods
	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float recoilAdded = CalculateRecoilAddedPerShot(_ammoDefinition);
		float currentPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
		m_WeaponRuntime.SetRecoilPenalty(ClampRecoilPenalty(currentPenalty + recoilAdded));
		m_DebugLastRecoilAdded = recoilAdded;
	}

	public float GetCurrentRecoveryPerSecond() => CalculateCurrentRecoveryPerSecond();

	public float ComputeRecoilAddedPerShot(AmmoDefinition _ammoDefinition) =>
		CalculateRecoilAddedPerShot(_ammoDefinition);

	private float CalculateRecoilAddedPerShot(AmmoDefinition _ammoDefinition)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		WeaponFireMode fireMode = m_FireController != null
			? m_FireController.ResolveEffectiveFireMode()
			: WeaponFireMode.SemiAuto;

		float attachmentModifier = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.GetAttachmentRecoilProduct(fireMode)
			: 1f;
		float skillMultiplier = m_CombatStats != null ? m_CombatStats.GetRecoilAddedMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetRecoilAddedMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetRecoilAddedMultiplier() : 1f;
		float postureMultiplier = m_StanceCombatModifiers != null
			? m_StanceCombatModifiers.GetRecoilAddedMultiplier()
			: 1f;
		m_DebugSkillRecoilAddedMultiplier = skillMultiplier * individualMultiplier;
		m_DebugConditionRecoilAddedMultiplier = conditionMultiplier;
		return WeaponDefinition.ComputeAddedRecoilPenalty(weaponDefinition, fireMode, _ammoDefinition, attachmentModifier) *
		       skillMultiplier *
		       individualMultiplier *
		       conditionMultiplier *
		       postureMultiplier;
	}

	private float CalculateCurrentRecoveryPerSecond()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		if (weaponDefinition == null)
			return 0f;

		float recoveryPerSecond = weaponDefinition.RecoilRecoveryPerSecond;

		if (m_FireController != null && m_FireController.IsFiringCommandActive)
			recoveryPerSecond *= m_RecoveryWhileFiringMultiplier;

		if (m_ReadyHands != null && !m_ReadyHands.IsWeaponEquippedAndReady())
			recoveryPerSecond *= m_RecoveryWhenNotReadyMultiplier;

		float skillMultiplier = m_CombatStats != null ? m_CombatStats.GetRecoilRecoveryMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetRecoilRecoveryMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetRecoilRecoveryMultiplier() : 1f;
		recoveryPerSecond *= skillMultiplier;
		recoveryPerSecond *= individualMultiplier;
		recoveryPerSecond *= conditionMultiplier;
		m_DebugSkillRecoveryMultiplier = skillMultiplier * individualMultiplier;
		m_DebugConditionRecoveryMultiplier = conditionMultiplier;

		return Mathf.Max(0f, recoveryPerSecond);
	}

	private float ClampRecoilPenalty(float _value) => Mathf.Clamp(_value, 0f, m_MaxRecoilPenalty);
	#endregion
}
