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
	[SerializeField] private MedkitRuntimeState m_MedkitState;
	[SerializeField] private RocketLauncherRuntimeState m_RocketLauncherState;
	#endregion

	#region Public Properties
	public WeaponRuntimeState WeaponState => m_WeaponState;
	public MagazineRuntimeState MagazineState => m_MagazineState;
	public AmmoContainerRuntimeState AmmoContainerState => m_AmmoContainerState;
	public MedkitRuntimeState MedkitState => m_MedkitState;
	public RocketLauncherRuntimeState RocketLauncherState => m_RocketLauncherState;
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
		m_MedkitState = null;
		m_RocketLauncherState = null;

		if (_definition == null)
			return;

		// Сначала магазин/патроны/аптечка: на одном ItemDefinition не должны одновременно жить WeaponRuntimeState и «магазин как предмет»,
		// иначе Unity упрётся в глубину сериализации (оружие → слот магазина → ItemInstanceState → снова оружие…).
		if (_definition.MagazineDefinition != null)
		{
			m_MagazineState = new MagazineRuntimeState();
			m_MagazineState.Configure(_definition.MagazineDefinition, null, 0);
			return;
		}

		if (_definition.AmmoDefinition != null)
		{
			m_AmmoContainerState = new AmmoContainerRuntimeState();
			m_AmmoContainerState.Configure(_definition.AmmoDefinition, _definition.InitialAmmoCount);
			return;
		}

		if (_definition.MedkitDefinition != null)
		{
			m_MedkitState = new MedkitRuntimeState();
			m_MedkitState.Configure(_definition.MedkitDefinition);
			return;
		}

		if (_definition.IsRocketLauncher)
		{
			m_RocketLauncherState = new RocketLauncherRuntimeState();
			bool startsLoaded = _definition.RocketLauncherType == RocketLauncherType.Disposable ||
			                   _definition.RocketLauncherStartsLoaded;
			ItemDefinition defaultRocket = _definition.RocketLauncherType == RocketLauncherType.Rpg7
				? _definition.RpgRocketItemDefinition
				: null;
			m_RocketLauncherState.Configure(startsLoaded, defaultRocket);
			return;
		}

		if (_definition.WeaponDefinition != null)
		{
			m_WeaponState = new WeaponRuntimeState();
			m_WeaponState.SetWeaponDefinition(_definition.WeaponDefinition);
			WeaponBuiltInMagazineUtility.TryEnsureBuiltInMagazine(
				m_WeaponState,
				_definition.WeaponDefinition.BuiltInMagazineDefaultAmmo);
		}
	}

	/// <summary>
	/// Обёртка для слота магазина без WeaponRuntimeState (вставка в оружие не должна сериализовать вложенное оружие).
	/// </summary>
	internal static ItemInstanceState CreateMagazineSlotOwner(MagazineRuntimeState _magazineRuntimeState)
	{
		ItemInstanceState state = new ItemInstanceState();
		state.m_WeaponState = null;
		state.m_AmmoContainerState = null;
		state.m_MedkitState = null;
		state.m_RocketLauncherState = null;
		state.m_MagazineState = _magazineRuntimeState;
		return state;
	}

	public void EnsureRocketLauncherState(ItemDefinition _definition)
	{
		if (m_RocketLauncherState != null)
			return;

		m_RocketLauncherState = new RocketLauncherRuntimeState();
		bool startsLoaded = _definition != null &&
		                    (_definition.RocketLauncherType == RocketLauncherType.Disposable ||
		                     _definition.RocketLauncherStartsLoaded);
		ItemDefinition defaultRocket = _definition != null &&
		                               _definition.RocketLauncherType == RocketLauncherType.Rpg7
			? _definition.RpgRocketItemDefinition
			: null;
		m_RocketLauncherState.Configure(startsLoaded, defaultRocket);
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
