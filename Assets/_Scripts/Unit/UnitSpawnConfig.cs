using System;
using UnityEngine;

/// <summary>
/// Стартовый набор предметов при спавне юнита.
/// </summary>
[Serializable]
public sealed class UnitSpawnLoadout
{
	[SerializeField] private ItemDefinition m_MainHandWeapon;
	[SerializeField] private ItemDefinition[] m_BagItems = Array.Empty<ItemDefinition>();

	[Header("Магазины")]
	[SerializeField] private AmmoDefinition m_AmmoForMagazines;
	[Tooltip("-1 = заполнить по вместимости MagazineDefinition.")]
	[SerializeField] private int m_RoundsPerMagazine = -1;
	[Tooltip("Первый совместимый заряженный магазин из сумки вставить в оружие.")]
	[SerializeField] private bool m_LoadMagazineIntoWeapon = true;

	public ItemDefinition MainHandWeapon => m_MainHandWeapon;
	public ItemDefinition[] BagItems => m_BagItems ?? Array.Empty<ItemDefinition>();
	public AmmoDefinition AmmoForMagazines => m_AmmoForMagazines;
	public int RoundsPerMagazine => m_RoundsPerMagazine;
	public bool LoadMagazineIntoWeapon => m_LoadMagazineIntoWeapon;
}

/// <summary>
/// Параметры роли юнита при спавне: команда, инвентарь, ready, отображаемое имя.
/// </summary>
[Serializable]
public sealed class UnitSpawnConfig
{
	[SerializeField] private UnitTeamId m_Team = UnitTeamId.Player;
	[SerializeField] private UnitSpawnLoadout m_Loadout = new UnitSpawnLoadout();
	[SerializeField] private bool m_StartReady;
	[SerializeField] private string m_DisplayName;
	[Tooltip("-1 = без брони. 0 = лёгкая, 1 = тяжёлая.")]
	[SerializeField] private int m_ArmorVisualIndex = MissionPrepUnitArmorVisualController.LightArmorIndex;

	public UnitTeamId Team => m_Team;
	public UnitSpawnLoadout Loadout => m_Loadout;
	public bool StartReady => m_StartReady;
	public string DisplayName => m_DisplayName;
	public int ArmorVisualIndex => m_ArmorVisualIndex;

	public UnitSpawnConfig()
	{
	}

	public UnitSpawnConfig(
		UnitTeamId _team,
		UnitSpawnLoadout _loadout,
		bool _startReady,
		string _displayName = null,
		int _armorVisualIndex = MissionPrepUnitArmorVisualController.LightArmorIndex)
	{
		m_Team = _team;
		m_Loadout = _loadout ?? new UnitSpawnLoadout();
		m_StartReady = _startReady;
		m_DisplayName = _displayName;
		m_ArmorVisualIndex = _armorVisualIndex;
	}
}

/// <summary>
/// Точка спавна на сцене + параметры юнита.
/// </summary>
[Serializable]
public sealed class UnitSceneSpawnEntry
{
	[SerializeField] private Transform m_SpawnPoint;
	[SerializeField] private UnitTeamId m_Team = UnitTeamId.Player;
	[SerializeField] private UnitSpawnLoadout m_Loadout = new UnitSpawnLoadout();
	[SerializeField] private bool m_StartReady;
	[SerializeField] private string m_DisplayName;
	[Tooltip("-1 = без брони. 0 = лёгкая, 1 = тяжёлая.")]
	[SerializeField] private int m_ArmorVisualIndex = MissionPrepUnitArmorVisualController.LightArmorIndex;

	public Transform SpawnPoint => m_SpawnPoint;

	public UnitSpawnConfig ToConfig()
	{
		return new UnitSpawnConfig(m_Team, m_Loadout, m_StartReady, m_DisplayName, m_ArmorVisualIndex);
	}
}
