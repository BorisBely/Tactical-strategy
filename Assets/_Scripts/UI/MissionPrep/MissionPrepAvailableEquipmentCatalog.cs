using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Список предметов для панели «доступное снаряжение» на экране предмиссии.
/// Можно дополнить предметами из пресетов каталога и задать свои записи.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepAvailableEquipmentCatalog : MonoBehaviour
{
	#region Serializable Types
	[System.Serializable]
	public sealed class Entry
	{
		[SerializeField] private ItemDefinition m_Item;
		[SerializeField] private AmmoDefinition m_AmmoForMagazine;
		[Tooltip("-1 = заполнить магазин по вместимости (только для ItemDefinition с MagazineDefinition).")]
		[SerializeField] private int m_RoundsPerMagazine = -1;

		public ItemDefinition Item => m_Item;
		public AmmoDefinition AmmoForMagazine => m_AmmoForMagazine;
		public int RoundsPerMagazine => m_RoundsPerMagazine;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private Entry[] m_Entries = System.Array.Empty<Entry>();

	[Tooltip("Готовый набор ItemDefinition (оружие, магазины, модули). Дубликаты с пресетами и m_Entries пропускаются.")]
	[SerializeField] private MissionPrepAvailableEquipmentItemSet m_ItemSet;

	[Header("Автозаполнение")]
	[Tooltip("Добавить уникальные предметы из стартовых наборов MissionPrepEquipmentPresetCatalog.")]
	[SerializeField] private bool m_IncludeItemsFromPresetCatalog = true;

	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;

	[Header("Заголовок панели")]
	[SerializeField] private TMP_Text m_TitleText;
	[SerializeField] private string m_TitleLocalizationKey = "mission_prep.equipment.available_title";
	[SerializeField] private string m_TitleFallback = "Available equipment";

	[Tooltip("Если Title Text пуст — ищем TMP_Text на объекте AvailableEquipmentPanel в сцене.")]
	[SerializeField] private bool m_AutoResolveTitleText = true;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		TryResolveTitleText();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (!Application.isPlaying)
			TryResolveTitleText();
	}
#endif

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		ApplyTitle();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}
	#endregion

	#region Public Methods
	public void BuildSlotList(List<InventorySlotRuntimeData> _outSlots)
	{
		if (_outSlots == null)
			return;

		_outSlots.Clear();
		var seen = new HashSet<ItemDefinition>();

		if (m_IncludeItemsFromPresetCatalog && m_PresetCatalog != null)
			AppendPresetCatalogItems(_outSlots, seen);

		if (m_Entries != null)
		{
			for (int i = 0; i < m_Entries.Length; i++)
				AppendEntry(_outSlots, seen, m_Entries[i]);
		}

		if (m_ItemSet != null)
			m_ItemSet.AppendUnique(_outSlots, seen);

		SortSlotsForDisplay(_outSlots);
	}

	private static void SortSlotsForDisplay(List<InventorySlotRuntimeData> _slots)
	{
		if (_slots == null || _slots.Count <= 1)
			return;

		_slots.Sort(static (a, b) =>
		{
			int orderA = GetDisplaySortOrder(a.Definition);
			int orderB = GetDisplaySortOrder(b.Definition);
			if (orderA != orderB)
				return orderA.CompareTo(orderB);

			string nameA = a.Definition != null ? a.Definition.name : string.Empty;
			string nameB = b.Definition != null ? b.Definition.name : string.Empty;
			return string.Compare(nameA, nameB, System.StringComparison.Ordinal);
		});
	}

	private static int GetDisplaySortOrder(ItemDefinition _definition)
	{
		if (_definition == null)
			return 99;

		if (_definition.WeaponDefinition != null)
			return 0;
		if (_definition.IsEquipment && _definition.EquipmentKind == EquipmentKind.Helmet)
			return 1;
		if (_definition.WeaponAttachmentDefinition != null)
			return 2;
		if (_definition.MagazineDefinition != null)
			return 3;
		if (_definition.AmmoDefinition != null)
			return 4;
		if (_definition.IsGrenade)
			return 5;
		if (_definition.IsRocketLauncher)
			return 0;
		if (_definition.IsRpgRocketAmmo)
			return 4;

		return 6;
	}
	#endregion

	#region Private Methods
	private void HandleLanguageChanged()
	{
		ApplyTitle();
	}

	private void ApplyTitle()
	{
		if (m_TitleText == null)
			return;

		m_TitleText.text = LocalizationManager.Get(m_TitleLocalizationKey, m_TitleFallback);
	}

	private void TryResolveTitleText()
	{
		if (m_TitleText != null || !m_AutoResolveTitleText)
			return;

		Transform[] children = transform.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i] == null || children[i].name != "AvailableEquipmentPanel")
				continue;

			m_TitleText = children[i].GetComponentInChildren<TMP_Text>(true);
			return;
		}
	}

	private void AppendPresetCatalogItems(List<InventorySlotRuntimeData> _outSlots, HashSet<ItemDefinition> _seen)
	{
		int presetCount = m_PresetCatalog.PresetCount;
		for (int p = 0; p < presetCount; p++)
		{
			MissionPrepEquipmentPresetCatalog.PresetEntry preset = m_PresetCatalog.GetPresetEntry(p);
			if (preset == null)
				continue;

			AppendDefinition(_outSlots, _seen, preset.WeaponItem);
			AppendDefinition(_outSlots, _seen, preset.MagazineItem, preset.AmmoForMagazine, preset.RoundsPerMagazine);

			if (preset.AmmoBoxItems != null)
			{
				for (int i = 0; i < preset.AmmoBoxItems.Length; i++)
					AppendDefinition(_outSlots, _seen, preset.AmmoBoxItems[i]);
			}

			if (preset.ExtraBagItems == null)
				continue;

			for (int i = 0; i < preset.ExtraBagItems.Length; i++)
				AppendDefinition(_outSlots, _seen, preset.ExtraBagItems[i]);
		}
	}

	private static void AppendEntry(List<InventorySlotRuntimeData> _outSlots, HashSet<ItemDefinition> _seen, Entry _entry)
	{
		if (_entry == null)
			return;

		AppendDefinition(_outSlots, _seen, _entry.Item, _entry.AmmoForMagazine, _entry.RoundsPerMagazine);
	}

	internal static void AppendDefinition(
		List<InventorySlotRuntimeData> _outSlots,
		HashSet<ItemDefinition> _seen,
		ItemDefinition _definition,
		AmmoDefinition _ammoForMagazine = null,
		int _roundsPerMagazine = -1)
	{
		if (_definition == null || !_seen.Add(_definition))
			return;

		InventorySlotRuntimeData slot = BuildSlot(_definition, _ammoForMagazine, _roundsPerMagazine);
		if (!slot.IsEmpty)
			_outSlots.Add(slot);
	}

	private static InventorySlotRuntimeData BuildSlot(
		ItemDefinition _definition,
		AmmoDefinition _ammoForMagazine,
		int _roundsPerMagazine)
	{
		InventorySlotRuntimeData slot = InventorySlotRuntimeData.FromDefinition(_definition);

		WeaponDefinition weaponDefinition = _definition.WeaponDefinition;
		if (weaponDefinition != null && weaponDefinition.UsesShellByShellReload)
		{
			AmmoDefinition ammo = _ammoForMagazine ?? weaponDefinition.BuiltInMagazineDefaultAmmo;
			WeaponBuiltInMagazineUtility.TryEnsureBuiltInMagazine(
				slot.InstanceState?.WeaponState,
				ammo,
				_roundsPerMagazine);
			return slot;
		}

		if (_definition.MagazineDefinition == null || _ammoForMagazine == null)
			return slot;

		MagazineRuntimeState magazineState = slot.InstanceState?.MagazineState;
		if (magazineState == null)
			return slot;

		int rounds = _roundsPerMagazine < 0
			? _definition.MagazineDefinition.Capacity
			: Mathf.Clamp(_roundsPerMagazine, 0, _definition.MagazineDefinition.Capacity);

		magazineState.Configure(_definition.MagazineDefinition, _ammoForMagazine, rounds);
		return slot;
	}
	#endregion
}
