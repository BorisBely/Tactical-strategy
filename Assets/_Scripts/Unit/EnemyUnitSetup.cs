using System;
using UnityEngine;

/// <summary>
/// Автонастройка врага на префабе: команда, стартовое оружие и базовое состояние ready.
/// Устарел — используйте <see cref="UnitFactionConfigurator"/>.
/// </summary>
[Obsolete("Use UnitFactionConfigurator instead.")]
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class EnemyUnitSetup : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitTeam m_Team;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;

	[Header("Setup")]
	[SerializeField] private UnitTeamId m_TeamId = UnitTeamId.Enemy;
	[SerializeField] private ItemDefinition m_StartingWeapon;
	[SerializeField] private bool m_EquipWeaponOnAwake = true;
	[SerializeField] private bool m_StartReady = false;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		CacheComponents();
	}

	private void Awake()
	{
		ApplySetup();
	}
	#endregion

	#region Public Methods
	[ContextMenu("Apply Enemy Setup")]
	public void ApplySetup()
	{
		CacheComponents();

		if (m_Team != null)
			m_Team.SetTeam(m_TeamId);

		if (m_EquipWeaponOnAwake && m_StartingWeapon != null && m_Equipment != null)
			m_Equipment.TryEquip(m_StartingWeapon);

		if (m_ReadyHands != null)
			m_ReadyHands.SetReadyWanted(m_StartReady, false);
	}
	#endregion

	#region Private Methods
	private void CacheComponents()
	{
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
	}
	#endregion
}
