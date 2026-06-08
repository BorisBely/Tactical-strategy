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
	[SerializeField] private UnitCombatCondition m_CombatCondition;

	[Header("Recovery")]
	[Tooltip("Множитель восстановления отдачи, пока удерживается огонь.")]
	[SerializeField, Min(0f)] private float m_RecoveryWhileFiringMultiplier = 0.45f;
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
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
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
		float nextPenalty = Mathf.MoveTowards(currentPenalty, 0f, recoveryPerSecond * Time.deltaTime);
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
		m_WeaponRuntime.SetRecoilPenalty(currentPenalty + recoilAdded);
		m_DebugLastRecoilAdded = recoilAdded;
	}

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
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetRecoilAddedMultiplier() : 1f;
		m_DebugSkillRecoilAddedMultiplier = skillMultiplier;
		m_DebugConditionRecoilAddedMultiplier = conditionMultiplier;
		return WeaponDefinition.ComputeAddedRecoilPenalty(weaponDefinition, fireMode, _ammoDefinition, attachmentModifier) *
		       skillMultiplier *
		       conditionMultiplier;
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
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetRecoilRecoveryMultiplier() : 1f;
		recoveryPerSecond *= skillMultiplier;
		recoveryPerSecond *= conditionMultiplier;
		m_DebugSkillRecoveryMultiplier = skillMultiplier;
		m_DebugConditionRecoveryMultiplier = conditionMultiplier;

		return Mathf.Max(0f, recoveryPerSecond);
	}
	#endregion
}
