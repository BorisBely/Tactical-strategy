using System;
using UnityEngine;

/// <summary>
/// Стартовый набор предметов при спавне юнита.
/// </summary>
[Serializable]
public sealed class UnitSpawnLoadout
{
	[SerializeField] private ItemDefinition m_MainHandWeapon;
	[SerializeField] private ItemDefinition m_HeadItem;
	[SerializeField] private ItemDefinition m_BackItem;
	[SerializeField] private ItemDefinition[] m_BagItems = Array.Empty<ItemDefinition>();

	[Header("Магазины")]
	[SerializeField] private AmmoDefinition m_AmmoForMagazines;
	[Tooltip("-1 = заполнить по вместимости MagazineDefinition.")]
	[SerializeField] private int m_RoundsPerMagazine = -1;
	[Tooltip("Первый совместимый заряженный магазин из сумки вставить в оружие.")]
	[SerializeField] private bool m_LoadMagazineIntoWeapon = true;

	public ItemDefinition MainHandWeapon => m_MainHandWeapon;
	public ItemDefinition HeadItem => m_HeadItem;
	public ItemDefinition BackItem => m_BackItem;
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
	[Tooltip("0 = пустынный, 1 = городской, 2 = лесной, 3 = джунгли.")]
	[SerializeField, Min(0)] private int m_CamouflageVisualIndex;
	[Tooltip("Вероятность женского пола при спавне (0 = всегда мужчина, 1 = всегда женщина).")]
	[SerializeField, Range(0f, 1f)] private float m_FemaleSpawnChance = UnitCharacterAppearance.DefaultFemaleSpawnChance;

	public UnitTeamId Team => m_Team;
	public UnitSpawnLoadout Loadout => m_Loadout;
	public bool StartReady => m_StartReady;
	public string DisplayName => m_DisplayName;
	public int ArmorVisualIndex => m_ArmorVisualIndex;
	public int CamouflageVisualIndex => m_CamouflageVisualIndex;
	public float FemaleSpawnChance => m_FemaleSpawnChance;

	public UnitSpawnConfig()
	{
	}

	public UnitSpawnConfig(
		UnitTeamId _team,
		UnitSpawnLoadout _loadout,
		bool _startReady,
		string _displayName = null,
		int _armorVisualIndex = MissionPrepUnitArmorVisualController.LightArmorIndex,
		int _camouflageVisualIndex = 0,
		float _femaleSpawnChance = UnitCharacterAppearance.DefaultFemaleSpawnChance)
	{
		m_Team = _team;
		m_Loadout = _loadout ?? new UnitSpawnLoadout();
		m_StartReady = _startReady;
		m_DisplayName = _displayName;
		m_ArmorVisualIndex = _armorVisualIndex;
		m_CamouflageVisualIndex = UnitCamouflagePatternUtility.ClampIndex(_camouflageVisualIndex);
		m_FemaleSpawnChance = Mathf.Clamp01(_femaleSpawnChance);
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
