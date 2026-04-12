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
	[SerializeField] private AmmoContainerRuntimeState m_AmmoContainerState;
	#endregion

	#region Public Properties
	public WeaponRuntimeState WeaponState => m_WeaponState;
	public MagazineRuntimeState MagazineState => m_MagazineState;
	public AmmoContainerRuntimeState AmmoContainerState => m_AmmoContainerState;
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
		m_AmmoContainerState = null;

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

		if (_definition.AmmoDefinition != null)
		{
			m_AmmoContainerState = new AmmoContainerRuntimeState();
			m_AmmoContainerState.Configure(_definition.AmmoDefinition, _definition.InitialAmmoCount);
		}
	}
	#endregion
}

/// <summary>
/// Постоянное состояние коробки или пачки патронов.
/// </summary>
[Serializable]
public sealed class AmmoContainerRuntimeState
{
	#region Serialized Fields
	[SerializeField] private AmmoDefinition m_AmmoDefinition;
	[SerializeField, Min(0)] private int m_CurrentAmmoCount;
	#endregion

	#region Public Properties
	public AmmoDefinition AmmoDefinition => m_AmmoDefinition;
	public int CurrentAmmoCount => m_CurrentAmmoCount;
	public bool HasAmmo => m_AmmoDefinition != null && m_CurrentAmmoCount > 0;
	#endregion

	#region Public Methods
	public void Configure(AmmoDefinition _ammoDefinition, int _ammoCount)
	{
		m_AmmoDefinition = _ammoDefinition;
		m_CurrentAmmoCount = Mathf.Max(0, _ammoCount);
	}

	public bool TryConsumeRound()
	{
		if (!HasAmmo)
			return false;

		m_CurrentAmmoCount = Mathf.Max(0, m_CurrentAmmoCount - 1);
		return true;
	}
	#endregion
}
