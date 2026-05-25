using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class MissionPrepInlineModificationBuilder
{
	#region Public Methods
	public static void ClearAllRows(InventoryPanelView _panel)
	{
		if (!Application.isPlaying || _panel == null || _panel.SlotsContainerTransform == null)
			return;

		Transform container = _panel.SlotsContainerTransform;
		for (int i = container.childCount - 1; i >= 0; i--)
		{
			Transform child = container.GetChild(i);
			if (child == null || child.GetComponent<MissionPrepModificationSlotView>() == null)
				continue;

			child.gameObject.SetActive(false);
			Object.Destroy(child.gameObject);
		}
	}

	public static void RebuildWeaponRows(
		InventoryPanelView _panel,
		MissionPrepLoadoutCoordinator _coordinator,
		InventorySlotView _weaponSlot,
		InventorySlotRuntimeData _weaponData,
		bool _isMainHand,
		int _bagIndex,
		bool _expandEmptySlots,
		IReadOnlyList<ItemModificationSlotDescriptor> _visibleDescriptors)
	{
		if (_panel == null || _weaponSlot == null || _visibleDescriptors == null || _visibleDescriptors.Count == 0)
			return;

		Transform container = _panel.SlotsContainerTransform;
		if (container == null)
			return;

		int insertIndex = _weaponSlot.transform.GetSiblingIndex() + 1;
		for (int i = 0; i < _visibleDescriptors.Count; i++)
		{
			GameObject row = new GameObject($"ModificationSlot_{_isMainHand}_{_bagIndex}_{i}", typeof(RectTransform));
			row.transform.SetParent(container, false);
			row.transform.SetSiblingIndex(insertIndex + i);

			LayoutElement layout = row.AddComponent<LayoutElement>();
			layout.preferredHeight = 30f;
			layout.minHeight = 30f;

			MissionPrepModificationSlotView view = row.AddComponent<MissionPrepModificationSlotView>();
			view.Configure(_coordinator, _visibleDescriptors[i], _weaponData, _isMainHand, _bagIndex);
		}
	}

	public static void RefreshHighlights(InventoryPanelView _panel)
	{
		if (_panel == null)
			return;

		MissionPrepModificationSlotView[] rows =
			_panel.SlotsContainerTransform.GetComponentsInChildren<MissionPrepModificationSlotView>(true);
		for (int i = 0; i < rows.Length; i++)
		{
			if (rows[i] != null)
				rows[i].RefreshHighlight();
		}
	}

	public static void RefreshMainHandSlotHighlights(InventoryPanelView _panel)
	{
		if (_panel == null || _panel.LeadingEquipmentSlotCount <= 0)
			return;

		IReadOnlyList<InventorySlotView> slots = _panel.Slots;
		if (slots.Count == 0 || slots[0] == null)
			return;

		MissionPrepMainHandEquipmentSlotView mainHandSlot = slots[0].GetComponent<MissionPrepMainHandEquipmentSlotView>();
		mainHandSlot?.RefreshHighlight();
	}
	#endregion
}
