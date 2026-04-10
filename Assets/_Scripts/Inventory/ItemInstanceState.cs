using System;
using UnityEngine;

/// <summary>
/// Постоянное состояние конкретного экземпляра предмета.
/// </summary>
[Serializable]
public sealed class ItemInstanceState
{
	#region Serialized Fields
	[SerializeField] private WeaponRuntimeState m_WeaponState;
	[SerializeField] private MagazineRuntimeState m_MagazineState;
	#endregion

	#region Public Properties
	public WeaponRuntimeState WeaponState => m_WeaponState;
	public MagazineRuntimeState MagazineState => m_MagazineState;
	#endregion

	#region Public Methods
	public static ItemInstanceState CreateForDefinition(ItemDefinition _definition)
	{
		ItemInstanceState itemState = new ItemInstanceState();
		itemState.InitializeFromDefinition(_definition);
		return itemState;
	}

	public void InitializeFromDefinition(ItemDefinition _definition)
	{
		m_WeaponState = null;
		m_MagazineState = null;

		if (_definition == null)
			return;

		if (_definition.WeaponDefinition != null)
		{
			m_WeaponState = new WeaponRuntimeState();
			m_WeaponState.SetWeaponDefinition(_definition.WeaponDefinition);
		}

		if (_definition.MagazineDefinition != null)
		{
			m_MagazineState = new MagazineRuntimeState();
			m_MagazineState.Configure(_definition.MagazineDefinition, null, 0);
		}
	}
	#endregion
}
