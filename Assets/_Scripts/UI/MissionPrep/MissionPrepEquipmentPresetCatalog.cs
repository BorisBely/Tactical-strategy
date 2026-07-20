using System;
using UnityEngine;

/// <summary>
/// Список доступных пресетов снаряжения для дропдауна (стандартный, тяжёлый и др.) и стартовый инвентарь каждого.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepEquipmentPresetCatalog : MonoBehaviour
{
	#region Serializable Types
	[Serializable]
	public sealed class PresetEntry
	{
		[SerializeField] private string m_DisplayName = "Standard";
		[SerializeField] private string m_LocalizationKey = "mission_prep.equipment.preset.standard";

		[Header("Стартовая броня")]
		[SerializeField, Min(0)] private int m_DefaultArmorVisualIndex = MissionPrepUnitArmorVisualController.LightArmorIndex;

		[Header("Стартовый инвентарь")]
		[SerializeField] private ItemDefinition m_WeaponItem;
		[SerializeField] private ItemDefinition m_HeadItem;
		[SerializeField] private ItemDefinition m_BackItem;
		[SerializeField] private ItemDefinition[] m_ExtraHeadItemsInBag = Array.Empty<ItemDefinition>();
		[SerializeField] private ItemDefinition m_MagazineItem;
		[SerializeField] private AmmoDefinition m_AmmoForMagazine;
		[Tooltip("Сколько заряженных магазинов положить в сумку (не считая магазин в оружии).")]
		[SerializeField, Min(0)] private int m_SpareLoadedMagazinesInBag;
		[Tooltip("Сколько пустых магазинов положить в сумку для ручной зарядки.")]
		[SerializeField, Min(0)] private int m_SpareEmptyMagazinesInBag;
		[Tooltip("-1 = заполнить магазин по вместимости.")]
		[SerializeField] private int m_RoundsPerMagazine = -1;
		[SerializeField] private bool m_PutLoadedMagazineInWeapon;
		[SerializeField] private ItemDefinition[] m_AmmoBoxItems = Array.Empty<ItemDefinition>();
		[Tooltip("Дополнительные предметы в сумку (гранатомёты, ракеты и т.п.).")]
		[SerializeField] private ItemDefinition[] m_ExtraBagItems = Array.Empty<ItemDefinition>();

		public string DisplayName => m_DisplayName;
		public string LocalizationKey => m_LocalizationKey;
		public int DefaultArmorVisualIndex => m_DefaultArmorVisualIndex;
		public ItemDefinition WeaponItem => m_WeaponItem;
		public ItemDefinition HeadItem => m_HeadItem;
		public ItemDefinition BackItem => m_BackItem;
		public ItemDefinition[] ExtraHeadItemsInBag => m_ExtraHeadItemsInBag;
		public ItemDefinition MagazineItem => m_MagazineItem;
		public AmmoDefinition AmmoForMagazine => m_AmmoForMagazine;
		public int SpareLoadedMagazinesInBag => m_SpareLoadedMagazinesInBag;
		public int SpareEmptyMagazinesInBag => m_SpareEmptyMagazinesInBag;
		public int RoundsPerMagazine => m_RoundsPerMagazine;
		public bool PutLoadedMagazineInWeapon => m_PutLoadedMagazineInWeapon;
		public ItemDefinition[] AmmoBoxItems => m_AmmoBoxItems;
		public ItemDefinition[] ExtraBagItems => m_ExtraBagItems;

		public PresetEntry()
		{
		}

		public PresetEntry(string _displayName, string _localizationKey)
		{
			m_DisplayName = _displayName;
			m_LocalizationKey = _localizationKey;
		}

		public string GetLocalizedLabel()
		{
			if (!string.IsNullOrWhiteSpace(m_LocalizationKey))
				return LocalizationManager.Get(m_LocalizationKey, m_DisplayName);

			return string.IsNullOrWhiteSpace(m_DisplayName) ? "Preset" : m_DisplayName;
		}
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private PresetEntry[] m_Presets =
	{
		new PresetEntry()
	};

	[Header("Дропдаун брони")]
	[SerializeField] private PresetEntry[] m_ArmorOptions =
	{
		new PresetEntry("Light armor", "mission_prep.equipment.armor.light"),
		new PresetEntry("Heavy armor", "mission_prep.equipment.armor.heavy")
	};

	[Header("Дропдаун камуфляжа")]
	[SerializeField] private PresetEntry[] m_CamouflageOptions =
	{
		new PresetEntry("Desert", "mission_prep.equipment.camouflage.desert"),
		new PresetEntry("Urban", "mission_prep.equipment.camouflage.urban"),
		new PresetEntry("Forest", "mission_prep.equipment.camouflage.forest"),
		new PresetEntry("Jungle", "mission_prep.equipment.camouflage.jungle")
	};

	[Header("Ссылки на данные")]
	[SerializeField] private GrenadeThrowData m_GrenadeThrowData;
	#endregion

	#region Public Properties
	public int PresetCount => m_Presets != null ? m_Presets.Length : 0;
	public int ArmorOptionCount => m_ArmorOptions != null ? m_ArmorOptions.Length : 0;
	public int CamouflageOptionCount => m_CamouflageOptions != null ? m_CamouflageOptions.Length : 0;
	public GrenadeThrowData GrenadeThrowData => m_GrenadeThrowData;
	#endregion

	#region Public Methods
	public string GetPresetLabel(int _index)
	{
		if (m_Presets == null || _index < 0 || _index >= m_Presets.Length || m_Presets[_index] == null)
			return string.Empty;

		return m_Presets[_index].GetLocalizedLabel();
	}

	public int ClampPresetIndex(int _index)
	{
		if (PresetCount <= 0)
			return 0;

		return Mathf.Clamp(_index, 0, PresetCount - 1);
	}

	public string GetArmorLabel(int _index)
	{
		if (m_ArmorOptions == null || _index < 0 || _index >= m_ArmorOptions.Length || m_ArmorOptions[_index] == null)
			return string.Empty;

		return m_ArmorOptions[_index].GetLocalizedLabel();
	}

	public int ClampArmorIndex(int _index)
	{
		if (ArmorOptionCount <= 0)
			return 0;

		return Mathf.Clamp(_index, 0, ArmorOptionCount - 1);
	}

	public string GetCamouflageLabel(int _index)
	{
		if (m_CamouflageOptions == null || _index < 0 || _index >= m_CamouflageOptions.Length || m_CamouflageOptions[_index] == null)
			return string.Empty;

		return m_CamouflageOptions[_index].GetLocalizedLabel();
	}

	public int ClampCamouflageIndex(int _index)
	{
		if (CamouflageOptionCount <= 0)
			return 0;

		return Mathf.Clamp(_index, 0, CamouflageOptionCount - 1);
	}

	public PresetEntry GetPresetEntry(int _index)
	{
		if (m_Presets == null || _index < 0 || _index >= m_Presets.Length)
			return null;

		return m_Presets[_index];
	}

	public void ApplyDefaultLoadoutToSnapshot(int _presetIndex, MissionPrepPresetSnapshot _snapshot)
	{
		PresetEntry entry = GetPresetEntry(_presetIndex);
		if (entry == null || _snapshot == null)
			return;

		MissionPrepPresetDefaultLoadoutUtility.ApplyToSnapshot(_snapshot, entry, m_GrenadeThrowData);
	}

	public bool PresetDefinesDefaultInventory(int _presetIndex)
	{
		return MissionPrepPresetDefaultLoadoutUtility.EntryDefinesInventory(GetPresetEntry(_presetIndex), m_GrenadeThrowData);
	}
	#endregion
}
