using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class MissionPrepInlineModificationBuilder
{
	#region Public Methods
	public static void ClearAllRows(InventoryPanelView _panel)
	{
		ClearAllRowsInternal(_panel, _immediateDestroy: false);
	}

	public static void ClearAllRowsImmediate(InventoryPanelView _panel)
	{
		ClearAllRowsInternal(_panel, _immediateDestroy: true);
	}

	public static void ClearRowsFollowingInventorySlot(InventoryPanelView _panel, InventorySlotView _weaponSlot)
	{
		if (!Application.isPlaying || _panel == null || _weaponSlot == null || _panel.SlotsContainerTransform == null)
			return;

		Transform container = _panel.SlotsContainerTransform;
		int weaponSiblingIndex = _weaponSlot.transform.GetSiblingIndex();
		var rowsToRemove = new List<GameObject>(4);

		for (int i = weaponSiblingIndex + 1; i < container.childCount; i++)
		{
			Transform child = container.GetChild(i);
			if (child == null || child.GetComponent<MissionPrepModificationSlotView>() == null)
				break;

			rowsToRemove.Add(child.gameObject);
		}

		DestroyRows(rowsToRemove, _panel, _immediateDestroy: true);
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

		RefreshMainHandSlotHighlights(_panel);
	}

	public static void RefreshMainHandSlotHighlights(InventoryPanelView _panel)
	{
		InventorySlotUiUtility.RefreshMainHandEquipHighlight(_panel);
	}
	#endregion

	#region Private Methods
	private static void ClearAllRowsInternal(InventoryPanelView _panel, bool _immediateDestroy)
	{
		if (!Application.isPlaying || _panel == null)
			return;

		Transform searchRoot = _panel.SlotsContainerTransform != null
			? _panel.SlotsContainerTransform
			: _panel.transform;

		MissionPrepModificationSlotView[] rows =
			searchRoot.GetComponentsInChildren<MissionPrepModificationSlotView>(true);
		if (rows == null || rows.Length == 0)
			return;

		var rowsToRemove = new List<GameObject>(rows.Length);
		for (int i = 0; i < rows.Length; i++)
		{
			if (rows[i] != null)
				rowsToRemove.Add(rows[i].gameObject);
		}

		DestroyRows(rowsToRemove, _panel, _immediateDestroy);
	}

	private static void DestroyRows(List<GameObject> _rows, InventoryPanelView _panel, bool _immediateDestroy)
	{
		if (_rows == null || _rows.Count == 0)
			return;

		Transform panelRoot = _panel != null ? _panel.transform : null;
		if (panelRoot != null)
		{
			if (!_immediateDestroy)
			{
				for (int i = 0; i < _rows.Count; i++)
				{
					GameObject row = _rows[i];
					if (row != null)
						row.SetActive(false);
				}
			}

			EditorSelectionGuard.DestroyRuntimeSpawnedSlotsBatch(_rows, panelRoot);
			return;
		}

		for (int i = 0; i < _rows.Count; i++)
		{
			GameObject row = _rows[i];
			if (row == null)
				continue;

			if (!_immediateDestroy)
				row.SetActive(false);

			Object.Destroy(row);
		}
	}
	#endregion
}
