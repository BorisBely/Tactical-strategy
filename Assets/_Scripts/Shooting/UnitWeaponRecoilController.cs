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

	[Header("Recovery")]
	[Tooltip("Множитель восстановления отдачи, пока удерживается огонь.")]
	[SerializeField, Min(0f)] private float m_RecoveryWhileFiringMultiplier = 0.45f;
	[Tooltip("Множитель восстановления отдачи, если оружие сейчас не на ready.")]
	[SerializeField, Min(0f)] private float m_RecoveryWhenNotReadyMultiplier = 1.2f;

	[Header("Debug")]
	[SerializeField, Min(0f)] private float m_DebugLastRecoilAdded;
	[SerializeField, Min(0f)] private float m_DebugLastRecoveryPerSecond;
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
		WeaponFireMode fireMode = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.SelectedFireMode
			: WeaponFireMode.SemiAuto;

		float fireModeMultiplier = fireMode switch
		{
			WeaponFireMode.FullAuto => weaponDefinition.AutoRecoilMultiplier,
			_ => weaponDefinition.SemiAutoRecoilMultiplier
		};

		float ammoModifier = _ammoDefinition != null ? _ammoDefinition.RecoilModifier : 1f;
		const float attachmentModifier = 1f;

		return weaponDefinition.RecoilPerShot * fireModeMultiplier * ammoModifier * attachmentModifier;
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

		return Mathf.Max(0f, recoveryPerSecond);
	}
	#endregion
}
