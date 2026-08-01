using System;
using UnityEngine;

/// <summary>
/// Накопление и восстановление штрафа отдачи для экипированного оружия.
/// Единственный источник истины для Hitscan и визуала.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponRecoilController : MonoBehaviour
{
	#region RecoilVisualState
	public readonly struct RecoilVisualState
	{
		public readonly float VisualPitch;
		public readonly float VisualYaw;
		public readonly float VisualBack;
		public readonly float VisualUp;
		public readonly float SpreadHalfAngle;
		public readonly float Stability01;

		public RecoilVisualState(
			float _visualPitch, float _visualYaw, float _visualBack, float _visualUp,
			float _spreadHalfAngle, float _stability01)
		{
			VisualPitch = _visualPitch;
			VisualYaw = _visualYaw;
			VisualBack = _visualBack;
			VisualUp = _visualUp;
			SpreadHalfAngle = _spreadHalfAngle;
			Stability01 = _stability01;
		}
	}
	#endregion

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
	[Tooltip("Максимальный накопленный штраф отдачи.")]
	[SerializeField, Min(0.1f)] private float m_MaxRecoilPenalty = 30f;
	[Tooltip("Множитель penalty → угол разброса. Должен совпадать с UnitWeaponHitscanShooting.RecoilSpreadScale.")]
	[SerializeField, Min(0f)] private float m_RecoilSpreadScale = 0.15f;
	[Header("Visual Recoil (отдельно от разброса)")]
	[Tooltip("Визуальный подъём ствола на единицу penalty (градусы).")]
	[SerializeField, Min(0f)] private float m_VisualPitchPerPenalty = 0.65f;
	[Tooltip("Визуальный сдвиг назад на единицу penalty (метры).")]
	[SerializeField, Min(0f)] private float m_VisualBackPerPenalty = 0.0035f;
	[Tooltip("Визуальный сдвиг вверх на единицу penalty (метры).")]
	[SerializeField, Min(0f)] private float m_VisualUpPerPenalty = 0.0015f;
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
	public event Action<float> PenaltyDelta;
	public float MaxRecoilPenalty => m_MaxRecoilPenalty;
	public float RecoilPenalty =>
		m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
			? m_WeaponRuntime.TransientState.RecoilPenalty
			: 0f;
	public float RecoveryWhileFiringMultiplier => m_RecoveryWhileFiringMultiplier;
	public bool IsRecoveringWhileFiring =>
		m_FireController != null && m_FireController.IsFiringCommandActive;
	public RecoilVisualState CurrentVisualState { get; private set; }
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
		CurrentVisualState = default;
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

		float recoveryPerSecond = CalculateCurrentRecoveryPerSecond();
		float nextPenalty;
		if (currentPenalty <= 0f)
		{
			nextPenalty = 0f;
			m_DebugLastRecoveryPerSecond = 0f;
		}
		else
		{
			nextPenalty = ClampRecoilPenalty(Mathf.MoveTowards(currentPenalty, 0f, recoveryPerSecond * Time.deltaTime));
			m_DebugLastRecoveryPerSecond = recoveryPerSecond;
		}

		if (!Mathf.Approximately(nextPenalty, currentPenalty))
		{
			m_WeaponRuntime.SetRecoilPenalty(nextPenalty);
			float delta = nextPenalty - currentPenalty;
			if (Mathf.Abs(delta) > 0.0001f)
				PenaltyDelta?.Invoke(delta);
		}

		CurrentVisualState = new RecoilVisualState(
			RecoilPenalty * m_VisualPitchPerPenalty,
			0f,
			RecoilPenalty * m_VisualBackPerPenalty,
			RecoilPenalty * m_VisualUpPerPenalty,
			RecoilPenalty * m_RecoilSpreadScale,
			1f - Mathf.Clamp01(RecoilPenalty / m_MaxRecoilPenalty));
	}
	#endregion

	#region Private Methods
	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float recoilAdded = CalculateRecoilAddedPerShot(_ammoDefinition);
		float oldPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
		float newPenalty = ClampRecoilPenalty(oldPenalty + recoilAdded);
		m_WeaponRuntime.SetRecoilPenalty(newPenalty);
		m_DebugLastRecoilAdded = recoilAdded;
		PenaltyDelta?.Invoke(recoilAdded);
	}

	public float GetCurrentRecoveryPerSecond() => CalculateCurrentRecoveryPerSecond();

	public float ComputeRecoilAddedPerShot(AmmoDefinition _ammoDefinition) =>
		CalculateRecoilAddedPerShot(_ammoDefinition);

	public void ResetRecoilPenalty()
	{
		if (m_WeaponRuntime == null)
			return;

		m_WeaponRuntime.SetRecoilPenalty(0f);
		m_DebugLastRecoilAdded = 0f;
		m_DebugLastRecoveryPerSecond = 0f;
		CurrentVisualState = default;
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
